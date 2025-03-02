using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using Universa.Desktop.Models;
using System.Text.Json.Serialization;
using System.Linq;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using Universa.Desktop.ViewModels;

namespace Universa.Desktop.Services
{
    public class AudiobookshelfService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private string _token;

        public AudiobookshelfService(string baseUrl, string username, string password)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl), "Audiobookshelf base URL cannot be null or empty");
            if (string.IsNullOrEmpty(username))
                throw new ArgumentNullException(nameof(username), "Audiobookshelf username cannot be null or empty");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password), "Audiobookshelf password cannot be null or empty");

            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _client = new HttpClient();
        }

        public async Task<bool> LoginAsync()
        {
            try
            {
                var loginUrl = $"{_baseUrl}/login";
                Debug.WriteLine($"Attempting to login to Audiobookshelf at {loginUrl}");
                var loginRequest = new LoginRequest
                {
                    Username = _username,
                    Password = _password
                };
                
                var response = await _client.PostAsJsonAsync(loginUrl, loginRequest);
                response.EnsureSuccessStatusCode();
                
                // Read raw response first for debugging
                var rawContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Raw login response: {rawContent}");

                var options = new JsonSerializerOptions
                {
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };
                
                var loginResponse = JsonSerializer.Deserialize<LoginResponse>(rawContent, options);

                if (loginResponse?.User?.Token == null)
                {
                    Debug.WriteLine("Login failed - no token in response");
                    return false;
                }

                _token = loginResponse.User.Token;
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                Debug.WriteLine("Login successful, token obtained");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Login failed: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        public async Task<List<AudiobookItem>> GetLibraryItemsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new List<AudiobookItem>();
                    }
                }

                var librariesUrl = $"{_baseUrl}/api/libraries";
                Debug.WriteLine($"Fetching libraries from {librariesUrl}");
                var librariesResponse = await _client.GetAsync(librariesUrl);
                librariesResponse.EnsureSuccessStatusCode();
                
                var librariesContent = await librariesResponse.Content.ReadAsStringAsync();
                Debug.WriteLine($"Libraries response: {librariesContent}");
                var libraries = JsonSerializer.Deserialize<LibrariesResponse>(librariesContent);

                if (libraries == null || !libraries.Libraries.Any())
                {
                    Debug.WriteLine("No libraries found");
                    return new List<AudiobookItem>();
                }

                var items = new List<AudiobookItem>();
                foreach (var library in libraries.Libraries)
                {
                    var url = $"{_baseUrl}/api/libraries/{library.Id}/items";
                    Debug.WriteLine($"Fetching items from {url}");
                    var response = await _client.GetAsync(url);
                    response.EnsureSuccessStatusCode();
                    
                    var content = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Items response: {content}");
                    var itemsResponse = JsonSerializer.Deserialize<ItemsResponse>(content);
                    
                    if (itemsResponse?.Results != null)
                    {
                        var libraryItems = itemsResponse.Results.Select(i => new AudiobookItem
                        {
                            Id = i.Id ?? "",
                            Title = i.Title,
                            Author = i.Author,
                            Duration = i.Duration,
                            CoverPath = !string.IsNullOrEmpty(i.Id) ? $"{_baseUrl}/api/items/{Uri.EscapeDataString(i.Id)}/cover" : null,
                            Progress = 0, // Progress will need to be fetched separately from user's media progress
                            Type = i.MediaType?.ToLower() == "podcast" ? AudiobookItemType.Podcast : AudiobookItemType.Audiobook
                        });
                        items.AddRange(libraryItems);
                    }
                }

                Debug.WriteLine($"Total items found: {items.Count}");
                return items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting items: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        public async Task<List<AudiobookItem>> GetLibraryContentsAsync(string libraryId)
        {
            if (string.IsNullOrEmpty(libraryId))
            {
                Debug.WriteLine("Library ID is null or empty");
                return new List<AudiobookItem>();
            }

            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new List<AudiobookItem>();
                    }
                }

                var escapedLibraryId = Uri.EscapeDataString(libraryId);
                var url = $"{_baseUrl}/api/libraries/{escapedLibraryId}/items";
                Debug.WriteLine($"Fetching library contents from {url}");
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Raw library contents response: {content}");
                var itemsResponse = JsonSerializer.Deserialize<ItemsResponse>(content);
                
                if (itemsResponse?.Results == null)
                {
                    Debug.WriteLine("No results found in the response");
                    return new List<AudiobookItem>();
                }

                var firstItem = itemsResponse.Results.FirstOrDefault();
                if (firstItem != null)
                {
                    Debug.WriteLine($"First item raw data:");
                    Debug.WriteLine($"  ID: {firstItem.Id}");
                    Debug.WriteLine($"  LibraryItemId: {firstItem.LibraryItemId}");
                    Debug.WriteLine($"  Raw Metadata: {JsonSerializer.Serialize(firstItem.Media?.Metadata)}");
                    Debug.WriteLine($"  Raw Series Data: {JsonSerializer.Serialize(firstItem.Media?.Metadata?.Series)}");
                    var firstSeries = firstItem.Media?.Metadata?.Series?.FirstOrDefault();
                    Debug.WriteLine($"  Series Name: {firstSeries?.Name}");
                    Debug.WriteLine($"  Series Sequence: {firstSeries?.Sequence}");
                    Debug.WriteLine($"  Computed Title: {firstItem.Title}");
                    Debug.WriteLine($"  Computed Author: {firstItem.Author}");
                }
                
                var result = itemsResponse.Results.Select(i => 
                {
                    var metadata = i.Media?.Metadata;
                    Debug.WriteLine($"Processing item: {metadata?.Title}");
                    
                    // Try to get series from both locations
                    var seriesInfo = i.Series?.FirstOrDefault() ?? metadata?.Series?.FirstOrDefault();
                    if (seriesInfo != null)
                    {
                        Debug.WriteLine($"  Series data - Name: {seriesInfo.Name}, Sequence: {seriesInfo.Sequence}");
                    }
                    
                    var item = new AudiobookItem
                    {
                        Id = i.LibraryItemId ?? "",
                        Title = i.Title,
                        Author = i.Author,
                        Narrator = i.Media?.Metadata?.NarratorName ?? "",
                        Duration = i.Duration,
                        CoverPath = !string.IsNullOrEmpty(i.Id) ? $"{_baseUrl}/api/items/{Uri.EscapeDataString(i.Id)}/cover" : null,
                        Progress = 0,
                        Type = i.MediaType?.ToLower() == "podcast" ? AudiobookItemType.Podcast : AudiobookItemType.Audiobook,
                        Series = seriesInfo?.Name,
                        SeriesSequence = seriesInfo?.Sequence
                    };
                    
                    if (seriesInfo != null)
                    {
                        Debug.WriteLine($"Created AudiobookItem - Title: {item.Title}, Series: {item.Series}, Sequence: {item.SeriesSequence}");
                    }
                    
                    return item;
                }).ToList() ?? new List<AudiobookItem>();

                Debug.WriteLine($"First converted item: Title={result.FirstOrDefault()?.Title}, Author={result.FirstOrDefault()?.Author}");
                Debug.WriteLine($"Converted {result.Count} items for library {libraryId}");
                Debug.WriteLine($"Items with series: {result.Count(i => !string.IsNullOrEmpty(i.Series))}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting library contents: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
        }

        private class LoginRequest
        {
            [JsonPropertyName("username")]
            public string Username { get; set; }

            [JsonPropertyName("password")]
            public string Password { get; set; }
        }

        private class LoginResponse
        {
            [JsonPropertyName("user")]
            public UserResponse User { get; set; }
        }

        private class UserResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("username")]
            public string Username { get; set; }

            [JsonPropertyName("token")]
            public string Token { get; set; }

            [JsonPropertyName("mediaProgress")]
            public List<MediaProgressResponse> MediaProgress { get; set; }
        }

        private class MediaProgressResponse
        {
            [JsonPropertyName("libraryItemId")]
            public string LibraryItemId { get; set; }

            [JsonPropertyName("duration")]
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public double? Duration { get; set; }

            [JsonPropertyName("progress")]
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public double Progress { get; set; }

            [JsonPropertyName("currentTime")]
            [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
            public double CurrentTime { get; set; }

            [JsonPropertyName("isFinished")]
            public bool IsFinished { get; set; }

            [JsonPropertyName("lastUpdate")]
            public long LastUpdate { get; set; }
        }

        private class LibrariesResponse
        {
            [JsonPropertyName("libraries")]
            public List<LibraryResponse> Libraries { get; set; }
        }

        private class ItemsResponse
        {
            [JsonPropertyName("results")]
            public List<AudiobookItemResponse> Results { get; set; }

            [JsonPropertyName("total")]
            public int Total { get; set; }

            [JsonPropertyName("limit")]
            public int Limit { get; set; }

            [JsonPropertyName("page")]
            public int Page { get; set; }
        }

        private class AudiobookItemResponse
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("libraryItemId")]
            public string LibraryItemId { get; set; }

            [JsonPropertyName("libraryId")]
            public string LibraryId { get; set; }

            [JsonPropertyName("media")]
            public AudiobookMedia Media { get; set; }

            [JsonPropertyName("series")]
            public SeriesInfo[] Series { get; set; }

            [JsonPropertyName("progressLastUpdate")]
            public long ProgressLastUpdate { get; set; }

            public string Title => Media?.Metadata?.Title ?? "Unknown Title";
            public string Author => Media?.Metadata?.AuthorName ?? "";
            public double Duration => Media?.Duration ?? 0;
            public string MediaType => Media?.MediaType;
        }

        private class AudiobookMedia
        {
            [JsonPropertyName("duration")]
            public double Duration { get; set; }

            [JsonPropertyName("mediaType")]
            public string MediaType { get; set; }

            [JsonPropertyName("metadata")]
            public AudiobookMetadata Metadata { get; set; }

            [JsonPropertyName("publishedAt")]
            public long? PublishedAt { get; set; }
        }

        private class AudiobookMetadata
        {
            [JsonPropertyName("title")]
            public string Title { get; set; }

            [JsonPropertyName("authorName")]
            public string AuthorName { get; set; }

            [JsonPropertyName("narratorName")]
            public string NarratorName { get; set; }

            [JsonPropertyName("description")]
            public string Description { get; set; }

            [JsonPropertyName("series")]
            public SeriesInfo[] Series { get; set; }
        }

        private class SeriesInfo
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("sequence")]
            public string Sequence { get; set; }
        }

        public async Task<List<LibraryResponse>> GetLibrariesAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new List<LibraryResponse>();
                    }
                }

                var librariesUrl = $"{_baseUrl}/api/libraries";
                Debug.WriteLine($"Fetching libraries from {librariesUrl}");
                var librariesResponse = await _client.GetAsync(librariesUrl);
                librariesResponse.EnsureSuccessStatusCode();
                
                var librariesContent = await librariesResponse.Content.ReadAsStringAsync();
                Debug.WriteLine($"Libraries response: {librariesContent}");
                var response = JsonSerializer.Deserialize<LibrariesResponse>(librariesContent);

                return response?.Libraries ?? new List<LibraryResponse>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting libraries: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<LibraryResponse>();
            }
        }

        public async Task<Dictionary<string, double>> GetUserProgressAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new Dictionary<string, double>();
                    }
                }

                var url = $"{_baseUrl}/api/me";
                Debug.WriteLine($"Fetching user data from {url}");
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"User response: {content}");
                var userResponse = JsonSerializer.Deserialize<UserResponse>(content);

                return userResponse?.MediaProgress?.ToDictionary(
                    p => p.LibraryItemId,
                    p => p.Progress * 100 // Convert from 0-1 to percentage
                ) ?? new Dictionary<string, double>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting user progress: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new Dictionary<string, double>();
            }
        }

        public async Task UpdateProgressAsync(string libraryItemId, double progress, double currentTime)
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return;
                    }
                }

                var url = $"{_baseUrl}/api/me/progress/{libraryItemId}";
                var payload = new
                {
                    progress = progress / 100, // Convert from percentage to 0-1
                    currentTime = currentTime,
                    isFinished = progress >= 100
                };

                Debug.WriteLine($"Updating progress for {libraryItemId} to {progress}% at {currentTime}s");
                var response = await _client.PostAsJsonAsync(url, payload);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating progress: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private class InProgressItemsResponse
        {
            [JsonPropertyName("libraryItems")]
            public List<AudiobookItemResponse> LibraryItems { get; set; }
        }

        public async Task<List<AudiobookItem>> GetInProgressItemsAsync(string libraryId)
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new List<AudiobookItem>();
                    }
                }

                var url = $"{_baseUrl}/api/me/items-in-progress";
                Debug.WriteLine($"Fetching in-progress items from {url}");
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"In-progress items response: {content}");
                var itemsResponse = JsonSerializer.Deserialize<InProgressItemsResponse>(content);

                if (itemsResponse?.LibraryItems == null)
                {
                    Debug.WriteLine("No in-progress items found");
                    return new List<AudiobookItem>();
                }

                Debug.WriteLine($"Found {itemsResponse.LibraryItems.Count} total in-progress items in response");
                
                // Filter items by library ID
                var libraryItems = itemsResponse.LibraryItems
                    .Where(i => i.LibraryId == libraryId)
                    .ToList();
                Debug.WriteLine($"Found {libraryItems.Count} in-progress items for library {libraryId}");

                var result = libraryItems.Select(i => 
                {
                    var metadata = i.Media?.Metadata;
                    Debug.WriteLine($"Processing in-progress item: {metadata?.Title}");
                    
                    var seriesInfo = i.Series?.FirstOrDefault() ?? metadata?.Series?.FirstOrDefault();
                    var item = new AudiobookItem
                    {
                        Id = i.LibraryItemId ?? i.Id ?? "",
                        Title = metadata?.Title ?? "Unknown Title",
                        Author = metadata?.AuthorName ?? "",
                        Narrator = metadata?.NarratorName ?? "",
                        Duration = i.Media?.Duration ?? 0,
                        CoverPath = !string.IsNullOrEmpty(i.Id) ? $"{_baseUrl}/api/items/{Uri.EscapeDataString(i.Id)}/cover" : null,
                        Progress = 0,
                        Type = i.MediaType?.ToLower() == "podcast" ? AudiobookItemType.Podcast : AudiobookItemType.Audiobook,
                        Series = seriesInfo?.Name,
                        SeriesSequence = seriesInfo?.Sequence
                    };
                    Debug.WriteLine($"Created in-progress item: {item.Title}");
                    return item;
                }).ToList();

                // Get progress for these items
                var progress = await GetUserProgressAsync();
                foreach (var item in result)
                {
                    if (progress.TryGetValue(item.Id, out double itemProgress))
                    {
                        item.Progress = itemProgress;
                        Debug.WriteLine($"Updated progress for {item.Title}: {itemProgress}%");
                    }
                }

                Debug.WriteLine($"Returning {result.Count} in-progress items");
                return result.OrderByDescending(i => i.Progress).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting in-progress items: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        public async Task<List<AudiobookItem>> GetPodcastEpisodesAsync(string podcastId)
        {
            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    Debug.WriteLine("No token found, attempting to login");
                    if (!await LoginAsync())
                    {
                        Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                        return new List<AudiobookItem>();
                    }
                }

                var url = $"{_baseUrl}/api/items/{Uri.EscapeDataString(podcastId)}/episodes";
                Debug.WriteLine($"Fetching podcast episodes from {url}");
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Podcast episodes response: {content}");
                var episodesResponse = JsonSerializer.Deserialize<ItemsResponse>(content);

                if (episodesResponse?.Results == null)
                {
                    Debug.WriteLine("No episodes found");
                    return new List<AudiobookItem>();
                }

                var result = episodesResponse.Results.Select(i => 
                {
                    var metadata = i.Media?.Metadata;
                    var item = new AudiobookItem
                    {
                        Id = i.LibraryItemId ?? i.Id ?? "",
                        Title = metadata?.Title ?? "Unknown Episode",
                        Author = metadata?.AuthorName ?? "",
                        Duration = i.Media?.Duration ?? 0,
                        CoverPath = !string.IsNullOrEmpty(i.Id) ? $"{_baseUrl}/api/items/{Uri.EscapeDataString(i.Id)}/cover" : null,
                        Progress = 0,
                        Type = AudiobookItemType.PodcastEpisode,
                        PublishedAt = DateTimeOffset.FromUnixTimeMilliseconds(i.Media?.PublishedAt ?? 0).DateTime
                    };
                    Debug.WriteLine($"Created episode item: {item.Title}");
                    return item;
                }).ToList();

                // Get progress for episodes
                var progress = await GetUserProgressAsync();
                foreach (var item in result)
                {
                    if (progress.TryGetValue(item.Id, out double itemProgress))
                    {
                        item.Progress = itemProgress;
                        Debug.WriteLine($"Updated progress for episode {item.Title}: {itemProgress}%");
                    }
                }

                Debug.WriteLine($"Returning {result.Count} podcast episodes");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting podcast episodes: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        public async Task<string> GetStreamUrlAsync(string itemId)
        {
            var url = $"{_baseUrl}/api/items/{itemId}/play";
            var response = await _client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<PlaybackResponse>();
            return result?.StreamUrl ?? throw new Exception("No stream URL returned from server");
        }

        private class PlaybackResponse
        {
            [JsonPropertyName("streamUrl")]
            public string StreamUrl { get; set; }
        }
    }
} 