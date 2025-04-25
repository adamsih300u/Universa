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
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new ArgumentNullException(nameof(libraryPath));
            }
            _fileReferenceService = new FileReferenceService(libraryPath);
            _currentFilePath = filePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                _fileReferenceService.SetCurrentFile(filePath);
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
            prompt.AppendLine("You are an AI assistant specialized in helping users write and edit fiction. You will analyze and respond based on the sections provided below.");

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

            // Add global rules if available
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== GLOBAL STORY RULES ===");
                prompt.AppendLine("These rules MUST be followed for ALL suggestions and changes:");
                prompt.AppendLine(_rules);
                System.Diagnostics.Debug.WriteLine($"Added rules to prompt: {_rules.Length} characters");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No rules available to add to prompt");
            }

            // Add story outline if available
            if (!string.IsNullOrEmpty(_outline))
            {
                prompt.AppendLine("\n=== STORY OUTLINE ===");
                prompt.AppendLine("This is the planned story structure. All suggestions must align with this outline:");
                prompt.AppendLine(_outline);
                System.Diagnostics.Debug.WriteLine($"Added outline to prompt: {_outline.Length} characters");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No outline available to add to prompt");
            }

            // Add style guide if available
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine(@"
=== STYLE GUIDE ===
The text below demonstrates the desired writing style. You must NEVER:
- Use any characters from this sample, unless already in the current story content or outline
- Reference any locations or settings, unless already established in the story content or outline
- Borrow any plot elements or situations, unless already established in the story content or outline
- Copy any specific descriptions, unless already established in the story content or outline
- Use any unique phrases or metaphors, unless already established in the story content or outline
- Include any story elements or content, unless already established in the story content or outline

Instead, ONLY analyze and match these technical aspects:
1. SENTENCE STRUCTURE:
   - Length and complexity of sentences
   - Paragraph structure and flow
   - Transition techniques
   - Punctuation patterns

2. LANGUAGE USE:
   - Vocabulary level and complexity
   - Verb tense and style
   - Descriptive approach
   - Dialog style (if present)

3. NARRATIVE ELEMENTS:
   - Point of view (1st/3rd person)
   - Narrative distance
   - Pacing approach
   - Show vs. tell balance

--- STYLE REFERENCE TEXT ---
" + _styleGuide);
                System.Diagnostics.Debug.WriteLine($"Added style guide to prompt: {_styleGuide.Length} characters");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No style guide available to add to prompt");
            }

            // Add response format instructions
            prompt.AppendLine(@"
=== RESPONSE FORMAT ===
When suggesting specific text changes, you MUST use EXACTLY this format:

```
Original text:
[paste the exact text to be replaced]
```

```
Changed to:
[your new version of the text]
```

CRITICAL INSTRUCTIONS:
1. If the user asks about a specific section (rules, outline, style, or story content), focus your response on that section
2. When suggesting changes:
   - Use the exact labels 'Original text:' and 'Changed to:'
   - Only use text from the CURRENT STORY CONTENT section for the original text
   - Never suggest changes to text from previous messages or conversation history
   - Include the triple backticks exactly as shown
   - The original text must be an exact match of what appears in the content
   - Do not include any other text between or inside the code blocks
   - Orignal text should include ALL original text being recommended for revision
   - Revisions should not match original text exactly, there must be something different
3. When providing guidance:
   - Be specific and reference relevant sections
   - Explain how your suggestions align with the rules and outline
   - Demonstrate understanding of the established style
4. Always:
   - Match the established writing style
   - Keep the same tone and vocabulary level
   - Maintain consistent narrative perspective
   - Follow all global story rules
   - Stay consistent with the story outline
   - Consider the conversation history for context, but only make changes to the current story content");

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

            // If it's a full story analysis or content is small, return everything
            if (needsFullStory || content.Length < 2000)
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

            // Find all chapter boundaries in the document
            var chapterBoundaries = new List<int>();
            chapterBoundaries.Add(startLineIndex); // Start from first content line, not the #file: line
            
            for (int i = startLineIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Check for chapter headings - ONLY consider ## as chapters, # as title, and ### as subsections
                if (line.StartsWith("## ", StringComparison.OrdinalIgnoreCase) || 
                    // Check for "Chapter" variations (case-insensitive)
                    line.StartsWith("CHAPTER ", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("CHAPTER", StringComparison.OrdinalIgnoreCase) ||
                    // Check for "Part" or "Section" headings (must be standalone or followed by number)
                    (line.StartsWith("Part ", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(line, @"^Part\s+\d+", RegexOptions.IgnoreCase)) ||
                    line.Equals("Part", StringComparison.OrdinalIgnoreCase) ||
                    (line.StartsWith("Section ", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(line, @"^Section\s+\d+", RegexOptions.IgnoreCase)) ||
                    line.Equals("Section", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"Found chapter boundary at line {i}: '{line}' (Level 2 heading or explicit chapter marker)");
                    chapterBoundaries.Add(i);
                }
                // Explicitly log when we're skipping level 1 or 3+ headers
                else if (line.StartsWith("# ", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping title at line {i}: '{line}' (Level 1 heading is treated as title, not chapter)");
                }
                else if (line.StartsWith("### ", StringComparison.OrdinalIgnoreCase) || 
                         line.StartsWith("#### ", StringComparison.OrdinalIgnoreCase) || 
                         line.StartsWith("##### ", StringComparison.OrdinalIgnoreCase) || 
                         line.StartsWith("###### ", StringComparison.OrdinalIgnoreCase))
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping subsection at line {i}: '{line}' (Level 3+ heading is treated as subsection, not chapter)");
                }
                // Check for numbered headings (e.g., "1.", "1)") ONLY if they appear to be chapter headings
                // This avoids treating numbered list items as chapter boundaries
                else if ((Regex.IsMatch(line, @"^\d+[\.\)]") && 
                         // Only consider it a chapter heading if it's followed by a title-like string
                         // (capitalized words, not just a continuation of a sentence)
                         Regex.IsMatch(line, @"^\d+[\.\)]\s+[A-Z][a-zA-Z\s]+$")) ||
                         // Or if it's just a number by itself (like "1" or "I" for chapter numbers)
                         Regex.IsMatch(line, @"^(\d+|[IVXLCDM]+)$"))
                {
                    // Additional check: if this is part of a numbered list, don't treat it as a chapter boundary
                    bool isPartOfNumberedList = false;
                    
                    // Check if previous line is also a numbered item
                    if (i > startLineIndex)
                    {
                        var prevLine = lines[i - 1].Trim();
                        if (Regex.IsMatch(prevLine, @"^\d+[\.\)]"))
                        {
                            isPartOfNumberedList = true;
                            System.Diagnostics.Debug.WriteLine($"Line {i} appears to be part of a numbered list (previous line is also numbered)");
                        }
                    }
                    
                    // Check if next line is also a numbered item
                    if (i < lines.Length - 1 && !isPartOfNumberedList)
                    {
                        var nextLine = lines[i + 1].Trim();
                        if (Regex.IsMatch(nextLine, @"^\d+[\.\)]"))
                        {
                            isPartOfNumberedList = true;
                            System.Diagnostics.Debug.WriteLine($"Line {i} appears to be part of a numbered list (next line is also numbered)");
                        }
                    }
                    
                    if (!isPartOfNumberedList)
                    {
                        System.Diagnostics.Debug.WriteLine($"Found chapter boundary at line {i}: '{line}' (Numbered heading)");
                        chapterBoundaries.Add(i);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"NOT treating as chapter boundary at line {i}: '{line}' (Part of a numbered list)");
                    }
                }
                // Debug log for numbered list items that are NOT considered chapter boundaries
                else if (Regex.IsMatch(line, @"^\d+[\.\)]"))
                {
                    System.Diagnostics.Debug.WriteLine($"NOT treating as chapter boundary at line {i}: '{line}' (Appears to be a numbered list item)");
                }
            }
            if (!chapterBoundaries.Contains(lines.Length))
            {
                chapterBoundaries.Add(lines.Length); // Add end of file as final boundary if not already added
            }
            
            // Ensure boundaries are properly sorted (just in case)
            chapterBoundaries.Sort();
            
            // Remove any duplicates that might have been added
            chapterBoundaries = chapterBoundaries.Distinct().ToList();

            // Find current chapter boundaries
            int currentChapterIndex = 0; // Default to first chapter
            
            // Find the chapter that contains our current line
            for (int i = 0; i < chapterBoundaries.Count - 1; i++)
            {
                // The cursor is in this chapter if:
                // - It's anywhere between the start and end boundary lines
                // - Or it's exactly at a chapter boundary (which we treat as being in that chapter)
                if (currentLineIndex >= chapterBoundaries[i] && currentLineIndex <= chapterBoundaries[i + 1])
                {
                    currentChapterIndex = i;
                    
                    // Special debug for boundary cases
                    if (currentLineIndex == chapterBoundaries[i] || currentLineIndex == chapterBoundaries[i + 1])
                    {
                        System.Diagnostics.Debug.WriteLine($"Cursor is exactly at a chapter boundary for chapter {i}");
                    }
                    
                    break;
                }
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
            var chapterEnd = Math.Min(chapterBoundaries[currentChapterIndex + 1], lines.Length - 1);
            System.Diagnostics.Debug.WriteLine($"Current chapter spans lines {chapterStart}-{chapterEnd}");
            
            // Get the complete current chapter content
            var currentChapterContent = string.Join("\n",
                lines.Skip(chapterStart)
                     .Take(chapterEnd - chapterStart + 1));
            
            if (!string.IsNullOrEmpty(currentChapterContent))
            {
                result.AppendLine("\n=== CURRENT CHAPTER ===");
                // Insert cursor position marker at the appropriate line
                var currentChapterLines = currentChapterContent.Split('\n');
                var cursorLineInChapter = currentLineIndex - chapterStart;
                
                // Ensure cursorLineInChapter is within bounds
                cursorLineInChapter = Math.Max(0, Math.Min(cursorLineInChapter, currentChapterLines.Length - 1));
                
                // Add the complete chapter content with cursor position marker
                for (int i = 0; i < currentChapterLines.Length; i++)
                {
                    // If we're at the cursor line, add the cursor marker before the line
                    if (i == cursorLineInChapter)
                    {
                        result.AppendLine("<<CURSOR POSITION>>");
                    }
                    result.AppendLine(currentChapterLines[i]);
                }
                
                System.Diagnostics.Debug.WriteLine($"Added complete current chapter with cursor marker at line {cursorLineInChapter} relative to chapter start");
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
                        System.Diagnostics.Debug.WriteLine($"Added complete next chapter (lines {nextChapterStart}-{nextChapterEnd})");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Next chapter was empty");
                    }
                }
                
                // Add marker if there are more chapters after this
                if (currentChapterIndex < chapterBoundaries.Count - 3)
                {
                    result.AppendLine("\n... (Subsequent content omitted) ...");
                    System.Diagnostics.Debug.WriteLine("Added marker for subsequent content");
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
                        SetStyleGuideContent(styleContent);
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
    }
} 