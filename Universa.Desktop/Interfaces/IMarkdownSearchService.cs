using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace Universa.Desktop.Interfaces
{
    public interface IMarkdownSearchService
    {
        /// <summary>
        /// Event fired when search results are updated
        /// </summary>
        event EventHandler<SearchResultsEventArgs> SearchResultsUpdated;
        
        /// <summary>
        /// Event fired when the current search result changes
        /// </summary>
        event EventHandler<CurrentSearchResultEventArgs> CurrentSearchResultChanged;
        
        /// <summary>
        /// Performs a search on the given text
        /// </summary>
        Task<SearchResults> SearchAsync(string text, string searchTerm, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Navigates to the next search result
        /// </summary>
        void FindNext();
        
        /// <summary>
        /// Navigates to the previous search result
        /// </summary>
        void FindPrevious();
        
        /// <summary>
        /// Gets the current search results
        /// </summary>
        SearchResults GetCurrentResults();
        
        /// <summary>
        /// Clears the current search results
        /// </summary>
        void ClearResults();
        
        /// <summary>
        /// Gets the current search index
        /// </summary>
        int CurrentIndex { get; }
        
        /// <summary>
        /// Gets whether a search is currently in progress
        /// </summary>
        bool IsSearching { get; }
        
        /// <summary>
        /// Gets the last search term used
        /// </summary>
        string LastSearchTerm { get; }
    }
    
    public class SearchResults
    {
        public List<int> Positions { get; set; } = new List<int>();
        public string SearchTerm { get; set; } = string.Empty;
        public int TotalCount { get; set; }
        public bool IsLimited { get; set; }
        public int MaxResults { get; set; }
    }
    
    public class SearchResultsEventArgs : EventArgs
    {
        public SearchResults Results { get; set; }
        public bool HasResults => Results?.Positions?.Count > 0;
    }
    
    public class CurrentSearchResultEventArgs : EventArgs
    {
        public int CurrentIndex { get; set; }
        public int Position { get; set; }
        public string SearchTerm { get; set; }
        public int TotalResults { get; set; }
    }
} 