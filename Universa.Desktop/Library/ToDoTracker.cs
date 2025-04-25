using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Universa.Desktop.Models;
using System.Threading.Tasks;

namespace Universa.Desktop.Library
{
    public class ToDoTracker : IDisposable
    {
        private static ToDoTracker _instance;
        private FileSystemWatcher _watcher;
        private Dictionary<string, ToDo> _todoFiles = new Dictionary<string, ToDo>();
        private bool _disposed = false;
        private bool _isScanning = false;
        private DateTime _lastScanTime = DateTime.MinValue;
        private const int SCAN_CACHE_DURATION_SECONDS = 5;
        public event Action TodosChanged;

        public static ToDoTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ToDoTracker();
                }
                return _instance;
            }
        }

        private ToDoTracker()
        {
        }

        public async Task InitializeAsync(string libraryPath)
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            if (string.IsNullOrEmpty(libraryPath) || !Directory.Exists(libraryPath))
            {
                return;
            }

            _watcher = new FileSystemWatcher(libraryPath)
            {
                Filter = "*.todo*",
                IncludeSubdirectories = true,
                EnableRaisingEvents = false
            };

            _watcher.Created += TodoFile_Changed;
            _watcher.Changed += TodoFile_Changed;
            _watcher.Deleted += TodoFile_Changed;
            _watcher.Renamed += TodoFile_Changed;

            try
            {
                await ScanTodoFilesAsync(libraryPath);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ToDoTracker: {ex.Message}");
            }
        }

        private async void TodoFile_Changed(object sender, FileSystemEventArgs e)
        {
            // Debounce rapid file changes
            await Task.Delay(500);
            await ScanTodoFilesAsync(Path.GetDirectoryName(e.FullPath));
        }

        public async Task ScanTodoFilesAsync(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            // Check if we're already scanning or if the cache is still valid
            if (_isScanning || (DateTime.Now - _lastScanTime).TotalSeconds < SCAN_CACHE_DURATION_SECONDS)
            {
                return;
            }

            try
            {
                _isScanning = true;
                System.Diagnostics.Debug.WriteLine($"\n=== Starting ToDo file scan in {path} ===");
                
                // Create a new dictionary to avoid modifying the collection while it might be in use
                var newTodoFiles = new Dictionary<string, ToDo>();
                
                // Use async file operations
                var todoFiles = await Task.Run(() => Directory.GetFiles(path, "*.todo", SearchOption.AllDirectories));
                var archiveFiles = await Task.Run(() => Directory.GetFiles(path, "*.todo.archive", SearchOption.AllDirectories));
                var allFiles = todoFiles.Concat(archiveFiles);
                
                System.Diagnostics.Debug.WriteLine($"Found {todoFiles.Length} .todo files and {archiveFiles.Length} .todo.archive files");
                
                foreach (var file in allFiles)
                {
                    try
                    {
                        var content = await File.ReadAllTextAsync(file);
                        if (string.IsNullOrWhiteSpace(content))
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping empty or invalid file: {file}");
                            continue;
                        }

                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            AllowTrailingCommas = true,
                            ReadCommentHandling = JsonCommentHandling.Skip
                        };

                        try
                        {
                            // Try to deserialize as an array of ToDo objects
                            var todos = JsonSerializer.Deserialize<List<ToDo>>(content, options);
                            if (todos != null && todos.Any())
                            {
                                System.Diagnostics.Debug.WriteLine($"Successfully loaded {todos.Count} ToDos from {file}");
                                foreach (var todo in todos)
                                {
                                    if (todo != null && !string.IsNullOrWhiteSpace(todo.Title))
                                    {
                                        todo.FilePath = file;
                                        var key = $"{file}#{todo.Title}";
                                        newTodoFiles[key] = todo;
                                        System.Diagnostics.Debug.WriteLine($"Added ToDo: Title='{todo.Title}', FilePath='{todo.FilePath}', IsCompleted={todo.IsCompleted}");
                                    }
                                }
                            }
                            else
                            {
                                // Try to deserialize as a single ToDo object as fallback
                                var todo = JsonSerializer.Deserialize<ToDo>(content, options);
                                if (todo != null && !string.IsNullOrWhiteSpace(todo.Title))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Successfully loaded single ToDo: Title='{todo.Title}'");
                                    todo.FilePath = file;
                                    newTodoFiles[file] = todo;
                                }
                            }
                        }
                        catch (JsonException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error parsing JSON in file {file}: {ex.Message}");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading todo file {file}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        continue;
                    }
                }

                // Atomic swap of the dictionary
                lock (_todoFiles)
                {
                    _todoFiles.Clear();
                    foreach (var kvp in newTodoFiles)
                    {
                        _todoFiles[kvp.Key] = kvp.Value;
                    }
                }

                _lastScanTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"\nScan complete. Loaded {_todoFiles.Count} ToDo items");
                TodosChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning todo files: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isScanning = false;
            }
        }

        public List<ToDo> GetAllTodos()
        {
            lock (_todoFiles)
            {
                return new List<ToDo>(_todoFiles.Values);
            }
        }

        public ToDo GetTodo(string filePath)
        {
            lock (_todoFiles)
            {
                return _todoFiles.TryGetValue(filePath, out var todo) ? todo : null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }
                _disposed = true;
            }
        }
    }
} 