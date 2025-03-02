using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using System.IO;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Diagnostics;
using System.Threading;

namespace Universa.Desktop.Data
{
    public class CharacterizationStore : IDisposable
    {
        private readonly ConcurrentDictionary<string, TrackCharacterization> _store;
        private readonly string _storePath;
        private volatile bool _isDirty;
        private Timer _embeddingsCheckTimer;
        private readonly object _syncLock = new object();
        private volatile bool _isGeneratingEmbeddings;
        private readonly SemaphoreSlim _saveSemaphore = new SemaphoreSlim(1, 1);

        public CharacterizationStore()
        {
            _store = new ConcurrentDictionary<string, TrackCharacterization>();
            
            var config = Configuration.Instance;
            if (string.IsNullOrEmpty(config.LibraryPath))
            {
                // If library path is not configured, use a temporary path in AppData
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Universa",
                    ".universa"
                );
                Directory.CreateDirectory(appDataPath);
                _storePath = Path.Combine(appDataPath, "track_characteristics.json");
                return;
            }

            // Create .universa folder in library if it doesn't exist
            var universaFolder = Path.Combine(config.LibraryPath, ".universa");
            Directory.CreateDirectory(universaFolder);
            
            // Store everything in track_characteristics.json
            _storePath = Path.Combine(universaFolder, "track_characteristics.json");
            
            LoadData();

            // Only initialize embeddings checking if AI characterization is enabled
            if (config.EnableAICharacterization)
            {
                // Check for missing embeddings immediately
                Task.Run(async () => await CheckForMissingEmbeddingsAsync());

                // Start periodic check for missing embeddings (every 5 minutes)
                _embeddingsCheckTimer = new Timer(async _ => await CheckForMissingEmbeddingsAsync(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
            }
        }

        private void LoadData()
        {
            try
            {
                if (File.Exists(_storePath))
                {
                    LoadFromPath(_storePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during data load: {ex.Message}");
                // Continue with empty store
            }
        }

        private void LoadFromPath(string path)
        {
            var json = File.ReadAllText(path);
            try
            {
                var trackDict = JsonSerializer.Deserialize<Dictionary<string, TrackCharacterization>>(json);
                foreach (var kvp in trackDict)
                {
                    _store.TryAdd(kvp.Key, kvp.Value);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading from {path}: {ex.Message}");
                // Continue with empty store
            }
        }

        private async Task SaveDataAsync()
        {
            if (!_isDirty) return;

            await _saveSemaphore.WaitAsync();
            try
            {
                if (!_isDirty) return;

                const int maxRetries = 3;
                const int retryDelayMs = 100;
                var attempt = 0;

                while (true)
                {
                    try
                    {
                        // Debug.WriteLine($"Preparing to save {_store.Count} tracks");
                        
                        var storeSnapshot = new Dictionary<string, TrackCharacterization>(_store);
                        var json = JsonSerializer.Serialize(storeSnapshot, new JsonSerializerOptions 
                        { 
                            WriteIndented = true 
                        });

                        var tempPath = _storePath + ".tmp";
                        await File.WriteAllTextAsync(tempPath, json);

                        if (File.Exists(_storePath))
                        {
                            File.Delete(_storePath);
                        }
                        File.Move(tempPath, _storePath);

                        // Debug.WriteLine($"Successfully saved tracks to {_storePath}");
                        _isDirty = false;
                        return;
                    }
                    catch (IOException) when (++attempt < maxRetries)
                    {
                        // Debug.WriteLine($"Save attempt {attempt} failed, retrying in {retryDelayMs}ms...");
                        await Task.Delay(retryDelayMs);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error saving data: {ex.Message}");
                        // Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        throw;
                    }
                }
            }
            finally
            {
                _saveSemaphore.Release();
            }
        }

        public async Task AddOrUpdateTrack(TrackCharacterization track)
        {
            try
            {
                var config = Configuration.Instance;
                // Only attempt to generate embeddings if both AI characterization and local embeddings are enabled
                if (config.EnableAICharacterization && config.EnableLocalEmbeddings && 
                    !string.IsNullOrEmpty(track.Characteristics) && track.Embeddings == null)
                {
                    try
                    {
                        track.Embeddings = await GetEmbeddingsAsync(track.Characteristics);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Warning: Failed to generate embeddings: {ex.Message}");
                        // If embeddings generation fails, ensure we don't keep a null embeddings array
                        track.Embeddings = Array.Empty<float>();
                    }
                }
                else if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings)
                {
                    // Clear embeddings if AI features are disabled
                    track.Embeddings = Array.Empty<float>();
                }

                _store.AddOrUpdate(track.Id, track, (_, __) => track);
                _isDirty = true;
                await SaveDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding/updating track: {ex.Message}");
                throw;
            }
        }

        private async Task EnsureEmbeddingsAsync()
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings) 
            {
                // Clear all embeddings if AI features are disabled
                foreach (var track in _store.Values)
                {
                    if (track.Embeddings != null && track.Embeddings.Length > 0)
                    {
                        track.Embeddings = Array.Empty<float>();
                        _isDirty = true;
                    }
                }
                if (_isDirty)
                {
                    await SaveDataAsync();
                }
                return;
            }

            try
            {
                var missingEmbeddings = _store.Values
                    .Where(t => !string.IsNullOrEmpty(t.Characteristics) && 
                               (t.Embeddings == null || t.Embeddings.Length == 0))
                    .Select(t => t.Characteristics)
                    .Distinct()
                    .ToList();

                if (missingEmbeddings.Any())
                {
                    var processed = 0;

                    foreach (var characteristics in missingEmbeddings)
                    {
                        // Double check AI is still enabled before each operation
                        if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings)
                        {
                            return;
                        }

                        try
                        {
                            var embeddings = await GetEmbeddingsAsync(characteristics);
                            
                            foreach (var track in _store.Values.Where(t => t.Characteristics == characteristics))
                            {
                                track.Embeddings = embeddings;
                            }
                            
                            processed++;

                            if (processed % 10 == 0)
                            {
                                await SaveDataAsync();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Warning: Failed to generate embeddings: {ex.Message}");
                            continue;
                        }
                    }

                    await SaveDataAsync();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring embeddings: {ex.Message}");
            }
        }

        public async Task<TrackCharacterization> GetTrack(string id)
        {
            try
            {
                _store.TryGetValue(id, out var track);
                return track;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting track: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TrackCharacterization>> FindSimilarTracks(string characteristics, int limit = 10)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization)
            {
                return new List<TrackCharacterization>();
            }

            try
            {
                await EnsureEmbeddingsAsync();

                var tracks = _store.Values
                    .Where(t => !string.IsNullOrEmpty(t.Characteristics) && t.Embeddings != null)
                    .ToList();

                if (!tracks.Any())
                {
                    return new List<TrackCharacterization>();
                }

                float[] queryEmbeddings;
                try
                {
                    queryEmbeddings = await GetEmbeddingsAsync(characteristics);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to get embeddings for query: {ex.Message}");
                    return new List<TrackCharacterization>();
                }

                var similarities = new ConcurrentBag<(TrackCharacterization Track, float Similarity)>();
                var processedCount = 0;

                await Task.Run(() => 
                {
                    Parallel.ForEach(tracks, track =>
                    {
                        try
                        {
                            var similarity = CosineSimilarity(queryEmbeddings, track.Embeddings);
                            similarities.Add((track, similarity));
                            Interlocked.Increment(ref processedCount);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error processing track {track.Artist} - {track.Title}: {ex.Message}");
                        }
                    });
                });

                if (_isDirty)
                {
                    await SaveDataAsync();
                }

                return similarities
                    .OrderByDescending(x => x.Similarity)
                    .Take(limit)
                    .Select(x => x.Track)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding similar tracks: {ex.Message}");
                return new List<TrackCharacterization>();
            }
        }

        public async Task<float[]> GetEmbeddingsAsync(string text)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization)
            {
                throw new InvalidOperationException("Local embeddings are disabled");
            }

            var localService = Services.ML.LocalEmbeddingService.Instance;
            return await localService.GetEmbeddingsAsync(text);
        }

        private float CosineSimilarity(float[] a, float[] b)
        {
            var dotProduct = a.Zip(b, (x, y) => x * y).Sum();
            var normA = (float)Math.Sqrt(a.Sum(x => x * x));
            var normB = (float)Math.Sqrt(b.Sum(x => x * x));
            return dotProduct / (normA * normB);
        }

        public async Task AddOrUpdateEmbeddings(string text, float[] embeddings)
        {
            _store.AddOrUpdate(text, new TrackCharacterization { Characteristics = text, Embeddings = embeddings }, (_, __) => new TrackCharacterization { Characteristics = text, Embeddings = embeddings });
            _isDirty = true;
            await SaveDataAsync();
        }

        public async Task<(int Total, int Missing, List<string> SampleMissing)> CheckMissingEmbeddingsAsync()
        {
            try
            {
                var uniqueCharacteristics = _store.Values
                    .Where(t => !string.IsNullOrEmpty(t.Characteristics))
                    .Select(t => t.Characteristics)
                    .Distinct()
                    .ToList();

                var missingEmbeddings = uniqueCharacteristics
                    .Where(c => !_store.Values.Any(t => t.Characteristics == c && t.Embeddings != null))
                    .ToList();

                var sampleMissing = missingEmbeddings.Take(5).ToList();
                return (uniqueCharacteristics.Count, missingEmbeddings.Count, sampleMissing);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking missing embeddings: {ex.Message}");
                throw;
            }
        }

        public async Task RegenerateAllEmbeddingsAsync(IProgress<(int Current, int Total)> progress = null)
        {
            try
            {
                var uniqueCharacteristics = _store.Values
                    .Where(t => !string.IsNullOrEmpty(t.Characteristics))
                    .Select(t => t.Characteristics)
                    .Distinct()
                    .ToList();

                var processed = 0;
                var total = uniqueCharacteristics.Count;

                foreach (var characteristics in uniqueCharacteristics)
                {
                    try
                    {
                        if (_store.Values.All(t => t.Characteristics != characteristics || t.Embeddings == null))
                        {
                            var embeddings = await GetEmbeddingsAsync(characteristics);
                            processed++;
                            progress?.Report((processed, total));

                            if (processed % 10 == 0)
                            {
                                await SaveDataAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing characteristics: {ex.Message}");
                        continue;
                    }
                }

                await SaveDataAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during embeddings regeneration: {ex.Message}");
                throw;
            }
        }

        public async Task<List<TrackCharacterization>> FindSimilarTracksFromEmbeddings(float[] queryEmbeddings)
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization)
            {
                return new List<TrackCharacterization>();
            }

            var similarTracks = await Task.Run(() =>
            {
                return _store.Values
                    .AsParallel()
                    .Where(track => track.Embeddings != null && track.Embeddings.Length > 0)
                    .Select(track =>
                    {
                        var similarity = CosineSimilarity(queryEmbeddings, track.Embeddings);
                        var originalSimilarity = similarity;

                        var trackCharacteristics = track.Characteristics
                            .Split(new[] { '[', ']', '"', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLower())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();

                        var queryCharacteristics = track.Characteristics
                            .Split(new[] { '[', ']', '"', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim().ToLower())
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();

                        var trackGenre = trackCharacteristics.FirstOrDefault() ?? "";
                        var queryGenre = queryCharacteristics.FirstOrDefault() ?? "";

                        var genrePenalties = new Dictionary<string, float>
                        {
                            { "classical", 0.7f },
                            { "opera", 0.7f },
                            { "baroque", 0.7f },
                            { "film score", 0.8f },
                            { "easy listening", 0.8f },
                            { "folk", 0.85f },
                            { "country", 0.85f }
                        };

                        foreach (var penalty in genrePenalties)
                        {
                            if (trackGenre.Contains(penalty.Key))
                            {
                                similarity *= penalty.Value;
                                break;
                            }
                        }

                        if (trackGenre.Contains("rock") || trackGenre.Contains("pop rock"))
                        {
                            similarity *= 1.2f;
                        }

                        return new { Track = track, Similarity = similarity };
                    })
                    .Where(result => result.Similarity > 0.90f)
                    .OrderByDescending(x => x.Similarity)
                    .Take(15)
                    .Select(x => x.Track)
                    .ToList();
            });

            return similarTracks;
        }

        public async Task SaveIfDirtyAsync()
        {
            if (_isDirty)
            {
                await SaveDataAsync();
            }
        }

        private async Task CheckForMissingEmbeddingsAsync()
        {
            var config = Configuration.Instance;
            if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings || _isGeneratingEmbeddings) return;

            lock (_syncLock)
            {
                if (_isGeneratingEmbeddings) return;
                _isGeneratingEmbeddings = true;
            }

            try
            {
                List<TrackCharacterization> tracksNeedingEmbeddings;
                lock (_syncLock)
                {
                    tracksNeedingEmbeddings = _store.Values
                        .Where(t => !string.IsNullOrEmpty(t.Characteristics) && 
                               (t.Embeddings == null || t.Embeddings.Length == 0))
                        .ToList();
                }

                if (tracksNeedingEmbeddings.Any())
                {
                    var embeddingsGenerated = false;
                    
                    foreach (var track in tracksNeedingEmbeddings)
                    {
                        // Double check AI settings before each operation
                        if (!config.EnableAICharacterization || !config.EnableLocalEmbeddings)
                        {
                            return;
                        }

                        try
                        {
                            track.Embeddings = await GetEmbeddingsAsync(track.Characteristics);
                            embeddingsGenerated = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error generating embeddings for track: {ex.Message}");
                            track.Embeddings = Array.Empty<float>();
                            continue;
                        }
                    }

                    if (embeddingsGenerated)
                    {
                        _isDirty = true;
                        await SaveDataAsync();
                    }
                }
            }
            finally
            {
                _isGeneratingEmbeddings = false;
            }
        }

        public void Dispose()
        {
            _embeddingsCheckTimer?.Dispose();
            _saveSemaphore?.Dispose();
            SaveIfDirtyAsync().Wait();
        }

        public async Task<List<TrackCharacterization>> GetAllTracks()
        {
            await LoadIfNeeded();
            return _store.Values.ToList();
        }

        private async Task LoadIfNeeded()
        {
            if (_store == null)
            {
                await LoadStore();
            }
        }

        private async Task LoadStore()
        {
            try
            {
                _store.Clear();
                
                if (File.Exists(_storePath))
                {
                    var json = await File.ReadAllTextAsync(_storePath);
                    var data = JsonSerializer.Deserialize<Dictionary<string, TrackCharacterization>>(json);
                    
                    foreach (var kvp in data)
                    {
                        _store.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading characterization store: {ex.Message}");
                _store.Clear();
            }
        }
    }
} 