using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class OutlineWritingBeta : BaseLangChainService
    {
        private string _outlineContent;
        private string _styleGuide;
        private string _rules;
        private readonly FileReferenceService _fileReferenceService;
        private readonly OutlineCharacterReferenceService _characterReferenceService;
        private static Dictionary<string, OutlineWritingBeta> _instances = new Dictionary<string, OutlineWritingBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int CONVERSATION_HISTORY_LIMIT = 4;  // Keep last 2 exchanges (user + assistant pairs)
        private const int OUTLINE_REFRESH_INTERVAL = 1;    // Always refresh outline context (fresh every message)
        private const int CONVERSATION_TRIM_INTERVAL = 6;  // Only trim conversation every 6 messages
        private int _messagesSinceRefresh = 0;
        private bool _needsRefresh = false;
        private int _currentCursorPosition;
        private string _currentFilePath;
        private AIProvider _currentProvider;
        private string _currentModel;
        private Dictionary<string, string> _frontmatter;
        
        // Track outline structure for change detection
        private int _lastKnownChapterCount = 0;
        private string _lastOutlineStructureHash = string.Empty;

        // Enhanced style guide parsing
        private readonly StyleGuideParser _styleParser = new StyleGuideParser();
        private StyleGuideParser.ParsedStyleGuide _parsedStyle;
        
        // Enhanced rules parsing
        private readonly RulesParser _rulesParser = new RulesParser();
        private RulesParser.ParsedRules _parsedRules;
        
        // Outline parsing for integration with FictionWritingBeta
        private readonly OutlineParser _outlineParser = new OutlineParser();
        private OutlineParser.ParsedOutline _parsedCurrentOutline;

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        // Keywords that indicate full outline development requests
        private static readonly string[] OUTLINE_DEVELOPMENT_KEYWORDS = new[]
        {
            "develop outline", "expand outline", "create outline", "build outline",
            "full outline", "entire outline", "complete outline", "outline structure",
            "new outline", "draft outline", "compose outline", "generate outline",
            "chapter breakdown", "story structure", "plot outline"
        };

        // Rough token estimation (conservative estimate)
        private int EstimateTokenCount(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            // Very rough estimate - using average of 1.3 tokens per word plus a small overhead
            return (int)(text.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length * 1.3) + 4;
        }

        private void LogTokenEstimates()
        {
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            var systemTokens = EstimateTokenCount(systemMessage?.Content ?? string.Empty);
            
            var conversationTokens = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                          .Sum(m => EstimateTokenCount(m.Content));

            System.Diagnostics.Debug.WriteLine($"\n=== OUTLINE BETA TOKEN ESTIMATES ===");
            System.Diagnostics.Debug.WriteLine($"System Message: ~{systemTokens} tokens");
            System.Diagnostics.Debug.WriteLine($"Conversation: ~{conversationTokens} tokens");
            System.Diagnostics.Debug.WriteLine($"Total: ~{systemTokens + conversationTokens} tokens");
            
            if ((systemTokens + conversationTokens) > 180000)
            {
                System.Diagnostics.Debug.WriteLine("WARNING: Approaching 200K token response limit!");
            }
        }

        private OutlineWritingBeta(string apiKey, string model, AIProvider provider, string content, string filePath = null, string libraryPath = null) 
            : base(apiKey, model, provider)
        {
            _currentProvider = provider;
            _currentModel = model;
            _currentFilePath = filePath;
            
            if (!string.IsNullOrEmpty(libraryPath))
            {
                _fileReferenceService = new FileReferenceService(libraryPath);
            }
            else
            {
                _fileReferenceService = new FileReferenceService(Configuration.Instance.LibraryPath);
            }
            
            // Initialize character reference service
            _characterReferenceService = new OutlineCharacterReferenceService(_fileReferenceService);
            
            if (!string.IsNullOrEmpty(filePath))
            {
                _fileReferenceService.SetCurrentFile(filePath);
            }
        }

        public static async Task<OutlineWritingBeta> GetInstance(string apiKey, string model, AIProvider provider, string filePath, string libraryPath)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("API key and model must be provided");
            }

            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new ArgumentNullException(nameof(libraryPath));
            }

            // Default file path to ensure we don't have null keys
            if (string.IsNullOrEmpty(filePath))
            {
                filePath = "default";
            }

            await _semaphore.WaitAsync();
            try
            {
                OutlineWritingBeta instance;
                
                // Always force a new instance to ensure correct library path
                System.Diagnostics.Debug.WriteLine($"Creating new OutlineWritingBeta instance for file: {filePath} with library path: {libraryPath}");
                instance = new OutlineWritingBeta(apiKey, model, provider, null, filePath, libraryPath);
                
                // Update the cache
                _instances[filePath] = instance;
                
                return instance;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetInstance: {ex}");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateContentAndInitialize(string content)
        {
            System.Diagnostics.Debug.WriteLine($"OutlineWritingBeta UpdateContentAndInitialize called with content length: {content?.Length ?? 0}");
            
            // Store recent conversation (preserve more context than before)
            var recentConversation = _memory
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .TakeLast(CONVERSATION_HISTORY_LIMIT)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"Preserving {recentConversation.Count} recent conversation messages");
            
            // Always update outline content (should be fresh every time)
            await UpdateContent(content);
            await LoadReferences();
            
            // Only clear conversation memory for MAJOR structural changes (more restrictive criteria)
            if (_needsRefresh && HasMajorStructuralChanges())
            {
                System.Diagnostics.Debug.WriteLine("Major structural changes detected - clearing conversation memory");
                ClearConversationMemory();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Preserving conversation context while refreshing outline analysis");
                
                // Remove only the system message, preserve conversation
                var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                if (systemMessage != null)
                {
                    _memory.Remove(systemMessage);
                }
                
                // Clear non-system messages to rebuild with preserved conversation
                _memory.RemoveAll(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                
                // Re-add fresh system message
                InitializeSystemMessage();
                
                // Restore conversation at end
                _memory.AddRange(recentConversation);
                System.Diagnostics.Debug.WriteLine($"Restored {recentConversation.Count} conversation messages after refresh");
            }
            
            // Mark that we've refreshed
            _messagesSinceRefresh = 0;
            _needsRefresh = false;
        }

        public void UpdateCursorPosition(int position)
        {
            _currentCursorPosition = position;
        }

        private async Task UpdateContent(string content)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateContent called with content length: {content?.Length ?? 0}");
            
            // Check for null content
            if (string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine("UpdateContent: content is null or empty");
                _outlineContent = string.Empty;
                _frontmatter = new Dictionary<string, string>();
                return;
            }
            
            _outlineContent = content;
            
            // Parse the outline content for structured analysis
            try
            {
                if (!string.IsNullOrEmpty(content))
                {
                    _parsedCurrentOutline = _outlineParser.Parse(content);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed current outline:");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedCurrentOutline.Chapters.Count} chapters");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedCurrentOutline.Characters.Count} characters");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedCurrentOutline.Sections.Count} sections");
                    
                    // Check for significant structural changes
                    if (HasSignificantStructuralChanges())
                    {
                        System.Diagnostics.Debug.WriteLine("Significant outline structural changes detected - forcing refresh");
                        _needsRefresh = true;
                        _messagesSinceRefresh = 0; // Reset counter to force immediate refresh
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing outline content: {ex.Message}");
                _parsedCurrentOutline = null;
            }
            
            // Process frontmatter if present
            bool hasFrontmatter = false;
            _frontmatter = new Dictionary<string, string>();
            
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                System.Diagnostics.Debug.WriteLine("Found frontmatter delimiter at start of content");
                // Find the closing delimiter
                int endIndex = -1;
                
                // Skip the first line (opening delimiter)
                int startIndex = content.IndexOf('\n') + 1;
                if (startIndex < content.Length)
                {
                    // Look for closing delimiter
                    endIndex = content.IndexOf("\n---", startIndex);
                    if (endIndex > startIndex)
                    {
                        // Extract frontmatter content
                        string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
                        
                        System.Diagnostics.Debug.WriteLine($"Extracted frontmatter content: {frontmatterContent}");
                        
                        // Parse frontmatter
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        System.Diagnostics.Debug.WriteLine($"Frontmatter contains {lines.Length} lines");
                        
                        foreach (string line in lines)
                        {
                            // Look for key-value pairs (key: value)
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                string key = line.Substring(0, colonIndex).Trim();
                                string value = line.Substring(colonIndex + 1).Trim();
                                
                                // Store in dictionary - remove hashtag if present
                                if (key.StartsWith("#"))
                                {
                                    key = key.Substring(1);
                                }
                                
                                _frontmatter[key] = value;
                                System.Diagnostics.Debug.WriteLine($"Found frontmatter key-value: '{key}' = '{value}'");
                            }
                            else if (line.StartsWith("#"))
                            {
                                // Handle tags (like #outline) - remove hashtag
                                string tag = line.Trim().Substring(1);
                                _frontmatter[tag] = "true";
                                System.Diagnostics.Debug.WriteLine($"Found frontmatter tag: '{tag}'");
                            }
                        }
                        
                        // Skip past the closing delimiter
                        int contentStartIndex = endIndex + 4; // Length of "\n---"
                        if (contentStartIndex < content.Length)
                        {
                            // If there's a newline after the closing delimiter, skip it too
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            // Update _outlineContent to only contain the content after frontmatter
                            _outlineContent = content.Substring(contentStartIndex);
                        }
                        
                        // Set flag indicating we found and processed frontmatter
                        hasFrontmatter = true;
                    }
                }
            }
            
            // Check for references in frontmatter and process them
            if (hasFrontmatter)
            {
                System.Diagnostics.Debug.WriteLine("Processing frontmatter references");
                
                // Define possible variations of keys to check
                string[] styleKeyVariations = new[] { "style", "ref style", "style-file", "style-ref", "stylefile", "styleref", "style_file", "style_ref" };
                string[] rulesKeyVariations = new[] { "rules", "ref rules", "rules-file", "rules-ref", "rulesfile", "rulesref", "rules_file", "rules_ref" };
                
                // Process style reference - try all variations
                string styleRef = null;
                foreach (var keyVariation in styleKeyVariations)
                {
                    if (_frontmatter.TryGetValue(keyVariation, out styleRef) && !string.IsNullOrEmpty(styleRef))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found style reference using key '{keyVariation}': '{styleRef}'");
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(styleRef))
                {
                    await ProcessStyleReference(styleRef);
                }
                
                // Process rules reference - try all variations
                string rulesRef = null;
                foreach (var keyVariation in rulesKeyVariations)
                {
                    if (_frontmatter.TryGetValue(keyVariation, out rulesRef) && !string.IsNullOrEmpty(rulesRef))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found rules reference using key '{keyVariation}': '{rulesRef}'");
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(rulesRef))
                {
                    await ProcessRulesReference(rulesRef);
                }
                
                // Process character references
                await _characterReferenceService.ProcessCharacterReferences(_frontmatter, _currentFilePath);
                System.Diagnostics.Debug.WriteLine($"Character reference processing complete: {_characterReferenceService.GetCharacterSummary()}");
            }
        }

        private async Task ProcessStyleReference(string styleRef)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Processing style reference: {styleRef}");
                var content = await _fileReferenceService.GetFileContent(styleRef);
                if (!string.IsNullOrEmpty(content))
                {
                    // Strip frontmatter to avoid including metadata in the prompt
                    _styleGuide = StripFrontmatter(content);
                    
                    // Parse the style guide
                    _parsedStyle = _styleParser.Parse(_styleGuide);
                    System.Diagnostics.Debug.WriteLine($"Style guide loaded successfully. Length: {_styleGuide.Length} (frontmatter stripped)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Style guide file not found or empty: {styleRef}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading style reference: {ex.Message}");
            }
        }

        private async Task ProcessRulesReference(string rulesRef)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Processing rules reference: {rulesRef}");
                var content = await _fileReferenceService.GetFileContent(rulesRef);
                if (!string.IsNullOrEmpty(content))
                {
                    // Strip frontmatter to avoid including metadata in the prompt
                    _rules = StripFrontmatter(content);
                    
                    // Parse the rules
                    _parsedRules = _rulesParser.Parse(_rules);
                    System.Diagnostics.Debug.WriteLine($"Rules loaded successfully. Length: {_rules.Length} (frontmatter stripped)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Rules file not found or empty: {rulesRef}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading rules reference: {ex.Message}");
            }
        }

        private async Task LoadReferences()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Loading outline references directly...");
                
                // The FileReferenceService doesn't have the methods we're trying to call
                // This method was designed to work with a different interface
                // For now, we'll skip this and rely on frontmatter processing
                System.Diagnostics.Debug.WriteLine("Skipping automatic reference loading - using frontmatter processing instead");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading references directly: {ex.Message}");
            }
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = BuildOutlinePrompt("");
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                systemMessage.Content = systemPrompt;
            }
            else
            {
                _memory.Insert(0, new MemoryMessage("system", systemPrompt, _model));
            }
            
            // Log token estimates after system message initialization
            LogTokenEstimates();
        }

        private string BuildOutlinePrompt(string request)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are an AI assistant specialized in helping users develop and refine story outlines. You will analyze and respond based on the sections provided below.");
            
            // Add current date and time context for temporal awareness
            prompt.AppendLine("");
            prompt.AppendLine("=== CURRENT DATE AND TIME ===");
            prompt.AppendLine($"Current Date/Time: {DateTime.Now:F}");
            prompt.AppendLine($"Local Time Zone: {TimeZoneInfo.Local.DisplayName}");
            prompt.AppendLine("");
            
            // Always use raw style guide - avoid parsing issues that lose context
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine("\n=== STYLE GUIDE ===");
                prompt.AppendLine("Use this style guide to inform the tone and approach of your outline suggestions:");
                prompt.AppendLine(_styleGuide);
            }

            // Always use raw rules content - avoid parsing issues that strip character names
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== CORE RULES ===");
                prompt.AppendLine("These are the fundamental rules and facts for this story universe:");
                prompt.AppendLine(_rules);
            }

            // Add character profiles if available
            var characterProfilesSection = _characterReferenceService.BuildCharacterProfilesSection();
            if (!string.IsNullOrEmpty(characterProfilesSection))
            {
                prompt.AppendLine(characterProfilesSection);
            }

            // Add outline-specific guidance
            prompt.AppendLine("\n=== OUTLINE DEVELOPMENT GUIDANCE ===");
            prompt.AppendLine("Your role is to help develop comprehensive story outlines with the following structure:");
            prompt.AppendLine("");
            prompt.AppendLine("# Overall Synopsis");
            prompt.AppendLine("- Brief 2-3 sentence summary of the entire story");
            prompt.AppendLine("");
            prompt.AppendLine("# Notes");
            prompt.AppendLine("- Core themes and their development throughout the story");
            prompt.AppendLine("- Tone and mood considerations for different chapters");
            prompt.AppendLine("- Special narrative techniques or structural elements");
            prompt.AppendLine("- Important symbols, motifs, or recurring elements");
            prompt.AppendLine("- Genre-specific considerations or tropes to utilize/subvert");
            prompt.AppendLine("");
            prompt.AppendLine("# Characters");
            prompt.AppendLine("## Protagonists");
            prompt.AppendLine("- Character Name - Brief description of role and personality");
            prompt.AppendLine("- Another Character - Description");
            prompt.AppendLine("## Antagonists");
            prompt.AppendLine("- Villain Name - Brief description of role and motivations");
            prompt.AppendLine("## Supporting Characters");
            prompt.AppendLine("- Support Character - Brief description");
            prompt.AppendLine("");
            prompt.AppendLine("NOTE: Character sections support both header format (## Protagonists) and bullet format (- Protagonists)");
            prompt.AppendLine("");
            prompt.AppendLine("# Outline");
            prompt.AppendLine("## Chapter 1");
            prompt.AppendLine("Brief summary of what happens in this chapter (3-5 sentences)");
            prompt.AppendLine("- Major story beat or event");
            prompt.AppendLine("  - Specific action or revelation");
            prompt.AppendLine("  - Character consequence or reaction");
            prompt.AppendLine("- Secondary story beat");
            prompt.AppendLine("  - How this develops or complicates");
            prompt.AppendLine("  - Connection to overall plot");
            prompt.AppendLine("- Chapter conclusion beat");
            prompt.AppendLine("  - How chapter ends");
            prompt.AppendLine("  - Setup for next chapter");
            prompt.AppendLine("");
            prompt.AppendLine("## Chapter 2");
            prompt.AppendLine("Brief summary of what happens in this chapter (3-5 sentences)");
            prompt.AppendLine("- Major story beat or event");
            prompt.AppendLine("  - Specific details");
            prompt.AppendLine("  - Character impact");
            prompt.AppendLine("");
            prompt.AppendLine("(Continue numerically - DO NOT name chapters, only number them)");

            // Add title and author information if available
            if (_frontmatter != null)
            {
                prompt.AppendLine("\n=== DOCUMENT METADATA ===");
                
                if (_frontmatter.TryGetValue("title", out string title) && !string.IsNullOrEmpty(title))
                {
                    prompt.AppendLine($"Title: {title}");
                }
                
                if (_frontmatter.TryGetValue("author", out string author) && !string.IsNullOrEmpty(author))
                {
                    prompt.AppendLine($"Author: {author}");
                }
                
                if (_frontmatter.TryGetValue("genre", out string genre) && !string.IsNullOrEmpty(genre))
                {
                    prompt.AppendLine($"Genre: {genre}");
                }
                
                if (_frontmatter.TryGetValue("series", out string series) && !string.IsNullOrEmpty(series))
                {
                    prompt.AppendLine($"Series: {series}");
                }
            }



            // Add current outline content
            if (!string.IsNullOrEmpty(_outlineContent))
            {
                prompt.AppendLine("\n=== CURRENT OUTLINE CONTENT ===");
                prompt.AppendLine("This is the current outline being developed. Your suggestions should build upon and improve this content:");
                prompt.AppendLine(_outlineContent);
            }

            // Add response format instructions
            prompt.AppendLine("");
            prompt.AppendLine("=== RESPONSE FORMAT ===");
            prompt.AppendLine("When suggesting specific text changes, you MUST use EXACTLY this format:");
            prompt.AppendLine("");
            prompt.AppendLine("For REVISIONS (replacing existing text):");
            prompt.AppendLine("```");
            prompt.AppendLine("Original text:");
            prompt.AppendLine("[paste the exact text to be replaced]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("```");
            prompt.AppendLine("Changed to:");
            prompt.AppendLine("[your new version of the text]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("For INSERTIONS (adding new text after existing text):");
            prompt.AppendLine("```");
            prompt.AppendLine("Insert after:");
            prompt.AppendLine("[paste the exact anchor text to insert after]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("```");
            prompt.AppendLine("New text:");
            prompt.AppendLine("[the new content to insert]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL TEXT PRECISION REQUIREMENTS:");
            prompt.AppendLine("- Original text and Insert after must be EXACT, COMPLETE, and VERBATIM from the outline");
            prompt.AppendLine("- Include ALL whitespace, line breaks, and formatting exactly as written");
            prompt.AppendLine("- Include complete sentences or natural text boundaries (periods, paragraph breaks)");
            prompt.AppendLine("- NEVER paraphrase, summarize, or reformat the original text");
            prompt.AppendLine("- COPY AND PASTE directly from the outline - do not retype or modify");
            prompt.AppendLine("- Include sufficient context (minimum 10-20 words) for unique identification");
            prompt.AppendLine("- If bullet points, include the bullet symbols and indentation exactly");
            prompt.AppendLine("- If headers, include the ## markdown symbols exactly");
            prompt.AppendLine("");
            prompt.AppendLine("TEXT MATCHING VALIDATION:");
            prompt.AppendLine("- Your original text MUST be findable with exact text search in the outline");
            prompt.AppendLine("- If you cannot copy exact text, provide surrounding context for identification");
            prompt.AppendLine("- Test your original text by mentally searching for it in the outline");
            prompt.AppendLine("- Incomplete or modified text will cause Apply buttons to fail");
            prompt.AppendLine("");
            prompt.AppendLine("ANCHOR TEXT GUIDELINES FOR INSERTIONS:");
            prompt.AppendLine("- Include COMPLETE sections, headers, or bullet points that end BEFORE where you want to insert");
            prompt.AppendLine("- NEVER use partial sentences or incomplete phrases as anchor text");
            prompt.AppendLine("- ALWAYS end anchor text at natural boundaries: section endings, headers, paragraph breaks");
            prompt.AppendLine("- Include enough context (at least 10-20 words) to ensure unique identification");
            prompt.AppendLine("");
            prompt.AppendLine("GRANULAR REVISION GUIDELINES:");
            prompt.AppendLine("- For SMALL CHANGES: Use the 'Original text/Changed to' format with minimal scope");
            prompt.AppendLine("- For TARGETED ADDITIONS: Use the 'Insert after/New text' format to add content at specific locations");
            prompt.AppendLine("- For ENHANCEMENTS: Modify only the specific bullet points or paragraphs that need updating");
            prompt.AppendLine("- AVOID rewriting entire chapters unless specifically requested");
            prompt.AppendLine("- PRESERVE formatting, indentation, and structure of unchanged content");
            prompt.AppendLine("");
            prompt.AppendLine("**REVISION FOCUS**: When providing outline revisions, be concise:");
            prompt.AppendLine("- Provide the revision blocks with minimal explanation");
            prompt.AppendLine("- Only add commentary if the user specifically asks for reasoning or analysis");
            prompt.AppendLine("- Focus on the structural changes themselves, not lengthy explanations");
            prompt.AppendLine("- If multiple revisions are needed, provide them cleanly in sequence");
            prompt.AppendLine("- Let the revised outline content speak for itself");
            prompt.AppendLine("");
            prompt.AppendLine("If you are generating new content (e.g., creating new major sections, adding entire chapters), simply provide the new text directly without the revision or insertion format.");
            prompt.AppendLine("");
            prompt.AppendLine("=== ORIGINALITY & PLOT PLAGIARISM AVOIDANCE ===");
            prompt.AppendLine("CRITICAL: Create entirely original story outlines that avoid plagiarizing from EXTERNAL published works:");
            prompt.AppendLine("- Develop unique plot structures and story beats that don't copy existing books, movies, or media");
            prompt.AppendLine("- Avoid replicating specific plot sequences, character arcs, or story progressions from published works");
            prompt.AppendLine("- Create original conflicts, revelations, and dramatic moments");
            prompt.AppendLine("- Develop fresh approaches to genre conventions and narrative structure");
            prompt.AppendLine("- Ensure chapter progressions and story beats are your own creative construction");
            prompt.AppendLine("- Draw inspiration from general themes and structures without copying specific plot elements");
            prompt.AppendLine("");
            prompt.AppendLine("IMPORTANT: ALWAYS use your PROJECT REFERENCE MATERIALS for consistency:");
            prompt.AppendLine("- Character Profiles: Use established character traits, relationships, and development arcs");
            prompt.AppendLine("- Rules: Follow universe constraints, world-building elements, and established facts");
            prompt.AppendLine("- Style Guide: Match the intended tone, themes, and narrative approach");
            prompt.AppendLine("- Existing Outline: Build upon and expand current story structure when refining");
            prompt.AppendLine("- Reference materials are for CONSISTENCY and CONTINUITY, not content to avoid");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL INSTRUCTIONS:");
            prompt.AppendLine("1. When developing outlines, focus on:");
            prompt.AppendLine("   - Clear plot progression from chapter to chapter");
            prompt.AppendLine("   - Character development arcs");
            prompt.AppendLine("   - Thematic consistency and development throughout the story");
            prompt.AppendLine("   - Proper story structure (setup, rising action, climax, resolution)");
            prompt.AppendLine("   - STRUCTURAL FOUNDATION: Outlines should provide clear plot progression for story development");
            prompt.AppendLine("   - CONCISE FOCUS: Focus on essential story beats, not detailed prose or dialogue");
            prompt.AppendLine("   - THEME INTEGRATION: Ensure themes are clearly articulated in Notes section");
            prompt.AppendLine("");
            prompt.AppendLine("2. For chapter outlines (STRUCTURED NARRATIVE BEATS):");
            prompt.AppendLine("   - Use EXACT format: \"## Chapter [number]\" (e.g., \"## Chapter 1\", \"## Chapter 2\")");
            prompt.AppendLine("   - NEVER create custom chapter names or titles");
            prompt.AppendLine("   - Include a comprehensive summary paragraph (3-5 sentences)");
            prompt.AppendLine("   - Follow with major narrative beats as main bullets (\"-\")");
            prompt.AppendLine("   - Each main beat can have UP TO 2 sub-bullets (\"  -\") for details");
            prompt.AppendLine("   - Maximum 8-10 main beats per chapter to prevent ballooning");
            prompt.AppendLine("   - Focus on WHAT HAPPENS, not how it's written or dialogue content");
            prompt.AppendLine("   - Include major plot events, character actions, and story progression");
            prompt.AppendLine("   - Avoid dialogue, internal thoughts, or prose-level descriptions");
            prompt.AppendLine("   - Keep main bullets concise (1-2 sentences), sub-bullets brief");
            prompt.AppendLine("   - Ensure each chapter advances the plot meaningfully");
            prompt.AppendLine("");
            prompt.AppendLine("3. Chapter Content Guidelines (STRUCTURAL EVENTS ONLY):");
            prompt.AppendLine("   - Major plot events: What significant things happen?");
            prompt.AppendLine("   - Character actions: What do characters DO (not think or feel)?");
            prompt.AppendLine("   - Story progression: How does the plot move forward?");
            prompt.AppendLine("   - Key reveals or discoveries: What information is revealed?");
            prompt.AppendLine("   - Conflicts or obstacles: What problems arise?");
            prompt.AppendLine("   - Setting changes: Where does the action take place?");
            prompt.AppendLine("   - Chapter conclusion: How does the chapter end?");
            prompt.AppendLine("");
            prompt.AppendLine("4. Narrative Beat Structure:");
            prompt.AppendLine("   - Main bullets (\"-\"): Major story beats, plot events, or scene transitions");
            prompt.AppendLine("   - Sub-bullets (\"  -\"): Specific details, character actions, or consequences");
            prompt.AppendLine("   - Use sub-bullets to break down complex beats into clear components");
            prompt.AppendLine("   - AVOID dialogue unless user specifically provides it");
            prompt.AppendLine("   - AVOID character thoughts, emotions, or internal monologue");
            prompt.AppendLine("   - AVOID detailed descriptions of settings or atmosphere");
            prompt.AppendLine("   - FOCUS on plot structure and essential story progression");
            prompt.AppendLine("   - Maximum 8-10 main beats per chapter (each with up to 2 sub-bullets)");
            prompt.AppendLine("");
            prompt.AppendLine("5. Character formatting MUST follow this pattern:");
            prompt.AppendLine("   - Use \"- Protagonists\", \"- Antagonists\", \"- Supporting Characters\" as section headers");
            prompt.AppendLine("   - Format each character as: \"  - Character Name - Brief role description\"");
            prompt.AppendLine("   - Use consistent bullet formatting throughout");
            prompt.AppendLine("   - Keep character descriptions concise (1-2 sentences maximum)");
            prompt.AppendLine("   - Focus on character role in the story, not detailed psychology");
            prompt.AppendLine("");
            prompt.AppendLine("6. Notes Section - Themes and Story Elements:");
            prompt.AppendLine("   - ALWAYS include a robust Notes section with core themes");
            prompt.AppendLine("   - Identify 2-4 major themes and how they develop through the story");
            prompt.AppendLine("   - Note tone shifts between chapters (dark, hopeful, tense, etc.)");
            prompt.AppendLine("   - Include important symbols, motifs, or recurring elements");
            prompt.AppendLine("   - Mention genre-specific elements or tropes being used/subverted");
            prompt.AppendLine("   - Keep theme descriptions concise but meaningful");
            prompt.AppendLine("");
            prompt.AppendLine("7. For parser compatibility:");
            prompt.AppendLine("   - Use \"-\" for all bullet points (not \"•\" or other symbols)");
            prompt.AppendLine("   - Maintain consistent indentation (2 spaces for sub-bullets)");
            prompt.AppendLine("   - Include action keywords in chapter descriptions (discovers, reveals, confronts, etc.)");
            prompt.AppendLine("   - Keep character names consistent throughout the outline");
            prompt.AppendLine("");
            prompt.AppendLine("8. When expanding or refining existing outline content:");
            prompt.AppendLine("   - Maintain the established structure and formatting");
            prompt.AppendLine("   - Build upon existing character and plot elements");
            prompt.AppendLine("   - ADD key story events without excessive detail");
            prompt.AppendLine("   - PRESERVE the exact heading structure for optimal parsing");
            prompt.AppendLine("   - FOCUS on structural clarity rather than prose details");
            prompt.AppendLine("");
            prompt.AppendLine("9. GRANULAR REVISION APPROACH:");
            prompt.AppendLine("   - PREFER TARGETED CHANGES: When a user requests specific modifications, make only the changes needed");
            prompt.AppendLine("   - PRESERVE EXISTING CONTENT: Keep all good content that doesn't conflict with the request");
            prompt.AppendLine("   - INCREMENTAL IMPROVEMENTS: Add or modify individual bullet points, scenes, or character details rather than rewriting entire chapters");
            prompt.AppendLine("   - SURGICAL EDITS: Replace only the specific sections that need updating");
            prompt.AppendLine("   - CONTEXTUAL ADDITIONS: When adding new elements, integrate them smoothly with existing content");
            prompt.AppendLine("");
            prompt.AppendLine("10. Granular Change Guidelines:");
            prompt.AppendLine("   - For character additions: Add only the new character to the Characters section, keep existing ones unchanged");
            prompt.AppendLine("   - For plot adjustments: Modify only affected chapter sections, not entire chapters");
            prompt.AppendLine("   - For scene modifications: Change only the specific scenes mentioned, preserve others");
            prompt.AppendLine("   - For detail enhancement: Add bullet points or expand existing ones rather than rewriting");
            prompt.AppendLine("   - For continuity fixes: Make minimal necessary adjustments to maintain story flow");
            prompt.AppendLine("");
            prompt.AppendLine("11. Always consider:");
            prompt.AppendLine("   - How the outline serves the overall story arc");
            prompt.AppendLine("   - Major character actions and plot progression");
            prompt.AppendLine("   - Pacing and tension management across all chapters");
            prompt.AppendLine("   - Integration with any established series or universe rules");
            prompt.AppendLine("   - STRUCTURAL CLARITY: Are the key story beats clearly defined?");
            prompt.AppendLine("   - PLOT PROGRESSION: Does each chapter move the story forward meaningfully?");
            prompt.AppendLine("   - CONCISENESS: Is the outline focused on essential events only?");
            prompt.AppendLine("");
            prompt.AppendLine("IMPORTANT: This outline provides the structural foundation for story development.");
            prompt.AppendLine("Focus on clear plot progression and key events rather than detailed content.");
            prompt.AppendLine("Keep outlines concise to prevent ballooning - detailed prose will be handled later.");
            prompt.AppendLine("");
            prompt.AppendLine("=== CLARIFYING QUESTIONS (USE SPARINGLY) ===");
            prompt.AppendLine("When a user request is genuinely ambiguous or lacks essential context, you MAY ask clarifying questions.");
            prompt.AppendLine("Use this capability SPARINGLY - only when truly necessary for quality outline development.");
            prompt.AppendLine("");
            prompt.AppendLine("WHEN TO ASK QUESTIONS:");
            prompt.AppendLine("- Request is vague about story direction (\"make it better\" without specifics)");
            prompt.AppendLine("- Conflicting elements that need clarification (timeline issues, character motivations)");
            prompt.AppendLine("- Missing critical context for major plot decisions");
            prompt.AppendLine("- Unclear genre expectations or target audience implications");
            prompt.AppendLine("- Ambiguous character relationship dynamics that affect plot structure");
            prompt.AppendLine("");
            prompt.AppendLine("WHEN NOT TO ASK QUESTIONS:");
            prompt.AppendLine("- Request is clear enough to proceed with reasonable assumptions");
            prompt.AppendLine("- You can make good creative decisions based on existing outline context");
            prompt.AppendLine("- Minor details that don't significantly impact story structure");
            prompt.AppendLine("- Style preferences that can be inferred from existing content");
            prompt.AppendLine("");
            prompt.AppendLine("QUESTION FORMAT:");
            prompt.AppendLine("**CLARIFICATION NEEDED**");
            prompt.AppendLine("I need some additional context to provide the best outline development:");
            prompt.AppendLine("");
            prompt.AppendLine("1. [Specific question about story direction/character motivation/plot structure]");
            prompt.AppendLine("2. [Another focused question if needed - maximum 3 questions]");
            prompt.AppendLine("");
            prompt.AppendLine("Once you provide this context, I'll develop the outline revisions accordingly.");
            prompt.AppendLine("");
            prompt.AppendLine("QUESTION GUIDELINES:");
            prompt.AppendLine("- Ask focused, specific questions that directly impact outline structure");
            prompt.AppendLine("- Limit to 1-3 questions maximum to avoid overwhelming the user");
            prompt.AppendLine("- Frame questions in terms of story development needs");
            prompt.AppendLine("- Avoid questions about minor details or preferences");
            prompt.AppendLine("- Always explain why the clarification is needed for better outline development");

            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"OutlineWritingBeta ProcessRequest called with content length: {content?.Length ?? 0}");
                
                // Check if this request involves chapter operations that might change structure
                var structuralChangeKeywords = new[]
                {
                    "chapter", "renumber", "reorder", "move chapter", "swap chapter", 
                    "delete chapter", "remove chapter", "add chapter", "insert chapter",
                    "chapter number", "chapter order", "reorganize"
                };
                
                bool isStructuralRequest = structuralChangeKeywords.Any(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                // Check if we need to refresh core materials
                _messagesSinceRefresh++;
                
                // Always refresh outline context (OUTLINE_REFRESH_INTERVAL = 1)
                // But only trim conversation based on CONVERSATION_TRIM_INTERVAL
                if (_messagesSinceRefresh >= OUTLINE_REFRESH_INTERVAL || isStructuralRequest)
                {
                    _needsRefresh = true;
                    if (isStructuralRequest)
                    {
                        System.Diagnostics.Debug.WriteLine("Structural change request detected - forcing refresh");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Outline refresh triggered (message #{_messagesSinceRefresh})");
                    }
                }

                // If content has changed or refresh is needed, update
                if (_outlineContent != content || _needsRefresh)
                {
                    System.Diagnostics.Debug.WriteLine($"Content has changed or refresh is needed. _outlineContent length: {_outlineContent?.Length ?? 0}, content length: {content?.Length ?? 0}");
                    await UpdateContentAndInitialize(content);
                }

                // Build the prompt with the most relevant context
                var contextPrompt = BuildContextPrompt(request);
                
                // Add the user request to memory
                AddUserMessage(request);
                
                // Handle conversation trimming based on separate interval
                if (_messagesSinceRefresh % CONVERSATION_TRIM_INTERVAL == 0)
                {
                    TrimOldConversationIfNeeded();
                }
                
                // Log token estimates before making the API call
                LogTokenEstimates();

                // Initialize retry count
                int retryCount = 0;
                const int maxRetries = 5;
                const int baseDelay = 2000; // Base delay of 2 seconds

                while (true)
                {
                    try
                    {
                        // Process the request with the AI
                        var response = await ExecutePrompt(contextPrompt);
                        
                        // Add the response to memory
                        AddAssistantMessage(response);
                        
                        return response;
                    }
                    catch (Exception ex) when (ex.Message.Contains("overloaded_error") && retryCount < maxRetries)
                    {
                        retryCount++;
                        var delay = baseDelay * Math.Pow(2, retryCount - 1); // Exponential backoff
                        
                        // Raise an event or notify UI about retry
                        OnRetryingOverloadedRequest?.Invoke(this, new RetryEventArgs 
                        { 
                            RetryCount = retryCount, 
                            MaxRetries = maxRetries,
                            DelayMs = delay 
                        });
                        
                        System.Diagnostics.Debug.WriteLine($"Anthropic API overloaded. Retry {retryCount}/{maxRetries} after {delay}ms");
                        await Task.Delay((int)delay);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        // For any other error, or if we've exceeded max retries
                        System.Diagnostics.Debug.WriteLine($"\n=== OUTLINE BETA ERROR ===\n{ex}");
                        throw new Exception($"Error after {retryCount} retries: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== OUTLINE BETA ERROR ===\n{ex}");
                throw;
            }
        }

        private string BuildContextPrompt(string request)
        {
            var prompt = new StringBuilder();
            
            // Add previous conversation context if it exists, with clear delineation
            var conversationMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
            if (conversationMessages.Any())
            {
                prompt.AppendLine("=== PREVIOUS CONVERSATION ===");
                
                foreach (var message in conversationMessages)
                {
                    prompt.AppendLine($"[{message.Role.ToUpper()}]: {message.Content}");
                    prompt.AppendLine();
                }
                
                prompt.AppendLine("=== END PREVIOUS CONVERSATION ===");
                prompt.AppendLine();
            }
            
            // Add the current user request with clear header
            prompt.AppendLine("=== CURRENT REQUEST ===");
            prompt.AppendLine(request);
            
            return prompt.ToString();
        }

        /// <summary>
        /// Strips frontmatter from content to include only the main content in prompts
        /// </summary>
        private string StripFrontmatter(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // Check for frontmatter (starts with ---)
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing ---
                int secondDelimiterPos = content.IndexOf("\n---", 3);
                if (secondDelimiterPos == -1)
                {
                    secondDelimiterPos = content.IndexOf("\r\n---", 3);
                }
                
                if (secondDelimiterPos != -1)
                {
                    // Skip past the closing --- and any following newlines
                    int contentStart = secondDelimiterPos + 4; // Skip past "\n---"
                    if (contentStart < content.Length && content[contentStart] == '\n')
                        contentStart++;
                    else if (contentStart < content.Length - 1 && content.Substring(contentStart, 2) == "\r\n")
                        contentStart += 2;
                    
                    if (contentStart < content.Length)
                    {
                        return content.Substring(contentStart).Trim();
                    }
                }
            }

            // No frontmatter found, return original content
            return content;
        }

        /// <summary>
        /// Clears a specific instance from the cache
        /// </summary>
        /// <param name="filePath">The file path of the instance to clear</param>
        public static void ClearInstance(string filePath)
        {
            lock (_lock)
            {
                if (!string.IsNullOrEmpty(filePath) && _instances.ContainsKey(filePath))
                {
                    _instances.Remove(filePath);
                    System.Diagnostics.Debug.WriteLine($"Cleared OutlineWritingBeta instance for file: {filePath}");
                }
            }
        }

        /// <summary>
        /// Clears all instances from the cache
        /// </summary>
        public static void ClearAllInstances()
        {
            lock (_lock)
            {
                _instances.Clear();
                System.Diagnostics.Debug.WriteLine("Cleared all OutlineWritingBeta instances");
            }
        }

        /// <summary>
        /// Gets the number of active instances
        /// </summary>
        public static int GetInstanceCount()
        {
            lock (_lock)
            {
                return _instances.Count;
            }
        }

        /// <summary>
        /// Clears all instances from the cache
        /// </summary>
        public static void ClearInstance()
        {
            lock (_lock)
            {
                // Clear all instances
                _instances.Clear();
                System.Diagnostics.Debug.WriteLine("All OutlineWritingBeta instances cleared");
            }
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;
        
        /// <summary>
        /// Gets the parsed outline structure for integration with other services
        /// </summary>
        public OutlineParser.ParsedOutline GetParsedOutline()
        {
            return _parsedCurrentOutline;
        }
        
        /// <summary>
        /// Validates that the current outline follows the proper format for parser compatibility
        /// </summary>
        public (bool IsValid, List<string> Issues) ValidateOutlineFormat()
        {
            var issues = new List<string>();
            bool isValid = true;
            
            if (string.IsNullOrEmpty(_outlineContent))
            {
                issues.Add("No outline content to validate");
                return (false, issues);
            }
            
            // Check for required sections
            if (!_outlineContent.Contains("# Overall Synopsis"))
            {
                issues.Add("Missing '# Overall Synopsis' section");
                isValid = false;
            }
            
            if (!_outlineContent.Contains("# Characters"))
            {
                issues.Add("Missing '# Characters' section");
                isValid = false;
            }
            
            if (!_outlineContent.Contains("# Outline"))
            {
                issues.Add("Missing '# Outline' section");
                isValid = false;
            }
            
            // Check chapter format
            if (!System.Text.RegularExpressions.Regex.IsMatch(_outlineContent, @"## Chapter \d+"))
            {
                issues.Add("No chapters found with proper format '## Chapter [number]'");
                isValid = false;
            }
            
            // Check for proper character formatting
            if (_outlineContent.Contains("# Characters") && !_outlineContent.Contains("- Protagonists"))
            {
                issues.Add("Characters section should contain '- Protagonists' subsection");
                isValid = false;
            }
            
            // Validate parsing success
            if (_parsedCurrentOutline == null)
            {
                issues.Add("Outline failed to parse properly - check formatting");
                isValid = false;
            }
            else
            {
                if (_parsedCurrentOutline.Chapters.Count == 0)
                {
                    issues.Add("No chapters were parsed - check chapter formatting");
                    isValid = false;
                }
            }
            
            if (isValid)
            {
                issues.Add("Outline format is valid and parser-compatible");
            }
            
            return (isValid, issues);
        }

        /// <summary>
        /// Detects if the outline has undergone significant structural changes that warrant a refresh
        /// </summary>
        private bool HasSignificantStructuralChanges()
        {
            if (_parsedCurrentOutline == null)
                return false;
            
            bool hasChanges = false;
            
            // Check if chapter count has changed
            int currentChapterCount = _parsedCurrentOutline.Chapters.Count;
            if (_lastKnownChapterCount != 0 && _lastKnownChapterCount != currentChapterCount)
            {
                System.Diagnostics.Debug.WriteLine($"Chapter count changed from {_lastKnownChapterCount} to {currentChapterCount}");
                hasChanges = true;
            }
            
            // Generate a hash of the outline structure (chapter numbers and titles)
            var structureElements = _parsedCurrentOutline.Chapters
                .OrderBy(c => c.Number)
                .Select(c => $"{c.Number}:{c.Title ?? "Untitled"}")
                .ToList();
            
            string currentStructureHash = string.Join("|", structureElements);
            
            // Check if the structure hash has changed (indicates chapter reordering, renaming, etc.)
            if (!string.IsNullOrEmpty(_lastOutlineStructureHash) && 
                _lastOutlineStructureHash != currentStructureHash)
            {
                System.Diagnostics.Debug.WriteLine("Outline structure hash changed - chapter ordering or titles modified");
                System.Diagnostics.Debug.WriteLine($"Previous: {_lastOutlineStructureHash}");
                System.Diagnostics.Debug.WriteLine($"Current:  {currentStructureHash}");
                hasChanges = true;
            }
            
            // Update tracking variables using the new method
            _lastKnownChapterCount = currentChapterCount;
            _lastOutlineStructureHash = GetStructuralHash();
            
            return hasChanges;
        }
        
        /// <summary>
        /// Determines if there are major structural changes that warrant clearing conversation memory
        /// More restrictive than the previous version to preserve conversation continuity
        /// </summary>
        private bool HasMajorStructuralChanges()
        {
            if (_parsedCurrentOutline == null)
                return false;
            
            // Only clear for truly major changes:
            // - More than 2 chapters added/removed (was 1, now 2)
            // - Structural hash changes only if we have a previous hash to compare
            var chapterCountChange = Math.Abs(_parsedCurrentOutline.Chapters.Count - _lastKnownChapterCount);
            var hasSignificantReordering = !string.IsNullOrEmpty(_lastOutlineStructureHash) && 
                                          GetStructuralHash() != _lastOutlineStructureHash;
            
            System.Diagnostics.Debug.WriteLine($"Structural change analysis:");
            System.Diagnostics.Debug.WriteLine($"  - Chapter count change: {chapterCountChange} (threshold: 2)");
            System.Diagnostics.Debug.WriteLine($"  - Hash comparison available: {!string.IsNullOrEmpty(_lastOutlineStructureHash)}");
            System.Diagnostics.Debug.WriteLine($"  - Significant reordering: {hasSignificantReordering}");
            
            return chapterCountChange > 2 || hasSignificantReordering;
        }
        
        /// <summary>
        /// Legacy method maintained for compatibility - now calls HasMajorStructuralChanges
        /// </summary>
        private bool ShouldClearMemoryForStructuralChanges()
        {
            return HasMajorStructuralChanges();
        }
        
        /// <summary>
        /// Gets a hash representing the structural organization of the outline
        /// </summary>
        private string GetStructuralHash()
        {
            if (_parsedCurrentOutline?.Chapters == null || !_parsedCurrentOutline.Chapters.Any())
                return string.Empty;
                
            // Create hash based on chapter order and titles
            var structureString = string.Join("|", _parsedCurrentOutline.Chapters
                .OrderBy(c => c.Number)
                .Select(c => $"{c.Number}:{c.Title?.Trim()}"));
                
            return structureString.GetHashCode().ToString();
        }
        
        /// <summary>
        /// Trims old conversation messages while preserving recent context
        /// </summary>
        private void TrimOldConversationIfNeeded()
        {
            var conversationMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (conversationMessages.Count > CONVERSATION_HISTORY_LIMIT)
            {
                var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                var recentMessages = conversationMessages.TakeLast(CONVERSATION_HISTORY_LIMIT).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Trimming conversation: {conversationMessages.Count} -> {recentMessages.Count} messages");
                
                _memory.Clear();
                if (systemMessage != null)
                {
                    _memory.Add(systemMessage);
                }
                _memory.AddRange(recentMessages);
            }
        }
        
        /// <summary>
        /// Clears the conversation memory while preserving the system message
        /// </summary>
        private void ClearConversationMemory()
        {
            // Keep only the system message, remove all conversation history
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            _memory.Clear();
            
            if (systemMessage != null)
            {
                _memory.Add(systemMessage);
            }
            
            System.Diagnostics.Debug.WriteLine("Conversation memory cleared - only system message preserved");
        }
    }
} 