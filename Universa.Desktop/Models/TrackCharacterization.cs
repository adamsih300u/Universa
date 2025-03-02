using System;

namespace Universa.Desktop.Models
{
    public class TrackCharacterization
    {
        public string Id { get; set; }  // Subsonic track ID
        public string Title { get; set; }
        public string Artist { get; set; }
        public string ContentHash { get; set; }  // Hash of artist+title for backup matching
        public string Characteristics { get; set; }  // JSON array of characteristics
        public DateTime LastVerified { get; set; }
        public bool NeedsReview { get; set; }
        public float[] Embeddings { get; set; }  // Numerical vector representation for similarity matching
    }
} 