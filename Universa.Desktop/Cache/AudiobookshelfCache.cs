using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Universa.Desktop.Models;

namespace Universa.Desktop.Cache
{
    public class AudiobookshelfCache
    {
        private static readonly string CacheDirectory = Path.Combine(
            Configuration.Instance.LibraryPath,
            ".universa",
            "cache",
            "audiobookshelf"
        );

        private static readonly string CacheFile = Path.Combine(CacheDirectory, "library.cache");
        private static readonly object _lock = new object();
        private static AudiobookshelfCache _instance;
        private Dictionary<string, List<AudiobookItem>> _libraryCache;
        private Dictionary<string, DateTime> _lastUpdateTimes;

        public static AudiobookshelfCache Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new AudiobookshelfCache();
                    }
                }
                return _instance;
            }
        }

        private AudiobookshelfCache()
        {
            System.Diagnostics.Debug.WriteLine($"Initializing AudiobookshelfCache at {CacheDirectory}");
            InitializeCache();
            EnsureCacheDirectoryExists();
            LoadCache();
        }

        private void InitializeCache()
        {
            System.Diagnostics.Debug.WriteLine("Initializing cache dictionaries");
            _libraryCache = new Dictionary<string, List<AudiobookItem>>();
            _lastUpdateTimes = new Dictionary<string, DateTime>();
        }

        private void EnsureCacheDirectoryExists()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Checking cache directory: {CacheDirectory}");
                if (!Directory.Exists(CacheDirectory))
                {
                    System.Diagnostics.Debug.WriteLine("Creating cache directory");
                    Directory.CreateDirectory(CacheDirectory);
                }
                System.Diagnostics.Debug.WriteLine($"Cache directory exists: {Directory.Exists(CacheDirectory)}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating cache directory: {ex.Message}");
                throw;
            }
        }

        private void LoadCache()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading cache from: {CacheFile}");
                if (File.Exists(CacheFile))
                {
                    System.Diagnostics.Debug.WriteLine("Cache file exists, reading content");
                    var json = File.ReadAllText(CacheFile);
                    var cacheData = JsonSerializer.Deserialize<CacheData>(json);
                    
                    // Ensure we have valid dictionaries even if deserialization returns null
                    _libraryCache = cacheData?.LibraryCache ?? new Dictionary<string, List<AudiobookItem>>();
                    _lastUpdateTimes = cacheData?.LastUpdateTimes ?? new Dictionary<string, DateTime>();
                    System.Diagnostics.Debug.WriteLine($"Loaded {_libraryCache.Count} libraries from cache");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No existing cache file found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading cache: {ex.Message}");
                InitializeCache(); // Reset to empty cache on error
            }
        }

        private void SaveCache()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Saving cache to: {CacheFile}");
                var cacheData = new CacheData
                {
                    LibraryCache = _libraryCache ?? new Dictionary<string, List<AudiobookItem>>(),
                    LastUpdateTimes = _lastUpdateTimes ?? new Dictionary<string, DateTime>()
                };
                var json = JsonSerializer.Serialize(cacheData);
                File.WriteAllText(CacheFile, json);
                System.Diagnostics.Debug.WriteLine($"Cache saved successfully with {_libraryCache.Count} libraries");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving cache: {ex.Message}");
            }
        }

        public List<AudiobookItem> GetCachedItems(string libraryId)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] GetCachedItems for library {libraryId}");
            if (_libraryCache?.TryGetValue(libraryId, out var items) == true)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Found {items.Count} items in cache");
                return items;
            }
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] No items found in cache for library {libraryId}");
            return new List<AudiobookItem>();
        }

        public bool ShouldRefresh(string libraryId, TimeSpan maxAge)
        {
            var now = DateTime.Now;
            if (_lastUpdateTimes?.TryGetValue(libraryId, out DateTime lastUpdate) == true)
            {
                var age = now - lastUpdate;
                System.Diagnostics.Debug.WriteLine($"Cache age check for library {libraryId}:");
                System.Diagnostics.Debug.WriteLine($"  - Last update: {lastUpdate:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"  - Current time: {now:yyyy-MM-dd HH:mm:ss}");
                System.Diagnostics.Debug.WriteLine($"  - Age: {age.TotalMinutes:F2} minutes");
                System.Diagnostics.Debug.WriteLine($"  - Max allowed age: {maxAge.TotalMinutes:F2} minutes");
                return age > maxAge;
            }
            System.Diagnostics.Debug.WriteLine($"No last update time found for library {libraryId}, refresh needed");
            return true;
        }

        public void UpdateCache(string libraryId, List<AudiobookItem> items)
        {
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Updating cache for library {libraryId} with {items.Count} items");
            _libraryCache ??= new Dictionary<string, List<AudiobookItem>>();
            _libraryCache[libraryId] = items;
            _lastUpdateTimes ??= new Dictionary<string, DateTime>();
            _lastUpdateTimes[libraryId] = DateTime.Now;
            SaveCache();
            System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Cache update complete");
        }

        private class CacheData
        {
            public CacheData()
            {
                LibraryCache = new Dictionary<string, List<AudiobookItem>>();
                LastUpdateTimes = new Dictionary<string, DateTime>();
            }

            public Dictionary<string, List<AudiobookItem>> LibraryCache { get; set; }
            public Dictionary<string, DateTime> LastUpdateTimes { get; set; }
        }
    }
} 