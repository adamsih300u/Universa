using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows.Input;
using System.Threading.Tasks;
using Universa.Desktop.Library;
using System.IO;
using System.Text.Json;
using Universa.Desktop.Commands;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.ViewModels
{
    public class ToDoViewModel : IToDoViewModel, INotifyPropertyChanged, IDisposable
    {
        private readonly IToDoService _todoService;
        private readonly IDialogService _dialogService;
        private ObservableCollection<ToDo> _todos;
        private ToDo _selectedTodo;
        private string _filterText;
        private bool _showCompleted;
        private bool _showArchived;
        private bool _disposed;
        private string _newTodoTitle;
        private string _newTodoDescription;
        private DateTime? _newTodoStartDate;
        private DateTime? _newTodoDueDate;
        private string _newTodoPriority;
        private string _newTodoCategory;
        private string _newTodoAssignee;
        private bool _newTodoIsRecurring;
        private int _newTodoRecurrenceInterval;
        private string _newTodoRecurrenceUnit;
        private string _newTodoTag;
        private bool _showCompletedItems;
        private string _searchText;
        private bool _hideFutureItems;

        public event PropertyChangedEventHandler PropertyChanged;

        // Expose the todo service publicly
        public IToDoService TodoService => _todoService;

        public ObservableCollection<ToDo> Todos
        {
            get => _todos;
            private set
            {
                if (_todos != value)
                {
                    _todos = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<ToDo> SubTasks { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<string> Tags { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Categories { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Priorities { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> Assignees { get; } = new ObservableCollection<string>();
        public ObservableCollection<ToDo> OverdueTodos { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<ToDo> DueTodayTodos { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<ToDo> DueThisWeekTodos { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<ToDo> CompletedTodos { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<ToDo> IncompleteTodos { get; } = new ObservableCollection<ToDo>();
        public ObservableCollection<ToDo> RecurringTodos { get; } = new ObservableCollection<ToDo>();

        public ToDo SelectedTodo
        {
            get => _selectedTodo;
            set
            {
                if (_selectedTodo != value)
                {
                    _selectedTodo = value;
                    OnPropertyChanged();
                    if (value != null)
                    {
                        LoadSubTasksAsync(value.Id);
                    }
                }
            }
        }

        public string FilterText
        {
            get => _filterText;
            set
            {
                if (_filterText != value)
                {
                    _filterText = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public bool ShowCompleted
        {
            get => _showCompleted;
            set
            {
                if (_showCompleted != value)
                {
                    _showCompleted = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public bool ShowArchived
        {
            get => _showArchived;
            set
            {
                if (_showArchived != value)
                {
                    _showArchived = value;
                    OnPropertyChanged();
                    ApplyFilter();
                }
            }
        }

        public string NewTodoTitle
        {
            get => _newTodoTitle;
            set
            {
                if (_newTodoTitle != value)
                {
                    _newTodoTitle = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoDescription
        {
            get => _newTodoDescription;
            set
            {
                if (_newTodoDescription != value)
                {
                    _newTodoDescription = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime? NewTodoStartDate
        {
            get => _newTodoStartDate;
            set
            {
                if (_newTodoStartDate != value)
                {
                    _newTodoStartDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime? NewTodoDueDate
        {
            get => _newTodoDueDate;
            set
            {
                if (_newTodoDueDate != value)
                {
                    _newTodoDueDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoPriority
        {
            get => _newTodoPriority;
            set
            {
                if (_newTodoPriority != value)
                {
                    _newTodoPriority = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoCategory
        {
            get => _newTodoCategory;
            set
            {
                if (_newTodoCategory != value)
                {
                    _newTodoCategory = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoAssignee
        {
            get => _newTodoAssignee;
            set
            {
                if (_newTodoAssignee != value)
                {
                    _newTodoAssignee = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool NewTodoIsRecurring
        {
            get => _newTodoIsRecurring;
            set
            {
                if (_newTodoIsRecurring != value)
                {
                    _newTodoIsRecurring = value;
                    OnPropertyChanged();
                }
            }
        }

        public int NewTodoRecurrenceInterval
        {
            get => _newTodoRecurrenceInterval;
            set
            {
                if (_newTodoRecurrenceInterval != value)
                {
                    _newTodoRecurrenceInterval = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoRecurrenceUnit
        {
            get => _newTodoRecurrenceUnit;
            set
            {
                if (_newTodoRecurrenceUnit != value)
                {
                    _newTodoRecurrenceUnit = value;
                    OnPropertyChanged();
                }
            }
        }

        public string NewTodoTag
        {
            get => _newTodoTag;
            set
            {
                if (_newTodoTag != value)
                {
                    _newTodoTag = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged();
                    RefreshTodos();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText != value)
                {
                    _searchText = value;
                    OnPropertyChanged();
                    RefreshTodos();
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
                    OnPropertyChanged();
                    RefreshTodos();
                }
            }
        }

        public ICommand AddTodoCommand { get; }
        public ICommand DeleteTodoCommand { get; }
        public ICommand CompleteTodoCommand { get; }
        public ICommand UncompleteTodoCommand { get; }
        public ICommand AddSubTaskCommand { get; }
        public ICommand RemoveSubTaskCommand { get; }
        public ICommand AddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }
        public ICommand ArchiveTodoCommand { get; }

        public ToDoViewModel(IToDoService todoService, IDialogService dialogService)
        {
            _todoService = todoService;
            _dialogService = dialogService; // This can be null, we'll handle it in methods that use it
            _todos = new ObservableCollection<ToDo>();
            
            // Set initial filter states - show everything by default
            _showCompleted = true;
            _showArchived = true;
            _showCompletedItems = true;
            
            // Initialize commands
            AddTodoCommand = new RelayCommand(async _ => await AddTodoAsync());
            DeleteTodoCommand = new RelayCommand<ToDo>(async todo => await DeleteTodoAsync(todo));
            CompleteTodoCommand = new RelayCommand(async _ => await CompleteTodoAsync(SelectedTodo));
            UncompleteTodoCommand = new RelayCommand(async _ => await UncompleteTodoAsync(SelectedTodo));
            AddSubTaskCommand = new RelayCommand(async _ => await AddSubTaskAsync(SelectedTodo, CreateNewTodo()));
            RemoveSubTaskCommand = new RelayCommand(async _ => await RemoveSubTaskAsync(SelectedTodo, SelectedTodo));
            AddTagCommand = new RelayCommand(async _ => await AddTagAsync(SelectedTodo, NewTodoTag));
            RemoveTagCommand = new RelayCommand(async _ => await RemoveTagAsync(SelectedTodo, NewTodoTag));
            ArchiveTodoCommand = new RelayCommand(async _ => await ArchiveTodoAsync(SelectedTodo));
            
            // Subscribe to service events if available
            if (_todoService != null)
            {
                _todoService.TodoChanged += OnTodoChanged;
            }
            
            // Initial load will be handled by the tab
            System.Diagnostics.Debug.WriteLine("Created new ToDoViewModel - Isolated from global state");
        }

        public async Task LoadTodosAsync()
        {
            var todos = await _todoService.GetAllTodosAsync();
            Todos.Clear();
            foreach (var todo in todos)
            {
                Todos.Add(todo);
            }

            await LoadFilteredTodosAsync();
        }

        private async Task LoadFilteredTodosAsync()
        {
            var overdueTodos = await _todoService.GetOverdueTodosAsync();
            OverdueTodos.Clear();
            foreach (var todo in overdueTodos)
            {
                OverdueTodos.Add(todo);
            }

            var dueTodayTodos = await _todoService.GetDueTodayTodosAsync();
            DueTodayTodos.Clear();
            foreach (var todo in dueTodayTodos)
            {
                DueTodayTodos.Add(todo);
            }

            var dueThisWeekTodos = await _todoService.GetDueThisWeekTodosAsync();
            DueThisWeekTodos.Clear();
            foreach (var todo in dueThisWeekTodos)
            {
                DueThisWeekTodos.Add(todo);
            }

            var completedTodos = await _todoService.GetCompletedTodosAsync();
            CompletedTodos.Clear();
            foreach (var todo in completedTodos)
            {
                CompletedTodos.Add(todo);
            }

            var incompleteTodos = await _todoService.GetIncompleteTodosAsync();
            IncompleteTodos.Clear();
            foreach (var todo in incompleteTodos)
            {
                IncompleteTodos.Add(todo);
            }

            var recurringTodos = await _todoService.GetRecurringTodosAsync();
            RecurringTodos.Clear();
            foreach (var todo in recurringTodos)
            {
                RecurringTodos.Add(todo);
            }
        }

        public async Task AddTodoAsync()
        {
            var todo = CreateNewTodo();
            
            // Save the file path in the ToDo item
            string currentFilePath = GetCurrentFilePath();
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                todo.FilePath = currentFilePath;
                System.Diagnostics.Debug.WriteLine($"Setting FilePath for new ToDo: {currentFilePath}");
            }
            
            await _todoService.CreateTodoAsync(todo);
            Todos.Add(todo);
            
            // Save all todos back to the file
            await SaveTodosToFileAsync(currentFilePath);
            
            await LoadFilteredTodosAsync();
            ClearNewTodoFields();
        }

        // Helper to get the current file path
        private string GetCurrentFilePath()
        {
            // Look at the FilePath of existing todos
            if (Todos.Count > 0 && !string.IsNullOrEmpty(Todos[0].FilePath))
            {
                return Todos[0].FilePath;
            }
            
            // Default file path from service
            if (_todoService is ToDoService todoService)
            {
                return todoService.FilePath;
            }
            
            return null;
        }
        
        // Method to save todos to a file
        public async Task SaveTodosToFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine("Cannot save - no file path specified");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine($"DIRECT SAVE: Saving {Todos.Count} todos to file: {filePath}");
                
                // Create a list of the current todos
                var todosToSave = new List<ToDo>(Todos);
                
                // Set the correct file path on each todo
                foreach (var todo in todosToSave)
                {
                    todo.FilePath = filePath;
                }
                
                // Serialize to JSON
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                // Create the directory if it doesn't exist
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating directory: {directory}");
                    Directory.CreateDirectory(directory);
                }
                
                // Serialize to JSON
                string json = JsonSerializer.Serialize(todosToSave, options);
                System.Diagnostics.Debug.WriteLine($"Serialized JSON: {json}");
                
                // DIRECT file write - no temp file to avoid any potential issues
                await File.WriteAllTextAsync(filePath, json);
                System.Diagnostics.Debug.WriteLine($"Direct file write successful to: {filePath}");
                
                // Verify file was written
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    System.Diagnostics.Debug.WriteLine($"Verified file: {filePath}, size: {fileInfo.Length} bytes, last modified: {fileInfo.LastWriteTime}");
                }
                
                // Update the service's todos as well to keep it in sync
                await _todoService.UpdateAllTodosAsync(todosToSave);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving todos: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Full error: {ex}");
                
                // Only show error dialog if we have a dialog service
                if (_dialogService != null)
                {
                    _dialogService.ShowError("Error Saving ToDos", $"Could not save ToDos to file: {ex.Message}");
                }
            }
        }

        public async Task UpdateTodoAsync(ToDo todo)
        {
            if (todo == null) return;
            
            await _todoService.UpdateTodoAsync(todo);
            
            // Save changes to file
            await SaveTodosToFileAsync(todo.FilePath);
            
            await LoadFilteredTodosAsync();
        }

        public async Task DeleteTodoAsync(ToDo todo)
        {
            if (todo != null)
            {
                if (_dialogService.ShowConfirmation("Are you sure you want to delete this todo?", "Delete Todo"))
                {
                    string filePath = todo.FilePath;
                    await _todoService.DeleteTodoAsync(todo.Id);
                    Todos.Remove(todo);
                    
                    // Save changes to file
                    await SaveTodosToFileAsync(filePath);
                    
                    await LoadFilteredTodosAsync();
                }
            }
        }

        public async Task CompleteTodoAsync(ToDo todo)
        {
            if (todo != null)
            {
                await _todoService.CompleteTodoAsync(todo.Id);
                
                // Save changes to file
                await SaveTodosToFileAsync(todo.FilePath);
                
                await LoadFilteredTodosAsync();
            }
        }

        public async Task UncompleteTodoAsync(ToDo todo)
        {
            if (todo != null)
            {
                await _todoService.UncompleteTodoAsync(todo.Id);
                
                // Save changes to file
                await SaveTodosToFileAsync(todo.FilePath);
                
                await LoadFilteredTodosAsync();
            }
        }

        public async Task AddSubTaskAsync(ToDo parentTodo, ToDo subTask)
        {
            if (parentTodo != null && subTask != null)
            {
                await _todoService.AddSubTaskAsync(parentTodo.Id, subTask);
                SubTasks.Add(subTask);
            }
        }

        public async Task RemoveSubTaskAsync(ToDo parentTodo, ToDo subTask)
        {
            if (parentTodo != null && subTask != null)
            {
                await _todoService.RemoveSubTaskAsync(parentTodo.Id, subTask.Id);
                SubTasks.Remove(subTask);
            }
        }

        public async Task AddTagAsync(ToDo todo, string tag)
        {
            if (todo != null && !string.IsNullOrEmpty(tag))
            {
                await _todoService.AddTagAsync(todo.Id, tag);
                if (!Tags.Contains(tag))
                {
                    Tags.Add(tag);
                }
            }
        }

        public async Task RemoveTagAsync(ToDo todo, string tag)
        {
            if (todo != null && !string.IsNullOrEmpty(tag))
            {
                await _todoService.RemoveTagAsync(todo.Id, tag);
            }
        }

        public async Task<bool> HasTagAsync(ToDo todo, string tag)
        {
            if (todo != null && !string.IsNullOrEmpty(tag))
            {
                return await _todoService.HasTagAsync(todo.Id, tag);
            }
            return false;
        }

        public async Task LoadTagsAsync()
        {
            var tags = await _todoService.GetAllTagsAsync();
            Tags.Clear();
            foreach (var tag in tags)
            {
                Tags.Add(tag);
            }
        }

        public async Task LoadCategoriesAsync()
        {
            var categories = await _todoService.GetAllCategoriesAsync();
            Categories.Clear();
            foreach (var category in categories)
            {
                Categories.Add(category);
            }
        }

        public async Task LoadPrioritiesAsync()
        {
            var priorities = await _todoService.GetAllPrioritiesAsync();
            Priorities.Clear();
            foreach (var priority in priorities)
            {
                Priorities.Add(priority);
            }
        }

        public async Task LoadAssigneesAsync()
        {
            var assignees = await _todoService.GetAllAssigneesAsync();
            Assignees.Clear();
            foreach (var assignee in assignees)
            {
                Assignees.Add(assignee);
            }
        }

        public async Task<bool> IsOverdueAsync(ToDo todo)
        {
            if (todo != null)
            {
                return await _todoService.IsOverdueAsync(todo.Id);
            }
            return false;
        }

        public async Task<bool> IsDueTodayAsync(ToDo todo)
        {
            if (todo != null)
            {
                return await _todoService.IsDueTodayAsync(todo.Id);
            }
            return false;
        }

        public async Task<bool> IsDueThisWeekAsync(ToDo todo)
        {
            if (todo != null)
            {
                return await _todoService.IsDueThisWeekAsync(todo.Id);
            }
            return false;
        }

        public async Task<DateTime?> GetNextDueDateAsync(ToDo todo)
        {
            if (todo != null)
            {
                return await _todoService.GetNextDueDateAsync(todo.Id);
            }
            return null;
        }

        private async Task LoadSubTasksAsync(string parentId)
        {
            if (!string.IsNullOrEmpty(parentId))
            {
                var subTasks = await _todoService.GetSubTasksAsync(parentId);
                SubTasks.Clear();
                foreach (var subTask in subTasks)
                {
                    SubTasks.Add(subTask);
                }
            }
        }

        private ToDo CreateNewTodo()
        {
            return new ToDo
            {
                Title = NewTodoTitle,
                Description = NewTodoDescription,
                StartDate = NewTodoStartDate,
                DueDate = NewTodoDueDate,
                Priority = NewTodoPriority,
                Category = NewTodoCategory,
                AssignedTo = NewTodoAssignee,
                IsRecurring = NewTodoIsRecurring,
                RecurrenceInterval = NewTodoRecurrenceInterval,
                RecurrenceUnit = NewTodoRecurrenceUnit
            };
        }

        private void ClearNewTodoFields()
        {
            NewTodoTitle = string.Empty;
            NewTodoDescription = string.Empty;
            NewTodoStartDate = null;
            NewTodoDueDate = null;
            NewTodoPriority = null;
            NewTodoCategory = null;
            NewTodoAssignee = null;
            NewTodoIsRecurring = false;
            NewTodoRecurrenceInterval = 1;
            NewTodoRecurrenceUnit = "day";
            NewTodoTag = string.Empty;
        }

        private void OnTodoChanged(object sender, TodoChangedEventArgs e)
        {
            // When a todo is changed through the service, update our collection manually
            // instead of using RefreshTodos which loaded from shared global state
            if (e.ChangeType == TodoChangeType.Created)
            {
                // Find the todo by ID
                var todo = _todoService.GetTodoByIdAsync(e.TodoId).Result;
                if (todo != null)
                {
                    Todos.Add(todo);
                }
            }
            else if (e.ChangeType == TodoChangeType.Updated || e.ChangeType == TodoChangeType.Modified)
            {
                // Find the todo by ID
                var todo = _todoService.GetTodoByIdAsync(e.TodoId).Result;
                if (todo != null)
                {
                    var existingIndex = Todos.IndexOf(Todos.FirstOrDefault(t => t.Id == e.TodoId));
                    if (existingIndex >= 0)
                    {
                        Todos[existingIndex] = todo;
                    }
                }
            }
            else if (e.ChangeType == TodoChangeType.Deleted)
            {
                var existingTodo = Todos.FirstOrDefault(t => t.Id == e.TodoId);
                if (existingTodo != null)
                {
                    Todos.Remove(existingTodo);
                }
            }
            
            // Apply any filters
            ApplyFilter();
        }

        public void ApplyFilter()
        {
            // Debug which filters are active
            System.Diagnostics.Debug.WriteLine($"Applying filters: ShowCompleted={ShowCompleted}, ShowCompletedItems={ShowCompletedItems}, ShowArchived={ShowArchived}, HideFutureItems={HideFutureItems}");
            
            // Make a copy of all todos before filtering
            var allTodos = new List<ToDo>(Todos);
            System.Diagnostics.Debug.WriteLine($"Total todos before filtering: {allTodos.Count}");
            
            var filteredTodos = allTodos.Where(todo =>
            {
                // Skip null items
                if (todo == null) 
                {
                    System.Diagnostics.Debug.WriteLine("Skipping null todo item");
                    return false;
                }
                
                // Text filter
                bool matchesText = string.IsNullOrEmpty(FilterText) ||
                    (todo.Title != null && todo.Title.Contains(FilterText, StringComparison.OrdinalIgnoreCase)) ||
                    (todo.Description != null && todo.Description.Contains(FilterText, StringComparison.OrdinalIgnoreCase));
                
                // Completed filter 
                bool matchesCompleted = ShowCompleted || !todo.IsCompleted;
                
                // Archive filter
                bool matchesArchive = ShowArchived || 
                    todo.FilePath == null || 
                    !todo.FilePath.EndsWith(".todo.archive");
                
                // Future filter
                bool matchesFuture = !HideFutureItems || !IsFutureItem(todo);
                
                bool passes = matchesText && matchesCompleted && matchesArchive && matchesFuture;
                
                if (!passes)
                {
                    System.Diagnostics.Debug.WriteLine($"Filtered out: '{todo.Title}' - Text:{matchesText}, Completed:{matchesCompleted}, Archive:{matchesArchive}, Future:{matchesFuture}");
                }
                
                return passes;
            }).ToList();
            
            System.Diagnostics.Debug.WriteLine($"Filtered todos count: {filteredTodos.Count}");
            
            // Create a new ObservableCollection with the filtered items
            // This approach is cleaner than trying to modify the existing collection
            var newCollection = new ObservableCollection<ToDo>(filteredTodos);
            
            // Replace the entire collection at once
            Todos = newCollection;
            
            // Force property changed notification for UI update
            OnPropertyChanged(nameof(Todos));
        }

        private bool IsFutureItem(ToDo todo)
        {
            // Handle null todo object
            if (todo == null)
                return false;
                
            // Consider an item "future" if its start date is in the future
            return todo.StartDate.HasValue && todo.StartDate.Value.Date > DateTime.Today;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                // Unsubscribe from events
                if (_todoService != null)
                {
                    _todoService.TodoChanged -= OnTodoChanged;
                }
            }
            _disposed = true;
        }

        ~ToDoViewModel()
        {
            Dispose();
        }

        public async Task<ObservableCollection<string>> GetAllTagsAsync()
        {
            var tags = await _todoService.GetAllTagsAsync();
            var collection = new ObservableCollection<string>();
            foreach (var tag in tags)
            {
                collection.Add(tag);
            }
            return collection;
        }

        public async Task<ObservableCollection<string>> GetAllPrioritiesAsync()
        {
            var priorities = await _todoService.GetAllPrioritiesAsync();
            var collection = new ObservableCollection<string>();
            foreach (var priority in priorities)
            {
                collection.Add(priority);
            }
            return collection;
        }

        public async Task<ObservableCollection<string>> GetAllCategoriesAsync()
        {
            var categories = await _todoService.GetAllCategoriesAsync();
            var collection = new ObservableCollection<string>();
            foreach (var category in categories)
            {
                collection.Add(category);
            }
            return collection;
        }

        public async Task<ObservableCollection<string>> GetAllAssigneesAsync()
        {
            var assignees = await _todoService.GetAllAssigneesAsync();
            var collection = new ObservableCollection<string>();
            foreach (var assignee in assignees)
            {
                collection.Add(assignee);
            }
            return collection;
        }

        public async Task LoadTodosFromFileAsync(string filePath)
        {
            System.Diagnostics.Debug.WriteLine($"========================================");
            System.Diagnostics.Debug.WriteLine($"Loading ToDos from file: {filePath}");
            
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"File path is null or empty");
                Todos.Clear();
                return;
            }
            
            if (!File.Exists(filePath))
            {
                System.Diagnostics.Debug.WriteLine($"File does not exist: {filePath}");
                Todos.Clear();
                return;
            }

            try
            {
                // Load todos directly from the file instead of using the service
                var json = await File.ReadAllTextAsync(filePath);
                System.Diagnostics.Debug.WriteLine($"File content length: {json.Length} bytes");
                
                if (string.IsNullOrWhiteSpace(json))
                {
                    System.Diagnostics.Debug.WriteLine($"File is empty or contains only whitespace");
                    Todos.Clear();
                    return;
                }
                
                // For debugging, print the entire JSON content and structure
                System.Diagnostics.Debug.WriteLine($"FULL JSON: {json}");
                
                // Try a different approach to read the JSON
                try 
                {
                    using (JsonDocument document = JsonDocument.Parse(json))
                    {
                        JsonElement root = document.RootElement;
                        
                        // Check if root is an array
                        if (root.ValueKind == JsonValueKind.Array)
                        {
                            System.Diagnostics.Debug.WriteLine($"JSON is an array with {root.GetArrayLength()} items");
                            
                            // Examine each item in the array
                            int index = 0;
                            foreach (JsonElement element in root.EnumerateArray())
                            {
                                System.Diagnostics.Debug.WriteLine($"Item {index}:");
                                
                                // Look for key properties
                                if (element.TryGetProperty("Title", out JsonElement titleElement))
                                {
                                    System.Diagnostics.Debug.WriteLine($"  Title: {titleElement}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("  No Title property found!");
                                }
                                
                                if (element.TryGetProperty("Description", out JsonElement descElement))
                                {
                                    System.Diagnostics.Debug.WriteLine($"  Description: {descElement}");
                                }
                                
                                if (element.TryGetProperty("IsCompleted", out JsonElement completedElement))
                                {
                                    System.Diagnostics.Debug.WriteLine($"  IsCompleted: {completedElement}");
                                }
                                
                                index++;
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("JSON root is not an array!");
                        }
                    }
                }
                catch (JsonException jex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON parsing error: {jex.Message}");
                }
                
                // Configure the JSON deserializer with appropriate options
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNamingPolicy = null // Use the property names as they are
                };
                
                // Deserialize the JSON into a list of ToDo objects
                var todos = JsonSerializer.Deserialize<List<ToDo>>(json, options);
                
                if (todos == null)
                {
                    System.Diagnostics.Debug.WriteLine("Deserialization returned null");
                    Todos.Clear();
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Deserialized {todos.Count} ToDo items");

                // Clear current list and create new one
                var newTodos = new ObservableCollection<ToDo>();
                
                foreach (var todo in todos)
                {
                    if (todo == null)
                    {
                        System.Diagnostics.Debug.WriteLine("Warning: Null todo item in deserialized list");
                        continue;
                    }
                    
                    // Ensure the FilePath property is set
                    if (string.IsNullOrEmpty(todo.FilePath))
                    {
                        todo.FilePath = filePath;
                    }
                    
                    // Inspect the todo item properties
                    var props = typeof(ToDo).GetProperties();
                    System.Diagnostics.Debug.WriteLine($"ToDo item details:");
                    foreach (var prop in props)
                    {
                        var value = prop.GetValue(todo);
                        System.Diagnostics.Debug.WriteLine($"  {prop.Name}: {(value == null ? "null" : value.ToString())}");
                    }
                    
                    // Add the todo to the collection
                    newTodos.Add(todo);
                }
                
                // Replace the collection
                Todos = newTodos;
                System.Diagnostics.Debug.WriteLine($"Replaced Todos collection with {Todos.Count} items");
                
                // Force property changed notification
                OnPropertyChanged(nameof(Todos));
                
                System.Diagnostics.Debug.WriteLine($"Final Todos count: {Todos.Count}");
                System.Diagnostics.Debug.WriteLine($"========================================");
            }
            catch (Exception ex)
            {
                // Handle any exceptions that might occur during loading
                System.Diagnostics.Debug.WriteLine($"Error loading ToDos: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _dialogService.ShowError("Error Loading ToDos", $"Could not load ToDos from file: {ex.Message}");
                Todos.Clear();
            }
        }

        public async Task ArchiveTodoAsync(ToDo todo)
        {
            if (todo != null)
            {
                await _todoService.ArchiveTodoAsync(todo.Id);
                await LoadFilteredTodosAsync();
            }
        }

        public void AddTodo()
        {
            var newTodo = CreateNewTodo();
            _todos.Add(newTodo);
            ClearNewTodoFields();
            SaveTodosToFileAsync(GetCurrentFilePath()).ConfigureAwait(false);
        }

        public void DeleteSubTask(ToDo subtask)
        {
            if (subtask == null) return;
            _todos.Remove(subtask);
            SaveTodosToFileAsync(GetCurrentFilePath()).ConfigureAwait(false);
        }

        public async Task SaveTodosAsync()
        {
            await SaveTodosToFileAsync(GetCurrentFilePath());
        }

        private void TestToDoSerialization()
        {
            try
            {
                // Create a test ToDo with known values
                var testTodo = new ToDo
                {
                    Title = "Test Todo",
                    Description = "This is a test todo item",
                    IsCompleted = false,
                    DueDate = DateTime.Now.AddDays(7),
                    Tags = new List<string> { "test", "validation" }
                };

                // Serialize it
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                string json = JsonSerializer.Serialize(testTodo, options);
                System.Diagnostics.Debug.WriteLine($"TEST SERIALIZATION: Serialized test ToDo to JSON:");
                System.Diagnostics.Debug.WriteLine(json);

                // Now deserialize it back
                var deserializedTodo = JsonSerializer.Deserialize<ToDo>(json, options);
                System.Diagnostics.Debug.WriteLine($"TEST DESERIALIZATION: Deserialized back to ToDo object:");
                System.Diagnostics.Debug.WriteLine($"Title: {deserializedTodo.Title}");
                System.Diagnostics.Debug.WriteLine($"Description: {deserializedTodo.Description}");
                System.Diagnostics.Debug.WriteLine($"IsCompleted: {deserializedTodo.IsCompleted}");
                
                // Add it to the collection to see if it displays
                Todos.Add(testTodo);
                System.Diagnostics.Debug.WriteLine("Added test ToDo to collection");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TEST SERIALIZATION ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine(ex.StackTrace);
            }
        }

        public void RefreshTodos()
        {
            // Instead of getting todos from a shared source, reload them from the file
            if (!string.IsNullOrEmpty(_todoService?.FilePath) && File.Exists(_todoService.FilePath))
            {
                System.Diagnostics.Debug.WriteLine($"RefreshTodos: Reloading from file {_todoService.FilePath}");
                LoadTodosFromFileAsync(_todoService.FilePath).ConfigureAwait(false);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("RefreshTodos: No file to reload from");
            }
        }
    }
} 