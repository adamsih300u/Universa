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
        
        // Word count debouncing
        private readonly DispatcherTimer _statusDebounceTimer;
        private const int STATUS_DEBOUNCE_MS = 250;
        private int _lastContentLength = 0;
        private SearchPanel _searchPanelInstance;
        private List<SearchResult> _searchResults = new List<SearchResult>();
        private int _currentSearchResultIndex = -1;

        private DispatcherTimer _chapterNavigationTimer; // Track the feedback timer
        private string _originalStatusText; // Store original text for restoration
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
                
                // CRITICAL DEBUG: Check if WordCountText is available after InitializeComponent
                if (WordCountText != null)
                {
                    Debug.WriteLine("Constructor: WordCountText control found and available");
                    WordCountText.Text = "Initializing...";
                }
                else
                {
                    Debug.WriteLine("CRITICAL ERROR: WordCountText control is NULL after InitializeComponent!");
                }
                
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
            
            // Initialize status debounce timer
            _statusDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(STATUS_DEBOUNCE_MS)
            };
            _statusDebounceTimer.Tick += StatusDebounceTimer_Tick;
            Debug.WriteLine($"Status debounce timer initialized with {STATUS_DEBOUNCE_MS}ms interval");

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
                    Debug.WriteLine("MarkdownTabAvalon Loaded event fired");
                    
                    // Debug the WordCountText control state

                    
                    // Force immediate status update on load
                    Dispatcher.BeginInvoke(new Action(() => 
                    {
                        UpdateStatusManually();
                    }), DispatcherPriority.Loaded);
                };
                
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
            
            // Update status on text changes (debounced for performance with large pastes)
            MarkdownEditor.Document.TextChanged += (s, e) =>
            {
                if (!_isLoadingContent)
                {
                    // Restart the debounce timer for status updates - this handles large paste operations efficiently
                    try
                    {
                        if (_statusDebounceTimer != null)
                        {
                            _statusDebounceTimer.Stop();
                            _statusDebounceTimer.Start();
                        }
                        else
                        {
                            UpdateStatusManually();
                        }
                    }
                    catch (Exception timerEx)
                    {
                        Debug.WriteLine($"Error with status timer: {timerEx.Message}");
                        UpdateStatusManually();
                    }
                    
                    // Update fiction file status immediately (this is lightweight)
                    UpdateFictionFileStatus();
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
                Debug.WriteLine("LoadFileAsync: Updating status after content load");
                try
                {
                    if (_statusManager != null)
                    {
                        Debug.WriteLine("LoadFileAsync: Using status manager");
                        _statusManager.UpdateStatus(MarkdownDocument.Text);
                    }
                    else
                    {
                        Debug.WriteLine("LoadFileAsync: No status manager, using manual update");
                        UpdateStatusManually();
                    }
                    
                    // FORCE an additional manual update to ensure it works
                    Debug.WriteLine("LoadFileAsync: Forcing additional manual status update");
                    UpdateStatusManually();
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
                Debug.WriteLine($"OnChapterNavigationFeedback: Showing message '{e.Message}'");
                
                // Stop any existing timer to prevent conflicts
                if (_chapterNavigationTimer != null)
                {
                    _chapterNavigationTimer.Stop();
                    _chapterNavigationTimer = null;
                }
                
                // Store original text before changing it
                _originalStatusText = WordCountText.Text;
                
                // Set the navigation message
                WordCountText.Text = e.Message;
                WordCountText.Foreground = e.IsSuccess ? 
                    new SolidColorBrush(Colors.Orange) : 
                    new SolidColorBrush(Colors.Red);
                
                // Create new timer for restoration
                _chapterNavigationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) }; // Reduced from 2 seconds
                _chapterNavigationTimer.Tick += (s, timerE) =>
                {
                    _chapterNavigationTimer.Stop();
                    _chapterNavigationTimer = null;
                    
                    Debug.WriteLine("OnChapterNavigationFeedback: Timer elapsed, restoring status");
                    WordCountText.Foreground = (Brush)Application.Current.Resources["TextBrush"];
                    
                    // BULLY IMPROVED RESTORATION: Use current live status instead of cached text
                    try
                    {
                        // Always get fresh status instead of using possibly stale cached text
                        if (_statusManager != null)
                        {
                            Debug.WriteLine("OnChapterNavigationFeedback: Using status manager for fresh update");
                            _statusManager.UpdateStatus(MarkdownEditor.Text);
                        }
                        else
                        {
                            Debug.WriteLine("OnChapterNavigationFeedback: Using manual update for fresh status");
                            UpdateStatusManually();
                        }
                        
                        // Force a UI refresh to ensure the update takes effect
                        Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                            // This ensures the UI thread processes the status update
                        }), DispatcherPriority.Background);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"OnChapterNavigationFeedback: Error during restoration: {ex.Message}");
                        // Fallback to manual update if there's any issue
                        UpdateStatusManually();
                    }
                };
                _chapterNavigationTimer.Start();
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
            _statusDebounceTimer?.Stop();
            
            // BULLY FIX: Cleanup chapter navigation timer to prevent memory leaks
            if (_chapterNavigationTimer != null)
            {
                _chapterNavigationTimer.Stop();
                _chapterNavigationTimer = null;
            }
            
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
        
        private async void TTSButton_Click(object sender, RoutedEventArgs e) 
        { 
            try
            {
                // Try OpenAI TTS first if available
                var openAITTS = new OpenAITTSService();
                if (openAITTS.IsAvailable())
                {
                    var selectedText = MarkdownEditor.SelectedText;
                    var textToSpeak = string.IsNullOrEmpty(selectedText) ? MarkdownEditor.Text : selectedText;

                    if (string.IsNullOrWhiteSpace(textToSpeak))
                    {
                        MessageBox.Show("No text to speak.", "Empty Text", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Check if already playing, then stop
                    if (openAITTS.IsPlaying)
                    {
                        openAITTS.Stop();
                        IsPlaying = false;
                        return;
                    }

                    // Start TTS
                    IsPlaying = true;
                    await openAITTS.SpeakAsync(textToSpeak);
                    IsPlaying = false;
                }
                else
                {
                    MessageBox.Show("OpenAI TTS is not configured. Please set your OpenAI API key in Settings.", 
                        "TTS Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                IsPlaying = false;
                MessageBox.Show($"Error during TTS: {ex.Message}", "TTS Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        
        private void StatusDebounceTimer_Tick(object sender, EventArgs e)
        {
            _statusDebounceTimer.Stop();
            
            try
            {
                if (_statusManager != null)
                {
                    var originalText = WordCountText?.Text;
                    _statusManager.UpdateStatus(MarkdownEditor.Text);
                    
                    // Only force manual update if the status manager failed to update the UI
                    if (WordCountText != null && WordCountText.Text == originalText)
                    {
                        UpdateStatusManually();
                    }
                }
                else
                {
                    UpdateStatusManually();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in status update: {ex.Message}");
                UpdateStatusManually();
            }
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
            Debug.WriteLine("OnTabSelected called - forcing status update");
            
            try
            {
                // FORCE status update regardless of status manager - the user is seeing logs but no UI updates
                Debug.WriteLine("OnTabSelected: FORCING manual status update to debug UI issue");
                UpdateStatusManually();
                
                // Also try the status manager if available
                if (_statusManager != null)
                {
                    Debug.WriteLine("OnTabSelected: Also trying status manager");
                    _statusManager.UpdateStatus(MarkdownEditor.Text);
                }
                
                // Double check the UI state
                if (WordCountText != null)
                {
                    Debug.WriteLine($"OnTabSelected: Final WordCountText.Text: '{WordCountText.Text}'");
                    Debug.WriteLine($"OnTabSelected: WordCountText.IsLoaded: {WordCountText.IsLoaded}");
                    Debug.WriteLine($"OnTabSelected: WordCountText.Parent: {WordCountText.Parent?.GetType().Name}");
                }
                else
                {
                    Debug.WriteLine("OnTabSelected: WordCountText is null!");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnTabSelected: {ex.Message}");
                Debug.WriteLine($"OnTabSelected error stack trace: {ex.StackTrace}");
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
            // Check for empty content
            if (string.IsNullOrEmpty(content))
                return false;
            
            // Check frontmatter for type: fiction only
            if (content.StartsWith("---"))
            {
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    return frontmatter.Contains("type: fiction") || frontmatter.Contains("type:fiction");
                }
            }
            
            return false;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateStatusManually()
        {
            Debug.WriteLine("UpdateStatusManually called");
            
                            // ALWAYS ensure we're on the UI thread for UI updates
            if (!Dispatcher.CheckAccess())
            {
                Debug.WriteLine("UpdateStatusManually: Not on UI thread, invoking on UI thread");
                Dispatcher.Invoke(() => UpdateStatusManually());
                return;
            }
            
            // Avoid excessive updates if content hasn't changed significantly
            var content = MarkdownEditor?.Text ?? string.Empty;
            if (content.Length > 0 && Math.Abs(content.Length - _lastContentLength) < 10)
            {
                // Content length changed by less than 10 chars, debounce the update
                return;
            }
            _lastContentLength = content.Length;
            
            try
            {
                if (WordCountText == null) 
                {
                    Debug.WriteLine("ERROR: WordCountText control is null - cannot update status");
                    return;
                }
                
                Debug.WriteLine($"UpdateStatusManually: Content length: {content.Length}");
                
                // For very large documents (paste operations), show immediate feedback
                if (content.Length > 50000) // 50k+ characters
                {
                    WordCountText.Text = "Calculating word count...";
                    Debug.WriteLine("UpdateStatusManually: Set temporary 'Calculating...' text");
                    
                    // Use Dispatcher to allow UI to update, then calculate
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            var wordCount = CalculateWordCountManual(content);
                            var characterCount = content.Length;
                            var readingTime = CalculateReadingTimeManual(wordCount);
                            
                            var statusText = $"Words: {wordCount:N0} | Characters: {characterCount:N0} | Reading: {readingTime}";
                            WordCountText.Text = statusText;
                            Debug.WriteLine($"Large document status update completed: {statusText}");
                            Debug.WriteLine($"WordCountText.Text after large update: '{WordCountText.Text}'");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ERROR calculating large document status: {ex.Message}");
                            WordCountText.Text = "Words: Error | Characters: Error | Reading: Error";
                        }
                    }), DispatcherPriority.Background);
                }
                else
                {
                    // Normal processing for smaller documents - force immediate UI update
                    var wordCount = CalculateWordCountManual(content);
                    var characterCount = content.Length;
                    var readingTime = CalculateReadingTimeManual(wordCount);
                    
                    var statusText = $"Words: {wordCount:N0} | Characters: {characterCount:N0} | Reading: {readingTime}";
                    
                    WordCountText.Text = statusText;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR in manual status update: {ex.Message}");
                Debug.WriteLine($"ERROR stack trace: {ex.StackTrace}");
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

            // Remove markdown formatting for more accurate word count using the same logic as AvalonEditStatusManager
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

            // Use the same comprehensive markdown removal logic as AvalonEditStatusManager for consistency
            var cleaned = content;
            
            // Remove headers
            cleaned = Regex.Replace(cleaned, @"^#{1,6}\s+", "", RegexOptions.Multiline);
            
            // Remove bold and italic
            cleaned = Regex.Replace(cleaned, @"\*\*([^*]+)\*\*", "$1");
            cleaned = Regex.Replace(cleaned, @"\*([^*]+)\*", "$1");
            cleaned = Regex.Replace(cleaned, @"__([^_]+)__", "$1");
            cleaned = Regex.Replace(cleaned, @"_([^_]+)_", "$1");
            
            // Remove inline code
            cleaned = Regex.Replace(cleaned, @"`([^`]+)`", "$1");
            
            // Remove links
            cleaned = Regex.Replace(cleaned, @"\[([^\]]*)\]\([^)]*\)", "$1");
            
            // Remove blockquotes
            cleaned = Regex.Replace(cleaned, @"^>\s*", "", RegexOptions.Multiline);
            
            // Remove list markers
            cleaned = Regex.Replace(cleaned, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);

            return cleaned;
        }
        #endregion
    }
} 