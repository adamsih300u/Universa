using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Linq;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class LibraryManager : INotifyPropertyChanged
    {
        private static readonly Lazy<LibraryManager> _instance = new Lazy<LibraryManager>(() => new LibraryManager());
        private bool _isInitialized;
        private ObservableCollection<LibraryItem> _items;
        private string _currentPath;

        private static readonly HashSet<string> VersionedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Documents
            ".md",    // Markdown
            ".txt",   // Text files
            ".rtf",   // Rich Text Format

            // Databases
            ".db",    // SQLite Database

            // Encrypted Files
            ".edb",   // Encrypted database
            ".emd",   // Encrypted markdown document
            ".eprj",  // Encrypted project file

            // Project Management
            ".org",   // Org-mode files
            ".prj",   // Project file
            ".todo",  // Todo list (legacy)
            ".task"   // Task list
        };

        private static readonly HashSet<string> NonVersionedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Calendar and Reminder files
            ".ics",   // iCalendar
            ".ical",  // iCalendar
            ".rem",   // Reminder
            ".cal",   // Calendar
            ".evt"    // Event
        };

        public static LibraryManager Instance => _instance.Value;

        public ObservableCollection<LibraryItem> Items
        {
            get => _items;
            private set
            {
                _items = value;
                OnPropertyChanged();
            }
        }

        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                if (_currentPath != value)
                {
                    _currentPath = value;
                    OnPropertyChanged();
                    RefreshItems();
                }
            }
        }

        private string HistoryPath => 
            string.IsNullOrEmpty(Configuration.Instance.LibraryPath) 
                ? null 
                : Path.Combine(Configuration.Instance.LibraryPath, ".versions");

        private LibraryManager()
        {
            _items = new ObservableCollection<LibraryItem>();
        }

        public bool IsVersionedFile(string filePath)
        {
            var extension = Path.GetExtension(filePath);
            var isVersioned = VersionedExtensions.Contains(extension) && !NonVersionedExtensions.Contains(extension);
            System.Diagnostics.Debug.WriteLine($"[LibraryManager] Checking if file is versioned - Path: {filePath}, Extension: {extension}, IsVersioned: {isVersioned}");
            return isVersioned;
        }

        private void EnsureDirectoryExists(string path, bool hidden = false)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
                if (!hidden)
                {
                    // Remove any hidden attribute that might have been automatically set
                    var dirInfo = new DirectoryInfo(path);
                    dirInfo.Attributes &= ~FileAttributes.Hidden;
                }
            }
        }

        private void SaveFileVersion(string filePath)
        {
            try
            {
                if (!File.Exists(filePath) || !IsVersionedFile(filePath)) return;

                var relativePath = GetRelativePath(filePath);
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var historyFilePath = Path.Combine(
                    HistoryPath,
                    $"{relativePath}.{timestamp}"
                );

                // Create directory structure in history folder
                var historyDir = Path.GetDirectoryName(historyFilePath);
                if (!string.IsNullOrEmpty(historyDir))
                {
                    EnsureDirectoryExists(historyDir);
                }

                // Copy current file to history
                File.Copy(filePath, historyFilePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving file version: {ex}");
            }
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            try
            {
                // Validate library path configuration before attempting versioned file operations
                if (string.IsNullOrEmpty(Configuration.Instance.LibraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("Warning: LibraryPath not configured, performing simple file move without versioning.");
                    // For non-configured library, just do a simple move
                    File.Move(sourcePath, destinationPath);
                    return;
                }
                
                // Save current version before moving if it's a versioned file
                if (IsVersionedFile(sourcePath))
                {
                    var sourceRelativePath = GetRelativePath(sourcePath);
                    var destRelativePath = GetRelativePath(destinationPath);
                    
                    // Check if history path is available before trying to access version files
                    var historyPath = HistoryPath;
                    if (string.IsNullOrEmpty(historyPath) || !Directory.Exists(historyPath))
                    {
                        // No version history available, just do simple move
                        File.Move(sourcePath, destinationPath);
                        return;
                    }
                    
                    var historyFiles = Directory.GetFiles(historyPath, $"{sourceRelativePath}.*", SearchOption.AllDirectories);

                    // Move the main file first
                    File.Move(sourcePath, destinationPath);

                    // Move all version files
                    foreach (var historyFile in historyFiles)
                    {
                        var fileName = Path.GetFileName(historyFile);
                        var newHistoryPath = Path.Combine(
                            historyPath,
                            Path.GetDirectoryName(destRelativePath) ?? "",
                            fileName.Replace(sourceRelativePath, destRelativePath)
                        );

                        // Create the directory structure if it doesn't exist
                        Directory.CreateDirectory(Path.GetDirectoryName(newHistoryPath));
                        File.Move(historyFile, newHistoryPath);
                    }
                }
                else
                {
                    // For non-versioned files, just do a simple move
                    File.Move(sourcePath, destinationPath);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error moving file: {ex.Message}",
                    "Move Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
                throw; // Re-throw to let caller handle the error
            }
        }

        public List<(string Path, DateTime Timestamp)> GetFileHistory(string filePath)
        {
            try
            {
                if (!IsVersionedFile(filePath))
                    return new List<(string, DateTime)>();

                var relativePath = GetRelativePath(filePath);
                var historyFiles = Directory.GetFiles(HistoryPath, $"{relativePath}.*", SearchOption.AllDirectories);

                return historyFiles.Select(f =>
                {
                    var timestamp = Path.GetExtension(f).TrimStart('.');
                    return (f, DateTime.ParseExact(timestamp, "yyyyMMddHHmmss", null));
                })
                .OrderByDescending(x => x.Item2)
                .ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting file history: {ex}");
                return new List<(string, DateTime)>();
            }
        }

        public void RestoreVersion(string filePath, string historyPath)
        {
            try
            {
                if (!File.Exists(historyPath) || !IsVersionedFile(filePath)) return;

                // Save current version before restoring
                SaveFileVersion(filePath);

                // Restore the selected version
                File.Copy(historyPath, filePath, true);

                RefreshItems();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error restoring version: {ex}");
            }
        }

        public void Initialize()
        {
            if (_isInitialized) return;

            // Create necessary directories
            EnsureDirectoryExists(Configuration.Instance.LibraryPath);
            EnsureDirectoryExists(HistoryPath);
            EnsureDirectoryExists(GetCachePath());
            EnsureDirectoryExists(GetThemesPath());
            EnsureDirectoryExists(GetDownloadsPath());
            EnsureDirectoryExists(GetLogsPath());

            try
            {
                // Create library directory if it doesn't exist
                if (!string.IsNullOrEmpty(Configuration.Instance.LibraryPath))
                {
                    Directory.CreateDirectory(Configuration.Instance.LibraryPath);
                    
                    // Create history directory without forcing hidden attribute
                    EnsureDirectoryExists(HistoryPath);

                    // Create application data directories
                    var appDataPath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Universa"
                    );
                    Directory.CreateDirectory(appDataPath);
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Cache"));
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Downloads"));
                    Directory.CreateDirectory(Path.Combine(appDataPath, "Logs"));

                    _isInitialized = true;
                    CurrentPath = Configuration.Instance.LibraryPath;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error initializing library: {ex.Message}",
                    "Initialization Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }
        }

        public void RefreshItems()
        {
            Items.Clear();

            if (string.IsNullOrEmpty(CurrentPath) || !Directory.Exists(CurrentPath))
                return;

            try
            {
                // Add parent directory item if not at root
                if (!IsRootPath(CurrentPath))
                {
                    Items.Add(new LibraryItem
                    {
                        Name = "..",
                        Path = Path.GetDirectoryName(CurrentPath),
                        Type = LibraryItemType.Directory
                    });
                }

                // Add directories
                foreach (var dir in Directory.GetDirectories(CurrentPath))
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == 0) // Skip hidden directories
                    {
                        Items.Add(new LibraryItem
                        {
                            Name = Path.GetFileName(dir),
                            Path = dir,
                            Type = LibraryItemType.Directory
                        });
                    }
                }

                // Add files
                foreach (var file in Directory.GetFiles(CurrentPath))
                {
                    Items.Add(new LibraryItem
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Type = LibraryItemType.File
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error refreshing library items: {ex}");
            }
        }

        public string GetRelativePath(string fullPath)
        {
            if (string.IsNullOrEmpty(fullPath))
                throw new ArgumentNullException(nameof(fullPath));
                
            var config = Configuration.Instance;
            if (config == null)
            {
                System.Diagnostics.Debug.WriteLine("Error: Configuration.Instance is null in GetRelativePath. Falling back to filename.");
                return Path.GetFileName(fullPath);
            }
                
            var libraryPath = config.LibraryPath;
            if (string.IsNullOrEmpty(libraryPath))
            {
                System.Diagnostics.Debug.WriteLine("Warning: LibraryPath is not configured. Cannot calculate relative path.");
                // Fallback to just the filename for safety
                return Path.GetFileName(fullPath);
            }
            
            return Path.GetRelativePath(libraryPath, fullPath)
                .Replace('\\', '/');
        }

        private bool IsRootPath(string path)
        {
            return string.Equals(path, Configuration.Instance.LibraryPath, StringComparison.OrdinalIgnoreCase);
        }

        public string GetCachePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "Cache"
            );
        }

        public string GetThemesPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "Themes"
            );
        }

        public string GetDownloadsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "Downloads"
            );
        }

        public string GetLogsPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Universa",
                "Logs"
            );
        }

        public async Task ClearCache()
        {
            try
            {
                var cachePath = GetCachePath();
                if (Directory.Exists(cachePath))
                {
                    var files = Directory.GetFiles(cachePath);
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error clearing cache: {ex.Message}",
                    "Cache Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error
                );
            }
        }

        public long GetCacheSize()
        {
            try
            {
                var cachePath = GetCachePath();
                if (!Directory.Exists(cachePath))
                    return 0;

                var files = Directory.GetFiles(cachePath);
                long size = 0;
                foreach (var file in files)
                {
                    var info = new FileInfo(file);
                    size += info.Length;
                }
                return size;
            }
            catch
            {
                return 0;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LibraryItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public LibraryItemType Type { get; set; }
    }
} 