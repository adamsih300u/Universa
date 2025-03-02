using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Universa.Desktop.Core.Configuration
{
    public class JsonConfigurationStore : IConfigurationStore
    {
        private readonly string _filePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private Configuration _configuration;
        private bool _isDirty;

        public JsonConfigurationStore(string filePath)
        {
            Debug.WriteLine($"Initializing JsonConfigurationStore with path: {filePath}");
            _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            _configuration = new Configuration();
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            Debug.WriteLine($"Getting value for key: {key}");
            try
            {
                if (_configuration.Values.TryGetValue(key, out var value))
                {
                    try
                    {
                        if (value is T typedValue)
                        {
                            Debug.WriteLine($"Found value of correct type: {typedValue}");
                            return typedValue;
                        }

                        if (value is JsonElement jsonElement)
                        {
                            var converted = jsonElement.Deserialize<T>();
                            Debug.WriteLine($"Converted JsonElement to type {typeof(T)}: {converted}");
                            return converted;
                        }

                        var result = (T)Convert.ChangeType(value, typeof(T));
                        Debug.WriteLine($"Converted value to type {typeof(T)}: {result}");
                        return result;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error converting value: {ex.Message}");
                        return defaultValue;
                    }
                }

                Debug.WriteLine($"Key not found, returning default value: {defaultValue}");
                return defaultValue;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Get<T>: {ex.Message}");
                return defaultValue;
            }
        }

        public void Set<T>(string key, T value)
        {
            Debug.WriteLine($"Setting value for key: {key}, value: {value}");
            try
            {
                _configuration.Values[key] = value;
                _isDirty = true;
                Debug.WriteLine("Value set successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Set<T>: {ex.Message}");
                throw;
            }
        }

        public bool HasKey(string key)
        {
            return _configuration.Values.ContainsKey(key);
        }

        public void RemoveKey(string key)
        {
            if (_configuration.Values.Remove(key))
            {
                _isDirty = true;
            }
        }

        public IEnumerable<string> GetAllKeys()
        {
            return _configuration.Values.Keys;
        }

        public void Save()
        {
            if (!_isDirty) return;

            Debug.WriteLine($"Saving configuration to: {_filePath}");
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!Directory.Exists(directory))
                {
                    Debug.WriteLine($"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_configuration, _jsonOptions);
                File.WriteAllText(_filePath, json);
                _isDirty = false;
                Debug.WriteLine("Configuration saved successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving configuration: {ex.Message}");
                throw;
            }
        }

        public async Task<Configuration> LoadAsync()
        {
            Debug.WriteLine($"Loading configuration from: {_filePath}");
            try
            {
                if (!File.Exists(_filePath))
                {
                    Debug.WriteLine("Configuration file does not exist, creating new configuration");
                    _configuration = new Configuration
                    {
                        Values = new Dictionary<string, object>()
                    };
                    return _configuration;
                }

                var json = await File.ReadAllTextAsync(_filePath);
                Debug.WriteLine($"Read configuration file content: {json}");

                _configuration = JsonSerializer.Deserialize<Configuration>(json, _jsonOptions);
                if (_configuration == null)
                {
                    Debug.WriteLine("Deserialized configuration is null, creating new configuration");
                    _configuration = new Configuration
                    {
                        Values = new Dictionary<string, object>()
                    };
                }
                else if (_configuration.Values == null)
                {
                    Debug.WriteLine("Configuration values dictionary is null, initializing");
                    _configuration.Values = new Dictionary<string, object>();
                }

                Debug.WriteLine($"Loaded {_configuration.Values.Count} configuration values");
                foreach (var kvp in _configuration.Values)
                {
                    Debug.WriteLine($"Loaded key: {kvp.Key}, value: {kvp.Value}");
                }

                return _configuration;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading configuration: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public void Save(Configuration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            Save();
        }

        public async Task SaveAsync(Configuration configuration)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            await Task.Run(() => Save());
        }

        public bool ValidateRequiredSettings(ISet<string> requiredSettings, out List<string> missingSettings)
        {
            missingSettings = new List<string>();
            foreach (var key in requiredSettings)
            {
                if (!HasKey(key))
                {
                    missingSettings.Add(key);
                }
            }
            return missingSettings.Count == 0;
        }

        public void SetIfNotExists<T>(string key, T value)
        {
            if (!HasKey(key))
            {
                Set(key, value);
            }
        }

        public void ResetToDefaults(IDictionary<string, object> defaultValues)
        {
            _configuration.Values.Clear();
            foreach (var kvp in defaultValues)
            {
                _configuration.Values[kvp.Key] = kvp.Value;
            }
            _isDirty = true;
            Save();
        }
    }
} 