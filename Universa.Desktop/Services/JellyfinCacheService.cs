using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Universa.Desktop.Cache;

namespace Universa.Desktop.Services
{
    public class JellyfinCacheService
    {
        private readonly string _cacheDirectory;
        private readonly MediaLibraryCache _legacyCache;
        private const int CACHE_VERSION = 2; // Increment when cache structure changes

        public JellyfinCacheService()
        {
            _legacyCache = MediaLibraryCache.Instance;
            
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Universa",
                "Cache",
                "Jellyfin"
            );

            try
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error creating cache directory: {ex.Message}");
            }
        }

        public async Task SaveToCacheAsync<T>(string type, T data)
        {
            try
            {
                var cacheData = new CacheEntry<T>
                {
                    Version = CACHE_VERSION,
                    Timestamp = DateTime.UtcNow,
                    Data = data
                };

                // Handle special case for MediaItems to preserve CollectionType
                if (data is List<MediaItem> mediaItems && type == "libraries")
                {
                    var cachedItems = mediaItems.Select(item => new CachedMediaItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Type = item.Type,
                        Path = item.Path,
                        ParentId = item.ParentId,
                        Overview = item.Overview,
                        ImagePath = item.ImagePath,
                        DateAdded = item.DateAdded,
                        Metadata = item.Metadata,
                        CollectionType = item.Metadata?.GetValueOrDefault("CollectionType")
                    }).ToList();

                    var specialCacheData = new CacheEntry<List<CachedMediaItem>>
                    {
                        Version = CACHE_VERSION,
                        Timestamp = DateTime.UtcNow,
                        Data = cachedItems
                    };

                    var json = JsonSerializer.Serialize(specialCacheData, new JsonSerializerOptions { WriteIndented = true });
                    var path = GetCachePath(type);
                    await File.WriteAllTextAsync(path, json);

                    // Also save to legacy cache for backward compatibility
                    await _legacyCache.SaveCacheAsync(cachedItems);
                    
                    // Save to settings as backup
                    Universa.Desktop.Properties.Settings.Default.CachedJellyfinLibraries = json;
                    Universa.Desktop.Properties.Settings.Default.Save();
                }
                else
                {
                    var json = JsonSerializer.Serialize(cacheData, new JsonSerializerOptions { WriteIndented = true });
                    var path = GetCachePath(type);
                    await File.WriteAllTextAsync(path, json);
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Successfully cached {type}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error saving to cache: {ex.Message}");
            }
        }

        public async Task<T> LoadFromCacheAsync<T>(string type) where T : class
        {
            try
            {
                var path = GetCachePath(type);
                if (!File.Exists(path))
                {
                    // Try legacy cache for libraries
                    if (typeof(T) == typeof(List<MediaItem>) && type == "libraries")
                    {
                        return await LoadFromLegacyCacheAsync<T>();
                    }
                    return null;
                }

                var json = await File.ReadAllTextAsync(path);
                
                // Handle special case for MediaItems
                if (typeof(T) == typeof(List<MediaItem>) && type == "libraries")
                {
                    var cachedEntry = JsonSerializer.Deserialize<CacheEntry<List<CachedMediaItem>>>(json);
                    
                    // Check cache version
                    if (cachedEntry.Version != CACHE_VERSION)
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Cache version mismatch for {type}, clearing cache");
                        await ClearCacheAsync(type);
                        return null;
                    }

                    if (cachedEntry?.Data != null)
                    {
                        var mediaItems = cachedEntry.Data.Select(item => new MediaItem
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Type = !string.IsNullOrEmpty(item.CollectionType) ? 
                                  GetMediaItemTypeFromCollection(item.CollectionType) : 
                                  item.Type,
                            Path = item.Path,
                            ParentId = item.ParentId,
                            Overview = item.Overview,
                            ImagePath = item.ImagePath,
                            DateAdded = item.DateAdded,
                            Metadata = item.Metadata ?? new Dictionary<string, string>(),
                            HasChildren = true
                        }).ToList();

                        System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Loaded {mediaItems.Count} items from cache for {type}");
                        return mediaItems as T;
                    }
                }
                else
                {
                    var cachedEntry = JsonSerializer.Deserialize<CacheEntry<T>>(json);
                    
                    // Check cache version
                    if (cachedEntry.Version != CACHE_VERSION)
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Cache version mismatch for {type}, clearing cache");
                        await ClearCacheAsync(type);
                        return null;
                    }

                    return cachedEntry?.Data;
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error loading from cache: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> IsCacheValidAsync(string type, TimeSpan maxAge)
        {
            try
            {
                var path = GetCachePath(type);
                if (!File.Exists(path))
                {
                    return false;
                }

                var fileInfo = new FileInfo(path);
                var age = DateTime.UtcNow - fileInfo.LastWriteTimeUtc;
                
                return age < maxAge;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error checking cache validity: {ex.Message}");
                return false;
            }
        }

        public async Task ClearCacheAsync(string type = null)
        {
            try
            {
                if (string.IsNullOrEmpty(type))
                {
                    // Clear all cache
                    if (Directory.Exists(_cacheDirectory))
                    {
                        Directory.Delete(_cacheDirectory, true);
                        Directory.CreateDirectory(_cacheDirectory);
                    }
                    
                    // Clear legacy cache
                    _legacyCache.ClearCache();
                    
                    System.Diagnostics.Debug.WriteLine("JellyfinCacheService: Cleared all cache");
                }
                else
                {
                    // Clear specific cache type
                    var path = GetCachePath(type);
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Cleared cache for {type}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error clearing cache: {ex.Message}");
            }
        }

        public async Task<List<MediaItem>> GetLibraryItemsAsync(string libraryId)
        {
            try
            {
                var cacheKey = $"library_{libraryId}";
                return await LoadFromCacheAsync<List<MediaItem>>(cacheKey);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error getting library items from cache: {ex.Message}");
                return null;
            }
        }

        public async Task UpdateLibraryItemsAsync(string libraryId, List<MediaItem> items)
        {
            try
            {
                var cacheKey = $"library_{libraryId}";
                await SaveToCacheAsync(cacheKey, items);
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Updated cache for library {libraryId} with {items.Count} items");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error updating library items cache: {ex.Message}");
            }
        }

        public async Task<bool> IsLibraryCacheValidAsync(string libraryId, TimeSpan maxAge)
        {
            try
            {
                var cacheKey = $"library_{libraryId}";
                return await IsCacheValidAsync(cacheKey, maxAge);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error checking library cache validity: {ex.Message}");
                return false;
            }
        }

        private async Task<T> LoadFromLegacyCacheAsync<T>() where T : class
        {
            try
            {
                if (typeof(T) == typeof(List<MediaItem>))
                {
                    var legacyItems = _legacyCache.GetCachedItems();
                    if (legacyItems?.Any() == true)
                    {
                        var mediaItems = legacyItems.Select(item => new MediaItem
                        {
                            Id = item.Id,
                            Name = item.Name,
                            Type = !string.IsNullOrEmpty(item.CollectionType) ? 
                                  GetMediaItemTypeFromCollection(item.CollectionType) : 
                                  item.Type,
                            Path = item.Path,
                            ParentId = item.ParentId,
                            Overview = item.Overview,
                            ImagePath = item.ImagePath,
                            DateAdded = item.DateAdded,
                            Metadata = item.Metadata ?? new Dictionary<string, string>(),
                            HasChildren = true
                        }).ToList();

                        System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Loaded {mediaItems.Count} items from legacy cache");
                        return mediaItems as T;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinCacheService: Error loading from legacy cache: {ex.Message}");
                return null;
            }
        }

        private string GetCachePath(string type)
        {
            return Path.Combine(_cacheDirectory, $"{type}.json");
        }

        private MediaItemType GetMediaItemTypeFromCollection(string collectionType)
        {
            return collectionType?.ToLower() switch
            {
                "movies" => MediaItemType.MovieLibrary,
                "tvshows" => MediaItemType.TVLibrary,
                "music" => MediaItemType.MusicLibrary,
                "books" => MediaItemType.Library,
                "photos" => MediaItemType.Library,
                _ => MediaItemType.Library
            };
        }

        private class CacheEntry<T>
        {
            public int Version { get; set; }
            public DateTime Timestamp { get; set; }
            public T Data { get; set; }
        }
    }
} 