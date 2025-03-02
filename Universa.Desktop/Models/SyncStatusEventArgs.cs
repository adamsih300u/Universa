using System;

namespace Universa.Desktop.Models
{
    public class SyncStatusEventArgs : EventArgs
    {
        public SyncStatus Status { get; }
        public string Message { get; }

        public SyncStatusEventArgs(SyncStatus status, string message = null)
        {
            Status = status;
            Message = message;
        }
    }
} 