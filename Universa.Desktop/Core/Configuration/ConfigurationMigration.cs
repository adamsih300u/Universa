using System;
using System.Diagnostics;
using Universa.Desktop.Properties;
using System.Collections.Generic;

namespace Universa.Desktop.Core.Configuration
{
    public static class ConfigurationMigration
    {
        public static void MigrateConfiguration(ConfigurationManager configManager)
        {
            // Migrate old settings to new format if needed
            var oldSettings = new Dictionary<string, string>();
            foreach (var key in configManager.GetAllKeys())
            {
                var value = configManager.Get<string>(key);
                if (!string.IsNullOrEmpty(value))
                {
                    oldSettings[key] = value;
                }
            }

            // Apply migrations here
            foreach (var kvp in oldSettings)
            {
                configManager.Set(kvp.Key, kvp.Value);
            }

            configManager.Save();
        }

        public static void MigrateFromLegacy(ConfigurationManager configManager)
        {
            try
            {
                Debug.WriteLine("Starting configuration migration...");
                var settings = Settings.Default;
                var oldConfig = Models.Configuration.Instance;

                // Migrate Sync Settings
                configManager.Set(ConfigurationKeys.Sync.ServerUrl, oldConfig.SyncServerUrl);
                configManager.Set(ConfigurationKeys.Sync.Username, oldConfig.SyncUsername);
                configManager.Set(ConfigurationKeys.Sync.Password, oldConfig.SyncPassword);
                configManager.Set(ConfigurationKeys.Sync.AutoSync, oldConfig.AutoSync);
                configManager.Set(ConfigurationKeys.Sync.IntervalMinutes, oldConfig.SyncIntervalMinutes);

                // Migrate Weather Settings
                configManager.Set(ConfigurationKeys.Weather.ApiKey, oldConfig.WeatherApiKey);
                configManager.Set(ConfigurationKeys.Weather.ZipCode, oldConfig.WeatherZipCode);
                configManager.Set(ConfigurationKeys.Weather.Enabled, oldConfig.EnableWeather);
                configManager.Set(ConfigurationKeys.Weather.MoonPhaseEnabled, oldConfig.EnableMoonPhase);

                // Migrate Library Settings
                configManager.Set(ConfigurationKeys.Library.Path, oldConfig.LibraryPath);
                configManager.Set(ConfigurationKeys.Library.LastWidth, oldConfig.LastLibraryWidth);
                configManager.Set(ConfigurationKeys.Library.IsExpanded, oldConfig.IsLibraryExpanded);
                configManager.Set(ConfigurationKeys.Library.ExpandedPaths, oldConfig.ExpandedPaths);
                configManager.Set(ConfigurationKeys.Library.RecentFiles, oldConfig.RecentFiles);
                configManager.Set(ConfigurationKeys.Library.OpenTabs, oldConfig.OpenTabs);

                // Migrate AI Settings
                configManager.Set(ConfigurationKeys.AI.OpenAIEnabled, oldConfig.EnableOpenAI);
                configManager.Set(ConfigurationKeys.AI.OpenAIApiKey, oldConfig.OpenAIApiKey);
                configManager.Set(ConfigurationKeys.AI.AnthropicEnabled, oldConfig.EnableAnthropic);
                configManager.Set(ConfigurationKeys.AI.AnthropicApiKey, oldConfig.AnthropicApiKey);
                configManager.Set(ConfigurationKeys.AI.XAIEnabled, oldConfig.EnableXAI);
                configManager.Set(ConfigurationKeys.AI.XAIApiKey, oldConfig.XAIApiKey);
                configManager.Set(ConfigurationKeys.AI.OllamaEnabled, oldConfig.EnableOllama);
                configManager.Set(ConfigurationKeys.AI.OllamaUrl, oldConfig.OllamaUrl);
                configManager.Set(ConfigurationKeys.AI.OllamaModel, oldConfig.OllamaModel);
                configManager.Set(ConfigurationKeys.AI.CharacterizationEnabled, oldConfig.EnableAICharacterization);
                configManager.Set(ConfigurationKeys.AI.LocalEmbeddingsEnabled, oldConfig.EnableLocalEmbeddings);
                configManager.Set(ConfigurationKeys.AI.ChatEnabled, oldConfig.EnableAIChat);

                // Migrate Theme Settings
                configManager.Set(ConfigurationKeys.Theme.Current, oldConfig.CurrentTheme);
                configManager.Set(ConfigurationKeys.Theme.DarkModePlayingColor, oldConfig.DarkModePlayingColor);
                configManager.Set(ConfigurationKeys.Theme.LightModePlayingColor, oldConfig.LightModePlayingColor);
                configManager.Set(ConfigurationKeys.Theme.DarkModePausedColor, oldConfig.DarkModePausedColor);
                configManager.Set(ConfigurationKeys.Theme.LightModePausedColor, oldConfig.LightModePausedColor);
                configManager.Set(ConfigurationKeys.Theme.DarkModeTextColor, oldConfig.DarkModeTextColor);
                configManager.Set(ConfigurationKeys.Theme.LightModeTextColor, oldConfig.LightModeTextColor);

                // Migrate Media Settings
                configManager.Set(ConfigurationKeys.Media.LastVolume, oldConfig.LastVolume);
                configManager.Set(ConfigurationKeys.Media.OpenTabs, oldConfig.OpenTabs);

                // Migrate Service Settings
                // Subsonic
                configManager.Set(ConfigurationKeys.Services.Subsonic.Url, oldConfig.SubsonicUrl);
                configManager.Set(ConfigurationKeys.Services.Subsonic.Username, oldConfig.SubsonicUsername);
                configManager.Set(ConfigurationKeys.Services.Subsonic.Password, oldConfig.SubsonicPassword);
                configManager.Set(ConfigurationKeys.Services.Subsonic.Name, oldConfig.SubsonicName);

                // Jellyfin
                configManager.Set(ConfigurationKeys.Services.Jellyfin.Url, oldConfig.JellyfinUrl);
                configManager.Set(ConfigurationKeys.Services.Jellyfin.Username, oldConfig.JellyfinUsername);
                configManager.Set(ConfigurationKeys.Services.Jellyfin.Password, oldConfig.JellyfinPassword);
                configManager.Set(ConfigurationKeys.Services.Jellyfin.Name, oldConfig.JellyfinName);

                // Matrix
                configManager.Set(ConfigurationKeys.Services.Matrix.Url, oldConfig.MatrixServerUrl);
                configManager.Set(ConfigurationKeys.Services.Matrix.Username, oldConfig.MatrixUsername);
                configManager.Set(ConfigurationKeys.Services.Matrix.Password, oldConfig.MatrixPassword);
                configManager.Set(ConfigurationKeys.Services.Matrix.Name, oldConfig.MatrixName);

                // Save the migrated configuration
                configManager.Save();
                Debug.WriteLine("Configuration migration completed successfully");

                // Mark settings as migrated
                settings.SettingsMigrated = true;
                settings.Save();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during configuration migration: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
} 