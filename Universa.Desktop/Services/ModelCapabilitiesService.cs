using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Centralized service for managing and caching AI model capabilities including token limits.
    /// Provides dynamic capabilities fetching where supported and fallback lookups for all providers.
    /// </summary>
    public class ModelCapabilitiesService
    {
        private readonly ConcurrentDictionary<string, ModelCapabilities> _capabilitiesCache = new();
        private readonly Dictionary<string, int> _knownContextLengths;
        private DateTime _lastCacheUpdate = DateTime.MinValue;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(6); // Cache for 6 hours

        public ModelCapabilitiesService()
        {
            Debug.WriteLine("Initializing ModelCapabilitiesService with known context lengths");
            _knownContextLengths = InitializeKnownContextLengths();
        }

        /// <summary>
        /// Gets the maximum output tokens for a model, considering both context window and provider limits
        /// </summary>
        public async Task<int> GetMaxOutputTokensAsync(string modelId, AIProvider provider)
        {
            var capabilities = await GetModelCapabilitiesAsync(modelId, provider);
            
            // Simple approach: use the specified max output tokens (which is 64k)
            // Let the API/model handle any actual limits
            return capabilities.MaxOutputTokens ?? 65536;
        }

        /// <summary>
        /// Gets comprehensive model capabilities with caching
        /// </summary>
        public async Task<ModelCapabilities> GetModelCapabilitiesAsync(string modelId, AIProvider provider)
        {
            string cacheKey = $"{provider}:{modelId}";
            
            // Check cache first
            if (_capabilitiesCache.TryGetValue(cacheKey, out var cachedCapabilities) && 
                DateTime.UtcNow - _lastCacheUpdate < _cacheExpiry)
            {
                return cachedCapabilities;
            }

            // Try to fetch dynamic capabilities
            var capabilities = await TryGetDynamicCapabilitiesAsync(modelId, provider);
            
            // Fallback to known capabilities
            if (capabilities == null)
            {
                capabilities = GetKnownCapabilities(modelId, provider);
            }

            // Cache the result
            _capabilitiesCache[cacheKey] = capabilities;
            _lastCacheUpdate = DateTime.UtcNow;
            
            Debug.WriteLine($"Cached capabilities for {modelId}: Context={capabilities.ContextLength}, Output={capabilities.MaxOutputTokens}");
            return capabilities;
        }

        /// <summary>
        /// Attempts to fetch dynamic capabilities from provider APIs
        /// </summary>
        private async Task<ModelCapabilities> TryGetDynamicCapabilitiesAsync(string modelId, AIProvider provider)
        {
            try
            {
                switch (provider)
                {
                    case AIProvider.OpenRouter:
                        // OpenRouter provides context_length in their models API
                        // This will be implemented when we modify OpenRouterService
                        return null; // Will be handled by OpenRouterService integration
                    
                    default:
                        return null; // No dynamic capabilities available
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to fetch dynamic capabilities for {modelId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Gets known capabilities from lookup table
        /// </summary>
        private ModelCapabilities GetKnownCapabilities(string modelId, AIProvider provider)
        {
            string lookupKey = GetLookupKey(modelId, provider);
            
            Debug.WriteLine($"Looking up capabilities for model '{modelId}' with lookup key '{lookupKey}'");
            
            if (_knownContextLengths.TryGetValue(lookupKey, out int contextLength))
            {
                var maxOutputTokens = GetKnownMaxOutputTokens(modelId, provider, contextLength);
                Debug.WriteLine($"Found known capabilities for '{lookupKey}': Context={contextLength}, Output={maxOutputTokens}");
                
                return new ModelCapabilities
                {
                    ModelId = modelId,
                    Provider = provider,
                    ContextLength = contextLength,
                    MaxOutputTokens = maxOutputTokens,
                    SupportsStreaming = true, // Most modern models support streaming
                    SupportsImages = IsVisionModel(modelId),
                    SupportsToolCalling = IsToolCallingModel(modelId, provider),
                    SupportsReasoning = ModelCapabilityHelpers.SupportsReasoningTokens(modelId, provider)
                };
            }

            Debug.WriteLine($"No known capabilities found for '{lookupKey}', using conservative defaults");
            
            // Conservative defaults
            return new ModelCapabilities
            {
                ModelId = modelId,
                Provider = provider,
                ContextLength = 4096,
                MaxOutputTokens = 65536, // Use 64k for all models
                SupportsStreaming = true,
                SupportsImages = false,
                SupportsToolCalling = false,
                SupportsReasoning = ModelCapabilityHelpers.SupportsReasoningTokens(modelId, provider)
            };
        }

        /// <summary>
        /// Initialize known context lengths for all major models
        /// </summary>
        private Dictionary<string, int> InitializeKnownContextLengths()
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                // OpenAI Models
                ["gpt-4o"] = 128000,
                ["gpt-4o-2024-11-20"] = 128000,
                ["gpt-4o-2024-08-06"] = 128000,
                ["gpt-4o-2024-05-13"] = 128000,
                ["chatgpt-4o-latest"] = 128000,
                ["gpt-4o-mini"] = 128000,
                ["gpt-4o-mini-2024-07-18"] = 128000,
                ["o1"] = 200000,
                ["o1-2024-12-17"] = 200000,
                ["o1-mini"] = 128000,
                ["o1-mini-2024-09-12"] = 128000,
                ["o1-preview"] = 128000,
                ["o1-preview-2024-09-12"] = 128000,
                ["o3-mini"] = 200000,
                ["o3-mini-2025-01-31"] = 200000,
                ["gpt-4-turbo"] = 128000,
                ["gpt-4-turbo-2024-04-09"] = 128000,
                ["gpt-4-0125-preview"] = 128000,
                ["gpt-4-1106-preview"] = 128000,
                ["gpt-4"] = 8192,
                ["gpt-4-0613"] = 8192,
                ["gpt-4-0314"] = 8192,
                ["gpt-3.5-turbo"] = 16385,
                ["gpt-3.5-turbo-0125"] = 16385,
                ["gpt-3.5-turbo-1106"] = 16385,

                // Anthropic Models
                ["claude-3.5-sonnet"] = 200000,
                ["claude-3.5-sonnet-20241022"] = 200000,
                ["claude-3.5-sonnet-20240620"] = 200000,
                ["claude-3-opus"] = 200000,
                ["claude-3-opus-20240229"] = 200000,
                ["claude-3-sonnet"] = 200000,
                ["claude-3-sonnet-20240229"] = 200000,
                ["claude-3-haiku"] = 200000,
                ["claude-3-haiku-20240307"] = 200000,
                
                // OpenRouter Anthropic Models (with provider prefix)
                ["anthropic/claude-3.5-sonnet"] = 200000,
                ["anthropic/claude-3.5-sonnet-20241022"] = 200000,
                ["anthropic/claude-3.5-sonnet-20240620"] = 200000,
                ["anthropic/claude-3-opus"] = 200000,
                ["anthropic/claude-3-opus-20240229"] = 200000,
                ["anthropic/claude-3-sonnet"] = 200000,
                ["anthropic/claude-3-sonnet-20240229"] = 200000,
                ["anthropic/claude-3-haiku"] = 200000,
                ["anthropic/claude-3-haiku-20240307"] = 200000,
                ["anthropic/claude-sonnet-4"] = 200000, // Claude 4 Sonnet via OpenRouter
                ["anthropic/claude-sonnet-4.5"] = 200000, // Claude 4.5 Sonnet
                ["claude-sonnet-4.5"] = 200000, // Claude 4.5 Sonnet (without prefix)

                // XAI Models  
                ["grok-2-1212"] = 131072,
                ["grok-2"] = 131072,
                ["grok-beta"] = 131072,

                // Popular OpenRouter Models (will be overridden by dynamic data)
                ["meta-llama/llama-3.1-405b-instruct"] = 131072,
                ["meta-llama/llama-3.1-70b-instruct"] = 131072,
                ["meta-llama/llama-3.1-8b-instruct"] = 131072,
                ["meta-llama/llama-3-70b-instruct"] = 8192,
                ["meta-llama/llama-3-8b-instruct"] = 8192,
                ["mistralai/mixtral-8x7b-instruct"] = 32768,
                ["mistralai/mixtral-8x22b-instruct"] = 65536,
                ["google/gemini-pro"] = 1048576,
                ["google/gemini-pro-1.5"] = 1048576,
                ["google/gemini-flash-1.5"] = 1048576,

                // Ollama Models (common defaults)
                ["llama3.1"] = 131072,
                ["llama3"] = 8192,
                ["llama2"] = 4096,
                ["mixtral"] = 32768,
                ["codellama"] = 16384,
                ["mistral"] = 8192,
                ["gemma"] = 8192
            };
        }



        /// <summary>
        /// Gets known max output tokens for specific models
        /// </summary>
        private int? GetKnownMaxOutputTokens(string modelId, AIProvider provider, int contextLength)
        {
            string lowerModelId = modelId.ToLowerInvariant();
            
            // Claude models have specific output limits based on version
            if (lowerModelId.Contains("claude"))
            {
                // Claude 4.5 Sonnet supports 64K output tokens
                if (lowerModelId.Contains("sonnet-4.5") || lowerModelId.Contains("sonnet-4-5"))
                {
                    return 64000;
                }
                // Claude 4 Sonnet and earlier versions support 8K output tokens
                return 8192;
            }
            
            // Simple approach: use 64k output tokens for all other models
            // Let the API/model handle any actual limits
            return 65536;
        }

        /// <summary>
        /// Creates lookup key for model capabilities
        /// </summary>
        private string GetLookupKey(string modelId, AIProvider provider)
        {
            // Strip provider prefixes for OpenRouter models
            if (provider == AIProvider.OpenRouter && modelId.StartsWith("openrouter/"))
            {
                return modelId.Substring(11);
            }
            
            return modelId;
        }

        /// <summary>
        /// Checks if a model supports vision/image inputs
        /// </summary>
        private bool IsVisionModel(string modelId)
        {
            var lowerModelId = modelId.ToLowerInvariant();
            return lowerModelId.Contains("gpt-4o") || 
                   lowerModelId.Contains("gpt-4-turbo") ||
                   lowerModelId.Contains("gpt-4-vision") ||
                   lowerModelId.Contains("claude-3") ||
                   lowerModelId.Contains("gemini");
        }

        /// <summary>
        /// Checks if a model supports tool/function calling
        /// </summary>
        private bool IsToolCallingModel(string modelId, AIProvider provider)
        {
            var lowerModelId = modelId.ToLowerInvariant();
            
            return provider switch
            {
                AIProvider.OpenAI => !lowerModelId.Contains("o1"), // O1 models don't support tools yet
                AIProvider.Anthropic => lowerModelId.Contains("claude-3"),
                AIProvider.OpenRouter => true, // Most OpenRouter models support tools
                _ => false
            };
        }

        /// <summary>
        /// Forces refresh of cached capabilities
        /// </summary>
        public void RefreshCache()
        {
            _capabilitiesCache.Clear();
            _lastCacheUpdate = DateTime.MinValue;
            Debug.WriteLine("ModelCapabilitiesService cache cleared");
        }

        /// <summary>
        /// Forces refresh of capabilities for a specific provider
        /// </summary>
        public void RefreshProviderCache(AIProvider provider)
        {
            var keysToRemove = _capabilitiesCache.Keys
                .Where(key => key.StartsWith($"{provider}:"))
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                _capabilitiesCache.TryRemove(key, out _);
            }
            
            Debug.WriteLine($"ModelCapabilitiesService cache cleared for provider: {provider}");
        }

        /// <summary>
        /// Gets cache statistics for monitoring
        /// </summary>
        public (int CachedModels, DateTime LastUpdate, TimeSpan CacheAge) GetCacheStats()
        {
            var cacheAge = DateTime.UtcNow - _lastCacheUpdate;
            return (_capabilitiesCache.Count, _lastCacheUpdate, cacheAge);
        }

        /// <summary>
        /// Updates capabilities for a specific model (used by services that fetch dynamic data)
        /// </summary>
        public void UpdateModelCapabilities(string modelId, AIProvider provider, ModelCapabilities capabilities)
        {
            string cacheKey = $"{provider}:{modelId}";
            _capabilitiesCache[cacheKey] = capabilities;
            _lastCacheUpdate = DateTime.UtcNow; // Update cache timestamp
            Debug.WriteLine($"Updated capabilities for {modelId}: Context={capabilities.ContextLength}, Output={capabilities.MaxOutputTokens}");
        }
    }

    /// <summary>
    /// Represents comprehensive model capabilities
    /// </summary>
    public class ModelCapabilities
    {
        public string ModelId { get; set; }
        public AIProvider Provider { get; set; }
        public int ContextLength { get; set; }
        public int? MaxOutputTokens { get; set; }
        public bool SupportsStreaming { get; set; }
        public bool SupportsImages { get; set; }
        public bool SupportsToolCalling { get; set; }
        public bool SupportsReasoning { get; set; }
        public string[] SupportedFormats { get; set; } = Array.Empty<string>();
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}

/// <summary>
/// Helper methods for model capability detection
/// </summary>
public static class ModelCapabilityHelpers
{
    /// <summary>
    /// Checks if a model supports reasoning tokens
    /// </summary>
    public static bool SupportsReasoningTokens(string modelId, AIProvider provider)
    {
        string lowerModelId = modelId.ToLowerInvariant();
        
        // Strip OpenRouter prefix if present
        if (lowerModelId.StartsWith("openrouter/"))
        {
            lowerModelId = lowerModelId.Substring(11);
        }
        
        // OpenAI reasoning models (o1, o3 series, GPT-5 series)
        if (lowerModelId.Contains("o1") || lowerModelId.Contains("o3") || 
            lowerModelId.Contains("gpt-5") || lowerModelId.StartsWith("openai/o1") || 
            lowerModelId.StartsWith("openai/o3") || lowerModelId.StartsWith("openai/gpt-5"))
        {
            return true;
        }
        
        // Anthropic reasoning models (Claude Sonnet 4+, extended thinking)
        if (lowerModelId.Contains("claude-sonnet-4") || lowerModelId.Contains("claude-4") ||
            lowerModelId.Contains("anthropic/claude-sonnet-4") || lowerModelId.Contains("anthropic/claude-4"))
        {
            return true;
        }
        
        // Gemini thinking models
        if (lowerModelId.Contains("gemini") && (lowerModelId.Contains("thinking") || lowerModelId.Contains("flash-thinking")))
        {
            return true;
        }
        
        // Grok reasoning models - Grok 2+ supports reasoning with effort parameter
        // According to OpenRouter docs: https://openrouter.ai/docs/use-cases/reasoning-tokens
        // Grok models support reasoning.effort parameter
        // Model IDs can be: "x-ai/grok-4", "x-ai/grok-2", "grok-2", "grok-4", "openrouter/x-ai/grok-4", etc.
        if (lowerModelId.Contains("grok"))
        {
            // All modern Grok models (2+) support reasoning via effort parameter
            // Match patterns like: grok-2, grok-4, x-ai/grok-4, x-ai/grok-2, etc.
            // The pattern "x-ai/grok" will match "x-ai/grok-4", "x-ai/grok-2", etc.
            if (lowerModelId.Contains("grok-2") || lowerModelId.Contains("grok-4") || 
                lowerModelId.Contains("grok-beta") || lowerModelId.Contains("x-ai/grok") ||
                lowerModelId.StartsWith("grok-") || lowerModelId.Contains("/grok-"))
            {
                Debug.WriteLine($"Detected Grok reasoning model: {modelId}");
                return true;
            }
        }
        
        return false;
    }
} 