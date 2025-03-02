using System;

namespace Universa.Desktop.Models
{
    public class MediaLibraryItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public MediaItemType Type { get; set; }
        public string StreamUrl { get; set; }
        public string Path { get; set; }
        public string Overview { get; set; }
        public string ImagePath { get; set; }
        public DateTime DateAdded { get; set; }
        public string ParentId { get; set; }
        public bool HasChildren { get; set; }
        public bool IsPlayable => Type == MediaItemType.Movie || Type == MediaItemType.Episode;
    }
} 