using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Handles progress tracking operations for Audiobookshelf
    /// </summary>
    public class AudiobookshelfProgressService
    {
        private readonly HttpClient _client;
        private readonly AudiobookshelfAuthService _authService;
        private readonly string _baseUrl;

        public AudiobookshelfProgressService(HttpClient client, AudiobookshelfAuthService authService, string baseUrl)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
        }

        /// <summary>
        /// Gets user's reading progress for all items
        /// </summary>
        public async Task<Dictionary<string, double>> GetUserProgressAsync()
        {
            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return new Dictionary<string, double>();
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

        /// <summary>
        /// Updates reading progress for a specific item
        /// </summary>
        public async Task UpdateProgressAsync(string libraryItemId, double progress, double currentTime)
        {
            if (string.IsNullOrEmpty(libraryItemId))
            {
                Debug.WriteLine("Library item ID is null or empty");
                return;
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return;
                }

                var url = $"{_baseUrl}/api/me/progress/{Uri.EscapeDataString(libraryItemId)}";
                var payload = new
                {
                    progress = Math.Max(0, Math.Min(1, progress / 100)), // Ensure 0-1 range
                    currentTime = Math.Max(0, currentTime),
                    isFinished = progress >= 100
                };

                Debug.WriteLine($"Updating progress for {libraryItemId} to {progress}% at {currentTime}s");
                
                var response = await _client.PostAsJsonAsync(url, payload);
                response.EnsureSuccessStatusCode();

                Debug.WriteLine($"Progress updated successfully for {libraryItemId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating progress: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Marks an item as finished
        /// </summary>
        public async Task MarkAsFinishedAsync(string libraryItemId)
        {
            await UpdateProgressAsync(libraryItemId, 100, 0);
        }

        /// <summary>
        /// Removes progress for an item (resets to 0)
        /// </summary>
        public async Task ResetProgressAsync(string libraryItemId)
        {
            await UpdateProgressAsync(libraryItemId, 0, 0);
        }

        /// <summary>
        /// Gets detailed progress information for a specific item
        /// </summary>
        public async Task<MediaProgress> GetItemProgressAsync(string libraryItemId)
        {
            if (string.IsNullOrEmpty(libraryItemId))
            {
                Debug.WriteLine("Library item ID is null or empty");
                return null;
            }

            try
            {
                if (!await _authService.EnsureAuthenticatedAsync())
                {
                    Debug.WriteLine("Failed to authenticate with Audiobookshelf");
                    return null;
                }

                var url = $"{_baseUrl}/api/me/progress/{Uri.EscapeDataString(libraryItemId)}";
                Debug.WriteLine($"Fetching progress for item {libraryItemId} from {url}");
                
                var response = await _client.GetAsync(url);
                
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    Debug.WriteLine($"No progress found for item {libraryItemId}");
                    return null;
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Progress response: {content}");
                
                var progressData = JsonSerializer.Deserialize<MediaProgress>(content);
                
                if (progressData != null)
                {
                    Debug.WriteLine($"Retrieved progress for {libraryItemId}: {progressData.Progress * 100}% at {progressData.CurrentTime}s");
                }

                return progressData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting item progress: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// Batch updates progress for multiple items
        /// </summary>
        public async Task BatchUpdateProgressAsync(Dictionary<string, (double progress, double currentTime)> progressUpdates)
        {
            if (progressUpdates == null || !progressUpdates.Any())
            {
                Debug.WriteLine("No progress updates provided");
                return;
            }

            Debug.WriteLine($"Batch updating progress for {progressUpdates.Count} items");

            var tasks = progressUpdates.Select(kvp => 
                UpdateProgressAsync(kvp.Key, kvp.Value.progress, kvp.Value.currentTime)
            );

            try
            {
                await Task.WhenAll(tasks);
                Debug.WriteLine("Batch progress update completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during batch progress update: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        /// <summary>
        /// Synchronizes local progress with server progress
        /// </summary>
        public async Task<Dictionary<string, double>> SyncProgressAsync(Dictionary<string, double> localProgress)
        {
            if (localProgress == null)
            {
                Debug.WriteLine("No local progress provided for sync");
                return new Dictionary<string, double>();
            }

            try
            {
                Debug.WriteLine($"Syncing progress for {localProgress.Count} local items");
                
                // Get server progress
                var serverProgress = await GetUserProgressAsync();
                
                // Create merged progress data
                var mergedProgress = new Dictionary<string, double>(serverProgress);
                
                // Update with local changes (local takes precedence)
                foreach (var kvp in localProgress)
                {
                    if (!serverProgress.ContainsKey(kvp.Key) || 
                        Math.Abs(serverProgress[kvp.Key] - kvp.Value) > 0.01) // Allow for small floating point differences
                    {
                        mergedProgress[kvp.Key] = kvp.Value;
                        Debug.WriteLine($"Local progress differs for {kvp.Key}: local={kvp.Value}%, server={serverProgress.GetValueOrDefault(kvp.Key, 0)}%");
                    }
                }

                Debug.WriteLine($"Progress sync completed. Merged {mergedProgress.Count} items");
                return mergedProgress;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during progress sync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return localProgress; // Return local progress as fallback
            }
        }
    }
} 