using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class MarkdownSearchService : IMarkdownSearchService
    {
        private const int MAX_SEARCH_RESULTS = 1000;
        private const int SEARCH_BATCH_SIZE = 100;
        
        private SearchResults _currentResults = new SearchResults();
        private int _currentIndex = -1;
        private bool _isSearching = false;
        private string _lastSearchTerm = string.Empty;

        public event EventHandler<SearchResultsEventArgs> SearchResultsUpdated;
        public event EventHandler<CurrentSearchResultEventArgs> CurrentSearchResultChanged;

        public int CurrentIndex => _currentIndex;
        public bool IsSearching => _isSearching;
        public string LastSearchTerm => _lastSearchTerm;

        public async Task<SearchResults> SearchAsync(string text, string searchTerm, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(searchTerm) || searchTerm.Length < 2)
            {
                var emptyResults = new SearchResults { SearchTerm = searchTerm };
                UpdateResults(emptyResults);
                return emptyResults;
            }

            // Skip search if it's the same as the last one
            if (searchTerm == _lastSearchTerm && _currentResults.Positions.Count > 0)
            {
                return _currentResults;
            }

            _isSearching = true;
            _lastSearchTerm = searchTerm;

            try
            {
                var positions = await Task.Run(() => PerformOptimizedSearch(text, searchTerm, cancellationToken), cancellationToken);
                
                var results = new SearchResults
                {
                    Positions = positions.Take(MAX_SEARCH_RESULTS).ToList(),
                    SearchTerm = searchTerm,
                    TotalCount = positions.Count,
                    IsLimited = positions.Count > MAX_SEARCH_RESULTS,
                    MaxResults = MAX_SEARCH_RESULTS
                };

                UpdateResults(results);
                
                if (results.Positions.Count > 0)
                {
                    _currentIndex = 0;
                    FireCurrentResultChanged();
                }

                Debug.WriteLine($"Found {results.TotalCount} search results for '{searchTerm}'");
                if (results.IsLimited)
                {
                    Debug.WriteLine($"Search limited to {MAX_SEARCH_RESULTS} results out of {results.TotalCount} total matches");
                }

                return results;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Search operation was cancelled");
                throw;
            }
            finally
            {
                _isSearching = false;
            }
        }

        public void FindNext()
        {
            if (_currentResults.Positions.Count == 0) return;

            _currentIndex++;
            if (_currentIndex >= _currentResults.Positions.Count)
                _currentIndex = 0;

            FireCurrentResultChanged();
        }

        public void FindPrevious()
        {
            if (_currentResults.Positions.Count == 0) return;

            _currentIndex--;
            if (_currentIndex < 0)
                _currentIndex = _currentResults.Positions.Count - 1;

            FireCurrentResultChanged();
        }

        public SearchResults GetCurrentResults()
        {
            return _currentResults;
        }

        public void ClearResults()
        {
            _currentResults = new SearchResults();
            _currentIndex = -1;
            _lastSearchTerm = string.Empty;
            
            SearchResultsUpdated?.Invoke(this, new SearchResultsEventArgs { Results = _currentResults });
        }

        private List<int> PerformOptimizedSearch(string text, string searchText, CancellationToken cancellationToken)
        {
            var results = new List<int>();
            
            try
            {
                // Use StringComparison.OrdinalIgnoreCase for better performance and consistency
                var comparison = StringComparison.OrdinalIgnoreCase;
                int index = 0;
                int searchLength = searchText.Length;
                int textLength = text.Length;
                int resultCount = 0;

                // First try exact search for best performance
                while (index < textLength && resultCount < MAX_SEARCH_RESULTS)
                {
                    // Check for cancellation periodically
                    if (resultCount % SEARCH_BATCH_SIZE == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    int foundIndex = text.IndexOf(searchText, index, comparison);
                    if (foundIndex == -1)
                        break;

                    results.Add(foundIndex);
                    resultCount++;
                    index = foundIndex + searchLength;
                }

                // If we found results with exact search, return them
                if (results.Count > 0)
                {
                    return results;
                }

                // If no exact matches and search contains newlines or multiple whitespace, try normalized search
                if (searchText.Contains('\n') || searchText.Contains('\r') || Regex.IsMatch(searchText, @"\s{2,}"))
                {
                    return PerformNormalizedSearch(text, searchText, cancellationToken);
                }

                return results;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Search operation was cancelled during execution");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in optimized search: {ex.Message}");
                return new List<int>();
            }
        }

        private List<int> PerformNormalizedSearch(string text, string searchText, CancellationToken cancellationToken)
        {
            var results = new List<int>();
            
            try
            {
                // Normalize both text and search term for multi-line matching
                string normalizedSearchText = NormalizeWhitespaceAndLineEndings(searchText);
                
                // Split text into reasonable chunks to avoid memory issues with very large documents
                const int chunkSize = 50000;
                const int overlap = 5000; // Overlap to catch matches that span chunk boundaries
                
                for (int chunkStart = 0; chunkStart < text.Length && results.Count < MAX_SEARCH_RESULTS; chunkStart += chunkSize - overlap)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    int chunkEnd = Math.Min(chunkStart + chunkSize, text.Length);
                    string chunk = text.Substring(chunkStart, chunkEnd - chunkStart);
                    string normalizedChunk = NormalizeWhitespaceAndLineEndings(chunk);
                    
                    int searchIndex = 0;
                    while (searchIndex < normalizedChunk.Length && results.Count < MAX_SEARCH_RESULTS)
                    {
                        int foundIndex = normalizedChunk.IndexOf(normalizedSearchText, searchIndex, StringComparison.OrdinalIgnoreCase);
                        if (foundIndex == -1)
                            break;
                            
                        // Map back to original text position
                        int originalPosition = MapNormalizedPositionToOriginal(chunk, normalizedChunk, foundIndex) + chunkStart;
                        
                        // Avoid duplicates from overlapping chunks
                        if (!results.Contains(originalPosition))
                        {
                            results.Add(originalPosition);
                        }
                        
                        searchIndex = foundIndex + normalizedSearchText.Length;
                    }
                }
                
                // Sort results by position
                results.Sort();
                
                Debug.WriteLine($"Normalized search found {results.Count} matches for multi-line text");
                return results;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Normalized search operation was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in normalized search: {ex.Message}");
                return new List<int>();
            }
        }
        
        private string NormalizeWhitespaceAndLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;
                
            // Normalize line endings to \n
            text = text.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Normalize multiple spaces to single space, but preserve single newlines
            text = Regex.Replace(text, @"[ \t]+", " ");
            
            // Normalize multiple newlines to single newlines
            text = Regex.Replace(text, @"\n+", "\n");
            
            return text.Trim();
        }
        
        private int MapNormalizedPositionToOriginal(string originalChunk, string normalizedChunk, int normalizedPosition)
        {
            // Simple mapping - for more complex cases, this could be enhanced
            // This is a basic implementation that works for most whitespace normalization cases
            
            int originalPos = 0;
            int normalizedPos = 0;
            
            while (normalizedPos < normalizedPosition && originalPos < originalChunk.Length)
            {
                char origChar = originalChunk[originalPos];
                
                if (normalizedPos < normalizedChunk.Length)
                {
                    char normChar = normalizedChunk[normalizedPos];
                    
                    if (origChar == normChar)
                    {
                        // Characters match exactly
                        originalPos++;
                        normalizedPos++;
                    }
                    else if (char.IsWhiteSpace(origChar))
                    {
                        // Skip whitespace in original that was normalized
                        originalPos++;
                        // Don't advance normalizedPos unless we hit the normalized whitespace
                        if (char.IsWhiteSpace(normChar))
                        {
                            normalizedPos++;
                        }
                    }
                    else
                    {
                        // This shouldn't happen with proper normalization, but advance both
                        originalPos++;
                        normalizedPos++;
                    }
                }
                else
                {
                    break;
                }
            }
            
            return originalPos;
        }

        private void UpdateResults(SearchResults results)
        {
            _currentResults = results;
            SearchResultsUpdated?.Invoke(this, new SearchResultsEventArgs { Results = results });
        }

        private void FireCurrentResultChanged()
        {
            if (_currentIndex >= 0 && _currentIndex < _currentResults.Positions.Count)
            {
                CurrentSearchResultChanged?.Invoke(this, new CurrentSearchResultEventArgs
                {
                    CurrentIndex = _currentIndex,
                    Position = _currentResults.Positions[_currentIndex],
                    SearchTerm = _currentResults.SearchTerm,
                    TotalResults = _currentResults.Positions.Count
                });
            }
        }
    }
} 