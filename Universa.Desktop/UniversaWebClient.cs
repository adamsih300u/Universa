using System;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading.Tasks;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Universa.Desktop.WebSync
{
    public enum SyncState
    {
        OutOfSync,
        Synchronizing,
        Synchronized
    }

    public enum FileConflictResolution
    {
        KeepLocal,
        KeepRemote,
        KeepBoth
    }

    public class FileMetadata
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("path")]
        public string Path { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("isDirectory")]
        public bool IsDirectory { get; set; }

        [JsonPropertyName("modTime")]
        public DateTime ModifiedTime { get; set; }

        [JsonPropertyName("hash")]
        public string Hash { get; set; }
    }

    public class FileChange
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("file")]
        public FileMetadata FileMetadata { get; set; }
    }

    public class FileConflictEventArgs : EventArgs
    {
        public string RelativePath { get; }
        public DateTime LocalModifiedTime { get; }
        public DateTime RemoteModifiedTime { get; }
        public long LocalSize { get; }
        public long RemoteSize { get; }
        public FileConflictResolution Resolution { get; set; }
        public TaskCompletionSource<FileConflictResolution> CompletionSource { get; }

        public FileConflictEventArgs(string relativePath, DateTime localModifiedTime, DateTime remoteModifiedTime, long localSize, long remoteSize)
        {
            RelativePath = relativePath;
            LocalModifiedTime = localModifiedTime;
            RemoteModifiedTime = remoteModifiedTime;
            LocalSize = localSize;
            RemoteSize = remoteSize;
            Resolution = FileConflictResolution.KeepBoth; // Default
            CompletionSource = new TaskCompletionSource<FileConflictResolution>();
        }
    }

    public class UniversaWebClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private ClientWebSocket _webSocket;
        private readonly FileSystemWatcher _watcher;
        private readonly string _baseUrl;
        private readonly Configuration _config;
        private bool _isAuthenticated;
        private CancellationTokenSource _webSocketCts;
        private SyncState _syncState = SyncState.OutOfSync;
        private DateTime? _lastSyncTime;
        private bool _disposed;
        private int _reconnectAttempts = 0;
        private const int MaxReconnectAttempts = 10;
        private const int InitialReconnectDelay = 1000; // 1 second
        
        public event Action<SyncState, DateTime?> SyncStateChanged;
        public event EventHandler<FileConflictEventArgs> FileConflictDetected;
        public event Action<string> FileSystemChanged;

        public UniversaWebClient(Configuration config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _baseUrl = config.UniversaWebUrl?.TrimEnd('/') ?? throw new ArgumentException("Web URL is required");
            
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
            
            // Initialize FileSystemWatcher
            var libraryPath = config.LibraryPath;
            if (!string.IsNullOrEmpty(libraryPath) && Directory.Exists(libraryPath))
            {
                _watcher = new FileSystemWatcher(libraryPath)
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = false
                };
                
                _watcher.Created += OnFileChanged;
                _watcher.Changed += OnFileChanged;
                _watcher.Deleted += OnFileChanged;
                _watcher.Renamed += OnFileRenamed;
            }
        }

        public async Task<bool> Login()
        {
            try
            {
                var loginData = new
                {
                    username = _config.UniversaWebUsername,
                    password = _config.UniversaWebPassword
                };

                var response = await _httpClient.PostAsync("/api/login", 
                    new StringContent(JsonSerializer.Serialize(loginData), Encoding.UTF8, "application/json"));

                if (response.IsSuccessStatusCode)
                {
                    _isAuthenticated = true;
                    // Add authentication header for subsequent requests
                    _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                        "Basic", 
                        Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_config.UniversaWebUsername}:{_config.UniversaWebPassword}"))
                    );
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Login failed: {ex.Message}");
                return false;
            }
        }

        public async Task StartSync()
        {
            if (!_isAuthenticated || _syncState == SyncState.Synchronizing)
                return;

            try
            {
                UpdateSyncState(SyncState.Synchronizing);
                
                // Start WebSocket connection for real-time updates
                await ConnectWebSocket();
                
                // Enable file system watcher
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = true;
                }

                // Initial sync
                await PerformInitialSync();
                
                UpdateSyncState(SyncState.Synchronized, DateTime.Now);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Sync failed: {ex.Message}");
                UpdateSyncState(SyncState.OutOfSync);
            }
        }

        public async Task StopSync()
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
            }

            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _webSocketCts?.Cancel();
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping sync", CancellationToken.None);
            }

            UpdateSyncState(SyncState.OutOfSync);
        }

        private async Task<string> CalculateFileHash(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        private async Task<FileConflictResolution> HandleFileConflict(string relativePath, FileMetadata remoteFile, DateTime localModifiedTime, string localHash)
        {
            var fullPath = Path.Combine(_config.LibraryPath, relativePath);
            
            // If hashes are the same, files are identical despite timestamp differences
            if (localHash == remoteFile.Hash)
            {
                return FileConflictResolution.KeepLocal; // Files are identical
            }

            var localSize = new FileInfo(fullPath).Length;
            
            // Raise event and wait for user decision
            var args = new FileConflictEventArgs(
                relativePath,
                localModifiedTime,
                remoteFile.ModifiedTime,
                localSize,
                remoteFile.Size
            );

            FileConflictDetected?.Invoke(this, args);
            var resolution = await args.CompletionSource.Task;

            switch (resolution)
            {
                case FileConflictResolution.KeepLocal:
                    await UploadFile(relativePath);
                    break;

                case FileConflictResolution.KeepRemote:
                    await DownloadFile(relativePath);
                    break;

                case FileConflictResolution.KeepBoth:
                    // Create a conflict copy with timestamp
                    var conflictPath = Path.Combine(
                        Path.GetDirectoryName(relativePath),
                        Path.GetFileNameWithoutExtension(relativePath) + 
                        $".conflict.{DateTime.Now:yyyyMMddHHmmss}" +
                        Path.GetExtension(relativePath)
                    );
                    
                    File.Copy(fullPath, Path.Combine(_config.LibraryPath, conflictPath));
                    await DownloadFile(relativePath);
                    System.Diagnostics.Debug.WriteLine($"Created conflict copy: {conflictPath}");
                    break;
            }

            return resolution;
        }

        private async Task PerformInitialSync()
        {
            try
            {
                // Get remote file list
                var response = await _httpClient.GetAsync("/api/files");
                response.EnsureSuccessStatusCode();
                
                var responseText = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Server response: {responseText}");

                // The server sends an object with an empty string key containing the file list
                var responseObj = await JsonSerializer.DeserializeAsync<Dictionary<string, List<FileMetadata>>>(
                    await response.Content.ReadAsStreamAsync()
                );

                // Get the file list from the empty string key
                var remoteFiles = responseObj?.GetValueOrDefault("") ?? new List<FileMetadata>();
                System.Diagnostics.Debug.WriteLine($"Parsed {remoteFiles.Count} files from server response");

                // Log any files with missing paths
                foreach (var file in remoteFiles.Where(f => string.IsNullOrEmpty(f.Path)))
                {
                    System.Diagnostics.Debug.WriteLine($"Warning: File metadata missing path: Name={file.Name}, Size={file.Size}, IsDirectory={file.IsDirectory}");
                }

                // Create a dictionary of remote files for quick lookup, filtering out any null paths
                var remoteFileDict = remoteFiles
                    .Where(f => !string.IsNullOrEmpty(f.Path))
                    .ToDictionary(f => f.Path, f => f);

                System.Diagnostics.Debug.WriteLine($"Found {remoteFileDict.Count} valid remote files for syncing");

                // Compare with local files and sync differences
                var localPath = _config.LibraryPath;
                if (string.IsNullOrEmpty(localPath) || !Directory.Exists(localPath))
                    return;

                // First, handle local files
                foreach (var localFile in Directory.GetFiles(localPath, "*", SearchOption.AllDirectories))
                {
                    var relativePath = Path.GetRelativePath(localPath, localFile).Replace('\\', '/');
                    var localFileInfo = new FileInfo(localFile);
                    var localHash = await CalculateFileHash(localFile);

                    // If file exists remotely, check if we need to update
                    if (remoteFileDict.TryGetValue(relativePath, out var remoteFile))
                    {
                        var timeDiff = Math.Abs((localFileInfo.LastWriteTimeUtc - remoteFile.ModifiedTime).TotalSeconds);
                        
                        // If timestamps are very close (within 2 seconds) or hashes match, consider them the same
                        if (timeDiff <= 2 || localHash == remoteFile.Hash)
                        {
                            // Files are the same, no action needed
                            remoteFileDict.Remove(relativePath);
                            continue;
                        }

                        // If timestamps differ significantly and hashes don't match, we have a conflict
                        if (timeDiff > 2 && localHash != remoteFile.Hash)
                        {
                            var resolution = await HandleFileConflict(relativePath, remoteFile, localFileInfo.LastWriteTimeUtc, localHash);
                            remoteFileDict.Remove(relativePath);
                            continue;
                        }

                        // If local file is newer, upload it
                        if (localFileInfo.LastWriteTimeUtc > remoteFile.ModifiedTime)
                        {
                            await UploadFile(relativePath);
                        }
                        // If remote file is newer, download it
                        else if (localFileInfo.LastWriteTimeUtc < remoteFile.ModifiedTime)
                        {
                            await DownloadFile(relativePath);
                        }
                        
                        remoteFileDict.Remove(relativePath);
                    }
                    // If file doesn't exist remotely, upload it
                    else
                    {
                        await UploadFile(relativePath);
                    }
                }

                // Download any remaining remote files that don't exist locally
                foreach (var remoteFile in remoteFileDict.Values)
                {
                    if (remoteFile.IsDirectory)
                    {
                        // Create directory if it doesn't exist
                        var dirPath = Path.Combine(_config.LibraryPath, remoteFile.Path);
                        if (!Directory.Exists(dirPath))
                        {
                            System.Diagnostics.Debug.WriteLine($"Creating directory: {remoteFile.Path}");
                            Directory.CreateDirectory(dirPath);
                        }
                    }
                    else
                    {
                        // Only download actual files
                        System.Diagnostics.Debug.WriteLine($"Downloading new file: {remoteFile.Path}");
                        await DownloadFile(remoteFile.Path);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in PerformInitialSync: {ex}");
                throw;
            }
        }

        private async Task ConnectWebSocket()
        {
            while (!_disposed && _reconnectAttempts < MaxReconnectAttempts)
            {
                try
                {
                    _webSocketCts?.Cancel();
                    _webSocketCts = new CancellationTokenSource();

                    _webSocket = new ClientWebSocket();
                    _webSocket.Options.SetRequestHeader("Authorization", _httpClient.DefaultRequestHeaders.Authorization.ToString());

                    await _webSocket.ConnectAsync(new Uri($"ws://{new Uri(_baseUrl).Host}:8080/api/changes"), _webSocketCts.Token);

                    // Reset reconnect attempts on successful connection
                    _reconnectAttempts = 0;
                    
                    // Start receiving messages
                    _ = ReceiveWebSocketMessages(_webSocketCts.Token);

                    // After successful reconnection, perform a full resync
                    try
                    {
                        UpdateSyncState(SyncState.Synchronizing);
                        await PerformInitialSync();
                        UpdateSyncState(SyncState.Synchronized, DateTime.Now);
                    }
                    catch (Exception syncEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to resync after reconnection: {syncEx.Message}");
                        UpdateSyncState(SyncState.OutOfSync);
                        // Don't throw here - we want to keep the WebSocket connection even if sync fails
                    }

                    return;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"WebSocket connection attempt {_reconnectAttempts + 1} failed: {ex.Message}");
                    
                    if (_disposed)
                    {
                        return;
                    }

                    _reconnectAttempts++;
                    
                    if (_reconnectAttempts < MaxReconnectAttempts)
                    {
                        // Calculate delay with exponential backoff (1s, 2s, 4s, 8s, etc.)
                        var delay = InitialReconnectDelay * Math.Pow(2, _reconnectAttempts - 1);
                        System.Diagnostics.Debug.WriteLine($"Waiting {delay}ms before reconnection attempt {_reconnectAttempts + 1}");
                        await Task.Delay((int)delay);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("Max reconnection attempts reached");
                        UpdateSyncState(SyncState.OutOfSync);
                        throw new Exception("Failed to establish WebSocket connection after maximum retry attempts");
                    }
                }
            }
        }

        private async Task ReceiveWebSocketMessages(CancellationToken cancellationToken)
        {
            var buffer = new byte[4096];
            try
            {
                while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                        UpdateSyncState(SyncState.OutOfSync);
                        break;
                    }

                    var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    System.Diagnostics.Debug.WriteLine($"Received WebSocket message: {message}");

                    try
                    {
                        var change = JsonSerializer.Deserialize<FileChange>(message);
                        if (change != null)
                        {
                            await HandleRemoteChange(change);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Failed to deserialize WebSocket message to FileChange object");
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error deserializing WebSocket message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"WebSocket error: {ex.Message}");
                UpdateSyncState(SyncState.OutOfSync);
                
                // Attempt to reconnect if not disposed
                if (!_disposed)
                {
                    System.Diagnostics.Debug.WriteLine("Attempting to reconnect WebSocket...");
                    _ = ConnectWebSocket();
                }
            }
        }

        private async Task HandleRemoteChange(FileChange change)
        {
            try
            {
                if (change == null || change.FileMetadata == null || string.IsNullOrEmpty(change.FileMetadata.Path))
                {
                    System.Diagnostics.Debug.WriteLine("Invalid FileChange object received");
                    return;
                }

                var localPath = Path.Combine(_config.LibraryPath, change.FileMetadata.Path);
                
                // Temporarily disable file system watcher to prevent recursive updates
                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                }

                try
                {
                    switch (change.Type?.ToLower())
                    {
                        case "create":
                        case "update":
                            System.Diagnostics.Debug.WriteLine($"Processing remote {change.Type} for {change.FileMetadata.Path}");
                            await DownloadFile(change.FileMetadata.Path);
                            // Always notify about the library root for remote changes
                            System.Diagnostics.Debug.WriteLine("Triggering library root refresh");
                            FileSystemChanged?.Invoke(_config.LibraryPath);
                            break;
                            
                        case "delete":
                            if (File.Exists(localPath))
                            {
                                System.Diagnostics.Debug.WriteLine($"Processing remote delete for {change.FileMetadata.Path}");
                                File.Delete(localPath);
                                // Always notify about the library root for remote changes
                                System.Diagnostics.Debug.WriteLine("Triggering library root refresh");
                                FileSystemChanged?.Invoke(_config.LibraryPath);
                            }
                            break;

                        default:
                            System.Diagnostics.Debug.WriteLine($"Received unknown change type: {change.Type}");
                            break;
                    }
                }
                finally
                {
                    // Re-enable file system watcher after operation is complete
                    if (_watcher != null)
                    {
                        _watcher.EnableRaisingEvents = true;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling remote change: {ex}");
            }
        }

        private bool HasDirectoryAccess(string path)
        {
            try
            {
                // Try to get directory info without actually listing files
                var di = new DirectoryInfo(path);
                return di.Exists && (di.Attributes & FileAttributes.ReadOnly) == 0;
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"No access to directory: {path}");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking directory access: {ex.Message}");
                return false;
            }
        }

        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            if (_syncState != SyncState.Synchronized)
                return;

            try
            {
                var relativePath = Path.GetRelativePath(_config.LibraryPath, e.FullPath);
                
                switch (e.ChangeType)
                {
                    case WatcherChangeTypes.Created:
                        await UploadFile(relativePath);
                        break;
                        
                    case WatcherChangeTypes.Changed:
                        // Calculate hash of changed file
                        var newHash = await CalculateFileHash(e.FullPath);
                        
                        // Get file from server to compare hash
                        var response = await _httpClient.GetAsync($"/api/files/{Uri.EscapeDataString(relativePath)}");
                        if (response.IsSuccessStatusCode)
                        {
                            var fileMetadata = await JsonSerializer.DeserializeAsync<FileMetadata>(
                                await response.Content.ReadAsStreamAsync());
                                
                            // Only upload if hashes are different
                            if (fileMetadata?.Hash != newHash)
                            {
                                await UploadFile(relativePath);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Skipping upload for {relativePath} - hashes match");
                            }
                        }
                        else
                        {
                            // If we can't get the file metadata, upload to be safe
                            await UploadFile(relativePath);
                        }
                        break;
                        
                    case WatcherChangeTypes.Deleted:
                        await DeleteFile(relativePath);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file change: {ex.Message}");
            }
        }

        private async void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            try
            {
                var oldRelativePath = Path.GetRelativePath(_config.LibraryPath, e.OldFullPath);
                var newRelativePath = Path.GetRelativePath(_config.LibraryPath, e.FullPath);
                
                // Handle as delete + create
                await DeleteFile(oldRelativePath);
                await UploadFile(newRelativePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling file rename: {ex.Message}");
            }
        }

        private async Task SendWebSocketNotification(string type, string path)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    System.Diagnostics.Debug.WriteLine("WebSocket not connected, skipping notification");
                    return;
                }

                var notification = new
                {
                    type = type,
                    file = type != "delete" ? new
                    {
                        name = Path.GetFileName(path),
                        path = path.Replace('\\', '/'),
                        size = new FileInfo(Path.Combine(_config.LibraryPath, path)).Length,
                        isDirectory = false,
                        modTime = DateTime.UtcNow,
                        hash = await CalculateFileHash(Path.Combine(_config.LibraryPath, path))
                    } : null
                };

                var json = JsonSerializer.Serialize(notification);
                var bytes = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    CancellationToken.None);

                System.Diagnostics.Debug.WriteLine($"Sent WebSocket notification: {json}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending WebSocket notification: {ex.Message}");
            }
        }

        public async Task UploadFile(string relativePath)
        {
            var fullPath = Path.Combine(_config.LibraryPath, relativePath);
            if (!File.Exists(fullPath))
                return;

            try
            {
                // Normalize path separators to forward slashes
                var normalizedPath = relativePath.Replace('\\', '/');

                using var form = new MultipartFormDataContent();
                using var fileContent = new StreamContent(File.OpenRead(fullPath));
                form.Add(fileContent, "file", Path.GetFileName(normalizedPath));
                form.Add(new StringContent(normalizedPath), "relativePath");

                System.Diagnostics.Debug.WriteLine($"Uploading file: {relativePath}");
                var response = await _httpClient.PostAsync("/api/files", form);
                response.EnsureSuccessStatusCode();
                System.Diagnostics.Debug.WriteLine($"Successfully uploaded file: {relativePath}");
                
                // Server will send WebSocket notifications to all clients
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error uploading file {relativePath}: {ex}");
                throw;
            }
        }

        public async Task DownloadFile(string relativePath)
        {
            try
            {
                // Normalize path separators and encode the path for URL
                var normalizedPath = relativePath.Replace('\\', '/');
                var encodedPath = string.Join("/", normalizedPath.Split('/').Select(Uri.EscapeDataString));
                
                System.Diagnostics.Debug.WriteLine($"Downloading file: {relativePath} (encoded as: {encodedPath})");
                var response = await _httpClient.GetAsync($"/api/files/{encodedPath}");
                response.EnsureSuccessStatusCode();

                var fullPath = Path.Combine(_config.LibraryPath, relativePath);
                var directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fileStream = File.Create(fullPath);
                await response.Content.CopyToAsync(fileStream);
                System.Diagnostics.Debug.WriteLine($"Successfully downloaded file: {relativePath}");
                
                // Notify that a file has been downloaded
                FileSystemChanged?.Invoke(directory ?? _config.LibraryPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error downloading file {relativePath}: {ex}");
                throw;
            }
        }

        public async Task DeleteFile(string relativePath)
        {
            try
            {
                // Normalize path separators and encode the path for URL
                var normalizedPath = relativePath.Replace('\\', '/');
                var encodedPath = string.Join("/", normalizedPath.Split('/').Select(Uri.EscapeDataString));
                
                var response = await _httpClient.DeleteAsync($"/api/files/{encodedPath}");
                response.EnsureSuccessStatusCode();
                
                // Server will send WebSocket notifications to all clients
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error deleting file {relativePath}: {ex}");
                throw;
            }
        }

        private void UpdateSyncState(SyncState state, DateTime? lastSyncTime = null)
        {
            _syncState = state;
            if (state == SyncState.Synchronized)
            {
                _lastSyncTime = lastSyncTime ?? DateTime.Now;
                _config.LastWebSyncTime = _lastSyncTime;
            }
            SyncStateChanged?.Invoke(state, _lastSyncTime);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                if (_webSocketCts != null)
                {
                    try
                    {
                        if (!_webSocketCts.IsCancellationRequested)
                        {
                            _webSocketCts.Cancel();
                        }
                        _webSocketCts.Dispose();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore if already disposed
                    }
                }

                _webSocket?.Dispose();
                _httpClient?.Dispose();
                _watcher?.Dispose();
            }
            finally
            {
                _disposed = true;
            }
        }
    }
} 