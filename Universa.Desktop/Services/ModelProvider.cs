using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class ModelProvider
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        public event EventHandler<List<AIModelInfo>> ModelsChanged;

        public ModelProvider(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            Debug.WriteLine($"ModelProvider initialized with config: {_configService != null}, provider: {_config != null}");

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;
        }

        private async void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            // Check if the change is related to AI settings
            if (e.Key.StartsWith(ConfigurationKeys.AI.OpenAIEnabled) ||
                e.Key.StartsWith(ConfigurationKeys.AI.OpenAIApiKey) ||
                e.Key.StartsWith(ConfigurationKeys.AI.AnthropicEnabled) ||
                e.Key.StartsWith(ConfigurationKeys.AI.AnthropicApiKey) ||
                e.Key.StartsWith(ConfigurationKeys.AI.XAIEnabled) ||
                e.Key.StartsWith(ConfigurationKeys.AI.XAIApiKey) ||
                e.Key.StartsWith(ConfigurationKeys.AI.OllamaEnabled) ||
                e.Key.StartsWith(ConfigurationKeys.AI.OllamaUrl))
            {
                Debug.WriteLine($"AI configuration changed: {e.Key}");
                var models = await GetModels();
                ModelsChanged?.Invoke(this, models);
            }
        }

        public async Task<List<AIModelInfo>> GetModels()
        {
            var models = new List<AIModelInfo>();
            
            Debug.WriteLine("Starting GetModels...");
            Debug.WriteLine("Checking AI provider configurations:");
            Debug.WriteLine($"- OpenAI: Enabled={_config.EnableOpenAI}, Has API Key={!string.IsNullOrEmpty(_config.OpenAIApiKey)}");
            Debug.WriteLine($"- Anthropic: Enabled={_config.EnableAnthropic}, Has API Key={!string.IsNullOrEmpty(_config.AnthropicApiKey)}");
            Debug.WriteLine($"- XAI: Enabled={_config.EnableXAI}, Has API Key={!string.IsNullOrEmpty(_config.XAIApiKey)}");
            Debug.WriteLine($"- Ollama: Enabled={_config.EnableOllama}, Has URL={!string.IsNullOrEmpty(_config.OllamaUrl)}");

            // Only try to load OpenAI models if it's enabled and has an API key
            if (_config.EnableOpenAI && !string.IsNullOrEmpty(_config.OpenAIApiKey))
            {
                try
                {
                    Debug.WriteLine("Loading OpenAI models...");
                    var openAIService = new OpenAIService(_config.OpenAIApiKey);
                    var openAIModels = await openAIService.GetAvailableModels();
                    Debug.WriteLine($"Found {openAIModels.Count} OpenAI models");
                    models.AddRange(openAIModels);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading OpenAI models: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            else
            {
                Debug.WriteLine($"Skipping OpenAI models: Enabled={_config.EnableOpenAI}, Has API Key={!string.IsNullOrEmpty(_config.OpenAIApiKey)}");
            }

            // Only try to load Anthropic models if it's enabled and has an API key
            if (_config.EnableAnthropic && !string.IsNullOrEmpty(_config.AnthropicApiKey))
            {
                try
                {
                    Debug.WriteLine("Loading Anthropic models...");
                    var anthropicService = new AnthropicService(_config.AnthropicApiKey);
                    var anthropicModels = await anthropicService.GetAvailableModels();
                    Debug.WriteLine($"Found {anthropicModels.Count} Anthropic models");
                    models.AddRange(anthropicModels);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading Anthropic models: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            else
            {
                Debug.WriteLine($"Skipping Anthropic models: Enabled={_config.EnableAnthropic}, Has API Key={!string.IsNullOrEmpty(_config.AnthropicApiKey)}");
            }

            // Only try to load XAI models if it's enabled and has an API key
            if (_config.EnableXAI && !string.IsNullOrEmpty(_config.XAIApiKey))
            {
                try
                {
                    Debug.WriteLine("Loading xAI models...");
                    var xaiService = new XAIService(_config.XAIApiKey);
                    var xaiModels = await xaiService.GetAvailableModels();
                    Debug.WriteLine($"Found {xaiModels.Count} xAI models");
                    models.AddRange(xaiModels);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading xAI models: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            else
            {
                Debug.WriteLine($"Skipping XAI models: Enabled={_config.EnableXAI}, Has API Key={!string.IsNullOrEmpty(_config.XAIApiKey)}");
            }

            // Only try to load Ollama models if it's enabled and has a URL
            if (_config.EnableOllama && !string.IsNullOrEmpty(_config.OllamaUrl))
            {
                try
                {
                    Debug.WriteLine("Loading Ollama models...");
                    var ollamaService = new OllamaService(_config.OllamaUrl);
                    var ollamaModels = await ollamaService.GetAvailableModels();
                    Debug.WriteLine($"Found {ollamaModels.Count} Ollama models");
                    models.AddRange(ollamaModels);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error loading Ollama models: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
            }
            else
            {
                Debug.WriteLine($"Skipping Ollama models: Enabled={_config.EnableOllama}, Has URL={!string.IsNullOrEmpty(_config.OllamaUrl)}");
            }

            Debug.WriteLine($"Total models found across all providers: {models.Count}");
            foreach (var model in models)
            {
                Debug.WriteLine($"- {model.DisplayName} ({model.Provider})");
            }
            
            // Notify subscribers of the updated models list
            ModelsChanged?.Invoke(this, models);
            
            return models;
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
                    "grok" => "Grok",
                    "claude" => "Claude",
                    _ => char.ToUpper(part[0]) + (part.Length > 1 ? part.Substring(1).ToLower() : string.Empty)
                };
            });

            return string.Join(" ", formattedParts);
        }

        public string GetApiKey(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => _config.OpenAIApiKey,
                AIProvider.Anthropic => _config.AnthropicApiKey,
                AIProvider.XAI => _config.XAIApiKey,
                AIProvider.Ollama => string.Empty,  // Ollama doesn't need an API key
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
        }
    }
} 