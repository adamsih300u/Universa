using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Views;

namespace Universa.Desktop.Tabs
{
    public partial class GlobalAgendaTab : UserControl, IFileTab, INotifyPropertyChanged
    {
        public int LastKnownCursorPosition { get; private set; } = 0;
        
        private readonly GlobalOrgAgendaService _globalAgendaService;
        private readonly IConfigurationService _configService;
        private bool _isLoading;
        private ObservableCollection<GlobalAgendaDay> _allDays;
        private DateTime _lastUpdated;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath 
        { 
            get => "Global Agenda";
            set { } // No-op as this is a virtual file
        }
        
        public string Title 
        { 
            get => "🗓️ Global Agenda";
            set { } // No-op as this is a virtual file
        }
        
        public bool IsModified 
        { 
            get => false;
            set { } // No-op as this tab is never modified
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

        public ObservableCollection<GlobalAgendaDay> AllDays
        {
            get => _allDays ??= new ObservableCollection<GlobalAgendaDay>();
            set
            {
                if (_allDays != value)
                {
                    _allDays = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasItems));
                    OnPropertyChanged(nameof(TotalItemCount));
                    OnPropertyChanged(nameof(OverdueCount));
                    OnPropertyChanged(nameof(ActionRequiredCount));
                }
            }
        }

        public DateTime LastUpdated
        {
            get => _lastUpdated;
            set
            {
                if (_lastUpdated != value)
                {
                    _lastUpdated = value;
                    OnPropertyChanged();
                }
            }
        }

        // Computed Properties
        public bool HasItems => AllDays.Any();
        public bool HasNoItems => !IsLoading && !HasItems;
        
        public string StatusText
        {
            get
            {
                if (IsLoading) return "Loading agenda items...";
                if (!_configService.Provider.EnableGlobalAgenda) return "Global agenda is disabled";
                
                var fileCount = _configService.Provider.OrgAgendaFiles.Length + _configService.Provider.OrgAgendaDirectories.Length;
                return $"Scanning {fileCount} configured sources";
            }
        }

        public string ConfiguredFilesText
        {
            get
            {
                var files = _configService.Provider.OrgAgendaFiles.Length;
                var dirs = _configService.Provider.OrgAgendaDirectories.Length;
                return $"Files: {files}, Directories: {dirs}";
            }
        }

        public int TotalItemCount => AllDays.Sum(d => d.Items.Count);
        
        public int OverdueCount 
        {
            get
            {
                var overdueDay = AllDays.FirstOrDefault(d => d.DateHeader.Contains("OVERDUE"));
                return overdueDay?.Items.Count ?? 0;
            }
        }
        
        public int ActionRequiredCount
        {
            get
            {
                var stateConfig = _globalAgendaService.GetStateConfiguration();
                return AllDays.SelectMany(d => d.Items)
                    .Count(i => stateConfig.RequiresAction(i.Item.State.ToString()));
            }
        }

        public GlobalAgendaTab(IConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;
            _globalAgendaService = new GlobalOrgAgendaService(configService);
            
            DataContext = this;

            // Subscribe to service events
            _globalAgendaService.ItemsChanged += OnItemsChanged;
            
            // Subscribe to configuration changes for auto-refresh
            _configService.Provider.ConfigurationChanged += OnConfigurationChanged;

            // Load initial data with force refresh
            _ = Task.Run(async () =>
            {
                await _globalAgendaService.ForceRefreshAsync();
                await LoadAgendaAsync();
            });
        }

        private async Task LoadAgendaAsync()
        {
            if (!_configService.Provider.EnableGlobalAgenda)
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(HasNoItems));
                return;
            }

            IsLoading = true;

            try
            {
                                                        // Load data from global agenda service
                var daysAhead = _configService.Provider.AgendaDaysAhead;
                var allDays = await _globalAgendaService.GetAllDaysAsync(daysAhead);

                System.Diagnostics.Debug.WriteLine($"LoadAgendaAsync: UI Update - AllDays={allDays.Count} days");

                // Update UI on main thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AllDays = new ObservableCollection<GlobalAgendaDay>(allDays);
                    LastUpdated = DateTime.Now;
                    
                    UpdateAllProperties();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show($"Error loading global agenda: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                });
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnItemsChanged(object sender, EventArgs e)
        {
            // Just reload the UI with current data - don't trigger another refresh
            _ = Task.Run(async () =>
            {
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        // Use cached data to update UI - do NOT call GetAllItemsAsync to avoid loops
                        var allDays = await _globalAgendaService.GetAllDaysAsync();

                        AllDays = new ObservableCollection<GlobalAgendaDay>(allDays);
                        LastUpdated = DateTime.Now;
                        
                        UpdateAllProperties();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error in OnItemsChanged: {ex.Message}");
                    }
                });
            });
        }

        private async void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is OrgItemWithSource itemWithSource)
            {
                try
                {
                    var stateConfig = _globalAgendaService.GetStateConfiguration();
                    var currentState = itemWithSource.Item.State.ToString();
                    var nextState = stateConfig.GetNextState(currentState);
                    
                    System.Diagnostics.Debug.WriteLine($"StateButton_Click: Changing '{itemWithSource.Item.Title}' from {currentState} to {nextState?.Name}");
                    
                    if (nextState != null)
                    {
                        var success = await _globalAgendaService.UpdateItemStateAsync(itemWithSource, nextState.Name);
                        if (success)
                        {
                            System.Diagnostics.Debug.WriteLine($"StateButton_Click: Successfully updated state, refreshing UI");
                            
                            // Just refresh the UI without forcing a full file reload
                            await LoadAgendaAsync();
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"StateButton_Click: Failed to update state");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"StateButton_Click: Error: {ex.Message}");
                    MessageBox.Show($"Error updating item state: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e)
        {
            await _globalAgendaService.ForceRefreshAsync();
            await LoadAgendaAsync();
        }

        private void Configure_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_configService);
            settingsWindow.Owner = Window.GetWindow(this);
            settingsWindow.ShowDialog();
            
            // Force refresh after settings change
            _ = Task.Run(async () =>
            {
                await _globalAgendaService.ForceRefreshAsync();
                await LoadAgendaAsync();
            });
        }

        private void UpdateAllProperties()
        {
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasNoItems));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ConfiguredFilesText));
            OnPropertyChanged(nameof(TotalItemCount));
            OnPropertyChanged(nameof(OverdueCount));
            OnPropertyChanged(nameof(ActionRequiredCount));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // IFileTab Implementation
        public Task<bool> Save()
        {
            // Global agenda doesn't need saving
            return Task.FromResult(true);
        }

        public Task<bool> SaveAs(string newPath = null)
        {
            // Global agenda doesn't support save as
            return Task.FromResult(false);
        }

        public void Reload()
        {
            _ = Task.Run(async () => await LoadAgendaAsync());
        }

        public string GetContent()
        {
            // Return a summary of agenda items
            var items = TotalItemCount;
            var overdue = OverdueCount;
            return $"Global Agenda Summary: {items} total items, {overdue} overdue";
        }

        public void OnTabSelected()
        {
            // Always refresh the UI when tab is selected to show latest data and colors
            _ = Task.Run(async () => await LoadAgendaAsync());
        }

        public void OnTabDeselected()
        {
            // Nothing to do when tab becomes inactive
        }

        public void Dispose()
        {
            // Unsubscribe from events to prevent memory leaks
            if (_globalAgendaService != null)
            {
                _globalAgendaService.ItemsChanged -= OnItemsChanged;
            }
            _configService.Provider.ConfigurationChanged -= OnConfigurationChanged;
            
            _globalAgendaService?.Dispose();
        }

        private void OnConfigurationChanged(object sender, EventArgs e)
        {
            // Force refresh when configuration changes
            _ = Task.Run(async () => await LoadAgendaAsync());
        }

        #region Context Menu Handlers

        private async void RefileItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                try
                {
                    var dialog = new Dialogs.RefileDialog(itemWithSource.Item, itemWithSource.Service, _configService, _globalAgendaService);
                    dialog.Owner = Window.GetWindow(this);
                    
                    var result = dialog.ShowDialog();
                    
                    if (result == true)
                    {
                        // Refresh the global agenda since the item was moved
                        await _globalAgendaService.ForceRefreshAsync();
                        await LoadAgendaAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening refile dialog: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                var dialog = new InputDialog("Add Tag", "Enter tag name:");
                dialog.Owner = Window.GetWindow(this);
                
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    try
                    {
                        await itemWithSource.Service.AddTagAsync(itemWithSource.Item.Id, dialog.InputText.Trim());
                        await itemWithSource.Service.SaveToFileAsync();
                        await LoadAgendaAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error adding tag: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void SetScheduled_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                var dialog = new DatePickerDialog("Set Scheduled Date", itemWithSource.Item.Scheduled ?? DateTime.Today);
                dialog.Owner = Window.GetWindow(this);
                
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        await itemWithSource.Service.SetScheduledAsync(itemWithSource.Item.Id, dialog.SelectedDate);
                        await itemWithSource.Service.SaveToFileAsync();
                        await LoadAgendaAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error setting scheduled date: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void SetDeadline_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                var dialog = new DatePickerDialog("Set Deadline", itemWithSource.Item.Deadline ?? DateTime.Today);
                dialog.Owner = Window.GetWindow(this);
                
                if (dialog.ShowDialog() == true)
                {
                    try
                    {
                        await itemWithSource.Service.SetDeadlineAsync(itemWithSource.Item.Id, dialog.SelectedDate);
                        await itemWithSource.Service.SaveToFileAsync();
                        await LoadAgendaAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error setting deadline: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void OpenSourceFile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                try
                {
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    mainWindow?.OpenFileInEditor(itemWithSource.SourceFile);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error opening source file: {ex.Message}", "Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is OrgItemWithSource itemWithSource)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{itemWithSource.Item.Title}'?", 
                    "Confirm Delete", 
                    MessageBoxButton.YesNo, 
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        await itemWithSource.Service.DeleteItemAsync(itemWithSource.Item.Id);
                        await itemWithSource.Service.SaveToFileAsync();
                        await LoadAgendaAsync();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error deleting item: {ex.Message}", "Error", 
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        #endregion
    }
} 