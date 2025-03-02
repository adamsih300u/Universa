using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using Universa.Desktop.Windows;

namespace Universa.Desktop
{
    public class FileItem
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Size { get; set; }
        public string Modified { get; set; }
        public string FullPath { get; set; }
    }

    public partial class FolderTab : UserControl
    {
        public string FolderPath { get; private set; }

        public FolderTab(string path)
        {
            FolderPath = path;
            InitializeComponent();
            LoadFolder(path);
        }

        private void LoadFolder(string folderPath)
        {
            try
            {
                var rootItem = new TreeViewItem
                {
                    Header = Path.GetFileName(folderPath),
                    Tag = folderPath
                };
                LoadSubdirectories(rootItem, folderPath);
                _folderTree.Items.Add(rootItem);
                rootItem.IsExpanded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading folder content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadSubdirectories(TreeViewItem parentItem, string path)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(path))
                {
                    var item = new TreeViewItem
                    {
                        Header = Path.GetFileName(dir),
                        Tag = dir
                    };
                    parentItem.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading subdirectories: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadFiles(string path)
        {
            try
            {
                _fileList.Items.Clear();
                var files = Directory.GetFiles(path)
                    .Select(f => new FileItem
                    {
                        Name = Path.GetFileName(f),
                        Type = Path.GetExtension(f).TrimStart('.').ToUpper(),
                        Size = new FileInfo(f).Length.ToString("N0") + " bytes",
                        Modified = File.GetLastWriteTime(f).ToString("g"),
                        FullPath = f
                    });

                foreach (var file in files)
                {
                    _fileList.Items.Add(file);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading files: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item && item.Tag is string path)
            {
                LoadFiles(path);
                
                // Load subdirectories if not already loaded
                if (item.Items.Count == 0)
                {
                    LoadSubdirectories(item, path);
                }
            }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            if (_folderTree.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                LoadFiles(path);
                item.Items.Clear();
                LoadSubdirectories(item, path);
            }
        }

        private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_fileList.SelectedItem is FileItem file)
            {
                if (IsMusicFile(file.FullPath))
                {
                    var mainWindow = Window.GetWindow(this) as IMediaWindow;
                    if (mainWindow != null)
                    {
                        mainWindow.MediaPlayerManager?.SetPlaylist(
                            new List<Models.Track> { new Models.Track 
                            { 
                                StreamUrl = file.FullPath,
                                Title = file.Name
                            }}
                        );
                    }
                }
            }
        }

        private bool IsMusicFile(string path)
        {
            var musicExtensions = new[] { ".mp3", ".m4a", ".wav", ".wma", ".ogg", ".flac" };
            return musicExtensions.Contains(Path.GetExtension(path).ToLower());
        }

        public void ApplyTheme(bool isDarkTheme)
        {
            if (_folderTree != null)
            {
                _folderTree.Background = Application.Current.Resources["WindowBackgroundBrush"] as SolidColorBrush;
                _folderTree.Foreground = Application.Current.Resources["TextBrush"] as SolidColorBrush;
                _folderTree.BorderBrush = Application.Current.Resources["BorderBrush"] as SolidColorBrush;
            }

            if (_fileList != null)
            {
                _fileList.Background = Application.Current.Resources["WindowBackgroundBrush"] as SolidColorBrush;
                _fileList.Foreground = Application.Current.Resources["TextBrush"] as SolidColorBrush;
                _fileList.BorderBrush = Application.Current.Resources["BorderBrush"] as SolidColorBrush;
            }

            if (_refreshButton != null)
            {
                _refreshButton.Background = Application.Current.Resources["ButtonBackgroundBrush"] as SolidColorBrush;
                _refreshButton.Foreground = Application.Current.Resources["TextBrush"] as SolidColorBrush;
                _refreshButton.BorderBrush = Application.Current.Resources["BorderBrush"] as SolidColorBrush;
            }
        }
    }
} 