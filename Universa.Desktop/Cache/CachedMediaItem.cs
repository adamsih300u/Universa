using System;
using System.Collections.Generic;
using Universa.Desktop.Models;

namespace Universa.Desktop.Cache
{
    public class CachedMediaItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public MediaItemType Type { get; set; }
        public string Path { get; set; }
        public string ParentId { get; set; }
        public string Overview { get; set; }
        public string ImagePath { get; set; }
        public DateTime DateAdded { get; set; }
        public Dictionary<string, string> Metadata { get; set; }
        public string CollectionType { get; set; }
        public bool HasChildren { get; set; }
        public bool IsFolder { get; set; }
    }
} 