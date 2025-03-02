using System;
using System.Collections.Generic;

namespace Universa.Desktop.Models
{
    public class Artist
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string ImageUrl { get; set; }
        public List<string> AlbumIds { get; set; } = new List<string>();
        public int SongCount { get; set; }
        public DateTime Created { get; set; } = DateTime.UtcNow;
        public DateTime LastModified { get; set; } = DateTime.UtcNow;
        public DateTime LastUpdated { get; set; }

        public Artist()
        {
        }

        public Artist(string id, string name, string imageUrl = null)
        {
            Id = id;
            Name = name;
            ImageUrl = imageUrl;
            Created = DateTime.UtcNow;
            LastModified = DateTime.UtcNow;
        }
    }
} 