using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Managers;
using Universa.Desktop.Services;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for managing file operations in markdown editors
    /// </summary>
    public class MarkdownFileService : IMarkdownFileService
    {
        private readonly IConfigurationService _configService;
        private readonly IFrontmatterProcessor _frontmatterProcessor;

        public event EventHandler<bool> ModifiedStateChanged;
        public event EventHandler ContentLoaded;

        public MarkdownFileService(IConfigurationService configService, IFrontmatterProcessor frontmatterProcessor)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _frontmatterProcessor = frontmatterProcessor ?? throw new ArgumentNullException(nameof(frontmatterProcessor));
        }

        public async Task<string> LoadFileAsync(string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Loading file: {filePath}");

                // Load file content on background thread
                string fileContent = await Task.Run(() => File.ReadAllText(filePath));
                
                // Process frontmatter using the service
                string contentToDisplay = await _frontmatterProcessor.ProcessFrontmatterForLoadingAsync(fileContent);
                
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Successfully loaded file: {filePath}");
                
                // Fire content loaded event
                ContentLoaded?.Invoke(this, EventArgs.Empty);
                
                return contentToDisplay;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Error loading file: {ex.Message}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error loading file: {ex.Message}", "Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task SaveFileAsync(string filePath, string content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Starting to save file: {filePath}");
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] File extension: {Path.GetExtension(filePath)}");
                
                var isVersioned = LibraryManager.Instance.IsVersionedFile(filePath);
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Is versioned file: {isVersioned}");

                // Process frontmatter using the service
                string processedContent = _frontmatterProcessor.ProcessFrontmatterForSaving(content);
                
                // Save the file content
                await Task.Run(() => 
                {
                    System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Writing file contents to: {filePath}");
                    File.WriteAllText(filePath, processedContent);
                });
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Successfully wrote file contents");
                
                // Fire modified state changed event
                ModifiedStateChanged?.Invoke(this, false);

                if (isVersioned)
                {
                    // Save a version after each save
                    try
                    {
                        System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Attempting to save version");
                        var versionManager = VersionManager.GetInstance();
                        System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Got VersionManager instance");
                        
                        // Explicitly check if file exists before saving version
                        if (File.Exists(filePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] File exists, proceeding with version save");
                            var directory = Path.GetDirectoryName(filePath);
                            var versionsDir = Path.Combine(directory, ".versions");
                            System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Versions directory will be: {versionsDir}");
                            
                            try
                            {
                                await versionManager.SaveVersion(filePath);
                                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Successfully saved version");
                            }
                            catch (Exception saveEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Error in SaveVersion: {saveEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Stack trace: {saveEx.StackTrace}");
                                throw; // Re-throw to be caught by outer catch block
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] File does not exist after save: {filePath}");
                        }
                    }
                    catch (Exception versionEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Error saving version: {versionEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Stack trace: {versionEx.StackTrace}");
                        await Application.Current.Dispatcher.InvokeAsync(() => 
                            MessageBox.Show($"Warning: File was saved but version could not be created.\nError: {versionEx.Message}", 
                                "Version Creation Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                        );
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] File type is not configured for versioning");
                }

                // Get the library path from the configuration service
                var libraryPath = _configService.Provider.LibraryPath;
                if (string.IsNullOrEmpty(libraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("[MarkdownFileService] Library path is not configured");
                    throw new InvalidOperationException("Library path is not configured");
                }

                // Get the relative path and sync
                var relativePath = Path.GetRelativePath(libraryPath, filePath);
                await SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] File synced");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Error saving file: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Stack trace: {ex.StackTrace}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task<string> SaveAsAsync(string content, string currentFilePath = null)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                DefaultExt = ".md"
            };

            if (!string.IsNullOrEmpty(currentFilePath))
            {
                dialog.InitialDirectory = Path.GetDirectoryName(currentFilePath);
                dialog.FileName = Path.GetFileName(currentFilePath);
            }

            if (dialog.ShowDialog() == true)
            {
                await SaveFileAsync(dialog.FileName, content);
                return dialog.FileName;
            }

            return null; // User cancelled
        }

        public async Task<string> ReloadFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}");
            }

            try
            {
                string content = await File.ReadAllTextAsync(filePath);
                
                // Process frontmatter and get content without it
                string contentToDisplay = await _frontmatterProcessor.ProcessFrontmatterForLoadingAsync(content);
                
                // Fire modified state changed event
                ModifiedStateChanged?.Invoke(this, false);
                
                return contentToDisplay;
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error reloading file: {ex.Message}", "Reload Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task<List<Managers.FileVersionInfo>> LoadVersionsAsync(string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Loading versions for file: {filePath}");
                var versions = VersionManager.GetInstance().GetVersions(filePath);
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Found {versions.Count} versions");
                
                return versions;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Error loading versions: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MarkdownFileService] Stack trace: {ex.StackTrace}");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error loading versions: {ex.Message}", "Version Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task<string> LoadVersionContentAsync(string versionPath)
        {
            try
            {
                return await VersionManager.GetInstance().LoadVersion(versionPath);
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error loading version: {ex.Message}", "Version Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task<string> HandleVersionSelectionAsync(Managers.FileVersionInfo selectedVersion, bool hasUnsavedChanges, Func<Task<bool>> saveCallback)
        {
            try
            {
                // If there are unsaved changes, prompt to save
                if (hasUnsavedChanges)
                {
                    var result = await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageBox.Show(
                            "You have unsaved changes. Would you like to save them before loading the selected version?",
                            "Unsaved Changes",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning)
                    );

                    if (result == MessageBoxResult.Cancel)
                    {
                        return null; // User cancelled
                    }
                    if (result == MessageBoxResult.Yes)
                    {
                        var saveResult = await saveCallback();
                        if (!saveResult)
                        {
                            return null; // Save failed
                        }
                    }
                }

                // Confirm version load
                var loadResult = await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show(
                        $"Are you sure you want to load the version from {selectedVersion.Timestamp:dd MMM yyyy HH:mm:ss}?\n\nThis will replace the current content.",
                        "Confirm Version Load",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question)
                );

                if (loadResult == MessageBoxResult.Yes)
                {
                    var content = await LoadVersionContentAsync(selectedVersion.Path);
                    
                    // Fire modified state changed event
                    ModifiedStateChanged?.Invoke(this, true);
                    
                    return content;
                }

                return null; // User cancelled
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error loading version: {ex.Message}", "Version Load Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        public async Task<List<Managers.FileVersionInfo>> RefreshVersionsAsync(string filePath)
        {
            return await LoadVersionsAsync(filePath);
        }
    }
} 