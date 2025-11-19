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
        private List<string> _characterProfiles;
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
        
        // Chapter follow-up service for asking about next chapter generation
        private readonly ChapterFollowUpService _chapterFollowUpService = new ChapterFollowUpService();

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        // BULLY FIX: More precise keywords that require explicit full story analysis intent
        // Removed overly broad terms like "overall" that cause false positives
        private static readonly string[] FULL_STORY_KEYWORDS = new[]
        {
            "analyze entire story",
            "analyze the entire story",
            "entire story analysis",
            "entire story content",
            "full story analysis",
            "analyze full story",
            "analyze the full story",
            "analyze whole story",
            "analyze the whole story", 
            "complete story analysis",
            "analyze complete story",
            "overall story analysis",        // More specific than just "overall"
            "overall narrative analysis",
            "full narrative analysis",
            "analyze story structure",       // More specific than just "story structure"
            "analyze narrative flow",        // More specific than just "narrative flow"
            "analyze plot structure",        // More specific than just "plot analysis"
            "story-wide analysis",
            "full manuscript analysis",
            "analyze the manuscript"
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
            
            // Check if we're using full story analysis mode by looking for the marker in system message
            bool isFullStoryMode = systemMessage?.Content?.Contains("=== FULL STORY ANALYSIS MODE ===") == true;
            if (isFullStoryMode)
            {
                System.Diagnostics.Debug.WriteLine("🔍 FULL STORY ANALYSIS MODE: Using reduced references to prevent 400 errors");
                System.Diagnostics.Debug.WriteLine("   - Truncated style guide to core principles only");
                System.Diagnostics.Debug.WriteLine("   - Excluded detailed rules and character profiles");
                System.Diagnostics.Debug.WriteLine("   - Included outline for story structure context");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("✏️  REGULAR EDITING MODE: Using full reference materials");
            }
            
            if ((systemTokens + conversationTokens) > 180000)
            {
                System.Diagnostics.Debug.WriteLine("⚠️  WARNING: Approaching 200K token response limit!");
                if (!isFullStoryMode)
                {
                    System.Diagnostics.Debug.WriteLine("💡 SUGGESTION: Try using 'entire story content' for reduced reference mode");
                }
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
            _characterProfiles = new List<string>();
            
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
                System.Diagnostics.Debug.WriteLine("Processing frontmatter references with cascade support");
                
                // NEW: Use cascade loading to get all references including those from outline
                var allReferences = await _fileReferenceService.LoadReferencesWithCascadeAsync(content, enableCascade: true);
                
                // Process all loaded references
                foreach (var reference in allReferences)
                {
                    switch (reference.Type)
                    {
                        case FileReferenceType.Style:
                            _styleGuide = reference.Content;
                            System.Diagnostics.Debug.WriteLine($"Loaded style guide via cascade: {reference.Content?.Length ?? 0} chars");
                            
                            // Parse the style guide for enhanced understanding
                            try
                            {
                                _parsedStyle = _styleParser.Parse(reference.Content);
                                System.Diagnostics.Debug.WriteLine($"Successfully parsed cascaded style guide: {_parsedStyle.Sections.Count} sections");
                            }
                            catch (Exception parseEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error parsing cascaded style guide: {parseEx.Message}");
                            }
                            break;
                            
                        case FileReferenceType.Rules:
                            _rules = reference.Content;
                            System.Diagnostics.Debug.WriteLine($"Loaded rules via cascade: {reference.Content?.Length ?? 0} chars");
                            
                            // Parse the rules for enhanced understanding
                            try
                            {
                                _parsedRules = _rulesParser.Parse(reference.Content);
                                System.Diagnostics.Debug.WriteLine($"Successfully parsed cascaded rules: {_parsedRules.Characters.Count} characters");
                            }
                            catch (Exception parseEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error parsing cascaded rules: {parseEx.Message}");
                            }
                            break;
                            
                        case FileReferenceType.Outline:
                            _outline = reference.Content;
                            System.Diagnostics.Debug.WriteLine($"Loaded outline via cascade: {reference.Content?.Length ?? 0} chars");
                            
                            // Parse outline for enhanced chapter context
                            _outlineChapterService.SetOutline(reference.Content);
                            break;
                            
                        case FileReferenceType.Character:
                            if (_characterProfiles == null)
                                _characterProfiles = new List<string>();
                            
                            // Strip frontmatter from character profiles to avoid muddying the content
                            string cleanedContent = StripFrontmatter(reference.Content);
                            _characterProfiles.Add(cleanedContent);
                            
                            var characterName = reference.GetCharacterName() ?? "Unknown Character";
                            System.Diagnostics.Debug.WriteLine($"Loaded character via cascade: {characterName} ({cleanedContent.Length} chars)");
                            break;
                    }
                }
                
                // No fallback - cascade either works or it doesn't
                System.Diagnostics.Debug.WriteLine("Cascade processing complete - no fallback processing");
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
            System.Diagnostics.Debug.WriteLine($"Character profiles: {(_characterProfiles?.Count ?? 0)} loaded ({(_characterProfiles?.Sum(c => c.Length) ?? 0)} total chars)");
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

        /// <summary>
        /// Process a character reference
        /// </summary>
        private async Task ProcessCharacterReference(string refPath, string refKey)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading character reference from: '{refPath}' (key: '{refKey}')");
                System.Diagnostics.Debug.WriteLine($"  Current file path: '{_currentFilePath}'");
                System.Diagnostics.Debug.WriteLine($"  Library path: '{_fileReferenceService?.GetType().GetField("_libraryPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(_fileReferenceService)}'");
                
                string characterContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(characterContent))
                {
                    // Strip frontmatter from character profiles to avoid muddying the content
                    string cleanedContent = StripFrontmatter(characterContent);
                    _characterProfiles.Add(cleanedContent);
                    
                    // Extract character name from key (e.g., "ref_character_derek" -> "Derek")
                    string characterName = "Unknown Character";
                    if (refKey.StartsWith("ref_character_") && refKey.Length > "ref_character_".Length)
                    {
                        characterName = refKey.Substring("ref_character_".Length);
                        // Capitalize first letter
                        if (!string.IsNullOrEmpty(characterName))
                        {
                            characterName = char.ToUpper(characterName[0]) + characterName.Substring(1);
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded character reference '{characterName}': {cleanedContent.Length} characters (frontmatter stripped)");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Character reference file was empty or could not be loaded: '{refPath}'");
                    
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
                System.Diagnostics.Debug.WriteLine($"Error loading character reference: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Strips frontmatter from content to provide clean character profile data
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
            
            // Check if this is a full story analysis request to determine content level
            bool needsFullStory = FULL_STORY_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            if (needsFullStory)
            {
                // REDUCED MODE FOR FULL STORY ANALYSIS
                // When sending full story content, only include essential references to stay within token limits
                prompt.AppendLine("");
                prompt.AppendLine("=== FULL STORY ANALYSIS MODE ===");
                prompt.AppendLine("You are analyzing the complete story content. Focus on overall narrative structure, consistency, and flow.");
                
                // Include only essential style guidance if available
                if (!string.IsNullOrEmpty(_styleGuide))
                {
                    // Extract just the core style principles (first ~500 chars) rather than the full guide
                    var truncatedStyle = _styleGuide.Length > 500 ? _styleGuide.Substring(0, 500) + "..." : _styleGuide;
                    prompt.AppendLine("\n=== CORE STYLE PRINCIPLES ===");
                    prompt.AppendLine(truncatedStyle);
                }
                
                // Include outline for story structure context
                if (!string.IsNullOrEmpty(_outline))
                {
                    prompt.AppendLine("\n=== STORY OUTLINE ===");
                    prompt.AppendLine("Use this outline to understand the intended story structure:");
                    prompt.AppendLine(_outline);
                }
                
                // Add basic frontmatter if available
                if (_frontmatter != null)
                {
                    prompt.AppendLine("\n=== DOCUMENT INFO ===");
                    if (_frontmatter.TryGetValue("title", out string title) && !string.IsNullOrEmpty(title))
                    {
                        prompt.AppendLine($"Title: {title}");
                    }
                    if (_frontmatter.TryGetValue("genre", out string genre) && !string.IsNullOrEmpty(genre))
                    {
                        prompt.AppendLine($"Genre: {genre}");
                    }
                }
                
                prompt.AppendLine("");
                prompt.AppendLine("=== FULL STORY ANALYSIS INSTRUCTIONS ===");
                prompt.AppendLine("When analyzing the full story:");
                prompt.AppendLine("- Focus on overall narrative structure and pacing");
                prompt.AppendLine("- Identify consistency issues across chapters");
                prompt.AppendLine("- Suggest high-level improvements for story flow");
                prompt.AppendLine("- Consider character development arcs throughout");
                prompt.AppendLine("- Note any plot holes or continuity issues");
                prompt.AppendLine("- Provide constructive feedback on the complete work");
            }
            else
            {
                // FULL MODE FOR REGULAR EDITING
                // Include all reference materials for detailed editing work
                
                // Always use raw style guide - avoid parsing issues that lose context
                if (!string.IsNullOrEmpty(_styleGuide))
                {
                    prompt.AppendLine("\n=== STYLE GUIDE ===");
                    prompt.AppendLine("The following is your comprehensive guide to the writing style with clear sections and headers:");
                    
                    // ENHANCEMENT: Add special handling for Writing Sample if parsed successfully
                    if (_parsedStyle?.WritingSample?.Length > 0)
                    {
                        prompt.AppendLine("\n** WRITING SAMPLE USAGE **");
                        prompt.AppendLine("CRITICAL: Your style guide contains a Writing Sample. When you encounter it:");
                        prompt.AppendLine("- EMULATE the technical style elements (voice, pacing, sentence structure, descriptive techniques)");
                        prompt.AppendLine("- NEVER copy characters, plot elements, settings, or specific content from the sample");
                        prompt.AppendLine("- ANALYZE the prose style and apply those techniques to your own original content");
                        prompt.AppendLine("");
                    }
                    
                    // ENHANCEMENT: Highlight critical rules if parsed successfully  
                    if (_parsedStyle?.CriticalRules?.Count > 0)
                    {
                        prompt.AppendLine("** CRITICAL STYLE REQUIREMENTS **");
                        prompt.AppendLine("These rules from your style guide MUST be followed without exception:");
                        foreach (var rule in _parsedStyle.CriticalRules.Take(5)) // Limit to avoid bloat
                        {
                            prompt.AppendLine($"- {rule}");
                        }
                        prompt.AppendLine("");
                    }
                    
                    // Present the full raw style guide (primary approach)
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

                // Add character profiles if available
                if (_characterProfiles?.Count > 0)
                {
                    prompt.AppendLine("\n=== CHARACTER PROFILES ===");
                    prompt.AppendLine("These are detailed character profiles for consistency in dialogue, behavior, and development:");
                    for (int i = 0; i < _characterProfiles.Count; i++)
                    {
                        prompt.AppendLine($"\n--- Character Profile {i + 1} ---");
                        prompt.AppendLine(_characterProfiles[i]);
                    }
                    prompt.AppendLine("\nUse these profiles to maintain character consistency in dialogue patterns, personality traits, relationships, and character development throughout the story.");
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
                        prompt.AppendLine("\n=== STORY OUTLINE: PLOT STRUCTURE & STORY BEATS ===");
                        prompt.AppendLine("This section contains the planned story structure including:");
                        prompt.AppendLine("- Plot points and story beats to achieve");
                        prompt.AppendLine("- Chapter summaries and major scenes");  
                        prompt.AppendLine("- Character arcs and development moments");
                        prompt.AppendLine("- Key events and dramatic moments");
                        prompt.AppendLine("- Relationship developments and conflicts");
                        prompt.AppendLine("\nUSE AS CREATIVE GOALS TO ACHIEVE, NOT TEXT TO EXPAND:");
                        prompt.AppendLine(_outline);
                        prompt.AppendLine("\nIMPORTANT: These are objectives and plot points - create original scenes that ACHIEVE these goals through fresh dialogue, action, and narrative.");
                    }
                }

                // Note: The actual story content will be provided in the CURRENT CONTENT section

                // Add detailed response format instructions for regular editing
                prompt.AppendLine("");
                prompt.AppendLine("=== CRITICAL OUTLINE USAGE INSTRUCTIONS ===");
                prompt.AppendLine("OUTLINE CONTENT DEFINITION: The outline contains PLOT OBJECTIVES and STORY GOALS, not finished prose.");
                prompt.AppendLine("You will see bullet points, chapter summaries, character beats, and plot events that need to happen.");
                prompt.AppendLine("");
                prompt.AppendLine("DO NOT:");
                prompt.AppendLine("- Copy outline bullet points into your prose");
                prompt.AppendLine("- Expand outline text directly into paragraphs");
                prompt.AppendLine("- Use outline phrasing as the basis for sentences");
                prompt.AppendLine("- Treat outline descriptions as content to elaborate or reword");
                prompt.AppendLine("");
                prompt.AppendLine("INSTEAD DO:");
                prompt.AppendLine("- Interpret outline points as DRAMATIC GOALS to achieve through original scenes");
                prompt.AppendLine("- Create completely new dialogue, action, and descriptions that fulfill the plot objectives");
                prompt.AppendLine("- Invent specific character interactions and detailed scenes that make the outlined events happen");
                prompt.AppendLine("- Transform plot summaries into vivid, engaging narrative scenes with your own fresh prose");
                prompt.AppendLine("");
                prompt.AppendLine("=== RESPONSE FORMAT (STRUCTURED JSON) ===");
                prompt.AppendLine("You MUST respond using structured JSON format for all editing operations.");
                prompt.AppendLine("This ensures precise, parseable output with no formatting ambiguity.");
                prompt.AppendLine("");
                prompt.AppendLine("You may optionally wrap the JSON in markdown code blocks for readability if you prefer:");
                prompt.AppendLine("");
                prompt.AppendLine("JSON SCHEMA:");
                prompt.AppendLine(@"{");
                prompt.AppendLine(@"  ""response_type"": ""text"" | ""edits"",");
                prompt.AppendLine(@"  ""text"": ""..."",          // Use when answering questions or providing analysis");
                prompt.AppendLine(@"  ""commentary"": ""..."",    // Optional: brief explanation before edits");
                prompt.AppendLine(@"  ""edits"": [");
                prompt.AppendLine(@"    {");
                prompt.AppendLine(@"      ""operation"": ""replace"" | ""insert"" | ""delete"" | ""generate"",");
                prompt.AppendLine(@"      ""original"": ""exact text to find"",     // For replace/delete");
                prompt.AppendLine(@"      ""changed"": ""new text"",                // For replace");
                prompt.AppendLine(@"      ""anchor"": ""text to insert after"",    // For insert");
                prompt.AppendLine(@"      ""new"": ""content to add/generate"",    // For insert/generate");
                prompt.AppendLine(@"      ""explanation"": ""optional reason""      // Optional");
                prompt.AppendLine(@"    }");
                prompt.AppendLine(@"  ]");
                prompt.AppendLine(@"}");
                prompt.AppendLine("");
                prompt.AppendLine("EXAMPLE RESPONSES:");
                prompt.AppendLine("");
                prompt.AppendLine("For answering questions or providing analysis:");
                prompt.AppendLine(@"{");
                prompt.AppendLine(@"  ""response_type"": ""text"",");
                prompt.AppendLine(@"  ""text"": ""Based on your style guide, consider showing John's emotional state through physical actions rather than internal monologue...""");
                prompt.AppendLine(@"}");
                prompt.AppendLine("");
                prompt.AppendLine("For making revisions:");
                prompt.AppendLine(@"{");
                prompt.AppendLine(@"  ""response_type"": ""edits"",");
                prompt.AppendLine(@"  ""commentary"": ""Tightening dialogue and improving character voice"",");
                prompt.AppendLine(@"  ""edits"": [");
                prompt.AppendLine(@"    {");
                prompt.AppendLine(@"      ""operation"": ""replace"",");
                prompt.AppendLine(@"      ""original"": ""He walked to the door slowly and opened it."",");
                prompt.AppendLine(@"      ""changed"": ""He strode to the door and yanked it open.""");
                prompt.AppendLine(@"    },");
                prompt.AppendLine(@"    {");
                prompt.AppendLine(@"      ""operation"": ""insert"",");
                prompt.AppendLine(@"      ""anchor"": ""He strode to the door and yanked it open."",");
                prompt.AppendLine(@"      ""new"": ""The hinges screamed in protest, echoing his frustration.""");
                prompt.AppendLine(@"    },");
                prompt.AppendLine(@"    {");
                prompt.AppendLine(@"      ""operation"": ""delete"",");
                prompt.AppendLine(@"      ""original"": ""This sentence is redundant and should be removed.""");
                prompt.AppendLine(@"    }");
                prompt.AppendLine(@"  ]");
                prompt.AppendLine(@"}");
                prompt.AppendLine("");
                prompt.AppendLine("For generating new content:");
                prompt.AppendLine(@"{");
                prompt.AppendLine(@"  ""response_type"": ""edits"",");
                prompt.AppendLine(@"  ""edits"": [");
                prompt.AppendLine(@"    {");
                prompt.AppendLine(@"      ""operation"": ""generate"",");
                prompt.AppendLine(@"      ""new"": ""## Chapter 7\n\nJohn stepped into the dimly lit warehouse...""");
                prompt.AppendLine(@"    }");
                prompt.AppendLine(@"  ]");
                prompt.AppendLine(@"}");
                prompt.AppendLine("");
                prompt.AppendLine("CRITICAL SCOPE ANALYSIS - Before selecting original text, always ask:");
                prompt.AppendLine("- Does this change affect character mood, tone, or emotional state in subsequent sentences?");
                prompt.AppendLine("- Would the next paragraph become inconsistent or awkward after this revision?");
                prompt.AppendLine("- Is this part of a larger dialogue exchange, action sequence, or emotional moment?");
                prompt.AppendLine("- Does the revision logic continue beyond the obvious problem text?");
                prompt.AppendLine("- Are there related sentences that reference or build on the content being revised?");
                prompt.AppendLine("");
                prompt.AppendLine("COMPLETE REVISION UNITS - When selecting original text, include ALL affected content:");
                prompt.AppendLine("- Complete dialogue exchanges if tone or speaker attitude changes");
                prompt.AppendLine("- Full action sequences that share the same emotional or physical momentum");
                prompt.AppendLine("- Any subsequent sentences that would sound disconnected after the revision");
                prompt.AppendLine("- Related paragraphs that reference or continue the revised content's logic");
                prompt.AppendLine("- Emotional transitions that span multiple sentences or paragraphs");
                prompt.AppendLine("");
                prompt.AppendLine("SCOPE EXAMPLES:");
                prompt.AppendLine("BAD - Incomplete scope: Only revises \"John slammed the door angrily\" but leaves \"He smiled warmly at Sarah\" creating emotional inconsistency");
                prompt.AppendLine("GOOD - Complete scope: Revises both sentences together to maintain emotional continuity throughout the character's actions");
                prompt.AppendLine("");
                prompt.AppendLine("CRITICAL REVISION REQUIREMENTS:");
                prompt.AppendLine("- Original text and Insert after must ONLY come from CURRENT CONTENT section");
                prompt.AppendLine("- NEVER suggest changes to text from: Outline, Rules, Character Profiles, or Style Guide");
                prompt.AppendLine("- Reference materials are for GUIDANCE only - they are NOT content to be edited");
                prompt.AppendLine("- Only the actual story narrative in CURRENT CONTENT should be revised or have insertions");
                prompt.AppendLine("- If you see outline text, rules text, or character profile text - these are READ-ONLY references");
                prompt.AppendLine("");
                prompt.AppendLine("CRITICAL TEXT PRECISION REQUIREMENTS:");
                prompt.AppendLine("⚠️ WARNING: The 'original' and 'anchor' fields MUST be PERFECT, CHARACTER-FOR-CHARACTER matches.");
                prompt.AppendLine("The system uses EXACT TEXT MATCHING. Even a single character difference will cause failure.");
                prompt.AppendLine("");
                prompt.AppendLine("MANDATORY RULES FOR 'original' and 'anchor' fields:");
                prompt.AppendLine("1. EXACT COPY: Copy the text EXACTLY as it appears - byte-for-byte identical");
                prompt.AppendLine("2. NO CORRECTIONS: Do NOT fix typos, grammar, or punctuation in the original text");
                prompt.AppendLine("3. PRESERVE JSON: Use \\n for line breaks, \\\" for quotes within strings");
                prompt.AppendLine("4. NO NORMALIZATION: Keep inconsistent spacing, double spaces, odd breaks as-is");
                prompt.AppendLine("5. NO PARAPHRASING: Do NOT rewrite or summarize - copy verbatim only");
                prompt.AppendLine("6. COMPLETE UNITS: Include complete sentences ending with proper punctuation");
                prompt.AppendLine("7. SUFFICIENT LENGTH: Include 20-50+ words for unique identification");
                prompt.AppendLine("8. VALID JSON: Ensure all JSON is properly formatted and escapable");
                prompt.AppendLine("");
                prompt.AppendLine("COMMON JSON FORMATTING MISTAKES TO AVOID:");
                prompt.AppendLine("❌ BAD: Fixing typos in original (\"he walked\" when content has \"he walkd\")");
                prompt.AppendLine("✅ GOOD: Copy typos exactly (\"he walkd\" even though it's wrong)");
                prompt.AppendLine("");
                prompt.AppendLine("❌ BAD: Unescaped quotes (\"He said \"hello\" to her\")");
                prompt.AppendLine("✅ GOOD: Escaped quotes (\"He said \\\"hello\\\" to her\")");
                prompt.AppendLine("");
                prompt.AppendLine("❌ BAD: Literal line breaks in JSON string");
                prompt.AppendLine("✅ GOOD: Escaped line breaks (\"First line\\nSecond line\")");
                prompt.AppendLine("");
                prompt.AppendLine("❌ BAD: Partial sentence (\"John walked to the\")");
                prompt.AppendLine("✅ GOOD: Complete sentence (\"John walked to the door and opened it.\")");
                prompt.AppendLine("");
                prompt.AppendLine("TEXT MATCHING VALIDATION:");
                prompt.AppendLine("- Mentally perform CTRL+F search - would your text be found exactly?");
                prompt.AppendLine("- If you had to retype it from memory, you're doing it wrong - you must copy");
                prompt.AppendLine("- Every character matters: spaces, punctuation, capitalization, line breaks");
                prompt.AppendLine("- If match fails, user cannot apply the revision and must manually edit");
                prompt.AppendLine("");
                prompt.AppendLine("⚠️ JSON FORMATTING REQUIREMENTS:");
                prompt.AppendLine("- ALWAYS output valid, parseable JSON");
                prompt.AppendLine("- Properly escape quotes and newlines within JSON strings");
                prompt.AppendLine("- For new chapters, use: {\"response_type\": \"edits\", \"edits\": [{\"operation\": \"generate\", \"new\": \"## Chapter 7\\n\\nContent...\"}]}");
                prompt.AppendLine("- Chapter headers MUST use ## (double hashtags), never single #");
                prompt.AppendLine("- If responding with multiple operations, put them all in the same \"edits\" array");
                prompt.AppendLine("=== RESPONSE APPROACH FOR QUESTIONS ===");
                prompt.AppendLine("TARGETED QUESTIONS: For specific, direct questions (e.g., 'How should this character react?', 'What's wrong with this dialogue?', 'Should I add more description here?'):");
                prompt.AppendLine("- Provide a focused, direct answer without comprehensive story analysis");
                prompt.AppendLine("- Address exactly what the user asked");
                prompt.AppendLine("- Reference relevant style guide or rules points if helpful");
                prompt.AppendLine("- Keep response concise and actionable");
                prompt.AppendLine("- No need for full story structure or thematic analysis unless specifically requested");
                prompt.AppendLine("");
                prompt.AppendLine("COMPREHENSIVE ANALYSIS: Provide thorough, detailed analysis when user requests:");
                prompt.AppendLine("- Full story analysis: 'Analyze entire story', 'Overall story structure', 'Complete story review'");
                prompt.AppendLine("- Chapter assessment: 'How does Chapter X look?', 'What do you think of this chapter?', 'Is this chapter working?'");
                prompt.AppendLine("- Scene evaluation: 'How's this scene?', 'Does this work?', 'Feedback on this part'");
                prompt.AppendLine("- Or when the user clearly asks for evaluation, assessment, or detailed feedback");
                prompt.AppendLine("- For these requests, provide thorough analysis with specific examples and actionable suggestions");
                prompt.AppendLine("");
                prompt.AppendLine("When providing analysis or answering questions:");
                prompt.AppendLine("- For targeted questions: Be direct and concise");
                prompt.AppendLine("- For assessment/evaluation requests: Be thorough and detailed");
                prompt.AppendLine("- Always point to specific parts of materials (Style Guide, Rules, Outline, Current Content) if relevant");
                prompt.AppendLine("");
                prompt.AppendLine("REVISION FOCUS: When providing revisions or insertions, be concise:");
                prompt.AppendLine("- Provide the revision/insertion blocks with minimal explanation");
                prompt.AppendLine("- Only add commentary if the user specifically asks for reasoning or analysis");
                prompt.AppendLine("- Focus on the changes themselves, not lengthy explanations");
                prompt.AppendLine("- If multiple revisions are needed, provide them cleanly in sequence");
                prompt.AppendLine("- Let the revised text speak for itself");
                prompt.AppendLine("");
                prompt.AppendLine("When generating new content:");
                prompt.AppendLine("- Follow your style guide completely - it contains all writing guidance you need");
                prompt.AppendLine("- Write original narrative prose, never expand or copy outline text");
                prompt.AppendLine("- For new chapters, ALWAYS use the format: \"## Chapter [number]\" (e.g., \"## Chapter 1\", \"## Chapter 2\")");
                prompt.AppendLine("- NEVER use single # for chapter headers - this breaks navigation and parsing");
                prompt.AppendLine("");
                prompt.AppendLine("=== ORIGINALITY & PLAGIARISM AVOIDANCE ===");
                prompt.AppendLine("CRITICAL: Create entirely original fiction prose that avoids plagiarism from EXTERNAL published works:");
                prompt.AppendLine("- Generate fresh, unique narrative voice and storytelling approach");
                prompt.AppendLine("- Avoid copying or closely mimicking scenes, dialogue, or plot elements from published books, movies, or other media");
                prompt.AppendLine("- Create original character interactions, descriptions, and narrative sequences");
                prompt.AppendLine("- Develop unique phrasing, metaphors, and stylistic elements");
                prompt.AppendLine("- Draw inspiration from story structure and themes without copying specific content from external sources");
                prompt.AppendLine("- Ensure all dialogue, action sequences, and descriptions are your own original creation");
                prompt.AppendLine("");
                prompt.AppendLine("IMPORTANT: ALWAYS use your PROJECT REFERENCE MATERIALS (Style Guide, Rules, Character Profiles, Outline):");
                prompt.AppendLine("- Character Profiles: Use these to maintain consistency in dialogue patterns, personality traits, and relationships");
                prompt.AppendLine("- Rules: Follow universe facts, character abilities, and world-building constraints");
                prompt.AppendLine("- Style Guide: Adhere to the established writing style and narrative approach");
                prompt.AppendLine("- Outline: Achieve the planned story beats and plot objectives through original scenes");
                prompt.AppendLine("- Reference materials are for GUIDANCE and CONSISTENCY, not content to avoid");
                prompt.AppendLine("");
                prompt.AppendLine("- Treat outline points as EVENTS/OCCURRENCES to write about, not content to elaborate");
                prompt.AppendLine("- Create scenes showing these events happening through original dialogue, actions, and narrative");
            }

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
                        
                        // Process the response for potential chapter follow-up questions
                        var processedResponse = _chapterFollowUpService.ProcessResponse(response, request);
                        
                        // Add the original response to memory (not the processed one with follow-up)
                        AddAssistantMessage(response);
                        
                        return processedResponse;
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
                
                // Check if this is full story mode to avoid adding chapter-specific instructions
                bool needsFullStory = FULL_STORY_KEYWORDS.Any(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                prompt.AppendLine("\n=== CURRENT CONTENT ===");
                prompt.AppendLine("This is the STORY NARRATIVE you're working with - this is the ONLY editable content.");
                prompt.AppendLine("");
                
                // Add explicit editing workspace boundaries for chapter-based editing
                if (!needsFullStory)
                {
                    prompt.AppendLine("🚨 CRITICAL: EDITING WORKSPACE BOUNDARIES 🚨");
                    prompt.AppendLine("");
                    prompt.AppendLine("WHAT YOU CAN EDIT:");
                    prompt.AppendLine("✅ CURRENT CHAPTER section ONLY - This is your editing workspace");
                    prompt.AppendLine("✅ The actual story prose, dialogue, descriptions, and narrative");
                    prompt.AppendLine("");
                    prompt.AppendLine("WHAT YOU CANNOT EDIT (READ-ONLY REFERENCE MATERIALS):");
                    prompt.AppendLine("❌ Style Guide (in system message) - These are writing rules, NOT story content");
                    prompt.AppendLine("❌ Core Rules (in system message) - Universe facts, NOT story content");
                    prompt.AppendLine("❌ Character Profiles (in system message) - Reference info, NOT story content");
                    prompt.AppendLine("❌ Outline (in system message) - Plot structure, NOT story content");
                    prompt.AppendLine("❌ PREVIOUS CHAPTER sections (below) - Context only, already written");
                    prompt.AppendLine("❌ NEXT CHAPTER sections (below) - Context only, already written");
                    prompt.AppendLine("");
                    prompt.AppendLine("⚠️ VERIFICATION BEFORE EACH EDIT:");
                    prompt.AppendLine("Before selecting 'original' or 'anchor' text, ask yourself:");
                    prompt.AppendLine("1. Is this text from the === CURRENT CHAPTER === section? (If no, STOP)");
                    prompt.AppendLine("2. Is this story narrative prose, dialogue, or description? (If no, STOP)");
                    prompt.AppendLine("3. Is this from outline, rules, style guide, or character profiles? (If yes, STOP)");
                    prompt.AppendLine("");
                    prompt.AppendLine("If you want to suggest outline changes, respond with 'response_type: text' and explain.");
                    prompt.AppendLine("NEVER use 'original' text from outline, rules, profiles, or other chapters.");
                    prompt.AppendLine("");
                }
                
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
                
            // BULLY FIX: Add debug logging for keyword detection
            if (needsFullStory)
            {
                var matchedKeyword = FULL_STORY_KEYWORDS.First(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                System.Diagnostics.Debug.WriteLine($"🔍 FULL STORY MODE triggered by keyword: '{matchedKeyword}' in request: '{request.Substring(0, Math.Min(100, request.Length))}...'");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"✏️  CHAPTER MODE: No full story keywords detected in request: '{request.Substring(0, Math.Min(100, request.Length))}...'");
            }

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
                    System.Diagnostics.Debug.WriteLine($"🔍 FULL STORY MODE: Returning complete story content for analysis. Triggered by keyword in request: '{request}'");
                    System.Diagnostics.Debug.WriteLine($"   Story length: {content.Length} characters");
                    System.Diagnostics.Debug.WriteLine($"   Using reduced reference materials in system prompt to prevent 400 errors");
                    
                    // BULLY FIX: Add token safety check to prevent 400 errors
                    int estimatedTokens = EstimateTokenCount(content);
                    if (estimatedTokens > 180000) // Conservative limit to prevent overflow
                    {
                        System.Diagnostics.Debug.WriteLine($"⚠️  WARNING: Full story content ({estimatedTokens} tokens) may cause token overflow!");
                        System.Diagnostics.Debug.WriteLine($"   Falling back to chapter-based analysis to prevent 400 errors");
                        
                        // Fall through to chapter-based analysis instead of risking token overflow
                        needsFullStory = false;
                    }
                    else
                    {
                        return content;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"📄 SMALL FILE: Returning full story content because it's small ({content.Length} characters < 2000)");
                    return content;
                }
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

            // Add up to two previous chapters, if they exist
            int numPreviousToInclude = Math.Min(2, currentChapterIndex);
            if (numPreviousToInclude > 0)
            {
                // Add marker if there are more chapters prior to the ones we'll include
                if (currentChapterIndex > numPreviousToInclude)
                {
                    result.AppendLine("... (Previous content omitted) ...");
                }

                // Include in chronological order: farthest first, then nearest
                for (int offset = numPreviousToInclude; offset >= 1; offset--)
                {
                    var prevIndex = currentChapterIndex - offset;
                    var prevStart = chapterBoundaries[prevIndex];
                    var prevEnd = chapterBoundaries[prevIndex + 1] - 1;

                    if (prevEnd >= prevStart)
                    {
                        var prevContent = string.Join("\n",
                            lines.Skip(prevStart)
                                 .Take(prevEnd - prevStart + 1));

                        if (!string.IsNullOrEmpty(prevContent))
                        {
                            result.AppendLine("=== PREVIOUS CHAPTER ===");
                            result.AppendLine(prevContent);
                            System.Diagnostics.Debug.WriteLine($"Added previous chapter index {prevIndex} (lines {prevStart}-{prevEnd})");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Previous chapter index {prevIndex} was empty");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid previous chapter range: {prevStart}-{prevEnd}");
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
            
            // Exclude the next chapter boundary from current chapter (fix header duplication)
            var chapterEnd = Math.Min(chapterBoundaries[currentChapterIndex + 1] - 1, lines.Length - 1);
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
                result.AppendLine(currentChapterContent);
                System.Diagnostics.Debug.WriteLine($"✅ Added complete current chapter (no cursor marker included in output)");
            }
            else
            {
                result.AppendLine("\n=== CURRENT CHAPTER ===");
                System.Diagnostics.Debug.WriteLine("Current chapter was empty");
            }

            // Add up to one subsequent chapter, if it exists
            int maxNextAvailable = (chapterBoundaries.Count - 2) - currentChapterIndex; // number of chapters after current
            int numNextToInclude = Math.Min(1, maxNextAvailable);
            if (numNextToInclude > 0)
            {
                for (int offset = 1; offset <= numNextToInclude; offset++)
                {
                    var nextStart = chapterBoundaries[currentChapterIndex + offset];
                    var nextEnd = chapterBoundaries[currentChapterIndex + offset + 1] - 1;

                    System.Diagnostics.Debug.WriteLine($"Next chapter offset {offset} range: lines {nextStart} to {nextEnd}");

                    if (nextEnd >= nextStart)
                    {
                        var nextContent = string.Join("\n",
                            lines.Skip(nextStart)
                                 .Take(nextEnd - nextStart + 1));

                        if (!string.IsNullOrEmpty(nextContent))
                        {
                            result.AppendLine("\n=== NEXT CHAPTER ===");
                            result.AppendLine(nextContent);
                            System.Diagnostics.Debug.WriteLine($"✅ Added next chapter offset {offset} (lines {nextStart}-{nextEnd})");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"❌ Next chapter offset {offset} was empty");
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"❌ Invalid next chapter range for offset {offset}: {nextStart} to {nextEnd}");
                    }
                }

                // Add marker if there are more chapters after the ones included
                if (maxNextAvailable > numNextToInclude)
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