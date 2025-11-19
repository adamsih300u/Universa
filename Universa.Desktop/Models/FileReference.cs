using System;

namespace Universa.Desktop.Models
{
    public enum FileReferenceType
    {
        Unknown,
        Style,
        Rules,
        Outline,
        Character,
        Relationship,
        Data,
        Story
    }

    public class FileReference
    {
        public FileReferenceType Type { get; set; }  // Reference type enum
        public string Path { get; set; }  // relative path to the file
        public string Content { get; set; }  // loaded content from the file
        public string Key { get; set; }  // original frontmatter key (for character name extraction)

        public FileReference()
        {
            Type = FileReferenceType.Unknown;
        }

        public FileReference(string type, string path)
        {
            Type = ParseTypeFromString(type);
            Path = path;
        }

        private static FileReferenceType ParseTypeFromString(string type)
        {
            if (string.IsNullOrEmpty(type))
                return FileReferenceType.Unknown;

            var lowerType = type.ToLowerInvariant();
            return lowerType switch
            {
                "style" => FileReferenceType.Style,
                "rules" => FileReferenceType.Rules,
                "outline" => FileReferenceType.Outline,
                "character" => FileReferenceType.Character,
                "relationship" => FileReferenceType.Relationship,
                "data" => FileReferenceType.Data,
                _ => FileReferenceType.Unknown
            };
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

        /// <summary>
        /// Extracts character name from frontmatter key like "ref_character_derek" -> "derek"
        /// </summary>
        public string GetCharacterName()
        {
            if (Type != FileReferenceType.Character || string.IsNullOrEmpty(Key))
                return null;

            if (Key.StartsWith("ref_character_"))
                return Key.Substring("ref_character_".Length);

            return null;
        }

        /// <summary>
        /// Extracts relationship name from frontmatter key like "ref_relationship_derek_elena" -> "derek_elena"
        /// </summary>
        public string GetRelationshipName()
        {
            if (Type != FileReferenceType.Relationship || string.IsNullOrEmpty(Key))
                return null;

            if (Key.StartsWith("ref_relationship_"))
                return Key.Substring("ref_relationship_".Length);

            return null;
        }
    }
} 