using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.Views
{
    public class MusicTabViewModel : INotifyPropertyChanged
    {
        private readonly IMusicDataService _musicDataService;
        private readonly ISubsonicService _subsonicService;
        
        private ObservableCollection<MusicTreeItem> _artists = new ObservableCollection<MusicTreeItem>();
        private ObservableCollection<MusicTreeItem> _albums = new ObservableCollection<MusicTreeItem>();
        private ObservableCollection<MusicTreeItem> _playlists = new ObservableCollection<MusicTreeItem>();
        private ObservableCollection<MusicTreeItem> _rootItems = new ObservableCollection<MusicTreeItem>();
        
        private ObservableCollection<MusicContentItem> _contentItems = new ObservableCollection<MusicContentItem>();
        
        private MusicTreeItem _selectedItem;
        private bool _isLoading;
        private string _errorMessage;
        
        public ObservableCollection<MusicTreeItem> RootItems => _rootItems;
        public ObservableCollection<MusicContentItem> ContentItems => _contentItems;
        
        public MusicTreeItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (_selectedItem != value)
                {
                    _selectedItem = value;
                    OnPropertyChanged();
                    LoadContentForSelectedItemAsync().ConfigureAwait(false);
                }
            }
        }
        
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (_isLoading != value)
                {
                    _isLoading = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                if (_errorMessage != value)
                {
                    _errorMessage = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public MusicTabViewModel(IMusicDataService musicDataService, ISubsonicService subsonicService)
        {
            _musicDataService = musicDataService ?? throw new ArgumentNullException(nameof(musicDataService));
            _subsonicService = subsonicService ?? throw new ArgumentNullException(nameof(subsonicService));
            
            // Subscribe to data changes - but only refresh if we're not already loading
            _musicDataService.DataChanged += (sender, args) => 
            {
                if (!_isLoading)
                {
                    // Check if this is an initial load or a refresh triggered by SaveToCacheAsync
                    // We can determine this by checking if we already have data loaded
                    bool hasExistingData = _artists.Count > 0 || _albums.Count > 0 || _playlists.Count > 0;
                    
                    if (hasExistingData)
                    {
                        Debug.WriteLine("DataChanged event received, refreshing tree");
                        RefreshTreeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        Debug.WriteLine("DataChanged event received during initial load, skipping refresh");
                    }
                }
                else
                {
                    Debug.WriteLine("DataChanged event received, but skipping refresh because we're already loading");
                }
            };
            
            // Initialize the tree - this is now called explicitly from MusicTab.InitializeMusicLibraryAsync
            // InitializeTreeAsync().ConfigureAwait(false);
        }
        
        public async Task InitializeTreeAsync()
        {
            try
            {
                // If we're already loading, don't start another initialization
                // The IsLoading flag is managed by RefreshTreeAsync now.
                // if (IsLoading) 
                // {
                //     Debug.WriteLine("InitializeTreeAsync: Already loading, skipping initialization");
                //     return;
                // }
                
                Debug.WriteLine("InitializeTreeAsync: Starting initialization by calling RefreshTreeAsync");
                // IsLoading = true; // Handled by RefreshTreeAsync
                // ErrorMessage = null; // Handled by RefreshTreeAsync
                
                // // Clear existing collections to prevent duplicates // Handled by RefreshTreeAsync
                // _rootItems.Clear();
                // _artists.Clear();
                // _albums.Clear();
                // _playlists.Clear();
                
                // // Create root items for Artists, Albums, and Playlists // Handled by RefreshTreeAsync
                // var artistsRoot = new MusicTreeItem
                // {
                //     Name = "Artists",
                //     Type = MusicItemType.Category,
                //     Children = _artists
                // };
                
                // var albumsRoot = new MusicTreeItem
                // {
                //     Name = "Albums",
                //     Type = MusicItemType.Category,
                //     Children = _albums
                // };
                
                // var playlistsRoot = new MusicTreeItem
                // {
                //     Name = "Playlists",
                //     Type = MusicItemType.Category,
                //     Children = _playlists
                // };
                
                // _rootItems.Add(artistsRoot);
                // _rootItems.Add(albumsRoot);
                // _rootItems.Add(playlistsRoot);
                
                // Debug.WriteLine($"InitializeTreeAsync: Added root items - RootItems count: {_rootItems.Count}");
                
                // // Load data from cache first // Replaced by full refresh logic
                // var cacheLoaded = await _musicDataService.LoadFromCacheAsync();
                // Debug.WriteLine($"InitializeTreeAsync: Cache loaded: {cacheLoaded}");
                
                // if (!cacheLoaded)
                // {
                //     // If cache loading failed, refresh the data but don't call RefreshTreeAsync
                //     // to avoid circular reference
                //     Debug.WriteLine("InitializeTreeAsync: Cache loading failed, populating tree directly");
                //     await PopulateTreeFromDataServiceAsync();
                // }
                // else
                // {
                //     // If cache was loaded, still populate the tree
                //     Debug.WriteLine("InitializeTreeAsync: Cache loaded, populating tree");
                //     await PopulateTreeFromDataServiceAsync();
                // }

                // Call RefreshTreeAsync to ensure data is fresh from Subsonic and cache is updated.
                await RefreshTreeAsync();
                
                Debug.WriteLine($"InitializeTreeAsync: Initialization (via RefreshTreeAsync) complete - RootItems count: {_rootItems.Count}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing tree: {ex.Message}");
                ErrorMessage = $"Error initializing music library: {ex.Message}";
            }
            // finally // Loading state is managed by RefreshTreeAsync
            // {
            //     IsLoading = false;
            // }
        }
        
        public async Task RefreshTreeAsync()
        {
            try
            {
                // If we're already loading, don't start another refresh
                if (IsLoading)
                {
                    Debug.WriteLine("RefreshTreeAsync: Already loading, skipping refresh");
                    return;
                }
                
                Debug.WriteLine("RefreshTreeAsync: Starting refresh");
                IsLoading = true;
                ErrorMessage = null;
                
                // Clear the cache in the music data service
                await _musicDataService.ClearCacheAsync();
                
                // Clear existing collections
                _rootItems.Clear();
                _artists.Clear();
                _albums.Clear();
                _playlists.Clear();
                _contentItems.Clear();
                
                // Create root items for Artists, Albums, and Playlists
                var artistsRoot = new MusicTreeItem
                {
                    Name = "Artists",
                    Type = MusicItemType.Category,
                    Children = _artists
                };
                
                var albumsRoot = new MusicTreeItem
                {
                    Name = "Albums",
                    Type = MusicItemType.Category,
                    Children = _albums
                };
                
                var playlistsRoot = new MusicTreeItem
                {
                    Name = "Playlists",
                    Type = MusicItemType.Category,
                    Children = _playlists
                };
                
                _rootItems.Add(artistsRoot);
                _rootItems.Add(albumsRoot);
                _rootItems.Add(playlistsRoot);
                
                // Populate the tree with fresh data
                await PopulateTreeFromDataServiceAsync();
                
                Debug.WriteLine("RefreshTreeAsync: Refresh complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing tree: {ex.Message}");
                ErrorMessage = $"Error refreshing music library: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task PopulateTreeFromDataServiceAsync()
        {
            Debug.WriteLine("PopulateTreeFromDataServiceAsync: Starting to populate tree");
            
            // Get artists
            var artists = await _musicDataService.GetArtistsAsync();
            Debug.WriteLine($"PopulateTreeFromDataServiceAsync: Got {artists.Count} artists");
            
            var artistTreeItems = new List<MusicTreeItem>();
            foreach (var artist in artists.OrderBy(a => a.Name))
            {
                var artistItem = new MusicTreeItem
                {
                    Id = artist.Id,
                    Name = artist.Name,
                    Type = MusicItemType.Artist,
                    ImageUrl = artist.ImageUrl
                };
                
                // Add albums as children
                if (artist.Albums.Any())
                {
                    var albumChildItems = new List<MusicTreeItem>();
                    foreach (var album in artist.Albums) // Sort after creating with full name
                    {
                        var albumChildItem = new MusicTreeItem
                        {
                            Id = album.Id,
                            Name = $"{artist.Name} - {album.Title}", // Format: Artist - Album Title
                            Type = MusicItemType.Album,
                            ImageUrl = album.ImageUrl,
                            ParentId = artist.Id,
                            ParentName = artist.Name
                        };
                        albumChildItems.Add(albumChildItem);
                    }
                    // Sort children by the new composite name
                    foreach(var sortedAlbumChildItem in albumChildItems.OrderBy(a => a.Name))
                    {
                        artistItem.Children.Add(sortedAlbumChildItem);
                    }
                }
                
                artistTreeItems.Add(artistItem);
            }
            // Add sorted artists to the observable collection
            _artists.Clear(); // Clear before adding to avoid duplicates if method is re-entrant
            foreach(var item in artistTreeItems) _artists.Add(item);
            
            // Get albums for the main Albums category
            var allSystemAlbums = await _musicDataService.GetAlbumsAsync();
            Debug.WriteLine($"PopulateTreeFromDataServiceAsync: Got {allSystemAlbums.Count} albums for main list");
            
            var albumTreeItems = new List<MusicTreeItem>();
            foreach (var album in allSystemAlbums) // Sort after creating with full name
            {
                var albumItem = new MusicTreeItem
                {
                    Id = album.Id,
                    Name = $"{album.Artist} - {album.Title}", // Format: Album Artist - Album Title
                    Type = MusicItemType.Album,
                    ImageUrl = album.ImageUrl,
                    ParentId = album.ArtistId,
                    ParentName = album.Artist
                };
                albumTreeItems.Add(albumItem);
            }
            // Sort and add to the observable collection
            _albums.Clear(); // Clear before adding
            foreach(var item in albumTreeItems.OrderBy(a => a.Name)) _albums.Add(item);
            
            // Get playlists
            var playlists = await _musicDataService.GetPlaylistsAsync();
            Debug.WriteLine($"PopulateTreeFromDataServiceAsync: Got {playlists.Count} playlists");
            
            foreach (var playlist in playlists.OrderBy(p => p.Name))
            {
                var playlistItem = new MusicTreeItem
                {
                    Id = playlist.Id,
                    Name = playlist.Name,
                    Type = MusicItemType.Playlist,
                    ImageUrl = playlist.ImageUrl,
                    Description = playlist.Description
                };
                
                _playlists.Add(playlistItem);
            }
            
            Debug.WriteLine($"PopulateTreeFromDataServiceAsync: Tree populated with {_artists.Count} artists, {_albums.Count} albums, and {_playlists.Count} playlists");
            Debug.WriteLine($"PopulateTreeFromDataServiceAsync: RootItems count: {_rootItems.Count}");
        }
        
        private async Task LoadContentForSelectedItemAsync()
        {
            if (_selectedItem == null)
            {
                _contentItems.Clear();
                return;
            }
            
            try
            {
                IsLoading = true;
                ErrorMessage = null;
                _contentItems.Clear();
                
                switch (_selectedItem.Type)
                {
                    case MusicItemType.Artist:
                        await LoadArtistContentAsync(_selectedItem);
                        break;
                    case MusicItemType.Album:
                        await LoadAlbumContentAsync(_selectedItem);
                        break;
                    case MusicItemType.Playlist:
                        await LoadPlaylistContentAsync(_selectedItem);
                        break;
                    case MusicItemType.Category:
                        // For categories, show their children in the content area
                        if (_selectedItem.Name == "Artists")
                        {
                            foreach (var artist in _artists)
                            {
                                _contentItems.Add(new MusicContentItem
                                {
                                    Id = artist.Id,
                                    Name = artist.Name,
                                    Type = artist.Type,
                                    ImageUrl = artist.ImageUrl
                                });
                            }
                        }
                        else if (_selectedItem.Name == "Albums")
                        {
                            foreach (var album in _albums)
                            {
                                _contentItems.Add(new MusicContentItem
                                {
                                    Id = album.Id,
                                    Name = album.Name,
                                    Type = album.Type,
                                    ImageUrl = album.ImageUrl,
                                    ParentName = album.ParentName
                                });
                            }
                        }
                        else if (_selectedItem.Name == "Playlists")
                        {
                            foreach (var playlist in _playlists)
                            {
                                _contentItems.Add(new MusicContentItem
                                {
                                    Id = playlist.Id,
                                    Name = playlist.Name,
                                    Type = playlist.Type,
                                    ImageUrl = playlist.ImageUrl,
                                    Description = playlist.Description
                                });
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading content: {ex.Message}");
                ErrorMessage = $"Error loading content: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }
        
        private async Task LoadArtistContentAsync(MusicTreeItem artist)
        {
            // Show albums for this artist
            foreach (var album in artist.Children)
            {
                _contentItems.Add(new MusicContentItem
                {
                    Id = album.Id,
                    Name = album.Name,
                    Type = album.Type,
                    ImageUrl = album.ImageUrl,
                    ParentId = artist.Id,
                    ParentName = artist.Name
                });
            }
        }
        
        private async Task LoadAlbumContentAsync(MusicTreeItem album)
        {
            if (string.IsNullOrEmpty(album.Id))
            {
                Debug.WriteLine("Album ID is null or empty, cannot load tracks");
                ErrorMessage = "Cannot load album tracks: Invalid album ID";
                return;
            }
            
            try
            {
                var tracks = await _musicDataService.GetTracksForAlbumAsync(album.Id);
                
                foreach (var track in tracks.OrderBy(t => t.TrackNumber))
                {
                    _contentItems.Add(new MusicContentItem
                    {
                        Id = track.Id,
                        Name = track.Title,
                        Type = MusicItemType.Track,
                        ImageUrl = track.CoverArtUrl,
                        ParentId = album.Id,
                        ParentName = album.Name,
                        ArtistName = track.Artist,
                        TrackNumber = track.TrackNumber,
                        Duration = track.Duration,
                        StreamUrl = track.StreamUrl
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading album tracks: {ex.Message}");
                ErrorMessage = $"Error loading album tracks: {ex.Message}";
            }
        }
        
        private async Task LoadPlaylistContentAsync(MusicTreeItem playlist)
        {
            if (string.IsNullOrEmpty(playlist.Id))
            {
                Debug.WriteLine("Playlist ID is null or empty, cannot load tracks");
                ErrorMessage = "Cannot load playlist tracks: Invalid playlist ID";
                return;
            }
            
            try
            {
                var tracks = await _musicDataService.GetTracksForPlaylistAsync(playlist.Id);
                
                foreach (var track in tracks)
                {
                    _contentItems.Add(new MusicContentItem
                    {
                        Id = track.Id,
                        Name = track.Title,
                        Type = MusicItemType.Track,
                        ImageUrl = track.CoverArtUrl,
                        ParentId = playlist.Id,
                        ParentName = playlist.Name,
                        ArtistName = track.Artist,
                        Album = track.Album,
                        TrackNumber = track.TrackNumber,
                        Duration = track.Duration,
                        StreamUrl = track.StreamUrl
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading playlist tracks: {ex.Message}");
                ErrorMessage = $"Error loading playlist tracks: {ex.Message}";
            }
        }
        
        public async Task PlayItemAsync(MusicContentItem item)
        {
            if (item == null)
            {
                Debug.WriteLine("Cannot play null item");
                return;
            }
            
            try
            {
                if (item.Type == MusicItemType.Track)
                {
                    // Play a single track
                    var track = new Track
                    {
                        Id = item.Id,
                        Title = item.Name,
                        Artist = item.ArtistName,
                        Album = item.ParentName,
                        TrackNumber = item.TrackNumber,
                        Duration = item.Duration,
                        StreamUrl = item.StreamUrl,
                        CoverArtUrl = item.ImageUrl
                    };
                    
                    await PlayTracksAsync(new List<Track> { track });
                }
                else if (item.Type == MusicItemType.Album)
                {
                    // Play all tracks in the album
                    var tracks = await _musicDataService.GetTracksForAlbumAsync(item.Id);
                    if (tracks.Any())
                    {
                        await PlayTracksAsync(tracks);
                    }
                    else
                    {
                        MessageBox.Show($"No tracks found in album {item.Name}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else if (item.Type == MusicItemType.Playlist)
                {
                    // Play all tracks in the playlist
                    var tracks = await _musicDataService.GetTracksForPlaylistAsync(item.Id);
                    if (tracks.Any())
                    {
                        await PlayTracksAsync(tracks);
                    }
                    else
                    {
                        MessageBox.Show($"No tracks found in playlist {item.Name}", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing item: {ex.Message}");
                MessageBox.Show($"Error playing item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        public async Task<IEnumerable<Track>> GetAlbumTracksAsync(string albumId)
        {
            try
            {
                Debug.WriteLine($"Getting tracks for album: {albumId}");
                return await _musicDataService.GetTracksForAlbumAsync(albumId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting album tracks: {ex.Message}");
                return Enumerable.Empty<Track>();
            }
        }

        public async Task<IEnumerable<Track>> GetPlaylistTracksAsync(string playlistId)
        {
            try
            {
                Debug.WriteLine($"Getting tracks for playlist: {playlistId}");
                return await _musicDataService.GetTracksForPlaylistAsync(playlistId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting playlist tracks: {ex.Message}");
                return Enumerable.Empty<Track>();
            }
        }

        public async Task PlayTracksAsync(IEnumerable<Track> tracks)
        {
            if (tracks == null || !tracks.Any())
            {
                Debug.WriteLine("No tracks to play");
                return;
            }

            Debug.WriteLine($"Playing {tracks.Count()} tracks");
            
            // Convert to MusicItem for the MediaPlayerManager
            var musicItems = tracks.Select(t => new MusicItem
            {
                Id = t.Id,
                Name = t.Title,
                Artist = t.Artist,
                Album = t.Album,
                StreamUrl = t.StreamUrl,
                Type = MusicItemType.Track,
                ImageUrl = t.CoverArtUrl,
                Duration = t.Duration
            }).ToList();
            
            // Invoke the event to play the tracks
            PlayTracksRequested?.Invoke(this, new PlayTracksEventArgs(tracks));
        }
        
        public event EventHandler<PlayTracksEventArgs> PlayTracksRequested;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public async Task<bool> DeletePlaylistAsync(string playlistId)
        {
            try
            {
                Debug.WriteLine($"Deleting playlist with ID: {playlistId}");
                await _subsonicService.DeletePlaylistAsync(playlistId);
                
                // Since SubsonicService.DeletePlaylistAsync doesn't return a value,
                // we'll consider it successful if no exception was thrown
                Debug.WriteLine("Playlist deleted successfully");
                
                // Remove from local collection
                var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
                if (playlist != null)
                {
                    _playlists.Remove(playlist);
                }
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting playlist: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> AddTracksToPlaylistAsync(string playlistId, List<string> trackIds)
        {
            try
            {
                Debug.WriteLine($"Adding {trackIds.Count} tracks to playlist {playlistId}");
                
                // Call the Subsonic service to add the tracks
                bool success = await _subsonicService.AddTracksToPlaylistAsync(playlistId, trackIds);
                
                if (success)
                {
                    Debug.WriteLine("Tracks added to playlist successfully");
                }
                else
                {
                    Debug.WriteLine("Failed to add tracks to playlist");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding tracks to playlist: {ex.Message}");
                return false;
            }
        }
        
        public async Task<bool> RemoveTrackFromPlaylistAsync(string playlistId, MusicContentItem track, int trackIndex)
        {
            try
            {
                if (string.IsNullOrEmpty(playlistId))
                {
                    Debug.WriteLine("Playlist ID is null or empty, cannot remove track");
                    return false;
                }
                
                if (track == null)
                {
                    Debug.WriteLine("Track is null, cannot remove from playlist");
                    return false;
                }
                
                Debug.WriteLine($"Removing track {track.Name} from playlist {playlistId} at index {trackIndex}");
                
                // Call the Subsonic service to remove the track
                bool success = await _subsonicService.RemoveTrackFromPlaylistAsync(playlistId, track.Id, trackIndex);
                
                if (success)
                {
                    Debug.WriteLine("Track removed from playlist successfully");
                    
                    // Remove the track from the ContentItems collection if we're currently viewing the playlist
                    if (SelectedItem?.Type == MusicItemType.Playlist && SelectedItem.Id == playlistId)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ContentItems.Remove(track);
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("Failed to remove track from playlist");
                }
                
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error removing track from playlist: {ex.Message}");
                return false;
            }
        }
    }
    
    public class MusicTreeItem : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private MusicItemType _type;
        private string _imageUrl;
        private string _parentId;
        private string _parentName;
        private string _description;
        private ObservableCollection<MusicTreeItem> _children = new ObservableCollection<MusicTreeItem>();
        private bool _isSelected;
        private bool _isExpanded;
        
        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public MusicItemType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ParentId
        {
            get => _parentId;
            set
            {
                if (_parentId != value)
                {
                    _parentId = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ParentName
        {
            get => _parentName;
            set
            {
                if (_parentName != value)
                {
                    _parentName = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public ObservableCollection<MusicTreeItem> Children
        {
            get => _children;
            set
            {
                if (_children != value)
                {
                    _children = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool HasChildren => Children.Count > 0;
        
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                    // Optionally: Add logic here if expanding an item should auto-load children
                    // if (_isExpanded && Children.Count == 0 && Type != MusicItemType.Category) // Example condition
                    // {
                    //    // Call a method to load children, e.g., from MusicTabViewModel
                    // }
                }
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class MusicContentItem : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private MusicItemType _type;
        private string _imageUrl;
        private string _parentId;
        private string _parentName;
        private string _artistName;
        private string _album;
        private int _trackNumber;
        private TimeSpan _duration;
        private string _streamUrl;
        private string _description;
        private bool _isPlaying;
        
        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public MusicItemType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ImageUrl
        {
            get => _imageUrl;
            set
            {
                if (_imageUrl != value)
                {
                    _imageUrl = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ParentId
        {
            get => _parentId;
            set
            {
                if (_parentId != value)
                {
                    _parentId = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ParentName
        {
            get => _parentName;
            set
            {
                if (_parentName != value)
                {
                    _parentName = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string ArtistName
        {
            get => _artistName;
            set
            {
                if (_artistName != value)
                {
                    _artistName = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Album
        {
            get => _album;
            set
            {
                if (_album != value)
                {
                    _album = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public int TrackNumber
        {
            get => _trackNumber;
            set
            {
                if (_trackNumber != value)
                {
                    _trackNumber = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public TimeSpan Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string StreamUrl
        {
            get => _streamUrl;
            set
            {
                if (_streamUrl != value)
                {
                    _streamUrl = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
    
    public class PlayTracksEventArgs : EventArgs
    {
        public IEnumerable<Track> Tracks { get; }
        
        public PlayTracksEventArgs(IEnumerable<Track> tracks)
        {
            Tracks = tracks;
        }
    }
} 