using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Linq;
using Universa.Desktop.Models;
using System.Net.Http.Headers;
using System.Diagnostics;

namespace Universa.Desktop.Services
{
    public class XAIService
    {
        private readonly HttpClient _httpClient;
        private readonly string _model = "grok-2-1212";

        public XAIService(string apiKey)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://api.x.ai/v1/"),
                Timeout = TimeSpan.FromSeconds(60) // Set a reasonable timeout for AI model requests
            };
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
                    // Only include Grok models
                    if (!id.StartsWith("grok-"))
                        continue;

                    models.Add(new AIModelInfo
                    {
                        Name = id,
                        DisplayName = FormatModelName(id),
                        Provider = AIProvider.XAI
                    });
                }

                return models;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching xAI models: {ex.Message}");
                return new List<AIModelInfo>();
            }
        }

        private string FormatModelName(string modelId)
        {
            // Convert model IDs like "grok-2-1212" to "Grok 2"
            var parts = modelId.Split('-');
            var formattedParts = parts.Select(part =>
            {
                if (string.IsNullOrEmpty(part)) return part;
                return part.ToLower() switch
                {
                    "grok" => "Grok",
                    _ => char.ToUpper(part[0]) + (part.Length > 1 ? part.Substring(1).ToLower() : string.Empty)
                };
            });

            return string.Join(" ", formattedParts);
        }

        public async Task<string> ProcessRequest(string content, string request)
        {
            var messages = new List<object>
            {
                new { role = "system", content = content },
                new { role = "user", content = request }
            };

            var requestBody = new
            {
                model = _model,
                messages = messages,
                temperature = 0.7
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var response = await _httpClient.PostAsync(
                "chat/completions",
                new StringContent(jsonContent, Encoding.UTF8, "application/json")
            );
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<JsonElement>(responseContent);
            return result.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }

        public async Task<float[]> GetEmbeddings(string text)
        {
            try
            {
                var request = new
                {
                    model = "text-embedding-3-small", // Using OpenAI-compatible model name
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
                Debug.WriteLine($"Error getting XAI embeddings: {ex.Message}");
                throw;
            }
        }
    }
} 