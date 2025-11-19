using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;
using System.Threading;
using System.IO;

namespace Universa.Desktop.Services
{
    public class OpenRouterService
    {
        private readonly string _apiKey;
        private readonly HttpClient _httpClient;
        private const string BaseUrl = "https://openrouter.ai/api/v1";
        private readonly ModelCapabilitiesService _capabilitiesService;
        private readonly ConcurrentDictionary<string, int> _dynamicContextLengths = new();
        private readonly List<string> _enabledModels;

        public OpenRouterService(string apiKey, ModelCapabilitiesService capabilitiesService = null, List<string> enabledModels = null)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _capabilitiesService = capabilitiesService ?? new ModelCapabilitiesService();
            _enabledModels = enabledModels ?? new List<string>();
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(300) // Set a longer timeout for AI model generation
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://universa.app");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Universa Desktop");
        }

        /// <summary>
        /// Gets appropriate max_tokens based on dynamic model capabilities or fallback to known limits
        /// </summary>
        private async Task<int> GetMaxTokensForModelAsync(string modelId)
        {
            try
            {
                // First, try to use dynamic context length if we have it
                string actualModelId = modelId.StartsWith("openrouter/") ? modelId.Substring(11) : modelId;
                
                if (_dynamicContextLengths.TryGetValue(actualModelId, out int dynamicContextLength))
                {
                    // Use generous output limit: 1/2 of context length or up to 64K for modern models
                    // This supports longer responses from models like Claude, Grok, and GPT-4
                    // The ExecuteWithTokenFallback mechanism will retry with lower limits if rejected
                    int dynamicOutputLimit = Math.Min(dynamicContextLength / 2, 65536);
                    Debug.WriteLine($"Using dynamic context length for {actualModelId}: {dynamicContextLength} -> output limit: {dynamicOutputLimit}");
                    return dynamicOutputLimit;
                }

                // Fallback to ModelCapabilitiesService
                var maxTokens = await _capabilitiesService.GetMaxOutputTokensAsync(modelId, AIProvider.OpenRouter);
                Debug.WriteLine($"Using ModelCapabilitiesService for {modelId}: {maxTokens} max output tokens");
                return maxTokens;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting max tokens for {modelId}: {ex.Message}");
                // Conservative fallback
                return 2048;
            }
        }

        /// <summary>
        /// Attempts request with progressively lower token limits if 400 errors occur
        /// </summary>
        private async Task<T> ExecuteWithTokenFallback<T>(
            Func<int, Task<T>> requestFunc, 
            string modelId, 
            string operation = "request")
        {
            var maxTokens = await GetMaxTokensForModelAsync(modelId);
            var fallbackLimits = new[] { maxTokens, maxTokens / 2, 1024, 512 };
            
            Exception lastException = null;
            
            foreach (var tokenLimit in fallbackLimits)
            {
                try
                {
                    Debug.WriteLine($"Attempting {operation} with {tokenLimit} max_tokens for model {modelId}");
                    return await requestFunc(tokenLimit);
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("400"))
                {
                    Debug.WriteLine($"400 error with {tokenLimit} tokens, trying lower limit...");
                    lastException = ex;
                    continue;
                }
            }
            
            // If all attempts failed, throw the last exception
            throw new Exception($"All token limit fallbacks failed for model {modelId}", lastException);
        }

        public async Task<List<AIModelInfo>> GetAvailableModels()
        {
            try
            {
                Debug.WriteLine("Fetching available models from OpenRouter...");

                var response = await _httpClient.GetAsync($"{BaseUrl}/models");
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Error fetching models: {response.StatusCode} - {content}");
                    return new List<AIModelInfo>();
                }

                var modelsResponse = JsonSerializer.Deserialize<OpenRouterModelsResponse>(content);
                var models = new List<AIModelInfo>();

                if (modelsResponse?.Data != null)
                {
                    foreach (var model in modelsResponse.Data)
                    {
                        // Skip models that might not be suitable
                        if (string.IsNullOrEmpty(model.Id)) continue;

                        var modelName = $"openrouter/{model.Id}";
                        
                        // Only update capabilities for enabled models (or all if no specific models enabled)
                        bool shouldUpdateCapabilities = _enabledModels.Count == 0 || _enabledModels.Contains(modelName);
                        
                        // Store dynamic context length for this model
                        if (model.ContextLength > 0)
                        {
                            _dynamicContextLengths[model.Id] = model.ContextLength;
                            
                            // Only update the ModelCapabilitiesService for enabled models
                            if (shouldUpdateCapabilities)
                            {
                                var capabilities = new ModelCapabilities
                                {
                                    ModelId = modelName,
                                    Provider = AIProvider.OpenRouter,
                                    ContextLength = model.ContextLength,
                                    MaxOutputTokens = CalculateOutputTokens(model.Id, model.ContextLength),
                                    SupportsStreaming = true,
                                    SupportsImages = IsVisionModel(model.Id),
                                    SupportsToolCalling = true, // Most OpenRouter models support tools
                                    SupportsReasoning = ModelCapabilityHelpers.SupportsReasoningTokens(modelName, AIProvider.OpenRouter),
                                    LastUpdated = DateTime.UtcNow
                                };
                                
                                _capabilitiesService.UpdateModelCapabilities(modelName, AIProvider.OpenRouter, capabilities);
                                
                                Debug.WriteLine($"Updated dynamic capabilities for {model.Id}: Context={model.ContextLength}, Output={capabilities.MaxOutputTokens}");
                            }
                        }

                        models.Add(new AIModelInfo
                        {
                            Name = modelName,
                            DisplayName = FormatModelName(model.Id, model.Name),
                            Provider = AIProvider.OpenRouter
                        });
                    }
                }

                Debug.WriteLine($"Found {models.Count} OpenRouter models with {_dynamicContextLengths.Count} context lengths cached");
                return models;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching OpenRouter models: {ex.Message}");
                return new List<AIModelInfo>();
            }
        }

        public async Task<List<string>> GetAllModelIds()
        {
            try
            {
                var models = await GetAvailableModels();
                return models.Select(m => m.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting all model IDs: {ex.Message}");
                return new List<string>();
            }
        }

        private string FormatModelName(string id, string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                return name;
            }

            // Format the ID if name is not available
            if (string.IsNullOrEmpty(id)) return "Unknown Model";

            // Remove provider prefix if present
            var displayName = id;
            if (displayName.Contains("/"))
            {
                displayName = displayName.Split('/').Last();
            }

            // Replace hyphens and underscores with spaces
            displayName = displayName.Replace('-', ' ').Replace('_', ' ');

            // Capitalize words
            var words = displayName.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                if (!string.IsNullOrEmpty(words[i]))
                {
                    words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1);
                }
            }

            return string.Join(" ", words);
        }

        /// <summary>
        /// Calculates appropriate max output tokens based on model ID
        /// </summary>
        private int CalculateOutputTokens(string modelId, int contextLength)
        {
            string lowerModelId = modelId.ToLowerInvariant();
            
            // Claude 4.5 Sonnet supports 64K output tokens
            if (lowerModelId.Contains("claude") && (lowerModelId.Contains("sonnet-4.5") || lowerModelId.Contains("sonnet-4-5")))
            {
                return 64000;
            }
            
            // Earlier Claude models support 8K output tokens
            if (lowerModelId.Contains("claude"))
            {
                return 8192;
            }
            
            // Most modern models support up to 64K output tokens
            // Conservative limit - let ExecuteWithTokenFallback retry with lower limits if needed
            return Math.Min(contextLength / 2, 65536);
        }

        private bool IsVisionModel(string modelId)
        {
            var lowerModelId = modelId.ToLowerInvariant();
            return lowerModelId.Contains("gpt-4o") || 
                   lowerModelId.Contains("gpt-4-turbo") ||
                   lowerModelId.Contains("gpt-4-vision") ||
                   lowerModelId.Contains("claude-3") ||
                   lowerModelId.Contains("claude-4") ||
                   lowerModelId.Contains("claude-sonnet-4") ||
                   lowerModelId.Contains("gemini") ||
                   lowerModelId.Contains("llava") ||
                   lowerModelId.Contains("vision");
        }

        #region Models
        public class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }

            [JsonPropertyName("reasoning")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Reasoning { get; set; }

            [JsonPropertyName("reasoning_details")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public object ReasoningDetails { get; set; }
        }

        private class OpenRouterModelsResponse
        {
            [JsonPropertyName("data")]
            public List<OpenRouterModel> Data { get; set; }
        }

        private class OpenRouterModel
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("context_length")]
            public int ContextLength { get; set; }
        }

        private class ProviderOptions
        {
            [JsonPropertyName("allow_fallbacks")]
            public bool AllowFallbacks { get; set; } = true;

            [JsonPropertyName("order")]
            public List<string> Order { get; set; }

            [JsonPropertyName("sort")]
            public string Sort { get; set; }
        }

        private class OpenRouterChatRequest
        {
            [JsonPropertyName("model")]
            public string Model { get; set; }

            [JsonPropertyName("messages")]
            public List<ChatMessage> Messages { get; set; }

            [JsonPropertyName("temperature")]
            public double Temperature { get; set; }
            
            [JsonPropertyName("provider")]
            public ProviderOptions Provider { get; set; }
        }

        public class ChatResponse
        {
            [JsonPropertyName("choices")]
            public List<ChatChoice> Choices { get; set; }
        }

        public class ChatChoice
        {
            [JsonPropertyName("message")]
            public ChatMessage Message { get; set; }
        }

        public class ChatResponseWithReasoning
        {
            public string Content { get; set; }
            public string Reasoning { get; set; }
            public object ReasoningDetails { get; set; }
        }

        public class StreamingResponse
        {
            [JsonPropertyName("choices")]
            public List<StreamChoice> Choices { get; set; }
        }

        public class StreamChoice
        {
            [JsonPropertyName("delta")]
            public ChatMessage Delta { get; set; }
            
            [JsonPropertyName("finish_reason")]
            public string FinishReason { get; set; }

            [JsonPropertyName("reasoning_details")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<object> ReasoningDetails { get; set; }
        }

        public class ReasoningConfig
        {
            [JsonPropertyName("effort")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Effort { get; set; } // "high", "medium", "low", "minimal", "none"

            [JsonPropertyName("max_tokens")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public int? MaxTokens { get; set; }

            [JsonPropertyName("exclude")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public bool? Exclude { get; set; }

            [JsonPropertyName("enabled")]
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public bool? Enabled { get; set; }
        }
        #endregion

        public async Task<string> SendChatMessage(List<ChatMessage> messages, string modelId, ReasoningConfig reasoning = null)
        {
            return await ExecuteWithTokenFallback(async (maxTokens) =>
            {
                Debug.WriteLine($"Sending chat message to OpenRouter with model: {modelId}");
                Debug.WriteLine($"Number of messages: {messages?.Count ?? 0}");

                // Strip the "openrouter/" prefix if present
                string actualModelId = modelId.StartsWith("openrouter/") ? modelId.Substring(11) : modelId;

                var request = new Dictionary<string, object>
                {
                    { "model", actualModelId },
                    { "messages", messages ?? new List<ChatMessage>() },
                    { "max_tokens", maxTokens },
                    { "temperature", 0.7 },
                    { "provider", new { allow_fallbacks = true } }
                };

                // Add reasoning config if provided
                if (reasoning != null)
                {
                    request["reasoning"] = reasoning;
                    Debug.WriteLine($"✓ REASONING PARAMETER SENT: Requesting reasoning with config - Effort: {reasoning.Effort ?? "default"}, MaxTokens: {reasoning.MaxTokens?.ToString() ?? "default"}, Exclude: {reasoning.Exclude?.ToString() ?? "false"}");
                }
                else
                {
                    Debug.WriteLine($"✗ NO REASONING PARAMETER: Reasoning not requested for this API call");
                }

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                Debug.WriteLine($"Request JSON with {maxTokens} max_tokens: {jsonContent}");

                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                Debug.WriteLine("Request Headers:");
                foreach (var header in _httpClient.DefaultRequestHeaders)
                {
                    Debug.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }

                var response = await _httpClient.PostAsync($"{BaseUrl}/chat/completions", content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"Response Status: {response.StatusCode}");
                Debug.WriteLine($"Response Headers:");
                foreach (var header in response.Headers)
                {
                    Debug.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
                Debug.WriteLine($"Response Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"OpenRouter API returned {response.StatusCode}: {responseContent}");
                }

                try
                {
                    var result = JsonSerializer.Deserialize<ChatResponse>(responseContent, jsonOptions);
                    if (result?.Choices == null || result.Choices.Count == 0)
                    {
                        throw new Exception("No response content from OpenRouter");
                    }

                    var message = result.Choices[0].Message;
                    if (message == null)
                    {
                        throw new Exception("Empty message from OpenRouter");
                    }

                    // Store reasoning if present (for potential future use in context)
                    if (!string.IsNullOrEmpty(message.Reasoning))
                    {
                        Debug.WriteLine($"Received reasoning tokens: {message.Reasoning.Length} characters");
                    }

                    // Return content - reasoning_details will be preserved via message object in context
                    return message.Content ?? 
                        throw new Exception("Empty message content from OpenRouter");
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"JSON Deserialization error: {ex.Message}");
                    Debug.WriteLine($"Raw response content: {responseContent}");
                    throw new Exception($"Failed to parse OpenRouter response: {ex.Message}. Response content: {responseContent}");
                }
            }, modelId, "SendChatMessage");
        }

        // New streaming method that updates content incrementally
        public async Task<string> StreamChatMessage(List<ChatMessage> messages, string modelId, Action<string> onContentUpdate, CancellationToken cancellationToken, ReasoningConfig reasoning = null, Action<string, object> onReasoningUpdate = null)
        {
            return await ExecuteWithTokenFallback(async (maxTokens) =>
            {
                Debug.WriteLine($"Streaming chat message from OpenRouter with model: {modelId}");
                Debug.WriteLine($"Number of messages: {messages?.Count ?? 0}");

                // Strip the "openrouter/" prefix if present
                string actualModelId = modelId.StartsWith("openrouter/") ? modelId.Substring(11) : modelId;

                var request = new Dictionary<string, object>
                {
                    { "model", actualModelId },
                    { "messages", messages ?? new List<ChatMessage>() },
                    { "max_tokens", maxTokens },
                    { "temperature", 0.7 },
                    { "stream", true },
                    { "provider", new { allow_fallbacks = true } }
                };

                // Add reasoning config if provided
                if (reasoning != null)
                {
                    request["reasoning"] = reasoning;
                    Debug.WriteLine($"✓ REASONING PARAMETER SENT (STREAM): Requesting reasoning with config - Effort: {reasoning.Effort ?? "default"}, MaxTokens: {reasoning.MaxTokens?.ToString() ?? "default"}, Exclude: {reasoning.Exclude?.ToString() ?? "false"}");
                }
                else
                {
                    Debug.WriteLine($"✗ NO REASONING PARAMETER (STREAM): Reasoning not requested for this streaming API call");
                }

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                Debug.WriteLine($"Stream Request JSON: {jsonContent}");

                // Create HTTP request manually to handle streaming
                var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/chat/completions");
                httpRequest.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Use SendAsync with ResponseHeadersRead to start reading the stream before the entire response is received
                using var response = await _httpClient.SendAsync(
                    httpRequest, 
                    HttpCompletionOption.ResponseHeadersRead, 
                    cancellationToken);
                
                response.EnsureSuccessStatusCode();
                
                using var stream = await response.Content.ReadAsStreamAsync();
                using var reader = new StreamReader(stream);

                string fullContent = "";
                string fullReasoning = "";
                List<object> accumulatedReasoningDetails = new List<object>();
                bool receivedAnyContent = false;
                
                while (!reader.EndOfStream)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrEmpty(line)) continue;
                    
                    // Server-Sent Events format starts with "data: "
                    if (!line.StartsWith("data:")) continue;
                    
                    // Get content after "data: "
                    var data = line.Substring(5).Trim();
                    
                    // Check for stream end
                    if (data == "[DONE]") break;

                    try
                    {
                        // Parse the JSON chunk
                        var chunkResponse = JsonSerializer.Deserialize<StreamingResponse>(data, jsonOptions);
                        if (chunkResponse?.Choices == null || chunkResponse.Choices.Count == 0) continue;

                        var choice = chunkResponse.Choices[0];
                        var content = choice.Delta?.Content;
                        var reasoning = choice.Delta?.Reasoning;
                        
                        // Log finish reason if present (indicates why generation stopped)
                        if (!string.IsNullOrEmpty(choice.FinishReason))
                        {
                            Debug.WriteLine($"Stream finished with reason: {choice.FinishReason}");
                        }

                        // Accumulate reasoning details for context preservation
                        if (choice.ReasoningDetails != null && choice.ReasoningDetails.Count > 0)
                        {
                            accumulatedReasoningDetails.AddRange(choice.ReasoningDetails);
                            Debug.WriteLine($"Received reasoning_details chunk with {choice.ReasoningDetails.Count} items");
                            
                            // Notify callback if provided
                            if (onReasoningUpdate != null)
                            {
                                onReasoningUpdate(fullReasoning, accumulatedReasoningDetails.Count > 0 ? accumulatedReasoningDetails : null);
                            }
                        }
                        
                        // Only update if we have actual content
                        if (!string.IsNullOrEmpty(content))
                        {
                            receivedAnyContent = true;
                            fullContent += content;
                            onContentUpdate(fullContent);
                        }

                        // Accumulate reasoning text for context preservation
                        if (!string.IsNullOrEmpty(reasoning))
                        {
                            fullReasoning += reasoning;
                            Debug.WriteLine($"Received reasoning chunk: {reasoning.Length} characters");
                            
                            // Notify callback if provided
                            if (onReasoningUpdate != null)
                            {
                                onReasoningUpdate(fullReasoning, accumulatedReasoningDetails.Count > 0 ? accumulatedReasoningDetails : null);
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        Debug.WriteLine($"Error parsing chunk: {ex.Message}");
                        Debug.WriteLine($"Raw chunk data: {data}");
                        // Continue processing instead of failing
                    }
                }

                // If we didn't receive any content and the stream ended, it was a premature ending
                if (!receivedAnyContent && string.IsNullOrEmpty(fullContent))
                {
                    throw new HttpRequestException("The response ended prematurely without any content. This could be due to a network issue or server timeout.");
                }

                Debug.WriteLine($"Streaming completed. Total content length: {fullContent.Length}");
                
                // Final reasoning update with complete data
                if (onReasoningUpdate != null && (accumulatedReasoningDetails.Count > 0 || !string.IsNullOrEmpty(fullReasoning)))
                {
                    onReasoningUpdate(fullReasoning, accumulatedReasoningDetails.Count > 0 ? accumulatedReasoningDetails : null);
                }
                
                return fullContent;
            }, modelId, "StreamChatMessage");
        }

        // Overload for simple message sending
        public async Task<string> SendChatMessage(string message, string modelId)
        {
            var messages = new List<ChatMessage> 
            { 
                new ChatMessage { Role = "user", Content = message } 
            };
            return await SendChatMessage(messages, modelId);
        }
    }
} 