using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class MusicDataService : IMusicDataService
    {
        private readonly ISubsonicService _subsonicService;
        private readonly IConfigurationService _configService;
        private readonly string _cacheDirectory;
        
        private List<Artist> _artists = new List<Artist>();
        private List<Album> _albums = new List<Album>();
        private List<Playlist> _playlists = new List<Playlist>();
        
        private Dictionary<string, List<Track>> _albumTracks = new Dictionary<string, List<Track>>();
        private Dictionary<string, List<Track>> _playlistTracks = new Dictionary<string, List<Track>>();
        
        public event EventHandler DataChanged;
        
        public MusicDataService(ISubsonicService subsonicService, IConfigurationService configService)
        {
            _subsonicService = subsonicService ?? throw new ArgumentNullException(nameof(subsonicService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            
            _cacheDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Universa",
                "Cache"
            );
            
            Directory.CreateDirectory(_cacheDirectory);
        }
        
        public async Task<List<Artist>> GetArtistsAsync()
        {
            if (_artists.Count == 0)
            {
                await RefreshDataAsync();
            }
            
            return _artists;
        }
        
        public async Task<List<Album>> GetAlbumsAsync()
        {
            if (_albums.Count == 0)
            {
                await RefreshDataAsync();
            }
            
            return _albums;
        }
        
        public async Task<List<Playlist>> GetPlaylistsAsync()
        {
            if (_playlists.Count == 0)
            {
                await RefreshDataAsync();
            }
            
            return _playlists;
        }
        
        public async Task<List<Track>> GetTracksForAlbumAsync(string albumId)
        {
            if (string.IsNullOrEmpty(albumId))
            {
                Debug.WriteLine("Album ID is null or empty, cannot get tracks");
                return new List<Track>();
            }
            
            // Check if we already have the tracks in cache
            if (_albumTracks.TryGetValue(albumId, out var cachedTracks))
            {
                Debug.WriteLine($"Returning {cachedTracks.Count} cached tracks for album ID: {albumId}");
                return cachedTracks;
            }
            
            // Get tracks from the Subsonic service
            try
            {
                var musicItems = await _subsonicService.GetTracks(albumId);
                var tracks = ConvertMusicItemsToTracks(musicItems);
                
                // Cache the tracks
                _albumTracks[albumId] = tracks;
                
                Debug.WriteLine($"Retrieved {tracks.Count} tracks for album ID: {albumId}");
                return tracks;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting tracks for album ID {albumId}: {ex.Message}");
                return new List<Track>();
            }
        }
        
        public async Task<List<Track>> GetTracksForPlaylistAsync(string playlistId)
        {
            if (string.IsNullOrEmpty(playlistId))
            {
                Debug.WriteLine("Playlist ID is null or empty, cannot get tracks");
                return new List<Track>();
            }
            
            // Check if we already have the tracks in cache
            if (_playlistTracks.TryGetValue(playlistId, out var cachedTracks))
            {
                Debug.WriteLine($"Returning {cachedTracks.Count} cached tracks for playlist ID: {playlistId}");
                return cachedTracks;
            }
            
            // Get tracks from the Subsonic service
            try
            {
                var musicItems = await _subsonicService.GetPlaylistTracks(playlistId);
                var tracks = ConvertMusicItemsToTracks(musicItems);
                
                // Cache the tracks
                _playlistTracks[playlistId] = tracks;
                
                Debug.WriteLine($"Retrieved {tracks.Count} tracks for playlist ID: {playlistId}");
                return tracks;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting tracks for playlist ID {playlistId}: {ex.Message}");
                return new List<Track>();
            }
        }
        
        public async Task<Album> FindAlbumByNameAsync(string albumName, string artistName)
        {
            if (string.IsNullOrEmpty(albumName))
            {
                Debug.WriteLine("Album name is null or empty, cannot find album");
                return null;
            }
            
            // Try to find an exact match first
            var album = _albums.FirstOrDefault(a => 
                a.Title == albumName && 
                (string.IsNullOrEmpty(artistName) || a.Artist == artistName || a.ArtistName == artistName));
            
            if (album != null)
            {
                Debug.WriteLine($"Found album by exact name match: {album.Title} by {album.Artist}");
                return album;
            }
            
            // Try a more flexible search
            album = _albums.FirstOrDefault(a => 
                a.Title.Contains(albumName, StringComparison.OrdinalIgnoreCase) || 
                albumName.Contains(a.Title, StringComparison.OrdinalIgnoreCase));
            
            if (album != null)
            {
                Debug.WriteLine($"Found album by flexible name match: {album.Title} by {album.Artist}");
                return album;
            }
            
            Debug.WriteLine($"Could not find album with name: {albumName}");
            return null;
        }
        
        public async Task<Album> FindAlbumByIdAsync(string albumId)
        {
            if (string.IsNullOrEmpty(albumId))
            {
                Debug.WriteLine("Album ID is null or empty, cannot find album");
                return null;
            }
            
            var album = _albums.FirstOrDefault(a => a.Id == albumId);
            
            if (album != null)
            {
                Debug.WriteLine($"Found album by ID: {album.Title} by {album.Artist}");
                return album;
            }
            
            Debug.WriteLine($"Could not find album with ID: {albumId}");
            return null;
        }
        
        public async Task SaveToCacheAsync()
        {
            try
            {
                var cacheData = new MusicDataCacheModel
                {
                    Artists = _artists,
                    Albums = _albums,
                    Playlists = _playlists,
                    LastUpdated = DateTime.UtcNow
                };
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                var json = JsonSerializer.Serialize(cacheData, options);
                var cacheFilePath = Path.Combine(_cacheDirectory, "music_data.json");
                
                await File.WriteAllTextAsync(cacheFilePath, json);
                Debug.WriteLine($"Saved music data to cache: {cacheFilePath}");
                
                // We don't need to trigger DataChanged here since we're just saving to cache
                // The data itself hasn't changed
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving music data to cache: {ex.Message}");
            }
        }
        
        public async Task<bool> LoadFromCacheAsync()
        {
            try
            {
                var cacheFilePath = Path.Combine(_cacheDirectory, "music_data.json");
                
                if (!File.Exists(cacheFilePath))
                {
                    Debug.WriteLine("Cache file does not exist, cannot load from cache");
                    return false;
                }
                
                var json = await File.ReadAllTextAsync(cacheFilePath);
                var cacheData = JsonSerializer.Deserialize<MusicDataCacheModel>(json);
                
                if (cacheData == null)
                {
                    Debug.WriteLine("Failed to deserialize cache data");
                    return false;
                }
                
                // Check if the cache is too old (more than 24 hours)
                var cacheAge = DateTime.UtcNow - cacheData.LastUpdated;
                if (cacheAge.TotalHours > 24)
                {
                    Debug.WriteLine($"Cache is too old ({cacheAge.TotalHours:F1} hours), not using it");
                    return false;
                }
                
                // Store previous counts to determine if data actually changed
                int previousArtistCount = _artists.Count;
                int previousAlbumCount = _albums.Count;
                int previousPlaylistCount = _playlists.Count;
                
                // Update our data from the cache
                _artists = cacheData.Artists ?? new List<Artist>();
                _albums = cacheData.Albums ?? new List<Album>();
                _playlists = cacheData.Playlists ?? new List<Playlist>();
                
                Debug.WriteLine($"Loaded from cache: {_artists.Count} artists, {_albums.Count} albums, {_playlists.Count} playlists");
                
                // Only notify that data has changed if the counts actually changed
                bool dataChanged = _artists.Count != previousArtistCount || 
                                  _albums.Count != previousAlbumCount || 
                                  _playlists.Count != previousPlaylistCount;
                
                if (dataChanged)
                {
                    Debug.WriteLine("Data has changed from cache load, notifying listeners");
                    OnDataChanged();
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading from cache: {ex.Message}");
                return false;
            }
        }
        
        public async Task ClearCacheAsync()
        {
            try
            {
                var cacheFilePath = Path.Combine(_cacheDirectory, "music_data.json");
                
                if (File.Exists(cacheFilePath))
                {
                    File.Delete(cacheFilePath);
                    Debug.WriteLine($"Deleted cache file: {cacheFilePath}");
                }
                
                _artists.Clear();
                _albums.Clear();
                _playlists.Clear();
                _albumTracks.Clear();
                _playlistTracks.Clear();
                
                Debug.WriteLine("Cleared music data cache");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing music data cache: {ex.Message}");
            }
        }
        
        private async Task RefreshDataAsync()
        {
            try
            {
                Debug.WriteLine("Refreshing music data (with diffing logic)...");
                
                // Store current data for comparison (create copies to avoid modifying during iteration issues if needed)
                var currentArtists = _artists.ToDictionary(a => a.Id);
                var currentAlbums = _albums.ToDictionary(a => a.Id);
                var currentPlaylists = _playlists.ToDictionary(p => p.Id);

                // Fetch new data from Subsonic
                var newArtistItems = await _subsonicService.GetArtists();
                var newAlbumItems = await _subsonicService.GetAllAlbums();
                var newPlaylistItems = await _subsonicService.GetPlaylists();

                var newlyFetchedArtists = ConvertMusicItemsToArtists(newArtistItems);
                var newlyFetchedAlbums = ConvertMusicItemsToAlbums(newAlbumItems);
                var newlyFetchedPlaylists = ConvertMusicItemsToPlaylists(newPlaylistItems);

                bool hasChanges = false;

                // --- Diff and update Artists ---
                var updatedArtistList = new List<Artist>();
                foreach (var newArtist in newlyFetchedArtists)
                {
                    if (currentArtists.TryGetValue(newArtist.Id, out var existingArtist))
                    {
                        // Artist exists, check for updates
                        if (existingArtist.Name != newArtist.Name || existingArtist.ImageUrl != newArtist.ImageUrl)
                        {
                            existingArtist.Name = newArtist.Name;
                            existingArtist.ImageUrl = newArtist.ImageUrl;
                            hasChanges = true;
                        }
                        updatedArtistList.Add(existingArtist); // Add existing (possibly updated) to new list
                        currentArtists.Remove(newArtist.Id); // Remove from temp dict to track deletions
                    }
                    else
                    {
                        // New artist
                        updatedArtistList.Add(newArtist);
                        hasChanges = true;
                    }
                }
                // Any artists left in currentArtists were deleted from server
                if (currentArtists.Count > 0) hasChanges = true;
                _artists = updatedArtistList; // Replace with the new list

                // --- Diff and update Albums ---
                var updatedAlbumList = new List<Album>();
                foreach (var newAlbum in newlyFetchedAlbums)
                {
                    if (currentAlbums.TryGetValue(newAlbum.Id, out var existingAlbum))
                    {
                        // Album exists, check for updates
                        if (existingAlbum.Title != newAlbum.Title || 
                            existingAlbum.Artist != newAlbum.Artist || 
                            existingAlbum.ArtistId != newAlbum.ArtistId || 
                            existingAlbum.ArtistName != newAlbum.ArtistName || 
                            existingAlbum.ImageUrl != newAlbum.ImageUrl || 
                            existingAlbum.Year != newAlbum.Year)
                        {
                            existingAlbum.Title = newAlbum.Title;
                            existingAlbum.Artist = newAlbum.Artist;
                            existingAlbum.ArtistId = newAlbum.ArtistId;
                            existingAlbum.ArtistName = newAlbum.ArtistName;
                            existingAlbum.ImageUrl = newAlbum.ImageUrl;
                            existingAlbum.Year = newAlbum.Year;
                            hasChanges = true;
                        }
                        updatedAlbumList.Add(existingAlbum);
                        currentAlbums.Remove(newAlbum.Id);
                    }
                    else
                    {
                        // New album
                        updatedAlbumList.Add(newAlbum);
                        hasChanges = true;
                    }
                }
                if (currentAlbums.Count > 0) hasChanges = true;
                _albums = updatedAlbumList;

                // --- Diff and update Playlists ---
                var updatedPlaylistList = new List<Playlist>();
                foreach (var newPlaylist in newlyFetchedPlaylists)
                {
                    if (currentPlaylists.TryGetValue(newPlaylist.Id, out var existingPlaylist))
                    {
                        // Playlist exists, check for updates
                        if (existingPlaylist.Name != newPlaylist.Name || 
                            existingPlaylist.Description != newPlaylist.Description || 
                            existingPlaylist.ImageUrl != newPlaylist.ImageUrl)
                        {
                            existingPlaylist.Name = newPlaylist.Name;
                            existingPlaylist.Description = newPlaylist.Description;
                            existingPlaylist.ImageUrl = newPlaylist.ImageUrl;
                            hasChanges = true;
                        }
                        updatedPlaylistList.Add(existingPlaylist);
                        currentPlaylists.Remove(newPlaylist.Id);
                    }
                    else
                    {
                        // New playlist
                        updatedPlaylistList.Add(newPlaylist);
                        hasChanges = true;
                    }
                }
                if (currentPlaylists.Count > 0) hasChanges = true;
                _playlists = updatedPlaylistList;
                
                // Link albums to artists (essential after _artists and _albums lists might have changed)
                // First, clear all existing album lists from artists to rebuild them accurately
                foreach (var artist in _artists)
                {
                    artist.Albums.Clear(); 
                }
                // Then, re-populate based on the updated _albums list
                foreach (var album in _albums)
                {
                    var artist = _artists.FirstOrDefault(a => a.Id == album.ArtistId || a.Name == album.Artist || a.Name == album.ArtistName);
                    if (artist != null)
                    {
                        // Check to avoid adding duplicates if an album somehow got linked multiple times before clear
                        if (!artist.Albums.Any(a => a.Id == album.Id))
                        {
                           artist.Albums.Add(album);
                        }
                    }
                }
                
                // Save to cache
                await SaveToCacheAsync();
                
                if (hasChanges)
                {
                    Debug.WriteLine("Data has changed after diffing, notifying listeners");
                    OnDataChanged();
                }
                else
                {
                    Debug.WriteLine("Data refresh (diff) completed but no changes detected");
                }
                
                Debug.WriteLine($"Refreshed music data: {_artists.Count} artists, {_albums.Count} albums, {_playlists.Count} playlists");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing music data: {ex.Message}");
            }
        }
        
        private List<Artist> ConvertMusicItemsToArtists(List<MusicItem> items)
        {
            var artists = new List<Artist>();
            
            foreach (var item in items.Where(i => i.Type == MusicItemType.Artist))
            {
                var artist = new Artist
                {
                    Id = item.Id,
                    Name = item.Name,
                    ImageUrl = item.ImageUrl
                };
                
                artists.Add(artist);
            }
            
            return artists;
        }
        
        private List<Album> ConvertMusicItemsToAlbums(List<MusicItem> items)
        {
            var albums = new List<Album>();
            
            foreach (var item in items.Where(i => i.Type == MusicItemType.Album))
            {
                var album = new Album
                {
                    Id = item.Id,
                    Title = item.OriginalName ?? item.Name,
                    Artist = item.Artist ?? item.ArtistName,
                    ArtistId = item.ArtistId,
                    ArtistName = item.ArtistName,
                    ImageUrl = item.ImageUrl,
                    Year = item.Year
                };
                
                albums.Add(album);
            }
            
            return albums;
        }
        
        private List<Playlist> ConvertMusicItemsToPlaylists(List<MusicItem> items)
        {
            var playlists = new List<Playlist>();
            
            foreach (var item in items.Where(i => i.Type == MusicItemType.Playlist))
            {
                var playlist = new Playlist
                {
                    Id = item.Id,
                    Name = item.Name,
                    Description = item.Description,
                    ImageUrl = item.ImageUrl
                };
                
                playlists.Add(playlist);
            }
            
            return playlists;
        }
        
        private List<Track> ConvertMusicItemsToTracks(List<MusicItem> items)
        {
            var tracks = new List<Track>();
            
            foreach (var item in items.Where(i => i.Type == MusicItemType.Track))
            {
                var track = new Track
                {
                    Id = item.Id,
                    Title = item.Name,
                    Artist = item.ArtistName ?? item.Artist,
                    Album = item.Album,
                    TrackNumber = item.TrackNumber,
                    Duration = item.Duration,
                    StreamUrl = item.StreamUrl,
                    CoverArtUrl = item.ImageUrl
                };
                
                tracks.Add(track);
            }
            
            return tracks;
        }
        
        private void OnDataChanged()
        {
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public class MusicDataCacheModel
    {
        public List<Artist> Artists { get; set; }
        public List<Album> Albums { get; set; }
        public List<Playlist> Playlists { get; set; }
        public DateTime LastUpdated { get; set; }
    }
} 