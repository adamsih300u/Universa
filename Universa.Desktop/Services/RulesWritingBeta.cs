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
    /// <summary>
    /// Specialized service for creating and managing Rules files that define literary universes,
    /// character profiles, series synopses, and worldbuilding elements
    /// </summary>
    public class RulesWritingBeta : BaseLangChainService
    {
        private string _rulesContent;
        private string _styleGuide;
        private readonly FileReferenceService _fileReferenceService;
        private static Dictionary<string, RulesWritingBeta> _instances = new Dictionary<string, RulesWritingBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MESSAGE_HISTORY_LIMIT = 6;  // Keep last 3 exchanges
        private const int REFRESH_INTERVAL = 2;  // Refresh core materials every 2 messages
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

        // Enhanced rules parsing with three-tier filtering
        private readonly RulesParser _rulesParser = new RulesParser();
        private RulesParser.ParsedRules _parsedRules;

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        // Keywords that indicate universe development requests
        private static readonly string[] UNIVERSE_DEVELOPMENT_KEYWORDS = new[]
        {
            "develop universe", "expand universe", "create universe", "build universe",
            "world building", "worldbuilding", "universe rules", "universe structure",
            "character development", "character profile", "character arc", "character relationship",
            "series synopsis", "book synopsis", "timeline", "chronology",
            "antagonist organization", "supporting characters", "character network"
        };

        // Keywords that indicate character-focused requests
        private static readonly string[] CHARACTER_KEYWORDS = new[]
        {
            "character", "protagonist", "antagonist", "villain", "hero", "supporting character",
            "character arc", "character development", "character profile", "character background",
            "character relationship", "character progression", "personality", "motivation"
        };

        public RulesWritingBeta(string apiKey, string model, AIProvider provider, string filePath = null, string libraryPath = null)
            : base(apiKey, model, provider)
        {
            _currentFilePath = filePath;
            _currentProvider = provider;
            _currentModel = model;
            
            if (!string.IsNullOrEmpty(libraryPath))
            {
                _fileReferenceService = new FileReferenceService(libraryPath);
            }
        }

        public static async Task<RulesWritingBeta> GetInstance(string apiKey, string model, AIProvider provider, string filePath, string libraryPath)
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
                RulesWritingBeta instance;
                
                // IMPORTANT: Always force a new instance to ensure correct library path and avoid disposed object errors
                // The static cache was causing issues when switching between files or when instances got disposed
                System.Diagnostics.Debug.WriteLine($"Creating new RulesWritingBeta instance for file: {filePath} with library path: {libraryPath}");
                instance = new RulesWritingBeta(apiKey, model, provider, filePath, libraryPath);
                
                // Update the cache
                _instances[filePath ?? "default"] = instance;
                
                return instance;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RulesWritingBeta.GetInstance: {ex}");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateContentAndInitialize(string content)
        {
            await _semaphore.WaitAsync();
            try
            {
                _rulesContent = content;
                await LoadReferenceMaterials();
                InitializeSystemMessage();
                _needsRefresh = false;
                _messagesSinceRefresh = 0;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        private async Task LoadReferenceMaterials()
        {
            if (_fileReferenceService == null) return;

            // Parse frontmatter from current content
            if (!string.IsNullOrEmpty(_rulesContent))
            {
                _frontmatter = ExtractFrontmatter(_rulesContent);
                
                // Parse rules content using enhanced parser
                try
                {
                    _parsedRules = _rulesParser.Parse(_rulesContent);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed rules content with {_parsedRules.Core.CoreCharacters.Count} core characters and {_parsedRules.Arcs.Count} arcs");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error parsing rules content: {ex.Message}");
                    _parsedRules = null;
                }
            }

            // Load style guide if referenced
            try
            {
                if (_frontmatter?.TryGetValue("ref style", out string styleRef) == true || 
                    _frontmatter?.TryGetValue("style", out styleRef) == true)
                {
                    _styleGuide = await _fileReferenceService.GetFileContent(styleRef, _currentFilePath);
                    System.Diagnostics.Debug.WriteLine($"Loaded style guide: {styleRef}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading style reference: {ex.Message}");
            }
        }

        private Dictionary<string, string> ExtractFrontmatter(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            if (string.IsNullOrEmpty(content)) return frontmatter;
            
            var lines = content.Split('\n');
            bool inFrontmatter = false;
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                
                if (trimmed == "---")
                {
                    if (!inFrontmatter)
                    {
                        inFrontmatter = true;
                        continue;
                    }
                    else
                    {
                        break; // End of frontmatter
                    }
                }
                
                if (inFrontmatter && trimmed.Contains(':'))
                {
                    var parts = trimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim();
                        frontmatter[key] = value;
                    }
                }
            }
            
            return frontmatter;
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = BuildRulesPrompt("");
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                systemMessage.Content = systemPrompt;
            }
            else
            {
                _memory.Insert(0, new MemoryMessage("system", systemPrompt, _model));
            }
        }

        private string BuildRulesPrompt(string request)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are a master universe architect specialized in creating comprehensive Rules files that define literary universes, character development, and series continuity. Your expertise lies in developing rich, consistent worldbuilding documentation that serves as the foundation for multi-book series.");
            
            // Add current date and time context
            prompt.AppendLine("");
            prompt.AppendLine("=== CURRENT DATE AND TIME ===");
            prompt.AppendLine($"Current Date/Time: {DateTime.Now:F}");
            prompt.AppendLine($"Local Time Zone: {TimeZoneInfo.Local.DisplayName}");
            
            // Add style guide if available
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine("\n=== STYLE GUIDE ===");
                prompt.AppendLine("Apply this style approach to your universe development suggestions:");
                prompt.AppendLine(_styleGuide);
            }

            // Add title and author information if available
            if (_frontmatter != null)
            {
                prompt.AppendLine("\n=== UNIVERSE METADATA ===");
                
                if (_frontmatter.TryGetValue("title", out string title) && !string.IsNullOrEmpty(title))
                {
                    prompt.AppendLine($"Universe Title: {title}");
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

            // Add current rules content (filtered for context)
            if (!string.IsNullOrEmpty(_rulesContent))
            {
                prompt.AppendLine("\n=== CURRENT RULES CONTENT ===");
                prompt.AppendLine("This is the current universe documentation being developed. Build upon and enhance this content:");
                
                // Use filtered content if parsing was successful, otherwise fall back to raw content
                var filteredContent = GetContextuallyFilteredRulesContent(request);
                if (!string.IsNullOrEmpty(filteredContent))
                {
                    prompt.AppendLine(filteredContent);
                    System.Diagnostics.Debug.WriteLine("Using filtered rules content for enhanced context");
                }
                else
                {
                    prompt.AppendLine(_rulesContent);
                    System.Diagnostics.Debug.WriteLine("Using raw rules content as fallback");
                }
            }

            // Add comprehensive rules development guidance
            prompt.AppendLine(@"

=== RULES FILE DEVELOPMENT GUIDANCE ===
Your role is to help create comprehensive universe documentation with the following structure:

**[Background]**
- Universe setup and foundational context
- Setting information and world parameters  
- Core premise and universe rules
- Historical context or timeline framework

**[Series Synopsis]**
- Book-by-book progression showing character and plot development
- Character introductions and their roles in each book
- Inter-book connections and continuity
- Timeline progression and character aging
- Major plot threads that span multiple books

**[Character Profiles]**
- Comprehensive character development with full arcs
- Physical descriptions, personality traits, and motivations
- Skills, abilities, and areas of expertise
- Personal relationships and dynamics with other characters
- Character progression through the series timeline
- Background, education, and formative experiences
- Internal conflicts and psychological complexity
- Distinctive qualities and recurring themes

**Supporting Character Profiles**
- Brief but detailed descriptions of secondary characters
- Roles in the overall narrative
- Connections to main characters
- Specific story functions

=== CHARACTER DEVELOPMENT EXCELLENCE ===
When developing character profiles, include:

**Core Characteristics**
- Age progression throughout series
- Physical appearance with specific details
- Personality traits and psychological makeup
- Health considerations and physical condition

**Background & Education**
- Educational background and formative experiences
- Professional history and career development
- Family background and personal history
- Key life events that shaped the character

**Skills & Abilities** 
- Professional competencies and expertise
- Physical and mental capabilities
- Special skills or talents
- Areas of weakness or limitation

**Relationships**
- Connections with other main characters
- Evolution of relationships over time
- Professional and personal dynamics
- Romantic relationships and their complexity

**Character Arc**
- Growth and development throughout the series
- Internal conflicts and resolution
- Professional evolution and changes
- Physical and emotional journey through time

**Distinctive Qualities**
- Unique mannerisms, habits, or preferences
- Memorable physical traits or accessories
- Speech patterns or characteristic behaviors
- Personal rituals or meaningful objects

=== SERIES CONTINUITY MANAGEMENT ===
For series synopsis entries:
- Track character appearances across books
- Note character development between books
- Maintain timeline consistency
- Show how events in one book affect later books
- Include character ages and progression
- Reference previous events appropriately

=== RESPONSE FORMAT INSTRUCTIONS ===
When suggesting specific content changes, you MUST use EXACTLY this format:

```
Original text:
[paste the exact text to be replaced]
```

```
Changed to:
[your new enhanced version]
```

For new content additions (expanding profiles, adding characters, etc.), provide the content directly.

=== CRITICAL DEVELOPMENT PRINCIPLES ===
1. **Consistency**: Ensure all character details align across the universe
2. **Depth**: Create multi-dimensional characters with complex motivations
3. **Timeline Integrity**: Maintain accurate chronological progression
4. **Relationship Networks**: Show how characters interconnect and influence each other
5. **Series Evolution**: Track how characters change and grow throughout multiple books
6. **Distinctive Voice**: Each character should have unique personality and traits
7. **Comprehensive Documentation**: Provide enough detail for consistent usage across multiple stories

=== UNIVERSE BUILDING FOCUS AREAS ===
- Character psychology and motivation
- Inter-character relationships and dynamics  
- Timeline management and character aging
- Skills and abilities development over time
- Background institutions and organizations
- Supporting character ecosystems
- Character-specific themes and arcs
- Physical and emotional character evolution");

            return prompt.ToString();
        }

        private string BuildContextPrompt(string request)
        {
            var prompt = new StringBuilder();
            
            // Determine request type for focused assistance
            bool isUniverseDevRequest = UNIVERSE_DEVELOPMENT_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            bool isCharacterFocused = CHARACTER_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            
            if (isUniverseDevRequest)
            {
                prompt.AppendLine("=== UNIVERSE DEVELOPMENT REQUEST ===");
                prompt.AppendLine("Focus on comprehensive universe building including:");
                prompt.AppendLine("- World consistency and logical framework");
                prompt.AppendLine("- Character network development");
                prompt.AppendLine("- Timeline and continuity management");
                prompt.AppendLine("- Series-wide plot thread tracking");
                prompt.AppendLine("");
            }
            else if (isCharacterFocused)
            {
                prompt.AppendLine("=== CHARACTER DEVELOPMENT REQUEST ===");
                prompt.AppendLine("Focus on rich character development including:");
                prompt.AppendLine("- Psychological depth and complexity");
                prompt.AppendLine("- Character relationships and dynamics");
                prompt.AppendLine("- Growth arcs throughout the series");
                prompt.AppendLine("- Distinctive personality traits and mannerisms");
                prompt.AppendLine("- Professional and personal background");
                prompt.AppendLine("");
            }
            
            prompt.AppendLine($"=== USER REQUEST ===");
            prompt.AppendLine(request);
            
            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                // Check if we need to refresh core materials
                _messagesSinceRefresh++;
                
                if (_messagesSinceRefresh >= REFRESH_INTERVAL || _needsRefresh)
                {
                    _needsRefresh = true;
                }

                // If content has changed or refresh is needed, update
                if (_rulesContent != content || _needsRefresh)
                {
                    await UpdateContentAndInitialize(content);
                }

                // Build the context prompt
                var contextPrompt = BuildContextPrompt(request);
                
                // Add the user request to memory
                AddUserMessage(contextPrompt);
                
                // Trim old conversation if needed
                TrimOldConversationIfNeeded();

                // Initialize retry count
                int retryCount = 0;
                const int maxRetries = 5;
                const int baseDelay = 2000;

                while (true)
                {
                    try
                    {
                        var response = await ExecutePrompt("");
                        
                        // Add assistant response to memory
                        AddAssistantMessage(response);
                        
                        return response;
                    }
                    catch (Exception ex) when (retryCount < maxRetries)
                    {
                        retryCount++;
                        var delay = baseDelay * (int)Math.Pow(2, retryCount - 1);
                        
                        System.Diagnostics.Debug.WriteLine($"RulesWritingBeta API call failed (attempt {retryCount}/{maxRetries}): {ex.Message}");
                        
                        if (ex.Message.Contains("overloaded") || ex.Message.Contains("rate limit"))
                        {
                            System.Diagnostics.Debug.WriteLine($"Retrying after {delay}ms due to rate limiting...");
                            await Task.Delay(delay);
                            continue;
                        }
                        
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RulesWritingBeta.ProcessRequest: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void TrimOldConversationIfNeeded()
        {
            var nonSystemMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
            
            if (nonSystemMessages.Count > MESSAGE_HISTORY_LIMIT)
            {
                var messagesToRemove = nonSystemMessages.Count - MESSAGE_HISTORY_LIMIT;
                var systemMessages = _memory.Where(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)).ToList();
                var messagesToKeep = nonSystemMessages.Skip(messagesToRemove).ToList();
                
                _memory.Clear();
                _memory.AddRange(systemMessages);
                _memory.AddRange(messagesToKeep);
                
                System.Diagnostics.Debug.WriteLine($"Trimmed {messagesToRemove} old messages from conversation history");
            }
        }

        public void UpdateCursorPosition(int position)
        {
            _currentCursorPosition = position;
            System.Diagnostics.Debug.WriteLine($"RulesWritingBeta: Cursor position updated to: {position}");
        }

        public void SetCurrentFilePath(string filePath)
        {
            _currentFilePath = filePath;
            System.Diagnostics.Debug.WriteLine($"RulesWritingBeta: File path updated to: {filePath}");
        }

        /// <summary>
        /// Gets contextually filtered rules content based on current request and context
        /// </summary>
        private string GetContextuallyFilteredRulesContent(string request)
        {
            // If parsing failed, return empty string to trigger fallback
            if (_parsedRules == null)
            {
                System.Diagnostics.Debug.WriteLine("No parsed rules available for filtering");
                return string.Empty;
            }

            try
            {
                // Use the enhanced parser's filtering method
                var filteredContent = _rulesParser.GetFilteredRulesContent(_parsedRules, request, _currentFilePath);
                
                System.Diagnostics.Debug.WriteLine($"Generated filtered rules content: {filteredContent.Length} characters");
                
                return filteredContent;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating filtered rules content: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Gets the current context information for debugging and integration
        /// </summary>
        public string GetCurrentContextInfo()
        {
            if (_parsedRules == null)
                return "No parsed rules available";

            var info = new StringBuilder();
            info.AppendLine($"Core Characters: {_parsedRules.Core.CoreCharacters.Count}");
            info.AppendLine($"Series Arcs: {_parsedRules.Arcs.Count}");
            info.AppendLine($"Book Details: {_parsedRules.BookDetails.Count}");
            info.AppendLine($"Total Books in Timeline: {_parsedRules.Timeline.Books.Count}");

            return info.ToString();
        }

        /// <summary>
        /// Validates content against the parsed rules for consistency
        /// </summary>
        public List<string> ValidateContentAgainstRules(string content)
        {
            var validationErrors = new List<string>();

            if (_parsedRules == null)
            {
                validationErrors.Add("No parsed rules available for validation");
                return validationErrors;
            }

            try
            {
                // Character consistency checks
                foreach (var character in _parsedRules.Characters.Values)
                {
                    if (content.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check for character death consistency
                        if (!string.IsNullOrEmpty(character.Fate) && 
                            character.Fate.Contains("dies", StringComparison.OrdinalIgnoreCase))
                        {
                            validationErrors.Add($"WARNING: {character.Name} may be deceased according to rules");
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"Validated content against rules: {validationErrors.Count} potential issues found");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during rules validation: {ex.Message}");
                validationErrors.Add($"Validation error: {ex.Message}");
            }

            return validationErrors;
        }

        public static void ClearInstance(string filePath)
        {
            lock (_lock)
            {
                _instances.Remove(filePath ?? "default");
            }
        }

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instances.Clear();
            }
        }

        // Event for retry notifications
        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;
    }
} 