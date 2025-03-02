using System;
using System.Text.Json.Serialization;

namespace Universa.Desktop.Models
{
    public class FileMetadata
    {
        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; }

        [JsonPropertyName("modTime")]
        public DateTime ModTime { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("isDirectory")]
        public bool IsDirectory { get; set; }
    }
} 