using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Timers;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service that handles WebDAV synchronization with configurable auto-sync
    /// </summary>
    public class WebDavSyncService : IWebDavSyncService, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly Timer _autoSyncTimer;
        private readonly WebDavSyncStateManager _stateManager;
        private WebDavClient _client;
        private bool _disposed;
        private bool _isSyncing;
        private WebDavSyncStatus _currentStatus = WebDavSyncStatus.Idle;
        private DateTime? _lastSyncTime;
        private readonly List<string> _conflictFiles = new List<string>();

        public event EventHandler<WebDavSyncStatusEventArgs> SyncStatusChanged;
        public event EventHandler<FileDownloadedEventArgs> FileDownloaded;

        public WebDavSyncStatus CurrentStatus => _currentStatus;
        public DateTime? LastSyncTime => _lastSyncTime;
        public bool IsAutoSyncEnabled => _autoSyncTimer?.Enabled ?? false;

        public WebDavSyncService(IConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            _config = _configService.Provider;
            _configService.ConfigurationChanged += OnConfigurationChanged;

            _stateManager = new WebDavSyncStateManager();
            System.Diagnostics.Debug.WriteLine($"[Sync] State manager initialized. Tracking {_stateManager.TrackedFileCount} files");

            _autoSyncTimer = new Timer();
            _autoSyncTimer.Elapsed += OnAutoSyncTimerElapsed;

            // Initialize based on current config
            RefreshConfiguration();
        }

        public void RefreshConfiguration()
        {
            // Dispose old client if exists
            _client?.Dispose();
            _client = null;

            // Create new client if configured
            if (!string.IsNullOrWhiteSpace(_config.WebDavServerUrl))
            {
                try
                {
                    _client = new WebDavClient(
                        _config.WebDavServerUrl,
                        _config.WebDavUsername ?? string.Empty,
                        _config.WebDavPassword ?? string.Empty
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to create WebDAV client: {ex.Message}");
                    UpdateStatus(WebDavSyncStatus.Error, $"Failed to initialize: {ex.Message}");
                }
            }

            // Update auto-sync timer
            if (_config.WebDavAutoSync && _config.WebDavSyncIntervalMinutes > 0)
            {
                _autoSyncTimer.Interval = TimeSpan.FromMinutes(_config.WebDavSyncIntervalMinutes).TotalMilliseconds;
                _autoSyncTimer.Start();
            }
            else
            {
                _autoSyncTimer.Stop();
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            if (_client == null)
            {
                RefreshConfiguration();
                if (_client == null)
                    return false;
            }

            try
            {
                return await _client.TestConnectionAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV connection test failed: {ex.Message}");
                return false;
            }
        }

        public async Task SynchronizeAsync()
        {
            if (_isSyncing)
            {
                System.Diagnostics.Debug.WriteLine("Sync already in progress, skipping");
                return;
            }

            if (_client == null)
            {
                RefreshConfiguration();
                if (_client == null)
                {
                    UpdateStatus(WebDavSyncStatus.Error, "WebDAV not configured");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(_config.LibraryPath) || !Directory.Exists(_config.LibraryPath))
            {
                UpdateStatus(WebDavSyncStatus.Error, "Library path not configured or does not exist");
                return;
            }

            _isSyncing = true;
            _conflictFiles.Clear(); // Reset conflict tracking for this sync
            UpdateStatus(WebDavSyncStatus.Syncing, "Synchronizing...");

            try
            {
                var remotePath = _config.WebDavRemoteFolder?.TrimStart('/') ?? string.Empty;

                // Ensure remote directory exists
                if (!string.IsNullOrEmpty(remotePath))
                {
                    await EnsureRemoteDirectoryExists(remotePath);
                }

                // Get remote files (recursively traverse all subdirectories)
                var remoteResources = await _client.ListDirectoryRecursiveAsync(remotePath);
                var remoteFiles = remoteResources
                    .Where(r => !r.IsDirectory)
                    .ToDictionary(r => NormalizePath(r.Path, remotePath), r => r);

                System.Diagnostics.Debug.WriteLine($"Found {remoteFiles.Count} remote files");

                // Get local files
                var localFiles = Directory.GetFiles(_config.LibraryPath, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(_config.LibraryPath, f))
                    .Where(f => !f.StartsWith(".") && !f.Contains("\\.")) // Skip hidden files/folders
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Found {localFiles.Count} local files");

                int uploaded = 0, downloaded = 0, skipped = 0;

                // Sync local files to remote
                foreach (var localFile in localFiles)
                {
                    var normalizedPath = localFile.Replace('\\', '/');
                    var remoteFilePath = string.IsNullOrEmpty(remotePath) 
                        ? normalizedPath 
                        : $"{remotePath}/{normalizedPath}";

                    if (remoteFiles.TryGetValue(normalizedPath, out var remoteResource))
                    {
                        // File exists on both sides - perform 3-WAY MERGE DETECTION
                        var localFullPath = Path.Combine(_config.LibraryPath, localFile);
                        var localInfo = new FileInfo(localFullPath);

                        // Calculate current ETags
                        var currentLocalETag = CalculateFileMD5(localFullPath);
                        var currentRemoteETag = remoteResource.ETag?.Trim('"') ?? string.Empty;

                        // Get last known state from previous sync
                        var lastKnownState = _stateManager.GetFileState(normalizedPath);

                        System.Diagnostics.Debug.WriteLine($"[Sync] 3-way merge analysis for: {normalizedPath}");
                        System.Diagnostics.Debug.WriteLine($"[Sync]   Current Local:  {currentLocalETag}");
                        System.Diagnostics.Debug.WriteLine($"[Sync]   Current Remote: {currentRemoteETag}");
                        System.Diagnostics.Debug.WriteLine($"[Sync]   Last Known:     {lastKnownState?.LastRemoteETag ?? "(first sync)"}");
                        
                        // Diagnostic: Check if ETags are MD5 format
                        bool localIsMD5 = currentLocalETag.Length == 32 && IsHexString(currentLocalETag);
                        bool remoteIsMD5 = currentRemoteETag.Length == 32 && IsHexString(currentRemoteETag);
                        if (!remoteIsMD5)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Sync] ⚠️ WARNING: Server ETag is NOT MD5 format! Using inode-based ETag: {currentRemoteETag}");
                            System.Diagnostics.Debug.WriteLine($"[Sync] ⚠️ This may cause false \"remote changed\" detections!");
                        }

                        // Determine what changed since last sync
                        bool localChanged = false;
                        bool remoteChanged = false;

                        if (lastKnownState == null)
                        {
                            // First time syncing this file - compare current states
                            System.Diagnostics.Debug.WriteLine($"[Sync]   → First sync of this file");
                            if (currentLocalETag != currentRemoteETag)
                            {
                                // Different content, use timestamps to decide
                                if (localInfo.LastWriteTimeUtc > remoteResource.LastModified)
                                {
                                    localChanged = true;
                                    System.Diagnostics.Debug.WriteLine($"[Sync]   → Local appears newer (first sync)");
                                }
                                else
                                {
                                    remoteChanged = true;
                                    System.Diagnostics.Debug.WriteLine($"[Sync]   → Remote appears newer (first sync)");
                                }
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"[Sync]   → Already in sync (first sync)");
                            }
                        }
                        else
                        {
                            // We have previous state - do proper 3-way comparison
                            localChanged = !string.Equals(currentLocalETag, lastKnownState.LastLocalETag, StringComparison.OrdinalIgnoreCase);
                            remoteChanged = !string.Equals(currentRemoteETag, lastKnownState.LastRemoteETag, StringComparison.OrdinalIgnoreCase);
                            
                            System.Diagnostics.Debug.WriteLine($"[Sync]   → Local changed:  {localChanged}");
                            System.Diagnostics.Debug.WriteLine($"[Sync]   → Remote changed: {remoteChanged}");
                        }

                        // 3-WAY MERGE DECISION TREE
                        if (!localChanged && !remoteChanged)
                        {
                            // CASE 1: Neither side changed - skip
                            System.Diagnostics.Debug.WriteLine($"[Sync] ✓ No changes detected: {normalizedPath}");
                            skipped++;
                        }
                        else if (localChanged && !remoteChanged)
                        {
                            // CASE 2: Only local changed - upload
                            try
                            {
                                System.Diagnostics.Debug.WriteLine($"[Sync] ↑ Uploading local changes: {normalizedPath} (Size: {localInfo.Length:N0} bytes)");
                                await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);
                                uploaded++;
                                System.Diagnostics.Debug.WriteLine($"[Sync] ✓ Upload successful: {normalizedPath}");
                                
                                // Update state after successful upload
                                _stateManager.UpdateFileState(
                                    normalizedPath,
                                    currentLocalETag,
                                    currentLocalETag, // Remote now has same ETag as local
                                    localInfo.LastWriteTimeUtc,
                                    localInfo.LastWriteTimeUtc,
                                    localInfo.Length
                                );
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Sync] ✗ Upload failed for {normalizedPath}: {ex.GetType().Name} - {ex.Message}");
                                if (ex.InnerException != null)
                                {
                                    System.Diagnostics.Debug.WriteLine($"[Sync]   Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                                }
                                throw;
                            }
                        }
                        else if (!localChanged && remoteChanged)
                        {
                            // CASE 3: Only remote changed - download
                            System.Diagnostics.Debug.WriteLine($"[Sync] ↓ Downloading remote changes: {normalizedPath}");
                            await _client.DownloadFileAsync(remoteFilePath, localFullPath);
                            downloaded++;
                            System.Diagnostics.Debug.WriteLine($"[Sync] ✓ Download successful: {normalizedPath}");
                            
                            // Notify that file was downloaded (for editor refresh)
                            FileDownloaded?.Invoke(this, new FileDownloadedEventArgs(localFullPath, normalizedPath));
                            
                            // Update local file info after download
                            localInfo.Refresh();
                            
                            // Update state after successful download
                            _stateManager.UpdateFileState(
                                normalizedPath,
                                currentRemoteETag, // Local now has same ETag as remote
                                currentRemoteETag,
                                localInfo.LastWriteTimeUtc,
                                remoteResource.LastModified,
                                remoteResource.Size
                            );
                        }
                        else
                        {
                            // CASE 4: Both sides changed - TRUE CONFLICT!
                            System.Diagnostics.Debug.WriteLine($"[Sync] ⚠⚠ TRUE CONFLICT: Both sides changed!");
                            System.Diagnostics.Debug.WriteLine($"[Sync] ⚠⚠ Applying 'save_both' strategy...");
                            
                            try
                            {
                                // Save remote version as conflict file locally
                                var conflictPath = GenerateConflictFilePath(localFullPath);
                                System.Diagnostics.Debug.WriteLine($"[Sync] 💾 Saving remote version to: {conflictPath}");
                                await _client.DownloadFileAsync(remoteFilePath, conflictPath);
                                
                                // Upload local version to server (preserves your work)
                                System.Diagnostics.Debug.WriteLine($"[Sync] ↑ Uploading local version to server");
                                await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);
                                
                                uploaded++;
                                System.Diagnostics.Debug.WriteLine($"[Sync] ✓ Conflict resolved (both versions saved)");
                                System.Diagnostics.Debug.WriteLine($"[Sync]   - Local version: {normalizedPath}");
                                System.Diagnostics.Debug.WriteLine($"[Sync]   - Remote backup: {Path.GetFileName(conflictPath)}");
                                
                                // Track conflict for status reporting
                                if (!_conflictFiles.Contains(normalizedPath))
                                    _conflictFiles.Add(normalizedPath);
                                
                                // Update state after conflict resolution
                                _stateManager.UpdateFileState(
                                    normalizedPath,
                                    currentLocalETag,
                                    currentLocalETag, // Remote now has local version
                                    localInfo.LastWriteTimeUtc,
                                    localInfo.LastWriteTimeUtc,
                                    localInfo.Length
                                );
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Sync] ✗ Conflict resolution failed: {ex.Message}");
                                throw;
                            }
                        }

                        remoteFiles.Remove(normalizedPath);
                    }
                    else
                    {
                        // File only exists locally - upload it (new file)
                        var localFullPath = Path.Combine(_config.LibraryPath, localFile);
                        try
                        {
                            var fileInfo = new FileInfo(localFullPath);
                            var localETag = CalculateFileMD5(localFullPath);
                            
                            System.Diagnostics.Debug.WriteLine($"[Sync] ↑ Uploading new local file: {normalizedPath} (Size: {fileInfo.Length:N0} bytes)");
                            
                            await UploadFileWithDirectories(localFullPath, remoteFilePath, remotePath);
                            uploaded++;
                            System.Diagnostics.Debug.WriteLine($"[Sync] ✓ Upload successful: {normalizedPath}");
                            
                            // Track newly uploaded file in state
                            _stateManager.UpdateFileState(
                                normalizedPath,
                                localETag,
                                localETag, // Remote now has same content as local
                                fileInfo.LastWriteTimeUtc,
                                fileInfo.LastWriteTimeUtc,
                                fileInfo.Length
                            );
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[Sync] ✗ Upload failed for {normalizedPath}: {ex.GetType().Name} - {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"[Sync]   Inner exception: {ex.InnerException.GetType().Name} - {ex.InnerException.Message}");
                            }
                            throw; // Re-throw to stop sync and show error to user
                        }
                    }
                }

                // Download remaining remote files that don't exist locally (new remote files)
                foreach (var remoteFile in remoteFiles.Values)
                {
                    var normalizedPath = NormalizePath(remoteFile.Path, remotePath);
                    var localFullPath = Path.Combine(_config.LibraryPath, normalizedPath.Replace('/', '\\'));
                    
                    System.Diagnostics.Debug.WriteLine($"[Sync] ↓ Downloading new remote file: {normalizedPath}");
                    await _client.DownloadFileAsync(remoteFile.Path, localFullPath);
                    downloaded++;
                    System.Diagnostics.Debug.WriteLine($"[Sync] ✓ Download successful: {normalizedPath}");
                    
                    // Notify that file was downloaded (for editor refresh)
                    FileDownloaded?.Invoke(this, new FileDownloadedEventArgs(localFullPath, normalizedPath));
                    
                    // Update local file info after download
                    var localInfo = new FileInfo(localFullPath);
                    var remoteETag = remoteFile.ETag?.Trim('"') ?? string.Empty;
                    
                    // Track newly downloaded file in state
                    _stateManager.UpdateFileState(
                        normalizedPath,
                        remoteETag, // Local now has same content as remote
                        remoteETag,
                        localInfo.LastWriteTimeUtc,
                        remoteFile.LastModified,
                        remoteFile.Size
                    );
                }

                _lastSyncTime = DateTime.Now;
                _config.LastWebDavSyncTime = _lastSyncTime;

                // Mark successful sync and persist state to disk
                _stateManager.MarkSuccessfulSync(remotePath);
                _stateManager.SaveState();
                System.Diagnostics.Debug.WriteLine($"[Sync] State saved. Now tracking {_stateManager.TrackedFileCount} files");

                // Build status message with conflict info
                var message = _conflictFiles.Count > 0
                    ? $"Sync complete: {uploaded} uploaded, {downloaded} downloaded, {skipped} unchanged, ⚠️ {_conflictFiles.Count} conflicts (saved both versions)"
                    : $"Sync complete: {uploaded} uploaded, {downloaded} downloaded, {skipped} unchanged";
                
                System.Diagnostics.Debug.WriteLine(message);
                if (_conflictFiles.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[Sync] Conflicts detected in:");
                    foreach (var file in _conflictFiles)
                    {
                        System.Diagnostics.Debug.WriteLine($"[Sync]   - {file}");
                    }
                }
                
                UpdateStatus(WebDavSyncStatus.Success, message, _lastSyncTime);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync error: {ex.Message}");
                UpdateStatus(WebDavSyncStatus.Error, $"Sync failed: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }

        public void StartAutoSync()
        {
            if (_config.WebDavSyncIntervalMinutes > 0)
            {
                _autoSyncTimer.Interval = TimeSpan.FromMinutes(_config.WebDavSyncIntervalMinutes).TotalMilliseconds;
                _autoSyncTimer.Start();
                System.Diagnostics.Debug.WriteLine($"Auto-sync started with {_config.WebDavSyncIntervalMinutes} minute interval");
            }
        }

        public void StopAutoSync()
        {
            _autoSyncTimer.Stop();
            System.Diagnostics.Debug.WriteLine("Auto-sync stopped");
        }

        private async void OnAutoSyncTimerElapsed(object sender, ElapsedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Auto-sync triggered");
            await SynchronizeAsync();
        }

        private async Task EnsureRemoteDirectoryExists(string remotePath)
        {
            System.Diagnostics.Debug.WriteLine($"[Sync] EnsureRemoteDirectoryExists: {remotePath}");
            var parts = remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var currentPath = string.Empty;
            bool createdNewDirectory = false;

            foreach (var part in parts)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}/{part}";
                System.Diagnostics.Debug.WriteLine($"[Sync] Creating directory: {currentPath}");
                
                try
                {
                    await _client.CreateDirectoryAsync(currentPath);
                    System.Diagnostics.Debug.WriteLine($"[Sync] Directory created/confirmed: {currentPath}");
                    createdNewDirectory = true; // Assume it was created (405 means it already exists)
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Sync] CreateDirectory failed for {currentPath}: {ex.GetType().Name} - {ex.Message}");
                    throw;
                }
            }
            
            // WORKAROUND: If we created directories, give the server time to actually create them
            // This handles servers with async directory creation or caching issues
            if (createdNewDirectory)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Waiting 500ms for server to complete directory creation...");
                await Task.Delay(500);
            }
        }

        private async Task UploadFileWithDirectories(string localPath, string remotePath, string baseRemotePath)
        {
            // Ensure all parent directories exist on remote
            var remoteDir = Path.GetDirectoryName(remotePath)?.Replace('\\', '/');
            System.Diagnostics.Debug.WriteLine($"[Sync] Upload: remotePath={remotePath}, remoteDir={remoteDir}, baseRemotePath={baseRemotePath}");
            
            if (!string.IsNullOrEmpty(remoteDir) && remoteDir != baseRemotePath)
            {
                System.Diagnostics.Debug.WriteLine($"[Sync] Creating parent directories for: {remoteDir}");
                try
                {
                    await EnsureRemoteDirectoryExists(remoteDir);
                    System.Diagnostics.Debug.WriteLine($"[Sync] Parent directories created successfully");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Sync] Failed to create parent directories: {ex.GetType().Name} - {ex.Message}");
                    throw;
                }
            }

            await _client.UploadFileAsync(localPath, remotePath);
        }

        private string NormalizePath(string path, string remotePath)
        {
            // Remove the remote base path to get relative path
            var normalized = path.TrimStart('/');
            
            if (!string.IsNullOrEmpty(remotePath))
            {
                var remotePrefix = remotePath.TrimStart('/').TrimEnd('/') + "/";
                if (normalized.StartsWith(remotePrefix))
                {
                    normalized = normalized.Substring(remotePrefix.Length);
                }
            }

            return normalized;
        }

        /// <summary>
        /// Calculates MD5 hash of a file (matches WebDAV ETag format)
        /// </summary>
        private string CalculateFileMD5(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = md5.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Checks if a string is valid hexadecimal
        /// </summary>
        private bool IsHexString(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            return value.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        }

        /// <summary>
        /// Generates a conflict file path with timestamp
        /// Example: story.md -> story.conflict-2025-10-26-143045.md
        /// </summary>
        private string GenerateConflictFilePath(string originalPath)
        {
            var directory = Path.GetDirectoryName(originalPath);
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
            var extension = Path.GetExtension(originalPath);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            
            var conflictFileName = $"{fileNameWithoutExt}.conflict-{timestamp}{extension}";
            return Path.Combine(directory ?? string.Empty, conflictFileName);
        }

        private void UpdateStatus(WebDavSyncStatus status, string message = null, DateTime? lastSyncTime = null)
        {
            _currentStatus = status;
            if (lastSyncTime.HasValue)
                _lastSyncTime = lastSyncTime;

            SyncStatusChanged?.Invoke(this, new WebDavSyncStatusEventArgs(status, message, _lastSyncTime));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _autoSyncTimer?.Stop();
                _autoSyncTimer?.Dispose();
                _client?.Dispose();
                _disposed = true;
            }
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            // If any WebDAV setting changes, refresh the client and timer
            if (e.Key.StartsWith("webdav."))
            {
                System.Diagnostics.Debug.WriteLine($"WebDAV configuration changed (Key: {e.Key}). Refreshing service.");
                RefreshConfiguration();
            }
        }
    }
}

