using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Media;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Media;
using System.Threading;
using System.Linq;
using System.Windows.Input;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Universa.Desktop.Interfaces;
using System.Text;
using Universa.Desktop.Commands;
using Universa.Desktop.Helpers;
using Universa.Desktop.TTS;
using Universa.Desktop.Managers;
using Microsoft.Win32;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop
{
    public partial class EditorTab : UserControl, INotifyPropertyChanged, IFileTab, ITTSSupport
    {
        private string _filePath;
        private bool _isModified;
        
        public int LastKnownCursorPosition { get; private set; } = 0;
        private CancellationTokenSource _ttsCancellationSource;
        private string _tempAudioFile;
        private int _currentSearchIndex = -1;
        private List<int> _searchResults = new List<int>();
        private TextHighlighter _textHighlighter;
        private TTSClient _ttsClient;
        private bool _isPlaying;
        private MediaPlayer _mediaPlayer = new MediaPlayer();
        private string _title;
        private bool _isContentLoaded;
        private string _cachedContent;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler ContentChanged;

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
                UpdateTabHeader();
            }
        }

        public string Title
        {
            get => _title ?? Path.GetFileName(FilePath);
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
                    UpdateTabHeader();
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
                    UpdateTabHeader();
                }
            }
        }

        public TTSClient TTSClient
        {
            get => _ttsClient;
            set => _ttsClient = value;
        }

        public EditorTab()
        {
            InitializeComponent();
            
            // Set up the editor and text highlighter
            SetupEditor();
            _textHighlighter = new TextHighlighter(Editor);
            SetupSearch();

            // Set up TTS button handler
            TTSButton.Click += TTSButton_Click;
        }

        private void SetupEditor()
        {
            Editor.AcceptsReturn = true;
            Editor.AcceptsTab = true;
            Editor.TextWrapping = TextWrapping.Wrap;
            Editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            Editor.FontFamily = new FontFamily("Cascadia Code");
            Editor.BorderThickness = new Thickness(0);
            Editor.IsInactiveSelectionHighlightEnabled = true;
            Editor.IsManipulationEnabled = true;
            
            // Add scroll event handler
            Editor.Loaded += (s, e) => {
                var scrollViewer = GetScrollViewer(Editor);
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollChanged += Editor_ScrollChanged;
                }
            };
            
            // Apply theme
            Editor.Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            Editor.Foreground = (Brush)Application.Current.Resources["TextBrush"];
            Editor.CaretBrush = (Brush)Application.Current.Resources["TextBrush"];
            Editor.SelectionBrush = (Brush)Application.Current.Resources["ListItemSelectedBackgroundBrush"];
            Editor.SelectionTextBrush = (Brush)Application.Current.Resources["TextBrush"];
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

        private void Editor_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            _textHighlighter?.RefreshHighlights();
        }

        private void Editor_TextChanged(object sender, TextChangedEventArgs e)
        {
            IsModified = true;
            ContentChanged?.Invoke(this, EventArgs.Empty);
        }

        public EditorTab(string filePath) : this()
        {
            FilePath = filePath;
            // Don't load content immediately, just store the path
        }

        private void LoadFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    _cachedContent = File.ReadAllText(path);
                    Editor.Text = _cachedContent;
                    IsModified = false;
                    _isContentLoaded = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OnTabSelected()
        {
            if (!_isContentLoaded && !string.IsNullOrEmpty(FilePath))
            {
                LoadFile(FilePath);
            }
        }

        public void OnTabDeselected()
        {
            if (IsModified)
            {
                Save().ConfigureAwait(false);
            }
        }

        public async Task<bool> Save()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                return await SaveAs();
            }

            try
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Check if this file type should be versioned
                var versionManager = VersionManager.GetInstance();
                if (LibraryManager.Instance.IsVersionedFile(FilePath))
                {
                    // Save a version before writing new content
                    await versionManager.SaveVersion(FilePath);
                }

                // Write the new content
                File.WriteAllText(FilePath, Editor.Text);
                IsModified = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving file: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            var saveFileDialog = new SaveFileDialog();
            if (!string.IsNullOrEmpty(FilePath))
            {
                saveFileDialog.InitialDirectory = Path.GetDirectoryName(FilePath);
                saveFileDialog.FileName = Path.GetFileName(FilePath);
            }
            else if (!string.IsNullOrEmpty(newPath))
            {
                saveFileDialog.InitialDirectory = Path.GetDirectoryName(newPath);
                saveFileDialog.FileName = Path.GetFileName(newPath);
            }

            if (saveFileDialog.ShowDialog() == true)
            {
                FilePath = saveFileDialog.FileName;
                return await Save();
            }

            return false;
        }

        public void Reload()
        {
            if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
            {
                LoadFile(FilePath);
            }
        }

        private void UpdateTabHeader()
        {
            if (Parent is TabItem tabItem)
            {
                var headerText = Path.GetFileName(FilePath) ?? "Untitled";
                if (IsModified)
                {
                    headerText += "*";
                }

                // If the header is already a DockPanel, update the TextBlock within it
                if (tabItem.Header is DockPanel dockPanel)
                {
                    var textBlock = dockPanel.Children.OfType<TextBlock>().FirstOrDefault();
                    if (textBlock != null)
                    {
                        textBlock.Text = headerText;
                        return;
                    }
                }

                // Create a new DockPanel for the header
                var header = new DockPanel();

                // Add close button
                var closeButton = new Button
                {
                    Content = "✕",
                    Command = ApplicationCommands.Close,
                    CommandTarget = this,
                    Style = (Style)FindResource("TabCloseButtonStyle")
                };
                DockPanel.SetDock(closeButton, Dock.Right);
                header.Children.Add(closeButton);

                // Add text
                var headerTextBlock = new TextBlock
                {
                    Text = headerText,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0)
                };
                header.Children.Add(headerTextBlock);

                tabItem.Header = header;
            }
        }

        public string GetContent()
        {
            return Editor.Text;
        }

        public void SetContent(string content)
        {
            Editor.Text = content;
            IsModified = false;
        }

        public void ApplyChanges(string newContent)
        {
            if (string.IsNullOrEmpty(newContent))
                return;

            Editor.Text = newContent;
            IsModified = true;
        }

        public void ApplyTheme(bool isDarkMode)
        {
            // Apply theme-specific styles
            Editor.Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            Editor.Foreground = (Brush)Application.Current.Resources["TextBrush"];
            Editor.CaretBrush = (Brush)Application.Current.Resources["TextBrush"];
            Editor.SelectionBrush = (Brush)Application.Current.Resources["ListItemSelectedBackgroundBrush"];
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

        private async Task PlayAudioAsync(byte[] audioData)
        {
            try
            {
                // Create a temporary file in the system temp directory
                string tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + ".wav");
                
                // Write the audio data to the temp file asynchronously
                await Task.Run(() => File.WriteAllBytes(tempFile, audioData));
                
                // Clean up previous temp file if it exists
                if (!string.IsNullOrEmpty(_tempAudioFile) && File.Exists(_tempAudioFile))
                {
                    try
                    {
                        await Task.Run(() => File.Delete(_tempAudioFile));
                    }
                    catch { /* Ignore cleanup errors */ }
                }
                
                _tempAudioFile = tempFile;
                
                // Use TaskCompletionSource to handle MediaOpened event
                var mediaOpenedTcs = new TaskCompletionSource<bool>();
                
                EventHandler mediaOpenedHandler = null;
                mediaOpenedHandler = (s, e) =>
                {
                    _mediaPlayer.MediaOpened -= mediaOpenedHandler;
                    mediaOpenedTcs.SetResult(true);
                };
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _mediaPlayer.MediaOpened += mediaOpenedHandler;
                    
                    // Stop any current playback
                    _mediaPlayer.Stop();
                    
                    // Open and play the media file
                    _mediaPlayer.Open(new Uri(tempFile));
                });
                
                // Wait for media to open (with timeout)
                await Task.WhenAny(mediaOpenedTcs.Task, Task.Delay(2000));
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _mediaPlayer.Play();
                });
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error playing audio: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ResetTTSButtons();
                });
            }
        }

        private async Task PlayTextChunksAsync(string text)
        {
            var chunks = SplitIntoSentences(text);
            if (chunks.Count == 0) return;

            var audioQueue = new Queue<byte[]>();
            var fetchTasks = new List<Task>();
            const int prefetchCount = 3; // Number of chunks to fetch ahead
            
            async Task FetchChunkAsync(string chunk)
            {
                if (_ttsCancellationSource.Token.IsCancellationRequested) return;
                
                try
                {
                    var config = Configuration.Instance;
                    using (var client = new HttpClient())
                    {
                        var request = new
                        {
                            text = chunk.Trim(),
                            voice = config.TTSVoice ?? config.TTSDefaultVoice
                        };

                        var response = await client.PostAsync(
                            $"{config.TTSApiUrl.TrimEnd('/')}/tts",
                            new StringContent(JsonSerializer.Serialize(request), System.Text.Encoding.UTF8, "application/json"),
                            _ttsCancellationSource.Token
                        );

                        if (response.IsSuccessStatusCode)
                        {
                            var audioBytes = await response.Content.ReadAsByteArrayAsync();
                            lock (audioQueue)
                            {
                                audioQueue.Enqueue(audioBytes);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { /* Ignore cancellation */ }
                catch (Exception ex)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show($"Error during TTS: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    });
                }
            }

            // Start initial prefetch
            for (int i = 0; i < Math.Min(prefetchCount, chunks.Count); i++)
            {
                fetchTasks.Add(FetchChunkAsync(chunks[i]));
            }

            int nextChunkToFetch = prefetchCount;
            int currentChunkIndex = 0;

            while (currentChunkIndex < chunks.Count && !_ttsCancellationSource.Token.IsCancellationRequested)
            {
                // Start fetching next chunk if available
                if (nextChunkToFetch < chunks.Count)
                {
                    fetchTasks.Add(FetchChunkAsync(chunks[nextChunkToFetch]));
                    nextChunkToFetch++;
                }

                // Remove completed fetch tasks
                fetchTasks.RemoveAll(t => t.IsCompleted);

                // Try to get next audio chunk
                byte[] currentAudio = null;
                lock (audioQueue)
                {
                    if (audioQueue.Count > 0)
                    {
                        currentAudio = audioQueue.Dequeue();
                    }
                }

                if (currentAudio != null)
                {
                    var playbackCompletion = new TaskCompletionSource<bool>();
                    
                    EventHandler mediaEndedHandler = null;
                    mediaEndedHandler = (s, e) =>
                    {
                        _mediaPlayer.MediaEnded -= mediaEndedHandler;
                        playbackCompletion.SetResult(true);
                    };
                    
                    _mediaPlayer.MediaEnded += mediaEndedHandler;
                    
                    // Start playback without awaiting it
                    _ = PlayAudioAsync(currentAudio);
                    
                    // Don't wait for playback to complete before continuing the loop
                    _ = playbackCompletion.Task.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                MessageBox.Show($"Error during playback: {t.Exception?.InnerException?.Message}", 
                                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    });

                    currentChunkIndex++;
                }
                else
                {
                    // If no audio is available, wait a short time before checking again
                    await Task.Delay(50, _ttsCancellationSource.Token);
                }
            }

            // Wait for any remaining fetch tasks to complete
            await Task.WhenAll(fetchTasks);
            
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResetTTSButtons();
            });
        }

        private async void TTSPlayButton_Click(object sender, EventArgs e)
        {
            try
            {
                var config = Configuration.Instance;
                if (!config.EnableTTS || string.IsNullOrEmpty(config.TTSApiUrl))
                {
                    MessageBox.Show("Please configure TTS settings first.", "TTS Not Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var selectedText = Editor.SelectedText;
                var textToSpeak = string.IsNullOrEmpty(selectedText) ? Editor.Text : selectedText;

                if (string.IsNullOrWhiteSpace(textToSpeak))
                {
                    MessageBox.Show("No text to speak.", "Empty Text", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                _isPlaying = true;
                UpdateTTSState();
                _ttsCancellationSource = new CancellationTokenSource();
                await PlayTextChunksAsync(textToSpeak);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during TTS: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _isPlaying = false;
                UpdateTTSState();
            }
        }

        private void TTSStopButton_Click(object sender, EventArgs e)
        {
            StopTTS();
        }

        public void StopTTS()
        {
            if (_ttsClient != null)
            {
                _ttsClient.Stop();
                _textHighlighter?.ClearHighlights();
                _isPlaying = false;
                UpdateTTSState();
            }
        }

        private void ResetTTSButtons()
        {
            _isPlaying = false;
            UpdateTTSState();
        }

        private void MediaPlayer_MediaEnded(object sender, EventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ResetTTSButtons();
                if (!string.IsNullOrEmpty(_tempAudioFile) && File.Exists(_tempAudioFile))
                {
                    try
                    {
                        File.Delete(_tempAudioFile);
                        _tempAudioFile = null;
                    }
                    catch { /* Ignore cleanup errors */ }
                }
            }));
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

            if (string.IsNullOrEmpty(SearchBox.Text))
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
                var ranges = _searchResults.Select(i => (i, SearchBox.Text.Length)).ToList();
                _textHighlighter.HighlightRanges(ranges, Color.FromRgb(255, 235, 100));
                FindNext();
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
            Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(index));
            
            // Update all highlights with current one in a different color
            var ranges = _searchResults.Select((pos, i) => 
                (pos, SearchBox.Text.Length)).ToList();
            _textHighlighter.HighlightRanges(ranges, 
                _currentSearchIndex >= 0 ? Colors.Orange : Colors.Yellow);
        }

        public void ClearHighlights()
        {
            _textHighlighter?.ClearHighlights();
        }

        public void HighlightRanges(IEnumerable<(int start, int length)> ranges, Color color)
        {
            _textHighlighter?.HighlightRanges(ranges.ToList(), color);
        }

        public string GetTextToSpeak()
        {
            return !string.IsNullOrEmpty(Editor.SelectedText) 
                ? Editor.SelectedText 
                : Editor.Text;
        }

        private void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                StopTTS();
            }
            else
            {
                TTSPlayButton_Click(sender, e);
            }
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
                if (_isPlaying)
                {
                    tabItem.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0));
                }
                else
                {
                    tabItem.Background = null;
                }
                tabItem.Header = headerText;
            }
            
            // Force update of TTS button state
            if (TTSButton != null)
            {
                var style = TTSButton.Style;
                TTSButton.Style = null;
                TTSButton.Style = style;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 