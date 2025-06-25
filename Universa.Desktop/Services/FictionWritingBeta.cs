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
    public class FictionWritingBeta : BaseLangChainService
    {
        private string _fictionContent;
        private string _styleGuide;
        private string _rules;
        private string _outline;
        private readonly FileReferenceService _fileReferenceService;
        private static Dictionary<string, FictionWritingBeta> _instances = new Dictionary<string, FictionWritingBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MESSAGE_HISTORY_LIMIT = 4;  // Keep last 2 exchanges
        private const int REFRESH_INTERVAL = 3;  // Refresh core materials every N messages
        private int _messagesSinceRefresh = 0;
        private bool _needsRefresh = false;
        private int _currentCursorPosition;
        private string _currentFilePath;
        private AIProvider _currentProvider;
        private string _currentModel;
        private Dictionary<string, string> _frontmatter;

        // Enhanced style guide parsing
        private readonly StyleGuideParser _styleParser = new StyleGuideParser();
        private StyleGuideParser.ParsedStyleGuide _parsedStyle;
        
        // Enhanced rules parsing
        private readonly RulesParser _rulesParser = new RulesParser();
        private RulesParser.ParsedRules _parsedRules;
        
        // Enhanced outline parsing and chapter context
        private readonly OutlineChapterService _outlineChapterService = new OutlineChapterService();

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        // Consolidated list of keywords that trigger full story analysis
        private static readonly string[] FULL_STORY_KEYWORDS = new[]
        {
            "entire story",
            "full story",
            "whole story",
            "overall",
            "story arc",
            "character arc",
            "story analysis",
            "narrative flow",
            "story structure",
            "plot analysis",
            "complete story"
        };
        
        // Keywords that indicate full chapter generation requests
        private static readonly string[] CHAPTER_GENERATION_KEYWORDS = new[]
        {
            "generate chapter", "write chapter", "create chapter", "complete chapter",
            "full chapter", "entire chapter", "whole chapter", "chapter from scratch",
            "new chapter", "draft chapter", "compose chapter"
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

            System.Diagnostics.Debug.WriteLine($"\n=== TOKEN ESTIMATES ===");
            System.Diagnostics.Debug.WriteLine($"System Message: ~{systemTokens} tokens");
            System.Diagnostics.Debug.WriteLine($"Conversation: ~{conversationTokens} tokens");
            System.Diagnostics.Debug.WriteLine($"Total: ~{systemTokens + conversationTokens} tokens");
            
            if (_currentProvider == AIProvider.Anthropic && (systemTokens + conversationTokens) > 100000)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ WARNING: Approaching Anthropic's token limit!");
            }
        }

        private FictionWritingBeta(string apiKey, string model, AIProvider provider, string content, string filePath = null, string libraryPath = null) 
            : base(apiKey, model, provider)
        {
            _currentProvider = provider;
            _currentModel = model;

            // Get the universal library path from configuration
            var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            var universalLibraryPath = configService?.Provider?.LibraryPath;

            if (string.IsNullOrEmpty(universalLibraryPath))
            {
                // Fallback or error if universal library path isn't found - for now, we can throw or log prominently
                // Alternatively, could use the passed-in libraryPath as a last resort, but that defeats the purpose of this fix.
                System.Diagnostics.Debug.WriteLine("CRITICAL ERROR: Universal library path is not configured or accessible. File references may be constrained.");
                // Forcing an error might be safer to ensure configuration is correct:
                throw new InvalidOperationException("Universal library path is not configured. Please set it in the application settings.");
            }
            
            // Use the universal library path for FileReferenceService
            _fileReferenceService = new FileReferenceService(universalLibraryPath);
            System.Diagnostics.Debug.WriteLine($"FileReferenceService initialized with universal library path: {universalLibraryPath}");

            _currentFilePath = filePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                _fileReferenceService.SetCurrentFile(filePath); // Still set current file for relative path resolution from it
                System.Diagnostics.Debug.WriteLine($"Current file for FileReferenceService set to: {filePath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Warning: filePath is null or empty in FictionWritingBeta constructor. Relative references from current file might not work as expected initially.");
            }
        }

        public static async Task<FictionWritingBeta> GetInstance(string apiKey, string model, AIProvider provider, string filePath, string libraryPath)
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
                FictionWritingBeta instance;
                
                // IMPORTANT: Always force a new instance to ensure correct library path
                // The static cache was causing issues when switching between files in different directories
                System.Diagnostics.Debug.WriteLine($"Creating new FictionWritingBeta instance for file: {filePath} with library path: {libraryPath}");
                instance = new FictionWritingBeta(apiKey, model, provider, null, filePath, libraryPath);
                
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
            System.Diagnostics.Debug.WriteLine($"UpdateContentAndInitialize called with content length: {content?.Length ?? 0}");
            
            // Store only the most recent conversation context
            var recentMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                      .TakeLast(MESSAGE_HISTORY_LIMIT)
                                      .ToList();
            
            // Clear all messages
            _memory.Clear();
            
            // Update content
            await UpdateContent(content);
            
            // Initialize new system message with core materials
            InitializeSystemMessage();
            
            // Restore only recent messages
            _memory.AddRange(recentMessages);
            
            // Reset refresh counter
            _messagesSinceRefresh = 0;
            _needsRefresh = false;
            
            System.Diagnostics.Debug.WriteLine("UpdateContentAndInitialize completed");
        }

        private async Task UpdateContent(string content)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateContent called with content length: {content?.Length ?? 0}");
            
            // Check for null content
            if (string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine("UpdateContent: content is null or empty");
                _fictionContent = string.Empty;
                _frontmatter = new Dictionary<string, string>();
                return;
            }
            
            _fictionContent = content;
            
            // Process frontmatter if present
            bool hasFrontmatter = false;
            _frontmatter = new Dictionary<string, string>();
            
            System.Diagnostics.Debug.WriteLine($"Checking for frontmatter in content starting with: '{content.Substring(0, Math.Min(20, content.Length))}...'");
            
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
                                // Handle tags (like #fiction) - remove hashtag
                                string tag = line.Trim().Substring(1);
                                _frontmatter[tag] = "true";
                                System.Diagnostics.Debug.WriteLine($"Found frontmatter tag: '{tag}'");
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Ignoring frontmatter line without key-value pair: '{line}'");
                            }
                        }
                        
                        // Skip past the closing delimiter
                        int contentStartIndex = endIndex + 4; // Length of "\n---"
                        if (contentStartIndex < content.Length)
                        {
                            // If there's a newline after the closing delimiter, skip it too
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            // Update _fictionContent to only contain the content after frontmatter
                            _fictionContent = content.Substring(contentStartIndex);
                        }
                        
                        // Set flag indicating we found and processed frontmatter
                        hasFrontmatter = true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("No closing frontmatter delimiter found");
                    }
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Content does not start with frontmatter delimiter");
            }
            
            // Log the extracted frontmatter
            System.Diagnostics.Debug.WriteLine($"Extracted {_frontmatter.Count} frontmatter items");
            foreach (var key in _frontmatter.Keys)
            {
                System.Diagnostics.Debug.WriteLine($"  '{key}' = '{_frontmatter[key]}'");
            }
            
            // Explicitly check for style, rules, and outline keys
            string[] checkKeys = new[] { "style", "rules", "outline", "ref style", "ref rules", "ref outline" };
            foreach (var checkKey in checkKeys)
            {
                if (_frontmatter.ContainsKey(checkKey))
                {
                    System.Diagnostics.Debug.WriteLine($"Found important frontmatter key: '{checkKey}' = '{_frontmatter[checkKey]}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Important key NOT found in frontmatter: '{checkKey}'");
                }
            }
            
            // Check for references in frontmatter and process them
            if (hasFrontmatter)
            {
                System.Diagnostics.Debug.WriteLine("Processing frontmatter references");
                
                // Define possible variations of keys to check
                string[] styleKeyVariations = new[] { "style", "ref style", "style-file", "style-ref", "stylefile", "styleref", "style_file", "style_ref" };
                string[] rulesKeyVariations = new[] { "rules", "ref rules", "rules-file", "rules-ref", "rulesfile", "rulesref", "rules_file", "rules_ref" };
                string[] outlineKeyVariations = new[] { "outline", "ref outline", "outline-file", "outline-ref", "outlinefile", "outlineref", "outline_file", "outline_ref" };
                
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
                else
                {
                    System.Diagnostics.Debug.WriteLine("No style reference found in frontmatter using any key variation");
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
                else
                {
                    System.Diagnostics.Debug.WriteLine("No rules reference found in frontmatter using any key variation");
                }
                
                // Process outline reference - try all variations
                string outlineRef = null;
                foreach (var keyVariation in outlineKeyVariations)
                {
                    if (_frontmatter.TryGetValue(keyVariation, out outlineRef) && !string.IsNullOrEmpty(outlineRef))
                    {
                        System.Diagnostics.Debug.WriteLine($"Found outline reference using key '{keyVariation}': '{outlineRef}'");
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(outlineRef))
                {
                    await ProcessOutlineReference(outlineRef);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No outline reference found in frontmatter using any key variation");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No frontmatter found or no references to process");
            }
            
            // Final summary of what was loaded
            System.Diagnostics.Debug.WriteLine("=== FICTION CONTENT SUMMARY ===");
            System.Diagnostics.Debug.WriteLine($"Style guide: {(_styleGuide != null ? $"{_styleGuide.Length} chars" : "NULL")}");
            System.Diagnostics.Debug.WriteLine($"Rules: {(_rules != null ? $"{_rules.Length} chars" : "NULL")}");
            System.Diagnostics.Debug.WriteLine($"Outline: {(_outline != null ? $"{_outline.Length} chars" : "NULL")}");
            System.Diagnostics.Debug.WriteLine($"Fiction content: {(_fictionContent != null ? $"{_fictionContent.Length} chars" : "NULL")}");
            
            // Log token estimates for debugging
            LogTokenEstimates();
            
            // Initialize system message
            InitializeSystemMessage();
        }
        
        /// <summary>
        /// Process a style reference
        /// </summary>
        private async Task ProcessStyleReference(string refPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading style reference from: '{refPath}'");
                System.Diagnostics.Debug.WriteLine($"  Current file path: '{_currentFilePath}'");
                System.Diagnostics.Debug.WriteLine($"  Library path: '{_fileReferenceService?.GetType().GetField("_libraryPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_fileReferenceService)}'");
                
                string styleContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(styleContent))
                {
                    _styleGuide = styleContent;
                    
                    // Parse the style guide for enhanced understanding
                    try
                    {
                        _parsedStyle = _styleParser.Parse(styleContent);
                        System.Diagnostics.Debug.WriteLine($"Successfully parsed style guide:");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.Sections.Count} sections");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.AllRules.Count} total rules");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.CriticalRules.Count} critical rules");
                        System.Diagnostics.Debug.WriteLine($"  - Writing sample: {(_parsedStyle.WritingSample?.Length ?? 0)} chars");
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing style guide: {parseEx.Message}");
                        // Continue with unparsed style guide
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded style reference: {styleContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Style reference file was empty or could not be loaded: '{refPath}'");
                    
                    // Try to check if the file exists directly
                    string directPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentFilePath), refPath);
                    if (System.IO.File.Exists(directPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"File exists at direct path: '{directPath}', but content couldn't be loaded");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"File does NOT exist at direct path: '{directPath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading style reference: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Process a rules reference
        /// </summary>
        private async Task ProcessRulesReference(string refPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading rules reference from: '{refPath}'");
                System.Diagnostics.Debug.WriteLine($"  Current file path: '{_currentFilePath}'");
                System.Diagnostics.Debug.WriteLine($"  Library path: '{_fileReferenceService?.GetType().GetField("_libraryPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_fileReferenceService)}'");
                
                string rulesContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(rulesContent))
                {
                    _rules = rulesContent;
                    
                    // Parse the rules for enhanced understanding
                    try
                    {
                        _parsedRules = _rulesParser.Parse(rulesContent);
                        System.Diagnostics.Debug.WriteLine($"Successfully parsed rules:");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Characters.Count} characters");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Timeline.Books.Count} books");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.PlotConnections.Count} plot connections");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.CriticalFacts.Count} critical facts");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Locations.Count} locations");
                        System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Organizations.Count} organizations");
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error parsing rules: {parseEx.Message}");
                        // Continue with unparsed rules
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded rules reference: {rulesContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Rules reference file was empty or could not be loaded: '{refPath}'");
                    
                    // Try to check if the file exists directly
                    string directPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentFilePath), refPath);
                    if (System.IO.File.Exists(directPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"File exists at direct path: '{directPath}', but content couldn't be loaded");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"File does NOT exist at direct path: '{directPath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading rules reference: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        /// <summary>
        /// Process an outline reference
        /// </summary>
        private async Task ProcessOutlineReference(string refPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading outline reference from: '{refPath}'");
                System.Diagnostics.Debug.WriteLine($"  Current file path: '{_currentFilePath}'");
                System.Diagnostics.Debug.WriteLine($"  Library path: '{_fileReferenceService?.GetType().GetField("_libraryPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_fileReferenceService)}'");
                
                string outlineContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(outlineContent))
                {
                    _outline = outlineContent;
                    
                    // Parse outline for enhanced chapter context
                    _outlineChapterService.SetOutline(outlineContent);
                    
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded outline reference: {outlineContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Outline reference file was empty or could not be loaded: '{refPath}'");
                    
                    // Try to check if the file exists directly
                    string directPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_currentFilePath), refPath);
                    if (System.IO.File.Exists(directPath))
                    {
                        System.Diagnostics.Debug.WriteLine($"File exists at direct path: '{directPath}', but content couldn't be loaded");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"File does NOT exist at direct path: '{directPath}'");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading outline reference: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = BuildFictionPrompt("");
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

        private string BuildFictionPrompt(string request)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are a MASTER NOVELIST crafting publication-ready fiction. Your primary goal is creating narrative prose that would appear in a professionally published novel, following the established style guide above all else.");
            
            // Add current date and time context for temporal awareness
            prompt.AppendLine("");
            prompt.AppendLine("=== CURRENT DATE AND TIME ===");
            prompt.AppendLine($"Current Date/Time: {DateTime.Now:F}");
            prompt.AppendLine($"Local Time Zone: {TimeZoneInfo.Local.DisplayName}");
            
            // Always use raw style guide - avoid parsing issues that lose context
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine("\n=== STYLE GUIDE ===");
                prompt.AppendLine("The following is your comprehensive guide to the writing style with clear sections and headers:");
                prompt.AppendLine(_styleGuide);
            }
            else
            {
                // Fallback framework description if no style guide available
                prompt.AppendLine("\nYou have access to these storytelling resources:");
                prompt.AppendLine("* **CORE RULES**: Facts about your story world");
                prompt.AppendLine("* **STORY OUTLINE**: Plot structure for reference");
                prompt.AppendLine("* **CURRENT STORY CONTENT**: Existing narrative to build upon");
            }

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
                
                if (_frontmatter.TryGetValue("summary", out string summary) && !string.IsNullOrEmpty(summary))
                {
                    prompt.AppendLine($"\nSummary: {summary}");
                }
                
                // Check for any chapters defined in frontmatter
                var chapterKeys = _frontmatter.Keys.Where(k => k.StartsWith("chapter")).ToList();
                if (chapterKeys.Count > 0)
                {
                    prompt.AppendLine("\nChapter Structure:");
                    foreach (var key in chapterKeys.OrderBy(k => k))
                    {
                        prompt.AppendLine($"- {key}: {_frontmatter[key]}");
                    }
                }
            }

            // Always use raw rules content - avoid parsing issues that strip character names
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== CORE RULES ===");
                prompt.AppendLine("These are fundamental rules for the story universe with clear sections and character descriptions:");
                prompt.AppendLine(_rules);
            }

            // Add enhanced outline context if available
            if (!string.IsNullOrEmpty(_outline))
            {
                // Use unified chapter detection service for consistent chapter number detection
                int currentChapter = 1;
                if (!string.IsNullOrEmpty(_fictionContent))
                {
                    currentChapter = ChapterDetectionService.GetCurrentChapterNumber(_fictionContent, _currentCursorPosition);
                    System.Diagnostics.Debug.WriteLine($"BuildFictionPrompt: Current chapter determined as {currentChapter} (unified detection)");
                }
                
                // Check if this is a chapter generation request
                bool isChapterGeneration = CHAPTER_GENERATION_KEYWORDS.Any(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                // Get chapter context and build enhanced prompt
                var chapterContext = _outlineChapterService.GetChapterContext(currentChapter);
                var enhancedOutlinePrompt = _outlineChapterService.BuildChapterOutlinePrompt(chapterContext, isChapterGeneration);
                
                if (!string.IsNullOrEmpty(enhancedOutlinePrompt))
                {
                    prompt.AppendLine(enhancedOutlinePrompt);
                }
                else
                {
                    // Fallback to original outline presentation
                    prompt.AppendLine("\n=== STORY OUTLINE (STRUCTURAL GUIDANCE) ===");
                    prompt.AppendLine("📋 This provides story structure for reference - USE AS CREATIVE GOALS, NOT SOURCE TEXT:");
                    prompt.AppendLine(_outline);
                    prompt.AppendLine("\n⚠️ IMPORTANT: Transform outline objectives into completely original narrative prose. Do not expand outline text directly.");
                }
            }

            // Style guide is now handled above - no need for duplicate section

            // Add current content (potentially a snippet)
            // The BuildContextPrompt method is responsible for adding the === CURRENT CONTENT === section with relevant fiction text.
            // No need to add _fictionContent directly here if BuildContextPrompt handles it before this system message is finalized for the API call.
            // However, if this BuildFictionPrompt is the *sole* source of the system message that includes current content, it should be here.
            // Based on ProcessRequest calling UpdateContentAndInitialize (which calls this) and then BuildContextPrompt for user message,
            // it seems the system message generated here might be more static, with context added per user turn.
            // For clarity, let's assume the system prompt might be used as a base and could benefit from seeing the full content context if no other mechanism provides it.
            // Re-adding a general instruction about current content if it's not empty.
            if (!string.IsNullOrEmpty(_fictionContent))
            {
                prompt.AppendLine("\n=== CURRENT STORY CONTENT ===");
                prompt.AppendLine("This is the story text being worked on. Follow the style guide and core rules when working with this content.");
            }

            // Add response format instructions
            prompt.AppendLine(@"

=== CRITICAL OUTLINE USAGE INSTRUCTIONS ===
⚠️ OUTLINE HANDLING: The outline provides STRUCTURAL GUIDANCE only. 

DO NOT:
- Copy outline bullet points into your prose
- Expand outline text directly into paragraphs
- Use outline phrasing as the basis for sentences
- Treat outline descriptions as content to elaborate

INSTEAD:
- Use outline points as dramatic moments to CREATE
- Develop your own original dialogue and descriptions  
- Create fresh narrative prose that ACHIEVES the outlined goals
- Invent specific details, conversations, and actions that fulfill the outline's purpose

=== RESPONSE FORMAT ===
When suggesting specific text changes, you MUST use EXACTLY this format:

For REVISIONS (replacing existing text):
```
Original text:
[paste the exact text to be replaced]
```

```
Changed to:
[your new version of the text]
```

For INSERTIONS (adding new text after existing text):
```
Insert after:
[paste the exact anchor text to insert after]
```

```
New text:
[the new content to insert]
```

If you are generating new content (e.g., continuing a scene, writing a new paragraph), simply provide the new text directly without the revision or insertion format.
If providing analysis, suggestions, or answering questions, use clear, concise language. Point to specific parts of the provided materials (Style Guide, Rules, Outline, Story Content) if relevant.

**REVISION FOCUS**: When providing revisions or insertions, be concise:
- Provide the revision/insertion blocks with minimal explanation
- Only add commentary if the user specifically asks for reasoning or analysis
- Focus on the changes themselves, not lengthy explanations
- If multiple revisions are needed, provide them cleanly in sequence
- Let the revised text speak for itself

When generating new content:
- Follow your style guide completely - it contains all writing guidance you need
- Write original narrative prose, never expand or copy outline text
- Treat outline points as EVENTS/OCCURRENCES to write about, not content to elaborate
- Create scenes showing these events happening through original dialogue, actions, and narrative");

            // Removed hardcoded narrative rules - relying on style guide content instead

            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ProcessRequest called with content length: {content?.Length ?? 0}");
                
                // Check if we need to refresh core materials
                _messagesSinceRefresh++;
                if (_messagesSinceRefresh >= REFRESH_INTERVAL)
                {
                    _needsRefresh = true;
                }

                // If content has changed or refresh is needed, update
                if (_fictionContent != content || _needsRefresh)
                {
                    System.Diagnostics.Debug.WriteLine($"Content has changed or refresh is needed. _fictionContent length: {_fictionContent?.Length ?? 0}, content length: {content?.Length ?? 0}");
                    await UpdateContentAndInitialize(content);
                }

                // Build the prompt with the most relevant context
                var contextPrompt = BuildContextPrompt(request);
                
                // Add the user request to memory
                AddUserMessage(request);
                
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
                        System.Diagnostics.Debug.WriteLine($"\n=== FICTION BETA ERROR ===\n{ex}");
                        throw new Exception($"Error after {retryCount} retries: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== FICTION BETA ERROR ===\n{ex}");
                throw;
            }
        }

        private string BuildContextPrompt(string request)
        {
            var prompt = new StringBuilder();
            
            // Include relevant section of fiction content
            if (!string.IsNullOrEmpty(_fictionContent))
            {
                var relevantContent = GetRelevantContent(_fictionContent, request);
                prompt.AppendLine("\n=== CURRENT CONTENT ===");
                prompt.AppendLine("This is the specific portion of the story we're working with. Focus your editing, suggestions, and content generation on this section, while maintaining consistency with the overall rules and style defined in the system message. If this is a full story view, consider the broader narrative structure in your response.");
                prompt.AppendLine(relevantContent);
            }

            return prompt.ToString();
        }

        private string GetRelevantContent(string content, string request)
        {
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine("GetRelevantContent: Content is empty");
                return string.Empty;
            }

            // Check if this is a request for full story analysis
            bool needsFullStory = FULL_STORY_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            // OPTIMIZATION FOR LARGE FILES ON INITIAL/GENERIC REQUEST:
            // If the request is generic (empty/whitespace) and the file is large, 
            // provide a smaller, faster-to-generate context snippet around the cursor.
            // The threshold (e.g., 20000 chars) can be adjusted based on performance testing.
            if (!needsFullStory && string.IsNullOrWhiteSpace(request) && content.Length > 20000) 
            {
                System.Diagnostics.Debug.WriteLine($"Returning lightweight context for large file on generic request. Content length: {content.Length}, Cursor: {_currentCursorPosition}");
                int snippetRadius = 2000; // Characters before and after cursor
                int snippetLength = snippetRadius * 2;

                int start = Math.Max(0, _currentCursorPosition - snippetRadius);
                // Adjust start if snippet goes beyond content length
                if (start + snippetLength > content.Length)
                {
                    start = Math.Max(0, content.Length - snippetLength);
                }
                
                string actualSnippet = content.Substring(start, Math.Min(snippetLength, content.Length - start));

                var sb = new StringBuilder();
                if (start > 0)
                {
                    sb.AppendLine("... (Content before cursor truncated for initial performance) ...");
                }
                sb.AppendLine(actualSnippet);
                if (start + actualSnippet.Length < content.Length)
                {
                    sb.AppendLine("... (Content after cursor truncated for initial performance) ...");
                }
                // It's useful to indicate where the cursor is within this snippet for the AI, if possible.
                // For simplicity in this fast path, we're not recalculating the exact cursor within the snippet,
                // but the AI will know the snippet is centered around the cursor.
                // A <<CURSOR POSITION>> marker could be added if precise relative positioning is re-calculated here.
                System.Diagnostics.Debug.WriteLine($"Lightweight context: Start={start}, Length={actualSnippet.Length}");
                return sb.ToString();
            }

            // If it's a full story analysis or content is small, return everything
            if (needsFullStory || content.Length < 2000) // Original threshold for small files
            {
                if (needsFullStory)
                {
                    System.Diagnostics.Debug.WriteLine($"Returning full story content for analysis. Triggered by keyword in request: {request}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Returning full story content because it's small ({content.Length} characters < 2000)");
                }
                return content;
            }

            // Split content into lines to find chapter boundaries
            var lines = content.Split('\n');
            System.Diagnostics.Debug.WriteLine($"Content has {lines.Length} lines");

            // If no lines, return empty
            if (lines.Length == 0)
            {
                System.Diagnostics.Debug.WriteLine("GetRelevantContent: No lines in content");
                return string.Empty;
            }
            
            // Skip the #file: line if present - this is our own metadata, not part of the document
            int startLineIndex = 0;
            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#file:"))
            {
                System.Diagnostics.Debug.WriteLine("Skipping #file: metadata line at beginning of document");
                startLineIndex = 1;
            }
            
            // Find the current line index based on cursor position
            int currentLineIndex = startLineIndex; // Default to first content line
            int currentPosition = 0;
            bool foundLine = false;
            
            // Skip the first line positions if we're skipping the #file: line
            if (startLineIndex > 0 && lines.Length > 0)
            {
                currentPosition += lines[0].Length + 1; // Skip first line length + newline
            }
            
            for (int i = startLineIndex; i < lines.Length; i++)
            {
                if (currentPosition <= _currentCursorPosition && 
                    _currentCursorPosition <= currentPosition + lines[i].Length + 1)
                {
                    currentLineIndex = i;
                    foundLine = true;
                    System.Diagnostics.Debug.WriteLine($"Found cursor at line {i} (position {_currentCursorPosition} in range {currentPosition}-{currentPosition + lines[i].Length + 1})");
                    break;
                }
                currentPosition += lines[i].Length + 1;
            }
            
            // If we haven't found the line and we're at the end of the file
            if (!foundLine)
            {
                // If cursor is beyond the end of the file, use last line
                if (_currentCursorPosition >= currentPosition)
                {
                    System.Diagnostics.Debug.WriteLine($"Cursor position {_currentCursorPosition} is beyond end of file, using last line");
                    currentLineIndex = lines.Length - 1;
                    
                    // Important: when we're at end of file, we should mark this as "found line"
                    // so the chapter detection works correctly
                    foundLine = true;
                }
                // If cursor is at the start or invalid, use first content line (after #file:)
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Cursor position {_currentCursorPosition} is invalid, using first content line");
                    currentLineIndex = startLineIndex;
                }
            }

            // Ensure currentLineIndex is within bounds
            currentLineIndex = Math.Max(startLineIndex, Math.Min(currentLineIndex, lines.Length - 1));
            System.Diagnostics.Debug.WriteLine($"Current line index: {currentLineIndex}, line content: '{lines[currentLineIndex]}'");

            // Use unified chapter detection service for consistent boundary detection
            var chapterBoundaries = ChapterDetectionService.GetChapterBoundaries(content);
            System.Diagnostics.Debug.WriteLine($"ChapterDetectionService found {chapterBoundaries.Count} boundaries");

            // Find current chapter boundaries
            int currentChapterIndex = 0; // Default to first chapter
            
            // Find the chapter that contains our current line
            for (int i = 0; i < chapterBoundaries.Count - 1; i++)
            {
                // The cursor is in this chapter if it's between boundaries
                // IMPORTANT: If cursor is exactly at a chapter boundary (heading), it belongs to THAT chapter, not the previous one
                if (currentLineIndex >= chapterBoundaries[i] && currentLineIndex < chapterBoundaries[i + 1])
                {
                    currentChapterIndex = i;
                    
                    // Special debug for boundary cases
                    if (currentLineIndex == chapterBoundaries[i])
                    {
                        System.Diagnostics.Debug.WriteLine($"Cursor is at the start of chapter boundary {i} - treating as being IN this chapter");
                    }
                    
                    break;
                }
            }
            
            // Ensure currentChapterIndex is valid (can't exceed the second-to-last boundary)
            // The last boundary is always the end of document, so max valid chapter index is Count - 2
            if (currentChapterIndex >= chapterBoundaries.Count - 1)
            {
                currentChapterIndex = chapterBoundaries.Count - 2;
                System.Diagnostics.Debug.WriteLine($"Clamping currentChapterIndex to max valid value: {currentChapterIndex}");
            }
            
            System.Diagnostics.Debug.WriteLine($"Found {chapterBoundaries.Count} chapter boundaries");
            System.Diagnostics.Debug.WriteLine($"Current chapter index: {currentChapterIndex} (boundaries: {chapterBoundaries[currentChapterIndex]} to {chapterBoundaries[currentChapterIndex + 1]}, cursor at line: {currentLineIndex})");

            // Build the content with context
            var result = new StringBuilder();

            // Add a marker if we're not starting from the beginning and there's a previous chapter
            if (currentChapterIndex > 0 && chapterBoundaries.Count > 1)
            {
                result.AppendLine("... (Previous content omitted) ...");
                
                // Add the entire previous chapter
                var previousChapterStart = chapterBoundaries[currentChapterIndex - 1];
                var previousChapterEnd = chapterBoundaries[currentChapterIndex] - 1;
                if (previousChapterEnd >= previousChapterStart)
                {
                    // Get the complete previous chapter content
                    var previousChapterContent = string.Join("\n", 
                        lines.Skip(previousChapterStart)
                             .Take(previousChapterEnd - previousChapterStart + 1));
                    
                    if (!string.IsNullOrEmpty(previousChapterContent))
                    {
                        result.AppendLine("=== PREVIOUS CHAPTER ===");
                        result.AppendLine(previousChapterContent);
                        System.Diagnostics.Debug.WriteLine($"Added complete previous chapter (lines {previousChapterStart}-{previousChapterEnd})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Previous chapter was empty");
                    }
                }
            }

            // Add current chapter content with cursor position
            var chapterStart = chapterBoundaries[currentChapterIndex];
            
            // Debug boundary access
            System.Diagnostics.Debug.WriteLine($"Accessing boundary for current chapter: currentChapterIndex={currentChapterIndex}, boundaries.Count={chapterBoundaries.Count}");
            
            if (currentChapterIndex + 1 >= chapterBoundaries.Count)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ERROR: Trying to access boundary {currentChapterIndex + 1} but only have {chapterBoundaries.Count} boundaries");
                return "Error: Invalid chapter boundary access";
            }
            
            var chapterEnd = Math.Min(chapterBoundaries[currentChapterIndex + 1], lines.Length - 1);
            System.Diagnostics.Debug.WriteLine($"Current chapter spans lines {chapterStart}-{chapterEnd} (lines.Length={lines.Length})");
            
            // Get the complete current chapter content
            System.Diagnostics.Debug.WriteLine($"Extracting chapter content: Skip({chapterStart}).Take({chapterEnd - chapterStart + 1}) from {lines.Length} lines");
            
            if (chapterStart >= lines.Length || chapterEnd >= lines.Length)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ERROR: Invalid line range - chapterStart={chapterStart}, chapterEnd={chapterEnd}, lines.Length={lines.Length}");
                return "Error: Invalid line range for chapter extraction";
            }
            
            var currentChapterContent = string.Join("\n",
                lines.Skip(chapterStart)
                     .Take(chapterEnd - chapterStart + 1));
            
            if (!string.IsNullOrEmpty(currentChapterContent))
            {
                result.AppendLine("\n=== CURRENT CHAPTER ===");
                // Insert cursor position marker at the appropriate line
                var currentChapterLines = currentChapterContent.Split('\n');
                var cursorLineInChapter = currentLineIndex - chapterStart;
                
                System.Diagnostics.Debug.WriteLine($"Cursor marker calculation: currentLineIndex={currentLineIndex}, chapterStart={chapterStart}, cursorLineInChapter={cursorLineInChapter}, chapterLines.Length={currentChapterLines.Length}");
                
                // Ensure cursorLineInChapter is within bounds
                cursorLineInChapter = Math.Max(0, Math.Min(cursorLineInChapter, currentChapterLines.Length - 1));
                
                System.Diagnostics.Debug.WriteLine($"Cursor marker position after bounds check: {cursorLineInChapter}");
                
                // Add the complete chapter content with cursor position marker
                for (int i = 0; i < currentChapterLines.Length; i++)
                {
                    // If we're at the cursor line, add the cursor marker before the line
                    if (i == cursorLineInChapter)
                    {
                        result.AppendLine("<<CURSOR POSITION>>");
                    }
                    
                    // Safety check before accessing array
                    if (i < currentChapterLines.Length)
                    {
                        result.AppendLine(currentChapterLines[i]);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ ERROR: Trying to access currentChapterLines[{i}] but length is {currentChapterLines.Length}");
                        break;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"✅ Added complete current chapter with cursor marker at line {cursorLineInChapter} relative to chapter start");
            }
            else
            {
                result.AppendLine("\n=== CURRENT CHAPTER ===");
                result.AppendLine("<<CURSOR POSITION>>");
                System.Diagnostics.Debug.WriteLine("Current chapter was empty");
            }

            // Add next chapter if it exists
            if (currentChapterIndex < chapterBoundaries.Count - 2)
            {
                var nextChapterStart = chapterBoundaries[currentChapterIndex + 1];
                var nextChapterEnd = chapterBoundaries[currentChapterIndex + 2] - 1;
                
                System.Diagnostics.Debug.WriteLine($"Next chapter calculation: currentChapterIndex={currentChapterIndex}, boundaries.Count={chapterBoundaries.Count}");
                System.Diagnostics.Debug.WriteLine($"Next chapter range: lines {nextChapterStart} to {nextChapterEnd}");
                
                if (nextChapterEnd >= nextChapterStart)
                {
                    // Get the complete next chapter content
                    var nextChapterContent = string.Join("\n",
                        lines.Skip(nextChapterStart)
                             .Take(nextChapterEnd - nextChapterStart + 1));
                    
                    if (!string.IsNullOrEmpty(nextChapterContent))
                    {
                        result.AppendLine("\n=== NEXT CHAPTER ===");
                        result.AppendLine(nextChapterContent);
                        System.Diagnostics.Debug.WriteLine($"✅ Added complete next chapter (lines {nextChapterStart}-{nextChapterEnd})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("❌ Next chapter was empty");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Invalid next chapter range: {nextChapterStart} to {nextChapterEnd}");
                }
                
                // Add marker if there are more chapters after this
                if (currentChapterIndex < chapterBoundaries.Count - 3)
                {
                    result.AppendLine("\n... (Subsequent content omitted) ...");
                    System.Diagnostics.Debug.WriteLine("Added marker for subsequent content");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"❌ No next chapter: currentChapterIndex={currentChapterIndex} >= boundaries.Count-2={chapterBoundaries.Count - 2}");
                
                // Additional debug: show all boundaries
                System.Diagnostics.Debug.WriteLine("All chapter boundaries:");
                for (int i = 0; i < chapterBoundaries.Count; i++)
                {
                    var boundaryLine = chapterBoundaries[i];
                    string lineContent;
                    
                    if (boundaryLine >= lines.Length)
                    {
                        lineContent = "[END OF DOCUMENT]";
                    }
                    else
                    {
                        lineContent = lines[boundaryLine].Trim();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"  [{i}] Line {boundaryLine}: {lineContent}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"Selected chapter content around cursor position {_currentCursorPosition}");
            System.Diagnostics.Debug.WriteLine($"Current chapter index: {currentChapterIndex}");
            System.Diagnostics.Debug.WriteLine($"Current line index: {currentLineIndex}");
            
            // Log the size of the selected content vs. full content
            var selectedContent = result.ToString();
            System.Diagnostics.Debug.WriteLine($"Selected content size: {selectedContent.Length} characters ({(selectedContent.Length * 100.0 / content.Length):F1}% of full content)");
            
            return selectedContent;
        }

        public void UpdateCursorPosition(int position)
        {
            _currentCursorPosition = position;
            System.Diagnostics.Debug.WriteLine($"Cursor position updated to: {position}");
        }

        /// <summary>
        /// Sets the current file path for reference resolution
        /// </summary>
        /// <param name="filePath">The absolute path to the current file</param>
        public void SetCurrentFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine("Warning: Attempted to set empty file path");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"Setting current file path: '{filePath}'");
            _currentFilePath = filePath;
            
            // Also update the file reference service
            if (_fileReferenceService != null)
            {
                _fileReferenceService.SetCurrentFile(filePath);
                System.Diagnostics.Debug.WriteLine("Updated FileReferenceService with the new file path");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("Warning: FileReferenceService is null, cannot update");
            }
        }

        /// <summary>
        /// Directly sets the rules content without relying on frontmatter
        /// </summary>
        /// <param name="rulesContent">The rules content to use</param>
        public void SetRulesContent(string rulesContent)
        {
            if (!string.IsNullOrEmpty(rulesContent))
            {
                _rules = rulesContent;
                
                // Parse the rules for enhanced understanding
                try
                {
                    _parsedRules = _rulesParser.Parse(rulesContent);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed directly set rules:");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Characters.Count} characters");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Timeline.Books.Count} books");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.PlotConnections.Count} plot connections");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.CriticalFacts.Count} critical facts");
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing directly set rules: {parseEx.Message}");
                    // Continue with unparsed rules
                }
                
                System.Diagnostics.Debug.WriteLine($"Rules content directly set: {rulesContent.Length} characters");
            }
        }

        /// <summary>
        /// Directly sets the style guide content without relying on frontmatter
        /// </summary>
        /// <param name="styleContent">The style guide content to use</param>
        public void SetStyleGuideContent(string styleContent)
        {
            if (!string.IsNullOrEmpty(styleContent))
            {
                _styleGuide = styleContent;
                
                // Parse the style guide for enhanced understanding
                try
                {
                    _parsedStyle = _styleParser.Parse(styleContent);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed directly set style guide:");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.Sections.Count} sections");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.AllRules.Count} total rules");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedStyle.CriticalRules.Count} critical rules");
                    System.Diagnostics.Debug.WriteLine($"  - Writing sample: {(_parsedStyle.WritingSample?.Length ?? 0)} chars");
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing directly set style guide: {parseEx.Message}");
                    // Continue with unparsed style guide
                }
                
                System.Diagnostics.Debug.WriteLine($"Style guide content directly set: {styleContent.Length} characters");
            }
        }

        /// <summary>
        /// Directly sets the outline content without relying on frontmatter
        /// </summary>
        /// <param name="outlineContent">The outline content to use</param>
        public void SetOutlineContent(string outlineContent)
        {
            if (!string.IsNullOrEmpty(outlineContent))
            {
                _outline = outlineContent;
                
                // Parse outline for enhanced chapter context
                _outlineChapterService.SetOutline(outlineContent);
                
                System.Diagnostics.Debug.WriteLine($"Outline content directly set: {outlineContent.Length} characters");
            }
        }

        /// <summary>
        /// Directly loads reference files from paths without relying on frontmatter
        /// </summary>
        /// <param name="rulesPath">Path to rules file</param>
        /// <param name="stylePath">Path to style guide file</param>
        /// <param name="outlinePath">Path to outline file</param>
        /// <returns>Async task</returns>
        public async Task LoadReferencesDirectly(string rulesPath = null, string stylePath = null, string outlinePath = null)
        {
            System.Diagnostics.Debug.WriteLine("Loading references directly from paths");
            
            try
            {
                if (!string.IsNullOrEmpty(rulesPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading rules directly from: {rulesPath}");
                    string rulesContent = await _fileReferenceService.GetFileContent(rulesPath, _currentFilePath);
                    if (!string.IsNullOrEmpty(rulesContent))
                    {
                        SetRulesContent(rulesContent);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load rules from path: {rulesPath}");
                    }
                }
                
                if (!string.IsNullOrEmpty(stylePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading style guide directly from: {stylePath}");
                    string styleContent = await _fileReferenceService.GetFileContent(stylePath, _currentFilePath);
                    if (!string.IsNullOrEmpty(styleContent))
                    {
                        SetStyleGuideContent(styleContent); // This will now also parse the style guide
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load style guide from path: {stylePath}");
                    }
                }
                
                if (!string.IsNullOrEmpty(outlinePath))
                {
                    System.Diagnostics.Debug.WriteLine($"Loading outline directly from: {outlinePath}");
                    string outlineContent = await _fileReferenceService.GetFileContent(outlinePath, _currentFilePath);
                    if (!string.IsNullOrEmpty(outlineContent))
                    {
                        SetOutlineContent(outlineContent);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load outline from path: {outlinePath}");
                    }
                }
                
                // Initialize system message to include the new content
                InitializeSystemMessage();
                
                System.Diagnostics.Debug.WriteLine("=== UPDATED FICTION CONTENT SUMMARY ===");
                System.Diagnostics.Debug.WriteLine($"Style guide: {(_styleGuide != null ? $"{_styleGuide.Length} chars" : "NULL")}");
                System.Diagnostics.Debug.WriteLine($"Rules: {(_rules != null ? $"{_rules.Length} chars" : "NULL")}");
                System.Diagnostics.Debug.WriteLine($"Outline: {(_outline != null ? $"{_outline.Length} chars" : "NULL")}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading references directly: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
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
                    System.Diagnostics.Debug.WriteLine($"Cleared FictionWritingBeta instance for file: {filePath}");
                }
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
                System.Diagnostics.Debug.WriteLine("All FictionWritingBeta instances cleared");
            }
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;
        
        /// <summary>
        /// Validates generated content against the outline expectations
        /// </summary>
        public List<string> ValidateContentAgainstOutline(string content, int? chapterNumber = null)
        {
            if (chapterNumber == null && !string.IsNullOrEmpty(_fictionContent))
            {
                // Use unified chapter detection for consistent chapter identification
                chapterNumber = ChapterDetectionService.GetCurrentChapterNumber(_fictionContent, _currentCursorPosition);
                System.Diagnostics.Debug.WriteLine($"ValidateContentAgainstOutline: Using unified detection, chapter {chapterNumber}");
            }
            
            if (chapterNumber.HasValue)
            {
                return _outlineChapterService.ValidateChapterAgainstOutline(content, chapterNumber.Value);
            }
            
            return new List<string>();
        }
        
        /// <summary>
        /// Gets a summary of what should happen in the current chapter
        /// </summary>
        public string GetCurrentChapterExpectations()
        {
            if (!string.IsNullOrEmpty(_fictionContent))
            {
                // Use unified chapter detection for consistent chapter identification
                var currentChapter = ChapterDetectionService.GetCurrentChapterNumber(_fictionContent, _currentCursorPosition);
                System.Diagnostics.Debug.WriteLine($"GetCurrentChapterExpectations: Using unified detection, chapter {currentChapter}");
                return _outlineChapterService.GetChapterExpectationSummary(currentChapter);
            }
            
            return "No chapter expectations available.";
        }
    }
} 