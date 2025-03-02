using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Universa.Desktop.Properties;

namespace Universa.Desktop.Core.Configuration
{
    public static class ConfigurationStartup
    {
        public static async Task InitializeAsync()
        {
            try
            {
                Debug.WriteLine("ConfigurationStartup: Starting initialization...");
                var configManager = ConfigurationManager.Instance;
                var settings = Settings.Default;

                // Check if we need to migrate settings
                if (!settings.SettingsMigrated)
                {
                    Debug.WriteLine("ConfigurationStartup: Starting configuration migration...");
                    ConfigurationMigration.MigrateFromLegacy(configManager);
                }

                // Apply default values for any missing settings
                Debug.WriteLine("ConfigurationStartup: Applying default values...");
                ConfigurationDefaults.ApplyDefaults(configManager);

                // Log current configuration state
                Debug.WriteLine("ConfigurationStartup: Current configuration state:");
                foreach (var key in configManager.GetAllKeys())
                {
                    var value = configManager.Get<object>(key);
                    if (key.Contains("password", StringComparison.OrdinalIgnoreCase))
                    {
                        Debug.WriteLine($"ConfigurationStartup: {key} = [hidden]");
                    }
                    else
                    {
                        Debug.WriteLine($"ConfigurationStartup: {key} = {value}");
                    }
                }

                // Validate required settings
                if (!ConfigurationDefaults.ValidateConfiguration(configManager, out var missingSettings))
                {
                    Debug.WriteLine($"ConfigurationStartup: Missing required settings: {string.Join(", ", missingSettings)}");
                }

                // Initialize the configuration provider
                Debug.WriteLine("ConfigurationStartup: Initializing configuration provider...");
                var provider = ConfigurationProvider.Instance;

                // Save any changes
                Debug.WriteLine("ConfigurationStartup: Saving configuration...");
                configManager.Save();

                Debug.WriteLine("ConfigurationStartup: Initialization complete.");
                await Task.CompletedTask; // For future async operations
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigurationStartup: Error initializing configuration: {ex.Message}");
                Debug.WriteLine($"ConfigurationStartup: Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public static void Reset()
        {
            try
            {
                Debug.WriteLine("ConfigurationStartup: Resetting configuration to defaults...");
                var configManager = ConfigurationManager.Instance;
                ConfigurationDefaults.ResetToDefaults(configManager);
                Debug.WriteLine("ConfigurationStartup: Configuration reset complete.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ConfigurationStartup: Error resetting configuration: {ex.Message}");
                Debug.WriteLine($"ConfigurationStartup: Stack trace: {ex.StackTrace}");
                throw;
            }
        }
    }
} 