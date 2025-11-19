using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Interfaces
{
    public interface IAudiobookshelfService : IDisposable
    {
        /// <summary>
        /// Authenticates with the Audiobookshelf server
        /// </summary>
        Task<bool> LoginAsync();

        /// <summary>
        /// Gets all available libraries from the server
        /// </summary>
        Task<List<AudiobookshelfLibraryResponse>> GetLibrariesAsync();

        /// <summary>
        /// Gets all items from all libraries
        /// </summary>
        Task<List<AudiobookItem>> GetLibraryItemsAsync();

        /// <summary>
        /// Gets contents of a specific library
        /// </summary>
        Task<List<AudiobookItem>> GetLibraryContentsAsync(string libraryId);

        /// <summary>
        /// Gets user's reading progress for all items
        /// </summary>
        Task<Dictionary<string, double>> GetUserProgressAsync();

        /// <summary>
        /// Updates reading progress for a specific item
        /// </summary>
        Task UpdateProgressAsync(string libraryItemId, double progress, double currentTime);

        /// <summary>
        /// Gets items currently in progress for a specific library
        /// </summary>
        Task<List<AudiobookItem>> GetInProgressItemsAsync(string libraryId);

        /// <summary>
        /// Gets detailed information for a specific audiobook item
        /// </summary>
        Task<AudiobookItem> GetItemDetailsAsync(string itemId);

        /// <summary>
        /// Gets episodes for a specific podcast
        /// </summary>
        Task<List<AudiobookItem>> GetPodcastEpisodesAsync(string podcastId);

        /// <summary>
        /// Gets the stream URL for a specific item
        /// </summary>
        Task<string> GetStreamUrlAsync(string itemId);
    }
} 