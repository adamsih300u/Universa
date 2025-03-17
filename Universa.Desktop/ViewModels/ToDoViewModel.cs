using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Windows.Input;
using Universa.Desktop.Library;
using System.IO;
using System.Text.Json;
using Universa.Desktop.Commands;

namespace Universa.Desktop.ViewModels
{
    public class ToDoViewModel : INotifyPropertyChanged, IDisposable
    {
        private ObservableCollection<ToDo> _todos;
        private ToDo _selectedTodo;
        private string _filterText;
        private bool _showCompleted;
        private bool _showArchived;
        private readonly ToDoTracker _todoTracker;
        private bool _disposed;

        public event PropertyChangedEventHandler PropertyChanged;

        public ToDoViewModel()
        {
            // Temporarily use singleton while we transition
            _todoTracker = ToDoTracker.Instance;
            _todos = new ObservableCollection<ToDo>();
            
            // Initialize commands
            AddTodoCommand = new RelayCommand(param => ExecuteAddTodo());
            DeleteTodoCommand = new RelayCommand(param => ExecuteDeleteTodo(), param => CanDeleteTodo());
            CompleteTodoCommand = new RelayCommand(param => ExecuteCompleteTodo(), param => CanCompleteTodo());
            ArchiveTodoCommand = new RelayCommand(param => ExecuteArchiveTodo(), param => CanArchiveTodo());
            
            // Subscribe to changes
            _todoTracker.TodosChanged += RefreshTodos;
            
            // Initial load
            RefreshTodos();
        }

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

        public ToDo SelectedTodo
        {
            get => _selectedTodo;
            set
            {
                if (_selectedTodo != value)
                {
                    _selectedTodo = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
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

        public ICommand AddTodoCommand { get; }
        public ICommand DeleteTodoCommand { get; }
        public ICommand CompleteTodoCommand { get; }
        public ICommand ArchiveTodoCommand { get; }

        private void ExecuteAddTodo()
        {
            var newTodo = new ToDo 
            { 
                Title = "New Todo",
                StartDate = DateTime.Now
            };

            var libraryPath = Configuration.Instance.LibraryPath;
            if (string.IsNullOrEmpty(libraryPath))
                return;

            var todoPath = Path.Combine(libraryPath, "todos");
            Directory.CreateDirectory(todoPath);

            var fileName = $"{DateTime.Now:yyyy-MM-dd-HH-mm-ss}-{Guid.NewGuid():N}.todo";
            var filePath = Path.Combine(todoPath, fileName);

            var json = JsonSerializer.Serialize(new[] { newTodo }, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            
            File.WriteAllText(filePath, json);
            _todoTracker.ScanTodoFiles(todoPath);
            SelectedTodo = newTodo;
        }

        private void ExecuteDeleteTodo()
        {
            if (SelectedTodo?.FilePath == null) return;

            try
            {
                if (File.Exists(SelectedTodo.FilePath))
                {
                    File.Delete(SelectedTodo.FilePath);
                    _todoTracker.ScanTodoFiles(Path.GetDirectoryName(SelectedTodo.FilePath));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error deleting todo: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private bool CanDeleteTodo() => SelectedTodo != null;

        private void ExecuteCompleteTodo()
        {
            if (SelectedTodo?.FilePath == null) return;

            try
            {
                // Toggle completion status
                SelectedTodo.IsCompleted = !SelectedTodo.IsCompleted;
                
                // Make sure tags collection exists
                if (SelectedTodo.Tags == null)
                {
                    SelectedTodo.Tags = new List<string>();
                }

                if (SelectedTodo.IsCompleted)
                {
                    // If completing the task, set the completion date to now
                    var now = DateTime.Now;
                    SelectedTodo.CompletedDate = now;
                    
                    // Add Completed tag with current date/time
                    var completedTag = $"Completed: {now:yyyy-MM-dd HH:mm:ss}";
                    if (!SelectedTodo.Tags.Any(t => t.StartsWith("Completed:")))
                    {
                        SelectedTodo.Tags.Add(completedTag);
                    }
                    else
                    {
                        // Replace existing Completed tag
                        for (int i = 0; i < SelectedTodo.Tags.Count; i++)
                        {
                            if (SelectedTodo.Tags[i].StartsWith("Completed:"))
                            {
                                SelectedTodo.Tags[i] = completedTag;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // If un-completing the task, store the previous completion date in tags
                    if (SelectedTodo.CompletedDate.HasValue)
                    {
                        var prevCompletionDate = SelectedTodo.CompletedDate.Value;
                        var prevCompletionTag = $"Previous Completion: {prevCompletionDate:yyyy-MM-dd HH:mm:ss}";
                        
                        // Remove existing "Completed:" tag and add to previous completions
                        string completedTag = null;
                        for (int i = SelectedTodo.Tags.Count - 1; i >= 0; i--)
                        {
                            if (SelectedTodo.Tags[i].StartsWith("Completed:"))
                            {
                                completedTag = SelectedTodo.Tags[i];
                                SelectedTodo.Tags.RemoveAt(i);
                                break;
                            }
                        }
                        
                        // Add previous completion tag if we had a completion date
                        if (!SelectedTodo.Tags.Any(t => t.Equals(prevCompletionTag)))
                        {
                            SelectedTodo.Tags.Add(prevCompletionTag);
                        }
                        
                        // Clear the completed date
                        SelectedTodo.CompletedDate = null;
                    }
                }

                SaveTodoChanges(SelectedTodo);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error updating todo: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private bool CanCompleteTodo() => SelectedTodo != null;

        private void ExecuteArchiveTodo()
        {
            if (SelectedTodo?.FilePath == null) return;

            try
            {
                var newPath = SelectedTodo.FilePath + ".archive";
                File.Move(SelectedTodo.FilePath, newPath);
                _todoTracker.ScanTodoFiles(Path.GetDirectoryName(SelectedTodo.FilePath));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error archiving todo: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private bool CanArchiveTodo() => SelectedTodo != null && SelectedTodo.IsCompleted;

        private void SaveTodoChanges(ToDo todo)
        {
            if (string.IsNullOrEmpty(todo.FilePath)) return;

            try
            {
                var todos = new List<ToDo> { todo };
                var json = JsonSerializer.Serialize(todos, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                File.WriteAllText(todo.FilePath, json);
                _todoTracker.ScanTodoFiles(Path.GetDirectoryName(todo.FilePath));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving todo changes: {ex.Message}", "Error", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public void RefreshTodos()
        {
            var todos = _todoTracker.GetAllTodos();
            ApplyFilter(todos);
        }

        private void ApplyFilter(IEnumerable<ToDo> source = null)
        {
            var todos = source ?? _todoTracker.GetAllTodos();

            var filtered = todos.Where(t => 
                (ShowCompleted || !t.IsCompleted) &&
                (ShowArchived || !t.FilePath.EndsWith(".archive")) &&
                (string.IsNullOrEmpty(FilterText) || 
                 t.Title?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true ||
                 t.Description?.Contains(FilterText, StringComparison.OrdinalIgnoreCase) == true)
            ).ToList();

            Todos.Clear();
            foreach (var todo in filtered.OrderByDescending(t => t.StartDate))
            {
                Todos.Add(todo);
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Unsubscribe from events
                    _todoTracker.TodosChanged -= RefreshTodos;
                }
                _disposed = true;
            }
        }

        ~ToDoViewModel()
        {
            Dispose(false);
        }
    }
} 