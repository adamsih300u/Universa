using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Universa.Desktop.Core.Configuration
{
    public interface IConfigurationService
    {
        ConfigurationProvider Provider { get; }
        event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;
        Task InitializeAsync();
        void Save();
        Task SaveAsync();
        T GetValue<T>(string key);
        void SetValue<T>(string key, T value);
        void SetIfNotExists<T>(string key, T value);
        void ResetToDefaults(IDictionary<string, object> defaultValues);
    }
} 