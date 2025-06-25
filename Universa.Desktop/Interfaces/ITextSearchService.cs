using System.Threading;
using System.Threading.Tasks;
using Universa.Desktop.Services;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Interface for text search and replacement services
    /// </summary>
    public interface ITextSearchService
    {
        /// <summary>
        /// Finds text in content using multiple strategies for maximum accuracy
        /// </summary>
        /// <param name="content">The content to search in</param>
        /// <param name="searchText">The text to search for</param>
        /// <param name="contextRadius">The radius of context to extract around matches</param>
        /// <returns>Search result with position, confidence, and match information</returns>
        EnhancedTextSearchService.SearchResult FindTextInContent(string content, string searchText, int contextRadius = 100);

        /// <summary>
        /// Finds text in content using multiple strategies for maximum accuracy (async version)
        /// </summary>
        /// <param name="content">The content to search in</param>
        /// <param name="searchText">The text to search for</param>
        /// <param name="contextRadius">The radius of context to extract around matches</param>
        /// <param name="cancellationToken">Cancellation token for the operation</param>
        /// <returns>Search result with position, confidence, and match information</returns>
        Task<EnhancedTextSearchService.SearchResult> FindTextInContentAsync(string content, string searchText, int contextRadius = 100, CancellationToken cancellationToken = default);

        /// <summary>
        /// Applies text changes with enhanced accuracy and validation
        /// </summary>
        /// <param name="content">The content to modify (passed by reference)</param>
        /// <param name="originalText">The original text to find and replace</param>
        /// <param name="changedText">The new text to replace with</param>
        /// <param name="errorMessage">Error message if the operation fails</param>
        /// <returns>True if the changes were applied successfully, false otherwise</returns>
        bool ApplyTextChanges(ref string content, string originalText, string changedText, out string errorMessage);
    }
} 