using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Universa.Desktop.Models;

namespace Universa.Desktop.Library
{
    public class LibraryTreeItem : INotifyPropertyChanged
    {
        private string _name;
        private string _path;
        private LibraryItemType _type;
        private string _icon;
        private ObservableCollection<LibraryTreeItem> _children;
        private bool _isExpanded;
        private object _tag;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Path
        {
            get => _path;
            set
            {
                if (_path != value)
                {
                    _path = value;
                    OnPropertyChanged();
                }
            }
        }

        public LibraryItemType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Icon));
                }
            }
        }

        public string Icon
        {
            get
            {
                if (!string.IsNullOrEmpty(_icon))
                    return _icon;
                    
                return Type switch
                {
                    LibraryItemType.Directory => "📁",
                    LibraryItemType.File => GetFileIcon(),
                    LibraryItemType.Service => "🔌",
                    LibraryItemType.Category => "📂",
                    LibraryItemType.Overview => "📊",
                    LibraryItemType.Inbox => "📥",
                    LibraryItemType.GlobalAgenda => "🗓️",
                    _ => "📄"
                };
            }
            set
            {
                if (_icon != value)
                {
                    _icon = value;
                    OnPropertyChanged();
                }
            }
        }

        private string GetFileIcon()
        {
            if (string.IsNullOrEmpty(Path)) return "📄";
            return System.IO.Path.GetExtension(Path).ToLower() switch
            {
                ".md" => "📝",
                ".todo" => "✓",
                ".project" => "📋",
                _ => "📄"
            };
        }

        public ObservableCollection<LibraryTreeItem> Children
        {
            get => _children;
            set
            {
                if (_children != value)
                {
                    _children = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool ContainsTodoFiles()
        {
            if (!Directory.Exists(Path)) return false;
            
            // Check immediate .todo files
            var hasTodoFiles = Directory.GetFiles(Path, "*.todo", SearchOption.TopDirectoryOnly).Any();
            
            // Check subdirectories for .todo files
            if (!hasTodoFiles)
            {
                hasTodoFiles = Directory.GetDirectories(Path)
                    .Any(dir => Directory.GetFiles(dir, "*.todo", SearchOption.AllDirectories).Any());
            }
            
            return hasTodoFiles;
        }

        public string FullPath => Path;

        public bool IsFolder => Type == LibraryItemType.Directory;

        public object Tag
        {
            get => _tag;
            set
            {
                if (_tag != value)
                {
                    _tag = value;
                    OnPropertyChanged();
                }
            }
        }
    }
} 