using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using System.IO;
using System.Xml.Linq;
using System.Windows;
using Universa.Desktop.Properties;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Diagnostics;

namespace Universa.Desktop
{
    public class SubsonicClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private const string API_VERSION = "1.16.1";
        private const string CLIENT_NAME = "Universa";
        private static readonly XNamespace NS = "http://subsonic.org/restapi";
        private readonly string _cacheDirectory;
        private readonly string _salt;
        private readonly string _token;

        public SubsonicClient(string baseUrl, string username, string password)
        {
            if (string.IsNullOrEmpty(baseUrl))
                throw new ArgumentException("Subsonic server URL is not configured.", nameof(baseUrl));
            if (string.IsNullOrEmpty(username))
                throw new ArgumentException("Subsonic username is not configured.", nameof(username));
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Subsonic password is not configured.", nameof(password));

            _baseUrl = baseUrl.TrimEnd('/');
            _username = username;
            _password = password;
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Universa",
                "Cache"
            );

            Directory.CreateDirectory(_cacheDirectory);

            _salt = GenerateSalt();
            _token = CreateToken(_password, _salt);
            _httpClient = new HttpClient();
        }

        private async Task<XDocument> MakeRequest(string action, string additionalParams = "")
        {
            try
            {
                var salt = GenerateRandomSalt();
                var token = CreatePasswordToken(_password, salt);
                var url = BuildUrl(action, salt, token) + additionalParams;

                Debug.WriteLine($"Making Subsonic API request: {action}");
                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"Received response for {action}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"HTTP error {response.StatusCode}: {content}");
                    throw new Exception($"HTTP error {response.StatusCode}: {response.ReasonPhrase}");
                }

                try
                {
                    var xml = XDocument.Parse(content);
                    var root = xml.Root;
                    
                    if (root == null)
                    {
                        Debug.WriteLine("Invalid XML response: No root element");
                        throw new Exception("Invalid server response: No root element");
                    }

                    var status = root.Attribute("status")?.Value;
                    if (status != "ok")
                    {
                        var error = root.Element(NS + "error");
                        var errorCode = error?.Attribute("code")?.Value;
                        var errorMessage = error?.Attribute("message")?.Value;
                        
                        Debug.WriteLine($"Subsonic API error: Code={errorCode}, Message={errorMessage}");
                        throw new Exception($"API Error {errorCode}: {errorMessage}");
                    }

                    Debug.WriteLine($"Successfully parsed XML response for {action}");
                    return xml;
                }
                catch (Exception ex) when (!(ex is Exception))  // Don't catch our own exceptions
                {
                    Debug.WriteLine($"Error parsing XML response: {ex.Message}");
                    Debug.WriteLine($"Response content: {content}");
                    throw new Exception("Invalid server response format", ex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MakeRequest for {action}: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<bool> TestConnection()
        {
            try
            {
                var xml = await MakeRequest("ping");
                return xml.Root?.Attribute("status")?.Value == "ok";
            }
            catch
            {
                throw new Exception("Failed to connect to Subsonic server. Please check your settings and try again.");
            }
        }

        public async Task<List<MusicItem>> GetArtists()
        {
            var xml = await MakeRequest("getArtists");
            var artists = new List<MusicItem>();
            var artistsElement = xml.Root?.Element(NS + "artists");
            
            if (artistsElement != null)
            {
                var indexes = artistsElement.Elements(NS + "index");
                foreach (var index in indexes)
                {
                    var artistElements = index.Elements(NS + "artist");
                    foreach (var artist in artistElements)
                    {
                        var musicItem = new MusicItem
                        {
                            Id = artist.Attribute("id")?.Value ?? "",
                            Name = artist.Attribute("name")?.Value ?? "",
                            Type = MusicItemType.Artist
                        };
                        musicItem.InitializeIconData();
                        artists.Add(musicItem);
                    }
                }
            }

            return artists;
        }

        public async Task<List<MusicItem>> GetAlbums(string artistId)
        {
            var xml = await MakeRequest("getArtist", $"&id={Uri.EscapeDataString(artistId)}");
            var albums = new List<MusicItem>();
            var artistElement = xml.Root?.Element(NS + "artist");
            var artistName = artistElement?.Attribute("name")?.Value ?? "";
            var albumElements = artistElement?.Elements(NS + "album");

            if (albumElements != null)
            {
                foreach (var album in albumElements)
                {
                    var musicItem = new MusicItem
                    {
                        Id = album.Attribute("id")?.Value ?? "",
                        Name = album.Attribute("name")?.Value ?? "",
                        Type = MusicItemType.Album
                    };
                    musicItem.InitializeIconData();
                    albums.Add(musicItem);
                }
            }

            return albums;
        }

        public async Task<List<MusicItem>> GetTracks(string albumId)
        {
            var xml = await MakeRequest("getAlbum", $"&id={Uri.EscapeDataString(albumId)}");
            var tracks = new List<MusicItem>();
            var albumElement = xml.Root?.Element(NS + "album");
            var albumArtist = albumElement?.Attribute("artist")?.Value ?? "";
            var albumName = albumElement?.Attribute("name")?.Value ?? "";
            var songElements = albumElement?.Elements(NS + "song");

            if (songElements != null)
            {
                foreach (var song in songElements)
                {
                    var songId = song.Attribute("id")?.Value;
                    int.TryParse(song.Attribute("track")?.Value, out int trackNumber);
                    int.TryParse(song.Attribute("duration")?.Value, out int durationSeconds);

                    var musicItem = new MusicItem
                    {
                        Id = songId,
                        Name = song.Attribute("title")?.Value ?? song.Attribute("name")?.Value ?? "",
                        Type = MusicItemType.Track,
                        Artist = song.Attribute("artist")?.Value ?? albumArtist,
                        AlbumArtist = albumArtist,
                        Album = song.Attribute("album")?.Value ?? albumName,
                        Genre = song.Attribute("genre")?.Value ?? "",
                        TrackNumber = trackNumber,
                        Duration = TimeSpan.FromSeconds(durationSeconds),
                        StreamUrl = GetStreamUrl(songId)
                    };
                    musicItem.InitializeIconData();
                    tracks.Add(musicItem);
                }
            }

            return tracks;
        }

        public async Task<List<MusicItem>> GetAllAlbums()
        {
            return await GetAlbumList("alphabeticalByName");
        }

        public async Task<List<MusicItem>> GetRecentAlbums(int limit = 50)
        {
            return await GetAlbumList("newest", limit);
        }

        private async Task<List<MusicItem>> GetAlbumList(string type, int limit = 500)
        {
            var albums = new List<MusicItem>();
            int offset = 0;
            bool hasMore = true;

            while (hasMore && (limit == 0 || albums.Count < limit))
            {
                var size = Math.Min(500, limit - albums.Count);
                var xml = await MakeRequest("getAlbumList2", $"&type={type}&size={size}&offset={offset}");
                var albumList = xml.Root?.Element(NS + "albumList2");
                
                if (albumList != null)
                {
                    var albumElements = albumList.Elements(NS + "album").ToList();
                    if (albumElements.Count == 0)
                    {
                        hasMore = false;
                    }
                    else
                    {
                        foreach (var album in albumElements)
                        {
                            var added = album.Attribute("created")?.Value;
                            DateTime.TryParse(added, out DateTime addedDate);
                            var artistName = album.Attribute("artist")?.Value ?? "";
                            var albumArtist = album.Attribute("albumArtist")?.Value ?? artistName;
                            
                            albums.Add(new MusicItem
                            {
                                Id = album.Attribute("id")?.Value ?? "",
                                Name = album.Attribute("name")?.Value ?? "",
                                Artist = artistName,
                                AlbumArtist = albumArtist,
                                Type = MusicItemType.Album,
                                IconData = MusicItem.GetIconForType(MusicItemType.Album),
                                DateAdded = addedDate
                            });
                        }
                        offset += albumElements.Count;
                    }
                }
                else
                {
                    hasMore = false;
                }

                if (limit > 0 && albums.Count >= limit)
                {
                    break;
                }
            }

            return albums;
        }

        public async Task<List<MusicItem>> GetPlaylists()
        {
            try
            {
                Debug.WriteLine("Getting playlists from Subsonic server...");
                var xml = await MakeRequest("getPlaylists");
                var playlists = new List<MusicItem>();
                var playlistsElement = xml.Root?.Element(NS + "playlists");

                if (playlistsElement == null)
                {
                    Debug.WriteLine("No playlists element found in response");
                    return playlists;
                }

                var playlistElements = playlistsElement.Elements(NS + "playlist");
                if (!playlistElements.Any())
                {
                    Debug.WriteLine("No playlist elements found in response");
                    return playlists;
                }

                foreach (var playlist in playlistElements)
                {
                    var id = playlist.Attribute("id")?.Value;
                    var name = playlist.Attribute("name")?.Value;
                    
                    Debug.WriteLine($"Found playlist: ID={id}, Name={name}");
                    
                    if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
                    {
                        Debug.WriteLine("Skipping playlist with missing ID or name");
                        continue;
                    }

                    var musicItem = new MusicItem
                    {
                        Id = id,
                        Name = name,
                        Type = MusicItemType.Playlist
                    };
                    musicItem.InitializeIconData();
                    playlists.Add(musicItem);
                }

                Debug.WriteLine($"Successfully loaded {playlists.Count} playlists");
                return playlists;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetPlaylists: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<List<MusicItem>> GetPlaylistTracks(string playlistId)
        {
            try
            {
                Debug.WriteLine($"Getting tracks for playlist ID: {playlistId}");
                var xml = await MakeRequest("getPlaylist", $"&id={Uri.EscapeDataString(playlistId)}");
                var tracks = new List<MusicItem>();
                
                var playlistElement = xml.Root?.Element(NS + "playlist");
                if (playlistElement == null)
                {
                    Debug.WriteLine("No playlist element found in response");
                    return tracks;
                }

                var songElements = playlistElement.Elements(NS + "entry");
                if (!songElements.Any())
                {
                    Debug.WriteLine("No track entries found in playlist");
                    return tracks;
                }

                foreach (var song in songElements)
                {
                    try
                    {
                        var songId = song.Attribute("id")?.Value;
                        var title = song.Attribute("title")?.Value ?? song.Attribute("name")?.Value;
                        var trackNumberStr = song.Attribute("track")?.Value;
                        var durationStr = song.Attribute("duration")?.Value;
                        var trackNumber = 0;
                        var durationSeconds = 0;

                        if (!string.IsNullOrEmpty(trackNumberStr))
                        {
                            int.TryParse(trackNumberStr, out trackNumber);
                        }

                        if (!string.IsNullOrEmpty(durationStr))
                        {
                            int.TryParse(durationStr, out durationSeconds);
                        }

                        var musicItem = new MusicItem
                        {
                            Id = songId,
                            Name = title ?? "Unknown Track",
                            Type = MusicItemType.Track,
                            Artist = song.Attribute("artist")?.Value ?? "Unknown Artist",
                            AlbumArtist = song.Attribute("albumArtist")?.Value ?? song.Attribute("artist")?.Value ?? "Unknown Artist",
                            Album = song.Attribute("album")?.Value ?? "Unknown Album",
                            Genre = song.Attribute("genre")?.Value ?? "",
                            TrackNumber = trackNumber,
                            Duration = TimeSpan.FromSeconds(durationSeconds),
                            StreamUrl = GetStreamUrl(songId)
                        };
                        musicItem.InitializeIconData();
                        tracks.Add(musicItem);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing individual track: {ex.Message}");
                        // Continue with next track
                        continue;
                    }
                }

                Debug.WriteLine($"Successfully loaded {tracks.Count} tracks from playlist");
                return tracks;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetPlaylistTracks: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task DeletePlaylistAsync(string playlistId)
        {
            await MakeRequest("deletePlaylist", $"&id={Uri.EscapeDataString(playlistId)}");
        }

        public async Task AddToPlaylistAsync(string playlistId, string songId)
        {
            if (string.IsNullOrWhiteSpace(playlistId))
                throw new ArgumentException("Playlist ID cannot be empty.", nameof(playlistId));
                
            if (string.IsNullOrWhiteSpace(songId))
                throw new ArgumentException("Song ID cannot be empty.", nameof(songId));
                
            await MakeRequest("updatePlaylist", $"&playlistId={Uri.EscapeDataString(playlistId)}&songIdToAdd={Uri.EscapeDataString(songId)}");
        }

        public async Task RemoveTrackFromPlaylistAsync(string playlistId, int index)
        {
            if (string.IsNullOrWhiteSpace(playlistId))
                throw new ArgumentException("Playlist ID cannot be empty.", nameof(playlistId));
                
            if (index < 0)
                throw new ArgumentException("Index cannot be negative.", nameof(index));
                
            await MakeRequest("updatePlaylist", $"&playlistId={Uri.EscapeDataString(playlistId)}&songIndexToRemove={index}");
        }

        public string GetStreamUrl(string trackId)
        {
            var salt = GenerateRandomSalt();
            var token = CreatePasswordToken(_password, salt);
            var baseUrlTrimmed = _baseUrl.TrimEnd('/');
            var url = $"{baseUrlTrimmed}/rest/stream.view" +
                   $"?u={Uri.EscapeDataString(_username)}" +
                   $"&t={token}" +
                   $"&s={salt}" +
                   $"&v={API_VERSION}" +
                   $"&c={CLIENT_NAME}" +
                   $"&id={Uri.EscapeDataString(trackId)}";
            
            Console.WriteLine($"Generated stream URL: {url}");
            return url;
        }

        private string BuildUrl(string action, string salt, string token)
        {
            return $"{_baseUrl}/rest/{action}?u={Uri.EscapeDataString(_username)}&t={token}&s={salt}&v={API_VERSION}&c={CLIENT_NAME}&f=xml";
        }

        private string GenerateSalt()
        {
            var random = new Random();
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string CreateToken(string password, string salt)
        {
            using (var md5 = MD5.Create())
            {
                var input = Encoding.UTF8.GetBytes(password + salt);
                var hash = md5.ComputeHash(input);
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
        }

        private string GenerateRandomSalt()
        {
            return GenerateSalt(); // Reuse existing method for consistency
        }

        private string CreatePasswordToken(string password, string salt)
        {
            return CreateToken(password, salt); // Reuse existing method for consistency
        }

        private string GetCachePath(string type)
        {
            // Deprecated - using MusicDataCache instead
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                $"subsonic_{type}_cache.json"
            );
        }

        public async Task LoadCachedData()
        {
            try
            {
                // Use MusicDataCache instead of local caching
                var musicCache = new MusicDataCache();
                var cachedData = await musicCache.LoadMusicData();
                
                if (cachedData != null && cachedData.Any())
                {
                    Debug.WriteLine($"Loaded {cachedData.Count} items from MusicDataCache");
                }
                else
                {
                    Debug.WriteLine("No cached data found in MusicDataCache");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading cached data: {ex.Message}");
                MessageBox.Show($"Error loading cached data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<MusicItem> GetTrack(string trackId)
        {
            var xml = await MakeRequest("getSong", $"&id={Uri.EscapeDataString(trackId)}");
            var songElement = xml.Root?.Element(NS + "song");

            if (songElement != null)
            {
                int.TryParse(songElement.Attribute("track")?.Value, out int trackNumber);
                int.TryParse(songElement.Attribute("duration")?.Value, out int durationSeconds);

                var musicItem = new MusicItem
                {
                    Id = songElement.Attribute("id")?.Value ?? "",
                    Name = songElement.Attribute("title")?.Value ?? songElement.Attribute("name")?.Value ?? "",
                    Type = MusicItemType.Track,
                    Artist = songElement.Attribute("artist")?.Value ?? "",
                    Album = songElement.Attribute("album")?.Value ?? "",
                    Genre = songElement.Attribute("genre")?.Value ?? "",
                    TrackNumber = trackNumber,
                    Duration = TimeSpan.FromSeconds(durationSeconds),
                    StreamUrl = GetStreamUrl(trackId)
                };
                musicItem.InitializeIconData();
                return musicItem;
            }

            return null;
        }

        public async Task CreatePlaylistAsync(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Playlist name cannot be empty.", nameof(name));

            await MakeRequest("createPlaylist", $"&name={Uri.EscapeDataString(name)}");
        }

        public async Task<List<MusicItem>> GetAllTracks()
        {
            var allTracks = new List<MusicItem>();
            var artists = await GetArtists();
            
            foreach (var artist in artists)
            {
                var albums = await GetAlbums(artist.Id);
                foreach (var album in albums)
                {
                    // Get detailed album info to ensure we have album artist
                    var xml = await MakeRequest("getAlbum", $"&id={Uri.EscapeDataString(album.Id)}");
                    var albumElement = xml.Root?.Element(NS + "album");
                    var albumArtist = albumElement?.Attribute("albumArtist")?.Value ?? 
                                    albumElement?.Attribute("artist")?.Value ?? 
                                    "Unknown Artist";

                    var tracks = await GetTracks(album.Id);
                    foreach (var track in tracks)
                    {
                        track.AlbumArtist = albumArtist;  // Ensure album artist is set
                    }
                    allTracks.AddRange(tracks);
                }
            }
            
            return allTracks;
        }
    }
} 