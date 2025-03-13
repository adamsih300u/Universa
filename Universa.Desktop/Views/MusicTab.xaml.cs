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
    public class UISettings
    {
        public Dictionary<string, double> ColumnWidths { get; set; } = new Dictionary<string, double>();
    }

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
        private bool _isRestoringTreeState = false;
        private DispatcherTimer _columnResizeTimer;
        private UISettings _uiSettings = new UISettings();

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
                
                // Add handler for column width changes
                ContentListView_Control.Loaded += (s, e) => RestoreColumnWidths();
                
                // Setup handlers for column width changes
                if (ContentListView_Control.View is GridView gridView)
                {
                    foreach (var column in gridView.Columns)
                    {
                        // Convert NaN width to actual width
                        if (double.IsNaN(column.Width))
                            column.Width = column.ActualWidth;
                        
                        // Add a Thumb drag completed handler to each column
                        var header = column.Header as GridViewColumnHeader;
                        if (header != null)
                        {
                            header.SizeChanged += GridViewColumnHeader_SizeChanged;
                        }
                    }
                }
                
                // Save settings when the tab is unloaded - ensure it's forcibly saved, not just scheduled
                this.Unloaded += (s, e) => 
                {
                    // Force an immediate save when tab is unloaded
                    Debug.WriteLine("MusicTab Unloaded");
                };

                // Initialize the column resize timer
                _columnResizeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _columnResizeTimer.Tick += ColumnResizeTimer_Tick;

                // Subscribe to events
                Loaded += MusicTab_Loaded;
                Unloaded += MusicTab_Unloaded;
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

            // Try to get the MediaPlayerManager from ServiceLocator
            try
            {
                _mediaPlayerManager = ServiceLocator.Instance.GetRequiredService<MediaPlayerManager>();
                
                // Subscribe to track changed event to highlight the currently playing track
                _mediaPlayerManager.TrackChanged += MediaPlayerManager_TrackChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting MediaPlayerManager from ServiceLocator: {ex.Message}");
            }
        }

        public MusicTab(BaseMainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;
            
            // First try to get the MediaPlayerManager from the main window through IMediaWindow interface
            if (_mainWindow is IMediaWindow mediaWindow && mediaWindow.MediaPlayerManager != null)
            {
                // Unsubscribe from previous event if it was set in the default constructor
                if (_mediaPlayerManager != null)
                {
                    _mediaPlayerManager.TrackChanged -= MediaPlayerManager_TrackChanged;
                }
                
                _mediaPlayerManager = mediaWindow.MediaPlayerManager;
                Debug.WriteLine("MusicTab: Using MediaPlayerManager from main window");
                
                // Subscribe to track changed event to highlight the currently playing track
                _mediaPlayerManager.TrackChanged += MediaPlayerManager_TrackChanged;
            }

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
                
                // Allow the UI thread to fully process and render the tree
                // before attempting to modify its expansion state
                await Task.Delay(500);
                
                // After the tree is populated, restore the expanded state
                // Use the dispatcher to ensure UI is fully updated
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                    Debug.WriteLine("Tree population completed");
                }));
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
            try
            {
                // Get the position of the mouse click
                var point = e.GetPosition(ContentListView_Control);

                // Perform a hit test to determine if an item was clicked
                HitTestResult result = VisualTreeHelper.HitTest(ContentListView_Control, point);
                if (result == null) return;

                // Find the ListViewItem that was clicked
                DependencyObject obj = result.VisualHit;
                while (obj != null && !(obj is ListViewItem))
                {
                    obj = VisualTreeHelper.GetParent(obj);
                }

                if (obj == null) return;

                // Get the MusicContentItem from the ListViewItem
                var listViewItem = obj as ListViewItem;
                var item = listViewItem.DataContext as MusicContentItem;

                if (item == null) return;

                // Make sure the clicked item is selected
                listViewItem.IsSelected = true;

                // Create context menu
                var contextMenu = new ContextMenu();

                // Add common menu items
                var playMenuItem = new MenuItem { Header = "Play" };
                playMenuItem.Click += PlayMenuItem_Click;
                contextMenu.Items.Add(playMenuItem);

                var shuffleMenuItem = new MenuItem { Header = "Shuffle" };
                shuffleMenuItem.Click += ShuffleMenuItem_Click;
                contextMenu.Items.Add(shuffleMenuItem);

                // Add "Remove from Playlist" option if we're viewing a playlist
                if (_viewModel.SelectedItem?.Type == MusicItemType.Playlist && item.Type == MusicItemType.Track)
                {
                    // Add separator
                    contextMenu.Items.Add(new Separator());
                    
                    // Add "Remove from Playlist" menu item
                    var removeFromPlaylistMenuItem = new MenuItem { Header = "Remove from Playlist" };
                    removeFromPlaylistMenuItem.Click += async (s, args) =>
                    {
                        try
                        {
                            var playlistId = _viewModel.SelectedItem.Id;
                            var playlistName = _viewModel.SelectedItem.Name;
                            
                            // First confirm with the user
                            var messageBoxResult = MessageBox.Show(
                                $"Are you sure you want to remove '{item.Name}' from playlist '{playlistName}'?",
                                "Remove from Playlist",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);
                                
                            if (messageBoxResult == MessageBoxResult.Yes)
                            {
                                // Get the index of the track in the playlist
                                int trackIndex = _viewModel.ContentItems.IndexOf(item);
                                if (trackIndex >= 0)
                                {
                                    // Call the ViewModel method to remove the track
                                    bool success = await _viewModel.RemoveTrackFromPlaylistAsync(playlistId, item, trackIndex);
                                    
                                    if (success)
                                    {
                                        Debug.WriteLine($"Successfully removed track from playlist: {item.Name}");
                                    }
                                    else
                                    {
                                        MessageBox.Show(
                                            $"Failed to remove track from playlist.",
                                            "Error",
                                            MessageBoxButton.OK,
                                            MessageBoxImage.Error);
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"Could not find track index in ContentItems collection");
                                    MessageBox.Show(
                                        $"Could not determine track position in playlist.",
                                        "Error",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Error);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error removing track from playlist: {ex.Message}");
                            MessageBox.Show(
                                $"Error removing track from playlist: {ex.Message}",
                                "Error",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        }
                    };
                    contextMenu.Items.Add(removeFromPlaylistMenuItem);
                }

                // Show the context menu
                contextMenu.IsOpen = true;
                
                // Mark the event as handled
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ContentListView_MouseRightButtonDown: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        private void ContentListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Store the mouse position for potential drag operations
            _dragStartPoint = e.GetPosition(null);
            _isDragging = true;

            // If we're not clicking on an item, clear the drag state
            HitTestResult result = VisualTreeHelper.HitTest(ContentListView_Control, e.GetPosition(ContentListView_Control));
            if (result == null || !(result.VisualHit is FrameworkElement element && 
                                   FindAncestor<ListViewItem>(element) != null))
            {
                _isDragging = false;
            }
        }
        
        private void ContentListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // If the mouse isn't pressed or we're not tracking a drag operation, exit
            if (e.LeftButton != MouseButtonState.Pressed || !_isDragging)
                return;

            // Get the current mouse position
            Point position = e.GetPosition(null);
            Vector diff = _dragStartPoint - position;

            // If the mouse has moved far enough to be considered a drag...
            if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // Get the selected items
                var selectedItems = ContentListView_Control.SelectedItems.Cast<MusicContentItem>().ToList();
                if (selectedItems.Count == 0)
                    return;

                // Only allow dragging tracks, not albums or playlists
                if (selectedItems.Any(item => item.Type != MusicItemType.Track))
                {
                    Debug.WriteLine("Drag operation aborted: Only tracks can be dragged to playlists");
                    return;
                }

                // Create a data object with the selected items
                DataObject dragData = new DataObject("MusicContentItems", selectedItems);
                
                // Start the drag-drop operation
                Debug.WriteLine($"Starting drag operation with {selectedItems.Count} tracks");
                DragDrop.DoDragDrop(ContentListView_Control, dragData, DragDropEffects.Copy);
                
                // Reset drag state
                _isDragging = false;
            }
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
                                Debug.WriteLine($"Shuffle Play Album: Got {tracks.Count()} tracks");
                                
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
                                
                                // Play the tracks using the MediaPlayerManager directly with shuffle enabled
                                if (_mediaPlayerManager != null)
                                {
                                    Debug.WriteLine("Playing shuffled tracks using MediaPlayerManager.PlayTracksWithShuffle");
                                    
                                    // Explicitly log that we're using shuffle mode
                                    Debug.WriteLine("Setting shuffle=true to ensure random track selection");
                                    
                                    // Call PlayTracksWithShuffle with shuffle=true to ensure random starting track
                                    _mediaPlayerManager.PlayTracksWithShuffle(musicItems, true);
                                }
                                else
                                {
                                    Debug.WriteLine("WARNING: _mediaPlayerManager is null, using ViewModel.PlayTracksAsync instead");
                                    
                                    // Fallback to using the ViewModel
                                    var shuffledTracks = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                                    await _viewModel.PlayTracksAsync(shuffledTracks);
                                }
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
                                
                                // Play the tracks using the MediaPlayerManager directly
                                if (_mediaPlayerManager != null)
                                {
                                    Debug.WriteLine("Playing tracks using MediaPlayerManager.PlayTracks");
                                    _mediaPlayerManager.PlayTracks(musicItems);
                                }
                                else
                                {
                                    Debug.WriteLine("WARNING: _mediaPlayerManager is null, using ViewModel.PlayTracksAsync instead");
                                    await _viewModel.PlayTracksAsync(tracks);
                                }
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
                                Debug.WriteLine($"Shuffle Play Playlist: Got {tracks.Count()} tracks");
                                
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
                                
                                // Play the tracks using the MediaPlayerManager directly with shuffle enabled
                                if (_mediaPlayerManager != null)
                                {
                                    Debug.WriteLine("Playing shuffled tracks using MediaPlayerManager.PlayTracksWithShuffle");
                                    
                                    // Explicitly log that we're using shuffle mode
                                    Debug.WriteLine("Setting shuffle=true to ensure random track selection");
                                    
                                    // Call PlayTracksWithShuffle with shuffle=true to ensure random starting track
                                    _mediaPlayerManager.PlayTracksWithShuffle(musicItems, true);
                                }
                                else
                                {
                                    Debug.WriteLine("WARNING: _mediaPlayerManager is null, using ViewModel.PlayTracksAsync instead");
                                    
                                    // Fallback to using the ViewModel
                                    var shuffledTracks = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                                    await _viewModel.PlayTracksAsync(shuffledTracks);
                                }
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
            try
            {
                // Check if the data format is supported
                if (!e.Data.GetDataPresent("MusicContentItems"))
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // Get the playlist item under the mouse
                TreeViewItem hoveredItem = FindTreeViewItemFromPoint(NavigationTree, e.GetPosition(NavigationTree));
                if (hoveredItem == null)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // Check if the target is a playlist
                MusicTreeItem targetItem = hoveredItem.DataContext as MusicTreeItem;
                if (targetItem == null || targetItem.Type != MusicItemType.Playlist)
                {
                    e.Effects = DragDropEffects.None;
                    e.Handled = true;
                    return;
                }

                // Highlight the target playlist item
                hoveredItem.Background = Brushes.LightBlue;
                _lastHighlightedItem = hoveredItem;
                
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigationTree_DragEnter: {ex.Message}");
                e.Effects = DragDropEffects.None;
                e.Handled = true;
            }
        }

        private void NavigationTree_DragLeave(object sender, DragEventArgs e)
        {
            // Remove highlight from the last highlighted item
            if (_lastHighlightedItem != null)
            {
                _lastHighlightedItem.Background = Brushes.Transparent;
                _lastHighlightedItem = null;
            }
        }

        private void NavigationTree_Drop(object sender, DragEventArgs e)
        {
            try
            {
                // Check if the data format is supported
                if (!e.Data.GetDataPresent("MusicContentItems"))
                {
                    e.Handled = true;
                    return;
                }

                // Get the dropped items
                var droppedItems = e.Data.GetData("MusicContentItems") as List<MusicContentItem>;
                if (droppedItems == null || droppedItems.Count == 0)
                {
                    e.Handled = true;
                    return;
                }

                // Get the playlist item under the mouse
                TreeViewItem hoveredItem = FindTreeViewItemFromPoint(NavigationTree, e.GetPosition(NavigationTree));
                if (hoveredItem == null)
                {
                    e.Handled = true;
                    return;
                }

                // Check if the target is a playlist
                MusicTreeItem targetItem = hoveredItem.DataContext as MusicTreeItem;
                if (targetItem == null || targetItem.Type != MusicItemType.Playlist)
                {
                    e.Handled = true;
                    return;
                }

                // Remove highlight from the target playlist item
                hoveredItem.Background = Brushes.Transparent;
                _lastHighlightedItem = null;

                // Add the tracks to the playlist
                AddTracksToPlaylist(droppedItems, targetItem.Id, targetItem.Name);
                
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NavigationTree_Drop: {ex.Message}");
                e.Handled = true;
            }
        }

        private async void AddTracksToPlaylist(List<MusicContentItem> tracks, string playlistId, string playlistName)
        {
            try
            {
                Debug.WriteLine($"Adding {tracks.Count} tracks to playlist {playlistName} (ID: {playlistId})");
                
                // Convert MusicContentItems to Track IDs
                var trackIds = tracks.Select(t => t.Id).ToList();
                
                // Call the ViewModel method to add tracks to the playlist
                bool success = await _viewModel.AddTracksToPlaylistAsync(playlistId, trackIds);
                
                if (success)
                {
                    Debug.WriteLine($"Successfully added {tracks.Count} tracks to playlist {playlistName}");
                    MessageBox.Show($"Added {tracks.Count} tracks to playlist '{playlistName}'", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Debug.WriteLine($"Failed to add tracks to playlist {playlistName}");
                    MessageBox.Show($"Failed to add tracks to playlist '{playlistName}'", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding tracks to playlist: {ex.Message}");
                MessageBox.Show($"Error adding tracks to playlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TreeViewItem FindTreeViewItemFromPoint(TreeView treeView, Point point)
        {
            HitTestResult result = VisualTreeHelper.HitTest(treeView, point);
            if (result == null)
                return null;

            DependencyObject obj = result.VisualHit;
            while (obj != null && !(obj is TreeViewItem))
            {
                obj = VisualTreeHelper.GetParent(obj);
            }

            return obj as TreeViewItem;
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
            if (ContentListView_Control.SelectedItems.Count > 0)
            {
                var selectedItems = ContentListView_Control.SelectedItems.Cast<MusicContentItem>().ToList();
                if (selectedItems.Any())
                {
                    // Convert to Track objects
                    var tracks = selectedItems.Select(item => new Track
                    {
                        Id = item.Id,
                        Title = item.Name,
                        Artist = item.ArtistName,
                        Album = item.Album,
                        Duration = item.Duration,
                        StreamUrl = item.StreamUrl
                    }).ToList();

                    // Shuffle the tracks
                    var shuffledTracks = tracks.OrderBy(x => Guid.NewGuid()).ToList();
                    
                    // Play the shuffled tracks
                    _viewModel.PlayTracksAsync(shuffledTracks);
                }
            }
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

        // Helper method to find an ancestor of a specific type
        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null && !(current is T))
            {
                current = VisualTreeHelper.GetParent(current);
            }
            return current as T;
        }

        // Handle track changed event to highlight the currently playing track
        private void MediaPlayerManager_TrackChanged(object sender, Universa.Desktop.Models.Track track)
        {
            try
            {
                if (track == null)
                {
                    // Clear all IsPlaying flags when no track is playing
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        foreach (var item in _viewModel.ContentItems)
                        {
                            item.IsPlaying = false;
                        }
                    });
                    Debug.WriteLine("MediaPlayerManager_TrackChanged: Cleared IsPlaying flags (track is null)");
                    return;
                }
                
                Debug.WriteLine($"MediaPlayerManager_TrackChanged: Track changed to {track.Title} (ID: {track.Id})");
                Debug.WriteLine($"MediaPlayerManager_TrackChanged: ContentItems count: {_viewModel.ContentItems.Count}");
                
                // Find the corresponding MusicContentItem in the ContentItems collection
                Application.Current.Dispatcher.Invoke(() =>
                {
                    // First, clear all existing IsPlaying flags
                    foreach (var item in _viewModel.ContentItems)
                    {
                        item.IsPlaying = false;
                    }
                    
                    // Try to find the item by ID first (most reliable)
                    var playingItem = _viewModel.ContentItems.FirstOrDefault(item => item.Id == track.Id);
                    
                    // If not found by ID, try by other properties (title, artist, album)
                    if (playingItem == null && !string.IsNullOrEmpty(track.Title))
                    {
                        playingItem = _viewModel.ContentItems.FirstOrDefault(item => 
                            item.Name == track.Title && 
                            (string.IsNullOrEmpty(track.Artist) || item.ArtistName == track.Artist));
                        
                        if (playingItem != null)
                        {
                            Debug.WriteLine($"MediaPlayerManager_TrackChanged: Found item by title/artist: {playingItem.Name}");
                        }
                    }
                    
                    if (playingItem != null)
                    {
                        Debug.WriteLine($"MediaPlayerManager_TrackChanged: Found matching item in ContentItems: {playingItem.Name} (ID: {playingItem.Id})");
                        playingItem.IsPlaying = true;
                        
                        // Scroll to the playing item to make it visible
                        ContentListView_Control.ScrollIntoView(playingItem);
                        
                        // Make the item selected for better visibility
                        ContentListView_Control.SelectedItem = playingItem;
                    }
                    else
                    {
                        Debug.WriteLine($"MediaPlayerManager_TrackChanged: No matching item found in ContentItems for track {track.Id} / {track.Title}");
                        // Log some items from the ContentItems collection for debugging
                        if (_viewModel.ContentItems.Any())
                        {
                            var sampleItems = _viewModel.ContentItems.Take(3);
                            foreach (var item in sampleItems)
                            {
                                Debug.WriteLine($"Sample item: ID={item.Id}, Name={item.Name}, Artist={item.ArtistName}");
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MediaPlayerManager_TrackChanged: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private string GetSettingsFilePath()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Universa",
                "MusicTab"
            );
            
            if (!Directory.Exists(appDataPath))
                Directory.CreateDirectory(appDataPath);
                
            return Path.Combine(appDataPath, "MusicTabSettings.json");
        }
        
        private void RestoreColumnWidths()
        {
            try
            {
                if (ContentListView_Control.View is GridView gridView)
                {
                    // Apply default column widths
                    foreach (var column in gridView.Columns)
                    {
                        if (column.Width == 0)
                        {
                            column.Width = 100; // Default width
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error restoring column widths: {ex.Message}");
            }
        }

        private void SaveColumnWidths()
        {
            try
            {
                if (ContentListView_Control.View is GridView gridView)
                {
                    _uiSettings.ColumnWidths.Clear();
                    foreach (var column in gridView.Columns)
                    {
                        if (column.Header != null)
                        {
                            _uiSettings.ColumnWidths[column.Header.ToString()] = column.ActualWidth;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving column widths: {ex.Message}");
            }
        }

        private void ContentListView_Control_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Implement a debounce mechanism for column width changes
            if (_columnResizeTimer == null)
            {
                _columnResizeTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                
                _columnResizeTimer.Tick += (s, args) =>
                {
                    _columnResizeTimer.Stop();
                    Debug.WriteLine("Column widths saved after resize");
                };
            }
            
            // Reset the timer
            _columnResizeTimer.Stop();
            _columnResizeTimer.Start();
        }

        private void ColumnResizeTimer_Tick(object sender, EventArgs e)
        {
            // Implement column resize logic if needed
        }

        private void MusicTab_Loaded(object sender, RoutedEventArgs e)
        {
            // Implement any additional logic needed when the tab is loaded
        }

        private void MusicTab_Unloaded(object sender, RoutedEventArgs e)
        {
            // Implement any additional logic needed when the tab is unloaded
        }

        // Clean up event handlers when the control is unloaded
        ~MusicTab()
        {
            // Unsubscribe from events to prevent memory leaks
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.TrackChanged -= MediaPlayerManager_TrackChanged;
            }
            
            if (_viewModel != null)
            {
                _viewModel.PlayTracksRequested -= ViewModel_PlayTracksRequested;
            }
        }

        private void GridViewColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // If we're resizing columns, save the settings
            if (Math.Abs(e.PreviousSize.Width - e.NewSize.Width) > 0.5)
            {
                // Debounce saving to avoid excessive I/O during resize
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(0.5) };
                timer.Tick += (s, args) =>
                {
                    Debug.WriteLine("Column width changed");
                    timer.Stop();
                };
                timer.Start();
            }
        }
        
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Implement grid splitter drag completed logic if needed
        }

        private TreeViewItem FindTreeViewItemForDataItem(ItemsControl container, object item)
        {
            if (container == null) return null;
            
            if (container.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem treeViewItem)
            {
                return treeViewItem;
            }
            
            // Search in nested items
            foreach (object childItem in container.Items)
            {
                TreeViewItem childContainer = container.ItemContainerGenerator.ContainerFromItem(childItem) as TreeViewItem;
                if (childContainer != null)
                {
                    TreeViewItem result = FindTreeViewItemForDataItem(childContainer, item);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            
            return null;
        }
    }
} 