using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Text;
using System.Linq;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class OllamaService
    {
        private readonly HttpClient _httpClient;

        public OllamaService(string baseUrl = "http://localhost:11434")
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri($"{baseUrl}/api/")
            };
        }

        public async Task<List<AIModelInfo>> GetAvailableModels()
        {
            try
            {
                var response = await _httpClient.GetAsync("tags");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<JsonElement>(content);

                var models = new List<AIModelInfo>();
                var modelsArray = data.GetProperty("models");

                foreach (var model in modelsArray.EnumerateArray())
                {
                    var name = model.GetProperty("name").GetString();
                    models.Add(new AIModelInfo
                    {
                        Name = name,
                        DisplayName = FormatModelName(name),
                        Provider = Models.AIProvider.Ollama
                    });
                }

                return models;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching Ollama models: {ex.Message}");
                return new List<AIModelInfo>();
            }
        }

        private string FormatModelName(string modelName)
        {
            // Convert model names like "llama2:latest" to "Llama 2 (Latest)"
            var parts = modelName.Split(':');
            var baseName = parts[0];
            var version = parts.Length > 1 ? parts[1] : null;

            // Format base name
            var formattedName = string.Join(" ", baseName.Split('-', '_')
                .Select(part => char.ToUpper(part[0]) + part.Substring(1)));

            // Add version if present
            if (!string.IsNullOrEmpty(version))
            {
                formattedName += $" ({char.ToUpper(version[0]) + version.Substring(1)})";
            }

            return formattedName;
        }
    }
} 