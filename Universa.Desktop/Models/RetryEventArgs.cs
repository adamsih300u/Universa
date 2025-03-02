using System;

namespace Universa.Desktop.Models
{
    public class RetryEventArgs : EventArgs
    {
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public double DelayMs { get; set; }
    }
} 