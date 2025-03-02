using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Data;
using Timer = System.Timers.Timer;
using System.Linq;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;

namespace Universa.Desktop.Services
{
    public class CharacterizationProgressEventArgs : EventArgs
    {
        public int ProcessedTracks { get; set; }
        public int TotalTracks { get; set; }
        public double ProgressPercentage { get; set; }
    }

    public class CharacterizationService : IDisposable
    {
        private static CharacterizationService _instance;
        private static readonly object _lock = new object();
        private readonly SubsonicService _subsonicService;
        private readonly SubsonicClient _subsonicClient;
        private readonly Timer _characterizationTimer;
        private readonly CharacterizationStore _store;
        private readonly ML.LocalEmbeddingService _localEmbeddingService;
        private readonly OpenAIService _openAIService;
        private bool _isCharacterizing;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private bool _isDisposed;

        public event EventHandler<CharacterizationProgressEventArgs> CharacterizationProgress;
        public event EventHandler<CharacterizationEventArgs> CharacterizationCompleted;

        public bool IsCharacterizing => _isCharacterizing;

        public CharacterizationService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;

            // Early exit if characterization is not enabled
            if (!_config.EnableAICharacterization)
            {
                Debug.WriteLine("AI Characterization is disabled - service will remain inactive");
                return;
            }

            var config = Configuration.Instance;
            try
            {
                _store = new CharacterizationStore();
            }
            catch (InvalidOperationException ex) when (ex.Message == "Library path not configured")
            {
                // If library path is not configured, create a temporary store
                _store = null;
            }

            _localEmbeddingService = ML.LocalEmbeddingService.Instance;
            
            // Only initialize OpenAIService if enabled and has API key
            if (config.EnableOpenAI && !string.IsNullOrEmpty(config.OpenAIApiKey))
            {
                _openAIService = new OpenAIService(config.OpenAIApiKey);
            }

            // Only initialize SubsonicService if we have valid configuration
            if (!string.IsNullOrEmpty(config.SubsonicUrl) &&
                !string.IsNullOrEmpty(config.SubsonicUsername) &&
                !string.IsNullOrEmpty(config.SubsonicPassword))
            {
                _subsonicService = new SubsonicService(_configService);
                _subsonicClient = new SubsonicClient(
                    config.SubsonicUrl,
                    config.SubsonicUsername,
                    config.SubsonicPassword
                );
            }

            // Initialize timer
            _characterizationTimer = new Timer();
            _characterizationTimer.Interval = TimeSpan.FromHours(24).TotalMilliseconds;
            _characterizationTimer.Elapsed += async (s, e) => await CharacterizeLibraryAsync();

            // Only start timer and check for missing genres if AI characterization is enabled
            // and we have a valid store and subsonic service
            if (config.EnableAICharacterization && config.EnableLocalEmbeddings && 
                _subsonicService != null && _store != null)
            {
                _characterizationTimer.Start();
                
                // Check for missing genres on startup
                Task.Run(async () =>
                {
                    try
                    {
                        Debug.WriteLine("Checking for tracks with missing genres...");
                        var tracks = await _store.GetAllTracks();
                        var tracksWithoutGenre = tracks.Where(t => 
                            !t.Characteristics.Contains("\"Genre:") && 
                            !t.Characteristics.Contains("\"genre:")).ToList();
                        
                        if (tracksWithoutGenre.Any())
                        {
                            Debug.WriteLine($"Found {tracksWithoutGenre.Count} tracks missing genre information");
                            int processed = 0;
                            
                            foreach (var track in tracksWithoutGenre)
                            {
                                try
                                {
                                    // Double check AI is still enabled before each operation
                                    if (!config.EnableAICharacterization)
                                    {
                                        return;
                                    }
                                    if (!config.EnableLocalEmbeddings)
                                    {
                                        Debug.WriteLine("AI characterization was disabled during genre check - stopping");
                                        return;
                                    }

                                    var musicItem = new MusicItem
                                    {
                                        Id = track.Id,
                                        Name = track.Title,
                                        Artist = track.Artist
                                    };
                                    
                                    await CharacterizeTrackAsync(musicItem);
                                    processed++;
                                    
                                    if (processed % 10 == 0)
                                    {
                                        Debug.WriteLine($"Processed {processed} of {tracksWithoutGenre.Count} tracks");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error adding genre for track {track.Artist} - {track.Title}: {ex.Message}");
                                    continue;
                                }
                            }
                            Debug.WriteLine("Completed genre backfill");
                        }
                        else
                        {
                            Debug.WriteLine("No tracks found missing genre information");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking for missing genres: {ex.Message}");
                    }
                });
            }
            else
            {
                Debug.WriteLine("AI Characterization or local embeddings disabled - not checking for missing genres");
            }
        }

        public static CharacterizationService GetInstance()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                        _instance = new CharacterizationService(configService);
                    }
                }
            }
            return _instance;
        }

        public async Task CharacterizeLibraryAsync(bool forceAll = false, bool backfillGenres = false)
        {
            if (!_config.EnableAICharacterization)
            {
                Debug.WriteLine("AI Characterization is disabled - cannot characterize library");
                return;
            }

            if (_isCharacterizing || _subsonicService == null)
            {
                Debug.WriteLine("Characterization already in progress or Subsonic service not initialized");
                return;
            }

            try
            {
                if (backfillGenres)
                {
                    await BackfillGenresAsync();
                    return;
                }

                _isCharacterizing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                Debug.WriteLine("Starting library characterization");
                var config = Configuration.Instance;

                // Get albums directly from SubsonicService
                var albums = await _subsonicClient.GetAllAlbums();
                var albumCount = albums?.Count ?? 0;
                Debug.WriteLine($"Number of albums: {albumCount}");

                var tracks = new List<MusicItem>();

                // Check if we have any albums
                if (albums == null || albumCount == 0)
                {
                    Debug.WriteLine("No albums found. Please ensure the music library has been loaded first.");
                    return;
                }

                // Create a snapshot of album IDs to process
                var albumIds = albums.Select(a => (Id: a.Id, Name: a.Name)).ToList();
                Debug.WriteLine($"Created snapshot of {albumIds.Count} albums to process");

                foreach ((string albumId, string albumName) in albumIds)
                {
                    try
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Debug.WriteLine("Characterization cancelled");
                            break;
                        }

                        var albumTracks = await _subsonicService.GetAlbumTracks(albumId);
                        if (albumTracks != null && albumTracks.Any())
                        {
                            tracks.AddRange(albumTracks);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing album: {ex.Message}");
                        continue;
                    }
                }

                Debug.WriteLine($"Processing {tracks.Count} tracks");
                int processed = 0;
                string lastProcessedTrackId = null;

                foreach (var track in tracks)
                {
                    try
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Debug.WriteLine("Characterization cancelled");
                            break;
                        }

                        var contentHash = CreateContentHash(track.Artist, track.Name);
                        var existing = await _store.GetTrack(track.Id);

                        // Skip if:
                        // 1. Not forcing recharacterization AND
                        // 2. Track exists AND
                        // 3. Content hash matches (same artist/title)
                        if (!forceAll && existing != null && existing.ContentHash == contentHash)
                        {
                            processed++;
                            OnCharacterizationProgress(processed, tracks.Count);
                            lastProcessedTrackId = track.Id;
                            continue;
                        }

                        Debug.WriteLine($"Processing track {processed + 1}/{tracks.Count}: {track.Artist} - {track.Name}");
                        if (existing != null && existing.ContentHash != contentHash)
                        {
                            Debug.WriteLine($"Track content changed (hash mismatch): {track.Artist} - {track.Name}");
                        }

                        var characteristics = await CharacterizeTrackAsync(track);

                        var characterization = new TrackCharacterization
                        {
                            Id = track.Id,
                            Title = track.Name,
                            Artist = track.Artist,
                            ContentHash = contentHash,
                            Characteristics = characteristics,
                            LastVerified = DateTime.Now,
                            NeedsReview = false
                        };

                        await _store.AddOrUpdateTrack(characterization);
                        Debug.WriteLine($"Saved characteristics for: {track.Artist} - {track.Name}");

                        processed++;
                        OnCharacterizationProgress(processed, tracks.Count);
                        lastProcessedTrackId = track.Id;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing track {track.Artist} - {track.Name}: {ex.Message}");
                        
                        // Verify the last successfully processed track
                        if (lastProcessedTrackId != null)
                        {
                            Debug.WriteLine($"Verifying last successfully processed track (ID: {lastProcessedTrackId})");
                            var lastTrack = await _store.GetTrack(lastProcessedTrackId);
                            if (lastTrack == null || string.IsNullOrEmpty(lastTrack.Characteristics))
                            {
                                Debug.WriteLine("Last track verification failed, will retry this track");
                                continue; // Retry the current track
                            }
                        }
                        
                        // Continue with next track
                        continue;
                    }
                }

                Debug.WriteLine("Library characterization complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during library characterization: {ex}");
                throw;
            }
            finally
            {
                _isCharacterizing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private string CreateContentHash(string artist, string title)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var combined = $"{artist}|{title}".ToLowerInvariant();
                var bytes = System.Text.Encoding.UTF8.GetBytes(combined);
                var hash = sha.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        private async Task<string> CharacterizeTrackAsync(MusicItem track)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings)
            {
                Debug.WriteLine($"AI characterization disabled - skipping track {track.Artist} - {track.Name}");
                return "[]";
            }

            try
            {
                // Get track details from Subsonic
                var trackDetails = await _subsonicService.GetTrack(track.Id);
                if (trackDetails == null)
                {
                    Debug.WriteLine($"Could not get track details for {track.Artist} - {track.Name}");
                    return "[]";
                }

                // Build the characterization request
                var request = $"Analyze this music track and provide a detailed set of characteristics as a JSON array. Consider the following aspects:\n" +
                            "1. Genre and subgenres\n" +
                            "2. Mood and emotional qualities\n" +
                            "3. Musical elements (tempo, key, instrumentation)\n" +
                            "4. Vocal characteristics\n" +
                            "5. Era or time period\n" +
                            "6. Cultural significance\n" +
                            "7. Similar artists or influences\n\n" +
                            $"Track: {track.Name}\n" +
                            $"Artist: {track.Artist}\n" +
                            $"Genre: {trackDetails.Genre}\n\n" +
                            "Return ONLY a JSON array of descriptive strings, each focusing on one aspect. Example:\n" +
                            "[\"Alternative rock with grunge influences\", \"Melancholic and introspective\", \"Mid-tempo with heavy guitar riffs\"]";

                // Get the characteristics using the configured AI service
                var characteristics = await GetCharacteristicsFromAI(request);
                if (string.IsNullOrEmpty(characteristics))
                {
                    Debug.WriteLine($"Failed to get characteristics for {track.Artist} - {track.Name}");
                    return "[]";
                }

                // Generate embeddings if enabled
                float[] embeddings = null;
                try
                {
                    if (config.EnableLocalEmbeddings)
                    {
                        embeddings = await _localEmbeddingService.GetEmbeddingsAsync(characteristics);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error generating local embeddings: {ex.Message}");
                }

                // Store the track with its embeddings
                var characterization = new TrackCharacterization
                {
                    Id = track.Id,
                    Title = track.Name,
                    Artist = track.Artist,
                    ContentHash = CreateContentHash(track.Artist, track.Name),
                    Characteristics = characteristics,
                    LastVerified = DateTime.Now,
                    NeedsReview = false
                };

                await _store.AddOrUpdateTrack(characterization);

                // Store embeddings if generated successfully
                if (embeddings != null)
                {
                    await _store.AddOrUpdateEmbeddings(characteristics, embeddings);
                }

                return characteristics;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error characterizing track {track.Artist} - {track.Name}: {ex.Message}");
                return "[]";
            }
        }

        private string GetApiKey(AIProvider provider)
        {
            var config = Configuration.Instance;
            return provider switch
            {
                AIProvider.OpenAI => config.EnableOpenAI ? config.OpenAIApiKey : null,
                AIProvider.Anthropic => config.EnableAnthropic ? config.AnthropicApiKey : null,
                AIProvider.XAI => config.EnableXAI ? config.XAIApiKey : null,
                _ => throw new ArgumentException("Unsupported AI provider")
            };
        }

        private string GetDefaultModel(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => "gpt-3.5-turbo",
                AIProvider.Anthropic => "claude-3-opus-20240229",
                AIProvider.XAI => "grok-1",
                _ => throw new ArgumentException("Unsupported AI provider")
            };
        }

        protected virtual void OnCharacterizationProgress(int processed, int total)
        {
            CharacterizationProgress?.Invoke(this, new CharacterizationProgressEventArgs
            {
                ProcessedTracks = processed,
                TotalTracks = total,
                ProgressPercentage = (double)processed / total * 100
            });
        }

        public void UpdateConfiguration()
        {
            var config = Configuration.Instance;
            if (config.EnableAICharacterization && _subsonicService != null)
            {
                _characterizationTimer.Start();

                Task.Run(async () => 
                {
                    int retryCount = 0;
                    const int maxRetries = 10;
                    const int retryDelayMs = 1000;

                    bool hasAlbums = false;
                    while (!hasAlbums && retryCount < maxRetries)
                    {
                        var albums = await _subsonicClient.GetAllAlbums();
                        var albumCount = albums?.Count ?? 0;
                        hasAlbums = albums != null && albumCount > 0;
                        if (!hasAlbums)
                        {
                            await Task.Delay(retryDelayMs);
                            retryCount++;
                        }
                    }

                    if (hasAlbums)
                    {
                        await CharacterizeLibraryAsync();
                    }
                    else
                    {
                        Debug.WriteLine("Failed to detect loaded music library after maximum retries");
                    }
                });
            }
            else
            {
                _characterizationTimer.Stop();
            }
        }

        public async Task CancelCharacterization(bool silent = false)
        {
            try
            {
                if (_isCharacterizing)
                {
                    _cancellationTokenSource?.Cancel();
                    
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try
                    {
                        while (_isCharacterizing && !timeoutCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(100, timeoutCts.Token);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.WriteLine("Timeout waiting for characterization to cancel");
                    }
                    
                    _isCharacterizing = false;
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = new CancellationTokenSource();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during characterization cancellation: {ex.Message}");
                if (!silent) throw;
            }
        }

        public async Task BackfillGenresAsync()
        {
            if (!_config.EnableAICharacterization)
            {
                Debug.WriteLine("AI Characterization is disabled - cannot backfill genres");
                return;
            }

            if (_isCharacterizing || _subsonicService == null)
            {
                Debug.WriteLine("Cannot backfill genres while characterization is in progress or Subsonic service is not initialized");
                return;
            }

            try
            {
                _isCharacterizing = true;
                _cancellationTokenSource = new CancellationTokenSource();

                Debug.WriteLine("Starting genre backfill");
                var config = Configuration.Instance;
                
                // Get all tracks from store
                var tracks = await _store.GetAllTracks();
                int processed = 0;
                
                foreach (var track in tracks)
                {
                    try
                    {
                        if (_cancellationTokenSource.Token.IsCancellationRequested)
                        {
                            Debug.WriteLine("Genre backfill cancelled");
                            break;
                        }

                        // Check if track characteristics contain genre
                        if (!track.Characteristics.Contains("\"Genre:") && !track.Characteristics.Contains("\"genre:"))
                        {
                            Debug.WriteLine($"Backfilling genre for track: {track.Artist} - {track.Title}");
                            
                            // Get track from Subsonic to get genre
                            var musicItem = await _subsonicService.GetTrack(track.Id);
                            if (musicItem != null && !string.IsNullOrEmpty(musicItem.Genre))
                            {
                                // Update characteristics to include genre
                                var characteristics = await CharacterizeTrackAsync(musicItem);
                                track.Characteristics = characteristics;
                                await _store.AddOrUpdateTrack(track);
                                Debug.WriteLine($"Added genre {musicItem.Genre} to track characteristics");
                            }
                        }

                        processed++;
                        OnCharacterizationProgress(processed, tracks.Count);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing track {track.Artist} - {track.Title}: {ex.Message}");
                        continue;
                    }
                }

                Debug.WriteLine("Genre backfill complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during genre backfill: {ex}");
                throw;
            }
            finally
            {
                _isCharacterizing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                try
                {
                    if (_characterizationTimer != null)
                    {
                        _characterizationTimer.Stop();
                        _characterizationTimer.Dispose();
                    }
                    
                    if (_isCharacterizing)
                    {
                        try
                        {
                            CancelCharacterization(silent: true).Wait();
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error cancelling characterization during disposal: {ex.Message}");
                        }
                    }
                    
                    try
                    {
                        if (_store != null)
                        {
                            _store.SaveIfDirtyAsync().Wait();
                            _store.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error saving store during disposal: {ex.Message}");
                    }
                    
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Cancel();
                        _cancellationTokenSource.Dispose();
                        _cancellationTokenSource = null;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error during CharacterizationService disposal: {ex.Message}");
                }
                _isDisposed = true;
            }
        }

        private async Task<string> GetCharacteristicsFromAI(string request)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization)
            {
                return "[]";
            }

            try
            {
                var chain = MusicChain.GetInstance(
                    GetApiKey(config.DefaultAIProvider),
                    GetDefaultModel(config.DefaultAIProvider),
                    config.DefaultAIProvider,
                    request,
                    null);

                var response = await chain.ProcessRequest(request, request);
                return response?.Trim() ?? "[]";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting characteristics from AI: {ex.Message}");
                return "[]";
            }
        }

        private async Task<float[]> GetEmbeddingsAsync(string text)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings)
            {
                return null;
            }

            try
            {
                if (config.EnableLocalEmbeddings)
                {
                    return await _localEmbeddingService.GetEmbeddingsAsync(text);
                }
                else
                {
                    return await _openAIService.GetEmbeddings(text);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting embeddings: {ex.Message}");
                throw;
            }
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            switch (e.Key)
            {
                case nameof(ConfigurationProvider.EnableAICharacterization):
                    if (!_config.EnableAICharacterization)
                    {
                        // Clean up any active characterization tasks
                        StopCharacterization();
                    }
                    break;
                case nameof(ConfigurationProvider.OpenAIApiKey):
                case nameof(ConfigurationProvider.AnthropicApiKey):
                    // Validate API keys and update service status
                    ValidateApiKeys();
                    break;
            }
        }

        private void ValidateApiKeys()
        {
            // Validate API keys and update service status
            // ...
        }

        private void StopCharacterization()
        {
            // Stop any active characterization tasks
            // ...
        }

        private void OnCharacterizationCompleted(CharacterizationResult result, string error)
        {
            CharacterizationCompleted?.Invoke(this, new CharacterizationEventArgs(result, error));
        }
    }

    public class CharacterizationEventArgs : EventArgs
    {
        public CharacterizationResult Result { get; }
        public string Error { get; }

        public CharacterizationEventArgs(CharacterizationResult result, string error)
        {
            Result = result;
            Error = error;
        }
    }

    public class CharacterizationResult
    {
        public string Summary { get; set; }
        public string[] Keywords { get; set; }
        public double Sentiment { get; set; }
    }
} 