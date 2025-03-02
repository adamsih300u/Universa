using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Universa.Desktop.Core.Configuration
{
    public interface IConfigurationStore
    {
        T Get<T>(string key, T defaultValue = default);
        void Set<T>(string key, T value);
        bool HasKey(string key);
        void RemoveKey(string key);
        IEnumerable<string> GetAllKeys();
        void Save();
        bool ValidateRequiredSettings(ISet<string> requiredSettings, out List<string> missingSettings);
        void SetIfNotExists<T>(string key, T value);
        void ResetToDefaults(IDictionary<string, object> defaultValues);
        Task<Configuration> LoadAsync();
        void Save(Configuration configuration);
        Task SaveAsync(Configuration configuration);
    }
} 