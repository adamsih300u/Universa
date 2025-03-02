using System;

namespace Universa.Desktop.Models
{
    public class FileReference
    {
        public string Type { get; set; }  // style, rules, outline
        public string Path { get; set; }  // relative path to the file
        public string Content { get; set; }  // loaded content from the file

        public FileReference(string type, string path)
        {
            Type = type?.ToLowerInvariant();
            Path = path;
        }

        public static FileReference Parse(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            // Handle various reference formats
            if (line.StartsWith("#ref ") || line.StartsWith("ref "))
            {
                string refPart = line.StartsWith("#ref ") ? line.Substring(5) : line.Substring(4);
                var parts = refPart.Split(new[] { ':' }, 2);
                if (parts.Length != 2)
                    return null;

                return new FileReference(
                    parts[0].Trim(),
                    parts[1].Trim()
                );
            }
            else if (line.StartsWith("#data ") || line.StartsWith("data "))
            {
                string path = line.StartsWith("#data ") ? line.Substring(6).Trim() : line.Substring(5).Trim();
                return new FileReference("data", path);
            }
            else if (line.StartsWith("#ref data:") || line.StartsWith("ref data:"))
            {
                string prefix = line.StartsWith("#ref data:") ? "#ref data:" : "ref data:";
                var path = line.Substring(prefix.Length).Trim();
                return new FileReference("data", path);
            }

            return null;
        }
    }
} 