using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Universa.Desktop.Models;

namespace Universa.Desktop.Library
{
    public class ToDoTracker : IDisposable
    {
        private static ToDoTracker _instance;
        private FileSystemWatcher _watcher;
        private Dictionary<string, ToDo> _todoFiles = new Dictionary<string, ToDo>();
        private bool _disposed = false;
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

        public void Initialize(string libraryPath)
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
                _watcher.EnableRaisingEvents = true;
                ScanTodoFiles(libraryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ToDoTracker: {ex.Message}");
            }
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
                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = false;
                        _watcher.Created -= TodoFile_Changed;
                        _watcher.Changed -= TodoFile_Changed;
                        _watcher.Deleted -= TodoFile_Changed;
                        _watcher.Renamed -= TodoFile_Changed;
                        _watcher.Dispose();
                        _watcher = null;
                    }
                }
                _disposed = true;
            }
        }

        ~ToDoTracker()
        {
            Dispose(false);
        }

        private void TodoFile_Changed(object sender, FileSystemEventArgs e)
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                if (!dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() => TodoFile_Changed(sender, e));
                    return;
                }

                var path = Path.GetDirectoryName(e.FullPath);
                if (string.IsNullOrEmpty(path)) return;

                ScanTodoFiles(path);
                TodosChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in TodoFile_Changed: {ex.Message}");
            }
        }

        public void ScanTodoFiles(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"\n=== Starting ToDo file scan in {path} ===");
                
                // Create a new dictionary to avoid modifying the collection while it might be in use
                var newTodoFiles = new Dictionary<string, ToDo>();
                
                var todoFiles = Directory.GetFiles(path, "*.todo", SearchOption.AllDirectories);
                var archiveFiles = Directory.GetFiles(path, "*.todo.archive", SearchOption.AllDirectories);
                var allFiles = todoFiles.Concat(archiveFiles);
                
                System.Diagnostics.Debug.WriteLine($"Found {todoFiles.Length} .todo files and {archiveFiles.Length} .todo.archive files");
                
                foreach (var file in allFiles)
                {
                    try
                    {
                        if (!File.Exists(file)) continue;

                        var content = File.ReadAllText(file);
                        if (string.IsNullOrWhiteSpace(content) || content.Length <= 2)
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipping empty or invalid file: {file}");
                            continue;
                        }

                        System.Diagnostics.Debug.WriteLine($"\nProcessing file: {file}");
                        System.Diagnostics.Debug.WriteLine($"File content length: {content.Length} characters");
                        
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            WriteIndented = true,
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
                            // Skip this file and continue with others
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading todo file {file}: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        // Skip this file and continue with others
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

                System.Diagnostics.Debug.WriteLine($"\nScan complete. Loaded {_todoFiles.Count} ToDo items");
                TodosChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning todo files: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
    }
} 