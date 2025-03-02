using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Universa.Desktop.Interfaces;
using System.Linq;
using System.Windows.Input;
using System.Media;
using System.Threading;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Documents;
using Universa.Desktop.Commands;
using Universa.Desktop.Helpers;
using System.Net.WebSockets;
using Universa.Desktop.TTS;
using System.Diagnostics;
using System.ComponentModel;
using Universa.Desktop.Managers;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop
{
    public partial class MarkdownTab : UserControl, IFileTab, ITTSSupport, INotifyPropertyChanged
    {
        private string _filePath;
        private bool _isModified;
        private bool _isPlaying;
        private int _currentSearchIndex = -1;
        private List<int> _searchResults = new List<int>();
        private TextHighlighter _textHighlighter;
        private TTSClient _ttsClient;
        private int _lastKnownCursorPosition;
        private static string _currentFont;
        private readonly IConfigurationService _configService;
        private Dictionary<string, string> _frontmatter;
        private bool _hasFrontmatter;
        private bool _isFrontmatterVisible = false;
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<int> CursorPositionChanged;

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
                }
            }
        }

        public string FilePath 
        { 
            get => _filePath;
            set
            {
                _filePath = value;
                UpdateTitle();
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                _isModified = value;
                UpdateTitle();
            }
        }

        public TTSClient TTSClient
        {
            get => _ttsClient;
            set
            {
                if (_ttsClient != null)
                {
                    // Unsubscribe from old client's events
                    UnsubscribeTTSEvents();
                }
                _ttsClient = value;
                if (_ttsClient != null)
                {
                    // Subscribe to new client's events
                    SubscribeTTSEvents();
                }
            }
        }

        public int LastKnownCursorPosition
        {
            get => _lastKnownCursorPosition;
            private set
            {
                if (_lastKnownCursorPosition != value)
                {
                    _lastKnownCursorPosition = value;
                    CursorPositionChanged?.Invoke(this, value);
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastKnownCursorPosition)));
                }
            }
        }

        private void UnsubscribeTTSEvents()
        {
            if (_ttsClient == null) return;
            
            _ttsClient.OnHighlightText -= TTSClient_OnHighlightText;
            _ttsClient.OnPlaybackStarted -= TTSClient_OnPlaybackStarted;
            _ttsClient.OnPlaybackCompleted -= TTSClient_OnPlaybackCompleted;
        }

        private void SubscribeTTSEvents()
        {
            if (_ttsClient == null) return;
            
            Debug.WriteLine("Setting up TTS event handlers");
            _ttsClient.OnHighlightText += TTSClient_OnHighlightText;
            _ttsClient.OnPlaybackStarted += TTSClient_OnPlaybackStarted;
            _ttsClient.OnPlaybackCompleted += TTSClient_OnPlaybackCompleted;
            Debug.WriteLine("TTS event handlers set up successfully");
        }

        private void TTSClient_OnHighlightText(object sender, string text)
        {
            Debug.WriteLine($"OnHighlightText event received for text: {text}");
            if (_textHighlighter == null)
            {
                Debug.WriteLine("Warning: TextHighlighter is null when trying to highlight text");
                return;
            }

            try
            {
                // Use BeginInvoke to prevent blocking and handle dispatcher exceptions
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // Always clear existing highlights first
                        _textHighlighter.ClearHighlights();

                        if (!string.IsNullOrEmpty(text))
                        {
                            Debug.WriteLine($"Highlighting text: {text}");
                            Debug.WriteLine($"Editor text length: {Editor.Text.Length}");
                            
                            // Get the text being played from TTSClient
                            var ttsClient = sender as TTSClient;
                            if (ttsClient != null && !string.IsNullOrEmpty(ttsClient.CurrentText))
                            {
                                Debug.WriteLine($"Attempting to highlight TTS text: '{ttsClient.CurrentText}'");
                                
                                // Wait a brief moment to ensure previous highlight is cleared
                                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    _textHighlighter.HighlightText(ttsClient.CurrentText, Colors.Yellow);
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                            else
                            {
                                Debug.WriteLine("No current TTS text available");
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Clearing text highlights");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in UI thread highlighting: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error dispatching highlight operation: {ex.Message}");
            }
        }

        private void TTSClient_OnPlaybackStarted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                IsPlaying = true;
                UpdateTTSState();
            });
        }

        private void TTSClient_OnPlaybackCompleted(object sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                IsPlaying = false;
                UpdateTTSState();
            });
        }

        private void UpdateTTSState()
        {
            if (Parent is TabItem tabItem)
            {
                var headerText = string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);
                if (IsModified)
                {
                    headerText += "*";
                }
                if (IsPlaying)
                {
                    tabItem.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0));
                }
                else
                {
                    tabItem.Background = null;
                }
                tabItem.Header = headerText;
            }
        }

        private void UpdateTitle()
        {
            if (Parent is TabItem tabItem)
            {
                var headerText = string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);
                if (IsModified)
                {
                    headerText += "*";
                }
                if (IsPlaying)
                {
                    tabItem.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0));
                }
                else
                {
                    tabItem.Background = null;
                }
                tabItem.Header = headerText;
            }
        }

        public MarkdownTab()
        {
            try
            {
                InitializeComponent();
                DataContext = this;  // Set DataContext for binding
                
                // First, set up the editor and text highlighter
                _configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                SetupEditor();  // This initializes _textHighlighter
                SetupSearch();
                SetupFonts();  // Initialize fonts
                
                // Initialize frontmatter visibility state
                UpdateToggleButtonAppearance();
                
                Debug.WriteLine("MarkdownTab constructor completed");
                
                // Prevent drag operations while allowing text selection
                Editor.PreviewMouseMove += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        e.Handled = true;
                    }
                };
                
                Editor.PreviewMouseDown += (s, e) =>
                {
                    if (e.OriginalSource is TextBox)
                    {
                        e.Handled = true;
                        Editor.Focus();
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing markdown tab: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        public MarkdownTab(string filePath) : this()
        {
            try
            {
                FilePath = filePath;
                if (File.Exists(filePath))
                {
                    string fileContent = File.ReadAllText(filePath);
                    
                    // Parse frontmatter if present
                    string contentToDisplay = ProcessFrontmatterForLoading(fileContent);
                    
                    Editor.Text = contentToDisplay;
                    IsModified = false;
                    // Load versions immediately after opening the file
                    _ = LoadVersions();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file in markdown tab: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private void SetupEditor()
        {
            Editor.FontFamily = new FontFamily("Cascadia Code");
            Editor.BorderThickness = new Thickness(0);
            Editor.AcceptsTab = true;
            Editor.AcceptsReturn = true;
            Editor.TextWrapping = TextWrapping.Wrap;
            Editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            
            const int TAB_SIZE = 4;
            
            // Handle tab key to insert 4 spaces instead of a tab character
            Editor.PreviewKeyDown += (s, e) => {
                if (e.Key == Key.Tab)
                {
                    e.Handled = true;
                    int caretIndex = Editor.CaretIndex;
                    Editor.Text = Editor.Text.Insert(caretIndex, new string(' ', TAB_SIZE));
                    Editor.CaretIndex = caretIndex + TAB_SIZE;
                }
                else if (e.Key == Key.Enter)
                {
                    // When Enter is pressed, add an extra newline for paragraph spacing
                    e.Handled = true;
                    int caretIndex = Editor.CaretIndex;
                    
                    // Check if we're already at the end of a paragraph (two newlines)
                    bool alreadyHasNewline = false;
                    if (caretIndex < Editor.Text.Length && Editor.Text[caretIndex] == '\n')
                    {
                        alreadyHasNewline = true;
                    }
                    else if (caretIndex > 0 && caretIndex < Editor.Text.Length && 
                             Editor.Text[caretIndex-1] == '\n' && Editor.Text[caretIndex] == '\n')
                    {
                        alreadyHasNewline = true;
                    }
                    
                    // Insert a single newline if we're already at a paragraph break,
                    // otherwise insert two newlines for paragraph spacing
                    string insertion = alreadyHasNewline ? "\n" : "\n\n";
                    Editor.Text = Editor.Text.Insert(caretIndex, insertion);
                    Editor.CaretIndex = caretIndex + insertion.Length;
                }
                else if (e.Key == Key.Back && Editor.CaretIndex > 0)
                {
                    // Check if we're at a tab stop
                    int spacesBeforeCaret = 0;
                    int checkIndex = Editor.CaretIndex - 1;
                    
                    while (checkIndex >= 0 && Editor.Text[checkIndex] == ' ')
                    {
                        spacesBeforeCaret++;
                        checkIndex--;
                    }

                    // If we have spaces before the caret and they align with a tab stop
                    if (spacesBeforeCaret > 0 && spacesBeforeCaret <= TAB_SIZE)
                    {
                        int spacesToRemove = spacesBeforeCaret % TAB_SIZE;
                        if (spacesToRemove == 0) spacesToRemove = TAB_SIZE;
                        
                        if (spacesBeforeCaret >= spacesToRemove)
                        {
                            e.Handled = true;
                            int removeStart = Editor.CaretIndex - spacesToRemove;
                            Editor.Text = Editor.Text.Remove(removeStart, spacesToRemove);
                            Editor.CaretIndex = removeStart;
                        }
                    }
                }
            };
            
            // Add line spacing
            Editor.SetValue(Block.LineHeightProperty, 1.7);  // 1.7x line height for comfortable reading
            
            Editor.TextChanged += Editor_TextChanged;
            Editor.SelectionChanged += Editor_SelectionChanged;
            
            // Initialize text highlighter
            _textHighlighter = new TextHighlighter(Editor);
            
            // Add scroll event handler
            Editor.Loaded += (s, e) => {
                var scrollViewer = GetScrollViewer(Editor);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollChanged += Editor_ScrollChanged;
                }
            };

            ApplyTheme(Application.Current.Resources["IsDarkTheme"] as bool? ?? false);
            UpdateWordCount();  // Initial word count
        }

        private ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                if (child is ScrollViewer scrollViewer)
                    return scrollViewer;

                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            IsModified = true;
            UpdateWordCount();
        }

        private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            LastKnownCursorPosition = Editor.SelectionStart;
            Debug.WriteLine($"Cursor position updated to: {LastKnownCursorPosition}");
        }

        private void Editor_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (!Editor.IsMouseCaptured)
                {
                    Editor.CaptureMouse();
                }
                e.Handled = true;
            }
        }

        private void Editor_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Released && Editor.IsMouseCaptured)
            {
                Editor.ReleaseMouseCapture();
            }
            e.Handled = true;
        }

        private void Editor_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            Editor.Focus();
            Editor.CaptureMouse();
            e.Handled = true;
        }

        public async Task<bool> Save()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                return await SaveAs();
            }

            try
            {
                Debug.WriteLine("[MarkdownTab] Save method called");
                await SaveFileAsync();
                Debug.WriteLine("[MarkdownTab] Save completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTab] Error in Save: {ex.Message}");
                Debug.WriteLine($"[MarkdownTab] Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            if (string.IsNullOrEmpty(newPath))
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                    DefaultExt = ".md"
                };

                if (!string.IsNullOrEmpty(FilePath))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(FilePath);
                    dialog.FileName = Path.GetFileName(FilePath);
                }

                if (dialog.ShowDialog() == true)
                {
                    newPath = dialog.FileName;
                }
                else
                {
                    return false;
                }
            }

            try
            {
                FilePath = newPath;
                await SaveFileAsync();
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            if (string.IsNullOrEmpty(FilePath) || !File.Exists(FilePath))
            {
                return;
            }

            try
            {
                Editor.Text = File.ReadAllText(FilePath);
                IsModified = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reloading file: {ex.Message}", "Reload Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyTheme(bool isDarkMode)
        {
            Editor.Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            Editor.Foreground = (Brush)Application.Current.Resources["TextBrush"];
            Editor.CaretBrush = (Brush)Application.Current.Resources["TextBrush"];
            
            // Create a semi-transparent yellow brush similar to search highlighting
            var selectionBrush = new SolidColorBrush(Color.FromArgb(128, 255, 235, 100));
            Editor.SelectionBrush = selectionBrush;
            Editor.SelectionTextBrush = (Brush)Application.Current.Resources["TextBrush"];
        }

        private List<string> SplitIntoSentences(string text)
        {
            // Split on more punctuation marks and with smaller chunk sizes
            var chunks = new List<string>();
            var currentChunk = new StringBuilder();
            
            // First split by sentence endings
            var roughSentences = Regex.Split(text, @"(?<=[.!?])\s+");
            
            foreach (var sentence in roughSentences)
            {
                if (string.IsNullOrWhiteSpace(sentence)) continue;
                
                // Further split long sentences by commas, semicolons, or dashes
                var subParts = Regex.Split(sentence, @"(?<=[,;—])\s+");
                foreach (var part in subParts)
                {
                    if (string.IsNullOrWhiteSpace(part)) continue;
                    
                    if (part.Length > 100) // Split very long chunks by word count
                    {
                        var words = part.Split(' ');
                        var tempChunk = new StringBuilder();
                        
                        foreach (var word in words)
                        {
                            if (tempChunk.Length + word.Length > 100)
                            {
                                chunks.Add(tempChunk.ToString().Trim());
                                tempChunk.Clear();
                            }
                            tempChunk.Append(word).Append(" ");
                        }
                        
                        if (tempChunk.Length > 0)
                        {
                            chunks.Add(tempChunk.ToString().Trim());
                        }
                    }
                    else
                    {
                        chunks.Add(part.Trim());
                    }
                }
            }
            
            return chunks;
        }

        private void SetupSearch()
        {
            // Register Ctrl+F keyboard shortcut
            var showSearch = new KeyBinding(
                new RelayCommand(obj => ShowSearchPanel()),
                Key.F, ModifierKeys.Control);
            this.InputBindings.Add(showSearch);

            // Register F3 and Shift+F3 for navigation
            var findNext = new KeyBinding(
                new RelayCommand(obj => FindNext()),
                Key.F3, ModifierKeys.None);
            var findPrev = new KeyBinding(
                new RelayCommand(obj => FindPrevious()),
                Key.F3, ModifierKeys.Shift);
            this.InputBindings.Add(findNext);
            this.InputBindings.Add(findPrev);

            // Wire up search box events
            SearchBox.TextChanged += (s, e) => PerformSearch();
            SearchBox.KeyDown += (s, e) => {
                if (e.Key == Key.Enter) FindNext();
                else if (e.Key == Key.Escape) HideSearchPanel();
            };

            // Wire up button clicks
            FindNextButton.Click += (s, e) => FindNext();
            FindPreviousButton.Click += (s, e) => FindPrevious();
            CloseSearchButton.Click += (s, e) => HideSearchPanel();
        }

        private void ShowSearchPanel()
        {
            SearchPanel.Visibility = Visibility.Visible;
            SearchBox.Focus();
            SearchBox.SelectAll();
            if (!string.IsNullOrEmpty(SearchBox.Text))
            {
                PerformSearch();
            }
        }

        private void HideSearchPanel()
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            _textHighlighter.ClearHighlights();
            Editor.Focus();
        }

        private void PerformSearch()
        {
            _searchResults.Clear();
            _currentSearchIndex = -1;

            if (string.IsNullOrEmpty(SearchBox.Text) || SearchBox.Text.Length < 2)
            {
                _textHighlighter.ClearHighlights();
                return;
            }

            string text = Editor.Text;
            int index = 0;
            while ((index = text.IndexOf(SearchBox.Text, index, StringComparison.CurrentCultureIgnoreCase)) != -1)
            {
                _searchResults.Add(index);
                index += SearchBox.Text.Length;
            }

            if (_searchResults.Count > 0)
            {
                // Just highlight all results initially
                var ranges = _searchResults.Select(i => (i, SearchBox.Text.Length)).ToList();
                _textHighlighter.HighlightRanges(ranges, Color.FromRgb(255, 235, 100));
                
                // Move to first result
                _currentSearchIndex = 0;
                
                // Scroll to the first result without changing focus
                if (_currentSearchIndex >= 0 && _currentSearchIndex < _searchResults.Count)
                {
                    int firstIndex = _searchResults[_currentSearchIndex];
                    var line = Editor.GetLineIndexFromCharacterIndex(firstIndex);
                    
                    // Store current focus
                    var focused = FocusManager.GetFocusedElement(Window.GetWindow(this));
                    
                    // Scroll to line
                    Editor.ScrollToLine(line);
                    
                    // Restore focus
                    if (focused != null)
                    {
                        focused.Focus();
                    }
                }
            }
            else
            {
                _textHighlighter.ClearHighlights();
            }
        }

        private void FindNext()
        {
            if (_searchResults.Count == 0) return;

            _currentSearchIndex++;
            if (_currentSearchIndex >= _searchResults.Count)
                _currentSearchIndex = 0;

            HighlightCurrentResult();
        }

        private void FindPrevious()
        {
            if (_searchResults.Count == 0) return;

            _currentSearchIndex--;
            if (_currentSearchIndex < 0)
                _currentSearchIndex = _searchResults.Count - 1;

            HighlightCurrentResult();
        }

        private void HighlightCurrentResult()
        {
            if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchResults.Count)
                return;

            int index = _searchResults[_currentSearchIndex];
            
            // First highlight all results
            var ranges = _searchResults.Select(pos => (pos, SearchBox.Text.Length)).ToList();
            _textHighlighter.HighlightRanges(ranges, Color.FromRgb(255, 235, 100));
            
            // Then highlight current result in a different color
            _textHighlighter.HighlightRanges(new List<(int, int)> { (index, SearchBox.Text.Length) }, Colors.Orange);

            // Scroll to the current result without changing focus
            var line = Editor.GetLineIndexFromCharacterIndex(index);
            var focused = FocusManager.GetFocusedElement(Window.GetWindow(this));
            Editor.ScrollToLine(line);
            if (focused != null)
            {
                focused.Focus();
            }
        }

        public void ClearHighlights()
        {
            _textHighlighter?.ClearHighlights();
        }

        public void HighlightRanges(IEnumerable<(int start, int length)> ranges, Color color)
        {
            _textHighlighter?.HighlightRanges(ranges.ToList(), color);
        }

        private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _textHighlighter?.RefreshHighlights();
        }

        public void StopTTS()
        {
            if (_ttsClient != null)
            {
                _ttsClient.Stop();
                _textHighlighter?.ClearHighlights();
                IsPlaying = false;
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    UpdateTTSState();
                }));
            }
        }

        private void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlaying)
            {
                StopTTS();
            }
            else
            {
                var mainWindow = Window.GetWindow(this) as Universa.Desktop.Views.MainWindow;
                if (mainWindow?.TTSClient != null)
                {
                    _ = mainWindow.TTSClient.SpeakAsync(GetTextToSpeak());
                }
            }
        }

        public string GetTextToSpeak()
        {
            return !string.IsNullOrEmpty(Editor.SelectedText) 
                ? Editor.SelectedText 
                : Editor.Text;
        }

        private void UpdateWordCount()
        {
            var text = Editor.Text;
            var wordCount = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var charCount = text.Length;
            
            // Calculate reading time (assuming 225 words per minute)
            var readingTimeMinutes = Math.Max(1, (int)Math.Ceiling(wordCount / 225.0));
            var readingTimeText = readingTimeMinutes == 1 ? "1 minute" : $"{readingTimeMinutes} minutes";
            
            WordCountText.Text = $"Words: {wordCount} | Characters: {charCount} | Reading time: {readingTimeText}";
        }

        private async Task SaveFileAsync()
        {
            try
            {
                Debug.WriteLine($"\n[MarkdownTab] Starting to save file: {FilePath}");
                Debug.WriteLine($"[MarkdownTab] File extension: {Path.GetExtension(FilePath)}");
                
                var isVersioned = LibraryManager.Instance.IsVersionedFile(FilePath);
                Debug.WriteLine($"[MarkdownTab] Is versioned file: {isVersioned}");

                // Get the text content from the UI thread before running the file save
                string content = Editor.Text;
                
                // Process frontmatter if needed
                content = ProcessFrontmatterForSaving(content);
                
                // Save the file content
                await Task.Run(() => 
                {
                    Debug.WriteLine($"[MarkdownTab] Writing file contents to: {FilePath}");
                    File.WriteAllText(FilePath, content);
                });
                Debug.WriteLine($"[MarkdownTab] Successfully wrote file contents");
                
                // Update UI state on the UI thread
                await Dispatcher.InvokeAsync(() => IsModified = false);

                if (isVersioned)
                {
                    // Save a version after each save
                    try
                    {
                        Debug.WriteLine($"[MarkdownTab] Attempting to save version");
                        var versionManager = VersionManager.GetInstance();
                        Debug.WriteLine($"[MarkdownTab] Got VersionManager instance");
                        
                        // Explicitly check if file exists before saving version
                        if (File.Exists(FilePath))
                        {
                            Debug.WriteLine($"[MarkdownTab] File exists, proceeding with version save");
                            var directory = Path.GetDirectoryName(FilePath);
                            var versionsDir = Path.Combine(directory, ".versions");
                            Debug.WriteLine($"[MarkdownTab] Versions directory will be: {versionsDir}");
                            
                            try
                            {
                                await versionManager.SaveVersion(FilePath);
                                Debug.WriteLine($"[MarkdownTab] Successfully saved version");

                                // Refresh the versions list
                                await LoadVersions();
                                Debug.WriteLine($"[MarkdownTab] Versions list refreshed");
                            }
                            catch (Exception saveEx)
                            {
                                Debug.WriteLine($"[MarkdownTab] Error in SaveVersion: {saveEx.Message}");
                                Debug.WriteLine($"[MarkdownTab] Stack trace: {saveEx.StackTrace}");
                                throw; // Re-throw to be caught by outer catch block
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[MarkdownTab] File does not exist after save: {FilePath}");
                        }
                    }
                    catch (Exception versionEx)
                    {
                        Debug.WriteLine($"[MarkdownTab] Error saving version: {versionEx.Message}");
                        Debug.WriteLine($"[MarkdownTab] Stack trace: {versionEx.StackTrace}");
                        await Dispatcher.InvokeAsync(() => 
                            MessageBox.Show($"Warning: File was saved but version could not be created.\nError: {versionEx.Message}", 
                                "Version Creation Error", MessageBoxButton.OK, MessageBoxImage.Warning)
                        );
                    }
                }
                else
                {
                    Debug.WriteLine($"[MarkdownTab] File type is not configured for versioning");
                }

                // Get the configuration service
                var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                if (configService == null)
                {
                    Debug.WriteLine("[MarkdownTab] Could not get configuration service");
                    throw new InvalidOperationException("Configuration service is not available");
                }

                // Get the library path from the configuration service
                var libraryPath = configService.Provider.LibraryPath;
                if (string.IsNullOrEmpty(libraryPath))
                {
                    Debug.WriteLine("[MarkdownTab] Library path is not configured");
                    throw new InvalidOperationException("Library path is not configured");
                }

                // Get the relative path and sync
                var relativePath = Path.GetRelativePath(libraryPath, FilePath);
                await SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                Debug.WriteLine($"[MarkdownTab] File synced");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTab] Error saving file: {ex.Message}");
                Debug.WriteLine($"[MarkdownTab] Stack trace: {ex.StackTrace}");
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error)
                );
                throw;
            }
        }

        private async Task LoadVersions()
        {
            try
            {
                Debug.WriteLine($"[MarkdownTab] Loading versions for file: {FilePath}");
                var versions = VersionManager.GetInstance().GetVersions(FilePath);
                Debug.WriteLine($"[MarkdownTab] Found {versions.Count} versions");
                
                // Store current selection
                var currentSelection = VersionComboBox.SelectedItem;
                
                // Update items
                VersionComboBox.ItemsSource = versions;
                
                // Restore selection if possible
                if (currentSelection != null)
                {
                    var matchingVersion = versions.FirstOrDefault(v => v.Path == ((Universa.Desktop.Managers.FileVersionInfo)currentSelection).Path);
                    if (matchingVersion != null)
                    {
                        VersionComboBox.SelectedItem = matchingVersion;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTab] Error loading versions: {ex.Message}");
                Debug.WriteLine($"[MarkdownTab] Stack trace: {ex.StackTrace}");
                MessageBox.Show($"Error loading versions: {ex.Message}", "Version Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (VersionComboBox.SelectedItem is Universa.Desktop.Managers.FileVersionInfo selectedVersion)
            {
                try
                {
                    // If there are unsaved changes, prompt to save
                    if (IsModified)
                    {
                        var result = MessageBox.Show(
                            "You have unsaved changes. Would you like to save them before loading the selected version?",
                            "Unsaved Changes",
                            MessageBoxButton.YesNoCancel,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Cancel)
                        {
                            // Revert selection
                            e.Handled = true;
                            return;
                        }
                        if (result == MessageBoxResult.Yes)
                        {
                            await Save();
                        }
                    }

                    // Confirm version load
                    var loadResult = MessageBox.Show(
                        $"Are you sure you want to load the version from {selectedVersion.Timestamp:dd MMM yyyy HH:mm:ss}?\n\nThis will replace the current content.",
                        "Confirm Version Load",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (loadResult == MessageBoxResult.Yes)
                    {
                        var content = await VersionManager.GetInstance().LoadVersion(selectedVersion.Path);
                        Editor.Text = content;
                        IsModified = true;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error loading version: {ex.Message}", "Version Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally
                {
                    // Clear selection to allow reselecting the same version
                    VersionComboBox.SelectedItem = null;
                }
            }
        }

        private async void RefreshVersionsButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadVersions();
        }

        public void OnTabActivated()
        {
            // Load versions when tab is activated
            _ = LoadVersions();
        }

        private void BoldButton_Click(object sender, RoutedEventArgs e)
        {
            var selectionStart = Editor.SelectionStart;
            var selectionLength = Editor.SelectionLength;
            var selectedText = Editor.SelectedText;

            if (string.IsNullOrEmpty(selectedText))
            {
                // If no text is selected, insert bold markers at cursor position
                Editor.Text = Editor.Text.Insert(selectionStart, "****");
                Editor.SelectionStart = selectionStart + 2;
            }
            else
            {
                // Wrap selected text in bold markers
                Editor.Text = Editor.Text.Remove(selectionStart, selectionLength)
                                      .Insert(selectionStart, $"**{selectedText}**");
                Editor.SelectionStart = selectionStart;
                Editor.SelectionLength = selectedText.Length + 4;
            }
            IsModified = true;
        }

        private void ItalicButton_Click(object sender, RoutedEventArgs e)
        {
            var selectionStart = Editor.SelectionStart;
            var selectionLength = Editor.SelectionLength;
            var selectedText = Editor.SelectedText;

            if (string.IsNullOrEmpty(selectedText))
            {
                // If no text is selected, insert italic markers at cursor position
                Editor.Text = Editor.Text.Insert(selectionStart, "**");
                Editor.SelectionStart = selectionStart + 1;
            }
            else
            {
                // Wrap selected text in italic markers
                Editor.Text = Editor.Text.Remove(selectionStart, selectionLength)
                                      .Insert(selectionStart, $"*{selectedText}*");
                Editor.SelectionStart = selectionStart;
                Editor.SelectionLength = selectedText.Length + 2;
            }
            IsModified = true;
        }

        private void SetupFonts()
        {
            try
            {
                // Get all installed fonts
                var fonts = System.Windows.Media.Fonts.SystemFontFamilies
                    .OrderBy(f => f.Source)
                    .ToList();

                // Populate the ComboBox
                FontComboBox.ItemsSource = fonts;

                // Load saved font preference
                var savedFont = _configService.Provider.GetValue<string>(ConfigurationKeys.Editor.Font);
                if (!string.IsNullOrEmpty(savedFont))
                {
                    var fontFamily = fonts.FirstOrDefault(f => f.Source == savedFont);
                    if (fontFamily != null)
                    {
                        FontComboBox.SelectedItem = fontFamily;
                        ApplyFont(fontFamily);
                    }
                }
                else
                {
                    // Default to Cascadia Code if available, otherwise use the first monospace font
                    var defaultFont = fonts.FirstOrDefault(f => f.Source == "Cascadia Code") 
                        ?? fonts.FirstOrDefault(f => f.Source.Contains("Mono") || f.Source.Contains("Consolas"))
                        ?? fonts.First();
                    
                    FontComboBox.SelectedItem = defaultFont;
                    ApplyFont(defaultFont);
                }

                // Load saved font size preference
                var savedFontSize = _configService.Provider.GetValue<double>(ConfigurationKeys.Editor.FontSize);
                if (savedFontSize > 0)
                {
                    var fontSizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => double.Parse(item.Content.ToString()) == savedFontSize);
                    if (fontSizeItem != null)
                    {
                        FontSizeComboBox.SelectedItem = fontSizeItem;
                    }
                }
                else
                {
                    // Default to 12pt if no size is saved
                    var defaultSizeItem = FontSizeComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => item.Content.ToString() == "12");
                    if (defaultSizeItem != null)
                    {
                        FontSizeComboBox.SelectedItem = defaultSizeItem;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up fonts: {ex.Message}");
                MessageBox.Show($"Error setting up fonts: {ex.Message}", "Font Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontComboBox.SelectedItem is FontFamily selectedFont)
            {
                ApplyFont(selectedFont);
                _configService.Provider.SetValue(ConfigurationKeys.Editor.Font, selectedFont.Source);
                _configService.Provider.Save();
            }
        }

        private void ApplyFont(FontFamily font)
        {
            if (font == null) return;
            
            _currentFont = font.Source;
            Editor.FontFamily = font;

            // Update all other open MarkdownTabs
            if (Application.Current.MainWindow is Views.MainWindow mainWindow)
            {
                foreach (TabItem tab in mainWindow.MainTabControl.Items)
                {
                    if (tab.Content is MarkdownTab markdownTab && markdownTab != this)
                    {
                        markdownTab.Editor.FontFamily = font;
                        if (markdownTab.FontComboBox.SelectedItem != font)
                        {
                            markdownTab.FontComboBox.SelectedItem = font;
                        }
                    }
                }
            }
        }

        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                if (double.TryParse(selectedItem.Content.ToString(), out double fontSize))
                {
                    ApplyFontSize(fontSize);
                    _configService.Provider.SetValue(ConfigurationKeys.Editor.FontSize, fontSize);
                    _configService.Provider.Save();
                }
            }
        }

        private void ApplyFontSize(double fontSize)
        {
            Editor.FontSize = fontSize;

            // Update all other open MarkdownTabs
            if (Application.Current.MainWindow is Views.MainWindow mainWindow)
            {
                foreach (TabItem tab in mainWindow.MainTabControl.Items)
                {
                    if (tab.Content is MarkdownTab markdownTab && markdownTab != this)
                    {
                        markdownTab.Editor.FontSize = fontSize;
                        if (markdownTab.FontSizeComboBox.SelectedItem != FontSizeComboBox.SelectedItem)
                        {
                            markdownTab.FontSizeComboBox.SelectedItem = FontSizeComboBox.SelectedItem;
                        }
                    }
                }
            }
        }

        // New methods for frontmatter handling
        
        /// <summary>
        /// Processes the content for loading, extracting frontmatter if present
        /// </summary>
        private string ProcessFrontmatterForLoading(string content)
        {
            _frontmatter = new Dictionary<string, string>();
            _hasFrontmatter = false;
            
            // Check if the content starts with frontmatter delimiter
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing delimiter
                int endIndex = -1;
                
                // Skip the first line (opening delimiter)
                int startIndex = content.IndexOf('\n') + 1;
                if (startIndex < content.Length)
                {
                    // Look for closing delimiter
                    endIndex = content.IndexOf("\n---", startIndex);
                    if (endIndex > startIndex)
                    {
                        // Extract frontmatter content
                        string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
                        ParseFrontmatter(frontmatterContent);
                        
                        // Skip past the closing delimiter
                        int contentStartIndex = endIndex + 4; // Length of "\n---"
                        if (contentStartIndex < content.Length)
                        {
                            // If there's a newline after the closing delimiter, skip it too
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            // Return the content without frontmatter
                            _hasFrontmatter = true;
                            
                            // If frontmatter should be visible, return the full content
                            if (_isFrontmatterVisible)
                                return content;
                            
                            // Return content without frontmatter, ensuring no extra newlines
                            string contentWithoutFrontmatter = content.Substring(contentStartIndex);
                            return contentWithoutFrontmatter.TrimStart(); // Remove any leading whitespace
                        }
                    }
                }
            }
            
            return content;
        }
        
        /// <summary>
        /// Processes the content for saving, adding frontmatter if needed
        /// </summary>
        private string ProcessFrontmatterForSaving(string content)
        {
            // If frontmatter is currently visible in the editor, we need to extract it
            // to avoid duplicating it
            if (_isFrontmatterVisible && (content.StartsWith("---\n") || content.StartsWith("---\r\n")))
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
            
            if (_frontmatter == null || _frontmatter.Count == 0)
            {
                // No frontmatter to add
                return content;
            }
            
            // Build frontmatter section
            StringBuilder frontmatterBuilder = new StringBuilder();
            frontmatterBuilder.AppendLine("---");
            
            foreach (var kvp in _frontmatter)
            {
                frontmatterBuilder.AppendLine($"{kvp.Key}: {kvp.Value}");
            }
            
            frontmatterBuilder.AppendLine("---");
            
            // Ensure content doesn't start with extra newlines
            content = content.TrimStart();
            
            frontmatterBuilder.Append(content);
            
            return frontmatterBuilder.ToString();
        }
        
        /// <summary>
        /// Parses frontmatter content into key-value pairs
        /// </summary>
        private void ParseFrontmatter(string frontmatterContent)
        {
            _frontmatter = new Dictionary<string, string>();
            
            // Split by lines
            string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            
            foreach (string line in lines)
            {
                // Look for key-value pairs (key: value)
                int colonIndex = line.IndexOf(':');
                if (colonIndex > 0)
                {
                    string key = line.Substring(0, colonIndex).Trim();
                    string value = line.Substring(colonIndex + 1).Trim();
                    
                    // Store in dictionary
                    _frontmatter[key] = value;
                }
                else if (line.StartsWith("#"))
                {
                    // Handle tags (like #fiction)
                    string tag = line.Trim();
                    _frontmatter[tag] = "true";
                }
            }
        }
        
        /// <summary>
        /// Gets a frontmatter value by key
        /// </summary>
        public string GetFrontmatterValue(string key)
        {
            if (_frontmatter != null && _frontmatter.TryGetValue(key, out string value))
            {
                return value;
            }
            return null;
        }
        
        /// <summary>
        /// Sets a frontmatter value
        /// </summary>
        public void SetFrontmatterValue(string key, string value)
        {
            if (_frontmatter == null)
            {
                _frontmatter = new Dictionary<string, string>();
            }
            
            _frontmatter[key] = value;
            IsModified = true;
        }
        
        /// <summary>
        /// Checks if the document has frontmatter
        /// </summary>
        public bool HasFrontmatter()
        {
            return _hasFrontmatter || (_frontmatter != null && _frontmatter.Count > 0);
        }
        
        /// <summary>
        /// Gets all frontmatter keys
        /// </summary>
        public IEnumerable<string> GetFrontmatterKeys()
        {
            return _frontmatter?.Keys ?? Enumerable.Empty<string>();
        }

        private void FrontmatterButton_Click(object sender, RoutedEventArgs e)
        {
            ShowFrontmatterDialog();
        }
        
        private void ShowFrontmatterDialog()
        {
            // Clear existing fields
            FrontmatterFields.Children.Clear();
            
            // If we don't have frontmatter yet, initialize with common fields
            if (_frontmatter == null || _frontmatter.Count == 0)
            {
                _frontmatter = new Dictionary<string, string>
                {
                    { "title", Path.GetFileNameWithoutExtension(FilePath) ?? "" },
                    { "author", "" },
                    { "authorfirst", "" },
                    { "authorlast", "" },
                    { "#fiction", "true" }
                };
            }
            
            // Add fields for each frontmatter entry
            foreach (var kvp in _frontmatter)
            {
                AddFrontmatterField(kvp.Key, kvp.Value);
            }
            
            // Show the dialog
            FrontmatterDialog.Visibility = Visibility.Visible;
        }
        
        private void AddFrontmatterField(string key, string value)
        {
            var fieldPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 5)
            };
            
            var keyTextBox = new TextBox
            {
                Text = key,
                Width = 150,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            var valueTextBox = new TextBox
            {
                Text = value,
                Width = 250,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            var removeButton = new Button
            {
                Content = "✕",
                Padding = new Thickness(5, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            
            removeButton.Click += (s, e) =>
            {
                FrontmatterFields.Children.Remove(fieldPanel);
            };
            
            fieldPanel.Children.Add(keyTextBox);
            fieldPanel.Children.Add(valueTextBox);
            fieldPanel.Children.Add(removeButton);
            
            FrontmatterFields.Children.Add(fieldPanel);
        }
        
        private void AddFieldButton_Click(object sender, RoutedEventArgs e)
        {
            AddFrontmatterField("", "");
        }
        
        private void SaveFrontmatterButton_Click(object sender, RoutedEventArgs e)
        {
            // Save the frontmatter values
            _frontmatter.Clear();
            
            foreach (var child in FrontmatterFields.Children)
            {
                if (child is StackPanel panel && panel.Children.Count >= 2)
                {
                    var keyTextBox = panel.Children[0] as TextBox;
                    var valueTextBox = panel.Children[1] as TextBox;
                    
                    if (keyTextBox != null && valueTextBox != null && !string.IsNullOrWhiteSpace(keyTextBox.Text))
                    {
                        _frontmatter[keyTextBox.Text] = valueTextBox.Text;
                    }
                }
            }
            
            // Mark as modified
            IsModified = true;
            
            // Hide the dialog
            FrontmatterDialog.Visibility = Visibility.Collapsed;
            
            // Refresh the editor content if frontmatter is visible
            if (_isFrontmatterVisible)
            {
                RefreshEditorContent();
            }
        }
        
        private void CancelFrontmatterButton_Click(object sender, RoutedEventArgs e)
        {
            // Just hide the dialog without saving
            FrontmatterDialog.Visibility = Visibility.Collapsed;
        }

        // Add support for chapter structure
        private void AddChapterStructure()
        {
            // Get the current text
            string content = Editor.Text;
            
            // Check if we already have chapter headings - specifically looking for H2 chapters
            bool hasChapters = Regex.IsMatch(content, @"^##\s+Chapter\s+\d+", RegexOptions.Multiline);
            
            if (!hasChapters)
            {
                // Split content by lines
                string[] lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                StringBuilder newContent = new StringBuilder();
                
                // Add title as H1 if not present
                if (!Regex.IsMatch(content, @"^#\s+[^\n\r]+", RegexOptions.Multiline))
                {
                    string title = string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileNameWithoutExtension(FilePath);
                    newContent.AppendLine($"# {title}");
                    newContent.AppendLine();
                }
                
                // Add chapter heading as H2
                newContent.AppendLine("## Chapter 1");
                newContent.AppendLine();
                
                // Add the existing content
                foreach (string line in lines)
                {
                    newContent.AppendLine(line);
                }
                
                // Update the editor
                Editor.Text = newContent.ToString();
                IsModified = true;
            }
        }
        
        // Add a new chapter at the current cursor position
        private void AddNewChapter()
        {
            int cursorPosition = Editor.CaretIndex;
            string content = Editor.Text;
            
            // Find all existing chapters - specifically looking for H2 chapters
            var chapterMatches = Regex.Matches(content, @"^##\s+Chapter\s+(\d+)", RegexOptions.Multiline);
            int highestChapterNumber = 0;
            
            foreach (Match match in chapterMatches)
            {
                if (match.Groups.Count > 1 && int.TryParse(match.Groups[1].Value, out int chapterNumber))
                {
                    highestChapterNumber = Math.Max(highestChapterNumber, chapterNumber);
                }
            }
            
            // Create new chapter heading as H2
            string newChapter = $"\n\n## Chapter {highestChapterNumber + 1}\n\n";
            
            // Insert at cursor position
            Editor.Text = content.Insert(cursorPosition, newChapter);
            Editor.CaretIndex = cursorPosition + newChapter.Length;
            IsModified = true;
        }

        private void AddChapterButton_Click(object sender, RoutedEventArgs e)
        {
            AddNewChapter();
        }
        
        private void StructureChaptersButton_Click(object sender, RoutedEventArgs e)
        {
            AddChapterStructure();
        }

        private void ToggleFrontmatterButton_Click(object sender, RoutedEventArgs e)
        {
            _isFrontmatterVisible = !_isFrontmatterVisible;
            UpdateToggleButtonAppearance();
            RefreshEditorContent();
        }
        
        private void UpdateToggleButtonAppearance()
        {
            // Update the button appearance based on the current state
            if (ToggleFrontmatterButton != null)
            {
                if (_isFrontmatterVisible)
                {
                    ToggleFrontmatterButton.ToolTip = "Hide Frontmatter";
                    ToggleFrontmatterButton.Background = new SolidColorBrush(Colors.LightBlue);
                }
                else
                {
                    ToggleFrontmatterButton.ToolTip = "Show Frontmatter";
                    ToggleFrontmatterButton.Background = null; // Use default background
                }
            }
        }

        private void RefreshEditorContent()
        {
            // Store cursor position
            int cursorPosition = Editor.CaretIndex;
            
            // If frontmatter is visible and we're toggling it off
            if (_hasFrontmatter && !_isFrontmatterVisible && (Editor.Text.StartsWith("---\n") || Editor.Text.StartsWith("---\r\n")))
            {
                // Find the end of the frontmatter section
                int endIndex = Editor.Text.IndexOf("\n---", 4);
                if (endIndex > 0)
                {
                    // Skip past the closing delimiter
                    int contentStartIndex = endIndex + 4; // Length of "\n---"
                    if (contentStartIndex < Editor.Text.Length)
                    {
                        // If there's a newline after the closing delimiter, skip it too
                        if (Editor.Text[contentStartIndex] == '\n')
                            contentStartIndex++;
                        
                        // Update the editor with content without frontmatter
                        Editor.Text = Editor.Text.Substring(contentStartIndex);
                        
                        // Adjust cursor position
                        cursorPosition = Math.Max(0, cursorPosition - contentStartIndex);
                    }
                }
            }
            // If frontmatter should be visible and isn't already
            else if (_frontmatter != null && _frontmatter.Count > 0 && _isFrontmatterVisible && 
                    (!Editor.Text.StartsWith("---\n") && !Editor.Text.StartsWith("---\r\n")))
            {
                // Build frontmatter section
                StringBuilder frontmatterBuilder = new StringBuilder();
                frontmatterBuilder.AppendLine("---");
                
                foreach (var kvp in _frontmatter)
                {
                    frontmatterBuilder.AppendLine($"{kvp.Key}: {kvp.Value}");
                }
                
                frontmatterBuilder.AppendLine("---");
                
                // Trim any leading whitespace from the content to prevent accumulation
                string content = Editor.Text.TrimStart();
                
                // Calculate how much whitespace was removed
                int whitespaceRemoved = Editor.Text.Length - content.Length;
                
                // Update the editor with frontmatter + content
                Editor.Text = frontmatterBuilder.ToString() + content;
                
                // Adjust cursor position to account for added frontmatter and removed whitespace
                cursorPosition = Math.Max(0, cursorPosition - whitespaceRemoved) + frontmatterBuilder.Length;
            }
            
            // Restore cursor position if possible
            if (cursorPosition >= 0 && cursorPosition < Editor.Text.Length)
            {
                Editor.CaretIndex = cursorPosition;
            }
            else if (cursorPosition >= Editor.Text.Length)
            {
                Editor.CaretIndex = Editor.Text.Length;
            }
            else
            {
                Editor.CaretIndex = 0;
            }
        }

        // Helper methods for document structure and heading hierarchy
        
        /// <summary>
        /// Adds a heading of the specified level at the current cursor position
        /// </summary>
        /// <param name="level">Heading level (1-6)</param>
        /// <param name="text">Heading text</param>
        public void AddHeading(int level, string text)
        {
            if (level < 1 || level > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(level), "Heading level must be between 1 and 6");
            }
            
            int cursorPosition = Editor.CaretIndex;
            string content = Editor.Text;
            
            // Create heading with appropriate number of hashtags
            string heading = new string('#', level) + " " + text;
            string insertion = $"\n\n{heading}\n\n";
            
            // Insert at cursor position
            Editor.Text = content.Insert(cursorPosition, insertion);
            Editor.CaretIndex = cursorPosition + insertion.Length;
            IsModified = true;
        }
        
        /// <summary>
        /// Analyzes the document structure to ensure proper heading hierarchy
        /// </summary>
        /// <returns>True if the document has proper heading hierarchy, false otherwise</returns>
        public bool AnalyzeHeadingHierarchy()
        {
            string content = Editor.Text;
            var headingMatches = Regex.Matches(content, @"^(#+)\s+(.+)$", RegexOptions.Multiline);
            
            int lastLevel = 0;
            bool hasProperHierarchy = true;
            
            foreach (Match match in headingMatches)
            {
                int level = match.Groups[1].Length;
                string text = match.Groups[2].Value;
                
                // Check if heading level jumps by more than one
                if (lastLevel > 0 && level > lastLevel + 1)
                {
                    hasProperHierarchy = false;
                    Debug.WriteLine($"Improper heading hierarchy: H{lastLevel} followed by H{level} ({text})");
                }
                
                lastLevel = level;
            }
            
            return hasProperHierarchy;
        }
        
        /// <summary>
        /// Gets document metadata for ePub export
        /// </summary>
        /// <returns>Dictionary with document metadata</returns>
        public Dictionary<string, string> GetDocumentMetadata()
        {
            var metadata = new Dictionary<string, string>();
            
            // Add frontmatter metadata if available
            if (_frontmatter != null && _frontmatter.Count > 0)
            {
                foreach (var kvp in _frontmatter)
                {
                    // Skip tags (keys starting with #)
                    if (!kvp.Key.StartsWith("#"))
                    {
                        metadata[kvp.Key] = kvp.Value;
                    }
                }
            }
            
            // Try to extract title from first H1 if not in frontmatter
            if (!metadata.ContainsKey("title"))
            {
                string content = Editor.Text;
                var titleMatch = Regex.Match(content, @"^#\s+(.+)$", RegexOptions.Multiline);
                if (titleMatch.Success)
                {
                    metadata["title"] = titleMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Use filename as fallback
                    metadata["title"] = string.IsNullOrEmpty(FilePath) ? 
                        "Untitled" : Path.GetFileNameWithoutExtension(FilePath);
                }
            }
            
            return metadata;
        }

        private void HeadingH1Button_Click(object sender, RoutedEventArgs e)
        {
            // Prompt for heading text
            var dialog = new Universa.Desktop.Dialogs.InputDialog(
                "Add H1 Heading",
                "Enter heading text:",
                true);
            
            if (dialog.ShowDialog() == true)
            {
                AddHeading(1, dialog.InputText);
            }
        }
        
        private void HeadingH2Button_Click(object sender, RoutedEventArgs e)
        {
            // Prompt for heading text
            var dialog = new Universa.Desktop.Dialogs.InputDialog(
                "Add H2 Heading",
                "Enter heading text:",
                true);
            
            if (dialog.ShowDialog() == true)
            {
                AddHeading(2, dialog.InputText);
            }
        }
        
        private void HeadingH3Button_Click(object sender, RoutedEventArgs e)
        {
            // Prompt for heading text
            var dialog = new Universa.Desktop.Dialogs.InputDialog(
                "Add H3 Heading",
                "Enter heading text:",
                true);
            
            if (dialog.ShowDialog() == true)
            {
                AddHeading(3, dialog.InputText);
            }
        }
        
        private void HeadingH4Button_Click(object sender, RoutedEventArgs e)
        {
            // Prompt for heading text
            var dialog = new Universa.Desktop.Dialogs.InputDialog(
                "Add H4 Heading",
                "Enter heading text:",
                true);
            
            if (dialog.ShowDialog() == true)
            {
                AddHeading(4, dialog.InputText);
            }
        }

        /// <summary>
        /// Gets the content of the markdown editor
        /// </summary>
        /// <returns>The content as a string</returns>
        public string GetContent()
        {
            // Get the text from the editor
            if (Editor != null)
            {
                return Editor.Text;
            }
            
            return string.Empty;
        }
    }
} 