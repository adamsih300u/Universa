using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Text.Json.Serialization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Linq;

namespace Universa.Desktop.Models
{
    public enum MusicItemType
    {
        Root,
        Category,
        Folder,
        Artist,
        Album,
        Track,
        Playlist
    }

    public class MusicItem : MediaItem
    {
        private bool _isPlaying;
        private string _name;
        private string _artist;
        private string _album;
        private ObservableCollection<MusicItem> _items;

        // Basic properties - these override the base class properties
        public new string Id { get; set; }
        public new string Name { get; set; }
        public new MusicItemType Type { get; set; }
        public new string StreamUrl { get; set; }
        public new DateTime DateAdded { get; set; }

        // Artist-specific properties
        public string ImageUrl { get; set; }

        // Album-specific properties
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public int Year { get; set; }

        // Track-specific properties
        public string Artist { get; set; }
        public string AlbumArtist { get; set; }
        public string Album { get; set; }
        public string Genre { get; set; }
        public int TrackNumber { get; set; }
        public TimeSpan Duration { get; set; }

        // Playlist-specific properties
        public string Description { get; set; }

        [JsonIgnore]
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

        [JsonIgnore]
        public bool HasChildren
        {
            get => _items?.Any() == true;
            set
            {
                if (value != HasChildren)
                {
                    if (value)
                    {
                        _items = new ObservableCollection<MusicItem>();
                    }
                    else
                    {
                        _items = null;
                    }
                    OnPropertyChanged();
                }
            }
        }

        [JsonPropertyName("Items")]
        public ObservableCollection<MusicItem> Items
        {
            get => _items;
            set
            {
                _items = value;
                HasChildren = value?.Any() == true;
                OnPropertyChanged();
            }
        }

        // Override the base class Children property to use Items
        [JsonIgnore]
        public override ObservableCollection<MediaItem> Children
        {
            get => new ObservableCollection<MediaItem>(Items.Cast<MediaItem>());
            set
            {
                if (value != null)
                {
                    Items = new ObservableCollection<MusicItem>(value.Cast<MusicItem>());
                }
            }
        }

        [JsonIgnore]
        public Geometry IconData { get; set; }

        public void InitializeIconData()
        {
            IconData = GetIconForType(Type);
        }

        public static Geometry GetIconForType(MusicItemType type)
        {
            switch (type)
            {
                case MusicItemType.Artist:
                    return Geometry.Parse("M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z");
                case MusicItemType.Album:
                    return Geometry.Parse("M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9Z");
                case MusicItemType.Track:
                    return Geometry.Parse("M12 3V13.55C11.41 13.21 10.73 13 10 13C7.79 13 6 14.79 6 17S7.79 21 10 21 14 19.21 14 17V7H18V3H12Z");
                case MusicItemType.Playlist:
                    return Geometry.Parse("M15,6H3V8H15V6M15,10H3V12H15V10M3,16H11V14H3V16M17,6V14.18C16.69,14.07 16.35,14 16,14A3,3 0 0,0 13,17A3,3 0 0,0 16,20A3,3 0 0,0 19,17V8H22V6H17Z");
                case MusicItemType.Folder:
                    return Geometry.Parse("M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z");
                default:
                    return null;
            }
        }

        public new MusicItem Clone()
        {
            return new MusicItem
            {
                Id = this.Id,
                Name = this.Name,
                Type = this.Type,
                IsPlaying = this.IsPlaying,
                StreamUrl = this.StreamUrl,
                ImageUrl = this.ImageUrl,
                ArtistId = this.ArtistId,
                ArtistName = this.ArtistName,
                Year = this.Year,
                Artist = this.Artist,
                AlbumArtist = this.AlbumArtist,
                Album = this.Album,
                Genre = this.Genre,
                TrackNumber = this.TrackNumber,
                Duration = this.Duration,
                Description = this.Description,
                HasChildren = this.HasChildren
            };
        }

        [JsonConstructor]
        public MusicItem()
        {
            Items = new ObservableCollection<MusicItem>();
            InitializeIconData();
        }

        public void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }
} 