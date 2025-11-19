using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Manages persistence of WebDAV sync state for 3-way merge detection
    /// </summary>
    public class WebDavSyncStateManager
    {
        private readonly string _stateFilePath;
        private WebDavSyncState _currentState;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public WebDavSyncStateManager()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var universaPath = Path.Combine(appDataPath, "Universa");
            
            // Ensure directory exists
            if (!Directory.Exists(universaPath))
            {
                Directory.CreateDirectory(universaPath);
            }

            _stateFilePath = Path.Combine(universaPath, "webdav_sync_state.json");
            _currentState = LoadState();
        }

        /// <summary>
        /// Loads sync state from disk, or creates new empty state
        /// </summary>
        private WebDavSyncState LoadState()
        {
            try
            {
                if (File.Exists(_stateFilePath))
                {
                    var json = File.ReadAllText(_stateFilePath);
                    var state = JsonSerializer.Deserialize<WebDavSyncState>(json, _jsonOptions);
                    System.Diagnostics.Debug.WriteLine($"[SyncState] Loaded state with {state?.Files?.Count ?? 0} tracked files");
                    return state ?? new WebDavSyncState();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncState] Failed to load state: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SyncState] Creating new empty state");
            }

            return new WebDavSyncState();
        }

        /// <summary>
        /// Saves current sync state to disk
        /// </summary>
        public void SaveState()
        {
            try
            {
                var json = JsonSerializer.Serialize(_currentState, _jsonOptions);
                File.WriteAllText(_stateFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[SyncState] Saved state with {_currentState.Files.Count} tracked files");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyncState] Failed to save state: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the last known state for a file, or null if never synced
        /// </summary>
        public FileSyncState GetFileState(string relativePath)
        {
            if (_currentState.Files.TryGetValue(relativePath, out var state))
            {
                return state;
            }
            return null;
        }

        /// <summary>
        /// Updates the state for a file after successful sync
        /// </summary>
        public void UpdateFileState(
            string relativePath,
            string localETag,
            string remoteETag,
            DateTime localModified,
            DateTime remoteModified,
            long fileSize)
        {
            _currentState.Files[relativePath] = new FileSyncState
            {
                LastLocalETag = localETag,
                LastRemoteETag = remoteETag,
                LastLocalModified = localModified,
                LastRemoteModified = remoteModified,
                LastSyncTime = DateTime.UtcNow,
                LastSize = fileSize
            };
        }

        /// <summary>
        /// Removes a file from tracking (when deleted)
        /// </summary>
        public void RemoveFileState(string relativePath)
        {
            _currentState.Files.Remove(relativePath);
        }

        /// <summary>
        /// Marks successful sync completion
        /// </summary>
        public void MarkSuccessfulSync(string remoteFolder)
        {
            _currentState.LastSuccessfulSync = DateTime.UtcNow;
            _currentState.RemoteFolder = remoteFolder;
        }

        /// <summary>
        /// Gets the entire current state (for diagnostics)
        /// </summary>
        public WebDavSyncState GetCurrentState()
        {
            return _currentState;
        }

        /// <summary>
        /// Clears all state (useful for troubleshooting or re-sync)
        /// </summary>
        public void ClearState()
        {
            System.Diagnostics.Debug.WriteLine($"[SyncState] Clearing all sync state");
            _currentState = new WebDavSyncState();
            SaveState();
        }

        /// <summary>
        /// Gets the number of tracked files
        /// </summary>
        public int TrackedFileCount => _currentState.Files.Count;
    }
}








