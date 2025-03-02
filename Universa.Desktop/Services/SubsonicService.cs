using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Universa.Desktop.Models;
using System.Text.Json.Serialization;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class SubsonicService : ISubsonicService
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _user;
        private readonly string _password;
        private readonly string _salt;
        private readonly string _token;
        private SubsonicClient _subsonicClient;
        private readonly IConfigurationService _configService;

        public SubsonicService(IConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            var config = _configService.Provider;

            _baseUrl = config.SubsonicUrl;
            _user = config.SubsonicUsername;
            _password = config.SubsonicPassword;

            _client = new HttpClient();
            _salt = GenerateSalt();
            _token = GenerateToken(_password, _salt);
            _subsonicClient = new SubsonicClient(_baseUrl, _user, _password);
        }

        public async Task<Album> GetAlbumAsync(string albumId)
        {
            try
            {
                var url = $"{_baseUrl}/rest/getAlbum.view" +
                         $"?u={_user}" +
                         $"&t={_token}" +
                         $"&s={_salt}" +
                         $"&v=1.16.1" +
                         $"&c=Universa" +
                         $"&f=json" +
                         $"&id={albumId}";

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<SubsonicWrapper>(content, options);
                var subsonicAlbum = wrapper?.SubsonicResponse?.Album;

                if (subsonicAlbum == null)
                {
                    return new Album
                    {
                        Id = albumId,
                        Title = "Unknown Album",
                        Artist = "Unknown Artist"
                    };
                }

                return new Album
                {
                    Id = subsonicAlbum.Id,
                    Title = subsonicAlbum.Name,
                    Artist = subsonicAlbum.Artist,
                    CoverArtUrl = $"{_baseUrl}/rest/getCoverArt.view?id={subsonicAlbum.Id}&u={_user}&t={_token}&s={_salt}&v=1.16.1&c=Universa"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting album {albumId}: {ex.Message}");
                return new Album
                {
                    Id = albumId,
                    Title = "Unknown Album",
                    Artist = "Unknown Artist"
                };
            }
        }

        public async Task<TrackInfo> GetTrackInfoAsync(string trackId)
        {
            try
            {
                var url = $"{_baseUrl}/rest/getSong.view" +
                         $"?u={_user}" +
                         $"&t={_token}" +
                         $"&s={_salt}" +
                         $"&v=1.16.1" +
                         $"&c=Universa" +
                         $"&f=json" +
                         $"&id={trackId}";

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<SubsonicWrapper>(content, options);
                var song = wrapper?.SubsonicResponse?.Song;

                if (song == null)
                {
                    return new TrackInfo
                    {
                        Id = trackId,
                        Title = "Unknown Track",
                        Duration = TimeSpan.Zero,
                        Artist = "Unknown Artist",
                        Album = "Unknown Album"
                    };
                }

                return new TrackInfo
                {
                    Id = song.Id,
                    Title = song.Title,
                    Duration = TimeSpan.FromSeconds(song.Duration),
                    Artist = song.Artist,
                    Album = song.Album
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting track info {trackId}: {ex.Message}");
                return new TrackInfo
                {
                    Id = trackId,
                    Title = "Unknown Track",
                    Duration = TimeSpan.Zero,
                    Artist = "Unknown Artist",
                    Album = "Unknown Album"
                };
            }
        }

        public async Task RefreshConfiguration()
        {
            var config = _configService.Provider;
            if (!string.IsNullOrEmpty(config.SubsonicUrl) &&
                !string.IsNullOrEmpty(config.SubsonicUsername) &&
                !string.IsNullOrEmpty(config.SubsonicPassword))
            {
                _subsonicClient = new SubsonicClient(config.SubsonicUrl, config.SubsonicUsername, config.SubsonicPassword);
            }
            await Task.CompletedTask;
        }

        private string GenerateSalt()
        {
            var random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var salt = new char[6];
            for (int i = 0; i < salt.Length; i++)
            {
                salt[i] = chars[random.Next(chars.Length)];
            }
            return new string(salt);
        }

        private string GenerateToken(string password, string salt)
        {
            using var md5 = MD5.Create();
            var input = Encoding.UTF8.GetBytes(password + salt);
            var hash = md5.ComputeHash(input);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        public async Task<List<MusicItem>> GetAllTracks()
        {
            return await _subsonicClient.GetAllTracks();
        }

        public async Task<List<MusicItem>> GetPlaylists()
        {
            try
            {
                return await _subsonicClient.GetPlaylists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting playlists: {ex.Message}");
                return new List<MusicItem>();
            }
        }

        public async Task<List<MusicItem>> GetPlaylistTracks(string playlistId)
        {
            try
            {
                return await _subsonicClient.GetPlaylistTracks(playlistId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting playlist tracks: {ex.Message}");
                return new List<MusicItem>();
            }
        }

        public async Task AddToPlaylistAsync(string playlistId, string songId)
        {
            try
            {
                await _subsonicClient.AddToPlaylistAsync(playlistId, songId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding song to playlist: {ex.Message}");
                throw;
            }
        }

        public async Task CreatePlaylistAsync(string name)
        {
            try
            {
                var url = $"{_baseUrl}/rest/createPlaylist.view" +
                         $"?u={_user}" +
                         $"&t={_token}" +
                         $"&s={_salt}" +
                         $"&v=1.16.1" +
                         $"&c=Universa" +
                         $"&f=json" +
                         $"&name={Uri.EscapeDataString(name)}";

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<SubsonicWrapper>(content, options);
                if (wrapper?.SubsonicResponse?.Status != "ok")
                {
                    throw new Exception($"Failed to create playlist: {wrapper?.SubsonicResponse?.Status}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating playlist: {ex.Message}");
                throw;
            }
        }

        public async Task DeletePlaylistAsync(string playlistId)
        {
            try
            {
                var url = $"{_baseUrl}/rest/deletePlaylist.view" +
                         $"?u={_user}" +
                         $"&t={_token}" +
                         $"&s={_salt}" +
                         $"&v=1.16.1" +
                         $"&c=Universa" +
                         $"&f=json" +
                         $"&id={Uri.EscapeDataString(playlistId)}";

                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<SubsonicWrapper>(content, options);
                if (wrapper?.SubsonicResponse?.Status != "ok")
                {
                    throw new Exception($"Failed to delete playlist: {wrapper?.SubsonicResponse?.Status}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting playlist: {ex.Message}");
                throw;
            }
        }

        public async Task<List<MusicItem>> GetAlbumTracks(string albumId)
        {
            var tracks = new List<MusicItem>();
            
            var url = $"{_baseUrl}/rest/getAlbum.view" +
                     $"?u={_user}" +
                     $"&t={_token}" +
                     $"&s={_salt}" +
                     $"&v=1.16.1" +
                     $"&c=Universa" +
                     $"&f=json" +
                     $"&id={albumId}";

            try
            {
                var response = await _client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                var wrapper = JsonSerializer.Deserialize<SubsonicWrapper>(content, options);
                
                if (wrapper?.SubsonicResponse == null)
                {
                    Debug.WriteLine($"Error: No response from Subsonic API");
                    return tracks;
                }

                if (wrapper.SubsonicResponse.Status != "ok")
                {
                    Debug.WriteLine($"Error response from Subsonic API: Status = {wrapper.SubsonicResponse.Status}");
                    return tracks;
                }

                var album = wrapper.SubsonicResponse.Album;
                if (album?.Song != null)
                {
                    foreach (var song in album.Song)
                    {
                        if (string.IsNullOrEmpty(song.Id) || string.IsNullOrEmpty(song.Title))
                        {
                            Debug.WriteLine($"Skipping invalid song in album {albumId}: Missing ID or title");
                            continue;
                        }

                        var track = new MusicItem
                        {
                            Id = song.Id,
                            Name = song.Title,
                            Artist = song.Artist ?? album.Artist,
                            Album = album.Name,
                            Type = MusicItemType.Track,
                            Duration = TimeSpan.FromSeconds(song.Duration),
                            StreamUrl = $"{_baseUrl}/rest/stream.view?id={song.Id}&u={_user}&t={_token}&s={_salt}&v=1.16.1&c=Universa"
                        };
                        track.InitializeIconData();
                        tracks.Add(track);
                    }
                }
                else
                {
                    Debug.WriteLine($"No tracks found in album {albumId}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting tracks for album {albumId}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return tracks;
        }

        public async Task<MusicItem> GetTrack(string id)
        {
            try
            {
                var track = await _subsonicClient.GetTrack(id);
                return track;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting track {id}: {ex.Message}");
                return null;
            }
        }

        public async Task<List<MusicItem>> GetArtists()
        {
            try
            {
                if (_subsonicClient == null)
                {
                    throw new InvalidOperationException("Subsonic client is not initialized");
                }
                return await _subsonicClient.GetArtists();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting artists: {ex.Message}");
                return new List<MusicItem>();
            }
        }

        public async Task<List<MusicItem>> GetAllAlbums()
        {
            try
            {
                if (_subsonicClient == null)
                {
                    throw new InvalidOperationException("Subsonic client is not initialized");
                }
                return await _subsonicClient.GetAllAlbums();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting albums: {ex.Message}");
                return new List<MusicItem>();
            }
        }

        public async Task<List<MusicItem>> GetTracks(string albumId)
        {
            try
            {
                if (_subsonicClient == null)
                {
                    throw new InvalidOperationException("Subsonic client is not initialized");
                }
                return await _subsonicClient.GetTracks(albumId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting tracks: {ex.Message}");
                return new List<MusicItem>();
            }
        }

        private class SubsonicWrapper
        {
            [JsonPropertyName("subsonic-response")]
            public SubsonicResponse SubsonicResponse { get; set; }
        }

        private class SubsonicResponse
        {
            [JsonPropertyName("status")]
            public string Status { get; set; }
            
            [JsonPropertyName("version")]
            public string Version { get; set; }
            
            [JsonPropertyName("type")]
            public string Type { get; set; }
            
            [JsonPropertyName("serverVersion")]
            public string ServerVersion { get; set; }
            
            [JsonPropertyName("openSubsonic")]
            public bool OpenSubsonic { get; set; }
            
            [JsonPropertyName("albumList2")]
            public AlbumList2 AlbumList2 { get; set; }
            
            [JsonPropertyName("album")]
            public SubsonicAlbum Album { get; set; }

            [JsonPropertyName("song")]
            public SubsonicSong Song { get; set; }
        }

        private class AlbumList2
        {
            [JsonPropertyName("album")]
            public List<SubsonicAlbum> Album { get; set; }
        }

        private class SubsonicAlbum
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
            
            [JsonPropertyName("name")]
            public string Name { get; set; }
            
            [JsonPropertyName("artist")]
            public string Artist { get; set; }
            
            [JsonPropertyName("song")]
            public List<SubsonicSong> Song { get; set; }
        }

        private class SubsonicSong
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }
            
            [JsonPropertyName("title")]
            public string Title { get; set; }
            
            [JsonPropertyName("artist")]
            public string Artist { get; set; }
            
            [JsonPropertyName("album")]
            public string Album { get; set; }

            [JsonPropertyName("duration")]
            public int Duration { get; set; }
        }
    }
} 