using System;
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

        public OpenRouterService(string apiKey)
        {
            _apiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(300) // Set a longer timeout for AI model generation
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Add("HTTP-Referer", "https://universa.app");
            _httpClient.DefaultRequestHeaders.Add("X-Title", "Universa Desktop");
        }

        public async Task<List<AIModelInfo>> GetAvailableModels()
        {
            try
            {
                Debug.WriteLine("Fetching available models from OpenRouter...");
                var response = await _httpClient.GetAsync($"{BaseUrl}/models");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var modelsResponse = JsonSerializer.Deserialize<OpenRouterModelsResponse>(content);

                if (modelsResponse?.Data == null)
                {
                    Debug.WriteLine("No models returned from OpenRouter API");
                    return new List<AIModelInfo>();
                }

                var models = modelsResponse.Data
                    .Where(m => m.Id != null)
                    .Select(m => new AIModelInfo
                    {
                        Name = m.Id,
                        DisplayName = FormatModelName(m.Id, m.Name),
                        Provider = AIProvider.OpenRouter
                    })
                    .ToList();

                Debug.WriteLine($"Retrieved {models.Count} models from OpenRouter");
                return models;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching OpenRouter models: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
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

        #region Models
        public class ChatMessage
        {
            [JsonPropertyName("role")]
            public string Role { get; set; }

            [JsonPropertyName("content")]
            public string Content { get; set; }
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

        public class StreamingResponse
        {
            [JsonPropertyName("choices")]
            public List<StreamChoice> Choices { get; set; }
        }

        public class StreamChoice
        {
            [JsonPropertyName("delta")]
            public ChatMessage Delta { get; set; }
        }
        #endregion

        public async Task<string> SendChatMessage(List<ChatMessage> messages, string modelId)
        {
            try
            {
                Debug.WriteLine($"Sending chat message to OpenRouter with model: {modelId}");
                Debug.WriteLine($"Number of messages: {messages?.Count ?? 0}");

                // Strip the "openrouter/" prefix if present
                string actualModelId = modelId.StartsWith("openrouter/") ? modelId.Substring(11) : modelId;

                var request = new
                {
                    model = actualModelId,
                    messages = messages ?? new List<ChatMessage>(),
                    max_tokens = 16384,
                    temperature = 0.7,
                    provider = new
                    {
                        allow_fallbacks = true // Enable automatic fallbacks between providers
                    }
                };

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };

                var jsonContent = JsonSerializer.Serialize(request, jsonOptions);
                Debug.WriteLine($"Request JSON: {jsonContent}");

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

                    return result.Choices[0].Message?.Content ?? 
                        throw new Exception("Empty message content from OpenRouter");
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"JSON Deserialization error: {ex.Message}");
                    Debug.WriteLine($"Raw response content: {responseContent}");
                    throw new Exception($"Failed to parse OpenRouter response: {ex.Message}. Response content: {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending chat message: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        // New streaming method that updates content incrementally
        public async Task<string> StreamChatMessage(List<ChatMessage> messages, string modelId, Action<string> onContentUpdate, CancellationToken cancellationToken)
        {
            try
            {
                Debug.WriteLine($"Streaming chat message from OpenRouter with model: {modelId}");
                Debug.WriteLine($"Number of messages: {messages?.Count ?? 0}");

                // Strip the "openrouter/" prefix if present
                string actualModelId = modelId.StartsWith("openrouter/") ? modelId.Substring(11) : modelId;

                var request = new
                {
                    model = actualModelId,
                    messages = messages ?? new List<ChatMessage>(),
                    max_tokens = 16384,
                    temperature = 0.7,
                    stream = true, // Enable streaming
                    provider = new
                    {
                        allow_fallbacks = true // Enable automatic fallbacks between providers
                    }
                };

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
                        
                        // Only update if we have actual content
                        if (!string.IsNullOrEmpty(content))
                        {
                            receivedAnyContent = true;
                            fullContent += content;
                            onContentUpdate(fullContent);
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
                return fullContent;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Streaming was cancelled");
                throw;
            }
            catch (HttpRequestException ex) when (ex.Message.Contains("prematurely"))
            {
                Debug.WriteLine($"Stream ended prematurely: {ex.Message}");
                throw new HttpRequestException("The response was interrupted. This could be due to network issues or server timeouts. Please try again.", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error streaming chat message: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
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