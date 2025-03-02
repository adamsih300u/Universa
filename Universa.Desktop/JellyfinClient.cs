using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using System.IO;
using Universa.Desktop.Properties;
using Universa.Desktop.Models;
using System.Collections.ObjectModel;
using Universa.Desktop.Cache;
using System.Threading;

namespace Universa.Desktop
{
    public class JellyfinClient
    {
        private readonly HttpClient _httpClient;
        private string _accessToken;
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;
        private string _userId;
        private readonly string _cacheDirectory;
        private readonly MediaLibraryCache _cache;
        private const string CLIENT_NAME = "Universa";
        private const string DEVICE_ID = "UniversaApp";
        private const string VERSION = "1.0.0";

        public JellyfinClient(string serverUrl, string username, string password)
        {
            if (string.IsNullOrEmpty(serverUrl))
                throw new ArgumentException("Server URL cannot be empty", nameof(serverUrl));
            if (string.IsNullOrEmpty(username))
                throw new ArgumentException("Username cannot be empty", nameof(username));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be empty", nameof(password));

            _serverUrl = serverUrl.TrimEnd('/');
            if (!_serverUrl.StartsWith("http://") && !_serverUrl.StartsWith("https://"))
            {
                _serverUrl = "http://" + _serverUrl;
            }
            _username = username;
            _password = password;
            
            // Configure HttpClient with timeout
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            
            _cache = MediaLibraryCache.Instance;

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
                System.Diagnostics.Debug.WriteLine($"Error creating cache directory: {ex.Message}");
            }
        }

        private void UpdateAuthorizationHeader()
        {
            var authHeader = $"MediaBrowser Client=\"{CLIENT_NAME}\", Device=\"Windows\", DeviceId=\"{DEVICE_ID}\", Version=\"{VERSION}\"";
            if (!string.IsNullOrEmpty(_accessToken))
            {
                authHeader += $", Token=\"{_accessToken}\"";
            }
            
            // Remove existing header if present
            if (_httpClient.DefaultRequestHeaders.Contains("X-Emby-Authorization"))
            {
                _httpClient.DefaultRequestHeaders.Remove("X-Emby-Authorization");
            }
            
            _httpClient.DefaultRequestHeaders.Add("X-Emby-Authorization", authHeader);
        }

        private string GetCachePath(string type)
        {
            return Path.Combine(_cacheDirectory, $"{type}.json");
        }

        public async Task SaveToCache<T>(string type, T data)
        {
            try
            {
                // If we're saving items, convert them to CachedMediaItems to preserve the CollectionType
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
                        CollectionType = item.Type == MediaItemType.MovieLibrary ? "movies" :
                                       item.Type == MediaItemType.TVLibrary ? "tvshows" : null
                    }).ToList();

                    var json = JsonSerializer.Serialize(cachedItems);
                    var path = GetCachePath(type);
                    await File.WriteAllTextAsync(path, json);

                    // Also save to settings for backup
                    if (type == "libraries")
                    {
                        Properties.Settings.Default.CachedJellyfinLibraries = json;
                        Properties.Settings.Default.Save();
                    }
                }
                else
                {
                    var json = JsonSerializer.Serialize(data);
                    var path = GetCachePath(type);
                    await File.WriteAllTextAsync(path, json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving to cache: {ex.Message}");
            }
        }

        public async Task<T> LoadFromCache<T>(string type) where T : class
        {
            try
            {
                var path = GetCachePath(type);
                if (File.Exists(path))
                {
                    var json = await File.ReadAllTextAsync(path);
                    
                    // If we're loading libraries, convert CachedMediaItems back to MediaItems
                    if (typeof(T) == typeof(List<MediaItem>) && type == "libraries")
                    {
                        var cachedItems = JsonSerializer.Deserialize<List<CachedMediaItem>>(json);
                        if (cachedItems != null)
                        {
                            var mediaItems = cachedItems.Select(item => new MediaItem
                            {
                                Id = item.Id,
                                Name = item.Name,
                                Type = !string.IsNullOrEmpty(item.CollectionType) ? 
                                      GetMediaItemType("collectionfolder", item.CollectionType) : 
                                      item.Type,
                                Path = item.Path,
                                ParentId = item.ParentId,
                                Overview = item.Overview,
                                ImagePath = item.ImagePath,
                                DateAdded = item.DateAdded,
                                Metadata = item.Metadata,
                                HasChildren = true
                            }).ToList();
                            return mediaItems as T;
                        }
                    }
                    
                    return JsonSerializer.Deserialize<T>(json);
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading from cache: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> Authenticate()
        {
            try
            {
                if (string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
                {
                    System.Diagnostics.Debug.WriteLine("Missing credentials for authentication");
                    return false;
                }

                UpdateAuthorizationHeader();

                var authRequest = new
                {
                    Username = _username,
                    Pw = _password,
                    Password = _password
                };

                System.Diagnostics.Debug.WriteLine($"Attempting to authenticate with server: {_serverUrl}");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    var response = await _httpClient.PostAsync(
                        $"{_serverUrl}/Users/authenticatebyname",
                        new StringContent(JsonSerializer.Serialize(authRequest), System.Text.Encoding.UTF8, "application/json"),
                        cts.Token
                    );

                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Auth response: {content}");

                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auth failed: {response.StatusCode} - {content}");
                        return false;
                    }

                    var authResult = JsonSerializer.Deserialize<AuthenticationResult>(content);
                    
                    if (string.IsNullOrEmpty(authResult?.AccessToken) || authResult?.User?.Id == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Auth response missing token or user ID");
                        return false;
                    }

                    _accessToken = authResult.AccessToken;
                    _userId = authResult.User.Id;
                    UpdateAuthorizationHeader();
                    System.Diagnostics.Debug.WriteLine($"Successfully authenticated. User ID: {_userId}");
                    
                    return true;
                }
                catch (OperationCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Authentication request timed out");
                    return false;
                }
                catch (HttpRequestException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"HTTP request error during authentication: {ex.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auth error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private async Task<bool> EnsureAuthenticated()
        {
            if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(_userId))
            {
                System.Diagnostics.Debug.WriteLine("No access token or user ID, attempting to authenticate...");
                return await Authenticate();
            }
            return true;
        }

        public async Task<List<MediaItem>> GetLibraries()
        {
            try
            {
                // Try to load from cache first
                var cachedLibraries = await LoadFromCache<List<MediaItem>>("libraries");
                if (cachedLibraries != null)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Loaded {cachedLibraries.Count} libraries from cache");
                    foreach (var lib in cachedLibraries)
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinClient: Cached library: Name='{lib.Name}', Type={lib.Type}, CollectionType={lib.Metadata?.GetValueOrDefault("CollectionType")}");
                    }
                    return cachedLibraries;
                }

                // Ensure we're authenticated before making the request
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Getting libraries from server: {_serverUrl}/Users/{_userId}/Views");
                UpdateAuthorizationHeader(); // Ensure auth header is up to date

                var response = await _httpClient.GetAsync($"{_serverUrl}/Users/{_userId}/Views");
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Server response: {content}");
                var data = JsonSerializer.Deserialize<ItemsResult>(content);

                if (data?.Items == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinClient: No libraries found in response");
                    return new List<MediaItem>();
                }

                var libraries = new List<MediaItem>();
                foreach (var item in data.Items)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Processing library item: Name='{item.Name}', Type={item.Type}, CollectionType={item.CollectionType}");
                    
                    // Skip items that don't have a name
                    if (string.IsNullOrEmpty(item.Name))
                    {
                        System.Diagnostics.Debug.WriteLine("JellyfinClient: Skipping library with no name");
                        continue;
                    }

                    var mediaItemType = GetMediaItemType(item.Type, item.CollectionType);
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Determined MediaItemType: {mediaItemType}");

                    var mediaItem = new MediaItem
                    {
                        Id = item.Id,
                        Name = item.Name,  // Use the actual name from Jellyfin
                        Type = mediaItemType,
                        HasChildren = true,
                        Children = new ObservableCollection<MediaItem>(),
                        Metadata = new Dictionary<string, string>
                        {
                            { "CollectionType", item.CollectionType },
                            { "OriginalType", item.Type }
                        }
                    };

                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Created MediaItem: Name='{mediaItem.Name}', Type={mediaItem.Type}, CollectionType={mediaItem.Metadata?.GetValueOrDefault("CollectionType")}");
                    libraries.Add(mediaItem);
                }

                // Save to cache
                await SaveToCache("libraries", libraries);
                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Saved {libraries.Count} libraries to cache");
                foreach (var lib in libraries)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Saved library: Name='{lib.Name}', Type={lib.Type}, CollectionType={lib.Metadata?.GetValueOrDefault("CollectionType")}");
                }

                return libraries;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Error getting libraries: {ex.Message}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetAllEpisodes(string seriesId)
        {
            try
            {
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                UpdateAuthorizationHeader(); // Ensure auth header is up to date
                var response = await _httpClient.GetAsync(
                    $"{_serverUrl}/Users/{_userId}/Items?ParentId={seriesId}&Recursive=true&IncludeItemTypes=Episode&Fields=PrimaryImageAspectRatio,ProductionYear,RunTimeTicks,UserData,IndexNumber,ParentIndexNumber&SortBy=ParentIndexNumber,IndexNumber&SortOrder=Ascending"
                );
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"All episodes response: {content}");
                    var result = JsonSerializer.Deserialize<ItemsResult>(content);
                    
                    if (result?.Items == null)
                    {
                        System.Diagnostics.Debug.WriteLine("No episodes found in the response");
                        return new List<MediaItem>();
                    }

                    return result.Items.ConvertAll(item => new MediaItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Type = MediaItemType.Episode,
                        Year = item.ProductionYear,
                        Duration = TimeSpan.FromTicks(item.RunTimeTicks ?? 0),
                        HasChildren = false,
                        IsPlayed = item.UserData?.Played ?? false,
                        EpisodeNumber = item.IndexNumber,
                        SeasonNumber = item.ParentIndexNumber,
                        StreamUrl = GetStreamUrl(item.Id)
                    });
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Failed to get all episodes: {response.StatusCode} - {errorContent}");
                return new List<MediaItem>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting all episodes: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<MediaItem>();
            }
        }

        public async Task<List<MediaItem>> GetLibraryItems(string libraryId, MediaItemType parentType = MediaItemType.Library)
        {
            try
            {
                // Ensure we're authenticated before making the request
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                string includeTypes = GetMediaTypeString(parentType);

                UpdateAuthorizationHeader(); // Ensure auth header is up to date

                var url = $"{_serverUrl}/Users/{_userId}/Items?ParentId={libraryId}&Recursive=false&IncludeItemTypes={includeTypes}&Fields=PrimaryImageAspectRatio,ProductionYear,RunTimeTicks,UserData,IndexNumber,DateCreated,Overview,Path,ImageTags&SortBy=SortName&SortOrder=Ascending";
                System.Diagnostics.Debug.WriteLine($"Getting library items with URL: {url}");

                // Use a longer timeout for library requests
                using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                
                HttpResponseMessage response;
                try
                {
                    response = await _httpClient.GetAsync(url, cts.Token);
                }
                catch (TaskCanceledException)
                {
                    System.Diagnostics.Debug.WriteLine("Request timed out, attempting to load from cache");
                    // Try to load from cache if available
                    var cachedItems = await LoadFromCache<List<MediaItem>>($"library_{libraryId}");
                    if (cachedItems != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Loaded {cachedItems.Count} items from cache");
                        return cachedItems;
                    }
                    throw;
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Error response: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        // Try to re-authenticate once
                        if (await Authenticate())
                        {
                            UpdateAuthorizationHeader();
                            response = await _httpClient.GetAsync(url, cts.Token);
                            response.EnsureSuccessStatusCode();
                        }
                        else
                        {
                            throw new InvalidOperationException("Failed to re-authenticate with Jellyfin server");
                        }
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }

                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Library items response: {content}");
                var result = JsonSerializer.Deserialize<ItemsResult>(content);
                
                if (result?.Items == null)
                {
                    System.Diagnostics.Debug.WriteLine("No items found in the response");
                    return new List<MediaItem>();
                }

                System.Diagnostics.Debug.WriteLine($"Found {result.Items.Count} items in response");
                var items = result.Items.Select(item =>
                {
                    System.Diagnostics.Debug.WriteLine($"Converting item: Name={item.Name}, Type={item.Type}");
                    return new MediaItem
                    {
                        Id = item.Id,
                        Name = item.Name,
                        Type = GetMediaItemType(item.Type),
                        Year = item.ProductionYear,
                        Duration = item.RunTimeTicks.HasValue ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : null,
                        HasChildren = item.Type?.ToLower() == "series" || 
                                    item.Type?.ToLower() == "season",
                        IsPlayed = item.UserData?.Played ?? false,
                        EpisodeNumber = item.IndexNumber,
                        SeasonNumber = item.ParentIndexNumber,
                        StreamUrl = GetStreamUrl(item.Id),
                        ParentId = item.ParentId,
                        Overview = item.Overview,
                        Path = GetStreamUrl(item.Id),
                        DateAdded = item.DateCreated ?? DateTime.MinValue,
                        ImagePath = item.ImageTags?.ContainsKey("Primary") == true
                            ? $"{_serverUrl}/Items/{item.Id}/Images/Primary"
                            : null,
                        Metadata = new Dictionary<string, string>
                        {
                            { "SeriesName", item.SeriesName },
                            { "SeasonName", item.SeasonName },
                            { "EpisodeNumber", item.IndexNumber?.ToString() },
                            { "Year", item.ProductionYear?.ToString() },
                            { "Duration", item.RunTimeTicks?.ToString() }
                        }
                    };
                }).ToList();

                // Save to cache
                await SaveToCache($"library_{libraryId}", items);

                return items;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting library items: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private MediaItemType GetMediaItemType(string jellyfinType, string collectionType = null)
        {
            System.Diagnostics.Debug.WriteLine($"GetMediaItemType: jellyfinType={jellyfinType}, collectionType={collectionType}");
            
            // If we have a collection type, this is a library
            if (!string.IsNullOrEmpty(collectionType))
            {
                var type = collectionType.ToLower() switch
                {
                    "movies" => MediaItemType.MovieLibrary,
                    "tvshows" => MediaItemType.TVLibrary,
                    "music" => MediaItemType.MusicLibrary,
                    _ => MediaItemType.Library
                };
                System.Diagnostics.Debug.WriteLine($"GetMediaItemType: Determined library type: {type}");
                return type;
            }

            // Otherwise, determine type based on the item type
            var itemType = jellyfinType?.ToLower() switch
            {
                "movie" => MediaItemType.Movie,
                "series" => MediaItemType.Series,
                "season" => MediaItemType.Season,
                "episode" => MediaItemType.Episode,
                "musicalbum" => MediaItemType.Album,
                "audio" => MediaItemType.Song,
                "collectionfolder" => MediaItemType.Library,
                "folder" => MediaItemType.Folder,
                "video" => MediaItemType.Movie,  // Treat standalone videos as movies
                "boxset" => MediaItemType.Folder,
                "playlist" => MediaItemType.Folder,
                "manualplaylist" => MediaItemType.Folder,
                "musicvideo" => MediaItemType.Movie,
                "homevideo" => MediaItemType.Movie,
                "trailer" => MediaItemType.Movie,
                _ => MediaItemType.Unknown
            };
            System.Diagnostics.Debug.WriteLine($"GetMediaItemType: Determined item type: {itemType}");
            return itemType;
        }

        private string GetMediaTypeString(MediaItemType type)
        {
            return type switch
            {
                MediaItemType.Movie => "movie",
                MediaItemType.MovieLibrary => "movies",
                MediaItemType.Series => "series",
                MediaItemType.TVLibrary => "tvshows",
                MediaItemType.Season => "season",
                MediaItemType.Episode => "episode",
                _ => type.ToString().ToLower()
            };
        }

        public string GetStreamUrl(string itemId)
        {
            // Ensure server URL is properly formatted
            var baseUrl = _serverUrl.TrimEnd('/');
            
            // Build the URL with proper encoding
            var builder = new UriBuilder(baseUrl);
            builder.Path += $"/Videos/{itemId}/stream";
            builder.Query = "static=true";
            
            var url = builder.Uri.ToString();
            System.Diagnostics.Debug.WriteLine($"Generated stream URL: {url}");
            return url;
        }

        public async Task<bool> MarkAsWatched(string itemId, bool watched)
        {
            try
            {
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var endpoint = watched ? "played" : "unplayed";
                UpdateAuthorizationHeader(); // Ensure auth header is up to date
                var response = await _httpClient.PostAsync(
                    $"{_serverUrl}/Users/{_userId}/PlayedItems/{itemId}/{endpoint}",
                    new StringContent("")
                );

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Failed to mark item as {(watched ? "watched" : "unwatched")}: {response.StatusCode} - {errorContent}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error marking item as {(watched ? "watched" : "unwatched")}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<List<MediaItem>> GetContinueWatching(bool isTV)
        {
            var url = $"{_serverUrl}/Users/{_userId}/Items/Resume?Limit=50&Fields=Path,Overview,SeasonName,SeasonNumber,EpisodeNumber&IncludeItemTypes={(isTV ? "Episode" : "Movie")}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            var items = json["Items"].ToObject<JArray>();

            return items.Select(item => new MediaItem
            {
                Id = item["Id"].ToString(),
                Name = item["Name"].ToString(),
                Type = isTV ? MediaItemType.Episode : MediaItemType.Movie,
                Path = item["Path"]?.ToString(),
                Overview = item["Overview"]?.ToString(),
                SeasonName = item["SeasonName"]?.ToString(),
                SeasonNumber = item["SeasonNumber"]?.ToObject<int?>(),
                EpisodeNumber = item["EpisodeNumber"]?.ToObject<int?>(),
                HasChildren = false
            }).ToList();
        }

        public async Task<List<MediaItem>> GetRecentlyAdded(bool isTV)
        {
            var url = $"{_serverUrl}/Users/{_userId}/Items/Latest?Limit=50&Fields=Path,Overview,SeasonName,SeasonNumber,EpisodeNumber&IncludeItemTypes={(isTV ? "Episode" : "Movie")}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var items = JArray.Parse(content);

            return items.Select(item => new MediaItem
            {
                Id = item["Id"].ToString(),
                Name = item["Name"].ToString(),
                Type = isTV ? MediaItemType.Episode : MediaItemType.Movie,
                Path = item["Path"]?.ToString(),
                Overview = item["Overview"]?.ToString(),
                SeasonName = item["SeasonName"]?.ToString(),
                SeasonNumber = item["SeasonNumber"]?.ToObject<int?>(),
                EpisodeNumber = item["EpisodeNumber"]?.ToObject<int?>(),
                HasChildren = false
            }).ToList();
        }

        public async Task<List<MediaItem>> GetItems(string itemParentId)
        {
            try
            {
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                var url = $"{_serverUrl}/Users/{_userId}/Items?ParentId={itemParentId}&Fields=Path,Overview,DateCreated,PrimaryImageAspectRatio,ImageTags";
                System.Diagnostics.Debug.WriteLine($"Getting items with URL: {url}");
                
                UpdateAuthorizationHeader(); // Ensure auth header is up to date
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"Error response: {errorContent}");
                    
                    if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        // Try to re-authenticate once
                        System.Diagnostics.Debug.WriteLine("Received 401 Unauthorized, attempting to re-authenticate...");
                        if (await Authenticate())
                        {
                            UpdateAuthorizationHeader();
                            response = await _httpClient.GetAsync(url);
                            response.EnsureSuccessStatusCode();
                        }
                        else
                        {
                            throw new InvalidOperationException("Failed to re-authenticate with Jellyfin server");
                        }
                    }
                    else
                    {
                        response.EnsureSuccessStatusCode();
                    }
                }

                var content = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Items response: {content}");
                var result = JsonSerializer.Deserialize<ItemsResult>(content);
                
                if (result?.Items == null)
                {
                    System.Diagnostics.Debug.WriteLine("No items found in the response");
                    return new List<MediaItem>();
                }

                return result.Items.Select(item => new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    Type = GetMediaItemType(item.Type),
                    Year = item.ProductionYear,
                    Duration = item.RunTimeTicks.HasValue ? TimeSpan.FromTicks(item.RunTimeTicks.Value) : null,
                    HasChildren = item.Type?.ToLower() == "series" || 
                                item.Type?.ToLower() == "season",
                    IsPlayed = item.UserData?.Played ?? false,
                    EpisodeNumber = item.IndexNumber,
                    SeasonNumber = item.ParentIndexNumber,
                    StreamUrl = GetStreamUrl(item.Id),
                    ParentId = item.ParentId,
                    Overview = item.Overview,
                    Path = GetStreamUrl(item.Id),
                    DateAdded = item.DateCreated ?? DateTime.MinValue,
                    ImagePath = item.ImageTags?.ContainsKey("Primary") == true
                        ? $"{_serverUrl}/Items/{item.Id}/Images/Primary"
                        : null,
                    Metadata = new Dictionary<string, string>
                    {
                        { "SeriesName", item.SeriesName },
                        { "SeasonName", item.SeasonName },
                        { "EpisodeNumber", item.IndexNumber?.ToString() },
                        { "Year", item.ProductionYear?.ToString() },
                        { "Duration", item.RunTimeTicks?.ToString() }
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting items: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<List<MediaItem>> GetMediaLibraryAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("JellyfinClient: Starting GetMediaLibraryAsync");

                // Try to get from cache first
                var cachedItems = _cache.GetCachedItems().ToList();
                if (cachedItems != null && cachedItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Retrieved {cachedItems.Count} items from cache");
                    var mediaItems = new List<MediaItem>();
                    foreach (var item in cachedItems)
                    {
                        mediaItems.Add(new MediaItem
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
                            HasChildren = item.HasChildren
                        });
                    }
                    return mediaItems;
                }

                // If not authenticated, try to authenticate
                if (!await EnsureAuthenticated())
                {
                    throw new InvalidOperationException("Failed to authenticate with Jellyfin server");
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                
                // Get the root libraries first
                List<MediaItem> libraries;
                try
                {
                    libraries = await GetLibraries();
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Retrieved {libraries.Count} root libraries");
                    foreach (var lib in libraries)
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinClient: Root library: Name='{lib.Name}', Type={lib.Type}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Error getting libraries: {ex.Message}");
                    throw new InvalidOperationException("Failed to retrieve libraries from Jellyfin server", ex);
                }

                var allItems = new List<MediaItem>();
                allItems.AddRange(libraries);

                // For each library, get its items with a timeout
                foreach (var library in libraries)
                {
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinClient: Getting items for library '{library.Name}'");
                        var items = await GetLibraryItems(library.Id, library.Type);
                        System.Diagnostics.Debug.WriteLine($"JellyfinClient: Retrieved {items.Count} items for library '{library.Name}'");

                        // Ensure each item has the correct parent ID and metadata
                        foreach (var item in items)
                        {
                            item.ParentId = library.Id;
                            if (item.Metadata == null) item.Metadata = new Dictionary<string, string>();
                            item.Metadata["LibraryName"] = library.Name;
                            item.Metadata["LibraryType"] = library.Type.ToString();
                        }

                        allItems.AddRange(items);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"JellyfinClient: Error getting items for library '{library.Name}': {ex.Message}");
                        // Continue with next library instead of failing completely
                        continue;
                    }
                }

                // Convert to cached items for storage
                var cachedItemsToSave = allItems.Select(item => new Cache.CachedMediaItem
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
                    CollectionType = item.Type switch
                    {
                        MediaItemType.MovieLibrary => "movies",
                        MediaItemType.TVLibrary => "tvshows",
                        _ => null
                    },
                    HasChildren = item.HasChildren
                }).ToList();

                // Save to cache
                await _cache.SaveCacheAsync(cachedItemsToSave);

                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Total items retrieved: {allItems.Count}");
                foreach (var item in allItems.Where(i => i.Type == MediaItemType.MovieLibrary || i.Type == MediaItemType.TVLibrary))
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinClient: Library in final list: Name='{item.Name}', Type={item.Type}");
                }

                return allItems;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinClient: Error in GetMediaLibraryAsync: {ex.Message}");
                throw;
            }
        }

        private class UserInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }

        private class AuthenticationResult
        {
            public string AccessToken { get; set; }
            public UserInfo User { get; set; }
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

        private class CachedMediaItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public MediaItemType Type { get; set; }
            public string Path { get; set; }
            public string ParentId { get; set; }
            public string Overview { get; set; }
            public string ImagePath { get; set; }
            public DateTime DateAdded { get; set; }
            public Dictionary<string, string> Metadata { get; set; }
            public string CollectionType { get; set; }  // Add this property
        }
    }
} 