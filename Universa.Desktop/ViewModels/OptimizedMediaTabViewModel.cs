using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using Universa.Desktop.Library;
using Universa.Desktop.Models;
using Universa.Desktop.Cache;
using Universa.Desktop.Commands;
using Universa.Desktop.Services;
using Universa.Desktop.Windows;

namespace Universa.Desktop.ViewModels
{
    public class OptimizedMediaTabViewModel : INotifyPropertyChanged
    {
        private readonly JellyfinService _jellyfinService;
        private readonly JellyfinMediaCache _cache;
        private const int CACHE_STALE_HOURS = 1;
        private CancellationTokenSource _loadingCancellation;
        
        // PERFORMANCE OPTIMIZATION: Remove artificial delays and increase batch efficiency
        private const int INITIAL_LOAD_SIZE = 200; // Load more items initially
        private const int INCREMENTAL_LOAD_SIZE = 100; // Load in larger chunks
        private const int VIEWPORT_BUFFER = 50; // Buffer items around viewport
        
        public enum LoadingState
        {
            Idle,
            LoadingLibrary,
            LoadingContent,
            LoadingIncremental,
            Error
        }

        private ObservableCollection<MediaItem> _contentItems;
        private ObservableCollection<MediaItem> _navigationItems;
        private MediaItem _selectedNavigationItem;
        private LoadingState _currentLoadingState;
        private string _errorMessage;
        private string _filterText;
        private bool _isInitialized;
        private List<MediaItem> _allItems; // Cache all items for filtering/virtualization
        private int _loadedItemCount;
        private bool _hasMoreItems;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand NavigateToItemCommand { get; }
        public ICommand PlayItemCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand ExpandItemCommand { get; }
        public ICommand LoadMoreCommand { get; }
        public ICommand DiagnoseCommand { get; }

        public ObservableCollection<MediaItem> ContentItems
        {
            get => _contentItems;
            set
            {
                _contentItems = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<MediaItem> NavigationItems
        {
            get => _navigationItems;
            set
            {
                _navigationItems = value;
                OnPropertyChanged();
            }
        }

        public MediaItem SelectedNavigationItem
        {
            get => _selectedNavigationItem;
            set
            {
                _selectedNavigationItem = value;
                OnPropertyChanged();
                _ = HandleNavigationSelectionAsync(value);
            }
        }

        public LoadingState CurrentLoadingState
        {
            get => _currentLoadingState;
            private set
            {
                _currentLoadingState = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(CanLoadMore));
            }
        }

        public bool IsLoading => CurrentLoadingState == LoadingState.LoadingLibrary || 
                               CurrentLoadingState == LoadingState.LoadingContent ||
                               CurrentLoadingState == LoadingState.LoadingIncremental;

        public bool CanLoadMore => !IsLoading && _hasMoreItems && _allItems?.Count > _loadedItemCount;

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
            }
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                _filterText = value;
                OnPropertyChanged();
                _ = ApplyFilterAsync();
            }
        }

        public OptimizedMediaTabViewModel(JellyfinService jellyfinService)
        {
            _jellyfinService = jellyfinService;
            _cache = JellyfinMediaCache.Instance;
            ContentItems = new ObservableCollection<MediaItem>();
            NavigationItems = new ObservableCollection<MediaItem>();
            _allItems = new List<MediaItem>();
            
            RefreshCommand = new RelayCommand(_ => _ = LoadContentWithRetryAsync(true));
            NavigateToItemCommand = new RelayCommand<MediaItem>(item => _ = HandleNavigationSelectionAsync(item));
            PlayItemCommand = new RelayCommand<MediaItem>(item => _ = PlayItemAsync(item));
            FilterCommand = new RelayCommand(_ => _ = ApplyFilterAsync());
            ExpandItemCommand = new RelayCommand<MediaItem>(item => _ = ExpandItemAsync(item));
            LoadMoreCommand = new RelayCommand(_ => _ = LoadMoreItemsAsync(), _ => CanLoadMore);
            DiagnoseCommand = new RelayCommand(_ => _ = DiagnoseCommandAsync());

            // Initial load
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            if (_isInitialized) return;
            
            await LoadContentWithRetryAsync(false);
            _isInitialized = true;
        }

        private async Task LoadContentWithRetryAsync(bool forceRefresh = false)
        {
            const int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    await LoadContentAsync(forceRefresh);
                    return;
                }
                catch (Exception ex)
                {
                    currentRetry++;
                    if (currentRetry == maxRetries)
                    {
                        ErrorMessage = $"Failed to load content after {maxRetries} attempts: {ex.Message}";
                        CurrentLoadingState = LoadingState.Error;
                        throw;
                    }
                    // Exponential backoff without Thread.Sleep - use Task.Delay instead
                    await Task.Delay(1000 * currentRetry);
                }
            }
        }

        private async Task LoadContentAsync(bool forceRefresh = false)
        {
            _loadingCancellation?.Cancel();
            _loadingCancellation = new CancellationTokenSource();

            CurrentLoadingState = LoadingState.LoadingLibrary;
            ErrorMessage = null;

            try
            {
                if (forceRefresh)
                {
                    await _jellyfinService.ClearCacheAsync();
                }

                var libraries = await _jellyfinService.GetMediaLibraryAsync();
                
                // Use ConfigureAwait(false) for better performance
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NavigationItems.Clear();
                    ContentItems.Clear();
                    
                    // Add special collections first for easy access
                    AddSpecialCollections();
                    
                    // Then add regular libraries
                    foreach (var library in libraries)
                    {
                        if (library.Type == MediaItemType.MovieLibrary || 
                            library.Type == MediaItemType.TVLibrary)
                        {
                            library.Children = new ObservableCollection<MediaItem>();
                            NavigationItems.Add(library);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading content: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        private void AddSpecialCollections()
        {
            // Continue Watching collection
            var continueWatching = new MediaItem
            {
                Id = "special_continue_watching",
                Name = "Continue Watching",
                Type = MediaItemType.SpecialCollection,
                HasChildren = false,
                Metadata = new Dictionary<string, string> { ["SpecialType"] = "ContinueWatching" }
            };
            NavigationItems.Add(continueWatching);

            // Next Up collection
            var nextUp = new MediaItem
            {
                Id = "special_next_up",
                Name = "Next Up",
                Type = MediaItemType.SpecialCollection,
                HasChildren = false,
                Metadata = new Dictionary<string, string> { ["SpecialType"] = "NextUp" }
            };
            NavigationItems.Add(nextUp);

            // Latest Movies collection
            var latestMovies = new MediaItem
            {
                Id = "special_latest_movies",
                Name = "Latest Movies",
                Type = MediaItemType.SpecialCollection,
                HasChildren = false,
                Metadata = new Dictionary<string, string> { ["SpecialType"] = "LatestMovies" }
            };
            NavigationItems.Add(latestMovies);

            // Latest TV collection
            var latestTV = new MediaItem
            {
                Id = "special_latest_tv",
                Name = "Latest TV",
                Type = MediaItemType.SpecialCollection,
                HasChildren = false,
                Metadata = new Dictionary<string, string> { ["SpecialType"] = "LatestTV" }
            };
            NavigationItems.Add(latestTV);
        }

        private async Task HandleNavigationSelectionAsync(MediaItem item)
        {
            if (item == null) return;

            switch (item.Type)
            {
                case MediaItemType.SpecialCollection:
                    await LoadSpecialCollectionAsync(item);
                    break;
                case MediaItemType.MovieLibrary:
                case MediaItemType.TVLibrary:
                    await LoadLibraryItemsOptimizedAsync(item);
                    break;
                case MediaItemType.Series:
                    await LoadSeriesSeasonsAsync(item);
                    break;
                case MediaItemType.Season:
                    await LoadSeasonEpisodesAsync(item);
                    break;
            }
        }

        private async Task LoadSpecialCollectionAsync(MediaItem collection)
        {
            if (collection?.Metadata == null) return;

            CurrentLoadingState = LoadingState.LoadingContent;
            ErrorMessage = null;

            try
            {
                var specialType = collection.Metadata.GetValueOrDefault("SpecialType");
                System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Loading {specialType} collection");
                List<MediaItem> items = new List<MediaItem>();

                switch (specialType)
                {
                    case "ContinueWatching":
                        // Get both TV and movie continue watching
                        System.Diagnostics.Debug.WriteLine("LoadSpecialCollectionAsync: Fetching TV continue watching...");
                        var continueTV = await _jellyfinService.GetContinueWatchingAsync(true);
                        System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Got {continueTV?.Count ?? 0} TV items");
                        
                        System.Diagnostics.Debug.WriteLine("LoadSpecialCollectionAsync: Fetching movie continue watching...");
                        var continueMovies = await _jellyfinService.GetContinueWatchingAsync(false);
                        System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Got {continueMovies?.Count ?? 0} movie items");
                        
                        if (continueTV != null) items.AddRange(continueTV);
                        if (continueMovies != null) items.AddRange(continueMovies);
                        break;

                    case "NextUp":
                        System.Diagnostics.Debug.WriteLine("LoadSpecialCollectionAsync: Fetching next up episodes...");
                        items = await _jellyfinService.GetNextUpAsync();
                        System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Got {items?.Count ?? 0} next up items");
                        break;

                    case "LatestMovies":
                        System.Diagnostics.Debug.WriteLine("LoadSpecialCollectionAsync: Fetching latest movies...");
                        items = await _jellyfinService.GetRecentlyAddedAsync(false);
                        System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Got {items?.Count ?? 0} latest movie items");
                        break;

                    case "LatestTV":
                        System.Diagnostics.Debug.WriteLine("LoadSpecialCollectionAsync: Fetching latest TV...");
                        items = await _jellyfinService.GetRecentlyAddedAsync(true);
                        System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Got {items?.Count ?? 0} latest TV items");
                        break;
                }

                // Store all items for virtualization and filtering
                _allItems = items ?? new List<MediaItem>();
                System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Total items to display: {_allItems.Count}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ContentItems.Clear();

                    // For special collections, load all items directly (they're already filtered by Jellyfin)
                    var itemsToLoad = _allItems.Take(INITIAL_LOAD_SIZE).ToList();
                    _loadedItemCount = itemsToLoad.Count;
                    _hasMoreItems = _allItems.Count > _loadedItemCount;

                    foreach (var item in itemsToLoad)
                    {
                        ContentItems.Add(item);
                    }

                    // Update command states
                    OnPropertyChanged(nameof(CanLoadMore));
                    
                    System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Added {ContentItems.Count} items to UI");
                });

                System.Diagnostics.Debug.WriteLine($"OptimizedMediaTabViewModel: Loaded {_allItems.Count} items for {collection.Name}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadSpecialCollectionAsync: Error loading {collection.Name}: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading {collection.Name}: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        // PERFORMANCE OPTIMIZATION: Remove artificial delays and batch processing
        private async Task LoadLibraryItemsOptimizedAsync(MediaItem library)
        {
            if (library == null) return;

            CurrentLoadingState = LoadingState.LoadingContent;
            ErrorMessage = null;

            try
            {
                var items = await _jellyfinService.GetLibraryItems(library.Id);

                // Store all items for virtualization
                _allItems = items.Where(item => 
                    (library.Type == MediaItemType.MovieLibrary && item.Type == MediaItemType.Movie) ||
                    (library.Type == MediaItemType.TVLibrary && item.Type == MediaItemType.Series)
                ).ToList();

                // Load initial batch only
                await LoadInitialBatchAsync(library);
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading items: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                if (CurrentLoadingState != LoadingState.Error)
                {
                    CurrentLoadingState = LoadingState.Idle;
                }
            }
        }

        private async Task LoadInitialBatchAsync(MediaItem library)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                library.Children?.Clear();
                ContentItems.Clear();

                var itemsToLoad = _allItems.Take(INITIAL_LOAD_SIZE).ToList();
                _loadedItemCount = itemsToLoad.Count;
                _hasMoreItems = _allItems.Count > _loadedItemCount;

                foreach (var item in itemsToLoad)
                {
                    if (library.Type == MediaItemType.TVLibrary && item.Type == MediaItemType.Series)
                    {
                        item.Children = new ObservableCollection<MediaItem>();
                        library.Children?.Add(item);
                    }
                    else if (library.Type == MediaItemType.MovieLibrary && item.Type == MediaItemType.Movie)
                    {
                        ContentItems.Add(item);
                    }
                }

                // Update command states
                OnPropertyChanged(nameof(CanLoadMore));
            });
        }

        private async Task LoadMoreItemsAsync()
        {
            if (!CanLoadMore) return;

            CurrentLoadingState = LoadingState.LoadingIncremental;

            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var itemsToLoad = _allItems
                        .Skip(_loadedItemCount)
                        .Take(INCREMENTAL_LOAD_SIZE)
                        .ToList();

                    foreach (var item in itemsToLoad)
                    {
                        if (_selectedNavigationItem?.Type == MediaItemType.TVLibrary && item.Type == MediaItemType.Series)
                        {
                            item.Children = new ObservableCollection<MediaItem>();
                            _selectedNavigationItem.Children?.Add(item);
                        }
                        else if (_selectedNavigationItem?.Type == MediaItemType.MovieLibrary && item.Type == MediaItemType.Movie)
                        {
                            ContentItems.Add(item);
                        }
                        else if (_selectedNavigationItem?.Type == MediaItemType.SpecialCollection)
                        {
                            // For special collections, add items directly to content view
                            ContentItems.Add(item);
                        }
                    }

                    _loadedItemCount += itemsToLoad.Count;
                    _hasMoreItems = _allItems.Count > _loadedItemCount;
                    OnPropertyChanged(nameof(CanLoadMore));
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        private async Task LoadSeriesSeasonsAsync(MediaItem series)
        {
            if (series == null) return;

            CurrentLoadingState = LoadingState.LoadingContent;
            ErrorMessage = null;
            ContentItems.Clear();

            try
            {
                var items = await _jellyfinService.GetItems(series.Id);
                
                // NO MORE ARTIFICIAL DELAYS - Direct UI update
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    series.Children?.Clear();
                    foreach (var item in items.Where(i => i.Type == MediaItemType.Season))
                    {
                        item.Children = new ObservableCollection<MediaItem>();
                        series.Children?.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading seasons: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        private async Task LoadSeasonEpisodesAsync(MediaItem season)
        {
            if (season == null) return;

            CurrentLoadingState = LoadingState.LoadingContent;
            ErrorMessage = null;

            try
            {
                var items = await _jellyfinService.GetItems(season.Id);
                
                // NO MORE ARTIFICIAL DELAYS - Direct UI update
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ContentItems.Clear();
                    foreach (var item in items.Where(i => i.Type == MediaItemType.Episode))
                    {
                        ContentItems.Add(item);
                    }
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error loading episodes: {ex.Message}";
                    CurrentLoadingState = LoadingState.Error;
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        private async Task ExpandItemAsync(MediaItem item)
        {
            if (item == null) return;

            switch (item.Type)
            {
                case MediaItemType.Series:
                    await LoadSeriesSeasonsAsync(item);
                    break;
                case MediaItemType.Season:
                    await LoadSeasonEpisodesAsync(item);
                    break;
            }
        }

        private async Task PlayItemAsync(MediaItem item)
        {
            if (item == null) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"PlayItemAsync: Starting playback for item: {item.Name} (ID: {item.Id})");
                
                // Ensure we're authenticated before trying to get stream URL
                var isAuthenticated = await _jellyfinService.Authenticate();
                if (!isAuthenticated)
                {
                    ErrorMessage = "Failed to authenticate with Jellyfin server";
                    System.Diagnostics.Debug.WriteLine("PlayItemAsync: Authentication failed");
                    return;
                }
                
                var streamUrl = _jellyfinService.GetStreamUrl(item.Id);
                System.Diagnostics.Debug.WriteLine($"PlayItemAsync: StreamUrl for {item.Name}: {streamUrl ?? "NULL"}");
                
                if (!string.IsNullOrEmpty(streamUrl))
                {
                    item.StreamUrl = streamUrl;
                }
                else
                {
                    ErrorMessage = $"Could not get stream URL for {item.Name}";
                    System.Diagnostics.Debug.WriteLine($"PlayItemAsync: StreamUrl is null for item {item.Id}");
                    return;
                }

                var track = new Track
                {
                    Id = item.Id,
                    Title = item.Name,
                    StreamUrl = item.StreamUrl,
                    Series = item.SeriesName,
                    Season = item.SeasonName,
                    Duration = item.Duration ?? TimeSpan.Zero,
                    IsVideo = item.Type == MediaItemType.Movie || item.Type == MediaItemType.Episode
                };

                System.Diagnostics.Debug.WriteLine($"PlayItemAsync: Created track - Title: {track.Title}, StreamUrl: {track.StreamUrl}, IsVideo: {track.IsVideo}, Duration: {track.Duration}");

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var mainWindow = Application.Current.MainWindow as IMediaWindow;
                    mainWindow?.MediaPlayerManager?.SetPlaylist(new[] { track });
                });

                // Mark as watched
                _ = _jellyfinService.MarkAsWatchedAsync(item.Id, true);
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error playing item: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"PlayItemAsync: Exception - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"PlayItemAsync: Stack trace - {ex.StackTrace}");
            }
        }

        private async Task ApplyFilterAsync()
        {
            if (string.IsNullOrWhiteSpace(FilterText))
            {
                if (SelectedNavigationItem != null)
                {
                    if (SelectedNavigationItem.Type == MediaItemType.SpecialCollection)
                    {
                        await LoadSpecialCollectionAsync(SelectedNavigationItem);
                    }
                    else
                    {
                        await LoadInitialBatchAsync(SelectedNavigationItem);
                    }
                }
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var filteredItems = _allItems
                    .Where(item => item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                                 (!string.IsNullOrEmpty(item.SeriesName) && item.SeriesName.Contains(FilterText, StringComparison.OrdinalIgnoreCase)))
                    .Take(INITIAL_LOAD_SIZE) // Limit filtered results for performance
                    .ToList();

                ContentItems.Clear();
                SelectedNavigationItem?.Children?.Clear();

                foreach (var item in filteredItems)
                {
                    if (SelectedNavigationItem?.Type == MediaItemType.TVLibrary && item.Type == MediaItemType.Series)
                    {
                        item.Children = new ObservableCollection<MediaItem>();
                        SelectedNavigationItem.Children?.Add(item);
                    }
                    else if (SelectedNavigationItem?.Type == MediaItemType.MovieLibrary && item.Type == MediaItemType.Movie)
                    {
                        ContentItems.Add(item);
                    }
                    else if (SelectedNavigationItem?.Type == MediaItemType.SpecialCollection)
                    {
                        // For special collections, add items directly to content view
                        ContentItems.Add(item);
                    }
                }
            });
        }

        private async Task DiagnoseCommandAsync()
        {
            try
            {
                CurrentLoadingState = LoadingState.LoadingContent;
                ErrorMessage = null;

                var diagnosticResults = await _jellyfinService.DiagnoseContinueWatchingAsync();
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.MessageBox.Show(
                        diagnosticResults,
                        "Continue Watching Diagnostics",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ErrorMessage = $"Error running diagnostics: {ex.Message}";
                });
            }
            finally
            {
                CurrentLoadingState = LoadingState.Idle;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 