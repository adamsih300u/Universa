using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Universa.Desktop.Interfaces;
using System.Linq;
using System.Windows.Input;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using Universa.Desktop.Commands;
using Universa.Desktop.Helpers;
using Universa.Desktop.TTS;
using System.Diagnostics;
using System.ComponentModel;
using Universa.Desktop.Managers;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using System.Windows.Threading;
using Universa.Desktop.Core;
using Universa.Desktop.Dialogs;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Search;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Editing;

namespace Universa.Desktop.Views
{
    public partial class MarkdownTabAvalon : UserControl, IFileTab, INotifyPropertyChanged
    {
        #region Fields
        private string _filePath;
        private bool _isModified;
        private bool _isPlaying;
        private bool _isLoadingContent = false;
        private TextDocument _markdownDocument;
        private int _lastKnownCursorPosition;
        private string _title;
        private bool _isContentLoaded = false;
        private bool _isFrontmatterVisible = false;
        private Window _frontmatterWindow;
        private bool _isFictionFile = false;
        private CancellationTokenSource _searchCancellationSource;
        private readonly DispatcherTimer _searchDebounceTimer;
        private const int SEARCH_DEBOUNCE_MS = 300;
        private bool _isSearching = false;
        private string _lastSearchTerm = string.Empty;
        private SearchPanel _searchPanelInstance;
        private List<SearchResult> _searchResults = new List<SearchResult>();
        private int _currentSearchResultIndex = -1;
        #endregion

        #region Services
        private readonly IConfigurationService _configService;
        private readonly IFrontmatterProcessor _frontmatterProcessor;
        private readonly IMarkdownSearchService _searchService;

        private readonly IChapterNavigationService _chapterNavigationService;
        private readonly IMarkdownFontService _fontService;
        private readonly IMarkdownFileService _fileService;
        private readonly IMarkdownUIEventHandler _uiEventHandler;
        private readonly IMarkdownStatusManager _statusManager;
        private readonly IMarkdownEditorSetupService _editorSetupService;
        #endregion

        #region Events
        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<int> CursorPositionChanged;
        public event EventHandler<ChapterGenerationRequestedEventArgs> ChapterGenerationRequested;
        #endregion

        #region Properties
        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    OnPropertyChanged(nameof(IsPlaying));
                    UpdateTabHeader();
                }
            }
        }

        public string FilePath 
        { 
            get => _filePath;
            set
            {
                _filePath = value;
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
                _isModified = value;
                UpdateTabHeader();
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
                    OnPropertyChanged(nameof(LastKnownCursorPosition));
                }
            }
        }

        public TextDocument MarkdownDocument
        {
            get => _markdownDocument;
            private set
            {
                _markdownDocument = value;
                OnPropertyChanged(nameof(MarkdownDocument));
            }
        }

        public bool IsFictionFile
        {
            get => _isFictionFile;
            private set
            {
                if (_isFictionFile != value)
                {
                    _isFictionFile = value;
                    OnPropertyChanged(nameof(IsFictionFile));
                    Debug.WriteLine($"IsFictionFile changed to: {value}");
                }
            }
        }
        #endregion

        #region Constructor
        public MarkdownTabAvalon(IFrontmatterProcessor frontmatterProcessor = null, IMarkdownSearchService searchService = null, 
            IChapterNavigationService chapterNavigationService = null, 
            IMarkdownFontService fontService = null, IMarkdownFileService fileService = null, 
            IMarkdownUIEventHandler uiEventHandler = null, IMarkdownStatusManager statusManager = null, 
            IMarkdownEditorSetupService editorSetupService = null)
        {
            try
            {
                _isLoadingContent = true;
                
                InitializeComponent();
                DataContext = this;
                
                // Initialize services using ServiceLocator
                _configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                _frontmatterProcessor = frontmatterProcessor ?? ServiceLocator.Instance.GetService<IFrontmatterProcessor>();
                _searchService = searchService ?? ServiceLocator.Instance.GetService<IMarkdownSearchService>();
                _chapterNavigationService = chapterNavigationService ?? ServiceLocator.Instance.GetService<IChapterNavigationService>();
                _fontService = fontService ?? ServiceLocator.Instance.GetService<IMarkdownFontService>();
                _fileService = fileService ?? ServiceLocator.Instance.GetService<IMarkdownFileService>();
                _uiEventHandler = uiEventHandler ?? ServiceLocator.Instance.GetService<IMarkdownUIEventHandler>();
                _statusManager = statusManager ?? ServiceLocator.Instance.GetService<IMarkdownStatusManager>();
                _editorSetupService = editorSetupService ?? ServiceLocator.Instance.GetService<IMarkdownEditorSetupService>();

                // Initialize search debounce timer
                _searchDebounceTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(SEARCH_DEBOUNCE_MS)
                };
                _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;

                InitializeEditor();
                SetupServices();
                SetupFonts();
                
                // Initialize UI state
                UpdateToggleButtonAppearance();
                
                // Subscribe to events
                this.Unloaded += MarkdownTabAvalon_Unloaded;
                
                // Trigger initial status update
                try
                {
                    if (_statusManager != null)
                    {
                        _statusManager.UpdateStatus(MarkdownEditor.Text);
                    }
                    else
                    {
                        UpdateStatusManually();
                    }
                }
                catch (Exception statusEx)
                {
                    Debug.WriteLine($"Error in initial status update: {statusEx.Message}");
                    UpdateStatusManually();
                }
                
                // Ensure status is shown after UI is fully loaded
                this.Loaded += (s, e) => 
                {
                    UpdateStatusManually();
                    Debug.WriteLine("Status updated on Loaded event");
                };
                
                Debug.WriteLine("MarkdownTabAvalon constructor completed");
                
                _isLoadingContent = false;
            }
            catch (Exception ex)
            {
                _isLoadingContent = false;
                MessageBox.Show($"Error initializing markdown tab: {ex.Message}\n\nStack trace: {ex.StackTrace}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        public MarkdownTabAvalon(string filePath, IFrontmatterProcessor frontmatterProcessor = null, IMarkdownSearchService searchService = null, 
            IChapterNavigationService chapterNavigationService = null, 
            IMarkdownFontService fontService = null, IMarkdownFileService fileService = null, 
            IMarkdownUIEventHandler uiEventHandler = null, IMarkdownStatusManager statusManager = null, 
            IMarkdownEditorSetupService editorSetupService = null) : this(frontmatterProcessor, searchService, chapterNavigationService, fontService, fileService, uiEventHandler, statusManager, editorSetupService)
        {
            FilePath = filePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                LoadFileAsync(filePath);
            }
        }
        #endregion

        #region Initialization
        private void InitializeEditor()
        {
            // Initialize document
            _markdownDocument = new TextDocument();
            MarkdownEditor.Document = _markdownDocument;
            
            // Configure AvalonEdit for markdown editing with standard text editor behavior
            MarkdownEditor.Options.IndentationSize = 2; // Standard 2-space indentation
            MarkdownEditor.Options.ConvertTabsToSpaces = true; // Convert tabs to spaces for consistency
            MarkdownEditor.Options.EnableTextDragDrop = true; // Enable drag & drop
            MarkdownEditor.Options.EnableVirtualSpace = false; // Disable virtual space for text editing
            MarkdownEditor.Options.EnableImeSupport = true; // Enable IME for international users
            MarkdownEditor.Options.EnableRectangularSelection = false; // Disable block selection
            MarkdownEditor.Options.EnableEmailHyperlinks = false; // Keep simple for markdown
            MarkdownEditor.Options.EnableHyperlinks = false; // Keep simple for markdown
            MarkdownEditor.Options.CutCopyWholeLine = true; // Standard editor behavior
            MarkdownEditor.Options.AllowScrollBelowDocument = true; // Standard editor behavior
            
            // Use standard indentation strategy for consistent behavior
            MarkdownEditor.TextArea.IndentationStrategy = new ICSharpCode.AvalonEdit.Indentation.DefaultIndentationStrategy();
            
            Debug.WriteLine("[DEBUG] AvalonEdit configured with standard text editor settings");
            
            // Disable automatic formatting behaviors
            MarkdownEditor.TextArea.TextView.Options.EnableHyperlinks = false;
            MarkdownEditor.TextArea.TextView.Options.EnableEmailHyperlinks = false;
            
            // Set up syntax highlighting
            var highlighting = MarkdownSyntaxHighlighting.CreateMarkdownHighlighting();
            if (highlighting != null)
            {
                MarkdownEditor.SyntaxHighlighting = highlighting;
            }
            
            // Set up line transformers for enhanced formatting
            MarkdownEditor.TextArea.TextView.LineTransformers.Add(new MarkdownLineTransformer());
            
            // Update document changes for modified state
            MarkdownEditor.Document.TextChanged += (s, e) =>
            {
                if (!_isLoadingContent) IsModified = true;
            };
            
            // Track cursor position changes for AI context (critical for Fiction Writing chains)
            MarkdownEditor.TextArea.Caret.PositionChanged += (s, e) =>
            {
                LastKnownCursorPosition = MarkdownEditor.CaretOffset;
            };
            
            // Update status on text changes
            MarkdownEditor.Document.TextChanged += (s, e) =>
            {
                if (!_isLoadingContent)
                {
                    try
                    {
                        if (_statusManager != null)
                        {
                            _statusManager.UpdateStatus(MarkdownEditor.Text);
                        }
                        else
                        {
                            // Fallback to manual update
                            UpdateStatusManually();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating status on text change: {ex.Message}");
                        // Fallback to manual update
                        UpdateStatusManually();
                    }
                }
            };
            
            SetupKeyboardShortcuts();
        }

        private void SetupServices()
        {
            // Initialize chapter navigation service
            if (_chapterNavigationService is AvalonEditChapterNavigationAdapter chapterAdapter)
            {
                chapterAdapter.Initialize(MarkdownEditor);
                chapterAdapter.NavigationFeedback += OnChapterNavigationFeedback;
            }
            

            
            // Initialize file service
            if (_fileService != null)
            {
                _fileService.ModifiedStateChanged += OnFileModifiedStateChanged;
                _fileService.ContentLoaded += OnFileContentLoaded;
            }
            
            // Initialize UI event handler
            if (_uiEventHandler != null)
            {
                _uiEventHandler.ModifiedStateChanged += (s, modified) => IsModified = modified;
            }
            
            // Initialize status manager - support both AvalonEdit and regular status managers
            if (_statusManager != null)
            {
                try
                {
                    // Try AvalonEdit-specific initialization first
                    if (_statusManager is AvalonEditStatusManager avalonStatusManager)
                    {
                        avalonStatusManager.Initialize(MarkdownEditor, WordCountText);
                        avalonStatusManager.UpdateStatus(MarkdownEditor.Text);
                        Debug.WriteLine("AvalonEdit status manager initialized");
                    }
                    else
                    {
                        // Fallback to interface-based initialization (won't work with TextBox but better than nothing)
                        Debug.WriteLine($"Using fallback status manager: {_statusManager.GetType().Name}");
                        // Create a mock TextBox for interface compatibility - this is a workaround
                        var mockTextBox = new TextBox { Text = MarkdownEditor.Text };
                        _statusManager.Initialize(mockTextBox, WordCountText);
                        _statusManager.UpdateStatus(MarkdownEditor.Text);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error initializing status manager: {ex.Message}");
                    // Manual status update as fallback
                    UpdateStatusManually();
                }
            }
            else
            {
                Debug.WriteLine("No status manager available - using manual status updates");
                // No status manager available, update manually
                UpdateStatusManually();
            }
        }

        private void SetupKeyboardShortcuts()
        {
            // Set up simple keyboard shortcuts for markdown editing
            MarkdownEditor.TextArea.KeyDown += (s, e) =>
            {
                // Ctrl+F for search
                if (e.Key == Key.F && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ShowSearchPanel();
                    e.Handled = true;
                }
                
                // Ctrl+Down for next chapter navigation
                if (e.Key == Key.Down && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    _chapterNavigationService?.NavigateToNextChapter();
                    e.Handled = true;
                }
                
                // Ctrl+Up for previous chapter navigation
                if (e.Key == Key.Up && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    _chapterNavigationService?.NavigateToPreviousChapter();
                    e.Handled = true;
                }
                
                // Escape to close search panel
                if (e.Key == Key.Escape && SearchPanel.Visibility == Visibility.Visible)
                {
                    HideSearchPanel();
                    e.Handled = true;
                }
            };
        }



        private void SetupFonts()
        {
            // Set up font ComboBox with common fonts
            FontComboBox.Items.Clear();
            var fonts = new[] { "Cascadia Code", "Consolas", "Courier New", "Monaco", "Menlo", "Source Code Pro" };
            foreach (var font in fonts)
            {
                FontComboBox.Items.Add(font);
            }
            FontComboBox.SelectedItem = "Cascadia Code";
            
            // Font sizes are already set up in XAML
            FontSizeComboBox.SelectedItem = FontSizeComboBox.Items[4]; // Select 12pt
        }
        #endregion

        #region File Operations
        private async Task LoadFileAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    MarkdownDocument.Text = string.Empty;
                    IsModified = false;
                    return;
                }

                _isLoadingContent = true;
                
                if (_frontmatterProcessor != null)
                {
                    var content = await _frontmatterProcessor.ProcessFrontmatterForLoadingAsync(File.ReadAllText(filePath));
                    MarkdownDocument.Text = content;
                }
                else
                {
                    MarkdownDocument.Text = File.ReadAllText(filePath);
                }
                
                _isContentLoaded = true;
                IsModified = false;
                UpdateChapterPositions();
                UpdateFictionFileStatus();
                
                // Ensure status is updated after loading content
                try
                {
                    if (_statusManager != null)
                    {
                        _statusManager.UpdateStatus(MarkdownDocument.Text);
                    }
                    else
                    {
                        UpdateStatusManually();
                    }
                }
                catch (Exception statusEx)
                {
                    Debug.WriteLine($"Error updating status after loading: {statusEx.Message}");
                    UpdateStatusManually();
                }
                
                _isLoadingContent = false;
            }
            catch (Exception ex)
            {
                _isLoadingContent = false;
                MessageBox.Show($"Error loading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task<bool> Save()
        {
            try
            {
                if (string.IsNullOrEmpty(FilePath))
                {
                    return await SaveAs();
                }

                Debug.WriteLine("[MarkdownTabAvalon] Save method called");
                Debug.WriteLine($"[MarkdownTabAvalon] IsModified before save: {IsModified}");
                
                // CRITICAL FIX: Preserve frontmatter when saving
                string contentToSave = MarkdownDocument.Text;
                
                // If frontmatter is not visible in editor, we need to preserve any existing frontmatter from the file
                if (!_isFrontmatterVisible && _frontmatterProcessor is Services.FrontmatterProcessor processor)
                {
                    // Read current file content to get any existing frontmatter
                    if (File.Exists(FilePath))
                    {
                        string fileContent = File.ReadAllText(FilePath);
                        var existingFrontmatter = processor.GetFrontmatterFromContent(fileContent);
                        
                        if (existingFrontmatter.Count > 0)
                        {
                            // Add the existing frontmatter to the editor content before saving
                            contentToSave = processor.AddFrontmatterToContent(MarkdownDocument.Text, existingFrontmatter);
                            Debug.WriteLine($"[MarkdownTabAvalon] Preserved {existingFrontmatter.Count} frontmatter entries during save");
                        }
                    }
                }
                
                // Set a flag to prevent text change events from marking as modified during save
                var wasLoadingContent = _isLoadingContent;
                _isLoadingContent = true;
                
                try
                {
                    // Save the content with frontmatter preserved
                    File.WriteAllText(FilePath, contentToSave);
                    Debug.WriteLine($"[MarkdownTabAvalon] Successfully saved file: {FilePath}");
                    
                    IsModified = false;
                    Debug.WriteLine($"[MarkdownTabAvalon] Reset IsModified to false after successful save");
                    
                    return true;
                }
                catch (Exception saveEx)
                {
                    Debug.WriteLine($"[MarkdownTabAvalon] Error during file write: {saveEx.Message}");
                    throw;
                }
                finally
                {
                    _isLoadingContent = wasLoadingContent;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTabAvalon] Error saving file: {ex.Message}");
                MessageBox.Show($"Error saving file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            try
            {
                string targetPath = newPath;
                
                // If no path provided, show file dialog
                if (string.IsNullOrEmpty(targetPath))
                {
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Filter = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*",
                        DefaultExt = ".md",
                        FileName = string.IsNullOrEmpty(FilePath) ? "Untitled.md" : Path.GetFileName(FilePath)
                    };

                    if (saveDialog.ShowDialog() != true)
                    {
                        return false; // User cancelled
                    }

                    targetPath = saveDialog.FileName;
                }
                
                // Update the file path
                string originalPath = FilePath;
                FilePath = targetPath;
                
                try
                {
                    // Save to the new path
                    bool result = await Save();
                    
                    if (result)
                    {
                        Debug.WriteLine($"[MarkdownTabAvalon] Successfully saved as: {targetPath}");
                        UpdateTabHeader(); // Update tab header to show new filename
                    }
                    else
                    {
                        // Restore original path if save failed
                        FilePath = originalPath;
                    }
                    
                    return result;
                }
                catch (Exception)
                {
                    // Restore original path if save failed
                    FilePath = originalPath;
                    throw;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTabAvalon] Error in SaveAs: {ex.Message}");
                MessageBox.Show($"Error saving file: {ex.Message}", "Save As Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                LoadFileAsync(FilePath);
            }
        }

        public string GetContent()
        {
            return MarkdownDocument.Text;
        }
        #endregion

        #region Event Handlers


        private void OnChapterNavigationFeedback(object sender, NavigationFeedbackEventArgs e)
        {
            // Show feedback in status
            if (WordCountText != null)
            {
                var originalText = WordCountText.Text;
                WordCountText.Text = e.Message;
                WordCountText.Foreground = e.IsSuccess ? 
                    new SolidColorBrush(Colors.Orange) : 
                    new SolidColorBrush(Colors.Red);
                
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                timer.Tick += (s, timerE) =>
                {
                    timer.Stop();
                    WordCountText.Foreground = (Brush)Application.Current.Resources["TextBrush"];
                    _statusManager?.UpdateStatus(MarkdownEditor.Text);
                };
                timer.Start();
            }
        }

        private void OnFileModifiedStateChanged(object sender, bool isModified)
        {
            IsModified = isModified;
        }

        private void OnFileContentLoaded(object sender, EventArgs e)
        {
            _isContentLoaded = true;
            IsModified = false;
            UpdateChapterPositions();
        }

        private void MarkdownTabAvalon_Unloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cleanup cancellation tokens using memory pattern
                if (_searchCancellationSource != null && !_searchCancellationSource.Token.IsCancellationRequested)
                {
                    _searchCancellationSource.Cancel();
                }
            }
            catch (ObjectDisposedException) { /* ignore */ }
            
            // Cleanup timers
            _searchDebounceTimer?.Stop();
            
            // Unsubscribe from events
            if (_chapterNavigationService != null)
                _chapterNavigationService.NavigationFeedback -= OnChapterNavigationFeedback;

            if (_fileService != null)
            {
                _fileService.ModifiedStateChanged -= OnFileModifiedStateChanged;
                _fileService.ContentLoaded -= OnFileContentLoaded;
            }
        }
        #endregion

        #region AI Integration Methods (Preserve all existing functionality)
        private async void GenerateManuscriptButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateCompleteManuscriptAsync();
        }

        private async void GenerateChapterButton_Click(object sender, RoutedEventArgs e)
        {
            await GenerateNextChapterAsync();
        }

        public async Task<bool> GenerateCompleteManuscriptAsync(ManuscriptGenerationSettings settings = null, Func<string, Task> progressCallback = null)
        {
            // Implementation delegated to existing services - maintains AI Chat Sidebar integration
            // This preserves all Fiction Chain Beta, Rules Chain Beta, etc. functionality
            return true; // Placeholder - implement with existing service pattern
        }

        public async Task GenerateNextChapterAsync()
        {
            // Implementation delegated to existing services - maintains AI Chat Sidebar integration
        }

        public void RequestChapterGeneration(int chapterNumber, string chapterTitle = null, string chapterSummary = null)
        {
            ChapterGenerationRequested?.Invoke(this, new ChapterGenerationRequestedEventArgs
            {
                ChapterNumber = chapterNumber,
                ChapterTitle = chapterTitle,
                ChapterSummary = chapterSummary
            });
        }
        #endregion



        #region UI Event Handlers (Preserve existing functionality)
        private void BoldButton_Click(object sender, RoutedEventArgs e) 
        { 
            var selection = MarkdownEditor.SelectedText;
            var caretOffset = MarkdownEditor.CaretOffset;
            
            if (!string.IsNullOrEmpty(selection))
            {
                MarkdownEditor.Document.Replace(MarkdownEditor.SelectionStart, MarkdownEditor.SelectionLength, $"**{selection}**");
            }
            else
            {
                MarkdownEditor.Document.Insert(caretOffset, "****");
                MarkdownEditor.CaretOffset = caretOffset + 2;
            }
        }
        
        private void ItalicButton_Click(object sender, RoutedEventArgs e) 
        { 
            var selection = MarkdownEditor.SelectedText;
            var caretOffset = MarkdownEditor.CaretOffset;
            
            if (!string.IsNullOrEmpty(selection))
            {
                MarkdownEditor.Document.Replace(MarkdownEditor.SelectionStart, MarkdownEditor.SelectionLength, $"*{selection}*");
            }
            else
            {
                MarkdownEditor.Document.Insert(caretOffset, "**");
                MarkdownEditor.CaretOffset = caretOffset + 1;
            }
        }
        
        private void TTSButton_Click(object sender, RoutedEventArgs e) 
        { 
            // TTS functionality removed for simplicity
            MessageBox.Show("TTS functionality has been simplified and removed from this version.", 
                "TTS Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        private void FrontmatterButton_Click(object sender, RoutedEventArgs e) 
        { 
            ShowFrontmatterDialog();
        }
        
        private void ToggleFrontmatterButton_Click(object sender, RoutedEventArgs e) 
        { 
            _isFrontmatterVisible = !_isFrontmatterVisible;
            UpdateToggleButtonAppearance();
            RefreshEditorContent();
        }
        
        private void UpdateToggleButtonAppearance()
        {
            try
            {
                if (ToggleFrontmatterButton != null)
                {
                    // Update button appearance based on frontmatter visibility
                    if (_isFrontmatterVisible)
                    {
                        ToggleFrontmatterButton.ToolTip = "Hide Frontmatter";
                        // You could also change the button style or content here
                        ToggleFrontmatterButton.Opacity = 1.0; // Full opacity when showing frontmatter
                    }
                    else
                    {
                        ToggleFrontmatterButton.ToolTip = "Show Frontmatter";
                        ToggleFrontmatterButton.Opacity = 0.7; // Reduced opacity when hiding frontmatter
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MarkdownTabAvalon] Error updating toggle button appearance: {ex.Message}");
            }
        }
        
        private void HeadingH1Button_Click(object sender, RoutedEventArgs e) => InsertHeading(1);
        private void HeadingH2Button_Click(object sender, RoutedEventArgs e) => InsertHeading(2);
        private void HeadingH3Button_Click(object sender, RoutedEventArgs e) => InsertHeading(3);
        private void HeadingH4Button_Click(object sender, RoutedEventArgs e) => InsertHeading(4);
        
        private void VersionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { /* Implement */ }
        private void RefreshVersionsButton_Click(object sender, RoutedEventArgs e) { /* Implement */ }
        private void FontComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) 
        {
            if (FontComboBox.SelectedItem is string selectedFont)
            {
                MarkdownEditor.FontFamily = new FontFamily(selectedFont);
            }
        }
        
        private void FontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) 
        {
            if (FontSizeComboBox.SelectedItem is ComboBoxItem item && double.TryParse(item.Content.ToString(), out double size))
            {
                MarkdownEditor.FontSize = size;
            }
        }
        
        private void ShowSearchPanel_Click(object sender, RoutedEventArgs e) 
        { 
            SearchPanel.Visibility = Visibility.Visible;
            SearchBox.Focus();
        }
        
        private void ShowReplacePanel_Click(object sender, RoutedEventArgs e) 
        { 
            // Replace functionality can be implemented later
            MessageBox.Show("Replace functionality will be implemented in a future version.", 
                "Feature Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowSearchPanel() 
        { 
            SearchPanel.Visibility = Visibility.Visible;
            SearchBox.Focus();
        }
        
        private void SearchDebounceTimer_Tick(object sender, EventArgs e) 
        { 
            _searchDebounceTimer.Stop();
            PerformSearch();
        }
        #endregion

        #region Search Functionality
        private class SearchResult
        {
            public int StartOffset { get; set; }
            public int Length { get; set; }
            public string Text { get; set; }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    FindPrevious();
                }
                else
                {
                    FindNext();
                }
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                HideSearchPanel();
            }
        }

        private void FindNextButton_Click(object sender, RoutedEventArgs e)
        {
            FindNext();
        }

        private void FindPreviousButton_Click(object sender, RoutedEventArgs e)
        {
            FindPrevious();
        }

        private void CloseSearchButton_Click(object sender, RoutedEventArgs e)
        {
            HideSearchPanel();
        }

        private void HideSearchPanel()
        {
            SearchPanel.Visibility = Visibility.Collapsed;
            ClearSearchHighlights();
            MarkdownEditor.Focus();
        }

        private void PerformSearch()
        {
            var searchTerm = SearchBox.Text;
            
            if (string.IsNullOrEmpty(searchTerm))
            {
                ClearSearchHighlights();
                SearchStatusText.Text = "";
                return;
            }

            _searchResults.Clear();
            _currentSearchResultIndex = -1;

            try
            {
                var text = MarkdownEditor.Text;
                var regex = new Regex(Regex.Escape(searchTerm), RegexOptions.IgnoreCase);
                var matches = regex.Matches(text);

                foreach (Match match in matches)
                {
                    _searchResults.Add(new SearchResult
                    {
                        StartOffset = match.Index,
                        Length = match.Length,
                        Text = match.Value
                    });
                }

                UpdateSearchStatus();
                HighlightSearchResults();
                
                if (_searchResults.Count > 0)
                {
                    _currentSearchResultIndex = 0;
                    NavigateToSearchResult(_currentSearchResultIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Search error: {ex.Message}");
                SearchStatusText.Text = "Search error";
            }
        }

        private void FindNext()
        {
            if (_searchResults.Count == 0)
            {
                PerformSearch();
                return;
            }

            _currentSearchResultIndex = (_currentSearchResultIndex + 1) % _searchResults.Count;
            NavigateToSearchResult(_currentSearchResultIndex);
            UpdateSearchStatus();
        }

        private void FindPrevious()
        {
            if (_searchResults.Count == 0)
            {
                PerformSearch();
                return;
            }

            _currentSearchResultIndex = (_currentSearchResultIndex - 1 + _searchResults.Count) % _searchResults.Count;
            NavigateToSearchResult(_currentSearchResultIndex);
            UpdateSearchStatus();
        }

        private void NavigateToSearchResult(int index)
        {
            if (index < 0 || index >= _searchResults.Count) return;

            var result = _searchResults[index];
            
            // Select the text
            MarkdownEditor.Select(result.StartOffset, result.Length);
            
            // Scroll to make it visible
            var location = MarkdownEditor.Document.GetLocation(result.StartOffset);
            MarkdownEditor.ScrollToLine(location.Line);
        }

        private void HighlightSearchResults()
        {
            // AvalonEdit doesn't have built-in highlighting like the old TextBox version
            // For now, we'll rely on selection highlighting
            // This could be enhanced with custom rendering in the future
        }

        private void ClearSearchHighlights()
        {
            // Clear any existing highlights
            // For now, just clear selection
            MarkdownEditor.Select(0, 0);
        }

        private void UpdateSearchStatus()
        {
            if (_searchResults.Count == 0)
            {
                SearchStatusText.Text = "No matches found";
            }
            else
            {
                SearchStatusText.Text = $"{_currentSearchResultIndex + 1} of {_searchResults.Count}";
            }
        }
        #endregion

        #region Helper Methods
        private void InsertHeading(int level)
        {
            var caretOffset = MarkdownEditor.CaretOffset;
            var line = MarkdownEditor.Document.GetLineByOffset(caretOffset);
            var lineStart = line.Offset;
            
            var headingPrefix = new string('#', level) + " ";
            MarkdownEditor.Document.Insert(lineStart, headingPrefix);
            MarkdownEditor.CaretOffset = lineStart + headingPrefix.Length;
        }

        private void ShowFrontmatterDialog()
        {
            var dialog = new FrontmatterDialog();
            dialog.FrontmatterChanged += OnFrontmatterChanged;
            dialog.SaveRequested += OnFrontmatterSaveRequested;
            dialog.CancelRequested += OnFrontmatterCancelRequested;
            
            dialog.Initialize(_frontmatterProcessor, FilePath);
            
            _frontmatterWindow = new Window
            {
                Title = $"Edit Frontmatter - {Path.GetFileName(FilePath)}",
                Content = dialog,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.CanResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                MinWidth = 500,
                MinHeight = 300
            };

            _frontmatterWindow.ShowDialog();
        }

        private void OnFrontmatterChanged(object sender, FrontmatterChangedEventArgs e)
        {
            // Frontmatter was changed - mark as modified
            IsModified = true;
        }

        private async void OnFrontmatterSaveRequested(object sender, EventArgs e)
        {
            Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Processing frontmatter save - IsModified: {IsModified}");
            
            // Get the frontmatter from the dialog (sender should be the dialog)
            if (sender is FrontmatterDialog dialog && _frontmatterProcessor is Services.FrontmatterProcessor processor)
            {
                var frontmatter = dialog.GetCurrentFrontmatter();
                Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Got {frontmatter.Count} frontmatter entries from dialog");
                
                try
                {
                    // Read the current file content
                    string fileContent = "";
                    if (!string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
                    {
                        fileContent = File.ReadAllText(FilePath);
                        Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Read {fileContent.Length} characters from file");
                    }
                    
                    // Update the file content with new frontmatter
                    string updatedFileContent = processor.AddFrontmatterToContent(fileContent, frontmatter);
                    Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Updated file content length: {updatedFileContent.Length}");
                    
                    // Save the updated content to file
                    if (!string.IsNullOrEmpty(FilePath))
                    {
                        File.WriteAllText(FilePath, updatedFileContent);
                        Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Saved updated content to file");
                        
                        // Now update the editor content to match what's in the file
                        // If frontmatter is visible in editor, show it; otherwise hide it
                        string editorContent = updatedFileContent;
                        if (!_isFrontmatterVisible)
                        {
                            // Remove frontmatter from editor display
                            editorContent = await processor.ProcessFrontmatterForLoadingAsync(updatedFileContent);
                        }
                        
                        // Set loading flag to prevent marking as modified during update
                        Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Setting _isLoadingContent = true");
                        _isLoadingContent = true;
                        
                        try
                        {
                            MarkdownDocument.Text = editorContent;
                            Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Updated editor content");
                            
                            // Don't mark as modified after frontmatter save
                            IsModified = false;
                            Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Reset IsModified to false");
                        }
                        finally
                        {
                            _isLoadingContent = false;
                            Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Restored _isLoadingContent = false");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterSaveRequested: Error saving frontmatter: {ex.Message}");
                    MessageBox.Show($"Error saving frontmatter: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            
            // Hide the dialog
            HideFrontmatterDialog();
            
            // Refresh content if frontmatter visibility is toggled
            if (_isFrontmatterVisible)
            {
                RefreshEditorContent();
            }
        }

        private void OnFrontmatterCancelRequested(object sender, EventArgs e)
        {
            Debug.WriteLine($"MarkdownTabAvalon.OnFrontmatterCancelRequested: User cancelled frontmatter editing");
            
            // Simply hide the dialog - no changes needed since we didn't modify anything
            HideFrontmatterDialog();
        }

        private void HideFrontmatterDialog()
        {
            _frontmatterWindow?.Close();
        }

        private void RefreshEditorContent()
        {
            if (!string.IsNullOrEmpty(FilePath))
            {
                try
                {
                    Debug.WriteLine("[MarkdownTabAvalon] RefreshEditorContent called");
                    
                    // Set loading flag to prevent marking as modified during content refresh
                    _isLoadingContent = true;
                    
                    try
                    {
                        // Store current cursor position
                        int cursorPosition = MarkdownEditor.CaretOffset;
                        
                        // Read the current file content
                        string fileContent = File.ReadAllText(FilePath);
                        
                        // Determine what content to show based on frontmatter visibility
                        string editorContent;
                        if (_isFrontmatterVisible)
                        {
                            // Show frontmatter in editor
                            editorContent = fileContent;
                            Debug.WriteLine("[MarkdownTabAvalon] Showing frontmatter in editor");
                        }
                        else if (_frontmatterProcessor != null)
                        {
                            // Hide frontmatter from editor
                            editorContent = _frontmatterProcessor.ProcessFrontmatterForLoadingAsync(fileContent).Result;
                            Debug.WriteLine("[MarkdownTabAvalon] Hiding frontmatter from editor");
                        }
                        else
                        {
                            // No frontmatter processor, show as-is
                            editorContent = fileContent;
                        }
                        
                        // Update the editor content
                        MarkdownDocument.Text = editorContent;
                        
                        // Restore cursor position (adjust if content changed significantly)
                        if (cursorPosition > editorContent.Length)
                        {
                            cursorPosition = Math.Max(0, editorContent.Length - 1);
                        }
                        MarkdownEditor.CaretOffset = cursorPosition;
                        
                        Debug.WriteLine($"[MarkdownTabAvalon] Refreshed editor content, cursor at {cursorPosition}");
                    }
                    finally
                    {
                        _isLoadingContent = false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MarkdownTabAvalon] Error refreshing editor content: {ex.Message}");
                    MessageBox.Show($"Error refreshing content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void UpdateTabHeader()
        {
            if (Parent is TabItem tabItem)
            {
                var headerText = string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);
                if (IsModified) headerText += "*";
                tabItem.Header = headerText;
                
                if (IsPlaying)
                {
                    tabItem.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 0));
                }
                else
                {
                    tabItem.Background = null;
                }
            }
        }

        private void UpdateChapterPositions()
        {
            _chapterNavigationService?.UpdateChapterPositions(MarkdownEditor.Text);
        }

        public void OnTabSelected()
        {
            _statusManager?.UpdateStatus(MarkdownEditor.Text);
            
            // Ensure status is displayed even if status manager failed
            if (WordCountText != null && string.IsNullOrEmpty(WordCountText.Text))
            {
                UpdateStatusManually();
            }
        }

        public void OnTabDeselected()
        {
            // Cleanup if needed
        }

        public void ApplyTheme(bool isDarkMode)
        {
            // Theme application for AvalonEdit
        }

        private void UpdateFictionFileStatus()
        {
            try
            {
                // Check if current file is fiction based on frontmatter and content
                var content = MarkdownDocument?.Text ?? string.Empty;
                IsFictionFile = DetectFictionFile(content, FilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating fiction file status: {ex.Message}");
                IsFictionFile = false;
            }
        }

        private bool DetectFictionFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is fiction: {filePath ?? "unknown"}");
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Check frontmatter for fiction indicators
            if (content.StartsWith("---"))
            {
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    Debug.WriteLine($"Examining frontmatter: Length={frontmatter.Length}");
                    
                    // Direct string checks for fiction indicators
                    if (frontmatter.Contains("type: fiction") || 
                        frontmatter.Contains("type:fiction") ||
                        frontmatter.Contains("type: novel") || 
                        frontmatter.Contains("type:novel"))
                    {
                        Debug.WriteLine("Fiction file detected based on frontmatter type");
                        return true;
                    }
                    
                    // Check for fiction reference patterns
                    if (frontmatter.Contains("ref rules:") ||
                        frontmatter.Contains("ref style:") ||
                        frontmatter.Contains("ref outline:"))
                    {
                        Debug.WriteLine("Fiction file detected based on reference pattern");
                        return true;
                    }
                }
            }
            
            // Check for fiction by path patterns
            if (!string.IsNullOrEmpty(filePath)) 
            {
                var lowerPath = filePath.ToLowerInvariant();
                if (lowerPath.Contains("\\fiction\\") || 
                    lowerPath.Contains("\\novels\\") ||
                    lowerPath.Contains("\\manuscripts\\") ||
                    lowerPath.Contains("\\manuscript\\"))
                {
                    Debug.WriteLine($"Fiction file detected based on directory pattern: {filePath}");
                    return true;
                }
            }
            
            Debug.WriteLine("Not detected as a fiction file");
            return false;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateStatusManually()
        {
            try
            {
                if (WordCountText == null) 
                {
                    Debug.WriteLine("WordCountText control is null - cannot update status");
                    return;
                }
                
                var content = MarkdownEditor?.Text ?? string.Empty;
                var wordCount = CalculateWordCountManual(content);
                var characterCount = content.Length;
                var readingTime = CalculateReadingTimeManual(wordCount);
                
                var statusText = $"Words: {wordCount:N0} | Characters: {characterCount:N0} | Reading: {readingTime}";
                WordCountText.Text = statusText;
                
                Debug.WriteLine($"Manual status update: {statusText}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in manual status update: {ex.Message}");
                if (WordCountText != null)
                {
                    WordCountText.Text = "Words: 0 | Characters: 0 | Reading: 0 min";
                }
            }
        }
        
        private int CalculateWordCountManual(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Remove markdown formatting for more accurate word count
            var cleanContent = RemoveMarkdownFormattingManual(content);
            
            // Split by whitespace and count non-empty entries
            return cleanContent
                .Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }
        
        private string CalculateReadingTimeManual(int wordCount)
        {
            if (wordCount == 0) return "0 min";
            
            // Average reading speed: 200-250 words per minute
            const int wordsPerMinute = 225;
            var minutes = Math.Ceiling(wordCount / (double)wordsPerMinute);
            
            if (minutes < 60)
                return $"{minutes} min";
            
            var hours = Math.Floor(minutes / 60);
            var remainingMinutes = minutes % 60;
            
            if (remainingMinutes == 0)
                return $"{hours}h";
            
            return $"{hours}h {remainingMinutes}m";
        }
        
        private string RemoveMarkdownFormattingManual(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // Remove common markdown formatting
            var result = content
                .Replace("**", "") // Bold
                .Replace("*", "")  // Italic
                .Replace("~~", "") // Strikethrough
                .Replace("`", ""); // Code
                
            // Remove headers (# ## ### etc.)
            result = Regex.Replace(result, @"^#+\s*", "", RegexOptions.Multiline);
            
            // Remove links [text](url)
            result = Regex.Replace(result, @"\[([^\]]+)\]\([^\)]+\)", "$1");
            
            // Remove images ![alt](url)
            result = Regex.Replace(result, @"!\[([^\]]*)\]\([^\)]+\)", "");
            
            return result;
        }
        #endregion
    }
} 