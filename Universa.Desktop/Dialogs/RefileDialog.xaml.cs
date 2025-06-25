using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.Dialogs
{
    public partial class RefileDialog : Window, INotifyPropertyChanged
    {
        private readonly OrgRefileService _refileService;
        private readonly OrgItem _itemToRefile;
        private readonly IOrgModeService _sourceService;
        private List<RefileTarget> _allTargets;
        private List<RefileTarget> _filteredTargets;
        private string _searchQuery;
        private bool _isLoading;

        public event PropertyChangedEventHandler PropertyChanged;

        public RefileTarget SelectedTarget { get; private set; }
        public bool DialogResult { get; private set; }

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (_searchQuery != value)
                {
                    _searchQuery = value;
                    OnPropertyChanged();
                    _ = Task.Run(FilterTargetsAsync);
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
                    
                    Dispatcher.Invoke(() =>
                    {
                        LoadingGrid.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
                    });
                }
            }
        }

        public RefileDialog(OrgItem itemToRefile, IOrgModeService sourceService, IConfigurationService configService, GlobalOrgAgendaService globalAgendaService)
        {
            InitializeComponent();
            DataContext = this;

            _itemToRefile = itemToRefile;
            _sourceService = sourceService;
            _refileService = new OrgRefileService(configService, globalAgendaService);
            _allTargets = new List<RefileTarget>();
            _filteredTargets = new List<RefileTarget>();

            // Setup UI
            ItemTitleText.Text = $"Refiling: {_itemToRefile?.Title ?? "Unknown Item"}";
            
            // Load data asynchronously
            Loaded += async (s, e) => await LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            IsLoading = true;
            
            try
            {
                // Load quick targets
                await LoadQuickTargetsAsync();
                
                // Load all targets
                _allTargets = await _refileService.GetRefileTargetsAsync();
                
                // Filter out the item we're refiling and its descendants
                _allTargets = _allTargets.Where(target => 
                    target.Item?.Id != _itemToRefile.Id && 
                    !IsDescendantOfItem(target.Item, _itemToRefile)).ToList();
                
                // Apply initial filter
                await FilterTargetsAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading refile targets: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadQuickTargetsAsync()
        {
            try
            {
                var quickTargets = await _refileService.GetQuickRefileTargetsAsync();
                
                Dispatcher.Invoke(() =>
                {
                    QuickTargetsPanel.Children.Clear();
                    
                    foreach (var quickTarget in quickTargets)
                    {
                        var button = new Button
                        {
                            Content = quickTarget.Name,
                            Style = (Style)FindResource("QuickButtonStyle"),
                            Tag = quickTarget.Target
                        };
                        button.Click += QuickTarget_Click;
                        QuickTargetsPanel.Children.Add(button);
                    }
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading quick targets: {ex.Message}");
            }
        }

        private async Task FilterTargetsAsync()
        {
            await Task.Run(() =>
            {
                // Safety check - if _allTargets is null, create empty list
                if (_allTargets == null)
                {
                    _allTargets = new List<RefileTarget>();
                }
                
                var filtered = _allTargets.AsEnumerable();

                // Apply search filter
                if (!string.IsNullOrWhiteSpace(SearchQuery))
                {
                    var query = SearchQuery.ToLower();
                    filtered = filtered.Where(target =>
                        target.DisplayPath.ToLower().Contains(query) ||
                        (target.Item?.Title?.ToLower().Contains(query) ?? false) ||
                        System.IO.Path.GetFileName(target.FilePath).ToLower().Contains(query));
                }

                // Apply type filters
                Dispatcher.Invoke(() =>
                {
                    if (ShowFilesCheckBox.IsChecked != true)
                    {
                        filtered = filtered.Where(t => t.Type != RefileTargetType.File);
                    }
                    
                    if (ShowProjectsCheckBox.IsChecked != true)
                    {
                        filtered = filtered.Where(t => t.Type != RefileTargetType.Item || 
                                                      !(t.Item?.IsProject ?? false));
                    }
                    
                    if (ShowHeadingsCheckBox.IsChecked != true)
                    {
                        filtered = filtered.Where(t => t.Type != RefileTargetType.Item || 
                                                      (t.Item?.IsProject ?? false));
                    }
                });

                _filteredTargets = filtered.OrderBy(t => t.Type)
                                          .ThenBy(t => t.FilePath)
                                          .ThenBy(t => t.Level)
                                          .ThenBy(t => t.DisplayPath)
                                          .ToList();

                Dispatcher.Invoke(() =>
                {
                    TargetsListBox.ItemsSource = _filteredTargets;
                });
            });
        }

        private bool IsDescendantOfItem(OrgItem potential, OrgItem ancestor)
        {
            if (potential == null || ancestor == null)
                return false;

            var current = potential.Parent;
            while (current != null)
            {
                if (current.Id == ancestor.Id)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        #region Event Handlers

        private async void QuickTarget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is RefileTarget target)
            {
                SelectedTarget = target;
                await PerformRefileAsync();
            }
        }

        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Filtering is handled by the SearchQuery property
        }

        private void TargetsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefileButton.IsEnabled = TargetsListBox.SelectedItem != null;
        }

        private async void TargetsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TargetsListBox.SelectedItem is RefileTarget target)
            {
                SelectedTarget = target;
                await PerformRefileAsync();
            }
        }

        private async void FilterCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            await FilterTargetsAsync();
        }

        private async void ShowRecent_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                IsLoading = true;
                var recentTargets = await _refileService.GetRecentRefileTargetsAsync();
                
                Dispatcher.Invoke(() =>
                {
                    TargetsListBox.ItemsSource = recentTargets;
                    SearchTextBox.Text = "";
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading recent targets: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void ShowAll_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Text = "";
            await FilterTargetsAsync();
        }

        private async void Refile_Click(object sender, RoutedEventArgs e)
        {
            if (TargetsListBox.SelectedItem is RefileTarget target)
            {
                SelectedTarget = target;
                await PerformRefileAsync();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        private async Task PerformRefileAsync()
        {
            if (SelectedTarget == null)
                return;

            try
            {
                IsLoading = true;
                
                var success = await _refileService.RefileItemAsync(_itemToRefile, _sourceService, SelectedTarget);
                
                if (success)
                {
                    DialogResult = true;
                    
                    // Refresh any open tabs that might be showing the affected files
                    Dispatcher.Invoke(async () =>
                    {
                        var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                        if (mainWindow != null)
                        {
                            // Small delay to ensure file save operations have completed
                            await Task.Delay(100);
                            
                            System.Diagnostics.Debug.WriteLine($"About to refresh tabs - Source: {_sourceService.FilePath}, Target: {SelectedTarget.FilePath}");
                            
                            // Refresh source file tab (item was removed)
                            mainWindow.RefreshOpenFileTab(_sourceService.FilePath);
                            
                            // Refresh target file tab (item was added)
                            mainWindow.RefreshOpenFileTab(SelectedTarget.FilePath);
                            
                            System.Diagnostics.Debug.WriteLine($"Triggered refresh for source: {_sourceService.FilePath} and target: {SelectedTarget.FilePath}");
                        }
                        
                        MessageBox.Show(
                            $"Successfully refiled '{_itemToRefile.Title}' to {SelectedTarget.DisplayPath}", 
                            "Refile Complete", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                        Close();
                    });
                }
                else
                {
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            "Failed to refile item. Please try again.", 
                            "Refile Error", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Error);
                    });
                }
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error during refile: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 