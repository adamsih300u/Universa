using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class FrontmatterProcessor : IFrontmatterProcessor
    {
        // Remove instance state to prevent cross-contamination between tabs
        // All methods now work with passed parameters instead of instance state

        public FrontmatterProcessor()
        {
            // No instance state needed
        }

        public async Task<string> ProcessFrontmatterForLoadingAsync(string content)
        {
            // Process frontmatter on background thread
            return await Task.Run(() =>
            {
                // Quick check for frontmatter delimiter
                if (!content.StartsWith("---\n") && !content.StartsWith("---\r\n"))
                {
                    return content;
                }

                // Find the closing delimiter
                int endIndex = -1;
                int startIndex = content.IndexOf('\n') + 1;
                
                if (startIndex >= content.Length)
                {
                    return content;
                }

                endIndex = content.IndexOf("\n---", startIndex);
                if (endIndex <= startIndex)
                {
                    return content;
                }

                // Skip past the closing delimiter
                int contentStartIndex = endIndex + 4; // Length of "\n---"
                if (contentStartIndex >= content.Length)
                {
                    return content;
                }

                // If there's a newline after the closing delimiter, skip it too
                if (content[contentStartIndex] == '\n')
                {
                    contentStartIndex++;
                }
                
                // Return content without frontmatter, ensuring no extra newlines
                return content.Substring(contentStartIndex).TrimStart();
            });
        }

        public string ProcessFrontmatterForSaving(string content)
        {
            // Extract current frontmatter from the content if it exists
            var frontmatter = ExtractFrontmatterFromContent(content);
            
            // If frontmatter is currently visible in the editor, we need to extract it
            // to avoid duplicating it
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                int endIndex = content.IndexOf("\n---", 4);
                if (endIndex > 0)
                {
                    // Skip past the closing delimiter
                    int contentStartIndex = endIndex + 4; // Length of "\n---"
                    if (contentStartIndex < content.Length)
                    {
                        // If there's a newline after the closing delimiter, skip it too
                        if (content[contentStartIndex] == '\n')
                            contentStartIndex++;
                        
                        // Get content without frontmatter and trim any leading whitespace
                        content = content.Substring(contentStartIndex).TrimStart();
                    }
                }
            }
            
            if (frontmatter == null || frontmatter.Count == 0)
            {
                // No frontmatter to add
                return content;
            }
            
            // Build frontmatter section
            StringBuilder frontmatterBuilder = new StringBuilder();
            frontmatterBuilder.AppendLine("---");
            
            foreach (var kvp in frontmatter)
            {
                frontmatterBuilder.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            
            frontmatterBuilder.AppendLine("---");
            
            // Ensure content doesn't start with extra newlines
            content = content.TrimStart();
            
            frontmatterBuilder.Append(content);
            
            return frontmatterBuilder.ToString();
        }

        private Dictionary<string, string> ExtractFrontmatterFromContent(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            // Quick check for frontmatter delimiter
            if (!content.StartsWith("---\n") && !content.StartsWith("---\r\n"))
            {
                return frontmatter;
            }

            // Find the closing delimiter
            int startIndex = content.IndexOf('\n') + 1;
            if (startIndex >= content.Length)
            {
                return frontmatter;
            }

            int endIndex = content.IndexOf("\n---", startIndex);
            if (endIndex <= startIndex)
            {
                return frontmatter;
            }

            // Extract and parse frontmatter
            string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
            ParseFrontmatter(frontmatterContent, frontmatter);
            
            return frontmatter;
        }

        public void ParseFrontmatter(string frontmatterContent)
        {
            // This method is kept for interface compatibility but doesn't store state
            // Use the overload that takes a dictionary parameter instead
            var tempFrontmatter = new Dictionary<string, string>();
            ParseFrontmatter(frontmatterContent, tempFrontmatter);
        }

        private void ParseFrontmatter(string frontmatterContent, Dictionary<string, string> frontmatter)
        {
            frontmatter.Clear();
            
            // Split by lines and process each line
            foreach (string line in frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // Handle tags (like #fiction)
                if (line.StartsWith("#"))
                {
                    frontmatter[line.Trim()] = "true";
                    continue;
                }

                // Look for key-value pairs (key: value)
                int colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = line.Substring(0, colonIndex).Trim();
                    string value = line.Substring(colonIndex + 1).Trim();
                    
                    // Only add non-empty keys
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        frontmatter[key] = value;
                    }
                }
            }
        }

        public string GetFrontmatterValue(string key)
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
            return null;
        }

        public void SetFrontmatterValue(string key, string value)
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
        }

        public bool HasFrontmatter()
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
            return false;
        }

        public IEnumerable<string> GetFrontmatterKeys()
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
            return Enumerable.Empty<string>();
        }

        public Dictionary<string, string> GetFrontmatter()
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
            return new Dictionary<string, string>();
        }

        public void SetFrontmatter(Dictionary<string, string> frontmatter)
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
        }

        public void ClearFrontmatter()
        {
            // This method can't work without instance state
            // Consider deprecating or requiring content parameter
        }

        // New methods that work with content directly
        public Dictionary<string, string> GetFrontmatterFromContent(string content)
        {
            return ExtractFrontmatterFromContent(content);
        }

        public bool HasFrontmatterInContent(string content)
        {
            return content.StartsWith("---\n") || content.StartsWith("---\r\n");
        }

        public string AddFrontmatterToContent(string content, Dictionary<string, string> frontmatter)
        {
            if (frontmatter == null || frontmatter.Count == 0)
            {
                return content;
            }
            
            // Remove existing frontmatter first using synchronous method
            string contentWithoutFrontmatter = RemoveFrontmatterFromContent(content);
            
            // Build frontmatter section
            StringBuilder frontmatterBuilder = new StringBuilder();
            frontmatterBuilder.AppendLine("---");
            
            foreach (var kvp in frontmatter)
            {
                frontmatterBuilder.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            
            frontmatterBuilder.AppendLine("---");
            
            // Ensure content doesn't start with extra newlines
            contentWithoutFrontmatter = contentWithoutFrontmatter.TrimStart();
            
            frontmatterBuilder.Append(contentWithoutFrontmatter);
            
            return frontmatterBuilder.ToString();
        }

        private string RemoveFrontmatterFromContent(string content)
        {
            // Quick check for frontmatter delimiter
            if (!content.StartsWith("---\n") && !content.StartsWith("---\r\n"))
            {
                return content;
            }

            // Find the closing delimiter
            int startIndex = content.IndexOf('\n') + 1;
            if (startIndex >= content.Length)
            {
                return content;
            }

            int endIndex = content.IndexOf("\n---", startIndex);
            if (endIndex <= startIndex)
            {
                return content;
            }

            // Skip past the closing delimiter
            int contentStartIndex = endIndex + 4; // Length of "\n---"
            if (contentStartIndex >= content.Length)
            {
                return content;
            }

            // If there's a newline after the closing delimiter, skip it too
            if (content[contentStartIndex] == '\n')
            {
                contentStartIndex++;
            }
            
            // Return content without frontmatter, ensuring no extra newlines
            return content.Substring(contentStartIndex).TrimStart();
        }
    }
} 