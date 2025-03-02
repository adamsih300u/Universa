using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Cache;
using System.Linq;
using System.Windows.Data;
using System.Windows.Controls;
using Universa.Desktop.Commands;

namespace Universa.Desktop.ViewModels
{
    public class AudiobookTabViewModel : INotifyPropertyChanged
    {
        private readonly AudiobookshelfService _audiobookshelfService;
        private const int CACHE_STALE_HOURS = 1;
        private bool _isLoading;
        private string _errorMessage;
        private string _filterText;
        private ObservableCollection<AudiobookItem> _items;
        private AudiobookItem _selectedItem;
        private ICollectionView _filteredItems;
        private string _currentView = "Titles";
        private string _selectedSeries;
        private string _selectedLibraryId;

        public event PropertyChangedEventHandler PropertyChanged;

        public ICommand RefreshCommand { get; }
        public ICommand FilterCommand { get; }
        public ICommand PlayCommand { get; }

        public AudiobookItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }

        public ObservableCollection<AudiobookItem> Items
        {
            get => _items;
            set
            {
                _items = value;
                OnPropertyChanged();
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
            }
        }

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
                ApplyFilter();
            }
        }

        public ICollectionView FilteredItems
        {
            get => _filteredItems;
            private set
            {
                _filteredItems = value;
                OnPropertyChanged();
            }
        }

        public string SelectedLibraryId
        {
            get => _selectedLibraryId;
            set
            {
                _selectedLibraryId = value;
                OnPropertyChanged();
            }
        }

        public AudiobookTabViewModel(AudiobookshelfService audiobookshelfService)
        {
            _audiobookshelfService = audiobookshelfService;
            _items = new ObservableCollection<AudiobookItem>();
            FilteredItems = CollectionViewSource.GetDefaultView(Items);
            FilteredItems.Filter = FilterItems;

            RefreshCommand = new RelayCommand(async _ => await LoadItems(true));
            FilterCommand = new RelayCommand(_ => ApplyFilter());
            PlayCommand = new RelayCommand(_ => PlaySelectedItem());

            // Load items when the view model is created
            _ = LoadItems();
        }

        private async Task LoadItems(bool forceRefresh = false)
        {
            try
            {
                IsLoading = true;
                ErrorMessage = null;

                if (!forceRefresh)
                {
                    // Try to load from cache first
                    var cachedItems = AudiobookshelfCache.Instance.GetCachedItems(_selectedLibraryId);
                    if (cachedItems.Any())
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            _items.Clear();
                            foreach (var item in cachedItems)
                            {
                                _items.Add(item);
                            }
                        });

                        // If cache is old, refresh in background
                        if (AudiobookshelfCache.Instance.ShouldRefresh(_selectedLibraryId, TimeSpan.FromHours(CACHE_STALE_HOURS)))
                        {
                            await RefreshItems();
                        }
                        return;
                    }
                }

                await RefreshItems();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Error loading items: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RefreshItems()
        {
            var items = await _audiobookshelfService.GetLibraryContentsAsync(_selectedLibraryId);
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _items.Clear();
                foreach (var item in items)
                {
                    _items.Add(item);
                }
            });

            // Update the cache
            AudiobookshelfCache.Instance.UpdateCache(_selectedLibraryId, items);
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrWhiteSpace(FilterText))
            {
                // Reset to show all items
                _ = LoadItems();
                return;
            }

            var filteredItems = Items.Where(item =>
                item.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase) ||
                item.Author.Contains(FilterText, StringComparison.OrdinalIgnoreCase)
            ).ToList();

            Items = new ObservableCollection<AudiobookItem>(filteredItems);
        }

        private void PlaySelectedItem()
        {
            if (SelectedItem == null) return;

            // TODO: Implement playback through MediaPlayerManager
            // For now, just show a message
            MessageBox.Show($"Playing {SelectedItem.Title}", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public async Task RefreshLibraryAsync()
        {
            if (string.IsNullOrEmpty(SelectedLibraryId)) return;

            var items = await _audiobookshelfService.GetLibraryContentsAsync(SelectedLibraryId);
            Items = new ObservableCollection<AudiobookItem>(items);
            FilteredItems = CollectionViewSource.GetDefaultView(Items);
            FilteredItems.Filter = FilterItems;
            FilteredItems.Refresh();
        }

        public void ShowAuthorsView()
        {
            _currentView = "Authors";
            var view = CollectionViewSource.GetDefaultView(Items);
            view.GroupDescriptions.Clear();
            view.GroupDescriptions.Add(new PropertyGroupDescription("Author"));
            FilteredItems = view;
            FilteredItems.Filter = FilterItems;
            FilteredItems.Refresh();
        }

        public void ShowTitlesView()
        {
            _currentView = "Titles";
            var view = CollectionViewSource.GetDefaultView(Items);
            view.GroupDescriptions.Clear();
            FilteredItems = view;
            FilteredItems.Filter = FilterItems;
            FilteredItems.Refresh();
        }

        public void LoadSeries()
        {
            var seriesList = Items
                .Where(i => !string.IsNullOrEmpty(i.Series))
                .Select(i => i.Series)
                .Distinct()
                .OrderBy(s => s)
                .ToList();

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow.FindName("AudiobookshelfTab") is Views.AudiobookshelfTab tab)
                {
                    var seriesNode = tab.FindName("SeriesNode") as TreeViewItem;
                    if (seriesNode != null)
                    {
                        seriesNode.Items.Clear();
                        foreach (var series in seriesList)
                        {
                            seriesNode.Items.Add(new TreeViewItem 
                            { 
                                Header = series,
                                Tag = series 
                            });
                        }
                    }
                }
            });
        }

        public void ShowSeriesBooks(string series)
        {
            _currentView = "Series";
            _selectedSeries = series;
            FilteredItems.Refresh();
        }

        private bool FilterItems(object item)
        {
            if (!(item is AudiobookItem audiobook)) return false;

            switch (_currentView)
            {
                case "Authors":
                    return true; // Show all items, will be grouped by author
                case "Titles":
                    return true; // Show all items
                case "Series":
                    return !string.IsNullOrEmpty(_selectedSeries) && 
                           audiobook.Series == _selectedSeries;
                default:
                    return true;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 