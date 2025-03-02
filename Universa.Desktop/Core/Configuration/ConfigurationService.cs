using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

namespace Universa.Desktop.Core.Configuration
{
    public class ConfigurationService : IConfigurationService
    {
        private readonly ConfigurationProvider _provider;
        public ConfigurationProvider Provider => _provider;
        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        public ConfigurationService(IConfigurationStore store = null)
        {
            var configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "config.json"
            );

            // Create directory if it doesn't exist
            var configDir = Path.GetDirectoryName(configPath);
            if (!Directory.Exists(configDir))
            {
                Directory.CreateDirectory(configDir);
            }

            store ??= new JsonConfigurationStore(configPath);
            _provider = new ConfigurationProvider(store);
            _provider.ConfigurationChanged += (s, e) => ConfigurationChanged?.Invoke(this, e);
        }

        public async Task InitializeAsync()
        {
            await _provider.LoadAsync();
        }

        public void Save()
        {
            _provider.Save();
        }

        public async Task SaveAsync()
        {
            await _provider.SaveAsync();
        }

        public T GetValue<T>(string key)
        {
            return _provider.GetValue<T>(key);
        }

        public void SetValue<T>(string key, T value)
        {
            _provider.SetValue(key, value);
        }

        public void SetIfNotExists<T>(string key, T value)
        {
            _provider.SetIfNotExists(key, value);
        }

        public void ResetToDefaults(IDictionary<string, object> defaultValues)
        {
            _provider.ResetToDefaults(defaultValues);
        }
    }
} 