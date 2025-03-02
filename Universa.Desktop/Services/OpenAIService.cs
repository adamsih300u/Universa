using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Linq;
using Universa.Desktop.Models;
using System.Diagnostics;
using System.Net.Http.Headers;

namespace Universa.Desktop.Services
{
    public class OpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.openai.com/v1/";

        public OpenAIService(string apiKey = null)
        {
            Debug.WriteLine("Initializing OpenAIService...");
            
            // Check if OpenAI is enabled in configuration
            var config = Configuration.Instance;
            if (!config.EnableOpenAI)
            {
                Debug.WriteLine("OpenAI is disabled in configuration");
                _apiKey = null;
                _httpClient = new HttpClient();
                return;
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.WriteLine("No API key provided to OpenAIService constructor");
                apiKey = config.OpenAIApiKey;
                if (string.IsNullOrEmpty(apiKey))
                {
                    Debug.WriteLine("No API key found in configuration");
                    throw new ArgumentException("OpenAI API key is required when OpenAI is enabled");
                }
            }

            _apiKey = apiKey;
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(BaseUrl)
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            Debug.WriteLine($"OpenAIService initialized with API key length: {_apiKey.Length}");
            Debug.WriteLine($"Base URL set to: {BaseUrl}");
        }

        public async Task<List<AIModelInfo>> GetAvailableModels()
        {
            // Return empty list if OpenAI is disabled or no API key is set
            if (string.IsNullOrEmpty(_apiKey) || !Configuration.Instance.EnableOpenAI)
            {
                Debug.WriteLine("OpenAI is disabled or no API key is set, returning empty models list");
                return new List<AIModelInfo>();
            }

            try
            {
                Debug.WriteLine("Getting available OpenAI models...");
                var request = new HttpRequestMessage(HttpMethod.Get, "models");
                
                Debug.WriteLine($"Sending request to: {request.RequestUri}");
                Debug.WriteLine($"Authorization header: Bearer {_apiKey.Substring(0, Math.Min(5, _apiKey.Length))}...");
                
                var response = await _httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"OpenAI API error: {response.StatusCode} - {content}");
                    Debug.WriteLine($"Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");
                    throw new HttpRequestException($"OpenAI API returned {response.StatusCode}: {content}");
                }
                
                Debug.WriteLine("Successfully received models response");
                var data = JsonSerializer.Deserialize<JsonElement>(content);
                var models = new List<AIModelInfo>();
                var modelsArray = data.GetProperty("data");

                foreach (var model in modelsArray.EnumerateArray())
                {
                    var id = model.GetProperty("id").GetString();
                    Debug.WriteLine($"Processing model: {id}");
                    
                    // Skip certain model types
                    if (id.Contains("embedding") || 
                        id.Contains("moderation") || 
                        id.Contains("tts-") || 
                        id.Contains("dall-e") ||
                        id.Contains("whisper") ||
                        id.Contains("babbage") ||
                        id.Contains("davinci"))
                    {
                        Debug.WriteLine($"Skipping utility model: {id}");
                        continue;
                    }

                    // Skip dated versions
                    if (id.Contains("-20") || // Skip models with year in name
                        id.Contains("-0") ||  // Skip models with month in name (e.g., -0613)
                        id.Contains("-1"))    // Skip models with month in name (e.g., -1106)
                    {
                        Debug.WriteLine($"Skipping dated model: {id}");
                        continue;
                    }

                    // Include GPT, O1, and O3 models
                    if (id.StartsWith("gpt-") || 
                        id.StartsWith("o1-") ||
                        id.StartsWith("o3-") ||
                        id.StartsWith("gpt4-"))
                    {
                        Debug.WriteLine($"Adding model: {id}");
                        models.Add(new AIModelInfo
                        {
                            Name = id,
                            DisplayName = FormatModelName(id),
                            Provider = AIProvider.OpenAI
                        });
                    }
                    else
                    {
                        Debug.WriteLine($"Skipping non-supported model: {id}");
                    }
                }

                Debug.WriteLine($"Found {models.Count} OpenAI models");
                foreach (var model in models)
                {
                    Debug.WriteLine($"- {model.Name} ({model.DisplayName})");
                }
                
                return models;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetAvailableModels: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Debug.WriteLine($"Inner stack trace: {ex.InnerException.StackTrace}");
                }
                throw; // Re-throw to let the caller handle the error
            }
        }

        private string FormatModelName(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return modelId;

            // Split on common delimiters
            var parts = modelId.Split(new[] { '-', '_', '.' });
            
            // Capitalize each part and handle special cases
            var formattedParts = parts.Select(part =>
            {
                if (string.IsNullOrEmpty(part)) return string.Empty;
                
                // Handle common model name parts
                return part.ToLower() switch
                {
                    "gpt" => "GPT",
                    "gpt4" => "GPT-4",
                    "turbo" => "Turbo",
                    _ => char.ToUpper(part[0]) + (part.Length > 1 ? part.Substring(1).ToLower() : string.Empty)
                };
            });

            return string.Join(" ", formattedParts.Where(p => !string.IsNullOrEmpty(p)));
        }

        public async Task<float[]> GetEmbeddings(string text)
        {
            try
            {
                var request = new
                {
                    model = "text-embedding-3-small",
                    input = text,
                    encoding_format = "float"
                };

                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/embeddings", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"OpenAI embeddings response: {responseJson}");

                var responseObj = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var data = responseObj.GetProperty("data")[0];
                var embedding = data.GetProperty("embedding");
                
                var embeddings = embedding.EnumerateArray()
                    .Select(x => x.GetSingle())
                    .ToArray();

                Debug.WriteLine($"Successfully generated embeddings with {embeddings.Length} dimensions");
                return embeddings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting embeddings from OpenAI: {ex.Message}");
                throw;
            }
        }
    }
} 