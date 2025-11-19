using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using System.Linq;

namespace Universa.Desktop.Services
{
    public class FileReferenceService
    {
        private readonly string _libraryPath;
        private string _currentFilePath;

        public FileReferenceService(string libraryPath)
        {
            _libraryPath = libraryPath ?? throw new ArgumentNullException(nameof(libraryPath));
        }

        public void SetCurrentFile(string filePath)
        {
            _currentFilePath = filePath;
        }

        public void SetCurrentFilePath(string filePath)
        {
            _currentFilePath = filePath;
        }

        private string GetFullPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return null;

            if (Path.IsPathRooted(relativePath))
            {
                return relativePath;
            }

            // Try relative to current file first
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var currentDir = Path.GetDirectoryName(_currentFilePath);
                var fullPath = Path.GetFullPath(Path.Combine(currentDir, relativePath));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            // Try relative to library root
            return Path.GetFullPath(Path.Combine(_libraryPath, relativePath));
        }

        /// <summary>
        /// Gets the content of a referenced file
        /// </summary>
        /// <param name="refPath">The reference path</param>
        /// <param name="currentFilePath">The current file path (optional)</param>
        /// <returns>The content of the referenced file</returns>
        public async Task<string> GetFileContent(string refPath, string currentFilePath = null)
        {
            if (string.IsNullOrEmpty(refPath))
                return null;
                
            try
            {
                string fullPath;
                if (Path.IsPathRooted(refPath))
                {
                    // If it's an absolute path, make sure it's within the library
                    var normalizedPath = Path.GetFullPath(refPath);
                    var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                    if (!normalizedPath.StartsWith(normalizedLibraryPath))
                    {
                        throw new InvalidOperationException("Referenced file must be within the library");
                    }
                    fullPath = normalizedPath;
                }
                else
                {
                    // For relative paths, try multiple resolution strategies
                    string currentDir = !string.IsNullOrEmpty(currentFilePath) 
                        ? Path.GetDirectoryName(currentFilePath) 
                        : (!string.IsNullOrEmpty(_currentFilePath) 
                            ? Path.GetDirectoryName(_currentFilePath) 
                            : _libraryPath);

                    // Try relative to current file first
                    fullPath = Path.GetFullPath(Path.Combine(currentDir, refPath));
                    
                    // If that doesn't exist, try relative to library root
                    if (!File.Exists(fullPath))
                    {
                        fullPath = Path.GetFullPath(Path.Combine(_libraryPath, refPath));
                    }
                    
                    // Verify the resolved path is still within the library
                    var normalizedLibraryPath = Path.GetFullPath(_libraryPath);
                    if (!fullPath.StartsWith(normalizedLibraryPath))
                    {
                        throw new InvalidOperationException("Referenced file must be within the library");
                    }
                }

                if (File.Exists(fullPath))
                {
                    string content = await File.ReadAllTextAsync(fullPath);
                    System.Diagnostics.Debug.WriteLine($"Successfully loaded reference file: {fullPath}");
                    return content;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Referenced file not found: {fullPath}");
                    System.Diagnostics.Debug.WriteLine($"Current file path: {currentFilePath ?? _currentFilePath}");
                    System.Diagnostics.Debug.WriteLine($"Library path: {_libraryPath}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading reference file {refPath}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        public async Task<List<FileReference>> LoadReferencesAsync(string content)
        {
            var references = new List<FileReference>();
            
            if (string.IsNullOrEmpty(content))
                return references;

            // Look for frontmatter section
            if (!content.StartsWith("---"))
                return references;

            var frontmatterEnd = content.IndexOf("\n---", 3);
            if (frontmatterEnd == -1)
                return references;

            var frontmatter = content.Substring(3, frontmatterEnd - 3);
            var lines = frontmatter.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;

                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                var key = line.Substring(0, colonIndex).Trim().ToLower();
                var value = line.Substring(colonIndex + 1).Trim();

                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);

                FileReferenceType refType = FileReferenceType.Unknown;

                // Determine reference type based on key (support both underscore and space variants)
                if (key == "ref_style" || key == "ref style" || key == "style")
                {
                    refType = FileReferenceType.Style;
                }
                else if (key == "ref_rules" || key == "ref rules" || key == "rules")
                {
                    refType = FileReferenceType.Rules;
                }
                else if (key == "ref_outline" || key == "ref outline" || key == "outline")
                {
                    refType = FileReferenceType.Outline;
                }
                // BULLY NEW: Support for character references
                else if (key.StartsWith("ref_character"))
                {
                    refType = FileReferenceType.Character;
                }
                // BULLY NEW: Support for relationship references  
                else if (key.StartsWith("ref_relationship"))
                {
                    refType = FileReferenceType.Relationship;
                }
                // BULLY NEW: Support for story references
                else if (key.StartsWith("ref_story"))
                {
                    refType = FileReferenceType.Story;
                }
                else
                {
                    continue; // Skip unknown reference types
                }

                if (!string.IsNullOrEmpty(value))
                {
                    var reference = new FileReference
                    {
                        Type = refType,
                        Path = value,
                        Key = key // Store the original key for character name extraction
                    };

                    try
                    {
                        var fullPath = GetFullPath(value);
                        if (File.Exists(fullPath))
                        {
                            reference.Content = await File.ReadAllTextAsync(fullPath);
                            references.Add(reference);
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Reference file not found: {fullPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error loading reference {value}: {ex.Message}");
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// Loads references with cascade support - inherits references from outline files
        /// </summary>
        /// <param name="content">The file content to parse</param>
        /// <param name="enableCascade">Whether to enable reference cascading from outline files</param>
        /// <param name="enableRulesCharacterCascade">Whether to cascade character references from rules files (for character development)</param>
        /// <returns>List of all references including cascaded ones</returns>
        public async Task<List<FileReference>> LoadReferencesWithCascadeAsync(string content, bool enableCascade = true, bool enableRulesCharacterCascade = false)
        {
            var references = await LoadReferencesAsync(content);
            
            if (!enableCascade)
                return references;

            // Look for outline reference to cascade from
            var outlineRef = references.FirstOrDefault(r => r.Type == FileReferenceType.Outline);
            if (outlineRef != null)
            {
                System.Diagnostics.Debug.WriteLine($"Cascading references from outline: {outlineRef.Path}");
                
                // Parse the outline file's frontmatter to get its references
                var outlineFrontmatter = ParseFrontmatterFromContent(outlineRef.Content);
                var cascadedReferences = await LoadReferencesFromFrontmatterAsync(outlineFrontmatter, outlineRef.Path);
                
                // Add cascaded references, avoiding duplicates
                foreach (var cascadedRef in cascadedReferences)
                {
                    // Don't cascade rules references themselves - we want the rules file itself, not its references
                    // BUT allow character references that come from rules files to be cascaded
                    if (cascadedRef.Type == FileReferenceType.Rules)
                        continue;
                        
                    if (!references.Any(existing => 
                        existing.Type == cascadedRef.Type && 
                        existing.Path == cascadedRef.Path))
                    {
                        references.Add(cascadedRef);
                        System.Diagnostics.Debug.WriteLine($"Cascaded reference: {cascadedRef.Type} from {cascadedRef.Path}");
                    }
                }
            }

            // NEW: If enabled, cascade character references from rules files
            if (enableRulesCharacterCascade)
            {
                var rulesRef = references.FirstOrDefault(r => r.Type == FileReferenceType.Rules);
                if (rulesRef != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Cascading character references from rules: {rulesRef.Path}");
                    
                    // Parse the rules file's frontmatter to get its character references
                    var rulesFrontmatter = ParseFrontmatterFromContent(rulesRef.Content);
                    var rulesCharacterRefs = rulesFrontmatter
                        .Where(kvp => kvp.Key.StartsWith("ref_character") && !string.IsNullOrEmpty(kvp.Value))
                        .ToList();
                    
                    foreach (var characterRef in rulesCharacterRefs)
                    {
                        var reference = new FileReference
                        {
                            Type = FileReferenceType.Character,
                            Path = characterRef.Value,
                            Key = characterRef.Key
                        };

                        try
                        {
                            var fullPath = GetFullPath(characterRef.Value);
                            if (File.Exists(fullPath))
                            {
                                reference.Content = await File.ReadAllTextAsync(fullPath);
                                
                                // Only add if not already present
                                if (!references.Any(existing => 
                                    existing.Type == FileReferenceType.Character && 
                                    existing.Path == characterRef.Value))
                                {
                                    references.Add(reference);
                                    System.Diagnostics.Debug.WriteLine($"Cascaded character from rules: {characterRef.Key} from {characterRef.Value}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error loading cascaded character from rules {characterRef.Value}: {ex.Message}");
                        }
                    }
                }
            }

            return references;
        }

        /// <summary>
        /// Parses frontmatter from content and returns a dictionary
        /// </summary>
        private Dictionary<string, string> ParseFrontmatterFromContent(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            if (string.IsNullOrEmpty(content) || !content.StartsWith("---"))
                return frontmatter;

            var frontmatterEnd = content.IndexOf("\n---", 3);
            if (frontmatterEnd == -1)
                return frontmatter;

            var frontmatterSection = content.Substring(3, frontmatterEnd - 3);
            var lines = frontmatterSection.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.Trim().StartsWith("#") || string.IsNullOrWhiteSpace(line))
                    continue;

                var colonIndex = line.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                var key = line.Substring(0, colonIndex).Trim().ToLower();
                var value = line.Substring(colonIndex + 1).Trim();

                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);

                frontmatter[key] = value;
            }

            return frontmatter;
        }

        /// <summary>
        /// Loads references from a frontmatter dictionary
        /// </summary>
        private async Task<List<FileReference>> LoadReferencesFromFrontmatterAsync(Dictionary<string, string> frontmatter, string sourceFilePath)
        {
            var references = new List<FileReference>();

            foreach (var kvp in frontmatter)
            {
                var key = kvp.Key;
                var value = kvp.Value;

                if (string.IsNullOrEmpty(value))
                    continue;

                FileReferenceType refType = FileReferenceType.Unknown;

                // Determine reference type based on key (support both underscore and space variants)
                if (key == "ref_style" || key == "ref style" || key == "style")
                {
                    refType = FileReferenceType.Style;
                }
                else if (key == "ref_rules" || key == "ref rules" || key == "rules")
                {
                    refType = FileReferenceType.Rules;
                }
                else if (key == "ref_outline" || key == "ref outline" || key == "outline")
                {
                    refType = FileReferenceType.Outline;
                }
                else if (key.StartsWith("ref_character"))
                {
                    refType = FileReferenceType.Character;
                }
                else if (key.StartsWith("ref_relationship"))
                {
                    refType = FileReferenceType.Relationship;
                }
                else if (key.StartsWith("ref_story"))
                {
                    refType = FileReferenceType.Story;
                }
                else
                {
                    continue; // Skip unknown reference types
                }

                var reference = new FileReference
                {
                    Type = refType,
                    Path = value,
                    Key = key
                };

                try
                {
                    var fullPath = GetFullPath(value);
                    if (File.Exists(fullPath))
                    {
                        reference.Content = await File.ReadAllTextAsync(fullPath);
                        references.Add(reference);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Cascaded reference file not found: {fullPath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading cascaded reference {value}: {ex.Message}");
                }
            }

            return references;
        }
    }
} 