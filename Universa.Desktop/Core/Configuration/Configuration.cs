using System;
using System.IO;
using System.Text.Json;
using System.Diagnostics;
using System.Collections.Generic;

namespace Universa.Desktop.Core.Configuration
{
    public class Configuration
    {
        private static Configuration _instance;
        private static readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Universa",
            "config.json"
        );

        public static Configuration Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = Load();
                }
                return _instance;
            }
        }

        // Dictionary to store configuration values
        public Dictionary<string, object> Values { get; set; } = new Dictionary<string, object>();

        // Configuration properties
        public string OpenAIApiKey { get; set; }
        public string AnthropicApiKey { get; set; }
        public string XAIApiKey { get; set; }
        public bool EnableOpenAI { get; set; } = false;
        public bool EnableAnthropic { get; set; } = false;
        public bool EnableXAI { get; set; } = false;
        public bool EnableOllama { get; set; } = false;
        public string LastUsedModel { get; set; }
        public bool UseBetaChains { get; set; } = false;
        public string Theme { get; set; } = "Light";

        private static Configuration Load()
        {
            try
            {
                var configDir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<Configuration>(json);
                    if (config != null)
                    {
                        config.Values ??= new Dictionary<string, object>();
                        return config;
                    }
                }

                return CreateDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading configuration: {ex.Message}");
                return CreateDefault();
            }
        }

        private static Configuration CreateDefault()
        {
            var config = new Configuration
            {
                Values = new Dictionary<string, object>(),
                EnableOpenAI = false,
                EnableAnthropic = false,
                EnableXAI = false,
                EnableOllama = false,
                Theme = "Light",
                UseBetaChains = false
            };
            config.Save();
            return config;
        }

        public void Save()
        {
            try
            {
                var configDir = Path.GetDirectoryName(_configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving configuration: {ex.Message}");
            }
        }
    }
}