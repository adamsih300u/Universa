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
        private static Dictionary<string, OutlineWritingBeta> _instances = new Dictionary<string, OutlineWritingBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int CONVERSATION_HISTORY_LIMIT = 8;  // Keep last 4 exchanges (user + assistant pairs)
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
            
            if (_currentProvider == AIProvider.Anthropic && (systemTokens + conversationTokens) > 100000)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ WARNING: Approaching Anthropic's token limit!");
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
                    _styleGuide = content;
                    
                    // Parse the style guide
                    _parsedStyle = _styleParser.Parse(content);
                    System.Diagnostics.Debug.WriteLine($"Style guide loaded successfully. Length: {content.Length}");
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
                    _rules = content;
                    
                    // Parse the rules
                    _parsedRules = _rulesParser.Parse(content);
                    System.Diagnostics.Debug.WriteLine($"Rules loaded successfully. Length: {content.Length}");
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

            // Add outline-specific guidance
            prompt.AppendLine("\n=== OUTLINE DEVELOPMENT GUIDANCE ===");
            prompt.AppendLine("Your role is to help develop comprehensive story outlines with the following structure:");
            prompt.AppendLine("");
            prompt.AppendLine("# Overall Synopsis");
            prompt.AppendLine("- Brief 2-3 sentence summary of the entire story");
            prompt.AppendLine("");
            prompt.AppendLine("# Notes");
            prompt.AppendLine("- Important notes about themes, tone, special considerations");
            prompt.AppendLine("- Use bullet points for better parsing");
            prompt.AppendLine("");
            prompt.AppendLine("# Characters");
            prompt.AppendLine("- Protagonists");
            prompt.AppendLine("  - Character Name - Brief description of role and personality");
            prompt.AppendLine("  - Another Character - Description");
            prompt.AppendLine("- Antagonists");
            prompt.AppendLine("  - Villain Name - Brief description of role and motivations");
            prompt.AppendLine("- Supporting Characters");
            prompt.AppendLine("  - Support Character - Brief description");
            prompt.AppendLine("");
            prompt.AppendLine("# Outline");
            prompt.AppendLine("## Chapter 1");
            prompt.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
            prompt.AppendLine("- Key event or action that occurs");
            prompt.AppendLine("- Character development moment");
            prompt.AppendLine("- Plot advancement");
            prompt.AppendLine("");
            prompt.AppendLine("## Chapter 2");
            prompt.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
            prompt.AppendLine("- Key event or action that occurs");
            prompt.AppendLine("- Character development moment");
            prompt.AppendLine("- Plot advancement");
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
            prompt.AppendLine("GRANULAR REVISION GUIDELINES:");
            prompt.AppendLine("- For SMALL CHANGES: Use the 'Original text/Changed to' format with minimal scope");
            prompt.AppendLine("- For ADDITIONS: Simply provide the new content to be added at a specific location");
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
            prompt.AppendLine("If you are generating new content (e.g., adding new chapters, expanding sections), simply provide the new text directly without the 'Original text/Changed to' format.");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL INSTRUCTIONS:");
            prompt.AppendLine("1. When developing outlines, focus on:");
            prompt.AppendLine("   - Clear plot progression from chapter to chapter");
            prompt.AppendLine("   - Character development arcs");
            prompt.AppendLine("   - Thematic consistency");
            prompt.AppendLine("   - Proper story structure (setup, rising action, climax, resolution)");
            prompt.AppendLine("   - MANUSCRIPT-READY CONTENT: Outlines should provide rich material for full chapter creation");
            prompt.AppendLine("   - CONTENT DEPTH: Focus on substance and detail, not writing style (style is handled during prose generation)");
            prompt.AppendLine("");
            prompt.AppendLine("2. For chapter outlines (MANUSCRIPT PREPARATION FOCUS):");
            prompt.AppendLine("   - Use EXACT format: \"## Chapter [number]\" (e.g., \"## Chapter 1\", \"## Chapter 2\")");
            prompt.AppendLine("   - NEVER create custom chapter names or titles");
            prompt.AppendLine("   - Include a comprehensive summary paragraph (3-5 sentences minimum)");
            prompt.AppendLine("   - Follow with detailed bullet points using \"-\" for key events");
            prompt.AppendLine("   - Ensure each chapter advances the plot meaningfully");
            prompt.AppendLine("   - PROVIDE scene-by-scene breakdowns with rich detail");
            prompt.AppendLine("   - SPECIFY character POV, emotional journey, and internal conflicts for each scene");
            prompt.AppendLine("   - DETAIL key dialogue exchanges: topics, emotional subtext, character goals");
            prompt.AppendLine("   - DESCRIBE action sequences step-by-step with sufficient detail for prose expansion");
            prompt.AppendLine("   - INDICATE setting details, atmosphere, and sensory elements");
            prompt.AppendLine("   - INCLUDE character reactions, thoughts, and emotional beats throughout");
            prompt.AppendLine("");
            prompt.AppendLine("3. Chapter Content Requirements for Manuscript Creation:");
            prompt.AppendLine("   - Scene opening: Specific setting, character state, immediate situation/conflict");
            prompt.AppendLine("   - Dialogue content: Key conversation points, character motivations, emotional dynamics");
            prompt.AppendLine("   - Action sequences: Step-by-step breakdown of physical events and character responses");
            prompt.AppendLine("   - Character internal journey: Thoughts, realizations, emotional shifts, decision points");
            prompt.AppendLine("   - Environmental details: Setting descriptions, atmosphere, mood indicators");
            prompt.AppendLine("   - Relationship dynamics: How characters interact, power dynamics, emotional undercurrents");
            prompt.AppendLine("   - Plot advancement: How events move the story forward, consequences, setup for future events");
            prompt.AppendLine("   - Scene transitions: How each scene connects, time passage, location changes");
            prompt.AppendLine("   - Chapter conclusion: Emotional resolution, cliffhangers, character state changes");
            prompt.AppendLine("");
            prompt.AppendLine("4. Content Depth Guidelines:");
            prompt.AppendLine("   - Each scene should have enough detail to write 1,000-3,000 words of prose");
            prompt.AppendLine("   - Include multiple layers: plot events, character emotions, relationship dynamics");
            prompt.AppendLine("   - Specify sensory details that bring scenes to life");
            prompt.AppendLine("   - Note pacing changes: slow character moments vs. fast action sequences");
            prompt.AppendLine("   - Include subtext and underlying tensions between characters");
            prompt.AppendLine("   - Describe physical actions and character body language");
            prompt.AppendLine("   - Note important props, objects, or environmental elements");
            prompt.AppendLine("");
            prompt.AppendLine("5. Character formatting MUST follow this pattern:");
            prompt.AppendLine("   - Use \"- Protagonists\", \"- Antagonists\", \"- Supporting Characters\" as section headers");
            prompt.AppendLine("   - Format each character as: \"  - Character Name - Description\"");
            prompt.AppendLine("   - Use consistent bullet formatting throughout");
            prompt.AppendLine("   - INCLUDE detailed character motivations, goals, and emotional drivers");
            prompt.AppendLine("   - SPECIFY character relationships and dynamics with other characters");
            prompt.AppendLine("");
            prompt.AppendLine("6. For parser compatibility:");
            prompt.AppendLine("   - Use \"-\" for all bullet points (not \"•\" or other symbols)");
            prompt.AppendLine("   - Maintain consistent indentation (2 spaces for sub-bullets)");
            prompt.AppendLine("   - Include action keywords in chapter descriptions (discovers, reveals, confronts, etc.)");
            prompt.AppendLine("   - Keep character names consistent throughout the outline");
            prompt.AppendLine("");
            prompt.AppendLine("7. When expanding or refining existing outline content:");
            prompt.AppendLine("   - Maintain the established structure and formatting");
            prompt.AppendLine("   - Build upon existing character and plot elements");
            prompt.AppendLine("   - ADD layers of detail that enrich the story content");
            prompt.AppendLine("   - Preserve the exact heading structure for optimal parsing");
            prompt.AppendLine("   - FOCUS on content depth rather than writing style");
            prompt.AppendLine("");
            prompt.AppendLine("8. GRANULAR REVISION APPROACH:");
            prompt.AppendLine("   - PREFER TARGETED CHANGES: When a user requests specific modifications, make only the changes needed");
            prompt.AppendLine("   - PRESERVE EXISTING CONTENT: Keep all good content that doesn't conflict with the request");
            prompt.AppendLine("   - INCREMENTAL IMPROVEMENTS: Add or modify individual bullet points, scenes, or character details rather than rewriting entire chapters");
            prompt.AppendLine("   - SURGICAL EDITS: Replace only the specific sections that need updating");
            prompt.AppendLine("   - CONTEXTUAL ADDITIONS: When adding new elements, integrate them smoothly with existing content");
            prompt.AppendLine("");
            prompt.AppendLine("9. Granular Change Guidelines:");
            prompt.AppendLine("   - For character additions: Add only the new character to the Characters section, keep existing ones unchanged");
            prompt.AppendLine("   - For plot adjustments: Modify only affected chapter sections, not entire chapters");
            prompt.AppendLine("   - For scene modifications: Change only the specific scenes mentioned, preserve others");
            prompt.AppendLine("   - For detail enhancement: Add bullet points or expand existing ones rather than rewriting");
            prompt.AppendLine("   - For continuity fixes: Make minimal necessary adjustments to maintain story flow");
            prompt.AppendLine("");
            prompt.AppendLine("10. Always consider:");
            prompt.AppendLine("   - How the outline serves the overall story arc");
            prompt.AppendLine("   - Character motivations and development throughout each scene");
            prompt.AppendLine("   - Pacing and tension management across all chapters");
            prompt.AppendLine("   - Integration with any established series or universe rules");
            prompt.AppendLine("   - MANUSCRIPT READINESS: Does each chapter provide sufficient material for rich prose?");
            prompt.AppendLine("   - SCENE RICHNESS: Multiple layers of plot, character, emotion, and setting");
            prompt.AppendLine("   - CONTENT COMPLETENESS: Can Fiction Writing Beta create compelling chapters from this outline?");
            prompt.AppendLine("");
            prompt.AppendLine("IMPORTANT: This outline will be used as source material for prose generation.");
            prompt.AppendLine("Focus on providing rich, detailed content rather than polished writing style.");
            prompt.AppendLine("The Fiction Writing Beta chain will handle style guide compliance during prose creation.");

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
            
            // Determine if this is an outline development request
            bool isOutlineDevelopment = OUTLINE_DEVELOPMENT_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            // Detect granular revision requests
            var granularKeywords = new[]
            {
                "add", "modify", "change", "update", "adjust", "fix", "correct", "revise",
                "expand this", "enhance", "improve", "refine", "tweak", "small change",
                "minor adjustment", "just need", "only change", "specific", "particular"
            };
            
            bool isGranularRequest = granularKeywords.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase)) && !isOutlineDevelopment;
            
            if (isOutlineDevelopment)
            {
                prompt.AppendLine("=== OUTLINE DEVELOPMENT REQUEST ===");
                prompt.AppendLine("The user is requesting comprehensive outline development. Focus on:");
                prompt.AppendLine("- Story structure and pacing");
                prompt.AppendLine("- Character arcs and development");
                prompt.AppendLine("- Plot progression and tension");
                prompt.AppendLine("- Thematic consistency");
                prompt.AppendLine("- Parser-compatible formatting");
                prompt.AppendLine("");
            }
            else if (isGranularRequest)
            {
                prompt.AppendLine("=== GRANULAR REVISION REQUEST ===");
                prompt.AppendLine("The user is requesting a specific, targeted change. Focus on:");
                prompt.AppendLine("- Making ONLY the requested modifications");
                prompt.AppendLine("- Preserving all existing content that works well");
                prompt.AppendLine("- Using surgical precision rather than broad rewrites");
                prompt.AppendLine("- Maintaining established structure and formatting");
                prompt.AppendLine("- Integrating changes smoothly with existing content");
                prompt.AppendLine("");
            }
            
            // Add parsed outline insights if available
            if (_parsedCurrentOutline != null)
            {
                prompt.AppendLine("=== CURRENT OUTLINE ANALYSIS (UPDATED) ===");
                prompt.AppendLine($"Parsed outline contains:");
                prompt.AppendLine($"- {_parsedCurrentOutline.Chapters.Count} chapters");
                prompt.AppendLine($"- {_parsedCurrentOutline.Characters.Count} characters");
                prompt.AppendLine($"- {_parsedCurrentOutline.MajorPlotPoints.Count} major plot points");
                
                // Always indicate fresh analysis since we refresh every message
                prompt.AppendLine("🔄 FRESH OUTLINE ANALYSIS: Working with the most current version of the outline");
                
                // Acknowledge conversation continuity if we have preserved messages
                var conversationMessageCount = _memory.Count(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                if (conversationMessageCount > 2)
                {
                    prompt.AppendLine("💬 CONVERSATION CONTEXT: Continue building on our previous discussion while working with the updated outline context above.");
                }
                
                if (_parsedCurrentOutline.Chapters.Any())
                {
                    prompt.AppendLine("Current chapters:");
                    foreach (var chapter in _parsedCurrentOutline.Chapters.Take(5))
                    {
                        var summaryPreview = !string.IsNullOrEmpty(chapter.Summary) 
                            ? chapter.Summary.Substring(0, Math.Min(80, chapter.Summary.Length))
                            : "No summary available";
                        prompt.AppendLine($"  • Chapter {chapter.Number}: {summaryPreview}...");
                    }
                    if (_parsedCurrentOutline.Chapters.Count > 5)
                    {
                        prompt.AppendLine($"  • ... and {_parsedCurrentOutline.Chapters.Count - 5} more chapters");
                    }
                }
                
                // Validation check
                var (isValid, issues) = ValidateOutlineFormat();
                if (!isValid)
                {
                    prompt.AppendLine("\n⚠️ FORMATTING ISSUES DETECTED:");
                    foreach (var issue in issues.Where(i => !i.Contains("valid and parser-compatible")))
                    {
                        prompt.AppendLine($"  • {issue}");
                    }
                    prompt.AppendLine("Please address these formatting issues for optimal parser compatibility.");
                }
                else
                {
                    prompt.AppendLine("\n✅ Outline format is parser-compatible");
                }
                prompt.AppendLine("");
            }
            
            prompt.AppendLine($"=== USER REQUEST ===");
            prompt.AppendLine(request);
            
            return prompt.ToString();
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