using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Cache;
using System.Threading.Tasks;
using System.Linq;
using System.Windows.Threading;

namespace Universa.Desktop.Views
{
    public partial class AudiobookshelfTab : UserControl
    {
        private readonly IAudiobookshelfService _service;
        private List<AudiobookshelfLibraryResponse> _libraries;
        private string _currentLibraryId;
        private Dictionary<string, double> _progress;
        private List<AudiobookItem> _currentLibraryItems;
        private Dictionary<string, List<AudiobookItem>> _seriesCache;
        private Dictionary<string, List<AudiobookItem>> _authorCache;
        private List<AudiobookItem> _inProgressCache;
        private static readonly TimeSpan CacheMaxAge = TimeSpan.FromHours(1);

        public AudiobookshelfTab(IAudiobookshelfService service)
        {
            InitializeComponent();
            _service = service;
            _seriesCache = new Dictionary<string, List<AudiobookItem>>();
            _authorCache = new Dictionary<string, List<AudiobookItem>>();
            Loaded += AudiobookshelfTab_Loaded;
            
            // Initialize the navigation tree
            var authorsNode = new TreeViewItem { Header = "Authors", Tag = "Authors" };
            var seriesNode = new TreeViewItem { Header = "Series", Tag = "Series" };
            var continueReadingNode = new TreeViewItem { Header = "Continue Reading", Tag = "ContinueReading" };
            var titlesNode = new TreeViewItem { Header = "Titles", Tag = "Titles" };
            
            NavigationTree.Items.Add(continueReadingNode);
            NavigationTree.Items.Add(authorsNode);
            NavigationTree.Items.Add(seriesNode);
            NavigationTree.Items.Add(titlesNode);

            // Add click handler for items
            ItemList.MouseDoubleClick += ItemList_MouseDoubleClick;
        }

        private void UpdateCaches()
        {
            if (_currentLibraryItems == null) return;

            // Create new dictionaries to avoid concurrent modification
            var newSeriesCache = new Dictionary<string, List<AudiobookItem>>();
            var newAuthorCache = new Dictionary<string, List<AudiobookItem>>();
            var itemsList = _currentLibraryItems.ToList(); // Create a snapshot of the items

            // Update series cache
            foreach (var item in itemsList.Where(i => !string.IsNullOrEmpty(i.Series)))
            {
                if (!newSeriesCache.ContainsKey(item.Series))
                {
                    newSeriesCache[item.Series] = new List<AudiobookItem>();
                }
                newSeriesCache[item.Series].Add(item);
            }

            // Update author cache
            foreach (var item in itemsList.Where(i => !string.IsNullOrEmpty(i.Author)))
            {
                if (!newAuthorCache.ContainsKey(item.Author))
                {
                    newAuthorCache[item.Author] = new List<AudiobookItem>();
                }
                newAuthorCache[item.Author].Add(item);
            }

            // Update in-progress cache
            var newInProgressCache = itemsList
                .Where(i => _progress?.TryGetValue(i.Id, out double progress) == true && progress > 0 && progress < 100)
                .OrderByDescending(i => _progress[i.Id])
                .ToList();

            // Atomic assignments of the new caches
            _seriesCache = newSeriesCache;
            _authorCache = newAuthorCache;
            _inProgressCache = newInProgressCache;

            System.Diagnostics.Debug.WriteLine($"Cache updated - Series: {_seriesCache.Count}, Authors: {_authorCache.Count}, In Progress: {_inProgressCache.Count}");
        }

        private async Task LoadAuthorsNode()
        {
            if (_currentLibraryItems == null) return;

            var authorsNode = NavigationTree.Items.Cast<TreeViewItem>()
                .FirstOrDefault(i => i.Tag?.ToString() == "Authors");
            if (authorsNode == null) return;

            authorsNode.Items.Clear();
            var authors = _authorCache.Keys.OrderBy(a => a);

            foreach (var author in authors)
            {
                var authorNode = new TreeViewItem
                {
                    Header = author,
                    Tag = $"Author:{author}"
                };
                authorsNode.Items.Add(authorNode);
            }
            System.Diagnostics.Debug.WriteLine($"Loaded {authorsNode.Items.Count} authors into navigation");
        }

        private async Task LoadSeriesNode()
        {
            if (_currentLibraryItems == null)
            {
                System.Diagnostics.Debug.WriteLine("LoadSeriesNode: _currentLibraryItems is null");
                return;
            }

            var seriesNode = NavigationTree.Items.Cast<TreeViewItem>()
                .FirstOrDefault(i => i.Tag?.ToString() == "Series");
            if (seriesNode == null)
            {
                System.Diagnostics.Debug.WriteLine("LoadSeriesNode: Series node not found in navigation tree");
                return;
            }

            seriesNode.Items.Clear();

            // For podcasts library, each podcast is treated as a series
            if (_currentLibraryItems.Any(i => i.Type == AudiobookItemType.Podcast))
            {
                var podcasts = _currentLibraryItems
                    .Where(i => i.Type == AudiobookItemType.Podcast)
                    .OrderBy(i => i.Title);

                foreach (var podcast in podcasts)
                {
                    var podcastNode = new TreeViewItem
                    {
                        Header = podcast.Title,
                        Tag = $"Podcast:{podcast.Id}"
                    };
                    seriesNode.Items.Add(podcastNode);
                    System.Diagnostics.Debug.WriteLine($"Added podcast: {podcast.Title}");
                }
                System.Diagnostics.Debug.WriteLine($"Loaded {seriesNode.Items.Count} podcasts into navigation");
            }
            else
            {
                // Regular audiobook series handling
                var series = _seriesCache.Keys.OrderBy(s => s);
                foreach (var seriesName in series)
                {
                    var seriesItem = new TreeViewItem
                    {
                        Header = seriesName,
                        Tag = $"Series:{seriesName}"
                    };
                    seriesNode.Items.Add(seriesItem);
                    System.Diagnostics.Debug.WriteLine($"Added series: {seriesName}");
                }
                System.Diagnostics.Debug.WriteLine($"Loaded {seriesNode.Items.Count} series into navigation");
            }
        }

        private async void AudiobookshelfTab_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshLibraries();
            await RefreshProgress();
        }

        private async Task RefreshProgress()
        {
            _progress = await _service.GetUserProgressAsync();
        }

        private async Task RefreshLibraries()
        {
            try
            {
                _libraries = await _service.GetLibrariesAsync();
                LibrarySelector.ItemsSource = _libraries;
                if (_libraries.Any())
                {
                    LibrarySelector.SelectedIndex = 0;
                    await LoadLibraryItems(_libraries[0].Id);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading libraries: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadLibraryItems(string libraryId)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LoadLibraryItems: Starting load for library {libraryId}");
                
                LoadingText.Text = "Loading library items...";
                LoadingOverlay.Visibility = Visibility.Visible;

                // Run the heavy operations in a background task
                var items = await Task.Run(async () =>
                {
                    // Force refresh from server to get complete metadata
                    var result = await _service.GetLibraryContentsAsync(libraryId);
                    if (result != null && result.Count > 0)
                    {
                        // Update progress for items
                        foreach (var item in result)
                        {
                            if (_progress?.TryGetValue(item.Id, out double progress) == true)
                            {
                                item.Progress = progress;
                            }
                        }
                    }
                    return result;
                });

                if (items != null && items.Count > 0)
                {
                    // Atomic assignment of items
                    _currentLibraryItems = new List<AudiobookItem>(items);
                    
                    // Update caches in background
                    await Task.Run(() => 
                    {
                        UpdateCaches();
                        AudiobookshelfCache.Instance.UpdateCache(libraryId, items);
                    });
                    
                    // Update UI
                    await Dispatcher.InvokeAsync(() =>
                    {
                        UpdateItemsSource(items);
                        LoadAuthorsNode();
                        LoadSeriesNode();
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] LoadLibraryItems ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error loading library items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async Task RefreshLibraryItems(string libraryId)
        {
            try
            {
                LoadingText.Text = "Refreshing library items...";
                LoadingOverlay.Visibility = Visibility.Visible;

                System.Diagnostics.Debug.WriteLine($"RefreshLibraryItems: Starting refresh for library {libraryId}");
                var items = await Task.Run(async () => await _service.GetLibraryContentsAsync(libraryId));
                System.Diagnostics.Debug.WriteLine($"RefreshLibraryItems: Got {items?.Count ?? 0} items from server");
                
                if (items == null || items.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("RefreshLibraryItems: No items returned from server");
                    return;
                }

                _currentLibraryItems = items;
                
                // Update progress and cache in background
                await Task.Run(() =>
                {
                    // Update progress for items
                    foreach (var item in items)
                    {
                        if (_progress?.TryGetValue(item.Id, out double progress) == true)
                        {
                            item.Progress = progress;
                        }
                    }
                    
                    // Update cache
                    System.Diagnostics.Debug.WriteLine($"RefreshLibraryItems: Updating cache with {items.Count} items");
                    AudiobookshelfCache.Instance.UpdateCache(libraryId, items);
                });
                
                // Update UI
                await Dispatcher.InvokeAsync(() =>
                {
                    UpdateItemsSource(items);
                    LoadAuthorsNode();
                    LoadSeriesNode();
                });

                System.Diagnostics.Debug.WriteLine("RefreshLibraryItems: Refresh complete");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during refresh: {ex.Message}");
                MessageBox.Show($"Error refreshing data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateItemsSource(List<AudiobookItem> items)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateItemsSource: Updating UI with {items?.Count ?? 0} items");
            ItemList.ItemsSource = items;
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadingText.Text = "Refreshing data...";
                LoadingOverlay.Visibility = Visibility.Visible;
                RefreshButton.IsEnabled = false;

                System.Diagnostics.Debug.WriteLine("Manual refresh requested - forcing cache refresh");
                await Task.Run(async () => _progress = await _service.GetUserProgressAsync());

                if (_currentLibraryId != null)
                {
                    await RefreshLibraryItems(_currentLibraryId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error during manual refresh: {ex.Message}");
                MessageBox.Show($"Error refreshing data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void NavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item)
            {
                var tag = item.Tag?.ToString() ?? "";
                System.Diagnostics.Debug.WriteLine($"Navigation selection changed to: {tag}");

                if (tag.StartsWith("Author:"))
                {
                    var authorName = tag.Substring("Author:".Length);
                    if (_authorCache.TryGetValue(authorName, out var authorBooks))
                    {
                        ItemList.ItemsSource = authorBooks.OrderBy(i => i.Title).ToList();
                    }
                }
                else if (tag.StartsWith("Series:"))
                {
                    var seriesName = tag.Substring("Series:".Length);
                    System.Diagnostics.Debug.WriteLine($"Loading books for series: {seriesName}");
                    if (_seriesCache.TryGetValue(seriesName, out var seriesBooks))
                    {
                        var orderedBooks = seriesBooks
                            .OrderBy(i => 
                            {
                                if (double.TryParse(i.SeriesSequence?.Split('.')[0], out double seq))
                                    return seq;
                                return double.MaxValue;
                            })
                            .ThenBy(i => i.Title)
                            .ToList();
                        System.Diagnostics.Debug.WriteLine($"Found {orderedBooks.Count} books in series {seriesName}");
                        ItemList.ItemsSource = orderedBooks;
                    }
                }
                else if (tag.StartsWith("Podcast:"))
                {
                    var podcastId = tag.Substring("Podcast:".Length);
                    System.Diagnostics.Debug.WriteLine($"Loading episodes for podcast: {podcastId}");
                    var episodes = await _service.GetPodcastEpisodesAsync(podcastId);
                    System.Diagnostics.Debug.WriteLine($"Loaded {episodes.Count} episodes");
                    var sortedEpisodes = episodes.OrderByDescending(e => e.PublishedAt).ToList();
                    ItemList.ItemsSource = sortedEpisodes;
                }
                else switch (tag)
                {
                    case "ContinueReading":
                        System.Diagnostics.Debug.WriteLine("Loading in-progress items");
                        var inProgressItems = await _service.GetInProgressItemsAsync(_currentLibraryId);
                        System.Diagnostics.Debug.WriteLine($"Found {inProgressItems.Count} in-progress items");
                        ItemList.ItemsSource = inProgressItems;
                        break;
                        
                    case "Authors":
                        await LoadAuthorsNode();
                        break;
                        
                    case "Series":
                        await LoadSeriesNode();
                        break;
                        
                    case "Titles":
                        ItemList.ItemsSource = _currentLibraryItems?.OrderBy(i => i.Title).ToList();
                        break;
                }
            }
        }

        private async void LibrarySelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LibrarySelector.SelectedItem is AudiobookshelfLibraryResponse library)
            {
                _currentLibraryId = library.Id;
                await LoadLibraryItems(library.Id);
            }
        }

        private void ItemList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ItemList.SelectedItem is AudiobookItem selectedItem)
            {
                System.Diagnostics.Debug.WriteLine($"Starting playback for: {selectedItem.Title}");
                MainWindow.Instance.MediaControlBar.StartPlayback(_service, selectedItem);
            }
        }

        private void ItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Handle selection change if needed
        }
    }
} 