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
        private static FictionWritingBeta _instance;
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
            if (string.IsNullOrEmpty(text)) return 0;
            // Rough estimation: words * 1.3 (for punctuation and special tokens) + 4 (for safety)
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

            await _semaphore.WaitAsync();
            try
            {
                if (_instance == null)
                {
                    System.Diagnostics.Debug.WriteLine("Creating new FictionWritingBeta instance");
                    _instance = new FictionWritingBeta(apiKey, model, provider, null, filePath, libraryPath);
                    await _instance.UpdateContentAndInitialize(null);
                }
                else if (_instance.CurrentProvider != provider || _instance.CurrentModel != model)
                {
                    System.Diagnostics.Debug.WriteLine($"Updating existing FictionWritingBeta instance. Provider change: {_instance.CurrentProvider != provider}, Model change: {_instance.CurrentModel != model}");
                    // Create new instance if provider or model changed
                    _instance = new FictionWritingBeta(apiKey, model, provider, null, filePath, libraryPath);
                    await _instance.UpdateContentAndInitialize(null);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Using existing FictionWritingBeta instance");
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        _instance._fileReferenceService.SetCurrentFile(filePath);
                    }
                }

                return _instance;
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
        }

        private async Task UpdateContent(string content)
        {
            _fictionContent = content;
            
            // Process frontmatter if present
            bool hasFrontmatter = false;
            _frontmatter = null;
            
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Extract frontmatter
                _frontmatter = ExtractFrontmatter(content, out string contentWithoutFrontmatter);
                if (_frontmatter != null)
                {
                    hasFrontmatter = true;
                    content = contentWithoutFrontmatter;
                    
                    // Log frontmatter for debugging
                    System.Diagnostics.Debug.WriteLine("Frontmatter found:");
                    foreach (var kvp in _frontmatter)
                    {
                        System.Diagnostics.Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
                    }
                }
            }
            
            // Process references
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
            var styleLines = new List<string>();
            var rulesLines = new List<string>();
            var outlineLines = new List<string>();
            var bodyLines = new List<string>();
            
            // Check for references in frontmatter first
            if (hasFrontmatter && _frontmatter != null)
            {
                // Process style reference
                if (_frontmatter.TryGetValue("ref style", out string styleRef) || 
                    _frontmatter.TryGetValue("style", out styleRef))
                {
                    System.Diagnostics.Debug.WriteLine($"Processing style reference from frontmatter: {styleRef}");
                    await ProcessStyleReference(styleRef);
                }
                
                // Process rules reference
                if (_frontmatter.TryGetValue("ref rules", out string rulesRef) || 
                    _frontmatter.TryGetValue("rules", out rulesRef))
                {
                    System.Diagnostics.Debug.WriteLine($"Processing rules reference from frontmatter: {rulesRef}");
                    await ProcessRulesReference(rulesRef);
                }
                
                // Process outline reference
                if (_frontmatter.TryGetValue("ref outline", out string outlineRef) || 
                    _frontmatter.TryGetValue("outline", out outlineRef))
                {
                    System.Diagnostics.Debug.WriteLine($"Processing outline reference from frontmatter: {outlineRef}");
                    await ProcessOutlineReference(outlineRef);
                }
                
                // Check for fiction tag
                if (_frontmatter.ContainsKey("fiction"))
                {
                    // Fiction tag is present in frontmatter
                    System.Diagnostics.Debug.WriteLine("Fiction tag found in frontmatter");
                }
            }
            
            // Continue with existing reference processing for backward compatibility
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                if (trimmedLine.StartsWith("#ref ") || trimmedLine.StartsWith("ref "))
                {
                    string refPart = trimmedLine.StartsWith("#ref ") ? trimmedLine.Substring(5) : trimmedLine.Substring(4);
                    var parts = refPart.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var refType = parts[0].Trim();
                        var refPath = parts[1].Trim();
                        
                        if (refType.Equals("style", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessStyleReference(refPath);
                        }
                        else if (refType.Equals("rules", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessRulesReference(refPath);
                        }
                        else if (refType.Equals("outline", StringComparison.OrdinalIgnoreCase))
                        {
                            await ProcessOutlineReference(refPath);
                        }
                    }
                }
            }
            
            // Continue with existing section processing
            bool inStyleSection = false;
            bool inRulesSection = false;
            bool inOutlineSection = false;
            bool inBodySection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip reference lines as they've been processed
                if (trimmedLine.StartsWith("#ref ") || trimmedLine.StartsWith("ref "))
                    continue;
                
                // Check for section starts
                if (trimmedLine.StartsWith("#style", StringComparison.OrdinalIgnoreCase) || 
                    trimmedLine.Equals("style", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = true;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#rules", StringComparison.OrdinalIgnoreCase) || 
                         trimmedLine.Equals("rules", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = false;
                    inRulesSection = true;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#outline", StringComparison.OrdinalIgnoreCase) || 
                         trimmedLine.Equals("outline", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = true;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#body", StringComparison.OrdinalIgnoreCase) || 
                         trimmedLine.Equals("body", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = true;
                    continue;
                }
                // Check for any other tag to end current section
                else if (trimmedLine.StartsWith("#"))
                {
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = false;
                }

                // Add line to appropriate section
                if (inStyleSection)
                {
                    styleLines.Add(line);
                }
                else if (inRulesSection)
                {
                    rulesLines.Add(line);
                }
                else if (inOutlineSection)
                {
                    outlineLines.Add(line);
                }
                else if (inBodySection)
                {
                    bodyLines.Add(line);
                }
            }
            
            // If we have inline content, use it
            if (styleLines.Count > 0)
            {
                _styleGuide = string.Join("\n", styleLines);
            }
            
            if (rulesLines.Count > 0)
            {
                _rules = string.Join("\n", rulesLines);
            }
            
            if (outlineLines.Count > 0)
            {
                _outline = string.Join("\n", outlineLines);
            }
            
            // Log token estimates for debugging
            LogTokenEstimates();
            
            // Initialize system message
            InitializeSystemMessage();
        }
        
        /// <summary>
        /// Extracts frontmatter from content
        /// </summary>
        private Dictionary<string, string> ExtractFrontmatter(string content, out string contentWithoutFrontmatter)
        {
            contentWithoutFrontmatter = content;
            var frontmatter = new Dictionary<string, string>();
            
            // Check if the content starts with frontmatter delimiter
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
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
                        
                        // Parse frontmatter
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
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
                                
                                frontmatter[key] = value;
                            }
                            else if (line.StartsWith("#"))
                            {
                                // Handle tags (like #fiction) - remove hashtag
                                string tag = line.Trim().Substring(1);
                                frontmatter[tag] = "true";
                            }
                        }
                        
                        // Skip past the closing delimiter
                        int contentStartIndex = endIndex + 4; // Length of "\n---"
                        if (contentStartIndex < content.Length)
                        {
                            // If there's a newline after the closing delimiter, skip it too
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            // Return the content without frontmatter
                            contentWithoutFrontmatter = content.Substring(contentStartIndex);
                        }
                    }
                }
            }
            
            return frontmatter;
        }
        
        /// <summary>
        /// Process a style reference
        /// </summary>
        private async Task ProcessStyleReference(string refPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading style reference from: {refPath}");
                string styleContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(styleContent))
                {
                    _styleGuide = styleContent;
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded style reference: {styleContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Style reference file was empty or could not be loaded: {refPath}");
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
                System.Diagnostics.Debug.WriteLine($"Loading rules reference from: {refPath}");
                string rulesContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(rulesContent))
                {
                    _rules = rulesContent;
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded rules reference: {rulesContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Rules reference file was empty or could not be loaded: {refPath}");
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
                System.Diagnostics.Debug.WriteLine($"Loading outline reference from: {refPath}");
                string outlineContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(outlineContent))
                {
                    _outline = outlineContent;
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded outline reference: {outlineContent.Length} characters");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Outline reference file was empty or could not be loaded: {refPath}");
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
                // Check if we need to refresh core materials
                _messagesSinceRefresh++;
                if (_messagesSinceRefresh >= REFRESH_INTERVAL)
                {
                    _needsRefresh = true;
                }

                // If content has changed or refresh is needed, update
                if (_fictionContent != content || _needsRefresh)
                {
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
                return string.Empty;
            }

            // Check if this is a request for full story analysis
            bool needsFullStory = FULL_STORY_KEYWORDS.Any(keyword => 
                request.Contains(keyword, StringComparison.OrdinalIgnoreCase));

            // If it's a full story analysis or content is small, return everything
            if (needsFullStory || content.Length < 2000)
            {
                System.Diagnostics.Debug.WriteLine($"Returning full story content for analysis. Triggered by request: {request}");
                return content;
            }

            // Split content into lines to find chapter boundaries
            var lines = content.Split('\n');

            // If no lines, return empty
            if (lines.Length == 0)
            {
                return string.Empty;
            }
            
            // Find the current line index based on cursor position
            int currentLineIndex = 0;
            int currentPosition = 0;
            bool foundLine = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (currentPosition <= _currentCursorPosition && 
                    _currentCursorPosition <= currentPosition + lines[i].Length + 1)
                {
                    currentLineIndex = i;
                    foundLine = true;
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
                    currentLineIndex = lines.Length - 1;
                }
                // If cursor is at the start or invalid, use first line
                else
                {
                    currentLineIndex = 0;
                }
            }

            // Ensure currentLineIndex is within bounds
            currentLineIndex = Math.Max(0, Math.Min(currentLineIndex, lines.Length - 1));

            // Find all chapter boundaries in the document
            var chapterBoundaries = new List<int>();
            chapterBoundaries.Add(0); // Always add start of file as first boundary
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("# ", StringComparison.OrdinalIgnoreCase) || 
                    line.StartsWith("CHAPTER ", StringComparison.OrdinalIgnoreCase) ||
                    line.Equals("CHAPTER", StringComparison.OrdinalIgnoreCase))
                {
                    chapterBoundaries.Add(i);
                }
            }
            if (!chapterBoundaries.Contains(lines.Length))
            {
                chapterBoundaries.Add(lines.Length); // Add end of file as final boundary if not already added
            }

            // Find current chapter boundaries
            int currentChapterIndex = 0; // Default to first chapter
            for (int i = 0; i < chapterBoundaries.Count - 1; i++)
            {
                if (currentLineIndex >= chapterBoundaries[i] && currentLineIndex < chapterBoundaries[i + 1])
                {
                    currentChapterIndex = i;
                    break;
                }
            }

            // Build the content with context
            var result = new StringBuilder();

            // Add a marker if we're not starting from the beginning and there's a previous chapter
            if (currentChapterIndex > 0 && chapterBoundaries.Count > 1)
            {
                result.AppendLine("... (Previous content omitted) ...\n");
                
                // Add the entire previous chapter
                result.AppendLine("=== PREVIOUS CHAPTER ===");
                var previousChapterStart = chapterBoundaries[currentChapterIndex - 1];
                var previousChapterEnd = chapterBoundaries[currentChapterIndex] - 1;
                if (previousChapterEnd >= previousChapterStart)
                {
                    var previousChapterContent = string.Join("\n", 
                        lines.Skip(previousChapterStart)
                             .Take(previousChapterEnd - previousChapterStart + 1));
                    result.AppendLine(previousChapterContent);
                    result.AppendLine();
                }
            }

            // Add current chapter content with cursor position
            result.AppendLine("=== CURRENT CHAPTER ===");
            if (currentChapterIndex < chapterBoundaries.Count - 1)
            {
                var chapterStart = chapterBoundaries[currentChapterIndex];
                var chapterEnd = chapterBoundaries[currentChapterIndex + 1];
                
                // Get content before cursor
                if (currentLineIndex > chapterStart)
                {
                    var beforeCursor = string.Join("\n", 
                        lines.Skip(chapterStart)
                             .Take(currentLineIndex - chapterStart));
                    if (!string.IsNullOrEmpty(beforeCursor))
                    {
                        result.AppendLine(beforeCursor);
                    }
                }

                // Add cursor position marker and current line
                result.AppendLine("<<CURSOR POSITION>>");
                result.AppendLine(lines[currentLineIndex]);

                // Get content after cursor
                if (currentLineIndex < chapterEnd - 1)
                {
                    var afterCursor = string.Join("\n", 
                        lines.Skip(currentLineIndex + 1)
                             .Take(chapterEnd - currentLineIndex - 1));
                    if (!string.IsNullOrEmpty(afterCursor))
                    {
                        result.AppendLine(afterCursor);
                    }
                }

                // Add next chapter if it exists
                if (currentChapterIndex < chapterBoundaries.Count - 2)
                {
                    result.AppendLine("\n=== NEXT CHAPTER ===");
                    var nextChapterStart = chapterBoundaries[currentChapterIndex + 1];
                    var nextChapterEnd = chapterBoundaries[currentChapterIndex + 2] - 1;
                    if (nextChapterEnd >= nextChapterStart)
                    {
                        var nextChapterContent = string.Join("\n",
                            lines.Skip(nextChapterStart)
                                 .Take(nextChapterEnd - nextChapterStart + 1));
                        result.AppendLine(nextChapterContent);
                    }
                    
                    // Add marker if there are more chapters after this
                    if (currentChapterIndex < chapterBoundaries.Count - 3)
                    {
                        result.AppendLine("\n... (Subsequent content omitted) ...");
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"Selected chapter content around cursor position {_currentCursorPosition}");
            System.Diagnostics.Debug.WriteLine($"Current chapter index: {currentChapterIndex}");
            System.Diagnostics.Debug.WriteLine($"Current line index: {currentLineIndex}");
            
            return result.ToString();
        }

        public void UpdateCursorPosition(int position)
        {
            _currentCursorPosition = position;
            System.Diagnostics.Debug.WriteLine($"Cursor position updated to: {position}");
        }

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;
    }
} 