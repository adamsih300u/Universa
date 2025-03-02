using System;
using System.Windows;
using System.Threading.Tasks;
using Universa.Desktop.WebSync;

namespace Universa.Desktop.Dialogs
{
    public partial class FileConflictDialog : Window
    {
        private readonly TaskCompletionSource<FileConflictResolution> _completionSource;

        public FileConflictDialog(string filePath, DateTime localModified, DateTime remoteModified, long localSize, long remoteSize)
        {
            InitializeComponent();
            _completionSource = new TaskCompletionSource<FileConflictResolution>();

            FilePathText.Text = filePath;
            LocalModifiedText.Text = localModified.ToLocalTime().ToString("g");
            RemoteModifiedText.Text = remoteModified.ToLocalTime().ToString("g");
            LocalSizeText.Text = FormatFileSize(localSize);
            RemoteSizeText.Text = FormatFileSize(remoteSize);
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void KeepLocalButton_Click(object sender, RoutedEventArgs e)
        {
            _completionSource.SetResult(FileConflictResolution.KeepLocal);
            Close();
        }

        private void KeepRemoteButton_Click(object sender, RoutedEventArgs e)
        {
            _completionSource.SetResult(FileConflictResolution.KeepRemote);
            Close();
        }

        private void KeepBothButton_Click(object sender, RoutedEventArgs e)
        {
            _completionSource.SetResult(FileConflictResolution.KeepBoth);
            Close();
        }

        public Task<FileConflictResolution> GetUserDecisionAsync()
        {
            return _completionSource.Task;
        }
    }
} 