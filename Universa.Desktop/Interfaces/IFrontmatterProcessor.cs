using System.Collections.Generic;
using System.Threading.Tasks;

namespace Universa.Desktop.Interfaces
{
    public interface IFrontmatterProcessor
    {
        /// <summary>
        /// Processes frontmatter when loading content, extracting it and returning content without frontmatter
        /// </summary>
        Task<string> ProcessFrontmatterForLoadingAsync(string content);
        
        /// <summary>
        /// Processes content for saving, adding frontmatter back to the content
        /// </summary>
        string ProcessFrontmatterForSaving(string content);
        
        /// <summary>
        /// Parses frontmatter content into key-value pairs
        /// </summary>
        void ParseFrontmatter(string frontmatterContent);
        
        /// <summary>
        /// Gets a frontmatter value by key
        /// </summary>
        string GetFrontmatterValue(string key);
        
        /// <summary>
        /// Sets a frontmatter value by key
        /// </summary>
        void SetFrontmatterValue(string key, string value);
        
        /// <summary>
        /// Checks if the document has frontmatter
        /// </summary>
        bool HasFrontmatter();
        
        /// <summary>
        /// Gets all frontmatter keys
        /// </summary>
        IEnumerable<string> GetFrontmatterKeys();
        
        /// <summary>
        /// Gets all frontmatter as a dictionary
        /// </summary>
        Dictionary<string, string> GetFrontmatter();
        
        /// <summary>
        /// Sets the entire frontmatter dictionary
        /// </summary>
        void SetFrontmatter(Dictionary<string, string> frontmatter);
        
        /// <summary>
        /// Clears all frontmatter
        /// </summary>
        void ClearFrontmatter();

        // New stateless methods that work with content directly
        /// <summary>
        /// Extracts frontmatter from content and returns it as a dictionary
        /// </summary>
        Dictionary<string, string> GetFrontmatterFromContent(string content);

        /// <summary>
        /// Checks if the content has frontmatter
        /// </summary>
        bool HasFrontmatterInContent(string content);

        /// <summary>
        /// Adds frontmatter to content, replacing any existing frontmatter
        /// </summary>
        string AddFrontmatterToContent(string content, Dictionary<string, string> frontmatter);
    }
} 