using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Text;
using System.Threading;

namespace Universa.Desktop.Services
{
    public class FictionWritingChain : BaseLangChainService
    {
        private string _fictionContent;
        private string _styleGuide;
        private string _rules;
        private string _outline;
        private readonly FileReferenceService _fileReferenceService;
        private static FictionWritingChain _instance;
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private AIProvider _currentProvider;
        private string _currentModel;

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

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

        private FictionWritingChain(string apiKey, string model, AIProvider provider, string content, string filePath = null) 
            : base(apiKey, model, provider)
        {
            _currentProvider = provider;
            _currentModel = model;
            _fileReferenceService = new FileReferenceService(Configuration.Instance.LibraryPath);
            if (!string.IsNullOrEmpty(filePath))
            {
                _fileReferenceService.SetCurrentFile(filePath);
            }
        }

        public static async Task<FictionWritingChain> GetInstance(string apiKey, string model, AIProvider provider, string content, string filePath = null)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("API key and model must be provided");
            }

            await _semaphore.WaitAsync();
            try
            {
                if (_instance == null)
                {
                    System.Diagnostics.Debug.WriteLine("Creating new FictionWritingChain instance");
                    _instance = new FictionWritingChain(apiKey, model, provider, content, filePath);
                    await _instance.UpdateContentAndInitialize(content);
                }
                else if (_instance.CurrentProvider != provider || _instance.CurrentModel != model || 
                         (_instance._fictionContent != content && !string.IsNullOrEmpty(content)))
                {
                    System.Diagnostics.Debug.WriteLine($"Updating existing FictionWritingChain instance. Provider change: {_instance.CurrentProvider != provider}, Model change: {_instance.CurrentModel != model}");
                    // Create new instance if provider or model changed
                    if (_instance.CurrentProvider != provider || _instance.CurrentModel != model)
                    {
                        _instance = new FictionWritingChain(apiKey, model, provider, content, filePath);
                    }
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        _instance._fileReferenceService.SetCurrentFile(filePath);
                    }
                    await _instance.UpdateContentAndInitialize(content);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Using existing FictionWritingChain instance");
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

        private async Task UpdateContentAndInitialize(string content)
        {
            // Store only the most recent conversation context
            var recentMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                      .TakeLast(4)  // Keep last 2 exchanges (2 user messages + 2 assistant responses)
                                      .ToList();
            
            // Clear all messages
            _memory.Clear();
            
            // Update content
            await UpdateContent(content);
            
            // Initialize new system message
            InitializeSystemMessage();
            
            // Restore only recent messages
            _memory.AddRange(recentMessages);
            
            // Trim if needed
            TrimMemory();
        }

        private async Task UpdateContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _fictionContent = string.Empty;
                _styleGuide = string.Empty;
                _rules = string.Empty;
                _outline = string.Empty;
                return;
            }

            // Load any file references first
            var references = await _fileReferenceService.LoadReferencesAsync(content);
            
            var lines = content.Split('\n');
            var styleLines = new List<string>();
            var rulesLines = new List<string>();
            var outlineLines = new List<string>();
            var fictionLines = new List<string>();
            
            bool inStyleSection = false;
            bool inRulesSection = false;
            bool inOutlineSection = false;
            bool inBodySection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip reference lines as they've been processed
                if (trimmedLine.StartsWith("#ref "))
                    continue;
                
                // Check for section starts
                if (trimmedLine.StartsWith("#style", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = true;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#rules", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = false;
                    inRulesSection = true;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#outline", StringComparison.OrdinalIgnoreCase))
                {
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = true;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.StartsWith("#body", StringComparison.OrdinalIgnoreCase))
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
                    fictionLines.Add(line);
                }
            }

            // Process references
            foreach (var reference in references)
            {
                switch (reference.Type.ToLowerInvariant())
                {
                    case "style":
                        styleLines.Add(reference.Content);
                        break;
                    case "rules":
                        rulesLines.Add(reference.Content);
                        break;
                    case "outline":
                        outlineLines.Add(reference.Content);
                        break;
                }
            }

            _styleGuide = string.Join("\n", styleLines).Trim();
            _rules = string.Join("\n", rulesLines).Trim();
            _outline = string.Join("\n", outlineLines).Trim();
            _fictionContent = string.Join("\n", fictionLines).Trim();
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

            // Always include core rules
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== CORE RULES ===");
                prompt.AppendLine(_rules);
            }

            // Add story outline if available
            if (!string.IsNullOrEmpty(_outline))
            {
                prompt.AppendLine("\n=== STORY OUTLINE ===");
                prompt.AppendLine("This is the planned story structure. All suggestions must align with this outline:");
                prompt.AppendLine(_outline);
            }

            // Add style guide if available
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine("\n=== STYLE GUIDE ===");
                prompt.AppendLine(_styleGuide);
            }

            // Add current content
            if (!string.IsNullOrEmpty(_fictionContent))
            {
                prompt.AppendLine("\n=== CURRENT STORY CONTENT ===");
                prompt.AppendLine("This is the current story text that can be analyzed or modified:");
                prompt.AppendLine(_fictionContent);
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
                // Only process the request if one was provided
                if (!string.IsNullOrEmpty(request))
                {
                    // Only update content if it has changed
                    if (!string.IsNullOrEmpty(content) && content != _fictionContent)
                    {
                        await UpdateContentAndInitialize(content);
                    }

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
                            // Get response from AI using the memory context
                            var response = await ExecutePrompt(string.Empty);
                            
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
                            System.Diagnostics.Debug.WriteLine($"\n=== FICTION CHAIN ERROR ===\n{ex}");
                            throw new Exception($"Error after {retryCount} retries: {ex.Message}");
                        }
                    }
                }
                
                return string.Empty;  // No request to process
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== FICTION CHAIN ERROR ===\n{ex}");
                throw;
            }
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
} 