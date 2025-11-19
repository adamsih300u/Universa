using System;
using System.Threading.Tasks;

namespace Universa.Desktop.Interfaces
{
    public enum WebDavSyncStatus
    {
        Idle,
        Syncing,
        Success,
        Error,
        Conflicted
    }

    public class WebDavSyncStatusEventArgs : EventArgs
    {
        public WebDavSyncStatus Status { get; }
        public string Message { get; }
        public DateTime? LastSyncTime { get; }

        public WebDavSyncStatusEventArgs(WebDavSyncStatus status, string message = null, DateTime? lastSyncTime = null)
        {
            Status = status;
            Message = message;
            LastSyncTime = lastSyncTime;
        }
    }

    public class FileDownloadedEventArgs : EventArgs
    {
        public string LocalPath { get; }
        public string RelativePath { get; }

        public FileDownloadedEventArgs(string localPath, string relativePath)
        {
            LocalPath = localPath;
            RelativePath = relativePath;
        }
    }

    /// <summary>
    /// Service interface for WebDAV synchronization
    /// </summary>
    public interface IWebDavSyncService
    {
        /// <summary>
        /// Event raised when sync status changes
        /// </summary>
        event EventHandler<WebDavSyncStatusEventArgs> SyncStatusChanged;

        /// <summary>
        /// Event raised when a file is downloaded from remote (for refreshing open editors)
        /// </summary>
        event EventHandler<FileDownloadedEventArgs> FileDownloaded;

        /// <summary>
        /// Gets the current sync status
        /// </summary>
        WebDavSyncStatus CurrentStatus { get; }

        /// <summary>
        /// Gets the last successful sync time
        /// </summary>
        DateTime? LastSyncTime { get; }

        /// <summary>
        /// Gets whether auto-sync is currently enabled
        /// </summary>
        bool IsAutoSyncEnabled { get; }

        /// <summary>
        /// Tests the WebDAV connection with current configuration
        /// </summary>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Performs a manual synchronization
        /// </summary>
        Task SynchronizeAsync();

        /// <summary>
        /// Starts auto-sync with the configured interval
        /// </summary>
        void StartAutoSync();

        /// <summary>
        /// Stops auto-sync
        /// </summary>
        void StopAutoSync();

        /// <summary>
        /// Refreshes configuration from settings
        /// </summary>
        void RefreshConfiguration();
    }
}


