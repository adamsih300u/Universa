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
using Universa.Desktop.Library;
using Universa.Desktop.Models;
using Universa.Desktop.Cache;
using Universa.Desktop.Commands;
using Universa.Desktop.Services;
using Universa.Desktop.Windows;

namespace Universa.Desktop.ViewModels
{
    public class MediaTabViewModel : INotifyPropertyChanged
    {
        private readonly JellyfinService _jellyfinService;
        private readonly JellyfinMediaCache _cache;
        private const int CACHE_STALE_HOURS = 1;
        private CancellationTokenSource _loadingCancellation;
        private const int BATCH_SIZE = 50;
        private const int BATCH_DELAY_MS = 50;

        public enum LoadingState
        {
            Idle,
            LoadingLibrary,
            LoadingContent,
            Error
        }

        private ObservableCollection<MediaItem> _contentItems;
        private ObservableCollection<MediaItem> _navigationItems;
        private MediaItem _selectedNavigationItem;
        private LoadingState _currentLoadingState;
        private string _errorMessage;
        private string _filterText;
        private bool _isInitialized;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand NavigateToItemCommand { get; }
        public ICommand PlayItemCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand ExpandItemCommand { get; }

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
                HandleNavigationSelectionAsync(value).ConfigureAwait(false);
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
            }
        }

        public bool IsLoading => CurrentLoadingState == LoadingState.LoadingLibrary || 
                               CurrentLoadingState == LoadingState.LoadingContent;

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
                ApplyFilterAsync().ConfigureAwait(false);
            }
        }

        public MediaTabViewModel(JellyfinService jellyfinService)
        {
            _jellyfinService = jellyfinService;
            _cache = JellyfinMediaCache.Instance;
            ContentItems = new ObservableCollection<MediaItem>();
            NavigationItems = new ObservableCollection<MediaItem>();
            
            RefreshCommand = new RelayCommand(_ => LoadContentWithRetryAsync(true).ConfigureAwait(false));
            NavigateToItemCommand = new RelayCommand<MediaItem>(item => HandleNavigationSelectionAsync(item).ConfigureAwait(false));
            PlayItemCommand = new RelayCommand<MediaItem>(item => PlayItemAsync(item).ConfigureAwait(false));
            FilterCommand = new RelayCommand(_ => ApplyFilterAsync().ConfigureAwait(false));
            ExpandItemCommand = new RelayCommand<MediaItem>(item => ExpandItemAsync(item).ConfigureAwait(false));

            // Initial load
            InitializeAsync().ConfigureAwait(false);
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
                    await Task.Delay(1000 * currentRetry); // Exponential backoff
                }
            }
        }

        private async Task LoadContentAsync(bool forceRefresh = false)
        {
            if (_loadingCancellation != null)
            {
                _loadingCancellation.Cancel();
                _loadingCancellation = new CancellationTokenSource();
            }

            CurrentLoadingState = LoadingState.LoadingLibrary;
            ErrorMessage = null;

            try
            {
                if (forceRefresh)
                {
                    _cache.ClearCache();
                }

                var libraries = await _jellyfinService.GetMediaLibraryAsync();
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    NavigationItems.Clear();
                    ContentItems.Clear();
                    foreach (var library in libraries)
                    {
                        // Only add libraries to the navigation tree
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

        private async Task HandleNavigationSelectionAsync(MediaItem item)
        {
            if (item == null) return;

            switch (item.Type)
            {
                case MediaItemType.MovieLibrary:
                case MediaItemType.TVLibrary:
                    await LoadLibraryItemsAsync(item);
                    break;
                case MediaItemType.Movie:
                    ContentItems.Clear();
                    ContentItems.Add(item);
                    break;
                case MediaItemType.Series:
                    await LoadSeriesSeasonsAsync(item);
                    break;
                case MediaItemType.Season:
                    await LoadSeasonEpisodesAsync(item);
                    break;
                case MediaItemType.Episode:
                    ContentItems.Clear();
                    ContentItems.Add(item);
                    break;
            }
        }

        private async Task LoadLibraryItemsAsync(MediaItem library)
        {
            if (library == null) return;

            CurrentLoadingState = LoadingState.LoadingContent;
            ErrorMessage = null;
            ContentItems.Clear();

            try
            {
                // Check cache first
                var maxAge = TimeSpan.FromHours(CACHE_STALE_HOURS);
                var cachedItems = _cache.IsLibraryCacheValid(library.Id, maxAge) 
                    ? await _cache.GetLibraryItemsAsync(library.Id)
                    : null;

                List<MediaItem> items;
                if (cachedItems != null)
                {
                    items = cachedItems;
                    System.Diagnostics.Debug.WriteLine($"Loaded {items.Count} items from cache for library {library.Name}");
                }
                else
                {
                    try
                    {
                        items = await _jellyfinService.GetLibraryItems(library.Id);
                        _cache.UpdateLibraryItems(library.Id, items);
                        System.Diagnostics.Debug.WriteLine($"Loaded {items.Count} items from server for library {library.Name}");
                    }
                    catch (TaskCanceledException)
                    {
                        // If server request times out, try to use any available cached data
                        items = await _cache.GetLibraryItemsAsync(library.Id) ?? new List<MediaItem>();
                        System.Diagnostics.Debug.WriteLine($"Server request timed out, using {items.Count} cached items for library {library.Name}");
                        
                        // Show a warning to the user
                        ErrorMessage = "Server request timed out. Showing cached data which may be outdated.";
                        CurrentLoadingState = LoadingState.Idle;
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    library.Children.Clear();
                    ContentItems.Clear();

                    var processedCount = 0;
                    foreach (var batch in items.Chunk(BATCH_SIZE))
                    {
                        foreach (var item in batch)
                        {
                            if (library.Type == MediaItemType.MovieLibrary)
                            {
                                if (item.Type == MediaItemType.Movie)
                                {
                                    ContentItems.Add(item);
                                    processedCount++;
                                }
                            }
                            else if (library.Type == MediaItemType.TVLibrary)
                            {
                                if (item.Type == MediaItemType.Series)
                                {
                                    item.Children = new ObservableCollection<MediaItem>();
                                    library.Children.Add(item);
                                    processedCount++;
                                }
                            }
                        }
                        
                        // Update progress message
                        if (items.Count > 0)
                        {
                            ErrorMessage = $"Loading items... ({processedCount}/{items.Count})";
                        }
                        
                        Thread.Sleep(BATCH_DELAY_MS);
                    }
                });
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
                CurrentLoadingState = LoadingState.Idle;
                ErrorMessage = null;
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
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    series.Children.Clear();
                    foreach (var batch in items.Chunk(BATCH_SIZE))
                    {
                        foreach (var item in batch)
                        {
                            if (item.Type == MediaItemType.Season)
                            {
                                item.Children = new ObservableCollection<MediaItem>();
                                series.Children.Add(item);
                            }
                        }
                        Thread.Sleep(BATCH_DELAY_MS);
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
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ContentItems.Clear();
                    foreach (var batch in items.Chunk(BATCH_SIZE))
                    {
                        foreach (var item in batch)
                        {
                            if (item.Type == MediaItemType.Episode)
                            {
                                ContentItems.Add(item);
                            }
                        }
                        Thread.Sleep(BATCH_DELAY_MS);
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

            var track = new Track
            {
                Id = item.Id,
                Title = item.Name,
                StreamUrl = item.StreamUrl,
                Series = item.SeriesName,
                Season = item.SeasonName,
                IsVideo = item.Type == MediaItemType.Movie || item.Type == MediaItemType.Episode
            };

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var mainWindow = Application.Current.MainWindow as IMediaWindow;
                if (mainWindow?.MediaPlayerManager != null)
                {
                    mainWindow.MediaPlayerManager.SetPlaylist(new[] { track });
                }
            });
        }

        private async Task ApplyFilterAsync()
        {
            if (string.IsNullOrWhiteSpace(FilterText))
            {
                if (SelectedNavigationItem != null)
                {
                    await HandleNavigationSelectionAsync(SelectedNavigationItem);
                }
                return;
            }

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var filteredItems = ContentItems
                    .Where(item => item.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                ContentItems.Clear();
                foreach (var batch in filteredItems.Chunk(BATCH_SIZE))
                {
                    foreach (var item in batch)
                    {
                        ContentItems.Add(item);
                    }
                    Thread.Sleep(BATCH_DELAY_MS);
                }
            });
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void CleanupUnusedResources()
        {
            GC.Collect();
        }
    }
} 