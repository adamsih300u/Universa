using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using System.ComponentModel.DataAnnotations;
using System.Windows.Data;
using System.Windows.Input;

namespace Universa.Desktop
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }

    public partial class ToDoTab : UserControl, INotifyPropertyChanged, IFileTab
    {
        private bool _hideFutureItems;
        private ObservableCollection<ToDo> _todos;
        private string _filePath;
        private bool _isModified;
        private string _title;
        private string _searchText = string.Empty;
        private ICollectionView _filteredView;
        private ICommand _deleteCommand;
        private ICommand _addSubTaskCommand;
        private bool _showCompletedItems;
        private string _archiveFilePath;

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

        public bool HideFutureItems
        {
            get => _hideFutureItems;
            set
            {
                if (_hideFutureItems != value)
                {
                    _hideFutureItems = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HideFutureItems)));
                    UpdateFilteredItems();
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
                    LoadCompletedItems();
                    UpdateFilteredItems();
                }
            }
        }

        public ObservableCollection<ToDo> Todos
        {
            get => _todos;
            private set
            {
                _todos = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Todos)));
            }
        }

        public ICommand DeleteCommand => _deleteCommand ??= new RelayCommand(ExecuteDelete);
        public ICommand AddSubTaskCommand => _addSubTaskCommand ??= new RelayCommand(ExecuteAddSubTask);

        public ToDoTab(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            _todos = new ObservableCollection<ToDo>();
            DataContext = this;

            // Initialize commands
            _deleteCommand = new RelayCommand(ExecuteDelete);
            _addSubTaskCommand = new RelayCommand(ExecuteAddSubTask);

            // Initialize filtered view
            _filteredView = CollectionViewSource.GetDefaultView(_todos);
            _filteredView.Filter = FilterTodo;

            LoadFile(filePath);
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

            // Filter by future items
            bool matchesFuture = !HideFutureItems || 
                               !todo.StartDate.HasValue || 
                               todo.StartDate.Value.Date <= DateTime.Today;

            return matchesSearch && matchesCompletion && matchesFuture;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;
                _filteredView?.Refresh();
            }
        }

        private void TodosListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var item = TodosListView.SelectedItem as ToDo;
            if (item != null)
            {
                item.IsExpanded = !item.IsExpanded;
                _isModified = true;
                SaveFile();
            }
        }

        private void ToDo_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ToDo todo)
            {
                if (todo.IsCompleted)
                {
                    // Item is being completed
                    todo.CompletedDate = DateTime.Now;
                    _isModified = true;
                    
                    // Handle recurring tasks
                    if (todo.IsRecurring)
                    {
                        // Calculate next start date based on completion
                        var nextStart = CalculateNextStartDate(todo);
                        
                        // Create a new instance for the next occurrence
                        var nextTodo = new ToDo
                        {
                            Title = todo.Title,
                            Description = todo.Description,
                            IsRecurring = todo.IsRecurring,
                            RecurrenceInterval = todo.RecurrenceInterval,
                            RecurrenceUnit = todo.RecurrenceUnit,
                            StartDate = nextStart,
                            DueDate = todo.DueDate.HasValue ? nextStart.AddDays((todo.DueDate.Value - (todo.StartDate ?? DateTime.Now)).Days) : null,
                            Tags = new List<string>(todo.Tags ?? new List<string>()),
                            FilePath = todo.FilePath
                        };
                        
                        // Add to active todos
                        _todos.Add(nextTodo);
                    }
                    
                    SaveFile();
                    
                    // Update the UI to ensure CompletedDate is displayed
                    OnPropertyChanged(nameof(ShowCompletedItems));
                    
                    // Archive the completed item
                    ArchiveCompletedItem(todo);
                }
                else
                {
                    // Store the previous completion date before clearing it
                    var previousCompletionTag = todo.Tags?.FirstOrDefault(t => t.StartsWith("Previous Completion:"));
                    if (previousCompletionTag != null)
                    {
                        todo.Tags.Remove(previousCompletionTag);
                    }
                    if (todo.CompletedDate.HasValue)
                    {
                        if (todo.Tags == null) todo.Tags = new List<string>();
                        todo.Tags.Add($"Previous Completion: {todo.CompletedDate:yyyy-MM-dd HH:mm:ss}");
                    }
                    
                    // Item is being "uncompleted"
                    todo.CompletedDate = null;
                    
                    // If this was an archived item, move it back to the active list
                    if (todo.FilePath == _archiveFilePath)
                    {
                        // Remove from archive file
                        RemoveFromArchive(todo);
                        
                        // Add to active list with the main file path
                        todo.FilePath = _filePath;
                        if (!_todos.Contains(todo))
                        {
                            _todos.Add(todo);
                        }
                    }
                    
                    _isModified = true;
                    SaveFile();
                    UpdateFilteredItems();
                }
            }
        }

        private DateTime CalculateNextStartDate(ToDo todo)
        {
            var baseDate = todo.CompletedDate ?? DateTime.Now;
            
            return todo.RecurrenceUnit?.ToLower() switch
            {
                "hour" => baseDate.AddHours(todo.RecurrenceInterval),
                "day" => baseDate.AddDays(todo.RecurrenceInterval),
                "week" => baseDate.AddDays(todo.RecurrenceInterval * 7),
                "month" => baseDate.AddMonths(todo.RecurrenceInterval),
                "year" => baseDate.AddYears(todo.RecurrenceInterval),
                _ => baseDate.AddDays(todo.RecurrenceInterval) // Default to days if unit is not recognized
            };
        }

        private void RemoveFromArchive(ToDo item)
        {
            try
            {
                var archivePath = Path.ChangeExtension(_filePath, ".todo.archive");
                if (!File.Exists(archivePath)) return;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                // Load existing archived items
                var archivedJson = File.ReadAllText(archivePath);
                var archivedItems = JsonSerializer.Deserialize<List<ToDo>>(archivedJson, options) ?? new List<ToDo>();

                // Remove the item from archive
                var itemToRemove = archivedItems.FirstOrDefault(i => i.Title == item.Title);
                if (itemToRemove != null)
                {
                    archivedItems.Remove(itemToRemove);
                    
                    // Save the updated archive
                    var archiveJsonContent = JsonSerializer.Serialize(archivedItems, options);
                    File.WriteAllText(archivePath, archiveJsonContent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error removing item from archive: {ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToDo_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            _isModified = true;
            SaveFile();

            // If this is a completed item from the archive, update the archive file
            if (sender is ToDo todo && todo.IsCompleted && todo.FilePath == _archiveFilePath)
            {
                UpdateArchivedItem(todo);
            }
        }

        private void UpdateArchivedItem(ToDo item)
        {
            try
            {
                var archivePath = Path.ChangeExtension(_filePath, ".todo.archive");
                if (!File.Exists(archivePath)) return;

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                // Load existing archived items
                var archivedJson = File.ReadAllText(archivePath);
                var archivedItems = JsonSerializer.Deserialize<List<ToDo>>(archivedJson, options) ?? new List<ToDo>();

                // Find and update the item
                var existingItem = archivedItems.FirstOrDefault(i => i.Title == item.Title);
                if (existingItem != null)
                {
                    // Update all properties
                    existingItem.Description = item.Description;
                    existingItem.Tags = item.Tags;
                    existingItem.StartDate = item.StartDate;
                    existingItem.DueDate = item.DueDate;
                    existingItem.SubTasks = item.SubTasks;
                    existingItem.IsExpanded = item.IsExpanded;
                    existingItem.HasSubTask = item.HasSubTask;

                    // Save the updated archive
                    var archiveJsonContent = JsonSerializer.Serialize(archivedItems, options);
                    File.WriteAllText(archivePath, archiveJsonContent);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating archived item: {ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ArchiveCompletedItem(ToDo item)
        {
            try
            {
                // Create archive file path by inserting .archive before the .todo extension
                var archivePath = Path.ChangeExtension(_filePath, ".todo.archive");
                
                // Add completion timestamp to the item
                var completionTag = $"Completed: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                if (!item.Tags.Contains(completionTag))
                {
                    item.Tags.Add(completionTag);
                }
                
                // Load or create the archive file
                List<ToDo> archivedItems;
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                if (File.Exists(archivePath))
                {
                    var archivedJson = File.ReadAllText(archivePath);
                    archivedItems = JsonSerializer.Deserialize<List<ToDo>>(archivedJson, options) ?? new List<ToDo>();
                }
                else
                {
                    archivedItems = new List<ToDo>();
                }
                
                // Check if this item is already archived
                var existingItem = archivedItems.FirstOrDefault(i => i.Title == item.Title);
                if (existingItem != null)
                {
                    // Update the existing archived item
                    existingItem.IsCompleted = item.IsCompleted;
                    existingItem.CompletedDate = item.CompletedDate;
                    existingItem.Tags = item.Tags;
                }
                else
                {
                    // Add the completed item to the archive
                    archivedItems.Add(item);
                }
                
                // Save the archive file
                var archiveJsonContent = JsonSerializer.Serialize(archivedItems, options);
                File.WriteAllText(archivePath, archiveJsonContent);
                
                // Remove the item from the current list if it's not from the archive
                if (item.FilePath == _filePath)
                {
                    _todos.Remove(item);
                }
                
                // Save the main file to update the list of incomplete items
                SaveFile();
                
                // Update the filtered items
                UpdateFilteredItems();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error archiving completed item: {ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // Revert the completion status
                item.IsCompleted = false;
                item.CompletedDate = null;
            }
        }

        private void UpdateFilteredItems()
        {
            if (_filteredView != null)
            {
                _filteredView.Refresh();
            }
        }

        private void LoadFile(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                var content = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var allItems = JsonSerializer.Deserialize<List<ToDo>>(content, options);
                if (allItems == null) return;

                // Only load incomplete items into the main list
                _todos.Clear();
                foreach (var item in allItems.Where(i => !i.IsCompleted))
                {
                    item.FilePath = filePath;
                    // Subscribe to property changes
                    item.PropertyChanged += ToDo_PropertyChanged;
                    _todos.Add(item);
                }

                // If showing completed items, load them from the archive
                if (ShowCompletedItems)
                {
                    LoadCompletedItems();
                }

                UpdateFilteredItems();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<bool> Save()
        {
            try
            {
                SaveFile();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            if (string.IsNullOrEmpty(newPath))
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "ToDo files (*.todo)|*.todo|All files (*.*)|*.*",
                    DefaultExt = ".todo"
                };

                if (dialog.ShowDialog() == true)
                {
                    newPath = dialog.FileName;
                }
                else
                {
                    return false;
                }
            }

            try
            {
                FilePath = newPath;
                await Task.Run(() => SaveFile());
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            LoadFile(FilePath);
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            
            try
            {
                // Save only incomplete items in the main file
                var incompleteItems = _todos.Where(t => !t.IsCompleted && t.FilePath == _filePath).ToList();
                foreach (var todo in incompleteItems)
                {
                    todo.FilePath = _filePath;
                    todo.Tags ??= new List<string>();
                }
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };

                System.Diagnostics.Debug.WriteLine($"Saving {incompleteItems.Count} incomplete ToDos to file: {_filePath}");
                var json = JsonSerializer.Serialize(incompleteItems, options);
                File.WriteAllText(_filePath, json);
                IsModified = false;

                // Notify ToDoTracker that the file has changed
                var libraryPath = Configuration.Instance.LibraryPath;
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    ToDoTracker.Instance.ScanTodoFiles(libraryPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving ToDo file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void AddToDo_Click(object sender, RoutedEventArgs e)
        {
            var newItem = new ToDo { Title = "New ToDo", FilePath = _filePath };
            // Subscribe to property changes for the new item
            newItem.PropertyChanged += ToDo_PropertyChanged;
            _todos.Add(newItem);
            _isModified = true;
            SaveFile();
            UpdateFilteredItems();
        }

        private void ExecuteDelete(object parameter)
        {
            if (parameter is ToDo todo)
            {
                // Remove from the current list
                Todos.Remove(todo);

                // If the item is completed, also remove it from the archive file
                if (todo.IsCompleted)
                {
                    try
                    {
                        var archivePath = Path.ChangeExtension(_filePath, ".todo.archive");
                        if (File.Exists(archivePath))
                        {
                            var options = new JsonSerializerOptions
                            {
                                WriteIndented = true,
                                PropertyNameCaseInsensitive = true
                            };

                            // Load existing archived items
                            var archivedJson = File.ReadAllText(archivePath);
                            var archivedItems = JsonSerializer.Deserialize<List<ToDo>>(archivedJson, options) ?? new List<ToDo>();

                            // Remove the item from archive
                            var itemToRemove = archivedItems.FirstOrDefault(i => i.Title == todo.Title);
                            if (itemToRemove != null)
                            {
                                archivedItems.Remove(itemToRemove);
                                
                                // Save the updated archive
                                var archiveJsonContent = JsonSerializer.Serialize(archivedItems, options);
                                File.WriteAllText(archivePath, archiveJsonContent);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error removing item from archive: {ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                _isModified = true;
                SaveFile();
            }
        }

        private void ExecuteAddSubTask(object parameter)
        {
            if (parameter is ToDo todo)
            {
                if (todo.SubTasks == null)
                {
                    todo.SubTasks = new ObservableCollection<ToDo>();
                }

                // Check if this would be the second subtask
                if (todo.SubTasks.Count >= 1)
                {
                    var result = MessageBox.Show(
                        "This task already has a subtask. Would you like to convert it to a project?",
                        "Convert to Project",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        if (todo.Tags == null)
                        {
                            todo.Tags = new List<string>();
                        }
                        if (!todo.Tags.Contains("Project"))
                        {
                            todo.Tags.Add("Project");
                        }
                    }
                    else
                    {
                        return; // Don't add another subtask if user declines
                    }
                }

                var newSubTask = new ToDo
                {
                    Title = "New Subtask",
                    FilePath = todo.FilePath
                };

                todo.SubTasks.Add(newSubTask);
                todo.HasSubTask = true;
                todo.IsExpanded = true;
                _isModified = true;
                SaveFile();
            }
        }

        private void LoadCompletedItems()
        {
            if (!ShowCompletedItems)
            {
                // Remove completed items that were loaded from archive
                var archivedItems = _todos.Where(t => t.IsCompleted && t.FilePath == _archiveFilePath).ToList();
                foreach (var item in archivedItems)
                {
                    item.PropertyChanged -= ToDo_PropertyChanged; // Unsubscribe from events
                    _todos.Remove(item);
                }
                return;
            }

            try
            {
                // Create archive file path
                _archiveFilePath = Path.ChangeExtension(_filePath, ".todo.archive");
                
                if (!File.Exists(_archiveFilePath)) return;

                var content = File.ReadAllText(_archiveFilePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var archivedItems = JsonSerializer.Deserialize<List<ToDo>>(content, options);
                if (archivedItems == null) return;

                // Add archived items that aren't already in the list
                foreach (var item in archivedItems)
                {
                    item.FilePath = _archiveFilePath;
                    if (!_todos.Any(t => t.FilePath == _archiveFilePath && t.Title == item.Title))
                    {
                        item.PropertyChanged += ToDo_PropertyChanged; // Subscribe to property changes
                        _todos.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading archived items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExpandButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToDo todo)
            {
                todo.IsExpanded = !todo.IsExpanded;
                _isModified = true;
                SaveFile();
            }
        }

        private void DeleteSubTask_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is ToDo subtask)
            {
                // Find the parent todo that contains this subtask
                var parentTodo = _todos.FirstOrDefault(t => t.SubTasks?.Contains(subtask) == true);
                if (parentTodo == null)
                {
                    // Check nested subtasks
                    foreach (var todo in _todos)
                    {
                        parentTodo = FindParentTodoRecursive(todo, subtask);
                        if (parentTodo != null) break;
                    }
                }

                if (parentTodo != null)
                {
                    parentTodo.SubTasks.Remove(subtask);
                    if (parentTodo.SubTasks.Count == 0)
                    {
                        parentTodo.HasSubTask = false;
                    }
                    _isModified = true;
                    SaveFile();
                }
            }
        }

        private ToDo FindParentTodoRecursive(ToDo todo, ToDo subtaskToFind)
        {
            if (todo.SubTasks?.Contains(subtaskToFind) == true)
            {
                return todo;
            }

            if (todo.SubTasks != null)
            {
                foreach (var subtask in todo.SubTasks)
                {
                    var result = FindParentTodoRecursive(subtask, subtaskToFind);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return null;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gets the content of the todo tab
        /// </summary>
        /// <returns>The content as a string</returns>
        public string GetContent()
        {
            // For todo tab, we'll return a simple representation of the todo items
            // This is a placeholder implementation since todo content isn't typically exported
            return "Todo list content is not available for export.";
        }
    }
} 