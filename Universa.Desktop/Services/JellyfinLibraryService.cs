using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Newtonsoft.Json.Linq;

namespace Universa.Desktop.Services
{
    public class JellyfinLibraryService
    {
        private readonly HttpClient _httpClient;
        private readonly JellyfinAuthService _authService;
        private readonly JellyfinCacheService _cacheService;

        public JellyfinLibraryService(HttpClient httpClient, JellyfinAuthService authService, JellyfinCacheService cacheService)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        }

        public async Task<List<MediaItem>> GetLibrariesAsync()
        {
            try
            {
                // Try to load from cache first
                var cachedLibraries = await _cacheService.LoadFromCacheAsync<List<MediaItem>>("libraries");
                if (cachedLibraries != null)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Loaded {cachedLibraries.Count} libraries from cache");
                    return cachedLibraries;
                }

                // Ensure we're authenticated before making the request
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Getting libraries from server: {_authService.ServerUrl}/Users/{_authService.UserId}/Views");

                var response = await _httpClient.GetAsync($"{_authService.ServerUrl}/Users/{_authService.UserId}/Views");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ItemsResult>(content);

                if (data?.Items == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinLibraryService: No libraries found in response");
                    return new List<MediaItem>();
                }

                var libraries = data.Items
                    .Where(item => !string.IsNullOrEmpty(item.CollectionType))
                    .Select(item => CreateMediaItemFromLibrary(item))
                    .Where(item => item != null)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Created {libraries.Count} library items");

                // Save to cache
                await _cacheService.SaveToCacheAsync("libraries", libraries);

                return libraries;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting libraries - {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetLibraryItemsAsync(string libraryId, MediaItemType parentType = MediaItemType.Library)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Getting items for library {libraryId}");

                var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/Items" +
                         $"?ParentId={libraryId}" +
                         "&Recursive=false" +
                         "&Fields=Path,Overview,DateCreated,MediaType,Type,CollectionType,RunTimeTicks,ProductionYear,IndexNumber,ParentIndexNumber,SeriesName,SeasonName,SeasonNumber,EpisodeNumber" +
                         "&SortBy=SortName" +
                         "&SortOrder=Ascending";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ItemsResult>(content);

                if (data?.Items == null)
                {
                    return new List<MediaItem>();
                }

                var mediaItems = data.Items
                    .Select(item => CreateMediaItemFromJellyfin(item))
                    .Where(item => item != null)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Retrieved {mediaItems.Count} items for library {libraryId}");
                return mediaItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting library items - {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetItemsAsync(string itemParentId)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/Items" +
                         $"?ParentId={itemParentId}" +
                         "&Recursive=false" +
                         "&Fields=Path,Overview,DateCreated,MediaType,Type,RunTimeTicks,ProductionYear,IndexNumber,ParentIndexNumber,SeriesName,SeasonName,SeasonNumber,EpisodeNumber" +
                         "&SortBy=IndexNumber,SortName" +
                         "&SortOrder=Ascending";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ItemsResult>(content);

                if (data?.Items == null)
                {
                    return new List<MediaItem>();
                }

                return data.Items
                    .Select(item => CreateMediaItemFromJellyfin(item))
                    .Where(item => item != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting items - {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetAllEpisodesAsync(string seriesId)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var url = $"{_authService.ServerUrl}/Shows/{seriesId}/Episodes" +
                         $"?UserId={_authService.UserId}" +
                         "&Fields=Path,Overview,DateCreated,MediaType,Type,RunTimeTicks,ProductionYear,IndexNumber,ParentIndexNumber,SeriesName,SeasonName,SeasonNumber,EpisodeNumber";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var data = JsonSerializer.Deserialize<ItemsResult>(content);

                if (data?.Items == null)
                {
                    return new List<MediaItem>();
                }

                return data.Items
                    .Select(item => CreateMediaItemFromJellyfin(item))
                    .Where(item => item != null)
                    .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting episodes - {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetContinueWatchingAsync(bool isTV)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var itemType = isTV ? "Episode" : "Movie";
                var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/Items/Resume" +
                         $"?Limit=50" +
                         "&Fields=Path,Overview,SeasonName,SeasonNumber,EpisodeNumber,UserData" +
                         $"&IncludeItemTypes={itemType}";

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching URL: {url}");
                
                var response = await _httpClient.GetAsync(url);
                
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching Response Status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching Error Response: {errorContent}");
                    response.EnsureSuccessStatusCode();
                }

                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching Raw Response: {content}");
                
                var json = JObject.Parse(content);
                var items = json["Items"]?.ToObject<JArray>();

                if (items == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinLibraryService: GetContinueWatching - No 'Items' property in response");
                    return new List<MediaItem>();
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching - Found {items.Count} items");

                var mediaItems = items.Select(item => new MediaItem
                {
                    Id = item["Id"]?.ToString(),
                    Name = item["Name"]?.ToString(),
                    Type = isTV ? MediaItemType.Episode : MediaItemType.Movie,
                    Path = item["Path"]?.ToString(),
                    Overview = item["Overview"]?.ToString(),
                    SeriesName = item["SeriesName"]?.ToString(),
                    SeasonName = item["SeasonName"]?.ToString(),
                    SeasonNumber = item["SeasonNumber"]?.ToObject<int?>(),
                    EpisodeNumber = item["EpisodeNumber"]?.ToObject<int?>(),
                    HasChildren = false
                }).ToList();

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetContinueWatching - Returning {mediaItems.Count} MediaItems");
                foreach (var mediaItem in mediaItems.Take(3)) // Log first 3 items
                {
                    System.Diagnostics.Debug.WriteLine($"  - {mediaItem.Name} (ID: {mediaItem.Id})");
                }

                return mediaItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting continue watching - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Stack trace - {ex.StackTrace}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetRecentlyAddedAsync(bool isTV)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var itemType = isTV ? "Episode" : "Movie";
                var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/Items/Latest" +
                         $"?Limit=50" +
                         "&Fields=Path,Overview,SeasonName,SeasonNumber,EpisodeNumber" +
                         $"&IncludeItemTypes={itemType}";

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var items = JArray.Parse(content);

                return items.Select(item => new MediaItem
                {
                    Id = item["Id"]?.ToString(),
                    Name = item["Name"]?.ToString(),
                    Type = isTV ? MediaItemType.Episode : MediaItemType.Movie,
                    Path = item["Path"]?.ToString(),
                    Overview = item["Overview"]?.ToString(),
                    SeriesName = item["SeriesName"]?.ToString(),
                    SeasonName = item["SeasonName"]?.ToString(),
                    SeasonNumber = item["SeasonNumber"]?.ToObject<int?>(),
                    EpisodeNumber = item["EpisodeNumber"]?.ToObject<int?>(),
                    HasChildren = false
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting recently added - {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetNextUpAsync()
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                // Using correct endpoint structure - /Shows/NextUp instead of /TvShows/NextUp
                var url = $"{_authService.ServerUrl}/Shows/NextUp" +
                         $"?UserId={_authService.UserId}" +
                         "&Limit=50" +
                         "&Fields=PrimaryImageAspectRatio,SeriesInfo,UserData,Overview" +
                         "&EnableUserData=true" +
                         "&EnableImages=true";

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp URL: {url}");
                
                var response = await _httpClient.GetAsync(url);
                
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp Response Status: {response.StatusCode}");
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp Error Response: {errorContent}");
                    response.EnsureSuccessStatusCode();
                }

                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp Raw Response: {content}");
                
                var json = JObject.Parse(content);
                var items = json["Items"]?.ToObject<JArray>();

                if (items == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinLibraryService: GetNextUp - No 'Items' property in response");
                    return new List<MediaItem>();
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp - Found {items.Count} items");

                var mediaItems = items.Select(item => new MediaItem
                {
                    Id = item["Id"]?.ToString(),
                    Name = item["Name"]?.ToString(),
                    Type = MediaItemType.Episode, // Next Up is always episodes
                    Path = item["Path"]?.ToString(),
                    Overview = item["Overview"]?.ToString(),
                    SeriesName = item["SeriesName"]?.ToString(),
                    SeasonName = item["SeasonName"]?.ToString(),
                    SeasonNumber = item["ParentIndexNumber"]?.ToObject<int?>(), // Season number is in ParentIndexNumber
                    EpisodeNumber = item["IndexNumber"]?.ToObject<int?>(), // Episode number is in IndexNumber
                    HasChildren = false,
                    Duration = item["RunTimeTicks"] != null ? TimeSpan.FromTicks(item["RunTimeTicks"].ToObject<long>()) : null,
                    Year = item["ProductionYear"]?.ToObject<int?>()
                }).ToList();

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: GetNextUp - Returning {mediaItems.Count} MediaItems");
                foreach (var mediaItem in mediaItems.Take(3)) // Log first 3 items
                {
                    System.Diagnostics.Debug.WriteLine($"  - {mediaItem.SeriesName} S{mediaItem.SeasonNumber}E{mediaItem.EpisodeNumber}: {mediaItem.Name} (ID: {mediaItem.Id})");
                }

                return mediaItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error getting next up - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Stack trace - {ex.StackTrace}");
                throw;
            }
        }

        public async Task<string> DiagnoseContinueWatchingAsync()
        {
            var diagnostics = new List<string>();
            
            try
            {
                diagnostics.Add("=== Continue Watching Diagnostics ===");
                
                // Test authentication
                var isAuthenticated = await _authService.EnsureAuthenticatedAsync();
                diagnostics.Add($"Authentication Status: {(isAuthenticated ? "✓ SUCCESS" : "✗ FAILED")}");
                
                if (!isAuthenticated)
                {
                    diagnostics.Add("Cannot proceed - authentication failed");
                    return string.Join("\n", diagnostics);
                }
                
                diagnostics.Add($"Server URL: {_authService.ServerUrl}");
                diagnostics.Add($"User ID: {_authService.UserId}");
                
                // Test both TV and Movie Continue Watching endpoints
                foreach (var (label, isTV) in new[] { ("TV Shows", true), ("Movies", false) })
                {
                    diagnostics.Add($"\n--- Testing {label} Continue Watching ---");
                    
                    try
                    {
                        var itemType = isTV ? "Episode" : "Movie";
                        var url = $"{_authService.ServerUrl}/Users/{_authService.UserId}/Items/Resume" +
                                 $"?Limit=50" +
                                 "&Fields=Path,Overview,SeasonName,SeasonNumber,EpisodeNumber,UserData" +
                                 $"&IncludeItemTypes={itemType}";
                        
                        diagnostics.Add($"URL: {url}");
                        
                        var response = await _httpClient.GetAsync(url);
                        diagnostics.Add($"Response Status: {response.StatusCode}");
                        
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsStringAsync();
                            var json = JObject.Parse(content);
                            var items = json["Items"]?.ToObject<JArray>();
                            
                            if (items != null)
                            {
                                diagnostics.Add($"Items Found: {items.Count}");
                                if (items.Count > 0)
                                {
                                    diagnostics.Add("Sample items:");
                                    foreach (var item in items.Take(3))
                                    {
                                        var name = item["Name"]?.ToString();
                                        var id = item["Id"]?.ToString();
                                        diagnostics.Add($"  - {name} (ID: {id})");
                                    }
                                }
                                else
                                {
                                    diagnostics.Add("No resume items found for this type");
                                    diagnostics.Add("This means you either have no partially watched content,");
                                    diagnostics.Add("or your Jellyfin server doesn't track watch progress for this content type.");
                                }
                            }
                            else
                            {
                                diagnostics.Add("No 'Items' property in response");
                            }
                        }
                        else
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            diagnostics.Add($"Error Response: {errorContent}");
                        }
                    }
                    catch (Exception ex)
                    {
                        diagnostics.Add($"Exception: {ex.Message}");
                    }
                }
                
                // Test Next Up endpoint
                diagnostics.Add("\n--- Testing Next Up ---");
                try
                {
                    var nextUpUrl = $"{_authService.ServerUrl}/Shows/NextUp" +
                                   $"?UserId={_authService.UserId}" +
                                   "&Limit=50" +
                                   "&Fields=PrimaryImageAspectRatio,SeriesInfo,UserData,Overview" +
                                   "&EnableUserData=true" +
                                   "&EnableImages=true";
                    
                    diagnostics.Add($"URL: {nextUpUrl}");
                    
                    var nextUpResponse = await _httpClient.GetAsync(nextUpUrl);
                    diagnostics.Add($"Response Status: {nextUpResponse.StatusCode}");
                    
                    if (nextUpResponse.IsSuccessStatusCode)
                    {
                        var nextUpContent = await nextUpResponse.Content.ReadAsStringAsync();
                        var nextUpJson = JObject.Parse(nextUpContent);
                        var nextUpItems = nextUpJson["Items"]?.ToObject<JArray>();
                        
                        if (nextUpItems != null)
                        {
                            diagnostics.Add($"Next Up Items Found: {nextUpItems.Count}");
                            if (nextUpItems.Count > 0)
                            {
                                diagnostics.Add("Sample Next Up items:");
                                foreach (var item in nextUpItems.Take(3))
                                {
                                    var seriesName = item["SeriesName"]?.ToString();
                                    var seasonNumber = item["ParentIndexNumber"]?.ToString(); // Correct field name
                                    var episodeNumber = item["IndexNumber"]?.ToString(); // Correct field name
                                    var name = item["Name"]?.ToString();
                                    var id = item["Id"]?.ToString();
                                    diagnostics.Add($"  - {seriesName} S{seasonNumber}E{episodeNumber}: {name} (ID: {id})");
                                }
                            }
                            else
                            {
                                diagnostics.Add("No next up items found");
                                diagnostics.Add("This means you either have no series in progress,");
                                diagnostics.Add("or all your series are caught up to the latest episodes.");
                            }
                        }
                        else
                        {
                            diagnostics.Add("No 'Items' property in Next Up response");
                        }
                    }
                    else
                    {
                        var errorContent = await nextUpResponse.Content.ReadAsStringAsync();
                        diagnostics.Add($"Next Up Error Response: {errorContent}");
                    }
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"Next Up Exception: {ex.Message}");
                }
                
                // Test general user data access
                diagnostics.Add("\n--- Testing General User Access ---");
                try
                {
                    var testUrl = $"{_authService.ServerUrl}/Users/{_authService.UserId}";
                    var testResponse = await _httpClient.GetAsync(testUrl);
                    diagnostics.Add($"User Info Access: {testResponse.StatusCode}");
                }
                catch (Exception ex)
                {
                    diagnostics.Add($"User Access Error: {ex.Message}");
                }
                
            }
            catch (Exception ex)
            {
                diagnostics.Add($"General Error: {ex.Message}");
                diagnostics.Add($"Stack Trace: {ex.StackTrace}");
            }
            
            return string.Join("\n", diagnostics);
        }

        private MediaItem CreateMediaItemFromLibrary(ItemInfo item)
        {
            try
            {
                var mediaType = GetMediaItemTypeFromCollection(item.CollectionType);
                
                var mediaItem = new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = mediaType,
                    Path = item.Path,
                    Overview = item.Overview,
                    DateAdded = item.DateCreated ?? DateTime.MinValue,
                    HasChildren = true,
                    Metadata = new Dictionary<string, string>
                    {
                        ["CollectionType"] = item.CollectionType
                    }
                };

                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Created library item: Name='{mediaItem.Name}', Type={mediaItem.Type}, CollectionType={item.CollectionType}");
                return mediaItem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error creating library item - {ex.Message}");
                return null;
            }
        }

        private MediaItem CreateMediaItemFromJellyfin(ItemInfo item)
        {
            try
            {
                var mediaType = GetMediaItemType(item.Type, item.CollectionType);
                
                var mediaItem = new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = mediaType,
                    Path = item.Path,
                    ParentId = item.ParentId,
                    Overview = item.Overview,
                    DateAdded = item.DateCreated ?? DateTime.MinValue,
                    SeriesName = item.SeriesName,
                    SeasonName = item.SeasonName,
                    SeasonNumber = item.SeasonNumber,
                    EpisodeNumber = item.EpisodeNumber,
                    HasChildren = mediaType == MediaItemType.Series || mediaType == MediaItemType.Season,
                    Metadata = new Dictionary<string, string>()
                };

                // Add duration if available
                if (item.RunTimeTicks.HasValue)
                {
                    var duration = TimeSpan.FromTicks(item.RunTimeTicks.Value);
                    mediaItem.Duration = duration;
                    mediaItem.Metadata["Duration"] = duration.ToString();
                }

                // Add production year if available
                if (item.ProductionYear.HasValue)
                {
                    mediaItem.Metadata["ProductionYear"] = item.ProductionYear.Value.ToString();
                }

                return mediaItem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinLibraryService: Error creating media item - {ex.Message}");
                return null;
            }
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

        private MediaItemType GetMediaItemType(string jellyfinType, string collectionType = null)
        {
            // First check collection type for libraries
            if (!string.IsNullOrEmpty(collectionType))
            {
                return GetMediaItemTypeFromCollection(collectionType);
            }

            // Otherwise, determine type based on the item type
            return jellyfinType?.ToLower() switch
            {
                "movie" => MediaItemType.Movie,
                "series" => MediaItemType.Series,
                "season" => MediaItemType.Season,
                "episode" => MediaItemType.Episode,
                "musicalbum" => MediaItemType.Album,
                "audio" => MediaItemType.Song,
                "collectionfolder" => MediaItemType.Library,
                "folder" => MediaItemType.Folder,
                "video" => MediaItemType.Movie,
                "boxset" => MediaItemType.Folder,
                "playlist" => MediaItemType.Folder,
                "manualplaylist" => MediaItemType.Folder,
                "musicvideo" => MediaItemType.Movie,
                "homevideo" => MediaItemType.Movie,
                "trailer" => MediaItemType.Movie,
                _ => MediaItemType.Unknown
            };
        }

        private class ItemsResult
        {
            public List<ItemInfo> Items { get; set; }
        }

        private class ItemInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Type { get; set; }
            public string CollectionType { get; set; }
            public int? ProductionYear { get; set; }
            public long? RunTimeTicks { get; set; }
            public int? IndexNumber { get; set; }
            public int? ParentIndexNumber { get; set; }
            public UserDataInfo UserData { get; set; }
            public string ParentId { get; set; }
            public string Overview { get; set; }
            public string Path { get; set; }
            public DateTime? DateCreated { get; set; }
            public Dictionary<string, string> ImageTags { get; set; }
            public string SeriesName { get; set; }
            public string SeasonName { get; set; }
            public int? SeasonNumber { get; set; }
            public int? EpisodeNumber { get; set; }
        }

        private class UserDataInfo
        {
            public bool Played { get; set; }
        }
    }
} 