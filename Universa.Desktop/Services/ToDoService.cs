using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class ToDoService : IToDoService
    {
        private readonly IConfigurationService _configurationService;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly string _libraryPath;
        private string _filePath;
        private List<ToDo> _todos;

        public event EventHandler<TodoChangedEventArgs> TodoChanged;
        
        // Expose the file path
        public string FilePath => _filePath;

        public ToDoService(IConfigurationService configurationService, string filePath)
        {
            _configurationService = configurationService;
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };
            
            // Handle the case when configurationService is null
            if (_configurationService != null)
            {
                _libraryPath = _configurationService.GetValue<string>("LibraryPath");
            }
            else
            {
                // Use a default path if no configuration service is provided
                _libraryPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "Universa"
                );
                System.Diagnostics.Debug.WriteLine($"Using default library path: {_libraryPath}");
            }
            
            _filePath = filePath;
            _todos = new List<ToDo>();
            
            System.Diagnostics.Debug.WriteLine($"Created new ToDoService for file: {_filePath}");
            
            // Only load from file if one is specified and it exists
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                LoadTodos();
            }
        }

        private void LoadTodos()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        System.Diagnostics.Debug.WriteLine($"Warning: Empty JSON file: {_filePath}");
                        _todos = new List<ToDo>();
                        return;
                    }
                    
                    try
                    {
                        _todos = JsonSerializer.Deserialize<List<ToDo>>(json, _jsonOptions) ?? new List<ToDo>();
                        
                        // Ensure all todos have the correct file path set
                        foreach (var todo in _todos)
                        {
                            todo.FilePath = _filePath;
                            
                            // Make sure Tags is initialized
                            if (todo.Tags == null)
                            {
                                todo.Tags = new List<string>();
                            }
                            
                            // Make sure Id is set
                            if (string.IsNullOrEmpty(todo.Id))
                            {
                                todo.Id = Guid.NewGuid().ToString();
                            }
                            
                            // Ensure CreatedDate is valid
                            if (todo.CreatedDate == default)
                            {
                                todo.CreatedDate = DateTime.Now;
                            }
                        }
                        
                        System.Diagnostics.Debug.WriteLine($"Successfully loaded {_todos.Count} todos from {_filePath}");
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error deserializing JSON from {_filePath}: {ex.Message}");
                        _todos = new List<ToDo>();
                        
                        // Create a backup of the corrupted file
                        string backupPath = $"{_filePath}.bak.{DateTime.Now:yyyyMMddHHmmss}";
                        File.Copy(_filePath, backupPath);
                        System.Diagnostics.Debug.WriteLine($"Created backup of corrupted file: {backupPath}");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"ToDo file does not exist: {_filePath}");
                    _todos = new List<ToDo>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading ToDos: {ex.Message}");
                _todos = new List<ToDo>();
            }
        }

        private void SaveTodos()
        {
            try
            {
                // Check if all todos have the same file path
                var filePaths = _todos.Select(t => t.FilePath).Distinct().ToList();
                
                if (filePaths.Count == 1 && !string.IsNullOrEmpty(filePaths[0]))
                {
                    // All todos have the same file path, write to that path
                    var targetPath = filePaths[0];
                    System.Diagnostics.Debug.WriteLine($"SaveTodos: Writing {_todos.Count} todos to file: {targetPath}");
                    
                    var json = JsonSerializer.Serialize(_todos, _jsonOptions);
                    File.WriteAllText(targetPath, json);
                    
                    System.Diagnostics.Debug.WriteLine($"Wrote {_todos.Count} todos to {targetPath}, size: {new FileInfo(targetPath).Length} bytes");
                }
                else if (!string.IsNullOrEmpty(_filePath))
                {
                    // Use the service's file path
                    System.Diagnostics.Debug.WriteLine($"SaveTodos: Writing {_todos.Count} todos to service path: {_filePath}");
                    
                    var json = JsonSerializer.Serialize(_todos, _jsonOptions);
                    File.WriteAllText(_filePath, json);
                    
                    System.Diagnostics.Debug.WriteLine($"Wrote {_todos.Count} todos to service path {_filePath}, size: {new FileInfo(_filePath).Length} bytes");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("SaveTodos: No valid file path found to save todos");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in SaveTodos: {ex.Message}");
            }
        }

        private void NotifyTodoChanged(string todoId, TodoChangeType changeType)
        {
            TodoChanged?.Invoke(this, new TodoChangedEventArgs { TodoId = todoId, ChangeType = changeType });
        }

        public async Task<IEnumerable<ToDo>> GetAllTodosAsync()
        {
            return await Task.FromResult(_todos);
        }

        public async Task<ToDo> GetTodoByIdAsync(string id)
        {
            return await Task.FromResult(_todos.FirstOrDefault(t => t.Id == id));
        }

        public async Task<IEnumerable<ToDo>> GetTodosByTagAsync(string tag)
        {
            return await Task.FromResult(_todos.Where(t => t.Tags.Contains(tag)));
        }

        public async Task<IEnumerable<ToDo>> GetTodosByCategoryAsync(string category)
        {
            return await Task.FromResult(_todos.Where(t => t.Category == category));
        }

        public async Task<IEnumerable<ToDo>> GetTodosByPriorityAsync(string priority)
        {
            return await Task.FromResult(_todos.Where(t => t.Priority == priority));
        }

        public async Task<IEnumerable<ToDo>> GetTodosByAssigneeAsync(string assignee)
        {
            return await Task.FromResult(_todos.Where(t => t.AssignedTo == assignee));
        }

        public async Task<IEnumerable<ToDo>> GetOverdueTodosAsync()
        {
            return await Task.FromResult(_todos.Where(t => t.DueDate.HasValue && t.DueDate.Value < DateTime.Now && !t.IsCompleted));
        }

        public async Task<IEnumerable<ToDo>> GetDueTodayTodosAsync()
        {
            var today = DateTime.Today;
            return await Task.FromResult(_todos.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date == today));
        }

        public async Task<IEnumerable<ToDo>> GetDueThisWeekTodosAsync()
        {
            var today = DateTime.Today;
            var endOfWeek = today.AddDays(7 - (int)today.DayOfWeek);
            return await Task.FromResult(_todos.Where(t => t.DueDate.HasValue && t.DueDate.Value.Date <= endOfWeek));
        }

        public async Task<IEnumerable<ToDo>> GetCompletedTodosAsync()
        {
            return await Task.FromResult(_todos.Where(t => t.IsCompleted));
        }

        public async Task<IEnumerable<ToDo>> GetIncompleteTodosAsync()
        {
            return await Task.FromResult(_todos.Where(t => !t.IsCompleted));
        }

        public async Task<IEnumerable<ToDo>> GetRecurringTodosAsync()
        {
            return await Task.FromResult(_todos.Where(t => t.IsRecurring));
        }

        public async Task<IEnumerable<ToDo>> GetSubTasksAsync(string parentId)
        {
            return await Task.FromResult(_todos.Where(t => t.ParentId == parentId));
        }

        public async Task<ToDo> CreateTodoAsync(ToDo todo)
        {
            _todos.Add(todo);
            SaveTodos();
            NotifyTodoChanged(todo.Id, TodoChangeType.Created);
            return await Task.FromResult(todo);
        }

        public async Task UpdateTodoAsync(ToDo todo)
        {
            var existingTodo = _todos.FirstOrDefault(t => t.Id == todo.Id);
            if (existingTodo != null)
            {
                var index = _todos.IndexOf(existingTodo);
                _todos[index] = todo;
                SaveTodos();
                NotifyTodoChanged(todo.Id, TodoChangeType.Updated);
            }
            await Task.CompletedTask;
        }

        public async Task UpdateAllTodosAsync(IEnumerable<ToDo> todos)
        {
            if (todos == null)
                return;
                
            // Replace the entire collection
            _todos = new List<ToDo>(todos);
            
            // Save to the service's file
            SaveTodos();
            
            // Notify that multiple todos have changed
            NotifyTodoChanged("all", TodoChangeType.Updated);
            
            await Task.CompletedTask;
        }

        public async Task DeleteTodoAsync(string id)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == id);
            if (todo != null)
            {
                _todos.Remove(todo);
                SaveTodos();
                NotifyTodoChanged(id, TodoChangeType.Deleted);
            }
            await Task.CompletedTask;
        }

        public async Task CompleteTodoAsync(string id)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == id);
            if (todo != null)
            {
                todo.IsCompleted = true;
                todo.CompletedDate = DateTime.Now;
                SaveTodos();
                NotifyTodoChanged(id, TodoChangeType.Completed);
            }
            await Task.CompletedTask;
        }

        public async Task UncompleteTodoAsync(string id)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == id);
            if (todo != null)
            {
                todo.IsCompleted = false;
                todo.CompletedDate = null;
                SaveTodos();
                NotifyTodoChanged(id, TodoChangeType.Modified);
            }
            await Task.CompletedTask;
        }

        public async Task ArchiveTodoAsync(string id)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == id);
            if (todo != null)
            {
                // Generate the archive file path based on the original file path
                var originalFilePath = todo.FilePath;
                var archiveFilePath = originalFilePath + ".archive";
                
                System.Diagnostics.Debug.WriteLine($"Archiving todo '{todo.Title}' from {originalFilePath} to {archiveFilePath}");
                
                // Ensure the todo is marked as completed
                todo.IsCompleted = true;
                if (!todo.CompletedDate.HasValue)
                {
                    todo.CompletedDate = DateTime.Now;
                }
                
                // Read existing archived todos or create a new list
                List<ToDo> archivedTodos = new List<ToDo>();
                if (File.Exists(archiveFilePath))
                {
                    try
                    {
                        var json = await File.ReadAllTextAsync(archiveFilePath);
                        archivedTodos = JsonSerializer.Deserialize<List<ToDo>>(json, _jsonOptions) ?? new List<ToDo>();
                    }
                    catch (Exception ex)
                    {
                        // If there's an error reading the file, log and use a new list
                        System.Diagnostics.Debug.WriteLine($"Error reading archive file: {ex.Message}");
                        archivedTodos = new List<ToDo>();
                    }
                }
                
                // Store the original file path in a special property
                // so we can maintain the connection between the original and archived todo
                todo.FilePath = archiveFilePath;
                
                // Add this todo to the archive
                archivedTodos.Add(todo);
                
                // Save the archive file
                var archiveJson = JsonSerializer.Serialize(archivedTodos, _jsonOptions);
                await File.WriteAllTextAsync(archiveFilePath, archiveJson);
                
                // Remove the todo from the active list
                _todos.Remove(todo);
                SaveTodos();
                
                NotifyTodoChanged(id, TodoChangeType.Archived);
                
                // Notify that a todo was moved to the archive
                System.Diagnostics.Debug.WriteLine($"Todo '{todo.Title}' successfully archived");
            }
            
            await Task.CompletedTask;
        }

        public async Task AddSubTaskAsync(string parentId, ToDo subTask)
        {
            var parentTodo = _todos.FirstOrDefault(t => t.Id == parentId);
            if (parentTodo != null)
            {
                subTask.ParentId = parentId;
                _todos.Add(subTask);
                SaveTodos();
                NotifyTodoChanged(parentId, TodoChangeType.SubTaskAdded);
            }
            await Task.CompletedTask;
        }

        public async Task RemoveSubTaskAsync(string parentId, string subTaskId)
        {
            var subTask = _todos.FirstOrDefault(t => t.Id == subTaskId && t.ParentId == parentId);
            if (subTask != null)
            {
                _todos.Remove(subTask);
                SaveTodos();
                NotifyTodoChanged(parentId, TodoChangeType.SubTaskRemoved);
            }
            await Task.CompletedTask;
        }

        public async Task AddTagAsync(string todoId, string tag)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            if (todo != null && !todo.Tags.Contains(tag))
            {
                todo.Tags.Add(tag);
                SaveTodos();
                NotifyTodoChanged(todoId, TodoChangeType.TagAdded);
            }
            await Task.CompletedTask;
        }

        public async Task RemoveTagAsync(string todoId, string tag)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            if (todo != null && todo.Tags.Contains(tag))
            {
                todo.Tags.Remove(tag);
                SaveTodos();
                NotifyTodoChanged(todoId, TodoChangeType.TagRemoved);
            }
            await Task.CompletedTask;
        }

        public async Task<bool> HasTagAsync(string todoId, string tag)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            return await Task.FromResult(todo?.Tags.Contains(tag) ?? false);
        }

        public async Task<IEnumerable<string>> GetAllTagsAsync()
        {
            return await Task.FromResult(_todos.SelectMany(t => t.Tags).Distinct());
        }

        public async Task<IEnumerable<string>> GetAllCategoriesAsync()
        {
            return await Task.FromResult(_todos.Select(t => t.Category).Where(c => !string.IsNullOrEmpty(c)).Distinct());
        }

        public async Task<IEnumerable<string>> GetAllPrioritiesAsync()
        {
            return await Task.FromResult(_todos.Select(t => t.Priority).Where(p => !string.IsNullOrEmpty(p)).Distinct());
        }

        public async Task<IEnumerable<string>> GetAllAssigneesAsync()
        {
            return await Task.FromResult(_todos.Select(t => t.AssignedTo).Where(a => !string.IsNullOrEmpty(a)).Distinct());
        }

        public async Task<bool> IsOverdueAsync(string todoId)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            return await Task.FromResult(todo?.DueDate.HasValue == true && todo.DueDate.Value < DateTime.Now && !todo.IsCompleted);
        }

        public async Task<bool> IsDueTodayAsync(string todoId)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            return await Task.FromResult(todo?.DueDate.HasValue == true && todo.DueDate.Value.Date == DateTime.Today);
        }

        public async Task<bool> IsDueThisWeekAsync(string todoId)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            if (todo?.DueDate.HasValue != true) return await Task.FromResult(false);

            var today = DateTime.Today;
            var endOfWeek = today.AddDays(7 - (int)today.DayOfWeek);
            return await Task.FromResult(todo.DueDate.Value.Date <= endOfWeek);
        }

        public async Task<DateTime?> GetNextDueDateAsync(string todoId)
        {
            var todo = _todos.FirstOrDefault(t => t.Id == todoId);
            if (todo?.IsRecurring != true) return await Task.FromResult(todo?.DueDate);

            var baseDate = todo.CompletedDate ?? DateTime.Now;
            var nextDueDate = todo.RecurrenceUnit?.ToLower() switch
            {
                "hour" => baseDate.AddHours(todo.RecurrenceInterval),
                "day" => baseDate.AddDays(todo.RecurrenceInterval),
                "week" => baseDate.AddDays(todo.RecurrenceInterval * 7),
                "month" => baseDate.AddMonths(todo.RecurrenceInterval),
                "year" => baseDate.AddYears(todo.RecurrenceInterval),
                _ => baseDate.AddDays(todo.RecurrenceInterval)
            };

            return await Task.FromResult(nextDueDate);
        }

        public void UpdateFilePath(string newPath)
        {
            if (string.IsNullOrEmpty(newPath))
            {
                return;
            }
            
            // Update service file path
            _filePath = newPath;
            
            // Update all todos to use the new file path
            foreach (var todo in _todos)
            {
                todo.FilePath = newPath;
            }
            
            // Save to the new location
            SaveTodos();
            System.Diagnostics.Debug.WriteLine($"Updated file path to {newPath} for {_todos.Count} todos");
        }
    }
}
