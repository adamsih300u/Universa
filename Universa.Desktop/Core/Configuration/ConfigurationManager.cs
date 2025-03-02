using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Universa.Desktop.Core.Configuration
{
    public class ConfigurationManager : IConfigurationStore
    {
        private static ConfigurationManager _instance;
        private static readonly object _lock = new object();
        private readonly Dictionary<string, object> _settings;
        private readonly string _configPath;
        private bool _isDirty;
        private bool _isGetting;
        private bool _isSetting;
        private readonly object _accessLock = new object();
        private readonly JsonSerializerOptions _jsonOptions;
        private Configuration _configuration;

        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        private ConfigurationManager()
        {
            _settings = new Dictionary<string, object>();
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "config.json"
            );
            _jsonOptions = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            _configuration = new Configuration();
            Load();
        }

        public static ConfigurationManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConfigurationManager();
                    }
                }
                return _instance;
            }
        }

        public T Get<T>(string key, T defaultValue = default)
        {
            lock (_accessLock)
            {
                if (_isGetting)
                    return defaultValue;

                try
                {
                    _isGetting = true;
                    if (_settings.TryGetValue(key, out var value))
                    {
                        try
                        {
                            if (value is T typedValue)
                                return typedValue;

                            if (value is JsonElement jsonElement)
                            {
                                return jsonElement.Deserialize<T>();
                            }

                            return (T)Convert.ChangeType(value, typeof(T));
                        }
                        catch
                        {
                            return defaultValue;
                        }
                    }
                    return defaultValue;
                }
                finally
                {
                    _isGetting = false;
                }
            }
        }

        public void Set<T>(string key, T value)
        {
            lock (_accessLock)
            {
                if (_isSetting)
                    return;

                try
                {
                    _isSetting = true;
                    var oldValue = _settings.ContainsKey(key) ? _settings[key] : null;
                    _settings[key] = value;
                    _isDirty = true;

                    if (!_isGetting)
                    {
                        OnConfigurationChanged(key, oldValue, value);
                    }
                }
                finally
                {
                    _isSetting = false;
                }
            }
        }

        public bool HasKey(string key)
        {
            return _settings.ContainsKey(key);
        }

        public void RemoveKey(string key)
        {
            if (_settings.Remove(key))
            {
                _isDirty = true;
                OnConfigurationChanged(key, null, null);
            }
        }

        public IEnumerable<string> GetAllKeys()
        {
            return _settings.Keys;
        }

        public void Save()
        {
            lock (_accessLock)
            {
                if (!_isDirty) return;

                try
                {
                    var directory = Path.GetDirectoryName(_configPath);
                    if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                    var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                    File.WriteAllText(_configPath, json);
                    _isDirty = false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error saving configuration: {ex.Message}");
                }
            }
        }

        private void Load()
        {
            lock (_accessLock)
            {
                if (File.Exists(_configPath))
                {
                    try
                    {
                        var json = File.ReadAllText(_configPath);
                        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                        if (settings != null)
                        {
                            _settings.Clear();
                            foreach (var kvp in settings)
                            {
                                _settings[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading configuration: {ex.Message}");
                    }
                }
            }
        }

        public bool ValidateRequiredSettings(ISet<string> requiredSettings, out List<string> missingSettings)
        {
            missingSettings = requiredSettings.Where(key => !HasKey(key)).ToList();
            return !missingSettings.Any();
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
            _settings.Clear();
            foreach (var kvp in defaultValues)
            {
                _settings[kvp.Key] = kvp.Value;
            }
            _isDirty = true;
            Save();
        }

        public async Task<Configuration> LoadAsync()
        {
            if (!File.Exists(_configPath))
            {
                _configuration = new Configuration();
                return _configuration;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_configPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json, _jsonOptions);
                if (settings != null)
                {
                    _settings.Clear();
                    foreach (var kvp in settings)
                    {
                        _settings[kvp.Key] = kvp.Value;
                    }
                }
                _configuration = new Configuration { Values = _settings };
                return _configuration;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading configuration: {ex.Message}");
                _configuration = new Configuration();
                return _configuration;
            }
        }

        public void Save(Configuration configuration)
        {
            _configuration = configuration;
            _settings.Clear();
            foreach (var kvp in configuration.Values)
            {
                _settings[kvp.Key] = kvp.Value;
            }
            Save();
        }

        public async Task SaveAsync(Configuration configuration)
        {
            _configuration = configuration;
            _settings.Clear();
            foreach (var kvp in configuration.Values)
            {
                _settings[kvp.Key] = kvp.Value;
            }
            await Task.Run(() => Save());
        }

        protected virtual void OnConfigurationChanged(string key, object oldValue, object newValue)
        {
            if (!_isGetting && !_isSetting)
            {
                ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(key, oldValue, newValue));
            }
        }
    }

    public class ConfigurationChangedEventArgs : EventArgs
    {
        public string Key { get; }
        public object OldValue { get; }
        public object NewValue { get; }

        public ConfigurationChangedEventArgs(string key, object oldValue, object newValue)
        {
            Key = key;
            OldValue = oldValue;
            NewValue = newValue;
        }
    }
} 