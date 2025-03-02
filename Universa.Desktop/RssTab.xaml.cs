using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Data;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Text;
using Universa.Desktop.Models;
using Universa.Desktop.Properties;
using System.Windows.Documents;
using System.Net;

namespace Universa.Desktop
{
    public class BoolToFontWeightConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isRead)
            {
                return isRead ? FontWeights.Normal : FontWeights.Bold;
            }
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class RssTab : UserControl
    {
        private readonly HttpClient _httpClient;
        private readonly Configuration _config;
        private Dictionary<string, TreeViewItem> _feedItems = new Dictionary<string, TreeViewItem>();
        private Dictionary<string, Article> _currentArticles = new Dictionary<string, Article>();
        private string _authToken;
        private RssReadingMode _currentViewMode = RssReadingMode.List;
        private Dictionary<string, UIElement> _articleElements = new Dictionary<string, UIElement>();
        private Article _selectedArticle;

        public Article SelectedArticle
        {
            get => _selectedArticle;
            private set
            {
                _selectedArticle = value;
                // You might want to raise a property changed event here if needed
            }
        }

        public RssTab()
        {
            InitializeComponent();
            _config = Configuration.Instance;

            // Initialize HttpClient with default settings
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            // Restore the view mode from configuration
            _currentViewMode = _config.RssReadingMode;
            ViewModeButton.IsChecked = _currentViewMode != RssReadingMode.List;
            DefaultView.Visibility = _currentViewMode == RssReadingMode.List ? Visibility.Visible : Visibility.Collapsed;
            ReadingView.Visibility = _currentViewMode != RssReadingMode.List ? Visibility.Visible : Visibility.Collapsed;
            
            // Set up the client and load feeds
            InitializeAsync();
        }

        private async void InitializeAsync()
        {
            if (await ConfigureHttpClientAsync())
            {
                await LoadFeeds();
            }
        }

        private async Task<bool> ConfigureHttpClientAsync()
        {
            try
            {
                // Validate and format the base URL
                if (string.IsNullOrEmpty(_config.RssServerUrl))
                {
                    MessageBox.Show("RSS Server URL is not configured.", "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var baseUrl = _config.RssServerUrl.TrimEnd('/') + "/";
                System.Diagnostics.Debug.WriteLine($"Configuring HTTP client with base URL: {baseUrl}");
                _httpClient.BaseAddress = new Uri(baseUrl);

                // Clear any existing headers
                _httpClient.DefaultRequestHeaders.Clear();

                // Add standard headers
                _httpClient.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Universa-RSS-Client/1.0");

                // Get auth token using ClientLogin
                var loginEndpoint = "api/greader.php/accounts/ClientLogin";
                var loginContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("Email", _config.RssUsername),
                    new KeyValuePair<string, string>("Passwd", _config.RssPassword)
                });

                var loginResponse = await _httpClient.PostAsync(loginEndpoint, loginContent);
                var loginResponseContent = await loginResponse.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Login response: {loginResponse.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Login content: {loginResponseContent}");

                if (!loginResponse.IsSuccessStatusCode)
                {
                    MessageBox.Show($"Failed to authenticate with FreshRSS: {loginResponse.StatusCode}\n{loginResponseContent}", 
                        "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Parse the auth token from response
                // Response format should be like:
                // SID=xxx
                // Auth=yyy
                var lines = loginResponseContent.Split('\n');
                foreach (var line in lines)
                {
                    if (line.StartsWith("Auth="))
                    {
                        _authToken = line.Substring(5);
                        break;
                    }
                }

                if (string.IsNullOrEmpty(_authToken))
                {
                    MessageBox.Show("Failed to get authentication token from server response", 
                        "Authentication Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }

                // Set the auth token in headers for subsequent requests
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"GoogleLogin auth={_authToken}");

                System.Diagnostics.Debug.WriteLine("HTTP client configured with authentication token");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error configuring HTTP client: {ex.Message}");
                MessageBox.Show($"Error configuring RSS client: {ex.Message}\n\n" +
                              "Note: For FreshRSS, you need to use your API password, not your login password.\n" +
                              "You can generate an API password in FreshRSS settings under the Profile section.",
                              "Configuration Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        private async Task LoadFeeds()
        {
            try
            {
                // Use FreshRSS Google Reader API endpoint
                var endpoint = "api/greader.php/reader/api/0/subscription/list?output=json";
                
                System.Diagnostics.Debug.WriteLine($"\nTrying endpoint: {endpoint}");
                
                var response = await _httpClient.GetAsync(endpoint);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                System.Diagnostics.Debug.WriteLine($"Response: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Response headers:");
                foreach (var header in response.Headers)
                {
                    System.Diagnostics.Debug.WriteLine($"{header.Key}: {string.Join(", ", header.Value)}");
                }
                System.Diagnostics.Debug.WriteLine($"Content: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = "Could not connect to the RSS server.\n\n";
                    errorMessage += $"Status Code: {response.StatusCode}\n";
                    errorMessage += "Headers:\n";
                    foreach (var header in response.Headers)
                    {
                        errorMessage += $"{header.Key}: {string.Join(", ", header.Value)}\n";
                    }
                    errorMessage += $"Content: {responseContent}";
                    MessageBox.Show(errorMessage, "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Try to parse the response
                try
                {
                    var feeds = JsonSerializer.Deserialize<FeedList>(responseContent);
                    if (feeds?.Subscriptions != null)
                    {
                        PopulateFeedList(feeds);
                    }
                    else
                    {
                        MessageBox.Show("Server response format not recognized.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"JSON parsing error: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"Response content: {responseContent}");
                    MessageBox.Show(
                        "Could not parse server response. Please check the server's API format.\n\n" +
                        $"Error: {ex.Message}\n\n" +
                        $"Response: {responseContent}",
                        "Parse Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                var errorMessage = $"Error loading feeds: {ex.GetType().Name} - {ex.Message}";
                MessageBox.Show(errorMessage, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateFeedList(FeedList feeds)
        {
            FeedTreeView.Items.Clear();
            _feedItems.Clear();

            // Add "All" item at the top
            var allItem = new TreeViewItem
            {
                Header = "All Items",
                Tag = "user/-/state/com.google/reading-list",  // Special Google Reader API tag for all items
                Style = FeedTreeView.Resources["TreeViewItemStyle"] as Style
            };
            _feedItems["user/-/state/com.google/reading-list"] = allItem;
            FeedTreeView.Items.Add(allItem);

            // Group feeds by category
            var categorizedFeeds = feeds.Subscriptions
                .GroupBy(f => f.Categories?.FirstOrDefault()?.Label ?? "Uncategorized")
                .OrderBy(g => g.Key);

            foreach (var category in categorizedFeeds)
            {
                var categoryItem = new TreeViewItem 
                { 
                    Header = category.Key,
                    Style = FeedTreeView.Resources["TreeViewItemStyle"] as Style
                };
                
                foreach (var feed in category.OrderBy(f => f.Title))
                {
                    var item = new TreeViewItem
                    {
                        Header = feed.Title,
                        Tag = feed.Id,
                        Style = FeedTreeView.Resources["TreeViewItemStyle"] as Style
                    };

                    _feedItems[feed.Id] = item;
                    categoryItem.Items.Add(item);
                }

                FeedTreeView.Items.Add(categoryItem);
            }

            // Load unread counts
            UpdateUnreadCounts();
        }

        private async Task UpdateUnreadCounts()
        {
            try
            {
                // Reset all feed headers to their original titles first
                foreach (var item in _feedItems.Values)
                {
                    var header = item.Header.ToString();
                    if (header.Contains(" ("))
                    {
                        item.Header = header.Substring(0, header.IndexOf(" ("));
                    }
                }

                var endpoint = "api/greader.php/reader/api/0/unread-count?output=json";
                var response = await _httpClient.GetAsync(endpoint);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var unreadCounts = JsonSerializer.Deserialize<UnreadCounts>(content);

                    foreach (var count in unreadCounts.Counts)
                    {
                        if (_feedItems.TryGetValue(count.Id, out var item) && count.Count > 0)
                        {
                            var baseHeader = item.Header.ToString();
                            if (baseHeader.Contains(" ("))
                            {
                                baseHeader = baseHeader.Substring(0, baseHeader.IndexOf(" ("));
                            }
                            item.Header = $"{baseHeader} ({count.Count})";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error updating unread counts: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFeeds();
        }

        private async void MarkAllReadButton_Click(object sender, RoutedEventArgs e)
        {
            if (FeedTreeView.SelectedItem is TreeViewItem selectedItem)
            {
                var feedId = selectedItem.Tag as string;
                if (feedId != null)
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(
                            $"/reader/api/0/mark-all-as-read?s={Uri.EscapeDataString(feedId)}", 
                            null);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            await UpdateUnreadCounts();
                            LoadArticles(feedId);
                        }
                        else
                        {
                            MessageBox.Show("Failed to mark articles as read", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Error marking articles as read: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private async void FeedTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is TreeViewItem item)
            {
                var feedId = item.Tag as string;
                if (feedId != null)
                {
                    LoadArticles(feedId);
                }
            }
        }

        private async Task LoadArticles(string feedId)
        {
            try
            {
                // Clear existing articles and selection
                _currentArticles.Clear();
                ArticleListView.Items.Clear();
                ArticlesPanel.Children.Clear();
                SelectedArticle = null;

                // Configure HTTP client if not already configured
                if (_httpClient == null)
                {
                    await ConfigureHttpClientAsync();
                }

                // Ensure feedId is properly encoded for the URL
                var encodedFeedId = Uri.EscapeDataString(feedId);
                var response = await _httpClient.GetAsync($"api/greader.php/reader/api/0/stream/contents/{encodedFeedId}?output=json");
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"Article response: {json}");

                var articleList = JsonSerializer.Deserialize<ArticleList>(json);
                if (articleList?.Items == null)
                {
                    throw new Exception("No articles found in response");
                }

                // Store articles for both views
                foreach (var article in articleList.Items)
                {
                    _currentArticles[article.Id] = article;
                }

                // Load articles into the appropriate view
                if (_config.RssReadingMode != RssReadingMode.List)
                {
                    await LoadArticlesReadingView(articleList.Items);
                }
                else
                {
                    await LoadArticlesListView(articleList.Items);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading articles: {ex}");
                MessageBox.Show($"Failed to load articles: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadArticlesListView(List<Article> articles)
        {
            try
            {
                ArticleListView.Items.Clear();
                foreach (var article in articles)
                {
                    var viewModel = new ArticleViewModel
                    {
                        Title = article.Title,
                        Date = DateTimeOffset.FromUnixTimeMilliseconds(article.Published).LocalDateTime.ToString("g"),
                        Feed = article.Origin?.Title ?? "",
                        Article = article
                    };

                    var item = new ListViewItem { Content = viewModel };
                    item.Tag = article;
                    ArticleListView.Items.Add(item);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading articles into list view: {ex}");
                throw;
            }
        }

        private async Task LoadArticlesReadingView(List<Article> articles)
        {
            try
            {
                ArticlesPanel.Children.Clear();
                
                // Set the first article as selected by default
                if (articles.Any())
                {
                    SelectedArticle = articles[0];
                }
                else
                {
                    SelectedArticle = null;
                }

                foreach (var article in articles)
                {
                    var articleContainer = new Border
                    {
                        Margin = new Thickness(0, 0, 0, 40),
                        Padding = new Thickness(20),
                        Background = (Brush)FindResource("WindowBackgroundBrush"),
                        BorderBrush = (Brush)FindResource("BorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4)
                    };

                    var contentPanel = new StackPanel
                    {
                        Margin = new Thickness(0)
                    };

                    LoadArticleIntoReadingView(article, contentPanel);

                    articleContainer.Child = contentPanel;
                    articleContainer.Tag = article;

                    ArticlesPanel.Children.Add(articleContainer);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading articles into reading view: {ex}");
                throw;
            }
        }

        private async void ArticleListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArticleListView.SelectedItem is ListViewItem selectedItem && 
                selectedItem.Content is ArticleViewModel viewModel)
            {
                SelectedArticle = viewModel.Article;
                LoadArticleIntoWebBrowser(viewModel.Article);
                await MarkArticleAsRead(viewModel.Article);
            }
        }

        private async void LoadArticlesIntoReadingView(IEnumerable<Article> articles)
        {
            try
            {
                ArticlesPanel.Children.Clear();
                _articleElements.Clear();

                foreach (var article in articles)
                {
                    var articleContainer = new Border
                    {
                        Margin = new Thickness(0, 0, 0, 40),
                        Padding = new Thickness(20),
                        Background = (Brush)FindResource("WindowBackgroundBrush"),
                        BorderBrush = (Brush)FindResource("BorderBrush"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4)
                    };

                    var contentPanel = new StackPanel();
                    LoadArticleIntoReadingView(article, contentPanel);
                    articleContainer.Child = contentPanel;
                    articleContainer.Tag = article;

                    ArticlesPanel.Children.Add(articleContainer);
                    _articleElements[article.Id] = articleContainer;

                    await MarkArticleAsRead(article);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading articles into reading view: {ex}");
                throw;
            }
        }

        private void LoadArticleIntoWebBrowser(Article article)
        {
            if (article == null) return;

            var backgroundColor = ((SolidColorBrush)FindResource("WindowBackgroundBrush")).Color;
            var textColor = ((SolidColorBrush)FindResource("TextBrush")).Color;
            var bgHex = $"#{backgroundColor.R:X2}{backgroundColor.G:X2}{backgroundColor.B:X2}";
            var textHex = $"#{textColor.R:X2}{textColor.G:X2}{textColor.B:X2}";

            var content = article.Content?.Html ?? 
                         article.Content?.ContentText ?? 
                         article.Summary?.Html ?? 
                         article.Summary?.ContentText ?? 
                         "No content available";

            var html = $@"
                <!DOCTYPE html>
                <html>
                <head>
                    <meta http-equiv=""X-UA-Compatible"" content=""IE=Edge""/>
                    <meta charset=""utf-8""/>
                    <style>
                        html, body {{ 
                            overflow: visible !important;
                            height: auto !important;
                            margin: 0;
                            padding: 0;
                            font-family: Arial, sans-serif; 
                            line-height: 1.6;
                            background-color: {bgHex};
                            color: {textHex};
                            font-size: 14px;
                        }}
                        body {{
                            padding: 10px;
                        }}
                        h2 {{
                            font-size: 18px;
                            margin-top: 0;
                            margin-bottom: 10px;
                        }}
                        img {{ 
                            max-width: 100%; 
                            height: auto;
                        }}
                        * {{
                            overflow: visible !important;
                            max-width: 100%;
                        }}
                        pre, code {{
                            white-space: pre-wrap;
                            word-wrap: break-word;
                            background-color: {bgHex};
                            color: {textHex};
                            font-family: Consolas, monospace;
                            font-size: 13px;
                        }}
                        a {{
                            color: {textHex};
                            text-decoration: underline;
                        }}
                        .metadata {{
                            font-size: 12px;
                            opacity: 0.7;
                            margin-bottom: 20px;
                        }}
                    </style>
                </head>
                <body>
                    <h2 style=""color: {textHex}"">{WebUtility.HtmlEncode(article.Title)}</h2>
                    <p class=""metadata"" style=""color: {textHex}"">
                        {WebUtility.HtmlEncode(article.Origin?.Title)} - 
                        {DateTimeOffset.FromUnixTimeMilliseconds(article.Published).LocalDateTime:g}
                    </p>
                    <div style=""color: {textHex}"">{content}</div>
                </body>
                </html>";

            ContentBrowser.NavigateToString(html);
        }

        private void LoadArticleIntoReadingView(Article article, StackPanel contentPanel)
        {
            try
            {
                var title = new TextBlock
                {
                    Text = article.Title,
                    FontWeight = article.IsRead ? FontWeights.Normal : FontWeights.Bold,
                    FontSize = 20,
                    Margin = new Thickness(0, 0, 0, 10),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("TextBrush")
                };

                var metadata = new TextBlock
                {
                    Text = $"{article.Origin?.Title} - {DateTimeOffset.FromUnixTimeMilliseconds(article.Published).LocalDateTime:g}",
                    Foreground = (Brush)FindResource("TextBrush"),
                    Opacity = 0.7,
                    Margin = new Thickness(0, 0, 0, 20)
                };

                var content = new RichTextBox
                {
                    IsReadOnly = true,
                    BorderThickness = new Thickness(0),
                    Background = (Brush)FindResource("WindowBackgroundBrush"),
                    Foreground = (Brush)FindResource("TextBrush"),
                    Padding = new Thickness(0),
                    Focusable = false
                };

                // Get the content from either content or summary
                var articleContent = article.Content?.Html ?? 
                                   article.Content?.ContentText ?? 
                                   article.Summary?.Html ?? 
                                   article.Summary?.ContentText ?? 
                                   "No content available";

                // Convert HTML to FlowDocument
                var flowDocument = new FlowDocument();
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run(System.Net.WebUtility.HtmlDecode(articleContent.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n"))));
                flowDocument.Blocks.Add(paragraph);
                content.Document = flowDocument;

                contentPanel.Children.Add(title);
                contentPanel.Children.Add(metadata);
                contentPanel.Children.Add(content);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading article into reading view: {ex}");
                throw;
            }
        }

        private string SanitizeHtml(string html)
        {
            try
            {
                // Remove script tags and their contents
                html = System.Text.RegularExpressions.Regex.Replace(html, @"<script[^>]*>[\s\S]*?</script>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                // Remove inline script events
                html = System.Text.RegularExpressions.Regex.Replace(html, @"\son\w+=""[^""]*""", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
                // Remove iframe elements
                html = System.Text.RegularExpressions.Regex.Replace(html, @"<iframe[^>]*>[\s\S]*?</iframe>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Wrap the content in a basic HTML structure with some basic styling
                return $@"
                    <html>
                        <head>
                            <meta http-equiv=""X-UA-Compatible"" content=""IE=Edge""/>
                            <style>
                                html, body {{ 
                                    overflow: visible !important;
                                    height: auto !important;
                                    margin: 0;
                                    padding: 0;
                                    font-family: Arial, sans-serif; 
                                    line-height: 1.6;
                                    background-color: transparent;
                                }}
                                body {{
                                    padding: 10px;
                                }}
                                img {{ 
                                    max-width: 100%; 
                                    height: auto;
                                }}
                                * {{
                                    overflow: visible !important;
                                    max-width: 100%;
                                }}
                                pre, code {{
                                    white-space: pre-wrap;
                                    word-wrap: break-word;
                                }}
                            </style>
                        </head>
                        <body>
                            {html}
                        </body>
                    </html>";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sanitizing HTML: {ex}");
                return "Error displaying content";
            }
        }

        private async void ReadingView_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (!_config.RssAutoMarkAsRead) return;

            var scrollViewer = (ScrollViewer)sender;
            var verticalOffset = scrollViewer.VerticalOffset;
            var viewportHeight = scrollViewer.ViewportHeight;

            // Get all article containers in the articles panel
            var articleContainers = ArticlesPanel.Children.OfType<StackPanel>()
                .Where(sp => sp.Tag is Article);

            foreach (var container in articleContainers)
            {
                var article = container.Tag as Article;
                if (article == null || article.IsRead) continue;

                // Get the element's position relative to the ScrollViewer
                var elementTop = container.TransformToAncestor(scrollViewer).Transform(new Point(0, 0)).Y;
                var elementBottom = elementTop + container.ActualHeight;

                // If the element is fully visible or has been scrolled past
                if (elementBottom < verticalOffset + viewportHeight)
                {
                    try
                    {
                        var response = await _httpClient.PostAsync(
                            $"api/greader.php/reader/api/0/edit-tag?i={Uri.EscapeDataString(article.Id)}&a=user/-/state/com.google/read",
                            null);

                        if (response.IsSuccessStatusCode)
                        {
                            article.IsRead = true;
                            await UpdateUnreadCounts();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error marking article as read: {ex.Message}");
                    }
                }
            }
        }

        private void ViewModeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton button)
            {
                _currentViewMode = button.IsChecked == true ? RssReadingMode.Magazine : RssReadingMode.List;
                DefaultView.Visibility = _currentViewMode == RssReadingMode.List ? Visibility.Visible : Visibility.Collapsed;
                ReadingView.Visibility = _currentViewMode != RssReadingMode.List ? Visibility.Visible : Visibility.Collapsed;

                // Save the current view mode
                _config.RssReadingMode = _currentViewMode;
                _config.Save();

                // Reload articles in the new view
                if (_selectedArticle != null)
                {
                    var selectedId = _selectedArticle.Id;
                    LoadArticles(selectedId).ConfigureAwait(false);
                }
            }
        }

        private async Task MarkArticleAsRead(Article article)
        {
            if (article != null && !article.IsRead && _config.RssAutoMarkAsRead)
            {
                try
                {
                    var response = await _httpClient.PostAsync(
                        $"api/greader.php/reader/api/0/edit-tag?i={Uri.EscapeDataString(article.Id)}&a=user/-/state/com.google/read",
                        null);

                    if (response.IsSuccessStatusCode)
                    {
                        article.IsRead = true;
                        await UpdateUnreadCounts();
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error marking article as read: {ex.Message}");
                }
            }
        }

        public class ArticleViewModel
        {
            public string Title { get; set; }
            public string Date { get; set; }
            public string Feed { get; set; }
            public Article Article { get; set; }
        }

        public string GetContent()
        {
            if (SelectedArticle == null) return string.Empty;

            var content = new System.Text.StringBuilder();
            
            content.AppendLine("#metadata");
            content.AppendLine($"Title: {SelectedArticle.Title}");
            
            // Only include author if it's available
            if (!string.IsNullOrEmpty(SelectedArticle.Author))
            {
                content.AppendLine($"Author: {SelectedArticle.Author}");
            }
            
            content.AppendLine($"Published: {DateTimeOffset.FromUnixTimeMilliseconds(SelectedArticle.Published).LocalDateTime:yyyy-MM-dd HH:mm:ss}");
            content.AppendLine($"Source: {SelectedArticle.Origin?.Title ?? "Unknown"}");
            content.AppendLine($"URL: {SelectedArticle.Origin?.HtmlUrl ?? "Unknown"}");
            
            content.AppendLine("\n#content");
            content.AppendLine(SelectedArticle.Content?.Html ?? 
                             SelectedArticle.Content?.ContentText ?? 
                             SelectedArticle.Summary?.Html ?? 
                             SelectedArticle.Summary?.ContentText ?? 
                             "No content available");

            return content.ToString();
        }
    }

    // Model classes for JSON deserialization
    public class Category
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("label")]
        public string Label { get; set; }
    }

    public class Subscription
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("categories")]
        public List<Category> Categories { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonPropertyName("htmlUrl")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("iconUrl")]
        public string IconUrl { get; set; }
    }

    public class FeedList
    {
        [JsonPropertyName("subscriptions")]
        public List<Subscription> Subscriptions { get; set; }
    }

    public class UnreadCount
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }

    public class UnreadCounts
    {
        [JsonPropertyName("max")]
        public int Max { get; set; }

        [JsonPropertyName("unreadcounts")]
        public List<UnreadCount> Counts { get; set; }
    }

    public class ArticleList
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("items")]
        public List<Article> Items { get; set; }
    }

    public class Article
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("summary")]
        public Content Summary { get; set; }

        [JsonPropertyName("content")]
        public Content Content { get; set; }

        [JsonPropertyName("published")]
        public long Published { get; set; }

        [JsonPropertyName("categories")]
        public string[] Categories { get; set; }

        [JsonPropertyName("origin")]
        public FeedOrigin Origin { get; set; }

        // Helper property to check if article is read
        public bool IsRead
        {
            get => Categories?.Contains("user/-/state/com.google/read") == true;
            set
            {
                if (Categories == null)
                {
                    Categories = new string[0];
                }
                
                var categoriesList = Categories.ToList();
                if (value)
                {
                    if (!categoriesList.Contains("user/-/state/com.google/read"))
                    {
                        categoriesList.Add("user/-/state/com.google/read");
                    }
                }
                else
                {
                    categoriesList.Remove("user/-/state/com.google/read");
                }
                Categories = categoriesList.ToArray();
            }
        }
    }

    public class FeedOrigin
    {
        [JsonPropertyName("streamId")]
        public string StreamId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("htmlUrl")]
        public string HtmlUrl { get; set; }
    }

    public class Content
    {
        [JsonPropertyName("content")]
        public string ContentText { get; set; }

        [JsonPropertyName("html")]
        public string Html { get; set; }
    }
} 