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
    public class StoryAnalysisBeta : BaseLangChainService
    {
        private string _storyContent;
        private string _currentFilePath;
        private AIProvider _currentProvider;
        private string _currentModel;
        private Dictionary<string, string> _frontmatter;
        private static Dictionary<string, StoryAnalysisBeta> _instances = new Dictionary<string, StoryAnalysisBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MESSAGE_HISTORY_LIMIT = 10;  // Keep reasonable history for context
        
        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        // Private constructor
        private StoryAnalysisBeta(string apiKey, string modelName, AIProvider provider, string filePath)
            : base(apiKey, modelName, provider)
        {
            _currentFilePath = filePath;
            _currentProvider = provider;
            _currentModel = modelName;
        }

        public static async Task<StoryAnalysisBeta> GetInstance(string apiKey, string modelName, AIProvider provider, string filePath)
        {
            await _semaphore.WaitAsync();
            try
            {
                var key = $"{filePath}_{modelName}_{provider}";
                
                if (!_instances.TryGetValue(key, out var instance))
                {
                    instance = new StoryAnalysisBeta(apiKey, modelName, provider, filePath);
                    _instances[key] = instance;
                }
                
                // Update API key and model in case they changed
                instance.UpdateApiKey(apiKey);
                instance.UpdateModel(modelName, provider);
                
                return instance;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public static void ClearInstance(string filePath, string modelName, AIProvider provider)
        {
            lock (_lock)
            {
                var key = $"{filePath}_{modelName}_{provider}";
                _instances.Remove(key);
            }
        }

        public void UpdateApiKey(string apiKey)
        {
            // Update the API key for the HTTP client
            _httpClient.DefaultRequestHeaders.Clear();
            
            switch (_currentProvider)
            {
                case AIProvider.OpenAI:
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    break;
                case AIProvider.Anthropic:
                    _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    break;
                case AIProvider.XAI:
                    _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
                    break;
                // Ollama doesn't need API key
            }
        }

        public void UpdateModel(string modelName, AIProvider provider)
        {
            _currentModel = modelName;
            _currentProvider = provider;
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;

        public async Task UpdateContentAndInitialize(string content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"StoryAnalysisBeta UpdateContentAndInitialize called with content length: {content?.Length ?? 0}");
                
                if (string.IsNullOrEmpty(content))
                {
                    System.Diagnostics.Debug.WriteLine("StoryAnalysisBeta: Empty content provided");
                    return;
                }

                _storyContent = content;
                
                // Parse frontmatter for basic metadata
                _frontmatter = ParseFrontmatter(content);
                
                // Clear memory and reinitialize with clean system prompt
                ClearMemory();
                InitializeSystemMessage();
                
                System.Diagnostics.Debug.WriteLine($"StoryAnalysisBeta initialized with story content: {_storyContent?.Length ?? 0} characters");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in StoryAnalysisBeta UpdateContentAndInitialize: {ex.Message}");
                throw;
            }
        }

        private void TrimMemoryIfNeeded()
        {
            if (_memory.Count > MESSAGE_HISTORY_LIMIT)
            {
                // Keep system message and trim older user/assistant pairs
                var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                var recentMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                           .TakeLast(MESSAGE_HISTORY_LIMIT - 1).ToList();
                
                _memory.Clear();
                if (systemMessage != null)
                {
                    _memory.Add(systemMessage);
                }
                _memory.AddRange(recentMessages);
            }
        }

        private Dictionary<string, string> ParseFrontmatter(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            if (content.StartsWith("---"))
            {
                var endIndex = content.IndexOf("---", 3);
                if (endIndex > 0)
                {
                    var frontmatterText = content.Substring(3, endIndex - 3);
                    var lines = frontmatterText.Split('\n');
                    
                    foreach (var line in lines)
                    {
                        var colonIndex = line.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            var key = line.Substring(0, colonIndex).Trim();
                            var value = line.Substring(colonIndex + 1).Trim().Trim('"');
                            frontmatter[key] = value;
                        }
                    }
                }
            }
            
            return frontmatter;
        }

        private void InitializeSystemMessage()
        {
            string systemPrompt = BuildStoryAnalysisPrompt();
            AddSystemMessage(systemPrompt);
        }

        private string BuildStoryAnalysisPrompt()
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are a professional story analysis expert. Your role is to read and understand the provided manuscript, then answer any questions about the story accurately and helpfully.");
            prompt.AppendLine();
            prompt.AppendLine("INSTRUCTIONS:");
            prompt.AppendLine("- Read the complete manuscript provided below to understand the full context");
            prompt.AppendLine("- Answer questions directly and specifically based on the story content");
            prompt.AppendLine("- Provide insights about plot, characters, themes, pacing, or any other story elements as requested");
            prompt.AppendLine("- Reference specific parts of the story when relevant to support your analysis");
            prompt.AppendLine("- If you cannot find information to answer a question, say so clearly");
            
            // Add story title if available from frontmatter
            if (_frontmatter != null && _frontmatter.TryGetValue("title", out string title) && !string.IsNullOrEmpty(title))
            {
                prompt.AppendLine();
                prompt.AppendLine($"STORY TITLE: {title}");
            }
            
            // Add the complete manuscript
            if (!string.IsNullOrEmpty(_storyContent))
            {
                prompt.AppendLine();
                prompt.AppendLine("=== MANUSCRIPT ===");
                prompt.AppendLine(_storyContent);
            }
            
            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"StoryAnalysisBeta ProcessRequest called with content length: {content?.Length ?? 0}");
                
                // Update content if it has changed
                if (_storyContent != content)
                {
                    await UpdateContentAndInitialize(content);
                }

                // Add the user request to memory
                AddUserMessage(request);
                TrimMemoryIfNeeded();
                
                try
                {
                    // Process the request with the AI
                    var response = await ExecutePrompt(request);
                    
                    // Add the response to memory
                    AddAssistantMessage(response);
                    TrimMemoryIfNeeded();
                    
                    return response;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"\n=== STORY ANALYSIS BETA ERROR ===\n{ex}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== STORY ANALYSIS BETA ERROR ===\n{ex}");
                throw;
            }
        }
    }
} 