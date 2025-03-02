namespace Universa.Desktop.Models
{
    public enum SyncStatus
    {
        Unknown,
        Idle,
        InSync,
        Syncing,
        Uploading,
        Downloading,
        Success,
        Error,
        Conflicted,
        Deleted
    }
} 