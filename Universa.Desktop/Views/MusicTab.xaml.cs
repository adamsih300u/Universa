using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using Universa.Desktop.Services;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Logging;
using Universa.Desktop.Managers;
using Universa.Desktop.Windows;
using System.Text.Json;
using System.IO;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Windows.Markup;
using System.Text;
using System.Windows.Controls.Primitives;  // For Thumb
using System.Windows.Media.Animation;  // For animations
using System.Net.Http;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using System.Diagnostics;
using Universa.Desktop.Controls;
using Universa.Desktop.Dialogs;  // For PlaylistNameDialog
using Universa.Desktop.Core.Configuration;
using Track = Universa.Desktop.Models.Track;  // Explicitly resolve Track ambiguity
using System.Threading;
using System.Windows.Threading;
using System.Collections.Concurrent;

namespace Universa.Desktop.Views
{
    public partial class MusicTab : UserControl, INotifyPropertyChanged
    {
        private readonly BaseMainWindow _mainWindow;
        private readonly IConfigurationService _configService;
        private readonly MediaPlayerManager _mediaPlayerManager;
        private MusicTabViewModel _viewModel;
        private UserControl _navigator;
        private TrackInfo _currentTrack;
        private TimeSpan _currentPosition;
        private List<TrackInfo> _trackList;
        private bool _isDragging = false;
        private TreeViewItem _lastHighlightedItem;
        private Point _dragStartPoint;
        private readonly ObservableCollection<MusicItem> _artistsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _albumsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _playlistsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _rootItems = new ObservableCollection<MusicItem>();
        private ICollectionView _filteredView;
        private string _currentFilter = string.Empty;
        private ISubsonicService _subsonicClient;
        private MusicItem _currentlyViewedPlaylist;
        private readonly MusicDataCache _musicCache = new MusicDataCache();
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private List<MusicItem> _allMusicData;
        private LoadingState _currentLoadingState;
        private string _errorMessage;

        public enum LoadingState
        {
            Idle,
            LoadingLibrary,
            LoadingContent,
            Error
        }

        public MusicTabViewModel ViewModel => _viewModel;

        public TrackInfo CurrentTrack
        {
            get => _currentTrack;
            private set
            {
                _currentTrack = value;
                OnPropertyChanged(nameof(CurrentTrack));
            }
        }

        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            private set
            {
                _currentPosition = value;
                OnPropertyChanged(nameof(CurrentPosition));
            }
        }

        public List<TrackInfo> TrackList
        {
            get => _trackList;
            private set
            {
                _trackList = value;
                OnPropertyChanged(nameof(TrackList));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public UserControl Navigator
        {
            get => _navigator;
            private set
            {
                _navigator = value;
                OnPropertyChanged(nameof(Navigator));
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MusicTab()
        {
            InitializeComponent();
            
            try
            {
                _configService = ServiceLocator.Instance.GetRequiredService<IConfigurationService>();
                
                // Initialize the ViewModel
                var musicDataService = ServiceLocator.Instance.GetRequiredService<IMusicDataService>();
                var subsonicService = ServiceLocator.Instance.GetRequiredService<ISubsonicService>();
                _viewModel = new MusicTabViewModel(musicDataService, subsonicService);
                
                // Set the DataContext
                DataContext = _viewModel;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting configuration service: {ex.Message}");
                MessageBox.Show("Error initializing music tab. Please try again later.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Clear existing collections instead of reassigning
            _artistsList.Clear();
            _albumsList.Clear();
            _playlistsList.Clear();
            _rootItems.Clear();

            // Initialize collections
            // Application.Current.Dispatcher.Invoke(() =>
            // {
            //     // Set the ItemsSource for the NavigationTree
            //     NavigationTree.ItemsSource = _rootItems;
            // });

            // Clean up old cache files - this is now handled by the MusicDataService
            // CleanupOldCacheFiles();

            // Initialize services
            // _musicCache = new MusicDataCache(); - this is now handled by the MusicDataService

            // Initialize the filtered view - this is now handled by the ViewModel
            // InitializeCollectionView();
        }

        public MusicTab(BaseMainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;
            _mediaPlayerManager = ServiceLocator.Instance.GetRequiredService<MediaPlayerManager>();

            // Set up event handlers for ContentListView_Control
            ContentListView_Control.SelectionChanged += ContentListView_SelectionChanged;
            ContentListView_Control.MouseRightButtonDown += ContentListView_MouseRightButtonDown;
            ContentListView_Control.MouseDoubleClick += ContentListView_MouseDoubleClick;
            ContentListView_Control.PreviewMouseLeftButtonDown += ContentListView_PreviewMouseLeftButtonDown;
            ContentListView_Control.PreviewMouseMove += ContentListView_PreviewMouseMove;

            // Subscribe to the PlayTracksRequested event
            _viewModel.PlayTracksRequested += ViewModel_PlayTracksRequested;
            
            // Initialize the music library
            InitializeMusicLibraryAsync();
        }
        
        private async void InitializeMusicLibraryAsync()
        {
            try
            {
                await _viewModel.InitializeTreeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing music library: {ex.Message}");
                MessageBox.Show($"Error initializing music library: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ViewModel_PlayTracksRequested(object sender, PlayTracksEventArgs e)
        {
            try
            {
                Debug.WriteLine("ViewModel_PlayTracksRequested called");
                
                if (e.Tracks == null || !e.Tracks.Any())
                {
                    Debug.WriteLine("ViewModel_PlayTracksRequested: No tracks provided");
                    return;
                }
                
                // Convert Track objects to MusicItem objects for the MediaPlayerManager
                var musicItems = e.Tracks.Select(track => new MusicItem
                {
                    Id = track.Id,
                    Name = track.Title,
                    Artist = track.Artist,
                    Album = track.Album,
                    StreamUrl = track.StreamUrl,
                    Type = MusicItemType.Track,
                    ImageUrl = track.CoverArtUrl,
                    Duration = track.Duration
                }).ToList();
                
                Debug.WriteLine($"ViewModel_PlayTracksRequested: Playing {e.Tracks.Count()} tracks");
                Debug.WriteLine($"ViewModel_PlayTracksRequested: First track URL: {musicItems.First().StreamUrl}");
                
                if (_mediaPlayerManager == null)
                {
                    Debug.WriteLine("ViewModel_PlayTracksRequested: _mediaPlayerManager is null");
                    return;
                }
                
                Debug.WriteLine("ViewModel_PlayTracksRequested: Calling _mediaPlayerManager.PlayTracks");
                _mediaPlayerManager.PlayTracks(musicItems);
                Debug.WriteLine("ViewModel_PlayTracksRequested: _mediaPlayerManager.PlayTracks completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ViewModel_PlayTracksRequested: Error playing tracks: {ex.Message}");
                Debug.WriteLine($"ViewModel_PlayTracksRequested: Stack trace: {ex.StackTrace}");
            }
        }
        
        private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is MusicTreeItem selectedItem)
            {
                _viewModel.SelectedItem = selectedItem;
            }
        }
        
        private void ContentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ContentListView_Control.SelectedItem is MusicContentItem selectedItem)
            {
                _viewModel.PlayItemAsync(selectedItem);
            }
        }
        
        private void ContentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle selection changes if needed
        }
        
        private void ContentListView_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Handle right-click context menu
        }
        
        private void ContentListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Store the mouse position for potential drag operations
            _dragStartPoint = e.GetPosition(null);
        }
        
        private void ContentListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Handle drag operations if needed
        }
        
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshMusicLibraryAsync();
        }
        
        private async void RefreshMusicLibraryAsync()
        {
            try
            {
                await _viewModel.RefreshTreeAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing music library: {ex.Message}");
                MessageBox.Show($"Error refreshing music library: {ex.Message}", "Refresh Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Implement search functionality if needed
        }
        
        private void NavigationTree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Get the TreeView and find the item that was clicked
                var treeView = sender as TreeView;
                var point = e.GetPosition(treeView);
                var result = VisualTreeHelper.HitTest(treeView, point);
                
                if (result == null) return;
                
                // Find the TreeViewItem that was clicked
                DependencyObject obj = result.VisualHit;
                while (obj != null && !(obj is TreeViewItem))
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }
                
                if (obj == null) return;
                
                // Get the MusicTreeItem from the TreeViewItem
                var treeViewItem = obj as TreeViewItem;
                var item = treeViewItem.DataContext as MusicTreeItem;
                
                if (item == null) return;
                
                // Mark the event as handled to prevent the default context menu
                e.Handled = true;
                
                // Select the item
                treeViewItem.IsSelected = true;
                
                // Create context menu based on item type
                var contextMenu = new ContextMenu();
                
                switch (item.Type)
                {
                    case MusicItemType.Album:
                        // Add Play Album menu item
                        var playAlbumMenuItem = new MenuItem { Header = "Play Album" };
                        playAlbumMenuItem.Click += async (s, args) =>
                        {
                            var tracks = await _viewModel.GetAlbumTracksAsync(item.Id);
                            if (tracks.Any())
                            {
                                await _viewModel.PlayTracksAsync(tracks);
                            }
                        };
                        contextMenu.Items.Add(playAlbumMenuItem);
                        
                        // Add Shuffle Play Album menu item
                        var shuffleAlbumMenuItem = new MenuItem { Header = "Shuffle Play Album" };
                        shuffleAlbumMenuItem.Click += async (s, args) =>
                        {
                            var tracks = await _viewModel.GetAlbumTracksAsync(item.Id);
                            if (tracks.Any())
                            {
                                var shuffledTracks = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                                await _viewModel.PlayTracksAsync(shuffledTracks);
                            }
                        };
                        contextMenu.Items.Add(shuffleAlbumMenuItem);
                        break;
                        
                    case MusicItemType.Playlist:
                        // Add Play Playlist menu item
                        var playPlaylistMenuItem = new MenuItem { Header = "Play Playlist" };
                        playPlaylistMenuItem.Click += async (s, args) =>
                        {
                            var tracks = await _viewModel.GetPlaylistTracksAsync(item.Id);
                            if (tracks.Any())
                            {
                                await _viewModel.PlayTracksAsync(tracks);
                            }
                        };
                        contextMenu.Items.Add(playPlaylistMenuItem);
                        
                        // Add Shuffle Play Playlist menu item
                        var shufflePlaylistMenuItem = new MenuItem { Header = "Shuffle Play Playlist" };
                        shufflePlaylistMenuItem.Click += async (s, args) =>
                        {
                            var tracks = await _viewModel.GetPlaylistTracksAsync(item.Id);
                            if (tracks.Any())
                            {
                                var shuffledTracks = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                                await _viewModel.PlayTracksAsync(shuffledTracks);
                            }
                        };
                        contextMenu.Items.Add(shufflePlaylistMenuItem);
                        
                        // Add separator
                        contextMenu.Items.Add(new Separator());
                        
                        // Add Delete Playlist menu item
                        var deletePlaylistMenuItem = new MenuItem { Header = "Delete Playlist" };
                        deletePlaylistMenuItem.Click += async (s, args) =>
                        {
                            var result = MessageBox.Show(
                                $"Are you sure you want to delete the playlist '{item.Name}'?",
                                "Delete Playlist",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                                
                            if (result == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    await _viewModel.DeletePlaylistAsync(item.Id);
                                    // Refresh the tree to show the updated playlists
                                    await _viewModel.RefreshTreeAsync();
                                }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(
                                        $"Error deleting playlist: {ex.Message}",
                                        "Error",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                                }
                            }
                        };
                        contextMenu.Items.Add(deletePlaylistMenuItem);
                        break;
                }
                
                // Show the context menu if it has items
                if (contextMenu.Items.Count > 0)
                {
                    contextMenu.IsOpen = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigationTree_MouseRightButtonDown: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private async void NavigationTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var treeView = sender as TreeView;
                var item = treeView?.SelectedItem as MusicTreeItem;
                
                if (item == null) return;
                
                Debug.WriteLine($"NavigationTree_MouseDoubleClick: Selected item type: {item.Type}, Name: {item.Name}");
                
                switch (item.Type)
                {
                    case MusicItemType.Album:
                        Debug.WriteLine($"Loading tracks for album: {item.Name}");
                        var albumTracks = await _viewModel.GetAlbumTracksAsync(item.Id);
                        if (albumTracks.Any())
                        {
                            Debug.WriteLine($"Playing {albumTracks.Count()} tracks from album");
                            
                            // Convert to MusicItem for the MediaPlayerManager
                            var musicItems = albumTracks.Select(t => new MusicItem
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
                            
                            // Play the tracks using the MediaPlayerManager directly
                            if (_mediaPlayerManager != null)
                            {
                                Debug.WriteLine("Playing tracks using MediaPlayerManager.PlayTracks");
                                _mediaPlayerManager.PlayTracks(musicItems);
                            }
                            else
                            {
                                Debug.WriteLine("WARNING: _mediaPlayerManager is null, using ViewModel.PlayTracksAsync instead");
                                await _viewModel.PlayTracksAsync(albumTracks);
                            }
                        }
                        break;
                        
                    case MusicItemType.Playlist:
                        Debug.WriteLine($"Loading tracks for playlist: {item.Name}");
                        var playlistTracks = await _viewModel.GetPlaylistTracksAsync(item.Id);
                        if (playlistTracks.Any())
                        {
                            Debug.WriteLine($"Playing {playlistTracks.Count()} tracks from playlist");
                            
                            // Convert to MusicItem for the MediaPlayerManager
                            var musicItems = playlistTracks.Select(t => new MusicItem
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
                            
                            // Play the tracks using the MediaPlayerManager directly
                            if (_mediaPlayerManager != null)
                            {
                                Debug.WriteLine("Playing tracks using MediaPlayerManager.PlayTracks");
                                _mediaPlayerManager.PlayTracks(musicItems);
                            }
                            else
                            {
                                Debug.WriteLine("WARNING: _mediaPlayerManager is null, using ViewModel.PlayTracksAsync instead");
                                await _viewModel.PlayTracksAsync(playlistTracks);
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigationTree_MouseDoubleClick: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void NavigationTree_DragEnter(object sender, DragEventArgs e)
        {
            // Handle drag enter events
        }
        
        private void NavigationTree_DragLeave(object sender, DragEventArgs e)
        {
            // Handle drag leave events
        }
        
        private void NavigationTree_Drop(object sender, DragEventArgs e)
        {
            // Handle drop events
        }
        
        private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (ContentListView_Control.SelectedItem is MusicContentItem selectedItem)
            {
                _viewModel.PlayItemAsync(selectedItem);
            }
        }
        
        private void ShuffleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            // Implement shuffle functionality
        }

        // Placeholder methods to satisfy references
        private void CleanupOldCacheFiles()
        {
            // This functionality is now handled by the MusicDataService
            Debug.WriteLine("CleanupOldCacheFiles is now handled by the MusicDataService");
        }

        private void InitializeCollectionView()
        {
            // This functionality is now handled by the ViewModel
            Debug.WriteLine("InitializeCollectionView is now handled by the ViewModel");
        }
    }
} 