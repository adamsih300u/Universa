using System;
using System.Collections.Generic;
using System.Linq;

namespace Universa.Desktop.Models
{
    public class Playlist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
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

        public Playlist()
        {
        }

        public Playlist(string id, string name, string description = null, string imageUrl = null)
        {
            Id = id;
            Name = name;
            Description = description;
            ImageUrl = imageUrl;
            Created = DateTime.UtcNow;
            LastModified = DateTime.UtcNow;
        }
    }
} 