using System;
using System.Windows.Controls;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Interfaces;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Generic;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Core;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows;

namespace Universa.Desktop.Tabs
{
    public partial class ToDoTab : UserControl, IFileTab, INotifyPropertyChanged
    {
        private readonly IToDoViewModel _viewModel;
        private string _filePath;
        private bool _isModified;
        private string _title;
        private readonly System.Windows.Threading.DispatcherTimer _autoSaveTimer;
        private int _pendingSaveCounter = 0;
        private readonly IToDoService _todoService;
        private readonly IServiceProvider _serviceProvider;
        private string _searchText = string.Empty;
        private ICollectionView _filteredView;
        private bool _showCompletedItems;
        private bool _isContentLoaded = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged(nameof(FilePath));
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public string Title
        {
            get => _title ?? Path.GetFileName(FilePath);
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    OnPropertyChanged(nameof(IsModified));
                }
            }
        }

        public bool ShowCompletedItems
        {
            get => _showCompletedItems;
            set
            {
                if (_showCompletedItems != value)
                {
                    _showCompletedItems = value;
                    OnPropertyChanged(nameof(ShowCompletedItems));
                    UpdateFilteredItems();
                }
            }
        }

        public IEnumerable<ToDo> Todos => _viewModel.Todos;

        public ToDoTab(string filePath, IToDoViewModel viewModel, IServiceProvider serviceProvider)
        {
            InitializeComponent();
            
            // Store the file path and service provider
            _filePath = filePath;
            _title = Path.GetFileName(filePath);
            _serviceProvider = serviceProvider;
            
            System.Diagnostics.Debug.WriteLine($">>>>>> Creating NEW ToDoTab for file: {_filePath} <<<<<<");
            
            // Create a new scope for this tab to ensure isolated services
            var scope = _serviceProvider.CreateScope();
            
            // Get the configuration service
            var configService = scope.ServiceProvider.GetRequiredService<IConfigurationService>();
            
            // Create a new ToDoService specifically for this file
            _todoService = new ToDoService(configService, filePath);
            
            // Create a new ViewModel with our isolated service
            _viewModel = new ToDoViewModel(_todoService, scope.ServiceProvider.GetService<IDialogService>());
            
            // Set the DataContext to our view model
            DataContext = _viewModel;
            
            // Initialize filtered view
            _filteredView = CollectionViewSource.GetDefaultView(_viewModel.Todos);
            _filteredView.Filter = FilterTodo;
            
            System.Diagnostics.Debug.WriteLine($">>>>>> Created ISOLATED service and view model for file: {_filePath} <<<<<<");
            
            // Set up auto-save timer
            _autoSaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _autoSaveTimer.Tick += AutoSaveTimer_Tick;
            _autoSaveTimer.Start();
            
            // Subscribe to collection changes
            if (_viewModel.Todos is System.Collections.Specialized.INotifyCollectionChanged notifyCollection)
            {
                notifyCollection.CollectionChanged += Todos_CollectionChanged;
            }
            
            // Subscribe to service events
            if (_todoService != null)
            {
                _todoService.TodoChanged += TodoService_TodoChanged;
            }
            
            // Load the ToDos from the specific file path
            LoadTodosFromFile();
        }

        private bool FilterTodo(object item)
        {
            if (!(item is ToDo todo)) return false;

            // Filter by search text
            bool matchesSearch = string.IsNullOrWhiteSpace(_searchText) ||
                               todo.Title?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true ||
                               todo.Description?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true;

            // Filter by completion status
            bool matchesCompletion = ShowCompletedItems || !todo.IsCompleted;

            return matchesSearch && matchesCompletion;
        }

        public void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;
                _filteredView?.Refresh();
            }
        }

        public void ToDo_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ToDo todo)
            {
                if (todo.IsCompleted)
                {
                    // Mark the ToDo as completed
                    _viewModel.CompleteTodoAsync(todo).ContinueWith(t =>
                    {
                        // After the todo is completed, ask if the user wants to archive it
                        Dispatcher.Invoke(() =>
                        {
                            var result = MessageBox.Show(
                                $"Do you want to archive the completed task '{todo.Title}'?",
                                "Archive Completed Task",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result == MessageBoxResult.Yes)
                            {
                                // Archive the todo
                                _viewModel.ArchiveTodoAsync(todo).ConfigureAwait(false);
                            }
                        });
                    }).ConfigureAwait(false);
                }
                else
                {
                    _viewModel.UncompleteTodoAsync(todo).ConfigureAwait(false);
                }
            }
        }

        private void UpdateFilteredItems()
        {
            _filteredView?.Refresh();
        }

        public void AddToDo_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.AddTodo();
        }

        public void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToDo todo)
            {
                todo.IsExpanded = !todo.IsExpanded;
                _isModified = true;
                Save();
            }
        }
        
        public void DeleteToDo_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToDo todo)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the ToDo '{todo.Title}'?",
                    "Delete ToDo",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    _viewModel.DeleteTodoAsync(todo).ConfigureAwait(false);
                }
            }
        }
        
        public void ToDo_Title_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Only handle double-clicks
            if (e.ClickCount == 2)
            {
                if (sender is FrameworkElement element && element.DataContext is ToDo todo)
                {
                    // Toggle expanded state
                    todo.IsExpanded = !todo.IsExpanded;
                    _isModified = true;
                    Save();
                    
                    // Mark the event as handled to prevent it from bubbling
                    e.Handled = true;
                }
            }
        }

        public void DeleteSubTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToDo subtask)
            {
                _viewModel.DeleteSubTask(subtask);
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Todos_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Mark as modified when collection changes
            System.Diagnostics.Debug.WriteLine($"Todos collection changed: {e.Action}");
            IsModified = true;
            
            // Immediately save changes
            ForceSave();
        }
        
        private void TodoService_TodoChanged(object sender, Universa.Desktop.Interfaces.TodoChangedEventArgs e)
        {
            // Mark as modified when a todo is changed through the service
            System.Diagnostics.Debug.WriteLine($"TodoService_TodoChanged: {e.TodoId}, {e.ChangeType}");
            IsModified = true;
            ForceSave();
        }
        
        private void ForceSave()
        {
            System.Diagnostics.Debug.WriteLine("ForceSave requested");
            
            // Increment pending save counter to track save operations
            _pendingSaveCounter++;
            var currentCounter = _pendingSaveCounter;
            
            // Restart auto-save timer with short delay
            _autoSaveTimer.Stop();
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(0.5);
            _autoSaveTimer.Start();
            
            // Execute save after a slight delay to batch rapid changes
            Task.Delay(500).ContinueWith(_ => 
            {
                // Only proceed if this is still the most recent save request
                if (currentCounter == _pendingSaveCounter)
                {
                    System.Diagnostics.Debug.WriteLine($"Executing delayed save for counter {currentCounter}");
                    Save().ConfigureAwait(false);
                }
            });
        }
        
        private void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            if (IsModified)
            {
                System.Diagnostics.Debug.WriteLine("Auto-saving ToDos");
                Save().ConfigureAwait(false);
            }
        }

        public void OnTabSelected()
        {
            // Refresh ToDos when tab is selected
            System.Diagnostics.Debug.WriteLine($">>>>>> Tab Selected for file: {_filePath} <<<<<<");
            
            // Force reload from file
            LoadTodosFromFile();
        }

        public void OnTabDeselected()
        {
            // Save changes when deselected
            if (IsModified)
            {
                System.Diagnostics.Debug.WriteLine("Tab deselected - saving changes");
                Save().ConfigureAwait(false);
            }
        }

        public string GetContent()
        {
            // ToDo tab doesn't support direct content editing
            return string.Empty;
        }

        public async Task<bool> Save()
        {
            try 
            {
                if (_viewModel == null || string.IsNullOrEmpty(_filePath)) 
                {
                    return false;
                }

                await _viewModel.SaveTodosAsync();
                IsModified = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving todos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath))
                {
                    return false;
                }

                // Update file path
                _filePath = newPath;
                _title = Path.GetFileName(newPath);
                
                // Update the service with the new path
                if (_todoService is ToDoService todoService)
                {
                    todoService.UpdateFilePath(newPath);
                }
                
                // Save to the new path
                return await Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving todos to new location: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            // Force reload of the content
            LoadTodosFromFile();
        }

        private async void LoadTodosFromFile()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading ToDos from file: {_filePath}");
                
                // Check if file exists, if not create a default structure
                if (!File.Exists(_filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"File does not exist, creating new ToDo file: {_filePath}");
                    var defaultTodo = new ToDo
                    {
                        Id = Guid.NewGuid().ToString(),
                        Title = "Sample ToDo Item",
                        Description = "This is a sample ToDo item. Click the checkbox to mark it as completed.",
                        CreatedDate = DateTime.Now,
                        IsCompleted = false
                    };
                    
                    var todos = new List<ToDo> { defaultTodo };
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var json = JsonSerializer.Serialize(todos, options);
                    
                    // Ensure directory exists
                    var directory = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    
                    File.WriteAllText(_filePath, json);
                    System.Diagnostics.Debug.WriteLine($"Created new ToDo file with sample item: {_filePath}");
                }
                
                await _viewModel.LoadTodosAsync();
                
                // Debug log ToDo items count
                System.Diagnostics.Debug.WriteLine($"Loaded {_viewModel.Todos.Count} ToDo items");
                foreach (var todo in _viewModel.Todos)
                {
                    System.Diagnostics.Debug.WriteLine($"  - ToDo: {todo.Title}, Completed: {todo.IsCompleted}");
                }
                
                _isContentLoaded = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading ToDos: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error loading todos: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
