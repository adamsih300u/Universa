using System;
using System.Collections.Generic;
using System.Linq;

namespace Universa.Desktop.Core.Configuration
{
    public static class ConfigurationExtensions
    {
        public static T GetValueOrDefault<T>(this IConfigurationStore store, string key, T defaultValue = default)
        {
            return store.Get(key, defaultValue);
        }

        public static bool HasValue<T>(this IConfigurationStore store, string key)
        {
            return store.HasKey(key);
        }

        public static void SetIfChanged<T>(this IConfigurationStore store, string key, T value)
        {
            var currentValue = store.Get<T>(key);
            if (!EqualityComparer<T>.Default.Equals(currentValue, value))
            {
                store.Set(key, value);
            }
        }

        public static void SetIfNotExists<T>(this IConfigurationStore store, string key, T value)
        {
            if (!store.HasKey(key))
            {
                store.Set(key, value);
            }
        }

        public static void RemoveIfExists<T>(this IConfigurationStore store, string key)
        {
            if (store.HasKey(key))
            {
                store.RemoveKey(key);
            }
        }

        public static void CopyFrom(this IConfigurationStore target, IConfigurationStore source, params string[] keys)
        {
            foreach (var key in keys)
            {
                if (source.HasKey(key))
                {
                    var value = source.Get<object>(key);
                    target.Set(key, value);
                }
            }
        }

        public static void BatchUpdate(this IConfigurationStore store, Action<IConfigurationStore> updateAction)
        {
            updateAction(store);
            store.Save();
        }

        public static bool ValidateRequiredSettings(this IConfigurationStore store, IEnumerable<string> requiredKeys, out List<string> missingKeys)
        {
            missingKeys = requiredKeys.Where(key => !store.HasKey(key)).ToList();
            return !missingKeys.Any();
        }

        public static void ResetToDefaults(this IConfigurationStore store, IDictionary<string, object> defaults)
        {
            foreach (var kvp in defaults)
            {
                store.Set(kvp.Key, kvp.Value);
            }
            store.Save();
        }

        public static bool GetBool(this IConfigurationStore store, string key, bool defaultValue = false)
        {
            return store.Get(key, defaultValue);
        }

        public static int GetInt(this IConfigurationStore store, string key, int defaultValue = 0)
        {
            return store.Get(key, defaultValue);
        }

        public static string GetString(this IConfigurationStore store, string key, string defaultValue = "")
        {
            return store.Get(key, defaultValue);
        }

        public static void SetBool(this IConfigurationStore store, string key, bool value)
        {
            store.Set(key, value);
        }

        public static void SetInt(this IConfigurationStore store, string key, int value)
        {
            store.Set(key, value);
        }

        public static T GetEnum<T>(this IConfigurationStore store, string key, T defaultValue) where T : struct
        {
            var value = store.Get<string>(key);
            if (string.IsNullOrEmpty(value))
                return defaultValue;

            if (Enum.TryParse<T>(value, true, out var result))
                return result;

            return defaultValue;
        }

        public static List<string> GetStringList(this IConfigurationStore store, string key, List<string> defaultValue = null)
        {
            var value = store.Get<List<string>>(key);
            return value ?? defaultValue ?? new List<string>();
        }

        public static void SetStringList(this IConfigurationStore store, string key, List<string> value)
        {
            store.Set(key, value);
        }

        public static Dictionary<string, string> GetDictionary(this IConfigurationStore store, string key)
        {
            var value = store.Get<Dictionary<string, string>>(key);
            return value ?? new Dictionary<string, string>();
        }

        public static void SetDictionary(this IConfigurationStore store, string key, Dictionary<string, string> value)
        {
            store.Set(key, value);
        }
    }
} 