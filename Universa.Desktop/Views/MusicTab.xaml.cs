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
using Universa.Desktop.Data;  // For CharacterizationStore
using System.Diagnostics;
using Universa.Desktop.Controls;
using Universa.Desktop.Dialogs;  // For PlaylistNameDialog
using Universa.Desktop.Core.Configuration;
using Track = Universa.Desktop.Models.Track;  // Explicitly resolve Track ambiguity
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Universa.Desktop.Views
{
    public partial class MusicTab : UserControl, INotifyPropertyChanged
    {
        private readonly BaseMainWindow _mainWindow;
        private readonly IConfigurationService _configService;
        private readonly ObservableCollection<MusicItem> _artistsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _albumsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _playlistsList = new ObservableCollection<MusicItem>();
        private readonly ObservableCollection<MusicItem> _rootItems = new ObservableCollection<MusicItem>();
        private readonly CharacterizationStore _characterizationStore;
        private ICollectionView _filteredView;
        private string _currentFilter = string.Empty;
        private ISubsonicService _subsonicClient;
        private UserControl _navigator;
        private TrackInfo _currentTrack;
        private TimeSpan _currentPosition;
        private List<TrackInfo> _trackList;
        private bool _isDragging = false;
        private TreeViewItem _lastHighlightedItem;
        private MusicItem _currentlyViewedPlaylist;
        private readonly MusicDataCache _musicCache = new MusicDataCache();
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initializationLock = new SemaphoreSlim(1, 1);
        private List<MusicItem> _allMusicData;
        private Point _dragStartPoint;
        private LoadingState _currentLoadingState;
        private string _errorMessage;

        public enum LoadingState
        {
            Idle,
            LoadingLibrary,
            LoadingContent,
            Error
        }

        public LoadingState CurrentLoadingState
        {
            get => _currentLoadingState;
            private set
            {
                _currentLoadingState = value;
                OnPropertyChanged(nameof(CurrentLoadingState));
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                _errorMessage = value;
                OnPropertyChanged(nameof(ErrorMessage));
            }
        }

        public bool IsLoading => CurrentLoadingState == LoadingState.LoadingLibrary || 
                               CurrentLoadingState == LoadingState.LoadingContent;

        public bool IsCurrentlyPlaying => _mainWindow._mediaPlayerManager?.IsPlaying ?? false;

        public ObservableCollection<MusicItem> Artists => _artistsList;
        public ObservableCollection<MusicItem> Albums => _albumsList;
        public ObservableCollection<MusicItem> Playlists => _playlistsList;

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
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting configuration service: {ex.Message}");
                MessageBox.Show("Error initializing music tab. Please try again later.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _characterizationStore = new CharacterizationStore();

            // Clear existing collections instead of reassigning
            _artistsList.Clear();
            _albumsList.Clear();
            _playlistsList.Clear();
            _rootItems.Clear();

            // Initialize collections
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Set the ItemsSource for the NavigationTree
                NavigationTree.ItemsSource = _rootItems;
            });

            // Clean up old cache files
            CleanupOldCacheFiles();

            // Initialize services
            _musicCache = new MusicDataCache();

            // Initialize the filtered view
            InitializeCollectionView();
        }

        public MusicTab(BaseMainWindow mainWindow) : this()
        {
            _mainWindow = mainWindow;

            // Set up event handlers for ContentListView_Control
            ContentListView_Control.SelectionChanged += ContentListView_SelectionChanged;
            ContentListView_Control.MouseRightButtonDown += ContentListView_MouseRightButtonDown;
            ContentListView_Control.MouseDoubleClick += ContentListView_MouseDoubleClick;
            ContentListView_Control.PreviewMouseLeftButtonDown += ContentListView_PreviewMouseLeftButtonDown;
            ContentListView_Control.PreviewMouseMove += ContentListView_PreviewMouseMove;

            // Move initialization to Loaded event
            Loaded += async (s, e) =>
            {
                try
                {
                    // Only initialize once
                    if (!await _initializationLock.WaitAsync(0))
                    {
                        return;  // Already initializing
                    }

                    try
                    {
                        if (_isInitialized) return;

                        // Check configuration
                        var config = _configService.Provider;
                        if (config == null)
                        {
                            MessageBox.Show("Configuration service is not available. Please try again later.", 
                                "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        if (string.IsNullOrEmpty(config.SubsonicUrl) ||
                            string.IsNullOrEmpty(config.SubsonicUsername) ||
                            string.IsNullOrEmpty(config.SubsonicPassword))
                        {
                            MessageBox.Show("Subsonic settings are not configured. Please configure them in Settings.", 
                                "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        // Initialize Subsonic client first
                        await Task.Run(async () => await InitializeSubsonicClientAsync());
                        if (_subsonicClient == null) return;

                        // Set up event handlers
                        NavigationTree.SelectedItemChanged += NavigationTree_SelectedItemChanged;
                        NavigationTree.MouseRightButtonDown += NavigationTree_MouseRightButtonDown;
                        NavigationTree.MouseDoubleClick += NavigationTree_MouseDoubleClick;
                        NavigationTree.DragEnter += NavigationTree_DragEnter;
                        NavigationTree.Drop += NavigationTree_Drop;

                        // Set up column handlers
                        await Application.Current.Dispatcher.InvokeAsync(() => SetupColumnHandlers());

                        // Try to load cached data first
                        bool hasCachedData = await Task.Run(async () => await LoadCachedDataAsync());
                        
                        // If no cached data, load from service
                        if (!hasCachedData)
                        {
                            await Task.Run(async () => await LoadMusicDataAsync());
                        }

                        // Clear any initial selection
                        await Application.Current.Dispatcher.InvokeAsync(() => ContentListView_Control.Items.Clear());

                        // Update the tab title after initialization
                        UpdateTabTitle();

                        _isInitialized = true;
                    }
                    finally
                    {
                        _initializationLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error initializing music tab: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            Unloaded += MusicTab_Unloaded;
        }

        private void InitializeCollectionView()
        {
            // Initialize the filtered view for the search functionality
            if (ContentListView_Control?.Items != null)
            {
                _filteredView = CollectionViewSource.GetDefaultView(ContentListView_Control.Items);
                _filteredView.Filter = FilterItem;
            }
        }

        private bool FilterItem(object item)
        {
            if (string.IsNullOrEmpty(_currentFilter)) return true;
            if (item is Universa.Desktop.Models.Track track)
            {
                return track.Title?.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) == true ||
                       track.Artist?.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) == true ||
                       track.Album?.Contains(_currentFilter, StringComparison.OrdinalIgnoreCase) == true;
            }
            return true;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentFilter = SearchBox.Text?.Trim().ToLower() ?? string.Empty;
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _filteredView?.Refresh();
            }), DispatcherPriority.Background);
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _subsonicClient.RefreshConfiguration();
                // Refresh your content here
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error refreshing content");
                MessageBox.Show("Error refreshing content: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is MusicItem selectedItem)
            {
                Debug.WriteLine($"Selected item: {selectedItem.Name} of type {selectedItem.Type}");
                
                try
                {
                    // Don't show content for top-level categories
                    if (selectedItem.Type == MusicItemType.Category)
                    {
                        return;
                    }

                    ContentListView_Control.Items.Clear();

                    switch (selectedItem.Type)
                    {
                        case MusicItemType.Artist:
                            // For artists, show their albums
                            var artistAlbums = _albumsList.Where(a => a.ArtistName == selectedItem.Name);
                            foreach (var album in artistAlbums)
                            {
                                ContentListView_Control.Items.Add(album);
                            }
                            break;

                        case MusicItemType.Album:
                            // Find the album in _allMusicData and show its tracks
                            var albumData = _allMusicData.FirstOrDefault(a => 
                                a.Type == MusicItemType.Album && 
                                a.Name == selectedItem.Name && 
                                a.ArtistName == selectedItem.ArtistName);
                            
                            if (albumData?.Items != null)
                            {
                                foreach (var track in albumData.Items.OrderBy(t => t.TrackNumber))
                                {
                                    ContentListView_Control.Items.Add(track);
                                }
                            }
                            break;

                        case MusicItemType.Playlist:
                            _currentlyViewedPlaylist = selectedItem;
                            // Find the playlist in _allMusicData and show its tracks
                            var playlistData = _allMusicData.FirstOrDefault(p => 
                                p.Type == MusicItemType.Playlist && 
                                p.Id == selectedItem.Id);
                            
                            if (playlistData?.Items != null)
                            {
                                foreach (var track in playlistData.Items)
                                {
                                    ContentListView_Control.Items.Add(track);
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error handling navigation selection: {ex.Message}");
                    MessageBox.Show($"Error loading content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void NavigationTree_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var treeViewItem = VisualUpwardSearch<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem == null) return;

            var musicItem = treeViewItem.DataContext as MusicItem;
            if (musicItem == null) return;

            var contextMenu = new ContextMenu();

            if (musicItem.Type == MusicItemType.Playlist)
            {
                var playMenuItem = new MenuItem { Header = "Play Playlist" };
                playMenuItem.Click += async (s, args) =>
                {
                    try
                    {
                        var playlistData = _allMusicData.FirstOrDefault(p => 
                            p.Type == MusicItemType.Playlist && 
                            p.Id == musicItem.Id);

                        if (playlistData?.Items != null && playlistData.Items.Any())
                        {
                            _mainWindow._mediaPlayerManager.PlayTracks(playlistData.Items);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error playing playlist: {ex.Message}");
                        MessageBox.Show("Failed to play playlist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                var shuffleMenuItem = new MenuItem { Header = "Shuffle Playlist" };
                shuffleMenuItem.Click += async (s, args) =>
                {
                    try
                    {
                        if (_mainWindow?._mediaPlayerManager == null)
                        {
                            MessageBox.Show("Media player is not initialized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        var playlistData = _allMusicData.FirstOrDefault(p => 
                            p.Type == MusicItemType.Playlist && 
                            p.Id == musicItem.Id);

                        if (playlistData?.Items != null && playlistData.Items.Any())
                        {
                            var random = new Random();
                            var shuffledTracks = playlistData.Items.OrderBy(x => random.Next()).ToList();
                            _mainWindow._mediaPlayerManager.PlayTracks(shuffledTracks);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error shuffling playlist: {ex.Message}");
                        MessageBox.Show("Failed to shuffle playlist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                var deleteMenuItem = new MenuItem { Header = "Delete Playlist" };
                deleteMenuItem.Click += async (s, args) =>
                {
                    try
                    {
                        var result = MessageBox.Show("Are you sure you want to delete this playlist?", "Confirm Delete", 
                            MessageBoxButton.YesNo, MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Yes)
                        {
                            await _subsonicClient.DeletePlaylistAsync(musicItem.Id);
                            await LoadMusicDataAsync(); // Refresh the list
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error deleting playlist: {ex.Message}");
                        MessageBox.Show("Failed to delete playlist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                contextMenu.Items.Add(playMenuItem);
                contextMenu.Items.Add(shuffleMenuItem);
                contextMenu.Items.Add(deleteMenuItem);
            }
            else if (musicItem.Name == "Playlists") // Root playlists node
            {
                var addMenuItem = new MenuItem { Header = "Add Playlist" };
                addMenuItem.Click += async (s, args) =>
                {
                    try
                    {
                        var dialog = new TextInputDialog("Create Playlist", "Enter playlist name:");
                        if (dialog.ShowDialog() == true)
                        {
                            var playlistName = dialog.ResponseText;
                            if (!string.IsNullOrWhiteSpace(playlistName))
                            {
                                await _subsonicClient.CreatePlaylistAsync(playlistName);
                                await LoadMusicDataAsync(); // Refresh the list
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating playlist: {ex.Message}");
                        MessageBox.Show("Failed to create playlist.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                contextMenu.Items.Add(addMenuItem);
            }

            if (contextMenu.Items.Count > 0)
            {
                contextMenu.IsOpen = true;
                e.Handled = true;
            }
        }

        private static T VisualUpwardSearch<T>(DependencyObject source) where T : DependencyObject
        {
            while (source != null && !(source is T))
            {
                source = VisualTreeHelper.GetParent(source);
            }
            return source as T;
        }

        private void NavigationTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = (e.OriginalSource as FrameworkElement)?.DataContext as MusicItem;
            if (item != null && (item.Type == MusicItemType.Album || item.Type == MusicItemType.Playlist))
            {
                PlayItem(item);
            }
        }

        private void NavigationTree_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(MusicItem)))
            {
                var targetItem = GetTreeViewItemFromPoint(e.GetPosition(NavigationTree));
                if (targetItem != null && targetItem.DataContext is MusicItem musicItem && musicItem.Type == MusicItemType.Playlist)
                {
                    e.Effects = DragDropEffects.Copy;
                    if (_lastHighlightedItem != null && _lastHighlightedItem != targetItem)
                    {
                        _lastHighlightedItem.Background = Brushes.Transparent;
                    }

                    // Try to get the highlight brush from resources, fall back to a default if not found
                    Brush highlightBrush;
                    try
                    {
                        highlightBrush = (Brush)FindResource("ListItemSelectedBackgroundBrush");
                    }
                    catch (ResourceReferenceKeyNotFoundException)
                    {
                        // Fallback to a default brush if the resource is not found
                        highlightBrush = new SolidColorBrush(Color.FromArgb(128, 96, 165, 250)); // Semi-transparent blue
                    }

                    targetItem.Background = highlightBrush;
                    _lastHighlightedItem = targetItem;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
        }

        private void NavigationTree_DragLeave(object sender, DragEventArgs e)
        {
            if (_lastHighlightedItem != null)
            {
                _lastHighlightedItem.Background = Brushes.Transparent;
                _lastHighlightedItem = null;
            }
        }

        private async void NavigationTree_Drop(object sender, DragEventArgs e)
        {
            try
            {
                var tracks = e.Data.GetData(typeof(List<MusicItem>)) as List<MusicItem>;
                var targetItem = GetTreeViewItemFromPoint(e.GetPosition(NavigationTree));

                if (tracks != null && targetItem?.DataContext is MusicItem target && target.Type == MusicItemType.Playlist)
                {
                    Debug.WriteLine($"Dropping {tracks.Count} tracks onto playlist: {target.Name}");

                    // Find the actual playlist in stored data
                    var playlist = _allMusicData?.FirstOrDefault(p => p.Type == MusicItemType.Playlist && p.Id == target.Id);
                    if (playlist != null)
                    {
                        var addedTracks = new List<string>();
                        var failedTracks = new List<string>();

                        foreach (var track in tracks)
                        {
                            try
                            {
                                Debug.WriteLine($"Adding track {track.Name} to playlist {target.Name}");
                                await _subsonicClient.AddToPlaylistAsync(target.Id, track.Id);
                                
                                if (playlist.Items == null)
                                {
                                    playlist.Items = new ObservableCollection<MusicItem>();
                                }
                                
                                // Only add if not already in playlist
                                if (!playlist.Items.Any(t => t.Id == track.Id))
                                {
                                    playlist.Items.Add(track);
                                    addedTracks.Add(track.Name);
                                    Debug.WriteLine($"Successfully added track {track.Name} to playlist {target.Name}");
                                }
                                else
                                {
                                    Debug.WriteLine($"Track {track.Name} already exists in playlist {target.Name}");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error adding track {track.Name} to playlist: {ex.Message}");
                                failedTracks.Add(track.Name);
                            }
                        }

                        // Update cache
                        await _musicCache.SaveMusicData(_allMusicData);
                        
                        // Build status message
                        var messageBuilder = new StringBuilder();
                        if (addedTracks.Any())
                        {
                            messageBuilder.AppendLine(addedTracks.Count == 1 
                                ? $"Added '{addedTracks[0]}' to playlist '{target.Name}'"
                                : $"Added {addedTracks.Count} tracks to playlist '{target.Name}'");
                        }
                        if (failedTracks.Any())
                        {
                            messageBuilder.AppendLine(failedTracks.Count == 1
                                ? $"Failed to add '{failedTracks[0]}'"
                                : $"Failed to add {failedTracks.Count} tracks");
                        }
                        
                        var message = messageBuilder.ToString().TrimEnd();
                        if (!string.IsNullOrEmpty(message))
                        {
                            MessageBox.Show(message, addedTracks.Any() ? "Success" : "Error", 
                                MessageBoxButton.OK, 
                                addedTracks.Any() ? MessageBoxImage.Information : MessageBoxImage.Warning);
                        }

                        // Update both the navigation tree and content view
                        var navPlaylist = _playlistsList.FirstOrDefault(p => p.Id == target.Id);
                        if (navPlaylist != null)
                        {
                            navPlaylist.Items = playlist.Items;
                            navPlaylist.HasChildren = playlist.Items?.Any() == true;
                        }

                        // If this is the currently viewed playlist, refresh the view
                        if (_currentlyViewedPlaylist?.Id == target.Id)
                        {
                            ContentListView_Control.Items.Clear();
                            foreach (var item in playlist.Items)
                            {
                                ContentListView_Control.Items.Add(item);
                            }
                        }
                        
                        Debug.WriteLine($"Completed drop operation. Added: {addedTracks.Count}, Failed: {failedTracks.Count}");
                    }
                }

                // Clear highlight
                if (_lastHighlightedItem != null)
                {
                    _lastHighlightedItem.Background = Brushes.Transparent;
                    _lastHighlightedItem = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during drop operation: {ex.Message}");
                MessageBox.Show($"Error adding tracks to playlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TreeViewItem GetTreeViewItemFromPoint(Point point)
        {
            DependencyObject element = NavigationTree.InputHitTest(point) as DependencyObject;
            while (element != null && !(element is TreeViewItem))
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as TreeViewItem;
        }

        private void ContentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ContentListView_Control.SelectedItem is Universa.Desktop.Models.Track track)
            {
                // Handle selection
            }
        }

        private void ContentListView_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var point = e.GetPosition(ContentListView_Control);
            var element = ContentListView_Control.InputHitTest(point) as UIElement;
            if (element != null)
            {
                var item = ContentListView_Control.ContainerFromElement(element) as ListViewItem;
                if (item != null)
                {
                    item.IsSelected = true;
                }
            }
        }

        private void ContentListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ContentListView_Control.SelectedItem is MusicItem selectedItem)
            {
                if (selectedItem.Type == MusicItemType.Track)
                {
                    // For tracks, play them
                    PlayItem(selectedItem);
                }
                else if (selectedItem.Type == MusicItemType.Album)
                {
                    // For albums, show their tracks
                    ContentListView_Control.Items.Clear();
                    
                    // Find the album in _allMusicData and show its tracks
                    var albumData = _allMusicData?.FirstOrDefault(a => 
                        a.Type == MusicItemType.Album && 
                        a.Name == selectedItem.Name && 
                        a.ArtistName == selectedItem.ArtistName);
                    
                    if (albumData?.Items != null)
                    {
                        foreach (var track in albumData.Items.OrderBy(t => t.TrackNumber))
                        {
                            ContentListView_Control.Items.Add(track);
                        }
                    }
                }
            }
        }

        private void ContentListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
            
            // Get the clicked item
            var item = (e.OriginalSource as FrameworkElement)?.DataContext as MusicItem;
            if (item != null)
            {
                // If the clicked item is not in the current selection and Ctrl/Shift is not pressed,
                // clear the selection and select only this item
                if (!ContentListView_Control.SelectedItems.Contains(item) && 
                    (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) == 0)
                {
                    ContentListView_Control.SelectedItems.Clear();
                    ContentListView_Control.SelectedItems.Add(item);
                }
                // Otherwise, keep the current selection
            }
        }

        private void ContentListView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag(e);
                }
            }
        }

        private void StartDrag(MouseEventArgs e)
        {
            var selectedItems = ContentListView_Control.SelectedItems.Cast<MusicItem>()
                .Where(item => item.Type == MusicItemType.Track)
                .ToList();

            if (selectedItems.Any())
            {
                Debug.WriteLine($"Starting drag operation with {selectedItems.Count} tracks");
                _isDragging = true;
                var data = new DataObject(typeof(List<MusicItem>), selectedItems);
                DragDrop.DoDragDrop(ContentListView_Control, data, DragDropEffects.Copy);
                _isDragging = false;
            }
        }

        private async Task PlayTracks(IEnumerable<Track> tracks, bool shuffle = false)
        {
            if (tracks == null || !tracks.Any()) return;

            var trackList = tracks.ToList();
            if (shuffle)
            {
                var rng = new Random();
                trackList = trackList.OrderBy(x => rng.Next()).ToList();
            }

            // Play the first track
            if (trackList.Any())
            {
                _mainWindow._mediaPlayerManager.PlayMedia(trackList.First());
            }
        }

        private async Task<Universa.Desktop.Models.Album> GetAlbumForTrack(Universa.Desktop.Models.Track track)
        {
            try
            {
                return await _subsonicClient.GetAlbumAsync(track.Album);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error getting album for track {track.Title}");
                return null;
            }
        }

        private async Task<TimeSpan> GetDurationForTrack(Universa.Desktop.Models.Track track)
        {
            try
            {
                var trackInfo = await _subsonicClient.GetTrackInfoAsync(track.Id);
                return trackInfo?.Duration ?? TimeSpan.Zero;
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Error getting duration for track {track.Title}");
                return TimeSpan.Zero;
            }
        }

        private async void PlayItem(MusicItem item)
        {
            try
            {
                switch (item.Type)
                {
                    case MusicItemType.Track:
                        // Track is already loaded, just play it
                        var track = new Track
                        {
                            Id = item.Id,
                            Title = item.Name,
                            Artist = item.Artist ?? item.ArtistName,
                            Album = item.Album,
                            StreamUrl = item.StreamUrl,
                            Duration = item.Duration,
                            TrackNumber = item.TrackNumber
                        };
                        _mainWindow._mediaPlayerManager.PlayMedia(track);
                        break;

                    case MusicItemType.Album:
                        try
                        {
                            // First check if we have the tracks in cache
                            var albumData = _allMusicData?.FirstOrDefault(a => 
                                a.Type == MusicItemType.Album && 
                                a.Id == item.Id);

                            if (albumData?.Items?.Any() == true)
                            {
                                // Use cached tracks
                                _mainWindow._mediaPlayerManager.PlayTracks(albumData.Items);
                            }
                            else
                            {
                                // Load album tracks from server
                                var albumTracks = await _subsonicClient.GetTracks(item.Id);
                                if (albumTracks?.Any() == true)
                                {
                                    _mainWindow._mediaPlayerManager.PlayTracks(albumTracks);
                                }
                                else
                                {
                                    Debug.WriteLine($"No tracks found in album {item.Name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading album tracks: {ex.Message}");
                            MessageBox.Show($"Error loading album: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        break;

                    case MusicItemType.Playlist:
                        try
                        {
                            // First check if we have the tracks in cache
                            var playlistData = _allMusicData?.FirstOrDefault(p => 
                                p.Type == MusicItemType.Playlist && 
                                p.Id == item.Id);

                            if (playlistData?.Items?.Any() == true)
                            {
                                // Use cached tracks
                                _mainWindow._mediaPlayerManager.PlayTracks(playlistData.Items);
                            }
                            else
                            {
                                // Load playlist tracks from server
                                var playlistTracks = await _subsonicClient.GetPlaylistTracks(item.Id);
                                if (playlistTracks?.Any() == true)
                                {
                                    _mainWindow._mediaPlayerManager.PlayTracks(playlistTracks);
                                    
                                    // Update cache
                                    if (playlistData != null)
                                    {
                                        playlistData.Items = new ObservableCollection<MusicItem>(playlistTracks);
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine($"No tracks found in playlist {item.Name}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error loading playlist tracks: {ex.Message}");
                            MessageBox.Show($"Error loading playlist: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error playing item: {ex.Message}");
                MessageBox.Show($"Error playing item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShuffleMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ContentListView_Control.SelectedItems;
            if (selectedItems != null && selectedItems.Count > 0)
            {
                var modelTracks = selectedItems.Cast<MusicItem>()
                    .Select(item => new Universa.Desktop.Models.Track
                    {
                        Id = item.Id,
                        Title = item.Name,
                        Artist = item.Artist ?? item.ArtistName,
                        Album = item.Name,
                        Duration = item.Duration,
                        StreamUrl = item.StreamUrl
                    }).ToList();

                _ = PlayTracks(modelTracks, true);
            }
        }

        private void PlayMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = ContentListView_Control.SelectedItems;
            if (selectedItems != null && selectedItems.Count > 0)
            {
                var firstItem = selectedItems[0] as MusicItem;
                if (firstItem != null)
                {
                    PlayItem(firstItem);
                }
            }
        }

        private void MusicTab_Unloaded(object sender, RoutedEventArgs e)
        {
            _subsonicClient = null;
        }

        private async Task InitializeSubsonicClientAsync()
        {
            try
            {
                Debug.WriteLine("Initializing Subsonic client");
                var config = _configService.Provider;
                var subsonicService = ServiceLocator.Instance.GetService<ISubsonicService>();
                if (subsonicService == null)
                {
                    Debug.WriteLine("Failed to get SubsonicService from ServiceLocator");
                    MessageBox.Show("Failed to initialize Subsonic service.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                _subsonicClient = subsonicService;
                await _subsonicClient.RefreshConfiguration();
                Debug.WriteLine("Successfully initialized Subsonic client");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing Subsonic client: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                Log.Error(ex, "Error initializing Subsonic client");
                MessageBox.Show($"Error initializing music service: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> LoadCachedDataAsync()
        {
            try
            {
                if (_musicCache == null || _subsonicClient == null)
                {
                    Debug.WriteLine("Music cache or Subsonic client is not initialized");
                    return false;
                }

                var cachedData = await _musicCache.LoadMusicData();
                if (cachedData?.Any() != true)
                {
                    return false;
                }

                // Immediately populate UI with cached data
                _allMusicData = cachedData;
                await PopulateMusicLists(cachedData);

                // Start background task to load playlist tracks
                _ = Task.Run(async () =>
                {
                    bool needsCacheUpdate = false;
                    var playlistTasks = cachedData
                        .Where(x => x.Type == MusicItemType.Playlist && (x.Items == null || !x.Items.Any()))
                        .Select(async playlist =>
                        {
                            try
                            {
                                var tracks = await _subsonicClient.GetPlaylistTracks(playlist.Id);
                                if (tracks != null)
                                {
                                    await Application.Current.Dispatcher.InvokeAsync(() =>
                                    {
                                        playlist.Items = new ObservableCollection<MusicItem>(tracks);
                                        playlist.HasChildren = tracks.Any();
                                        
                                        // Update UI if this is the currently viewed playlist
                                        if (_currentlyViewedPlaylist?.Id == playlist.Id)
                                        {
                                            ContentListView_Control.Items.Clear();
                                            foreach (var track in tracks)
                                            {
                                                ContentListView_Control.Items.Add(track);
                                            }
                                        }
                                    });
                                    needsCacheUpdate = true;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error loading tracks for playlist {playlist.Name}: {ex.Message}");
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    playlist.Items = new ObservableCollection<MusicItem>();
                                    playlist.HasChildren = false;
                                });
                            }
                        });

                    if (playlistTasks.Any())
                    {
                        await Task.WhenAll(playlistTasks);
                        if (needsCacheUpdate)
                        {
                            await _musicCache.SaveMusicData(cachedData);
                        }
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading cached music data: {ex.Message}");
                return false;
            }
        }

        private async Task LoadMusicDataAsync()
        {
            try
            {
                CurrentLoadingState = LoadingState.LoadingContent;
                ErrorMessage = null;

                // Only load high-level items initially
                var artists = await _subsonicClient.GetArtists();
                var albums = await _subsonicClient.GetAllAlbums();
                var playlists = await _subsonicClient.GetPlaylists();

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _artistsList.Clear();
                    _albumsList.Clear();
                    _playlistsList.Clear();

                    foreach (var artist in artists)
                    {
                        _artistsList.Add(artist);
                    }

                    foreach (var album in albums)
                    {
                        _albumsList.Add(album);
                    }

                    foreach (var playlist in playlists)
                    {
                        _playlistsList.Add(playlist);
                    }

                    // Store only high-level items in _allMusicData
                    _allMusicData = new List<MusicItem>();
                    _allMusicData.AddRange(artists);
                    _allMusicData.AddRange(albums);
                    _allMusicData.AddRange(playlists);
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading music library: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        private bool CompareMusicData(IEnumerable<MusicItem> existing, IEnumerable<MusicItem> updated)
        {
            if (existing == null || updated == null)
                return false;

            var existingSet = new HashSet<string>(existing.Select(i => $"{i.Id}_{i.Name}_{i.Type}"));
            var updatedSet = new HashSet<string>(updated.Select(i => $"{i.Id}_{i.Name}_{i.Type}"));

            return existingSet.SetEquals(updatedSet);
        }

        private async Task PopulateMusicLists(List<MusicItem> items)
        {
            if (items == null) return;

            try
            {
                _allMusicData = items;

                // Create root categories
                var artistsRoot = new MusicItem 
                { 
                    Name = "Artists", 
                    Type = MusicItemType.Category,
                    Items = new ObservableCollection<MusicItem>()
                };
                var albumsRoot = new MusicItem 
                { 
                    Name = "Albums", 
                    Type = MusicItemType.Category,
                    Items = new ObservableCollection<MusicItem>()
                };
                var playlistsRoot = new MusicItem 
                { 
                    Name = "Playlists", 
                    Type = MusicItemType.Category,
                    Items = new ObservableCollection<MusicItem>()
                };

                artistsRoot.InitializeIconData();
                albumsRoot.InitializeIconData();
                playlistsRoot.InitializeIconData();

                // Process all items in memory first
                var playlists = items.Where(i => i.Type == MusicItemType.Playlist)
                    .OrderBy(p => p.Name)
                    .Select(playlist => 
                    {
                        var playlistNav = new MusicItem
                        {
                            Id = playlist.Id,
                            Name = playlist.Name,
                            Type = MusicItemType.Playlist,
                            Description = playlist.Description,
                            Items = new ObservableCollection<MusicItem>(),  // Empty for nav tree
                            HasChildren = playlist.Items?.Any() == true
                        };
                        playlistNav.InitializeIconData();
                        return playlistNav;
                    }).ToList();

                // Group tracks by artist and album
                var artistItems = new List<MusicItem>();
                var albumItems = new List<MusicItem>();
                var artistGroups = items.Where(i => i.Type == MusicItemType.Track)
                    .GroupBy(t => t.Artist ?? "Unknown Artist");

                foreach (var artistGroup in artistGroups.OrderBy(g => g.Key))
                {
                    var artistItem = new MusicItem
                    {
                        Name = artistGroup.Key,
                        Type = MusicItemType.Artist,
                        Items = new ObservableCollection<MusicItem>()
                    };
                    artistItem.InitializeIconData();

                    foreach (var albumGroup in artistGroup.GroupBy(t => t.Album ?? "Unknown Album")
                        .Where(ag => !string.IsNullOrEmpty(ag.Key)))
                    {
                        var albumItem = new MusicItem
                        {
                            Name = albumGroup.Key,
                            Type = MusicItemType.Album,
                            ArtistName = artistGroup.Key,
                            Items = new ObservableCollection<MusicItem>(),
                            HasChildren = albumGroup.Any()
                        };
                        albumItem.InitializeIconData();

                        artistItem.Items.Add(albumItem);
                        albumItems.Add(albumItem);
                    }

                    artistItem.HasChildren = artistItem.Items.Any();
                    artistItems.Add(artistItem);
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _artistsList.Clear();
                    _albumsList.Clear();
                    _playlistsList.Clear();
                    _rootItems.Clear();

                    // Add all items at once
                    foreach (var artistItem in artistItems)
                    {
                        _artistsList.Add(artistItem);
                        artistsRoot.Items.Add(artistItem);
                    }
                    foreach (var albumItem in albumItems)
                    {
                        _albumsList.Add(albumItem);
                        albumsRoot.Items.Add(albumItem);
                    }
                    foreach (var playlist in playlists)
                    {
                        _playlistsList.Add(playlist);
                        playlistsRoot.Items.Add(playlist);
                    }

                    // Set HasChildren for root categories
                    artistsRoot.HasChildren = artistsRoot.Items.Any();
                    albumsRoot.HasChildren = albumsRoot.Items.Any();
                    playlistsRoot.HasChildren = playlistsRoot.Items.Any();

                    // Add root categories to navigation tree
                    _rootItems.Add(artistsRoot);
                    _rootItems.Add(albumsRoot);
                    _rootItems.Add(playlistsRoot);

                    // Force TreeView to refresh
                    NavigationTree.Items.Refresh();
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PopulateMusicLists: {ex.Message}");
                throw;
            }
        }

        private void SetupColumnHandlers()
        {
            if (ContentListView_Control.View is GridView gridView)
            {
                foreach (var column in gridView.Columns)
                {
                    if (column.Header is TextBlock headerText)
                    {
                        headerText.MouseLeftButtonDown += (s, e) =>
                        {
                            // Handle column click for sorting
                            var propertyName = column.DisplayMemberBinding?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(propertyName))
                            {
                                var view = CollectionViewSource.GetDefaultView(ContentListView_Control.Items);
                                view.SortDescriptions.Clear();
                                view.SortDescriptions.Add(new SortDescription(propertyName, ListSortDirection.Ascending));
                            }
                        };
                    }
                }
            }
        }

        private void CleanupOldCacheFiles()
        {
            try
            {
                var appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Universa"
                );

                // List of old cache files to remove
                var oldCacheFiles = new[]
                {
                    "subsonic_artists_cache.json",
                    "subsonic_albums_cache.json",
                    "subsonic_playlists_cache.json"
                };

                foreach (var cacheFile in oldCacheFiles)
                {
                    var filePath = Path.Combine(appDataPath, cacheFile);
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                        Debug.WriteLine($"Deleted old cache file: {cacheFile}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error cleaning up old cache files: {ex.Message}");
            }
        }

        private void UpdateTabTitle()
        {
            try
            {
                if (_mainWindow?.MainTabControl?.SelectedItem is TabItem selectedTab)
                {
                    var config = _configService?.Provider;
                    if (config != null && !string.IsNullOrEmpty(config.SubsonicName))
                    {
                        selectedTab.Header = config.SubsonicName;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating tab title: {ex.Message}");
            }
        }

        private class TextInputDialog : Window
        {
            public string ResponseText
            {
                get { return _responseTextBox.Text; }
                set { _responseTextBox.Text = value; }
            }

            private readonly TextBox _responseTextBox;

            public TextInputDialog(string title, string promptText)
            {
                Title = title;
                Width = 300;
                Height = 150;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                ResizeMode = ResizeMode.NoResize;

                var grid = new Grid { Margin = new Thickness(10) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var promptLabel = new Label { Content = promptText };
                Grid.SetRow(promptLabel, 0);

                _responseTextBox = new TextBox { Margin = new Thickness(0, 5, 0, 5) };
                Grid.SetRow(_responseTextBox, 1);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 5, 0, 0)
                };

                var okButton = new Button
                {
                    Content = "OK",
                    Width = 60,
                    Height = 25,
                    Margin = new Thickness(0, 0, 5, 0),
                    IsDefault = true
                };
                okButton.Click += (s, e) => { DialogResult = true; };

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 60,
                    Height = 25,
                    IsCancel = true
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);
                Grid.SetRow(buttonPanel, 2);

                grid.Children.Add(promptLabel);
                grid.Children.Add(_responseTextBox);
                grid.Children.Add(buttonPanel);

                Content = grid;

                Loaded += (s, e) => _responseTextBox.Focus();
            }
        }
    }
} 