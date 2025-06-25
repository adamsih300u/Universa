using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for managing file operations in markdown editors
    /// </summary>
    public interface IMarkdownFileService
    {
        /// <summary>
        /// Event fired when the modified state changes
        /// </summary>
        event EventHandler<bool> ModifiedStateChanged;

        /// <summary>
        /// Event fired when content is loaded
        /// </summary>
        event EventHandler ContentLoaded;

        /// <summary>
        /// Loads a file asynchronously and returns the processed content
        /// </summary>
        Task<string> LoadFileAsync(string filePath);

        /// <summary>
        /// Saves content to a file asynchronously
        /// </summary>
        Task SaveFileAsync(string filePath, string content);

        /// <summary>
        /// Saves content to a file with dialog selection
        /// </summary>
        Task<string> SaveAsAsync(string content, string currentFilePath = null);

        /// <summary>
        /// Reloads content from the current file path
        /// </summary>
        Task<string> ReloadFileAsync(string filePath);

        /// <summary>
        /// Loads version information for a file
        /// </summary>
        Task<List<Managers.FileVersionInfo>> LoadVersionsAsync(string filePath);

        /// <summary>
        /// Loads content from a specific version
        /// </summary>
        Task<string> LoadVersionContentAsync(string versionPath);

        /// <summary>
        /// Handles version selection with user confirmation
        /// </summary>
        Task<string> HandleVersionSelectionAsync(Managers.FileVersionInfo selectedVersion, bool hasUnsavedChanges, Func<Task<bool>> saveCallback);

        /// <summary>
        /// Refreshes the version list for a file
        /// </summary>
        Task<List<Managers.FileVersionInfo>> RefreshVersionsAsync(string filePath);
    }
} 