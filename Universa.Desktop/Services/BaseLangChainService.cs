using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Universa.Desktop.Models;
using System.Net.Http.Headers;
using System.Diagnostics;
using System.Threading;

namespace Universa.Desktop.Services
{
    public abstract class BaseLangChainService : IDisposable
    {
        protected readonly string _apiKey;
        protected readonly string _model;
        protected readonly Models.AIProvider _provider;
        protected readonly List<MemoryMessage> _memory;
        protected readonly HttpClient _httpClient;
        protected const int MAX_HISTORY_ITEMS = 25;
        protected string _currentContext;
        protected bool _disposed;
        protected bool _isThinkingMode;

        public class MemoryMessage
        {
            public string Role { get; set; }
            public string Content { get; set; }
            public string Model { get; set; }
            public DateTime Timestamp { get; set; }

            public MemoryMessage(string role, string content)
            {
                Role = role;
                Content = content;
                Timestamp = DateTime.Now;
            }

            public MemoryMessage(string role, string content, string model) : this(role, content)
            {
                Model = model;
            }

            public MemoryMessage()
            {
                Role = "assistant";
                Content = string.Empty;
                Timestamp = DateTime.Now;
            }
        }

        protected BaseLangChainService(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false)
        {
            _apiKey = apiKey;
            _model = model;
            _provider = provider;
            _memory = new List<MemoryMessage>();
            _isThinkingMode = isThinkingMode;
            _httpClient = new HttpClient();

            // Configure HTTP client based on provider
            switch (provider)
            {
                case Models.AIProvider.OpenAI:
                    _httpClient.BaseAddress = new Uri("https://api.openai.com/v1/");
                    _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                    break;
                case Models.AIProvider.Anthropic:
                    _httpClient.BaseAddress = new Uri("https://api.anthropic.com/v1/");
                    _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
                    _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                    break;
                case Models.AIProvider.Ollama:
                    _httpClient.BaseAddress = new Uri("http://localhost:11434/api/");
                    break;
                case Models.AIProvider.XAI:
                    _httpClient.BaseAddress = new Uri("https://api.x.ai/v1/");
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    break;
            }
        }

        public abstract Task<string> ProcessRequest(string content, string request);

        public virtual async Task<string> ProcessRequest(string content, string request, CancellationToken cancellationToken)
        {
            return await ProcessRequest(content, request);
        }

        protected virtual string BuildBasePrompt(string content, string request)
        {
            var historyText = string.Join("\n", _memory.Select(m => $"{m.Role}: {m.Content}"));
            return $@"You are an AI assistant specialized in helping users with their tasks.

Previous conversation:
{historyText}

Current content:
{content}

User request:
{request}

Please provide specific and helpful suggestions.";
        }

        protected async Task<string> ExecutePrompt(string prompt)
        {
            try
            {
                return await ExecutePrompt(prompt, CancellationToken.None);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing prompt: {ex.Message}");
                throw;
            }
        }

        protected async Task<string> ExecutePrompt(string prompt, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            try
            {
                switch (_provider)
                {
                    case Models.AIProvider.OpenAI:
                        return await ExecuteOpenAIPrompt(prompt, cancellationToken);
                    case Models.AIProvider.Anthropic:
                        return await ExecuteAnthropicPrompt(prompt, cancellationToken);
                    case Models.AIProvider.Ollama:
                        return await ExecuteOllamaPrompt(prompt, cancellationToken);
                    case Models.AIProvider.XAI:
                        return await ExecuteXAIPrompt(prompt, cancellationToken);
                    case Models.AIProvider.OpenRouter:
                        return await ExecuteOpenRouterPrompt(prompt, cancellationToken);
                    default:
                        throw new NotSupportedException($"Provider {_provider} is not supported.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing prompt: {ex.Message}");
                throw;
            }
        }

        private async Task<string> ExecuteOpenAIPrompt(string prompt, CancellationToken cancellationToken)
        {
            // Debug logging for model
            System.Diagnostics.Debug.WriteLine($"\n=== OPENAI API CALL ===");
            System.Diagnostics.Debug.WriteLine($"Using model: {_model}");
            System.Diagnostics.Debug.WriteLine($"Memory items: {_memory.Count}");
            System.Diagnostics.Debug.WriteLine("Messages being sent:");
            foreach (var msg in _memory)
            {
                System.Diagnostics.Debug.WriteLine($"{msg.Role}: {msg.Content}");
            }

            // Convert memory to messages array
            var messages = _memory.Select(m => new { role = m.Role.ToLower(), content = m.Content }).ToList();

            // If there's a prompt, add it as a user message
            if (!string.IsNullOrEmpty(prompt))
            {
                messages.Add(new { role = "user", content = prompt });
            }

            var requestBody = new
            {
                model = _model,
                messages = messages.ToArray(),
                temperature = 0.7,
                max_tokens = 16384
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            System.Diagnostics.Debug.WriteLine($"\n=== REQUEST JSON ===\n{jsonContent}");

            var response = await _httpClient.PostAsync(
                "chat/completions",
                new StringContent(jsonContent, Encoding.UTF8, "application/json"),
                cancellationToken
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"OpenAI API error: {responseContent}");
            }

            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseData.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        private async Task<string> ExecuteAnthropicPrompt(string prompt, CancellationToken cancellationToken)
        {
            // Debug logging for model and memory
            Debug.WriteLine($"\n=== ANTHROPIC API CALL DEBUG ===");
            Debug.WriteLine($"Using model: {_model}");
            Debug.WriteLine($"Thinking mode: {_isThinkingMode}");
            Debug.WriteLine($"Memory items: {_memory.Count}");
            Debug.WriteLine("\nRaw memory contents:");
            foreach (var msg in _memory)
            {
                Debug.WriteLine($"[{msg.Role}]: {msg.Content}");
            }

            // Get system message
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            
            // Convert memory to messages array, excluding system message
            var messages = _memory
                .Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                .Select(m => new { role = m.Role.ToLower(), content = m.Content })
                .ToList();

            // If there's a prompt, add it as a user message
            if (!string.IsNullOrEmpty(prompt))
            {
                messages.Add(new { role = "user", content = prompt });
            }

            // Debug the messages being sent
            Debug.WriteLine("\n=== MESSAGES BEING SENT ===");
            foreach (var msg in messages)
            {
                Debug.WriteLine($"{msg.role}: {msg.content}");
            }

            // Create the request body with or without thinking mode
            object requestBody;
            
            if (_isThinkingMode)
            {
                requestBody = new
                {
                    model = _model,
                    messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray(),
                    max_tokens = 16384,
                    temperature = 0.7,
                    system = systemMessage?.Content ?? string.Empty,
                    tool_choice = new { type = "thinking" }
                };
                Debug.WriteLine("Using thinking mode for Anthropic API call");
            }
            else
            {
                requestBody = new
                {
                    model = _model,
                    messages = messages.Select(m => new { role = m.role, content = m.content }).ToArray(),
                    max_tokens = 16384,
                    temperature = 0.7,
                    system = systemMessage?.Content ?? string.Empty
                };
            }

            var jsonContent = JsonSerializer.Serialize(requestBody);
            Debug.WriteLine("\n=== ANTHROPIC REQUEST ===");
            Debug.WriteLine($"Final JSON being sent:");
            Debug.WriteLine(jsonContent);

            var response = await _httpClient.PostAsync(
                "messages",
                new StringContent(jsonContent, Encoding.UTF8, "application/json"),
                cancellationToken
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Anthropic API error: {responseContent}");
            }

            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            
            // Aggregate all content blocks from the response
            var contentBlocks = responseData.GetProperty("content");
            var fullResponse = new StringBuilder();
            
            for (int i = 0; i < contentBlocks.GetArrayLength(); i++)
            {
                var block = contentBlocks[i];
                if (block.TryGetProperty("text", out var textElement))
                {
                    fullResponse.Append(textElement.GetString());
                }
            }
            
            Debug.WriteLine($"\n=== ANTHROPIC RESPONSE ===");
            Debug.WriteLine($"Response length: {fullResponse.Length}");
            Debug.WriteLine($"First 100 chars: {(fullResponse.Length > 100 ? fullResponse.ToString().Substring(0, 100) + "..." : fullResponse.ToString())}");
            
            return fullResponse.ToString();
        }

        private async Task<string> ExecuteOllamaPrompt(string prompt, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _model,
                prompt = $"You are a helpful AI assistant specialized in the task at hand. Follow the given instructions precisely.\n\n{prompt}",
                temperature = 0.7,
                num_predict = 4096
            };

            var response = await _httpClient.PostAsync(
                "generate",
                new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json"),
                cancellationToken
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Ollama API error: {responseContent}");
            }

            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseData.GetProperty("response").GetString();
        }

        private async Task<string> ExecuteXAIPrompt(string prompt, CancellationToken cancellationToken)
        {
            // Debug logging
            System.Diagnostics.Debug.WriteLine($"\n=== XAI API CALL ===");
            System.Diagnostics.Debug.WriteLine($"Using model: {_model}");
            System.Diagnostics.Debug.WriteLine($"Memory items: {_memory.Count}");

            var messages = new List<object>();
                
            // Add system message if present
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                messages.Add(new { role = "system", content = systemMessage.Content });
            }

            // Add memory messages
            foreach (var msg in _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
            {
                messages.Add(new { role = msg.Role.ToLower(), content = msg.Content });
            }

            // Add current prompt if present
            if (!string.IsNullOrEmpty(prompt))
            {
                messages.Add(new { role = "user", content = prompt });
            }

            var requestData = new
            {
                model = _model,
                messages = messages,
                temperature = 0.7
            };

            var jsonContent = JsonSerializer.Serialize(requestData);
            System.Diagnostics.Debug.WriteLine($"\n=== REQUEST JSON ===\n{jsonContent}");

            var response = await _httpClient.PostAsync(
                "chat/completions",
                new StringContent(jsonContent, Encoding.UTF8, "application/json"),
                cancellationToken
            );

            var responseContent = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"xAI API error: {responseContent}");
            }

            var responseData = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return responseData.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        protected async Task<string> ExecuteOpenRouterPrompt(string prompt, CancellationToken cancellationToken = default)
        {
            try
            {
                Debug.WriteLine($"Executing OpenRouter prompt with model: {_model}");
                
                // Create a new OpenRouterService
                var openRouterService = new OpenRouterService(_apiKey);
                
                // Prepare the messages for the OpenRouter API
                var messages = new List<OpenRouterService.ChatMessage>();
                
                // Add system message if available
                var systemMessage = _memory.FirstOrDefault(m => m.Role == "system");
                if (systemMessage != null)
                {
                    messages.Add(new OpenRouterService.ChatMessage
                    {
                        Role = "system",
                        Content = systemMessage.Content
                    });
                }
                
                // Add user and assistant messages
                foreach (var message in _memory.Where(m => m.Role != "system"))
                {
                    messages.Add(new OpenRouterService.ChatMessage
                    {
                        Role = message.Role,
                        Content = message.Content
                    });
                }
                
                // Add the current prompt if not empty
                if (!string.IsNullOrEmpty(prompt))
                {
                    messages.Add(new OpenRouterService.ChatMessage
                    {
                        Role = "user",
                        Content = prompt
                    });
                }
                
                // Send the request to OpenRouter
                return await openRouterService.SendChatMessage(messages, _model);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error executing OpenRouter prompt: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                throw;
            }
        }

        public virtual void ClearMemory()
        {
            // Preserve the system message if it exists
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            _memory.Clear();
            if (systemMessage != null)
            {
                _memory.Add(systemMessage);
            }
        }

        public void AddUserMessage(string content)
        {
            // Add the new user message
            _memory.Add(new MemoryMessage("user", content, _model));
            
            // Trim memory if needed, but preserve system message
            TrimMemory();
        }

        public void AddAssistantMessage(string content)
        {
            _memory.Add(new MemoryMessage("assistant", content, _model));
            
            // Trim memory if needed, but preserve system message
            TrimMemory();
        }

        public void AddSystemMessage(string content)
        {
            // Remove any existing system message
            var existingSystemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (existingSystemMessage != null)
            {
                _memory.Remove(existingSystemMessage);
            }
            
            // Add the new system message at the beginning
            _memory.Insert(0, new MemoryMessage("system", content, _model));
        }

        protected void TrimMemory()
        {
            // Keep track of how many non-system messages we have
            var nonSystemMessages = _memory.Count(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            
            // If we're over the limit, remove oldest non-system messages
            while (nonSystemMessages > MAX_HISTORY_ITEMS)
            {
                // Find the first non-system message
                var firstNonSystemIndex = _memory.FindIndex(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                if (firstNonSystemIndex >= 0)
                {
                    _memory.RemoveAt(firstNonSystemIndex);
                    nonSystemMessages--;
                }
            }
        }

        public IReadOnlyList<MemoryMessage> GetMessageHistory()
        {
            return _memory.AsReadOnly();
        }

        public virtual async Task<string> GetResponseAsync(string request)
        {
            return await ProcessRequest(string.Empty, request);
        }

        public virtual async Task UpdateContextAsync(string context)
        {
            _currentContext = context;
            await Task.CompletedTask;
        }

        public virtual void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }

        protected virtual void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }
        }
    }
} 