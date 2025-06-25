using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Services;
using Universa.Desktop.Interfaces;
using System.Threading.Tasks;

namespace Universa.Desktop.Controls
{
    public partial class FictionMessageControl : UserControl
    {
        private readonly ITextSearchService _textSearchService;
        public static readonly DependencyProperty ContentProperty =
            DependencyProperty.Register("Content", typeof(string), typeof(FictionMessageControl), new PropertyMetadata(string.Empty));

        public string Content
        {
            get { return (string)GetValue(ContentProperty); }
            set { SetValue(ContentProperty, value); }
        }

        public FictionMessageControl()
        {
            InitializeComponent();
            _textSearchService = new EnhancedTextSearchService();
        }

        private void OriginalTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string originalText)
                {
                    Debug.WriteLine($"Original text button clicked: {originalText.Substring(0, Math.Min(50, originalText.Length))}...");
                    
                    // Find the main window and get the current markdown tab
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    if (mainWindow == null)
                    {
                        Debug.WriteLine("Main window not found");
                        return;
                    }

                    // Get the currently selected tab
                    var selectedTab = mainWindow.MainTabControl?.SelectedItem as TabItem;
                    object markdownTab = null;

                    // Get the AvalonEdit markdown tab from the selected tab
                    if (selectedTab?.Content is Views.MarkdownTabAvalon avalonTab)
                    {
                        markdownTab = avalonTab;
                    }
                    else
                    {
                        // If not found, try to get it from the chat sidebar's associated editor
                        var chatSidebarViewModel = GetChatSidebarViewModel();
                        if (chatSidebarViewModel != null)
                        {
                            markdownTab = GetMarkdownTabFromChatSidebar(chatSidebarViewModel);
                        }
                    }

                    if (markdownTab == null)
                    {
                        Debug.WriteLine("No markdown tab found");
                        ShowNavigationMessage("No markdown editor found. Please open a markdown file first.");
                        return;
                    }

                    // Navigate to and highlight the original text
                    NavigateToText(markdownTab, originalText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OriginalTextButton_Click: {ex.Message}");
                ShowNavigationMessage("Error navigating to text. Please try again.");
            }
        }

        private void ApplyChangesButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is Converters.FictionTextBlock fictionBlock)
                {
                    Debug.WriteLine($"Apply changes button clicked for: {fictionBlock.OriginalText?.Substring(0, Math.Min(50, fictionBlock.OriginalText.Length))}...");
                    
                    var markdownTab = GetCurrentMarkdownTab();
                    if (markdownTab == null)
                    {
                        Debug.WriteLine("No markdown tab found");
                        ShowNavigationMessage("No markdown editor found. Please open a markdown file first.");
                        return;
                    }

                    // Apply the changes
                    ApplyTextChanges(markdownTab, fictionBlock.OriginalText, fictionBlock.ChangedText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyChangesButton_Click: {ex.Message}");
                ShowNavigationMessage("Error applying changes. Please try again.");
            }
        }

        private void AnchorTextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is string anchorText)
                {
                    Debug.WriteLine($"Anchor text button clicked: {anchorText.Substring(0, Math.Min(50, anchorText.Length))}...");
                    
                    var markdownTab = GetCurrentMarkdownTab();
                    if (markdownTab == null)
                    {
                        Debug.WriteLine("No markdown tab found");
                        ShowNavigationMessage("No markdown editor found. Please open a markdown file first.");
                        return;
                    }

                    // Navigate to and highlight the anchor text
                    NavigateToText(markdownTab, anchorText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in AnchorTextButton_Click: {ex.Message}");
                ShowNavigationMessage("Error navigating to text. Please try again.");
            }
        }

        private void ApplyInsertionButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is Converters.FictionTextBlock fictionBlock)
                {
                    Debug.WriteLine($"Apply insertion button clicked for anchor: {fictionBlock.AnchorText?.Substring(0, Math.Min(50, fictionBlock.AnchorText.Length))}...");
                    
                    var markdownTab = GetCurrentMarkdownTab();
                    if (markdownTab == null)
                    {
                        Debug.WriteLine("No markdown tab found");
                        ShowNavigationMessage("No markdown editor found. Please open a markdown file first.");
                        return;
                    }

                    // Apply the insertion
                    ApplyTextInsertion(markdownTab, fictionBlock.AnchorText, fictionBlock.NewText);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ApplyInsertionButton_Click: {ex.Message}");
                ShowNavigationMessage("Error applying insertion. Please try again.");
            }
        }

        private ChatSidebarViewModel GetChatSidebarViewModel()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow?.ChatSidebar?.DataContext is ChatSidebarViewModel viewModel)
                {
                    return viewModel;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting ChatSidebarViewModel: {ex.Message}");
                return null;
            }
        }

        private object GetMarkdownTabFromChatSidebar(ChatSidebarViewModel chatViewModel)
        {
            try
            {
                // Try to get the associated editor from the selected chat tab
                if (chatViewModel.SelectedTab?.AssociatedEditor is Views.MarkdownTabAvalon associatedAvalonTab)
                {
                    Debug.WriteLine($"Found associated MarkdownTabAvalon: {associatedAvalonTab.FilePath}");
                    return associatedAvalonTab;
                }

                // If no associated editor, try to find a markdown tab by filename matching
                if (chatViewModel.SelectedTab != null && chatViewModel.SelectedTab.Name.StartsWith("Chat - "))
                {
                    string chatFileName = chatViewModel.SelectedTab.Name.Substring(7); // Remove "Chat - " prefix
                    
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    if (mainWindow != null)
                    {
                        foreach (TabItem tabItem in mainWindow.MainTabControl.Items)
                        {
                            if (tabItem.Content is Views.MarkdownTabAvalon avalonTab)
                            {
                                string tabFileName = System.IO.Path.GetFileName(avalonTab.FilePath);
                                if (tabFileName == chatFileName || chatViewModel.SelectedTab.Name.Contains(tabFileName))
                                {
                                    Debug.WriteLine($"Found matching MarkdownTabAvalon by filename: {avalonTab.FilePath}");
                                    return avalonTab;
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting markdown tab from chat sidebar: {ex.Message}");
                return null;
            }
        }

        private async void NavigateToText(object markdownTab, string originalText)
        {
            try
            {
                // Extract common properties based on tab type
                string filePath;
                string editorContent;
                
                if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                {
                    filePath = avalonTab.FilePath;
                    editorContent = avalonTab.MarkdownDocument?.Text ?? string.Empty;
                    
                    Debug.WriteLine($"Navigating to text in MarkdownTabAvalon: {filePath}");
                    
                    if (avalonTab.MarkdownEditor == null)
                    {
                        Debug.WriteLine("MarkdownTabAvalon Editor is null");
                        ShowNavigationMessage("Editor not available.");
                        return;
                    }
                }
                else
                {
                    Debug.WriteLine("Unknown markdown tab type");
                    ShowNavigationMessage("Unsupported editor type.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(originalText))
                {
                    Debug.WriteLine("Original text is null or empty");
                    ShowNavigationMessage("No text to search for.");
                    return;
                }

                if (string.IsNullOrEmpty(editorContent))
                {
                    Debug.WriteLine("Editor content is empty");
                    ShowNavigationMessage("Editor content is empty.");
                    return;
                }

                // Validate text length before search
                if (originalText.Length > editorContent.Length)
                {
                    Debug.WriteLine($"Original text ({originalText.Length} chars) is longer than editor content ({editorContent.Length} chars)");
                    ShowNavigationMessage("Search text is longer than document content.");
                    return;
                }

                // Show searching message for long searches
                if (originalText.Length > 500 || editorContent.Length > 50000)
                {
                    ShowNavigationMessage("Searching for text in document...");
                }

                // Use enhanced text search service to find the text
                using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15)))
                {
                    var searchResult = await _textSearchService.FindTextInContentAsync(editorContent, originalText, 100, cts.Token);
                    
                    if (searchResult.Index >= 0)
                    {
                        Debug.WriteLine($"Found text at index: {searchResult.Index} using {searchResult.MatchType} match (confidence: {searchResult.Confidence:F2})");
                        
                        // Validate the search result
                        if (searchResult.Index + searchResult.Length > editorContent.Length)
                        {
                            Debug.WriteLine($"Search result extends beyond content bounds. Index: {searchResult.Index}, Length: {searchResult.Length}, Content length: {editorContent.Length}");
                            ShowNavigationMessage("Found text but position is invalid. Document may have changed.");
                            return;
                        }

                        // Switch to the markdown tab if not already viewing
                        if (!IsCurrentlyViewingMarkdownTab())
                        {
                            Debug.WriteLine("User not currently viewing a markdown tab, switching to target tab");
                            SwitchToMarkdownTab(markdownTab);
                        }
                        
                        // Update UI on the UI thread
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                                if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                                {
                                    // Handle AvalonEdit MarkdownTab
                                    if (avalonTab.MarkdownEditor == null) return;
                                    
                                    avalonTab.MarkdownEditor.CaretOffset = searchResult.Index;
                                    avalonTab.MarkdownEditor.Focus();
                                    
                                    var location = avalonTab.MarkdownEditor.Document.GetLocation(searchResult.Index);
                                    avalonTab.MarkdownEditor.ScrollToLine(location.Line);
                                    
                                    // Select the found text for visual feedback
                                    avalonTab.MarkdownEditor.Select(searchResult.Index, searchResult.Length);
                                    
                                    string matchInfo = searchResult.IsExactMatch ? "" : $" ({searchResult.MatchType} match, {searchResult.Confidence:F1}% confidence)";
                                    ShowNavigationMessageSync($"Navigated to text at line {location.Line}{matchInfo}");
                                }
                            }
                            catch (Exception uiEx)
                            {
                                Debug.WriteLine($"Error updating UI during navigation: {uiEx.Message}");
                                ShowNavigationMessageSync("Found text but failed to navigate to it.");
                            }
                        }, System.Windows.Threading.DispatcherPriority.Normal);
                    }
                    else
                    {
                        Debug.WriteLine($"Text not found in editor content. Search result: {searchResult.MatchType}");
                        
                        string message = searchResult.MatchType switch
                        {
                            "Empty input" => "Invalid search text provided.",
                            "Search cancelled" => "Search was cancelled due to timeout.",
                            "Search error" => "An error occurred during search.",
                            _ => "Text not found in the current document. The content may have been modified significantly."
                        };
                        
                        ShowNavigationMessage(message);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Text search was cancelled due to timeout");
                ShowNavigationMessage("Search timed out. The text segment may be too long or complex to find quickly.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to text: {ex.Message}");
                ShowNavigationMessage("Error navigating to text. Please try again.");
            }
        }

        private bool IsCurrentlyViewingMarkdownTab()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow?.MainTabControl != null)
                {
                    var selectedTab = mainWindow.MainTabControl.SelectedItem as TabItem;
                    return selectedTab?.Content is Views.MarkdownTabAvalon;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking current tab type: {ex.Message}");
                return false;
            }
        }

        private void SwitchToMarkdownTab(object markdownTab)
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow?.MainTabControl != null)
                {
                    // Find the tab item that contains this markdown tab
                    foreach (TabItem tabItem in mainWindow.MainTabControl.Items)
                    {
                        if (tabItem.Content == markdownTab)
                        {
                            mainWindow.MainTabControl.SelectedItem = tabItem;
                            
                            string filePath = markdownTab is Views.MarkdownTabAvalon avalonTab ? avalonTab.FilePath : "Unknown";
                            Debug.WriteLine($"Switched to markdown tab: {filePath}");
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error switching to markdown tab: {ex.Message}");
            }
        }

        private void HighlightText(object markdownTab, int startIndex, int length)
        {
            try
            {
                if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                {
                    // For AvalonEdit, the text is already selected in NavigateToText, so no additional highlighting needed
                    Debug.WriteLine($"Text selection applied in MarkdownTabAvalon at index {startIndex}, length {length}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error highlighting text: {ex.Message}");
            }
        }

        private object GetCurrentMarkdownTab()
        {
            try
            {
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow == null)
                {
                    Debug.WriteLine("Main window not found");
                    return null;
                }

                // Get the currently selected tab
                var selectedTab = mainWindow.MainTabControl?.SelectedItem as TabItem;

                // First, try to get the markdown tab from the selected tab
                if (selectedTab?.Content is Views.MarkdownTabAvalon avalonTab)
                {
                    return avalonTab;
                }

                // If not found, try to get it from the chat sidebar's associated editor
                var chatSidebarViewModel = GetChatSidebarViewModel();
                if (chatSidebarViewModel != null)
                {
                    return GetMarkdownTabFromChatSidebar(chatSidebarViewModel);
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current markdown tab: {ex.Message}");
                return null;
            }
        }

        private async void ApplyTextChanges(object markdownTab, string originalText, string changedText)
        {
            try
            {
                // Extract properties based on tab type
                string filePath;
                string editorContent;
                
                if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                {
                    filePath = avalonTab.FilePath;
                    editorContent = avalonTab.MarkdownDocument?.Text ?? string.Empty;
                    Debug.WriteLine($"Applying text changes in MarkdownTabAvalon: {filePath}");
                    
                    if (avalonTab.MarkdownEditor == null)
                    {
                        Debug.WriteLine("MarkdownTabAvalon Editor is null");
                        ShowNavigationMessage("Editor not available.");
                        return;
                    }
                }
                else
                {
                    Debug.WriteLine("Unknown markdown tab type");
                    ShowNavigationMessage("Unsupported editor type.");
                    return;
                }
                
                if (string.IsNullOrEmpty(editorContent))
                {
                    Debug.WriteLine("Editor content is empty");
                    ShowNavigationMessage("Editor content is empty.");
                    return;
                }

                // Show progress message for large operations
                if (editorContent.Length > 10000 || originalText.Length > 1000)
                {
                    ShowNavigationMessage("Applying changes...");
                }

                // Perform text processing on background thread
                string contentCopy = null;
                string errorMessage = null;
                bool success = false;

                try
                {
                    await Task.Run(() =>
                    {
                        contentCopy = editorContent;
                        success = _textSearchService.ApplyTextChanges(ref contentCopy, originalText, changedText, out errorMessage);
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in background text processing: {ex.Message}");
                    ShowNavigationMessage("Error processing text changes. Please try again.");
                    return;
                }
                
                if (success)
                {
                    // Switch to the markdown tab if not already viewing
                    if (!IsCurrentlyViewingMarkdownTab())
                    {
                        Debug.WriteLine("User not currently viewing a markdown tab, switching to target tab");
                        SwitchToMarkdownTab(markdownTab);
                    }
                    
                    // Update UI on the UI thread
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                            {
                                // Handle AvalonEdit MarkdownTab
                                if (avalonTab.MarkdownEditor == null) return;
                                
                                // Update content
                                avalonTab.MarkdownDocument.Text = contentCopy;
                                
                                // Find and navigate to the changed text
                                var searchResult = await Task.Run(() => _textSearchService.FindTextInContent(contentCopy, changedText));
                                
                                if (searchResult.Index >= 0)
                                {
                                    int newCursorPosition = Math.Min(searchResult.Index + changedText.Length, contentCopy.Length);
                                    avalonTab.MarkdownEditor.CaretOffset = newCursorPosition;
                                    avalonTab.MarkdownEditor.Focus();
                                    
                                    var location = avalonTab.MarkdownEditor.Document.GetLocation(searchResult.Index);
                                    avalonTab.MarkdownEditor.ScrollToLine(location.Line);
                                    
                                    // Select the changed text for visual feedback
                                    avalonTab.MarkdownEditor.Select(searchResult.Index, searchResult.Length);
                                    
                                    ShowNavigationMessageSync($"Applied changes at line {location.Line}");
                                    Debug.WriteLine($"Successfully applied text changes. New cursor position: {newCursorPosition}");
                                }
                                else
                                {
                                    ShowNavigationMessageSync("Changes applied successfully.");
                                }
                            }
                        }
                        catch (Exception uiEx)
                        {
                            Debug.WriteLine($"Error updating UI after text changes: {uiEx.Message}");
                            ShowNavigationMessageSync("Changes applied but there was an error updating the display.");
                        }
                    }, DispatcherPriority.Normal);
                }
                else
                {
                    Debug.WriteLine($"Failed to apply text changes: {errorMessage}");
                    ShowNavigationMessage($"Failed to apply changes: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying text changes: {ex.Message}");
                ShowNavigationMessage("Error applying changes. Please try again.");
            }
        }

        private async void ApplyTextInsertion(object markdownTab, string anchorText, string newText)
        {
            try
            {
                Debug.WriteLine($"Applying text insertion in markdown tab");
                
                // CRITICAL FIX: Validate inputs before proceeding
                if (markdownTab == null)
                {
                    Debug.WriteLine("MarkdownTab is null");
                    ShowNavigationMessage("Editor not available.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(anchorText))
                {
                    Debug.WriteLine("Anchor text is null or empty");
                    ShowNavigationMessage("No anchor text to search for.");
                    return;
                }

                if (string.IsNullOrWhiteSpace(newText))
                {
                    Debug.WriteLine("New text is null or empty");
                    ShowNavigationMessage("No text to insert.");
                    return;
                }

                // Get editor content from AvalonEdit markdown tab
                string editorContent = string.Empty;
                if (markdownTab is Views.MarkdownTabAvalon avalonTab && avalonTab.MarkdownDocument != null)
                {
                    editorContent = avalonTab.MarkdownDocument.Text;
                }

                if (string.IsNullOrEmpty(editorContent))
                {
                    Debug.WriteLine("Editor content is empty");
                    ShowNavigationMessage("Editor content is empty.");
                    return;
                }

                // Show progress message for large operations
                if (editorContent.Length > 10000 || anchorText.Length > 1000)
                {
                    ShowNavigationMessage("Applying insertion...");
                }

                // Perform text processing on background thread
                string contentCopy = null;
                string errorMessage = null;
                bool success = false;
                int insertionIndex = -1;

                try
                {
                    await Task.Run(() =>
                    {
                        contentCopy = editorContent;
                        // Find the anchor text
                        var searchResult = _textSearchService.FindTextInContent(contentCopy, anchorText);
                        if (searchResult.Index >= 0)
                        {
                            // Calculate insertion point (after the anchor text)
                            insertionIndex = searchResult.Index + searchResult.Length;
                            
                            // Insert the new text after the anchor
                            // Add appropriate spacing if needed
                            string insertionText = newText;
                            if (!contentCopy.Substring(insertionIndex).StartsWith("\n") && !newText.StartsWith("\n"))
                            {
                                insertionText = "\n" + newText;
                            }
                            if (!insertionText.EndsWith("\n") && insertionIndex < contentCopy.Length - 1)
                            {
                                insertionText = insertionText + "\n";
                            }
                            
                            contentCopy = contentCopy.Insert(insertionIndex, insertionText);
                            success = true;
                        }
                        else
                        {
                            errorMessage = "Anchor text not found in document.";
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in background text processing: {ex.Message}");
                    ShowNavigationMessage("Error processing text insertion. Please try again.");
                    return;
                }
                
                if (success)
                {
                    Debug.WriteLine("Text insertion successful, updating editor");
                    
                    // Update the editor on the UI thread
                    await Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        try
                        {
                            if (markdownTab is Views.MarkdownTabAvalon avalonTab)
                            {
                                // Handle AvalonEdit MarkdownTab
                                if (avalonTab.MarkdownEditor == null) return;
                                
                                // Update content
                                avalonTab.MarkdownDocument.Text = contentCopy;
                                
                                // Navigate to the inserted text
                                var searchResult = await Task.Run(() => _textSearchService.FindTextInContent(contentCopy, newText));
                                
                                if (searchResult.Index >= 0)
                                {
                                    int newCursorPosition = Math.Min(searchResult.Index + newText.Length, contentCopy.Length);
                                    avalonTab.MarkdownEditor.CaretOffset = newCursorPosition;
                                    avalonTab.MarkdownEditor.Focus();
                                    
                                    var location = avalonTab.MarkdownEditor.Document.GetLocation(searchResult.Index);
                                    avalonTab.MarkdownEditor.ScrollToLine(location.Line);
                                    
                                    // Select the inserted text for visual feedback
                                    avalonTab.MarkdownEditor.Select(searchResult.Index, searchResult.Length);
                                    
                                    ShowNavigationMessageSync($"Inserted text at line {location.Line}");
                                    Debug.WriteLine($"Successfully inserted text. New cursor position: {newCursorPosition}");
                                }
                                else
                                {
                                    ShowNavigationMessageSync("Text inserted successfully.");
                                }
                            }
                        }
                        catch (Exception uiEx)
                        {
                            Debug.WriteLine($"Error updating UI after text insertion: {uiEx.Message}");
                            ShowNavigationMessageSync("Text inserted but there was an error updating the display.");
                        }
                    }), DispatcherPriority.Normal);
                }
                else
                {
                    Debug.WriteLine($"Failed to insert text: {errorMessage}");
                    ShowNavigationMessage($"Failed to insert text: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error applying text insertion: {ex.Message}");
                ShowNavigationMessage("Error applying insertion. Please try again.");
            }
        }

        private void ShowNavigationMessage(string message)
        {
            try
            {
                // Show a brief message to the user
                Debug.WriteLine($"Navigation message: {message}");
                
                // Try to show the message in the status bar if available
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow != null)
                {
                    // CRITICAL FIX: Use InvokeAsync instead of BeginInvoke to prevent deadlocks
                    _ = Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Try to find and update a status bar or create a temporary tooltip
                            var statusBar = mainWindow.FindName("StatusBar") as System.Windows.Controls.Primitives.StatusBar;
                            if (statusBar != null)
                            {
                                // Update status bar if it exists
                                var statusText = statusBar.Items.OfType<TextBlock>().FirstOrDefault();
                                if (statusText != null)
                                {
                                    statusText.Text = message;
                                    
                                    // Clear the message after 3 seconds
                                    var timer = new DispatcherTimer
                                    {
                                        Interval = TimeSpan.FromSeconds(3)
                                    };
                                    timer.Tick += (s, e) =>
                                    {
                                        statusText.Text = "Ready";
                                        timer.Stop();
                                    };
                                    timer.Start();
                                }
                            }
                            else
                            {
                                // Fallback: Show as a temporary tooltip on the main window
                                mainWindow.ToolTip = message;
                                var timer = new DispatcherTimer
                                {
                                    Interval = TimeSpan.FromSeconds(2)
                                };
                                timer.Tick += (s, e) =>
                                {
                                    mainWindow.ToolTip = null;
                                    timer.Stop();
                                };
                                timer.Start();
                            }
                        }
                        catch (Exception innerEx)
                        {
                            Debug.WriteLine($"Error updating UI with navigation message: {innerEx.Message}");
                        }
                    }, DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing navigation message: {ex.Message}");
            }
        }

        private void ShowNavigationMessageSync(string message)
        {
            try
            {
                // Show a brief message to the user
                Debug.WriteLine($"Navigation message: {message}");
                
                // Try to show the message in the status bar if available
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow != null)
                {
                    // CRITICAL FIX: Direct UI update since we're already on UI thread
                    // Try to find and update a status bar or create a temporary tooltip
                    var statusBar = mainWindow.FindName("StatusBar") as System.Windows.Controls.Primitives.StatusBar;
                    if (statusBar != null)
                    {
                        // Update status bar if it exists
                        var statusText = statusBar.Items.OfType<TextBlock>().FirstOrDefault();
                        if (statusText != null)
                        {
                            statusText.Text = message;
                            
                            // Clear the message after 3 seconds using a timer
                            var timer = new DispatcherTimer
                            {
                                Interval = TimeSpan.FromSeconds(3)
                            };
                            timer.Tick += (s, e) =>
                            {
                                statusText.Text = "Ready";
                                timer.Stop();
                            };
                            timer.Start();
                        }
                    }
                    else
                    {
                        // Fallback: Show as a temporary tooltip on the main window
                        mainWindow.ToolTip = message;
                        var timer = new DispatcherTimer
                        {
                            Interval = TimeSpan.FromSeconds(2)
                        };
                        timer.Tick += (s, e) =>
                        {
                            mainWindow.ToolTip = null;
                            timer.Stop();
                        };
                        timer.Start();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing navigation message: {ex.Message}");
            }
        }
    }
} 