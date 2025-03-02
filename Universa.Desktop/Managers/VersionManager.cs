using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Universa.Desktop.Managers
{
    public class VersionManager
    {
        private const int MaxVersions = 10;
        private static VersionManager _instance;
        private static readonly object _lock = new object();

        public static VersionManager GetInstance()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new VersionManager();
                }
            }
            return _instance;
        }

        private VersionManager() { }

        public async Task SaveVersion(string filePath)
        {
            try
            {
                Debug.WriteLine($"\n[VersionManager] Starting SaveVersion for file: {filePath}");

                if (!File.Exists(filePath))
                {
                    Debug.WriteLine($"[VersionManager] File does not exist: {filePath}");
                    return;
                }

                // Check if this file type should be versioned
                if (!LibraryManager.Instance.IsVersionedFile(filePath))
                {
                    Debug.WriteLine($"[VersionManager] File type not configured for versioning: {filePath}");
                    return;
                }

                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var versionsDir = Path.Combine(directory, ".versions");

                Debug.WriteLine($"[VersionManager] Directory: {directory}");
                Debug.WriteLine($"[VersionManager] FileName: {fileName}");
                Debug.WriteLine($"[VersionManager] Extension: {extension}");
                Debug.WriteLine($"[VersionManager] VersionsDir: {versionsDir}");

                // Create versions directory if it doesn't exist
                if (!Directory.Exists(versionsDir))
                {
                    Debug.WriteLine($"[VersionManager] Creating versions directory: {versionsDir}");
                    try
                    {
                        // Create the directory with normal attributes first
                        Directory.CreateDirectory(versionsDir);
                        
                        // Then set it as hidden
                        var dirInfo = new DirectoryInfo(versionsDir);
                        if ((dirInfo.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
                        {
                            dirInfo.Attributes |= FileAttributes.Hidden;
                            Debug.WriteLine($"[VersionManager] Set hidden attribute on versions directory");
                        }
                        Debug.WriteLine($"[VersionManager] Successfully created versions directory");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VersionManager] Error creating versions directory: {ex.Message}");
                        Debug.WriteLine($"[VersionManager] Stack trace: {ex.StackTrace}");
                        throw;
                    }
                }
                else
                {
                    Debug.WriteLine($"[VersionManager] Versions directory already exists");
                }

                // Generate version file name with timestamp
                var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                var versionPath = Path.Combine(versionsDir, $"{fileName}.{timestamp}{extension}");
                Debug.WriteLine($"[VersionManager] Version path: {versionPath}");

                // Copy current file to versions directory
                try
                {
                    Debug.WriteLine($"[VersionManager] Attempting to copy file from {filePath} to {versionPath}");
                    File.Copy(filePath, versionPath, true);
                    Debug.WriteLine($"[VersionManager] Successfully copied file to version path");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VersionManager] Error copying file to version path: {ex.Message}");
                    Debug.WriteLine($"[VersionManager] Stack trace: {ex.StackTrace}");
                    throw;
                }

                // Clean up old versions
                await CleanupOldVersions(filePath);
                Debug.WriteLine($"[VersionManager] SaveVersion completed successfully\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionManager] Error in SaveVersion: {ex.Message}");
                Debug.WriteLine($"[VersionManager] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public List<FileVersionInfo> GetVersions(string filePath)
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var versionsDir = Path.Combine(directory, ".versions");

                Debug.WriteLine($"[VersionManager] Getting versions for file: {filePath}");
                Debug.WriteLine($"[VersionManager] Versions directory: {versionsDir}");

                if (!Directory.Exists(versionsDir))
                {
                    Debug.WriteLine("[VersionManager] Versions directory does not exist");
                    return new List<FileVersionInfo>();
                }

                var versionFiles = Directory.GetFiles(versionsDir, $"{fileName}.*{extension}")
                    .Select(path =>
                    {
                        var fileInfo = new FileInfo(path);
                        var timestampStr = Path.GetFileNameWithoutExtension(path)
                            .Substring(fileName.Length + 1); // +1 for the dot
                        
                        if (DateTime.TryParseExact(timestampStr, "yyyyMMddHHmmss", 
                            null, System.Globalization.DateTimeStyles.AssumeUniversal, 
                            out DateTime timestamp))
                        {
                            return new FileVersionInfo
                            {
                                Path = path,
                                Timestamp = timestamp,
                                Size = fileInfo.Length
                            };
                        }
                        return null;
                    })
                    .Where(v => v != null)
                    .OrderByDescending(v => v.Timestamp)
                    .ToList();

                Debug.WriteLine($"[VersionManager] Found {versionFiles.Count} version files");

                // If we have more versions than the maximum allowed, clean them up
                if (versionFiles.Count > MaxVersions)
                {
                    Debug.WriteLine($"[VersionManager] Cleaning up old versions (keeping {MaxVersions} most recent)");
                    _ = CleanupOldVersions(filePath);
                    // Return only the MaxVersions most recent versions
                    versionFiles = versionFiles.Take(MaxVersions).ToList();
                }

                return versionFiles;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionManager] Error getting versions: {ex.Message}");
                Debug.WriteLine($"[VersionManager] Stack trace: {ex.StackTrace}");
                return new List<FileVersionInfo>();
            }
        }

        public async Task<string> LoadVersion(string versionPath)
        {
            try
            {
                if (!File.Exists(versionPath))
                    throw new FileNotFoundException("Version file not found", versionPath);

                return await File.ReadAllTextAsync(versionPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading version: {ex.Message}");
                throw;
            }
        }

        private async Task CleanupOldVersions(string filePath)
        {
            try
            {
                Debug.WriteLine($"[VersionManager] Starting cleanup of old versions for: {filePath}");
                var directory = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var extension = Path.GetExtension(filePath);
                var versionsDir = Path.Combine(directory, ".versions");

                if (!Directory.Exists(versionsDir))
                {
                    Debug.WriteLine("[VersionManager] Versions directory does not exist, nothing to clean up");
                    return;
                }

                var allVersions = Directory.GetFiles(versionsDir, $"{fileName}.*{extension}")
                    .Select(path =>
                    {
                        var fileInfo = new FileInfo(path);
                        var timestampStr = Path.GetFileNameWithoutExtension(path)
                            .Substring(fileName.Length + 1);
                        
                        if (DateTime.TryParseExact(timestampStr, "yyyyMMddHHmmss", 
                            null, System.Globalization.DateTimeStyles.AssumeUniversal, 
                            out DateTime timestamp))
                        {
                            return new { Path = path, Timestamp = timestamp };
                        }
                        return null;
                    })
                    .Where(v => v != null)
                    .OrderByDescending(v => v.Timestamp)
                    .ToList();

                if (allVersions.Count <= MaxVersions)
                {
                    Debug.WriteLine("[VersionManager] No cleanup needed, version count within limit");
                    return;
                }

                var versionsToDelete = allVersions.Skip(MaxVersions);
                foreach (var version in versionsToDelete)
                {
                    try
                    {
                        Debug.WriteLine($"[VersionManager] Deleting old version: {version.Path}");
                        File.Delete(version.Path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[VersionManager] Error deleting version {version.Path}: {ex.Message}");
                    }
                }

                Debug.WriteLine("[VersionManager] Cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VersionManager] Error cleaning up versions: {ex.Message}");
                Debug.WriteLine($"[VersionManager] Stack trace: {ex.StackTrace}");
            }
        }
    }

    public class FileVersionInfo
    {
        public string Path { get; set; }
        public DateTime Timestamp { get; set; }
        public long Size { get; set; }

        public string DisplayName => $"{Timestamp:dd MMM yyyy HH:mm:ss} ({FormatSize(Size)})";

        private string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {sizes[order]}";
        }
    }
} 