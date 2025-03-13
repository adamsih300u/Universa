using System.Collections.Generic;
using System.IO;
using System;

namespace Universa.Desktop.Core.Configuration
{
    public static class ConfigurationDefaults
    {
        public static readonly IDictionary<string, object> DefaultValues = new Dictionary<string, object>
        {
            // Theme Settings
            { ConfigurationKeys.Theme.Current, "Light" },
            { ConfigurationKeys.Theme.DarkModePlayingColor, "#4CAF50" },
            { ConfigurationKeys.Theme.LightModePlayingColor, "#2E7D32" },
            { ConfigurationKeys.Theme.DarkModePausedColor, "#9E9E9E" },
            { ConfigurationKeys.Theme.LightModePausedColor, "#757575" },
            { ConfigurationKeys.Theme.DarkModeTextColor, "#FFFFFF" },
            { ConfigurationKeys.Theme.LightModeTextColor, "#000000" },

            // Library Settings
            { ConfigurationKeys.Library.LastWidth, 250.0 },
            { ConfigurationKeys.Library.IsExpanded, true },
            // Library Path is required and must be set by user

            // Media Settings
            { ConfigurationKeys.Media.LastVolume, 0.5 },

            // AI Settings
            { ConfigurationKeys.AI.OpenAIEnabled, false },
            { ConfigurationKeys.AI.AnthropicEnabled, false },
            { ConfigurationKeys.AI.XAIEnabled, false },
            { ConfigurationKeys.AI.OllamaEnabled, false },
            { ConfigurationKeys.AI.OllamaUrl, "http://localhost:11434" },
            { ConfigurationKeys.AI.OllamaModel, "llama2" },
            { ConfigurationKeys.AI.ChatEnabled, true },
            { ConfigurationKeys.AI.UseBetaChains, false }
        };

        public static readonly ISet<string> RequiredSettings = new HashSet<string>
        {
            ConfigurationKeys.Library.Path
        };

        public static readonly IDictionary<string, string> ValidationRegex = new Dictionary<string, string>
        {
            { ConfigurationKeys.AI.OllamaUrl, @"^(http|https)://[a-zA-Z0-9\-\.]+\.[a-zA-Z]{2,}(:[0-9]{1,5})?(/.*)?$" }
        };

        public static void ApplyDefaults(IConfigurationStore store)
        {
            foreach (var kvp in DefaultValues)
            {
                store.SetIfNotExists(kvp.Key, kvp.Value);
            }
            store.Save();
        }

        public static bool ValidateConfiguration(IConfigurationStore store, out List<string> missingSettings)
        {
            return store.ValidateRequiredSettings(RequiredSettings, out missingSettings);
        }

        public static void ResetToDefaults(IConfigurationStore store)
        {
            store.ResetToDefaults(DefaultValues);
        }

        private static string GetDefaultLibraryPath()
        {
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (string.IsNullOrEmpty(documentsPath))
                {
                    // Fallback to user profile directory if Documents is not available
                    documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                }
                
                if (string.IsNullOrEmpty(documentsPath))
                {
                    // Final fallback to current directory if all else fails
                    documentsPath = AppDomain.CurrentDomain.BaseDirectory;
                }

                return Path.Combine(documentsPath, "Universa");
            }
            catch (Exception ex)
            {
                // If all else fails, use the application's base directory
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Universa");
            }
        }
    }
} 