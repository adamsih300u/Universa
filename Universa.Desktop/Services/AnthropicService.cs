using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Linq;
using Universa.Desktop.Models;
using System.Diagnostics;

namespace Universa.Desktop.Services
{
    public class AnthropicService
    {
        private readonly HttpClient _httpClient;

        public AnthropicService(string apiKey)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.anthropic.com/v1/"),
                Timeout = TimeSpan.FromSeconds(120) // Set a longer timeout for AI model generation
            };
            _httpClient.DefaultRequestHeaders.Add("x-api-key", apiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        }

        public async Task<List<AIModelInfo>> GetAvailableModels()
        {
            try
            {
                var response = await _httpClient.GetAsync("models");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);

                var models = new List<AIModelInfo>();
                var modelsArray = data.GetProperty("data");

                foreach (var model in modelsArray.EnumerateArray())
                {
                    var id = model.GetProperty("id").GetString();
                    // Skip non-Claude models
                    if (!id.StartsWith("claude"))
                        continue;

                    var displayName = model.GetProperty("display_name").GetString();
                    
                    // Add the regular model
                    models.Add(new AIModelInfo
                    {
                        Name = id,
                        DisplayName = displayName ?? FormatModelName(id), // Fallback to our formatter if display_name is null
                        Provider = Models.AIProvider.Anthropic,
                        IsThinkingMode = false
                    });
                    
                    // If this is Claude 3.7, add a thinking mode version
                    if (id.Contains("claude-3-7") || id.Contains("claude-3.7"))
                    {
                        models.Add(new AIModelInfo
                        {
                            Name = id,
                            DisplayName = (displayName ?? FormatModelName(id)) + " (Thinking)",
                            Provider = Models.AIProvider.Anthropic,
                            IsThinkingMode = true
                        });
                        Debug.WriteLine($"Added thinking mode for model: {id}");
                    }
                }

                return models;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching Anthropic models: {ex.Message}");
                return new List<AIModelInfo>();
            }
        }

        private string FormatModelName(string modelName)
        {
            // Convert model names like "claude-3-opus-20240229" to "Claude 3 Opus"
            var parts = modelName.Split('-');
            var formattedParts = parts.Select(part =>
            {
                // Capitalize first letter
                if (string.IsNullOrEmpty(part)) return part;
                return char.ToUpper(part[0]) + part.Substring(1);
            });

            // Filter out version numbers and join
            return string.Join(" ", formattedParts.Where(p => !p.All(char.IsDigit)));
        }

        public async Task<float[]> GetEmbeddings(string text)
        {
            try
            {
                var request = new
                {
                    model = "claude-3-embedding",
                    input = text,
                    encoding_format = "float"
                };

                var content = new StringContent(
                    JsonSerializer.Serialize(request),
                    Encoding.UTF8,
                    "application/json"
                );

                var response = await _httpClient.PostAsync("embeddings", content);
                response.EnsureSuccessStatusCode();

                var responseString = await response.Content.ReadAsStringAsync();
                var responseJson = JsonDocument.Parse(responseString);
                var embedding = responseJson.RootElement
                    .GetProperty("data")[0]
                    .GetProperty("embedding");

                var embeddings = new float[embedding.GetArrayLength()];
                var index = 0;
                foreach (var value in embedding.EnumerateArray())
                {
                    embeddings[index++] = value.GetSingle();
                }

                return embeddings;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting Anthropic embeddings: {ex.Message}");
                throw;
            }
        }
    }
} 