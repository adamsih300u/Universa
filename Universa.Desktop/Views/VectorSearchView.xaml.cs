using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Universa.Desktop.Commands;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;
using Universa.Desktop.Services.VectorStore;

namespace Universa.Desktop.Views
{
    /// <summary>
    /// Interaction logic for VectorSearchView.xaml
    /// </summary>
    public partial class VectorSearchView : UserControl
    {
        private readonly ContentVectorizationService _contentVectorizationService;
        private readonly VectorizeLibraryCommand _vectorizeLibraryCommand;
        private readonly ConfigurationProvider _config;
        private List<ContentSearchResult> _searchResults;
        private bool _isSearching;

        /// <summary>
        /// Creates a new instance of the VectorSearchView
        /// </summary>
        public VectorSearchView()
        {
            InitializeComponent();

            // Get services from service locator
            _contentVectorizationService = ServiceLocator.Instance.GetService<ContentVectorizationService>();
            _vectorizeLibraryCommand = ServiceLocator.Instance.GetService<VectorizeLibraryCommand>();
            
            var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            _config = configService?.Provider;
            
            // Initialize UI
            UpdateUI();
        }

        /// <summary>
        /// Updates the UI based on the current state
        /// </summary>
        private void UpdateUI()
        {
            // Enable/disable vectorize button based on command availability
            VectorizeButton.IsEnabled = _vectorizeLibraryCommand?.CanExecute(null) ?? false;
            
            // Enable/disable search button based on search text and state
            SearchButton.IsEnabled = !string.IsNullOrWhiteSpace(SearchBox.Text) && !_isSearching;
            
            // Update auto-vectorize status
            bool localEmbeddingsEnabled = _config?.EnableLocalEmbeddings ?? false;
            AutoVectorizeStatusTextBlock.Text = $"Auto-vectorize: {(localEmbeddingsEnabled ? "On" : "Off")}";
            
            // Update status text
            StatusTextBlock.Text = "Ready";
            
            // Show/hide no results message
            NoResultsTextBlock.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Handles the Search button click event
        /// </summary>
        private async void SearchButton_Click(object sender, RoutedEventArgs e)
        {
            await PerformSearchAsync();
        }

        /// <summary>
        /// Handles the key down event in the search box
        /// </summary>
        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await PerformSearchAsync();
            }
        }

        /// <summary>
        /// Performs a search using the current search text
        /// </summary>
        private async Task PerformSearchAsync()
        {
            string searchText = SearchBox.Text?.Trim();
            
            if (string.IsNullOrEmpty(searchText) || _isSearching)
            {
                return;
            }
            
            try
            {
                _isSearching = true;
                SearchButton.IsEnabled = false;
                SearchButton.Content = "Searching...";
                StatusTextBlock.Text = "Searching...";
                
                // Clear previous results
                SearchResultsListView.ItemsSource = null;
                NoResultsTextBlock.Visibility = Visibility.Collapsed;
                
                // Perform search
                if (_contentVectorizationService != null)
                {
                    _searchResults = await _contentVectorizationService.SearchAsync(searchText, 10);
                    
                    // Display results
                    if (_searchResults != null && _searchResults.Count > 0)
                    {
                        // Convert to display format
                        var displayResults = _searchResults.Select(r => new SearchResultDisplay
                        {
                            FilePath = r.FilePath,
                            FileName = r.FileName,
                            Content = r.Content,
                            ChunkIndex = r.ChunkIndex,
                            ChunkCount = r.ChunkCount,
                            Score = r.Score,
                            DisplayPath = $"{r.FileName} (Chunk {r.ChunkIndex + 1}/{r.ChunkCount})",
                            DisplayContent = TruncateContent(r.Content, 200)
                        }).ToList();
                        
                        SearchResultsListView.ItemsSource = displayResults;
                        StatusTextBlock.Text = $"Found {_searchResults.Count} results";
                        Debug.WriteLine($"Found {_searchResults.Count} results for query: {searchText}");
                    }
                    else
                    {
                        NoResultsTextBlock.Visibility = Visibility.Visible;
                        StatusTextBlock.Text = "No results found";
                        Debug.WriteLine($"No results found for query: {searchText}");
                    }
                }
                else
                {
                    MessageBox.Show(
                        "Vector search service is not available. Please ensure local embeddings are enabled in settings.",
                        "Search Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusTextBlock.Text = "Error: Vector search service not available";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error performing search: {ex.Message}");
                MessageBox.Show(
                    $"Error performing search: {ex.Message}",
                    "Search Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusTextBlock.Text = "Error performing search";
            }
            finally
            {
                _isSearching = false;
                SearchButton.Content = "Search";
                SearchButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Truncates content to a specified length with ellipsis
        /// </summary>
        private string TruncateContent(string content, int maxLength)
        {
            if (string.IsNullOrEmpty(content))
            {
                return string.Empty;
            }
            
            if (content.Length <= maxLength)
            {
                return content;
            }
            
            return content.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Handles the selection changed event in the search results list view
        /// </summary>
        private void SearchResultsListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchResultsListView.SelectedItem is SearchResultDisplay selectedResult)
            {
                // Update status with selected result info
                StatusTextBlock.Text = $"Selected: {selectedResult.FileName} (Chunk {selectedResult.ChunkIndex + 1}/{selectedResult.ChunkCount})";
                Debug.WriteLine($"Selected result: {selectedResult.FileName}, Chunk: {selectedResult.ChunkIndex + 1}/{selectedResult.ChunkCount}");
            }
        }

        /// <summary>
        /// Handles the Vectorize button click event
        /// </summary>
        private void VectorizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_vectorizeLibraryCommand?.CanExecute(null) == true)
            {
                StatusTextBlock.Text = "Vectorizing library...";
                VectorizeButton.IsEnabled = false;
                
                try
                {
                    _vectorizeLibraryCommand.Execute(null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error executing vectorize command: {ex.Message}");
                    MessageBox.Show(
                        $"Error vectorizing library: {ex.Message}",
                        "Vectorization Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusTextBlock.Text = "Error vectorizing library";
                }
                finally
                {
                    VectorizeButton.IsEnabled = true;
                }
            }
            else
            {
                MessageBox.Show(
                    "Unable to vectorize library. Please ensure local embeddings are enabled in settings and a library path is configured.",
                    "Vectorization Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusTextBlock.Text = "Error: Unable to vectorize library";
            }
        }
    }

    /// <summary>
    /// Display model for search results
    /// </summary>
    public class SearchResultDisplay
    {
        public string FilePath { get; set; }
        public string FileName { get; set; }
        public string Content { get; set; }
        public int ChunkIndex { get; set; }
        public int ChunkCount { get; set; }
        public float Score { get; set; }
        public string DisplayPath { get; set; }
        public string DisplayContent { get; set; }
    }
} 