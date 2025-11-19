using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Handles library operations for Audiobookshelf
    /// </summary>
    public class AudiobookshelfLibraryService
    {
        private readonly HttpClient _client;
        private readonly AudiobookshelfAuthService _authService;
        private readonly string _baseUrl;

        public AudiobookshelfLibraryService(HttpClient client, AudiobookshelfAuthService authService, string baseUrl)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Gets all available libraries from the server
        /// </summary>
        public async Task<List<AudiobookshelfLibraryResponse>> GetLibrariesAsync()
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new List<AudiobookshelfLibraryResponse>();
                }

                var librariesUrl = $"{_baseUrl}/api/libraries";
                Debug.WriteLine($"Fetching libraries from {librariesUrl}");
                
                var response = await _client.GetAsync(librariesUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Libraries response: {content}");
                
                var libraries = JsonSerializer.Deserialize<LibrariesResponse>(content);
                return libraries?.Libraries ?? new List<AudiobookshelfLibraryResponse>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting libraries: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookshelfLibraryResponse>();
            }
        }

        /// <summary>
        /// Gets all items from all libraries
        /// </summary>
        public async Task<List<AudiobookItem>> GetLibraryItemsAsync()
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new List<AudiobookItem>();
                }

                var libraries = await GetLibrariesAsync();
                if (!libraries.Any())
                {
                    Debug.WriteLine("No libraries found");
                    return new List<AudiobookItem>();
                }

                var items = new List<AudiobookItem>();
                foreach (var library in libraries)
                {
                    var libraryItems = await GetLibraryContentsAsync(library.Id);
                    items.AddRange(libraryItems);
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

        /// <summary>
        /// Gets contents of a specific library
        /// </summary>
        public async Task<List<AudiobookItem>> GetLibraryContentsAsync(string libraryId)
        {
            if (string.IsNullOrEmpty(libraryId))
            {
                Debug.WriteLine("Library ID is null or empty");
                return new List<AudiobookItem>();
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new List<AudiobookItem>();
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

                Debug.WriteLine($"Found {itemsResponse.Results.Count} items in library {libraryId}");

                var items = itemsResponse.Results.Select(i =>
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
                }).ToList();

                Debug.WriteLine($"Processed {items.Count} items for library {libraryId}");
                return items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting library contents: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        /// <summary>
        /// Gets items currently in progress for a specific library
        /// </summary>
        public async Task<List<AudiobookItem>> GetInProgressItemsAsync(string libraryId)
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new List<AudiobookItem>();
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

                Debug.WriteLine($"Processed {result.Count} in-progress items for library {libraryId}");
                return result;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting in-progress items: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<AudiobookItem>();
            }
        }

        /// <summary>
        /// Gets detailed information for a specific audiobook item
        /// </summary>
        public async Task<AudiobookItem> GetItemDetailsAsync(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                Debug.WriteLine("Item ID is null or empty");
                return null;
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return null;
                }

                var escapedItemId = Uri.EscapeDataString(itemId);
                var url = $"{_baseUrl}/api/items/{escapedItemId}";
                Debug.WriteLine($"Fetching item details from {url}");
                
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Item details response: {content}");
                
                var itemResponse = JsonSerializer.Deserialize<AudiobookItemResponse>(content);

                if (itemResponse == null)
                {
                    Debug.WriteLine("No item details found");
                    return null;
                }

                var metadata = itemResponse.Media?.Metadata;
                var seriesInfo = itemResponse.Series?.FirstOrDefault() ?? metadata?.Series?.FirstOrDefault();

                var item = new AudiobookItem
                {
                    Id = itemResponse.LibraryItemId ?? itemResponse.Id ?? "",
                    Title = metadata?.Title ?? itemResponse.Title ?? "Unknown Title",
                    Author = metadata?.AuthorName ?? itemResponse.Author ?? "",
                    Narrator = metadata?.NarratorName ?? "",
                    Duration = itemResponse.Duration,
                    CoverPath = !string.IsNullOrEmpty(itemResponse.Id) ? 
                        $"{_baseUrl}/api/items/{Uri.EscapeDataString(itemResponse.Id)}/cover" : null,
                    Progress = 0,
                    Type = itemResponse.MediaType?.ToLower() == "podcast" ? 
                        AudiobookItemType.Podcast : AudiobookItemType.Audiobook,
                    Series = seriesInfo?.Name,
                    SeriesSequence = seriesInfo?.Sequence
                };

                Debug.WriteLine($"Retrieved item details: {item.Title}");
                return item;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting item details: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Gets episodes for a specific podcast
        /// </summary>
        public async Task<List<AudiobookItem>> GetPodcastEpisodesAsync(string podcastId)
        {
            if (string.IsNullOrEmpty(podcastId))
            {
                Debug.WriteLine("Podcast ID is null or empty");
                return new List<AudiobookItem>();
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new List<AudiobookItem>();
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
                        PublishedAt = DateTime.Now // Use current date as placeholder since PublishedAt is not available in MediaInfo
                    };
                    Debug.WriteLine($"Created episode item: {item.Title}");
                    return item;
                }).ToList();

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

        /// <summary>
        /// Gets the stream URL for a specific item
        /// </summary>
        public async Task<string> GetStreamUrlAsync(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
            {
                Debug.WriteLine("Item ID is null or empty");
                throw new ArgumentException("Item ID cannot be null or empty", nameof(itemId));
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    throw new InvalidOperationException("Authentication failed");
                }

                var url = $"{_baseUrl}/api/items/{Uri.EscapeDataString(itemId)}/play";
                Debug.WriteLine($"Getting stream URL from {url}");
                
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Stream URL response: {content}");
                
                var playbackResponse = JsonSerializer.Deserialize<PlaybackResponse>(content);
                
                if (string.IsNullOrEmpty(playbackResponse?.StreamUrl))
                {
                    throw new InvalidOperationException("No stream URL returned from server");
                }

                Debug.WriteLine($"Stream URL obtained: {playbackResponse.StreamUrl}");
                return playbackResponse.StreamUrl;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting stream URL: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private class PlaybackResponse
        {
            [JsonPropertyName("streamUrl")]
            public string StreamUrl { get; set; }
        }
    }
} 