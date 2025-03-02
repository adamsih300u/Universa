using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Universa.Desktop.Models
{
    public class MediaItem : INotifyPropertyChanged
    {
        private string _id;
        private string _name;
        private MediaItemType _type;
        private string _path;
        private string _parentId;
        private string _overview;
        private string _imagePath;
        private DateTime _dateAdded;
        private bool _hasChildren;
        private bool _isPlayed;
        private int? _year;
        private TimeSpan? _duration;
        private string _seriesName;
        private string _seasonName;
        private int? _seasonNumber;
        private int? _episodeNumber;
        private string _streamUrl;
        private Dictionary<string, string> _metadata;
        private bool _isExpanded;
        private ObservableCollection<MediaItem> _children;
        private bool _isFolder;

        public MediaItem()
        {
            _children = new ObservableCollection<MediaItem>();
            _metadata = new Dictionary<string, string>();
            _dateAdded = DateTime.Now;
            _isExpanded = false;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public string Id
        {
            get => _id;
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
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

        public MediaItemType Type
        {
            get => _type;
            set
            {
                if (_type != value)
                {
                    _type = value;
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

        public string ParentId
        {
            get => _parentId;
            set
            {
                if (_parentId != value)
                {
                    _parentId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Overview
        {
            get => _overview;
            set
            {
                if (_overview != value)
                {
                    _overview = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ImagePath
        {
            get => _imagePath;
            set
            {
                if (_imagePath != value)
                {
                    _imagePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime DateAdded
        {
            get => _dateAdded;
            set
            {
                if (_dateAdded != value)
                {
                    _dateAdded = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                if (_hasChildren != value)
                {
                    _hasChildren = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsPlayed
        {
            get => _isPlayed;
            set
            {
                if (_isPlayed != value)
                {
                    _isPlayed = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? Year
        {
            get => _year;
            set
            {
                if (_year != value)
                {
                    _year = value;
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan? Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SeriesName
        {
            get => _seriesName;
            set
            {
                if (_seriesName != value)
                {
                    _seriesName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SeasonName
        {
            get => _seasonName;
            set
            {
                if (_seasonName != value)
                {
                    _seasonName = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? SeasonNumber
        {
            get => _seasonNumber;
            set
            {
                if (_seasonNumber != value)
                {
                    _seasonNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        public int? EpisodeNumber
        {
            get => _episodeNumber;
            set
            {
                if (_episodeNumber != value)
                {
                    _episodeNumber = value;
                    OnPropertyChanged();
                }
            }
        }

        public string StreamUrl
        {
            get => _streamUrl;
            set
            {
                if (_streamUrl != value)
                {
                    _streamUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public Dictionary<string, string> Metadata
        {
            get => _metadata ??= new Dictionary<string, string>();
            set
            {
                if (_metadata != value)
                {
                    _metadata = value;
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

        public virtual ObservableCollection<MediaItem> Children
        {
            get => _children;
            set
            {
                if (_children != value)
                {
                    _children = value;
                    HasChildren = value != null && value.Any();
                    OnPropertyChanged();
                }
            }
        }

        public bool IsFolder
        {
            get => _isFolder;
            set
            {
                if (_isFolder != value)
                {
                    _isFolder = value;
                    OnPropertyChanged();
                }
            }
        }

        public override string ToString()
        {
            return Name ?? base.ToString();
        }
    }
} 