using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Main orchestrating service for Audiobookshelf integration
    /// Coordinates authentication, library, and progress services
    /// </summary>
    public class AudiobookshelfService : IAudiobookshelfService
    {
        private readonly HttpClient _client;
        private readonly AudiobookshelfAuthService _authService;
        private readonly AudiobookshelfLibraryService _libraryService;
        private readonly AudiobookshelfProgressService _progressService;
        private readonly string _baseUrl;

        public AudiobookshelfService(string baseUrl, string username, string password)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentNullException(nameof(baseUrl), "Audiobookshelf base URL cannot be null or empty");
            if (string.IsNullOrEmpty(username))
                throw new ArgumentNullException(nameof(username), "Audiobookshelf username cannot be null or empty");
            if (string.IsNullOrEmpty(password))
                throw new ArgumentNullException(nameof(password), "Audiobookshelf password cannot be null or empty");

            _baseUrl = baseUrl.TrimEnd('/');
            _client = new HttpClient();

            // Initialize sub-services
            _authService = new AudiobookshelfAuthService(_client, _baseUrl, username, password);
            _libraryService = new AudiobookshelfLibraryService(_client, _authService, _baseUrl);
            _progressService = new AudiobookshelfProgressService(_client, _authService, _baseUrl);
        }

        /// <summary>
        /// Authenticates with the Audiobookshelf server
        /// </summary>
        public async Task<bool> LoginAsync()
        {
            return await _authService.LoginAsync();
        }

        /// <summary>
        /// Gets all items from all libraries
        /// </summary>
        public async Task<List<AudiobookItem>> GetLibraryItemsAsync()
        {
            return await _libraryService.GetLibraryItemsAsync();
        }

        /// <summary>
        /// Gets contents of a specific library
        /// </summary>
        public async Task<List<AudiobookItem>> GetLibraryContentsAsync(string libraryId)
        {
            return await _libraryService.GetLibraryContentsAsync(libraryId);
        }

        /// <summary>
        /// Gets all available libraries from the server
        /// </summary>
        public async Task<List<AudiobookshelfLibraryResponse>> GetLibrariesAsync()
        {
            return await _libraryService.GetLibrariesAsync();
        }

        /// <summary>
        /// Gets user's reading progress for all items
        /// </summary>
        public async Task<Dictionary<string, double>> GetUserProgressAsync()
        {
            return await _progressService.GetUserProgressAsync();
        }

        /// <summary>
        /// Updates reading progress for a specific item
        /// </summary>
        public async Task UpdateProgressAsync(string libraryItemId, double progress, double currentTime)
        {
            await _progressService.UpdateProgressAsync(libraryItemId, progress, currentTime);
        }

        /// <summary>
        /// Gets items currently in progress for a specific library
        /// </summary>
        public async Task<List<AudiobookItem>> GetInProgressItemsAsync(string libraryId)
        {
            return await _libraryService.GetInProgressItemsAsync(libraryId);
        }

        /// <summary>
        /// Gets detailed information for a specific audiobook item
        /// </summary>
        public async Task<AudiobookItem> GetItemDetailsAsync(string itemId)
        {
            return await _libraryService.GetItemDetailsAsync(itemId);
        }

        /// <summary>
        /// Gets episodes for a specific podcast
        /// </summary>
        public async Task<List<AudiobookItem>> GetPodcastEpisodesAsync(string podcastId)
        {
            return await _libraryService.GetPodcastEpisodesAsync(podcastId);
        }

        /// <summary>
        /// Gets the stream URL for a specific item
        /// </summary>
        public async Task<string> GetStreamUrlAsync(string itemId)
        {
            return await _libraryService.GetStreamUrlAsync(itemId);
        }

        /// <summary>
        /// Disposes of the service and its resources
        /// </summary>
        public void Dispose()
        {
            _authService?.Dispose();
            _client?.Dispose();
        }
    }
} 