using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Collections.Generic;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class MediaItem : INotifyPropertyChanged
    {
        private string _name;
        private MediaItemType _type;
        private bool _hasChildren;
        private bool _isPlaying;
        private bool _isPlayed;
        private ObservableCollection<MediaItem> _children;
        private string _parentId;
        private DateTime _dateAdded;
        private string _imagePath;
        private Dictionary<string, string> _metadata;

        public string Id { get; set; }

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

        public bool HasChildren
        {
            get => _hasChildren;
            set
            {
                if (_hasChildren != value)
                {
                    _hasChildren = value;
                    OnPropertyChanged();
                    if (value && Children == null)
                    {
                        Children = new ObservableCollection<MediaItem>();
                    }
                }
            }
        }

        public ObservableCollection<MediaItem> Children
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

        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
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

        public int? Year { get; set; }
        public TimeSpan? Duration { get; set; }
        public string SeriesName { get; set; }
        public string SeasonName { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
        public string Overview { get; set; }
        public string Path { get; set; }
        public string StreamUrl { get; set; }

        public string DisplayName
        {
            get
            {
                if (Type == MediaItemType.Episode && !string.IsNullOrEmpty(SeriesName))
                {
                    var parts = new List<string>();
                    
                    // Add series name
                    parts.Add(SeriesName);
                    
                    // Add season info if available
                    if (SeasonNumber.HasValue)
                    {
                        parts.Add($"S{SeasonNumber:D2}");
                    }
                    
                    // Add episode info if available
                    if (EpisodeNumber.HasValue)
                    {
                        parts.Add($"E{EpisodeNumber:D2}");
                    }
                    
                    // Add episode name
                    if (!string.IsNullOrEmpty(Name))
                    {
                        parts.Add(Name);
                    }
                    
                    return string.Join(" - ", parts);
                }
                
                return Name;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 