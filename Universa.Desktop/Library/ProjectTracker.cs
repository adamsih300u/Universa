using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Universa.Desktop.Models;
using System.Windows.Threading;

namespace Universa.Desktop.Library
{
    public class ProjectTracker : IDisposable
    {
        private static ProjectTracker _instance;
        private FileSystemWatcher _watcher;
        private Dictionary<string, Project> _projectFiles = new Dictionary<string, Project>();
        private HashSet<string> _categories = new HashSet<string>();
        private bool _disposed = false;
        public event Action ProjectsChanged;
        public event Action CategoriesChanged;

        public static ProjectTracker Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new ProjectTracker();
                }
                return _instance;
            }
        }

        private ProjectTracker()
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
                Filter = "*.project*",
                IncludeSubdirectories = true,
                EnableRaisingEvents = false
            };

            _watcher.Created += ProjectFile_Changed;
            _watcher.Changed += ProjectFile_Changed;
            _watcher.Deleted += ProjectFile_Changed;
            _watcher.Renamed += ProjectFile_Changed;

            try
            {
                _watcher.EnableRaisingEvents = true;
                ScanProjectFiles(libraryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error initializing ProjectTracker: {ex.Message}");
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
                        _watcher.Created -= ProjectFile_Changed;
                        _watcher.Changed -= ProjectFile_Changed;
                        _watcher.Deleted -= ProjectFile_Changed;
                        _watcher.Renamed -= ProjectFile_Changed;
                        _watcher.Dispose();
                        _watcher = null;
                    }
                }
                _disposed = true;
            }
        }

        ~ProjectTracker()
        {
            Dispose(false);
        }

        private void ProjectFile_Changed(object sender, FileSystemEventArgs e)
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                if (!dispatcher.CheckAccess())
                {
                    dispatcher.Invoke(() => ProjectFile_Changed(sender, e));
                    return;
                }

                var path = Path.GetDirectoryName(e.FullPath);
                if (string.IsNullOrEmpty(path)) return;

                ScanProjectFiles(path);
                ProjectsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ProjectFile_Changed: {ex.Message}");
            }
        }

        public void ScanProjectFiles(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            try
            {
                // Create new collections to avoid modifying while in use
                var newProjectFiles = new Dictionary<string, Project>();
                var newCategories = new HashSet<string>();
                
                // Get both .project and .project.archive files
                var projectFiles = Directory.GetFiles(path, "*.project", SearchOption.AllDirectories);
                var archiveFiles = Directory.GetFiles(path, "*.project.archive", SearchOption.AllDirectories);
                var allFiles = projectFiles.Concat(archiveFiles);
                
                foreach (var file in allFiles)
                {
                    try
                    {
                        if (!File.Exists(file)) continue;

                        var content = File.ReadAllText(file);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };
                        var project = JsonSerializer.Deserialize<Project>(content, options);
                        if (project != null)
                        {
                            project.FilePath = file;
                            newProjectFiles[file] = project;

                            // Add category to the set if it exists
                            if (!string.IsNullOrEmpty(project.Category))
                            {
                                newCategories.Add(project.Category);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error reading project file {file}: {ex.Message}");
                    }
                }

                // Atomic swap of collections
                lock (_projectFiles)
                {
                    _projectFiles.Clear();
                    foreach (var kvp in newProjectFiles)
                    {
                        _projectFiles[kvp.Key] = kvp.Value;
                    }
                }

                lock (_categories)
                {
                    _categories.Clear();
                    foreach (var category in newCategories)
                    {
                        _categories.Add(category);
                    }
                }

                ProjectsChanged?.Invoke();
                CategoriesChanged?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning project files: {ex.Message}");
            }
        }

        public bool HasProjectFiles()
        {
            return _projectFiles.Any();
        }

        public List<Project> GetAllProjects()
        {
            return _projectFiles.Values.ToList();
        }

        public List<string> GetAllCategories()
        {
            return _categories.OrderBy(c => c).ToList();
        }

        public List<Project> GetProjectsByCategory(string category)
        {
            return _projectFiles.Values
                .Where(p => p.Category == category)
                .ToList();
        }

        public List<Project> GetDependentProjects(string projectFilePath)
        {
            return _projectFiles.Values
                .Where(p => p.Dependencies.Any(d => d.FilePath == projectFilePath))
                .ToList();
        }

        public List<Project> GetProjectDependencies(string projectFilePath)
        {
            var project = _projectFiles.TryGetValue(projectFilePath, out var value) ? value : null;
            if (project == null) return new List<Project>();

            return project.Dependencies
                .Select(dep => _projectFiles.TryGetValue(dep.FilePath, out var p) ? p : null)
                .Where(p => p != null)
                .ToList();
        }
    }
} 