using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;

namespace Universa.Desktop.Cache
{
    public class MediaLibraryCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Universa",
            "Cache"
        );

        private static readonly string JellyfinCacheFile = Path.Combine(CacheDirectory, "jellyfin_library.cache");
        private static readonly object _lock = new object();
        private static MediaLibraryCache _instance;
        private Dictionary<string, CachedMediaItem> _cachedItems;
        private DateTime _lastUpdateTime;

        public static MediaLibraryCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new MediaLibraryCache();
                    }
                }
                return _instance;
            }
        }

        private MediaLibraryCache()
        {
            _cachedItems = new Dictionary<string, CachedMediaItem>();
            EnsureCacheDirectoryExists();
            LoadCache();
        }

        private void EnsureCacheDirectoryExists()
        {
            if (!Directory.Exists(CacheDirectory))
            {
                Directory.CreateDirectory(CacheDirectory);
            }
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(JellyfinCacheFile))
                {
                    var json = File.ReadAllText(JellyfinCacheFile);
                    var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                    _cachedItems = cacheData.Items.ToDictionary(item => item.Id);
                    _lastUpdateTime = cacheData.LastUpdateTime;
                    
                    System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Loaded {_cachedItems.Count} items from cache. Last update: {_lastUpdateTime}");
                    
                    // Log library items for debugging
                    foreach (var item in _cachedItems.Values.Where(i => !string.IsNullOrEmpty(i.CollectionType)))
                    {
                        System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Loaded library: Name='{item.Name}', Type={item.Type}, CollectionType={item.CollectionType}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Error loading cache: {ex.Message}");
                _cachedItems = new Dictionary<string, CachedMediaItem>();
                _lastUpdateTime = DateTime.MinValue;
            }
        }

        public async Task SaveCacheAsync(IEnumerable<CachedMediaItem> items)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Saving {items.Count()} items to cache");
                
                // Log library items for debugging
                foreach (var item in items.Where(i => !string.IsNullOrEmpty(i.CollectionType)))
                {
                    System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Saving library: Name='{item.Name}', Type={item.Type}, CollectionType={item.CollectionType}");
                }

                var cacheData = new CacheData
                {
                    Items = items.ToList(),
                    LastUpdateTime = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                await File.WriteAllTextAsync(JellyfinCacheFile, json);
                _cachedItems = items.ToDictionary(item => item.Id);
                _lastUpdateTime = cacheData.LastUpdateTime;
                System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Successfully saved items to cache");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Error saving cache: {ex.Message}");
                throw;
            }
        }

        public IEnumerable<CachedMediaItem> GetCachedItems()
        {
            System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Getting {_cachedItems.Count} cached items");
            var items = _cachedItems.Values.ToList();
            
            // Log library items for debugging
            foreach (var item in items.Where(i => !string.IsNullOrEmpty(i.CollectionType)))
            {
                System.Diagnostics.Debug.WriteLine($"MediaLibraryCache: Found library: Name='{item.Name}', Type={item.Type}, CollectionType={item.CollectionType}");
            }
            
            return items;
        }

        public bool IsCacheStale(TimeSpan threshold)
        {
            return DateTime.UtcNow - _lastUpdateTime > threshold;
        }

        public void ClearCache()
        {
            try
            {
                if (File.Exists(JellyfinCacheFile))
                {
                    File.Delete(JellyfinCacheFile);
                }
                _cachedItems.Clear();
                _lastUpdateTime = DateTime.MinValue;
                System.Diagnostics.Debug.WriteLine("Cache cleared successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing cache: {ex.Message}");
                throw;
            }
        }
    }

    public class CacheData
    {
        public List<CachedMediaItem> Items { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
} 