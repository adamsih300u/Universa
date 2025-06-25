using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Universa.Desktop.Models;
using Universa.Desktop.Windows;

namespace Universa.Desktop.Library
{
    public class LibraryManager
    {
        private static LibraryManager _instance;
        public static LibraryManager Instance => _instance ??= new LibraryManager();

        private string _currentPath;
        public string CurrentPath
        {
            get => _currentPath;
            set
            {
                _currentPath = value;
                RefreshItems();
            }
        }

        private LibraryManager()
        {
            // Private constructor for singleton
        }

        public void Initialize()
        {
            var config = Configuration.Instance;
            if (!string.IsNullOrEmpty(config.LibraryPath) && Directory.Exists(config.LibraryPath))
            {
                _currentPath = config.LibraryPath;
                RefreshItems();
            }
        }

        public void MoveFile(string sourcePath, string destinationPath)
        {
            try
            {
                // Validate configuration before attempting versioned operations
                var config = Configuration.Instance;
                if (config == null)
                {
                    System.Diagnostics.Debug.WriteLine("Error: Configuration.Instance is null in Library.LibraryManager, performing simple file move.");
                    File.Move(sourcePath, destinationPath);
                    RefreshItems();
                    return;
                }
                
                var libraryPath = config.LibraryPath;
                if (string.IsNullOrEmpty(libraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("Warning: LibraryPath not configured in Library.LibraryManager, performing simple file move.");
                    File.Move(sourcePath, destinationPath);
                    RefreshItems();
                    return;
                }
                
                // Save current version before moving if it's a versioned file
                if (Universa.Desktop.LibraryManager.Instance.IsVersionedFile(sourcePath))
                {
                    var sourceRelativePath = Universa.Desktop.LibraryManager.Instance.GetRelativePath(sourcePath);
                    var destRelativePath = Universa.Desktop.LibraryManager.Instance.GetRelativePath(destinationPath);
                    var historyPath = Path.Combine(libraryPath, ".versions");
                    var historyFiles = Directory.GetFiles(historyPath, $"{sourceRelativePath}.*", SearchOption.AllDirectories);

                    // Move the main file first
                    File.Move(sourcePath, destinationPath);

                    // Move all version files
                    foreach (var historyFile in historyFiles)
                    {
                        var fileName = Path.GetFileName(historyFile);
                        var newHistoryPath = Path.Combine(
                            historyPath,
                            Path.GetDirectoryName(destRelativePath),
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

                // Refresh the library view
                RefreshItems();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error moving file: {ex.Message}",
                    "Move Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                throw; // Re-throw to let caller handle the error
            }
        }

        public void RefreshItems()
        {
            var mainWindow = Application.Current.MainWindow as BaseMainWindow;
            mainWindow?.LibraryNavigatorInstance?.RefreshItems();
        }
    }
} 