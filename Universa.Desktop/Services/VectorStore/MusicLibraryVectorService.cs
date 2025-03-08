using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Services.ML;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Service for storing and retrieving music library items using vector search
    /// </summary>
    public class MusicLibraryVectorService
    {
        private readonly IVectorStore _vectorDb;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _collectionName = "music_library";
        private readonly Configuration _config;

        /// <summary>
        /// Creates a new instance of the MusicLibraryVectorService
        /// </summary>
        /// <param name="vectorDb">Vector database service</param>
        public MusicLibraryVectorService(IVectorStore vectorDb)
        {
            _vectorDb = vectorDb;
            _embeddingService = ServiceLocator.Instance.GetService<IEmbeddingService>();
            _config = Configuration.Instance;
            
            Debug.WriteLine($"Initialized MusicLibraryVectorService with EnableLocalEmbeddings={_config.EnableLocalEmbeddings}");
            
            // Ensure collection exists if local embeddings are enabled
            if (_config.EnableLocalEmbeddings)
            {
                Task.Run(async () => 
                {
                    try
                    {
                        await _vectorDb.EnsureCollectionExistsAsync(_collectionName);
                        Debug.WriteLine($"Ensured collection exists: {_collectionName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error ensuring collection exists: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Indexes a music track in the vector database
        /// </summary>
        /// <param name="track">Track to index</param>
        /// <param name="characteristics">Track characteristics (if available)</param>
        /// <returns>ID of the indexed track</returns>
        public async Task<string> IndexTrackAsync(Track track, string characteristics = null)
        {
            try
            {
                if (!_config.EnableLocalEmbeddings)
                {
                    Debug.WriteLine("Local embeddings are disabled, skipping vector indexing");
                    return null;
                }

                // Generate embeddings for track information
                string trackInfo = $"{track.Artist} - {track.Title}";
                if (!string.IsNullOrEmpty(characteristics))
                {
                    trackInfo += $"\n{characteristics}";
                }

                var embedding = await _embeddingService.GenerateEmbeddingAsync(trackInfo);

                // Create metadata
                var metadata = new Dictionary<string, object>
                {
                    ["id"] = track.Id,
                    ["artist"] = track.Artist,
                    ["title"] = track.Title,
                    ["album"] = track.Album,
                    ["duration"] = track.Duration.ToString(),
                    ["characteristics"] = characteristics ?? ""
                };

                // Create vector item
                var vectorItem = new VectorItem(embedding, metadata)
                {
                    Id = track.Id // Use track ID as vector item ID
                };

                // Store in vector database
                await _vectorDb.AddItemAsync(_collectionName, vectorItem);
                Debug.WriteLine($"Indexed track {track.Artist} - {track.Title} with ID {track.Id}");

                return track.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error indexing track: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds similar tracks to the query
        /// </summary>
        /// <param name="query">Query text</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <returns>List of similar tracks</returns>
        public async Task<List<Track>> FindSimilarTracksAsync(string query, int limit = 10)
        {
            try
            {
                if (!_config.EnableLocalEmbeddings)
                {
                    Debug.WriteLine("Local embeddings are disabled, skipping vector search");
                    return new List<Track>();
                }

                // Generate embeddings for the query
                var embedding = await _embeddingService.GenerateEmbeddingAsync(query);

                // Search for similar tracks
                var results = await _vectorDb.SearchAsync(_collectionName, embedding, limit);

                // Convert search results to tracks
                return results.Select(r => new Track
                {
                    Id = r.Metadata.TryGetValue("id", out var id) ? id : "",
                    Artist = r.Metadata.TryGetValue("artist", out var artist) ? artist : "",
                    Title = r.Metadata.TryGetValue("title", out var title) ? title : "",
                    Album = r.Metadata.TryGetValue("album", out var album) ? album : ""
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding similar tracks: {ex.Message}");
                return new List<Track>();
            }
        }

        /// <summary>
        /// Deletes a track from the vector database
        /// </summary>
        /// <param name="trackId">ID of the track to delete</param>
        public async Task DeleteTrackAsync(string trackId)
        {
            try
            {
                await _vectorDb.DeleteItemsAsync(_collectionName, new List<string> { trackId });
                Debug.WriteLine($"Deleted track with ID {trackId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting track: {ex.Message}");
            }
        }

        /// <summary>
        /// Indexes all tracks in the music library
        /// </summary>
        /// <param name="tracks">List of tracks to index</param>
        /// <param name="characterizationStore">Optional characterization store for AI-generated descriptions</param>
        /// <returns>Number of tracks indexed</returns>
        public async Task<int> IndexAllTracksAsync(IEnumerable<Track> tracks, object characterizationStore = null)
        {
            try
            {
                if (!_config.EnableLocalEmbeddings)
                {
                    Debug.WriteLine("Local embeddings are disabled, skipping vector indexing");
                    return 0;
                }

                int count = 0;
                int total = tracks.Count();
                Debug.WriteLine($"Indexing {total} tracks in vector database");

                foreach (var track in tracks)
                {
                    string characteristics = null;
                    
                    // Try to get characteristics from the store if provided
                    if (characterizationStore != null)
                    {
                        // This is a simplified approach - in reality, you'd need to implement
                        // a proper method to get characteristics from the store
                        characteristics = GetCharacteristicsFromStore(characterizationStore, track);
                    }
                    
                    await IndexTrackAsync(track, characteristics);
                    count++;
                    
                    if (count % 100 == 0)
                    {
                        Debug.WriteLine($"Indexed {count}/{total} tracks");
                    }
                }
                
                Debug.WriteLine($"Finished indexing {count} tracks");
                return count;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error indexing tracks: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Gets the number of indexed tracks
        /// </summary>
        /// <returns>Number of tracks</returns>
        public async Task<int> GetIndexedTrackCountAsync()
        {
            try
            {
                return await _vectorDb.GetCollectionSizeAsync(_collectionName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting indexed track count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Clears all indexed tracks
        /// </summary>
        public async Task ClearIndexAsync()
        {
            try
            {
                await _vectorDb.DeleteCollectionAsync(_collectionName);
                Debug.WriteLine("Cleared music library index");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing music library index: {ex.Message}");
            }
        }

        // Helper method to get characteristics from a store
        private string GetCharacteristicsFromStore(object store, Track track)
        {
            // This is a placeholder - implement actual logic based on your characterization store
            return null;
        }
    }
} 