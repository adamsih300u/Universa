using System.Collections.Generic;

namespace Universa.Desktop.Models
{
    public class SyncChanges
    {
        public List<SyncFileInfo> FilesToUpload { get; set; } = new List<SyncFileInfo>();
        public List<SyncFileInfo> FilesToDownload { get; set; } = new List<SyncFileInfo>();
        public List<SyncFileInfo> FilesToDelete { get; set; } = new List<SyncFileInfo>();
    }

    public class SyncFileInfo
    {
        public string RelativePath { get; set; }
        public string FullPath { get; set; }
        public long Size { get; set; }
        public string LastModified { get; set; }
    }

    public class CompareResult
    {
        public List<SyncFileInfo> FilesToUpload { get; set; } = new List<SyncFileInfo>();
        public List<SyncFileInfo> FilesToDownload { get; set; } = new List<SyncFileInfo>();
        public List<SyncFileInfo> FilesToDelete { get; set; } = new List<SyncFileInfo>();
        public string Error { get; set; }
    }
} 