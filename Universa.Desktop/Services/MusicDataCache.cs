using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;
using System.Linq;
using System.Text.Json.Serialization;

namespace Universa.Desktop.Services
{
    public class MusicDataCache
    {
        private readonly string _cachePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public MusicDataCache()
        {
            _cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "music_cache.json"
            );
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                ReferenceHandler = ReferenceHandler.Preserve,
                Converters = 
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
            };

            // Create directory if it doesn't exist
            var cacheDir = Path.GetDirectoryName(_cachePath);
            if (!Directory.Exists(cacheDir))
            {
                Directory.CreateDirectory(cacheDir);
            }
        }

        public async Task SaveMusicData(List<MusicItem> musicData)
        {
            try
            {
                Debug.WriteLine($"Saving {musicData?.Count ?? 0} music items to cache");
                var itemsToSave = musicData?.Select(item =>
                {
                    // Ensure Type is properly set for playlists
                    if (item.Type == MusicItemType.Playlist)
                    {
                        Debug.WriteLine($"Saving playlist: {item.Name} with {item.Items?.Count ?? 0} tracks");
                    }
                    return item;
                }).ToList();

                var json = JsonSerializer.Serialize(itemsToSave, _jsonOptions);
                await File.WriteAllTextAsync(_cachePath, json);
                Debug.WriteLine("Music data successfully saved to cache");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving music cache: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public async Task<List<MusicItem>> LoadMusicData()
        {
            try
            {
                if (!File.Exists(_cachePath))
                {
                    Debug.WriteLine("No music cache file found");
                    return new List<MusicItem>();
                }

                Debug.WriteLine("Loading music data from cache");
                var json = await File.ReadAllTextAsync(_cachePath);
                var musicData = JsonSerializer.Deserialize<List<MusicItem>>(json, _jsonOptions);
                
                // Verify playlist items after deserialization
                if (musicData != null)
                {
                    var playlists = musicData.Where(item => item.Type == MusicItemType.Playlist).ToList();
                    Debug.WriteLine($"Found {playlists.Count} playlists in cache");
                    foreach (var playlist in playlists)
                    {
                        Debug.WriteLine($"Loaded playlist: {playlist.Name} with {playlist.Items?.Count ?? 0} tracks");
                    }
                }
                
                Debug.WriteLine($"Loaded {musicData?.Count ?? 0} music items from cache");
                return musicData ?? new List<MusicItem>();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading music cache: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<MusicItem>();
            }
        }

        public void ClearCache()
        {
            try
            {
                if (File.Exists(_cachePath))
                {
                    File.Delete(_cachePath);
                    Debug.WriteLine("Music cache cleared successfully");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing music cache: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
    }
} 