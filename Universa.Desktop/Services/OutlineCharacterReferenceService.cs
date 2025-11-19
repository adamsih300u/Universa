using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for handling character references in outline development
    /// Provides character profile loading and integration for outline writing chains
    /// </summary>
    public class OutlineCharacterReferenceService
    {
        private readonly FileReferenceService _fileReferenceService;
        private readonly List<string> _characterProfiles;
        private readonly Dictionary<string, string> _characterMetadata;

        public OutlineCharacterReferenceService(FileReferenceService fileReferenceService)
        {
            _fileReferenceService = fileReferenceService ?? throw new ArgumentNullException(nameof(fileReferenceService));
            _characterProfiles = new List<string>();
            _characterMetadata = new Dictionary<string, string>();
        }

        /// <summary>
        /// Gets the loaded character profiles
        /// </summary>
        public IReadOnlyList<string> CharacterProfiles => _characterProfiles.AsReadOnly();

        /// <summary>
        /// Gets character metadata (name mappings)
        /// </summary>
        public IReadOnlyDictionary<string, string> CharacterMetadata => _characterMetadata;

        /// <summary>
        /// Clears all loaded character references
        /// </summary>
        public void ClearCharacterReferences()
        {
            _characterProfiles.Clear();
            _characterMetadata.Clear();
            Debug.WriteLine("OutlineCharacterReferenceService: All character references cleared");
        }

        /// <summary>
        /// Processes character references from frontmatter
        /// </summary>
        /// <param name="frontmatter">The frontmatter dictionary to process</param>
        /// <param name="currentFilePath">Current file path for relative reference resolution</param>
        public async Task ProcessCharacterReferences(Dictionary<string, string> frontmatter, string currentFilePath)
        {
            if (frontmatter == null)
            {
                Debug.WriteLine("OutlineCharacterReferenceService: No frontmatter to process");
                return;
            }

            // Clear existing character references
            ClearCharacterReferences();

            // Find all character reference keys
            var characterRefs = frontmatter
                .Where(kvp => kvp.Key.StartsWith("ref_character") && !string.IsNullOrEmpty(kvp.Value))
                .ToList();

            if (!characterRefs.Any())
            {
                Debug.WriteLine("OutlineCharacterReferenceService: No character references found in frontmatter");
                return;
            }

            Debug.WriteLine($"OutlineCharacterReferenceService: Found {characterRefs.Count} character references");

            foreach (var characterRef in characterRefs)
            {
                await ProcessSingleCharacterReference(characterRef.Value, characterRef.Key, currentFilePath);
            }

            Debug.WriteLine($"OutlineCharacterReferenceService: Successfully loaded {_characterProfiles.Count} character profiles");
        }

        /// <summary>
        /// Processes a single character reference
        /// </summary>
        private async Task ProcessSingleCharacterReference(string refPath, string refKey, string currentFilePath)
        {
            try
            {
                Debug.WriteLine($"OutlineCharacterReferenceService: Loading character reference from '{refPath}' (key: '{refKey}')");

                string characterContent = await _fileReferenceService.GetFileContent(refPath, currentFilePath);
                if (!string.IsNullOrEmpty(characterContent))
                {
                    // Strip frontmatter to avoid including metadata in the character profile
                    string cleanedContent = StripFrontmatter(characterContent);
                    _characterProfiles.Add(cleanedContent);

                    // Extract character name from key (e.g., "ref_character_derek" -> "Derek")
                    string characterName = ExtractCharacterNameFromKey(refKey);
                    _characterMetadata[refKey] = characterName;

                    Debug.WriteLine($"OutlineCharacterReferenceService: Successfully loaded character '{characterName}': {cleanedContent.Length} characters (frontmatter stripped)");
                }
                else
                {
                    Debug.WriteLine($"OutlineCharacterReferenceService: Character reference file was empty or could not be loaded: '{refPath}'");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OutlineCharacterReferenceService: Error loading character reference '{refKey}': {ex.Message}");
            }
        }

        /// <summary>
        /// Strips frontmatter from content to include only the main content in character profiles
        /// </summary>
        private string StripFrontmatter(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // Check for frontmatter (starts with ---)
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing ---
                int secondDelimiterPos = content.IndexOf("\n---", 3);
                if (secondDelimiterPos == -1)
                {
                    secondDelimiterPos = content.IndexOf("\r\n---", 3);
                }
                
                if (secondDelimiterPos != -1)
                {
                    // Skip past the closing --- and any following newlines
                    int contentStart = secondDelimiterPos + 4; // Skip past "\n---"
                    if (contentStart < content.Length && content[contentStart] == '\n')
                        contentStart++;
                    else if (contentStart < content.Length - 1 && content.Substring(contentStart, 2) == "\r\n")
                        contentStart += 2;
                    
                    if (contentStart < content.Length)
                    {
                        return content.Substring(contentStart).Trim();
                    }
                }
            }

            // No frontmatter found, return original content
            return content;
        }

        /// <summary>
        /// Extracts character name from reference key
        /// </summary>
        private string ExtractCharacterNameFromKey(string refKey)
        {
            if (refKey.StartsWith("ref_character_") && refKey.Length > "ref_character_".Length)
            {
                string characterName = refKey.Substring("ref_character_".Length);
                // Capitalize first letter and handle underscores
                if (!string.IsNullOrEmpty(characterName))
                {
                    characterName = characterName.Replace("_", " ");
                    return char.ToUpper(characterName[0]) + characterName.Substring(1);
                }
            }
            return "Unknown Character";
        }

        /// <summary>
        /// Builds the character profiles section for system prompts
        /// </summary>
        public string BuildCharacterProfilesSection()
        {
            if (!_characterProfiles.Any())
            {
                return string.Empty;
            }

            var section = new System.Text.StringBuilder();
            section.AppendLine("\n=== CHARACTER PROFILES FOR OUTLINE DEVELOPMENT ===");
            section.AppendLine("These character profiles provide essential information for developing consistent outlines:");
            section.AppendLine("Use these to ensure character arcs, motivations, relationships, and dialogue patterns are properly planned in the outline.");
            section.AppendLine("");

            for (int i = 0; i < _characterProfiles.Count; i++)
            {
                var characterKey = _characterMetadata.FirstOrDefault(kvp => kvp.Value != "Unknown Character").Key;
                var characterName = characterKey != null ? _characterMetadata[characterKey] : $"Character {i + 1}";
                
                section.AppendLine($"--- {characterName} ---");
                section.AppendLine(_characterProfiles[i]);
                section.AppendLine("");
            }

            section.AppendLine("OUTLINE CHARACTER INTEGRATION GUIDELINES:");
            section.AppendLine("- Reference character motivations when planning chapter conflicts and resolutions");
            section.AppendLine("- Ensure character dialogue patterns and voice are considered in scene descriptions");
            section.AppendLine("- Plan character development arcs that align with established personality traits");
            section.AppendLine("- Include character relationship dynamics in scene planning");
            section.AppendLine("- Consider character backgrounds when designing plot events and reactions");
            section.AppendLine("- Maintain consistency with established character abilities and limitations");

            return section.ToString();
        }

        /// <summary>
        /// Gets a summary of loaded character information
        /// </summary>
        public string GetCharacterSummary()
        {
            if (!_characterProfiles.Any())
            {
                return "No character profiles loaded";
            }

            var characterNames = _characterMetadata.Values.Where(name => name != "Unknown Character").ToList();
            if (characterNames.Any())
            {
                return $"{_characterProfiles.Count} character profiles loaded: {string.Join(", ", characterNames)}";
            }
            else
            {
                return $"{_characterProfiles.Count} character profiles loaded";
            }
        }
    }
} 