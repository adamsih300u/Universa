using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Cache
{
    public class JellyfinMediaCache
    {
        private static JellyfinMediaCache _instance;
        private static readonly object _lock = new object();
        private const string CACHE_FILE = "jellyfin_cache.json";
        private const int CACHE_VERSION = 1;

        public static JellyfinMediaCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new JellyfinMediaCache();
                    }
                }
                return _instance;
            }
        }

        private class CacheData
        {
            public int Version { get; set; }
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, LibraryCacheEntry> Libraries { get; set; }
        }

        private class LibraryCacheEntry
        {
            public List<MediaItem> Items { get; set; }
            public DateTime LastUpdated { get; set; }
        }

        private readonly string _cacheFilePath;
        private CacheData _cache;

        private JellyfinMediaCache()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Universa",
                "Cache"
            );
            Directory.CreateDirectory(appDataPath);
            _cacheFilePath = Path.Combine(appDataPath, CACHE_FILE);
            LoadCache();
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    var json = File.ReadAllText(_cacheFilePath);
                    _cache = JsonSerializer.Deserialize<CacheData>(json);

                    // Handle version mismatch by clearing cache
                    if (_cache.Version != CACHE_VERSION)
                    {
                        _cache = CreateNewCache();
                    }
                }
                else
                {
                    _cache = CreateNewCache();
                }
            }
            catch (Exception)
            {
                _cache = CreateNewCache();
            }
        }

        private CacheData CreateNewCache()
        {
            return new CacheData
            {
                Version = CACHE_VERSION,
                LastUpdated = DateTime.MinValue,
                Libraries = new Dictionary<string, LibraryCacheEntry>()
            };
        }

        private void SaveCache()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cache);
                File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception)
            {
                // Log error but continue - cache is non-critical
            }
        }

        public async Task<List<MediaItem>> GetLibraryItemsAsync(string libraryId)
        {
            if (_cache.Libraries.TryGetValue(libraryId, out var entry))
            {
                return entry.Items;
            }
            return null;
        }

        public void UpdateLibraryItems(string libraryId, List<MediaItem> items)
        {
            _cache.Libraries[libraryId] = new LibraryCacheEntry
            {
                Items = items,
                LastUpdated = DateTime.UtcNow
            };
            _cache.LastUpdated = DateTime.UtcNow;
            SaveCache();
        }

        public bool IsLibraryCacheValid(string libraryId, TimeSpan maxAge)
        {
            if (_cache.Libraries.TryGetValue(libraryId, out var entry))
            {
                return DateTime.UtcNow - entry.LastUpdated < maxAge;
            }
            return false;
        }

        public void ClearCache()
        {
            _cache = CreateNewCache();
            SaveCache();
        }
    }
} 