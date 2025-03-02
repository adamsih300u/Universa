using System.Collections.Generic;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Extension methods for Dictionary
    /// </summary>
    public static class DictionaryExtensions
    {
        /// <summary>
        /// Gets a value from a dictionary with a fallback if the key doesn't exist
        /// </summary>
        public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue)
        {
            if (dictionary != null && dictionary.TryGetValue(key, out TValue value))
            {
                return value;
            }
            
            return defaultValue;
        }
    }
} 