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
        private List<string> _characterProfiles;
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
            "magic system", "technology rules", "social structure", "cultural norms",
            "series synopsis", "book synopsis", "timeline", "chronology",
            "organizations", "institutions", "power dynamics", "universe constraints"
        };

        // Keywords that indicate character-related universe rules requests
        private static readonly string[] CHARACTER_KEYWORDS = new[]
        {
            "character", "protagonist", "antagonist", "villain", "hero", "supporting character",
            "character abilities", "character limitations", "character constraints", "character rules",
            "character relationships", "character hierarchy", "character affiliations", "character background"
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
                
                // Parse rules content for debug/validation purposes only (not used for filtering)
                try
                {
                    _parsedRules = _rulesParser.Parse(_rulesContent);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed rules content for validation purposes");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Rules parsing failed (not critical since we use raw content): {ex.Message}");
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

            // NEW: Load character profiles if referenced
            try
            {
                var characterRefs = _frontmatter?
                    .Where(kvp => kvp.Key.StartsWith("ref_character") && !string.IsNullOrEmpty(kvp.Value))
                    .ToList();

                if (characterRefs?.Any() == true)
                {
                    System.Diagnostics.Debug.WriteLine($"Found {characterRefs.Count} character references in rules file");
                    foreach (var characterRef in characterRefs)
                    {
                        System.Diagnostics.Debug.WriteLine($"Processing character reference '{characterRef.Key}': '{characterRef.Value}'");
                        await ProcessCharacterReference(characterRef.Value, characterRef.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading character references: {ex.Message}");
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
                
                string characterContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(characterContent))
                {
                    if (_characterProfiles == null)
                        _characterProfiles = new List<string>();
                    
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
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading character reference: {ex.Message}");
            }
        }

        /// <summary>
        /// Strip frontmatter from content
        /// </summary>
        private string StripFrontmatter(string content)
        {
            if (string.IsNullOrEmpty(content) || !content.StartsWith("---"))
                return content;

            var frontmatterEnd = content.IndexOf("\n---", 3);
            if (frontmatterEnd == -1)
                return content;

            var contentStart = frontmatterEnd + 4; // Length of "\n---"
            if (contentStart < content.Length)
            {
                // If there's a newline after the closing delimiter, skip it too
                if (content[contentStart] == '\n')
                    contentStart++;
                
                return content.Substring(contentStart);
            }

            return content;
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
            prompt.AppendLine("You are a master universe architect specialized in creating comprehensive Rules files that define literary universes, worldbuilding systems, and series continuity. Your expertise lies in developing rich, consistent universe documentation that serves as the foundation for multi-book series.");
            
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

            // Add current rules content (using raw, well-formatted content)
            if (!string.IsNullOrEmpty(_rulesContent))
            {
                prompt.AppendLine("\n=== CURRENT RULES CONTENT ===");
                prompt.AppendLine("This is the current universe documentation being developed. Build upon and enhance this content:");
                
                // ALWAYS use raw content - user's formatting is excellent and parsing was making it worse
                prompt.AppendLine(_rulesContent);
                System.Diagnostics.Debug.WriteLine("Using raw rules content - trusting user's excellent formatting");
            }

            // Add character profiles if available (for universe rules context only)
            if (_characterProfiles?.Count > 0)
            {
                prompt.AppendLine("\n=== EXISTING CHARACTER PROFILES FOR UNIVERSE RULES ===");
                prompt.AppendLine("These character profiles provide context for universe rules development:");
                prompt.AppendLine("Use these to ensure universe rules are consistent with established character relationships and abilities.");
                prompt.AppendLine("");
                
                for (int i = 0; i < _characterProfiles.Count; i++)
                {
                    prompt.AppendLine($"--- Character Profile {i + 1} ---");
                    prompt.AppendLine(_characterProfiles[i]);
                    prompt.AppendLine("");
                }
                
                prompt.AppendLine("UNIVERSE RULES CHARACTER CONSISTENCY GUIDELINES:");
                prompt.AppendLine("- Ensure universe rules are consistent with established character abilities and limitations");
                prompt.AppendLine("- Reference character relationships when developing universe social structures and hierarchies");
                prompt.AppendLine("- Consider character backgrounds when establishing universe history and cultural norms");
                prompt.AppendLine("- Maintain consistency with character motivations in universe rule development");
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

**[Character References]**
- Reference existing character profiles for consistency
- Note character relationships and dynamics
- Document character roles in universe events
- Track character involvement in major plot threads

=== SERIES CONTINUITY MANAGEMENT ===
For series synopsis entries:
- Track universe rule consistency across books
- Note how universe constraints affect story progression
- Maintain timeline consistency and causality
- Show how universe events affect multiple books
- Include universe evolution and rule changes
- Reference previous universe events appropriately

=== RESPONSE FORMAT INSTRUCTIONS ===
When suggesting specific content changes, you MUST use EXACTLY this format:

For REVISIONS (replacing existing text):
```
Original text:
[paste the exact text to be replaced]
```

```
Changed to:
[your new enhanced version]
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

CRITICAL TEXT PRECISION REQUIREMENTS:
- Original text and Insert after must be EXACT, COMPLETE, and VERBATIM from the current rules content
- Include ALL whitespace, line breaks, and formatting exactly as written
- Include complete sentences or natural text boundaries (periods, paragraph breaks)
- NEVER paraphrase, summarize, or reformat the original text
- COPY AND PASTE directly from the current content - do not retype or modify
- Include sufficient context (minimum 10-20 words) for unique identification
- If bullet points, include the bullet symbols and indentation exactly
- If headers, include the ## markdown symbols exactly

TEXT MATCHING VALIDATION:
- Your original text MUST be findable with exact text search in the current rules content
- If you cannot copy exact text, provide surrounding context for identification
- Test your original text by mentally searching for it in the current rules content
- Incomplete or modified text will cause Apply buttons to fail

ANCHOR TEXT GUIDELINES FOR INSERTIONS:
- Include COMPLETE sections, headers, or bullet points that end BEFORE where you want to insert
- NEVER use partial sentences or incomplete phrases as anchor text
- ALWAYS end anchor text at natural boundaries: section endings, headers, paragraph breaks
- Include enough context (at least 10-20 words) to ensure unique identification

For new content additions (expanding universe rules, adding worldbuilding elements, creating new major sections), provide the content directly without the revision or insertion format.

=== CRITICAL DEVELOPMENT PRINCIPLES ===
1. **Universe Consistency**: Ensure all rules and constraints align across the universe
2. **Logical Framework**: Create coherent world-building systems and limitations
3. **Timeline Integrity**: Maintain accurate chronological progression and causality
4. **Character Integration**: Ensure rules work with existing character abilities and relationships
5. **Series Evolution**: Track how universe rules affect multiple books and storylines
6. **Clear Documentation**: Provide comprehensive rules for consistent usage across stories
7. **Flexible Constraints**: Create rules that guide storytelling without being overly restrictive

=== UNIVERSE BUILDING FOCUS AREAS ===
- Magic systems and technology limitations
- Social structures and cultural norms
- Political systems and power dynamics
- Economic and resource constraints
- Historical events and their consequences
- Geographic and environmental factors
- Religious and philosophical frameworks
- Scientific and supernatural laws");

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
                prompt.AppendLine("- Magic system and technology constraints");
                prompt.AppendLine("- Social structures and cultural norms");
                prompt.AppendLine("- Timeline and causality management");
                prompt.AppendLine("");
            }
            else if (isCharacterFocused)
            {
                prompt.AppendLine("=== CHARACTER-RELATED UNIVERSE RULES REQUEST ===");
                prompt.AppendLine("Focus on universe rules that affect characters including:");
                prompt.AppendLine("- Character ability limitations and constraints");
                prompt.AppendLine("- Social hierarchies and character relationships");
                prompt.AppendLine("- Character development rules and progression systems");
                prompt.AppendLine("- Background institutions and character affiliations");
                prompt.AppendLine("- Character interaction guidelines and cultural norms");
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
            var info = new StringBuilder();
            info.AppendLine($"Content Processing: Using raw, user-formatted content");
            info.AppendLine($"Content Length: {_rulesContent?.Length ?? 0} characters");
            info.AppendLine($"Frontmatter Fields: {_frontmatter?.Count ?? 0}");
            
            if (_parsedRules != null)
            {
                info.AppendLine($"Parsing Status: Available for validation (Core Characters: {_parsedRules.Core.CoreCharacters.Count})");
            }
            else
            {
                info.AppendLine($"Parsing Status: Not available (using raw content only)");
            }

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