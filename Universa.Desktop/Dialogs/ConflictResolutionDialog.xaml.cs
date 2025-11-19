using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Dialogs
{
    /// <summary>
    /// Dialog for resolving WebDAV sync conflicts
    /// TODO: Implement in future milestone for interactive conflict resolution
    /// </summary>
    public partial class ConflictResolutionDialog : Window, INotifyPropertyChanged
    {
        private string _filePath;
        private string _localModifiedDisplay;
        private string _remoteModifiedDisplay;

        public event PropertyChangedEventHandler PropertyChanged;

        public ConflictResolutionChoice Result { get; private set; } = ConflictResolutionChoice.Skip;
        public bool ApplyToAll => ApplyToAllCheckBox?.IsChecked ?? false;

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                OnPropertyChanged();
            }
        }

        public string LocalModifiedDisplay
        {
            get => _localModifiedDisplay;
            set
            {
                _localModifiedDisplay = value;
                OnPropertyChanged();
            }
        }

        public string RemoteModifiedDisplay
        {
            get => _remoteModifiedDisplay;
            set
            {
                _remoteModifiedDisplay = value;
                OnPropertyChanged();
            }
        }

        public ConflictResolutionDialog(
            string filePath,
            DateTime localModified,
            DateTime remoteModified)
        {
            InitializeComponent();
            DataContext = this;

            FilePath = filePath;
            LocalModifiedDisplay = $"Modified: {localModified:yyyy-MM-dd HH:mm:ss}";
            RemoteModifiedDisplay = $"Modified: {remoteModified:yyyy-MM-dd HH:mm:ss}";
        }

        private void KeepLocal_Click(object sender, RoutedEventArgs e)
        {
            Result = ConflictResolutionChoice.KeepLocal;
            DialogResult = true;
            Close();
        }

        private void KeepRemote_Click(object sender, RoutedEventArgs e)
        {
            Result = ConflictResolutionChoice.KeepRemote;
            DialogResult = true;
            Close();
        }

        private void KeepBoth_Click(object sender, RoutedEventArgs e)
        {
            Result = ConflictResolutionChoice.KeepBoth;
            DialogResult = true;
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            Result = ConflictResolutionChoice.Skip;
            DialogResult = false;
            Close();
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}








