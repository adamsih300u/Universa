using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Models;

namespace Universa.Desktop.Dialogs
{
    public partial class SyncConfirmationDialog : Window
    {
        public bool IsConfirmed { get; private set; }

        public SyncConfirmationDialog(SyncChanges changes)
        {
            InitializeComponent();

            // Populate the lists
            UploadList.ItemsSource = changes.FilesToUpload;
            DownloadList.ItemsSource = changes.FilesToDownload;
            DeleteList.ItemsSource = changes.FilesToDelete;

            // Update tab headers with counts
            var tabControl = (TabControl)LogicalTreeHelper.FindLogicalNode(this, "MainTabControl");
            foreach (TabItem tab in tabControl.Items)
            {
                var header = tab.Header.ToString();
                var count = header switch
                {
                    "Files to Upload" => changes.FilesToUpload.Count,
                    "Files to Download" => changes.FilesToDownload.Count,
                    "Files to Delete" => changes.FilesToDelete.Count,
                    _ => 0
                };
                tab.Header = $"{header} ({count})";
            }
        }

        private void ConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = true;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            DialogResult = false;
            Close();
        }
    }
} 