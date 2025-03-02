using System;
using System.Collections.Generic;
using System.Linq;

namespace Universa.Desktop.Models
{
    public class Album
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string CoverArtUrl { get; set; }
        public string Description { get; set; }
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public string ImageUrl { get; set; }
        public int Year { get; set; }
        public List<Track> Tracks { get; set; } = new List<Track>();
        public int SongCount => Tracks?.Count ?? 0;
        public TimeSpan Duration => CalculateDuration();
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public DateTime LastUpdated { get; set; }

        private TimeSpan CalculateDuration()
        {
            if (Tracks == null || Tracks.Count == 0) return TimeSpan.Zero;
            return TimeSpan.FromTicks(Tracks.Sum(t => t.Duration.Ticks));
        }

        public Album()
        {
        }

        public Album(string id, string name, string artistId, string artistName, string imageUrl = null, int year = 0)
        {
            Id = id;
            Title = name;
            ArtistId = artistId;
            ArtistName = artistName;
            ImageUrl = imageUrl;
            Year = year;
            Created = DateTime.UtcNow;
            LastModified = DateTime.UtcNow;
        }
    }
} 