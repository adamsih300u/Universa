using System;
using System.Collections.Generic;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Represents the persistent state of WebDAV sync for 3-way merge detection
    /// </summary>
    public class WebDavSyncState
    {
        /// <summary>
        /// Dictionary of file paths to their last known sync state
        /// Key: Relative file path (e.g., "Writing/story.md")
        /// Value: File sync state
        /// </summary>
        public Dictionary<string, FileSyncState> Files { get; set; } = new Dictionary<string, FileSyncState>();

        /// <summary>
        /// Last successful sync timestamp
        /// </summary>
        public DateTime? LastSuccessfulSync { get; set; }

        /// <summary>
        /// Remote folder being synced
        /// </summary>
        public string RemoteFolder { get; set; }
    }

    /// <summary>
    /// Represents the last known state of a file after successful sync
    /// </summary>
    public class FileSyncState
    {
        /// <summary>
        /// The ETag (MD5 hash) of the remote file when last synced
        /// Used to detect if remote file changed since last sync
        /// </summary>
        public string LastRemoteETag { get; set; }

        /// <summary>
        /// The ETag (MD5 hash) of the local file when last synced
        /// Used to detect if local file changed since last sync
        /// </summary>
        public string LastLocalETag { get; set; }

        /// <summary>
        /// Remote file modification time when last synced
        /// </summary>
        public DateTime LastRemoteModified { get; set; }

        /// <summary>
        /// Local file modification time when last synced
        /// </summary>
        public DateTime LastLocalModified { get; set; }

        /// <summary>
        /// When this file was last successfully synced
        /// </summary>
        public DateTime LastSyncTime { get; set; }

        /// <summary>
        /// File size when last synced (for quick change detection)
        /// </summary>
        public long LastSize { get; set; }
    }
}








