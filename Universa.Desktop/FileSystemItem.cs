using System;
using System.IO;
using System.Windows.Media;
using System.Windows;
using System.Collections.ObjectModel;

namespace Universa.Desktop
{
    public class FileSystemItem
    {
        public string Name { get; set; }
        public string FullPath { get; set; }
        public bool IsDirectory { get; set; }
        public ObservableCollection<FileSystemItem> Items { get; set; }
        public Geometry IconData { get; private set; }

        public FileSystemItem(string path, bool isDirectory)
        {
            FullPath = path;
            Name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(Name)) // For root directory
            {
                Name = path;
            }
            IsDirectory = isDirectory;
            
            if (isDirectory)
            {
                Items = new ObservableCollection<FileSystemItem>();
                // Folder icon path data
                IconData = Geometry.Parse("M3,3H21V21H3V3M3,7V19H19V7H3Z");
                LoadSubDirectories();
            }
            else
            {
                Items = null;
                // File icon path data
                IconData = Geometry.Parse("M13,9V3.5L18.5,9M6,2C4.89,2 4,2.89 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2H6Z");
            }
        }

        private void LoadSubDirectories()
        {
            try
            {
                // Add directories
                foreach (string dir in Directory.GetDirectories(FullPath))
                {
                    Items.Add(new FileSystemItem(dir, true));
                }

                // Add files
                foreach (string file in Directory.GetFiles(FullPath))
                {
                    if (Path.GetExtension(file).ToLower() == ".md")
                    {
                        Items.Add(new FileSystemItem(file, false));
                    }
                }
            }
            catch
            {
                // Handle any exceptions (access denied, etc.)
            }
        }
    }
} 