using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Threading;
using Universa.Desktop.Interfaces;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Helpers;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Highlighting;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Tabs
{
    public partial class OrgModeTab : UserControl, INotifyPropertyChanged, IDisposable, IFileTab
    {
        public int LastKnownCursorPosition { get; private set; } = 0;
        
        private readonly OrgModeService _orgService;
        private readonly ConfigurationProvider _configProvider;
        private string _filePath;
        private TextDocument _sourceDocument;
        private Timer _autoSaveTimer;
        private Timer _sourceParseTimer;
        private FoldingManager _foldingManager;
        private OrgModeFoldingStrategy _foldingStrategy;
        private bool _isDisposed;
        private bool _isProgrammaticallyChangingText;
        private OrgItem _selectedItem;
        
        // State cycling debounce support
        private Timer _stateCyclingDebounceTimer;
        private OrgItem _lastCycledItem;
        private string _lastCycledState;
        private DateTime _lastTextReplacement = DateTime.MinValue;

        public event PropertyChangedEventHandler PropertyChanged;

        // IFileTab Properties
        public string FilePath 
        { 
            get => _filePath; 
            set 
            {
                if (_filePath != value)
                {
                    _filePath = value; 
                    OnPropertyChanged();
                    UpdateTabHeader(); // Update tab header when file path changes
                }
            }
        }
        
        public string Title 
        { 
            get => Path.GetFileName(_filePath) ?? "Untitled";
            set { } // Setter not used for file tabs
        }
        
        public bool IsModified 
        { 
            get => HasUnsavedChanges; 
            set
            {
                if (HasUnsavedChanges != value)
                {
                    HasUnsavedChanges = value;
                    OnPropertyChanged(nameof(IsModified));
                    OnPropertyChanged(nameof(HasUnsavedChanges));
                    UpdateTabHeader(); // Update tab header to show/hide asterisk
                }
            }
        }
        
        // Legacy property for internal use
        private bool HasUnsavedChanges { get; set; }
        
        public ObservableCollection<OrgItem> Items => _orgService?.Items ?? new ObservableCollection<OrgItem>();
        
        public OrgItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }
        
        public string StatusText => $"Org-Mode: {Items.Count} items";
        public int ItemCount => Items.Count;
        public int CompletedCount => Items.Count(i => i.State == OrgState.DONE);
        public int OverdueCount => Items.Count(i => i.Deadline.HasValue && i.Deadline < DateTime.Now && i.State != OrgState.DONE);
        
        // Source editor document binding
        public TextDocument SourceDocument
        {
            get => _sourceDocument;
            private set
            {
                _sourceDocument = value;
                OnPropertyChanged();
            }
        }

        public OrgModeTab(string filePath)
        {
            InitializeComponent();
            
            _filePath = filePath;
            _orgService = new OrgModeService(filePath);
            _configProvider = ConfigurationProvider.Instance; // Use the singleton instance
            
            // Initialize AvalonEdit components
            InitializeSourceEditor();
            
            DataContext = this;

            // Remove auto-save timer - we want user-controlled saving only
            // _autoSaveTimer = new Timer(TimeSpan.FromSeconds(2).TotalMilliseconds);
            // _autoSaveTimer.Elapsed += AutoSaveTimer_Tick;

            // Set up source parse timer (debounced parsing) - keep this for UI updates
            _sourceParseTimer = new Timer(TimeSpan.FromSeconds(1).TotalMilliseconds);
            _sourceParseTimer.Elapsed += SourceParseTimer_Tick;

            // Set up state cycling debounce timer (for CLOSED timestamp handling)
            _stateCyclingDebounceTimer = new Timer(TimeSpan.FromSeconds(1.5).TotalMilliseconds) { AutoReset = false };
            _stateCyclingDebounceTimer.Elapsed += StateCyclingDebounceTimer_Elapsed;

            // Subscribe to service events
            _orgService.ItemChanged += OnItemChanged;
            
            // Subscribe to org state configuration changes for automatic updates
            Services.OrgStateConfigurationService.Instance.ConfigurationChanged += OnOrgStateConfigurationChanged;

            // Load the file
            LoadFile();
        }

        private void InitializeSourceEditor()
        {
            // Initialize the text document first
            _sourceDocument = new TextDocument();
            SourceEditor.Document = _sourceDocument;
            
            // Set up the source editor
            SourceEditor.TextArea.TextView.LineTransformers.Add(new OrgModeInlineFormatter());
            
            // Set up folding
            _foldingStrategy = new OrgModeFoldingStrategy();
            
            // Install folding manager now that document exists
            if (_foldingManager == null)
            {
                _foldingManager = FoldingManager.Install(SourceEditor.TextArea);
                _foldingStrategy.UpdateFoldings(_foldingManager, SourceEditor.Document);
            }
            
            // Update foldings when document changes
            SourceEditor.Document.TextChanged += (s, e) =>
            {
                if (_foldingManager != null)
                {
                    _foldingStrategy.UpdateFoldings(_foldingManager, SourceEditor.Document);
                }
            };
            
            // Wire up events for auto-save and parsing
            _sourceDocument.TextChanged += SourceDocument_TextChanged;
            
            // Set up keyboard shortcuts
            SetupKeyboardShortcuts();
            
            // Apply settings when editor is loaded (for additional setup if needed)
            SourceEditor.Loaded += (s, e) => ApplyEditorSettings();
        }
        
        private void ApplyEditorSettings()
        {
            if (_isDisposed) return;
            
            // Only install folding manager if it doesn't exist and we have a document
            if (_foldingManager == null && SourceEditor.Document != null)
            {
                _foldingManager = FoldingManager.Install(SourceEditor.TextArea);
                _foldingStrategy.UpdateFoldings(_foldingManager, SourceEditor.Document);
            }
        }
        
        private void SetupKeyboardShortcuts()
        {
            // Use PreviewKeyDown to intercept Enter before AvalonEdit's internal handlers
            SourceEditor.TextArea.PreviewKeyDown += (s, e) =>
            {
                // Enter - Handle new lines, list items, and folded headers FIRST
                if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    System.Diagnostics.Debug.WriteLine($"PreviewKeyDown: Enter key pressed at offset {SourceEditor.CaretOffset}");
                    
                    // First check if we're at the end of a folded header line
                    if (HandleEnterOnFoldedHeader())
                    {
                        System.Diagnostics.Debug.WriteLine("HandleEnterOnFoldedHeader succeeded - handling enter");
                        e.Handled = true;
                        return;
                    }
                    
                    // The automatic list creation was intrusive. It is now removed.
                    // The user should have full control over when to create a list.
                    
                    System.Diagnostics.Debug.WriteLine("No special Enter handling - letting AvalonEdit handle it");
                }

                // Backspace - Prevent deletion of folded content
                if (e.Key == Key.Back)
                {
                    System.Diagnostics.Debug.WriteLine($"Backspace key detected. Caret offset: {SourceEditor.CaretOffset}");
                    var caretOffset = SourceEditor.CaretOffset;

                    if (_foldingManager != null)
                    {
                        // Get the line the caret is on.
                        var line = SourceEditor.Document.GetLineByOffset(caretOffset);

                        // Check if the caret is at the visual end of the line's content.
                        if (caretOffset == line.EndOffset - line.DelimiterLength)
                        {
                            // Now, find a folded section that starts exactly where this line ends (after the delimiter).
                            var foldingToUnfold = _foldingManager.AllFoldings
                                .FirstOrDefault(f => f.IsFolded && f.StartOffset == line.EndOffset);

                            if (foldingToUnfold != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"Backspace protection: Unfolding section {foldingToUnfold.StartOffset}-{foldingToUnfold.EndOffset} instead of deleting.");
                                
                                // Unfold the section instead of deleting it.
                                foldingToUnfold.IsFolded = false;
                                
                                // Mark the event as handled to prevent the default backspace action.
                                e.Handled = true;
                            }
                        }
                    }
                }
            };
            
            // TAB on header to cycle folding (Emacs org-mode style)
            SourceEditor.TextArea.KeyDown += (s, e) =>
            {
                // Ctrl+Shift+[ - Collapse all foldings
                if (e.Key == Key.OemOpenBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    CollapseAllFoldings();
                    e.Handled = true;
                    return;
                }
                
                // Ctrl+Shift+] - Expand all foldings
                if (e.Key == Key.OemCloseBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    ExpandAllFoldings();
                    e.Handled = true;
                    return;
                }
                
                // Ctrl+[ - Collapse current folding
                if (e.Key == Key.OemOpenBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    CollapseCurrentFolding();
                    e.Handled = true;
                    return;
                }
                
                // Ctrl+] - Expand current folding
                if (e.Key == Key.OemCloseBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    ExpandCurrentFolding();
                    e.Handled = true;
                    return;
                }
                
                // TAB on header to toggle folding (Emacs org-mode style)
                if (e.Key == Key.Tab && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
                    var lineText = SourceEditor.Document.GetText(line);
                    
                    // Check if cursor is on a header line
                    if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
                    {
                        TryToggleFoldingAtCursor(true);
                        e.Handled = true;
                    }
                    // Check if cursor is on a list item - indent it
                    else if (OrgModeListSupport.IndentListItem(SourceEditor, true))
                    {
                        e.Handled = true;
                    }
                    // For regular text lines, insert a tab character (consistent with MarkdownTab behavior)
                    else
                    {
                        SourceEditor.Document.Insert(SourceEditor.CaretOffset, "    "); // 4 spaces like MarkdownTab
                        e.Handled = true;
                    }
                }
                else if (e.Key == Key.Tab && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Shift+TAB cycles backwards through folding states or outdents lists
                    var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
                    var lineText = SourceEditor.Document.GetText(line);
                    
                    if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
                    {
                        if (TryToggleFoldingAtCursor(false))
                        {
                            e.Handled = true;
                        }
                    }
                    // Check if cursor is on a list item - outdent it
                    else if (OrgModeListSupport.IndentListItem(SourceEditor, false))
                    {
                        e.Handled = true;
                    }
                    // For regular text lines, handle Shift+Tab (remove indentation)
                    else
                    {
                        var caretOffset = SourceEditor.CaretOffset;
                        var currentLine = SourceEditor.Document.GetLineByOffset(caretOffset);
                        var currentLineText = SourceEditor.Document.GetText(currentLine);
                        
                        // Remove up to 4 spaces at the beginning of the line
                        if (currentLineText.StartsWith("    "))
                        {
                            SourceEditor.Document.Remove(currentLine.Offset, 4);
                        }
                        else if (currentLineText.StartsWith("   "))
                        {
                            SourceEditor.Document.Remove(currentLine.Offset, 3);
                        }
                        else if (currentLineText.StartsWith("  "))
                        {
                            SourceEditor.Document.Remove(currentLine.Offset, 2);
                        }
                        else if (currentLineText.StartsWith(" "))
                        {
                            SourceEditor.Document.Remove(currentLine.Offset, 1);
                        }
                        e.Handled = true;
                    }
                }
                
                // Ctrl+Shift+C - Toggle checkbox (non-conflicting with copy)
                if (e.Key == Key.C && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    if (OrgModeListSupport.ToggleCheckbox(SourceEditor))
                    {
                        e.Handled = true;
                    }
                }
                
                // Ctrl+Shift++ for cycling TODO states
                if ((e.Key == Key.OemPlus || e.Key == Key.Add) && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    if (CycleTodoStateAtCursor())
                    {
                        e.Handled = true;
                    }
                    return;
                }
                
                // Ctrl+W or Ctrl+R for refile item (org-mode standard)
                if ((e.Key == Key.W || e.Key == Key.R) && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
                {
                    // CRITICAL FIX: Sync editor content to service before getting item
                    // This ensures that any unsaved edits in the editor are reflected in the service's items
                    try
                    {
                        // Use GetAwaiter().GetResult() for synchronous execution in UI context
                        SyncSourceToService().GetAwaiter().GetResult();
                        System.Diagnostics.Debug.WriteLine("GetOrgItemAtCursor: Synced editor content to service before refile");
                    }
                    catch (Exception syncEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Sync failed: {syncEx.Message}");
                    }
                    
                    var orgItem = GetOrgItemAtCursor();
                    if (orgItem != null)
                    {
                        // Set the selected item to the one at cursor, then refile
                        SelectedItem = orgItem;
                        RefileItem_Click(null, null); // UI operations must run on UI thread
                        e.Handled = true;
                    }
                    else if (SelectedItem != null)
                    {
                        // Fallback to selected item
                        RefileItem_Click(null, null); // UI operations must run on UI thread
                        e.Handled = true;
                    }
                    return;
                }

                // Ctrl+Shift+T for tag cycling (similar to Emacs org-mode C-c C-q)
                if (e.Key == Key.T && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
                {
                    if (CycleTagsAtCursor())
                    {
                        e.Handled = true;
                    }
                    return;
                }
                

            };
        }
        
        private bool TryToggleFoldingAtCursor(bool expand = true)
        {
            var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
            var lineText = SourceEditor.Document.GetText(line);
            
            // Check if current line is a header
            if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
            {
                var folding = _foldingManager.GetFoldingsAt(line.Offset).FirstOrDefault();
                if (folding != null)
                {
                    folding.IsFolded = expand ? !folding.IsFolded : false;
                    return true;
                }
            }
            return false;
        }
        
        private void CollapseAllFoldings()
        {
            if (_foldingManager != null)
            {
                foreach (var folding in _foldingManager.AllFoldings)
                {
                    folding.IsFolded = true;
                }
            }
        }
        
        private void ExpandAllFoldings()
        {
            if (_foldingManager != null)
            {
                foreach (var folding in _foldingManager.AllFoldings)
                {
                    folding.IsFolded = false;
                }
            }
        }
        
        private void CollapseCurrentFolding()
        {
            if (_foldingManager != null && SourceEditor != null)
            {
                var folding = FindFoldingForCurrentPosition();
                if (folding != null)
                {
                    folding.IsFolded = true;
                }
            }
        }
        
        private void ExpandCurrentFolding()
        {
            if (_foldingManager != null && SourceEditor != null)
            {
                var folding = FindFoldingForCurrentPosition();
                if (folding != null)
                {
                    folding.IsFolded = false;
                }
            }
        }
        
        private FoldingSection FindFoldingForCurrentPosition()
        {
            if (_foldingManager == null || SourceEditor == null)
                return null;
                
            var caretOffset = SourceEditor.CaretOffset;
            var line = SourceEditor.Document.GetLineByOffset(caretOffset);
            var lineText = SourceEditor.Document.GetText(line);
            
            // Check if current line is a header
            if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
            {
                // For header lines, find folding that starts after this line
                var lineEnd = line.EndOffset;
                return _foldingManager.AllFoldings
                    .Where(f => f.StartOffset >= lineEnd && f.StartOffset <= lineEnd + 50) // Small tolerance
                    .OrderBy(f => f.StartOffset)
                    .FirstOrDefault();
            }
            else
            {
                // For non-header lines, find containing folding
                return _foldingManager.GetFoldingsAt(caretOffset).FirstOrDefault() ??
                       _foldingManager.AllFoldings
                           .Where(f => f.StartOffset <= caretOffset && f.EndOffset >= caretOffset)
                           .OrderByDescending(f => f.StartOffset) // Get the innermost folding
                           .FirstOrDefault();
            }
        }
        
        /// <summary>
        /// Handles Enter key when positioned at the end of a folded header line or after folded content
        /// </summary>
        private bool HandleEnterOnFoldedHeader()
        {
            if (_foldingManager == null || SourceEditor == null)
            {
                System.Diagnostics.Debug.WriteLine("HandleEnterOnFoldedHeader: No folding manager or editor");
                return false;
            }
                
            var caretOffset = SourceEditor.CaretOffset;
            var line = SourceEditor.Document.GetLineByOffset(caretOffset);
            var lineText = SourceEditor.Document.GetText(line);
            
            System.Diagnostics.Debug.WriteLine($"HandleEnterOnFoldedHeader: Caret at {caretOffset}, line text: '{lineText}'");
            
            // Check if current line is a header
            var headerMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"^(\s*)(\*+)\s+(.*)$");
            if (!headerMatch.Success)
            {
                System.Diagnostics.Debug.WriteLine("HandleEnterOnFoldedHeader: Current line is not a header");
                return false;
            }
            
            System.Diagnostics.Debug.WriteLine($"HandleEnterOnFoldedHeader: FOUND HEADER! Stars: '{headerMatch.Groups[2].Value}' Title: '{headerMatch.Groups[3].Value}'");
            
            // Find any folded section that starts right after this line
            var allFoldings = _foldingManager.AllFoldings.Where(f => f.IsFolded).ToList();
            var lineEnd = line.EndOffset;
            
            var associatedFolding = allFoldings.FirstOrDefault(f => f.StartOffset == lineEnd);
                
            if (associatedFolding != null)
            {
                _isProgrammaticallyChangingText = true;
                try
                {
                    System.Diagnostics.Debug.WriteLine($"HandleEnterOnFoldedHeader: Found associated folding {associatedFolding.StartOffset}-{associatedFolding.EndOffset}");

                    // Unfold the region before inserting, to avoid AvalonEdit conflicts
                    associatedFolding.IsFolded = false;

                    // Insert a newline *after* the content that was folded
                    var insertPosition = associatedFolding.EndOffset;
                    SourceEditor.Document.Insert(insertPosition, Environment.NewLine);
                    SourceEditor.CaretOffset = insertPosition + Environment.NewLine.Length;

                    System.Diagnostics.Debug.WriteLine($"HandleEnterOnFoldedHeader: Inserted newline at {insertPosition}, cursor at {SourceEditor.CaretOffset}");
                }
                finally
                {
                    _isProgrammaticallyChangingText = false;
                }
                
                // Manually trigger the updates now that the programmatic change is complete.
                UpdateFolding();
                _sourceParseTimer.Stop();
                _sourceParseTimer.Start();

                return true;
            }
            
            System.Diagnostics.Debug.WriteLine("HandleEnterOnFoldedHeader: No folded section after this header");
            return false;
        }

        
        private void SourceDocument_TextChanged(object sender, EventArgs e)
        {
            IsModified = true;

            // If a change is happening via code, defer updates until it's complete
            if (_isProgrammaticallyChangingText)
            {
                return;
            }
            
            // Update folding
            UpdateFolding();
            
            // Debounce parsing
            _sourceParseTimer.Stop();
            _sourceParseTimer.Start();
        }
        
        private void UpdateFolding()
        {
            if (_foldingManager != null && _foldingStrategy != null)
            {
                _foldingStrategy.UpdateFoldings(_foldingManager, _sourceDocument);
            }
        }

        private async Task LoadFile()
        {
            try
            {
                // Check if file exists and get latest content for sync scenarios
                if (!File.Exists(_filePath))
                {
                    // File doesn't exist - create minimal content but don't auto-save
                    System.Diagnostics.Debug.WriteLine($"LoadFile: File does not exist: {_filePath}");
                    
                    // Initialize with empty content and mark as modified so user must explicitly save
                    _sourceDocument.Text = string.Empty;
                    IsModified = false; // Start clean - user will add content and then it becomes modified
                    
                    // Clear the service items
                    _orgService.Items.Clear();
                    OnPropertyChanged(nameof(Items));
                    OnPropertyChanged(nameof(SourceDocument));
                    
                    MessageBox.Show($"File '{Path.GetFileName(_filePath)}' does not exist. Starting with empty content.", 
                        "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                
                // File exists - load the latest content (important for sync scenarios)
                await _orgService.LoadFromFileAsync(_filePath);
                OnPropertyChanged(nameof(Items));
                
                // Initialize source content with what was actually loaded from file
                var loadedContent = _orgService.GetContent();
                _sourceDocument.Text = loadedContent;
                OnPropertyChanged(nameof(SourceDocument));
                
                // Start clean - file content matches what's loaded
                IsModified = false;
                
                System.Diagnostics.Debug.WriteLine($"LoadFile: Successfully loaded {loadedContent.Length} characters from {_filePath}");
            }
            catch (FileNotFoundException)
            {
                // Handle file not found specifically
                System.Diagnostics.Debug.WriteLine($"LoadFile: File not found: {_filePath}");
                
                _sourceDocument.Text = string.Empty;
                IsModified = false;
                _orgService.Items.Clear();
                OnPropertyChanged(nameof(Items));
                OnPropertyChanged(nameof(SourceDocument));
                
                MessageBox.Show($"File '{Path.GetFileName(_filePath)}' not found. Starting with empty content.", 
                    "File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFile: Error loading file {_filePath}: {ex.Message}");
                MessageBox.Show($"Error loading org file: {ex.Message}\n\nStarting with empty content.", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Fallback to empty content on any error
                _sourceDocument.Text = string.Empty;
                IsModified = false;
                _orgService.Items.Clear();
                OnPropertyChanged(nameof(Items));
                OnPropertyChanged(nameof(SourceDocument));
            }
        }

        private void OnItemChanged(object sender, OrgItemChangedEventArgs e)
        {
            IsModified = true;
            // Remove auto-save timer - let user control when to save
            // StartAutoSaveTimer();
        }

        private void StartAutoSaveTimer()
        {
            // Auto-save timer removed - manual save only
            // _autoSaveTimer.Stop();
            // _autoSaveTimer.Start();
        }

        private async void AutoSaveTimer_Tick(object sender, EventArgs e)
        {
            // Auto-save timer removed - manual save only
            /*
            _autoSaveTimer.Stop();
            
            // Marshal to UI thread since we might access UI elements
            if (Dispatcher.CheckAccess())
            {
                if (IsModified)
                {
                    await Save();
                }
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsModified)
                    {
                        _ = Save();
                    }
                }));
            }
            */
        }

        private async void SourceParseTimer_Tick(object sender, EventArgs e)
        {
            _sourceParseTimer.Stop();
            
            // Marshal to UI thread since TextDocument can only be accessed from UI thread
            if (Dispatcher.CheckAccess())
            {
                await ParseSourceContent();
            }
            else
            {
                Dispatcher.BeginInvoke(new Action(() => _ = ParseSourceContent()));
            }
        }

        /// <summary>
        /// Handles debounced state cycling - only applies CLOSED timestamps after user stops cycling
        /// </summary>
        private async void StateCyclingDebounceTimer_Elapsed(object sender, EventArgs e)
        {
            try
            {
                // Marshal to UI thread since we need to access WPF elements
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.BeginInvoke(new Action(async () => await HandleDebouncedStateChange()));
                    return;
                }
                
                await HandleDebouncedStateChange();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debounce: Error in timer elapsed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the actual debounced state change on the UI thread
        /// </summary>
        private async Task HandleDebouncedStateChange()
        {
            try
            {
                if (_lastCycledItem != null && !string.IsNullOrEmpty(_lastCycledState))
                {
                    System.Diagnostics.Debug.WriteLine($"Debounce: User stopped cycling, applying final state '{_lastCycledState}' to '{_lastCycledItem.Title}'");
                    
                    // Check if final state is a done state that needs CLOSED timestamp
                    var stateService = Services.OrgStateConfigurationService.Instance;
                    var config = stateService.GetConfiguration();
                    var isDoneState = config.DoneStates.Any(s => s.Name == _lastCycledState);
                    
                    System.Diagnostics.Debug.WriteLine($"Debounce: State '{_lastCycledState}' is done state: {isDoneState}");
                    
                    if (isDoneState && !_lastCycledItem.Closed.HasValue)
                    {
                        _lastCycledItem.Closed = DateTime.Now;
                        System.Diagnostics.Debug.WriteLine($"Debounce: Added CLOSED timestamp for done state '{_lastCycledState}'");
                        
                        // Update the text in the editor to show the CLOSED timestamp
                        await UpdateClosedTimestampInEditor(_lastCycledItem);
                        
                        // Save the change
                        await _orgService.UpdateItemAsync(_lastCycledItem);
                        await _orgService.SaveToFileAsync();
                    }
                    else if (!isDoneState && _lastCycledItem.Closed.HasValue)
                    {
                        _lastCycledItem.Closed = null;
                        System.Diagnostics.Debug.WriteLine($"Debounce: Removed CLOSED timestamp for non-done state '{_lastCycledState}'");
                        
                        // Update the text in the editor to remove the CLOSED timestamp
                        await UpdateClosedTimestampInEditor(_lastCycledItem);
                        
                        // Save the change
                        await _orgService.UpdateItemAsync(_lastCycledItem);
                        await _orgService.SaveToFileAsync();
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Debounce: No CLOSED timestamp change needed (isDone={isDoneState}, hasClosed={_lastCycledItem.Closed.HasValue})");
                    }
                }
                
                // Clear the tracking
                _lastCycledItem = null;
                _lastCycledState = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debounce: Error handling final state: {ex.Message}");
            }
        }

        /// <summary>
        /// Updates the CLOSED timestamp in the editor text
        /// </summary>
        private async Task UpdateClosedTimestampInEditor(OrgItem item)
        {
            try
            {
                // Find the line containing this item
                var lines = SourceEditor.Document.Lines.ToList();
                DocumentLine targetLine = null;
                
                foreach (var line in lines)
                {
                    var lineText = SourceEditor.Document.GetText(line);
                    if (lineText.Contains(item.Title) && lineText.StartsWith("*"))
                    {
                        targetLine = line;
                        break;
                    }
                }
                
                if (targetLine != null)
                {
                    var currentLineText = SourceEditor.Document.GetText(targetLine);
                    
                    // Remove any existing CLOSED timestamp
                    var cleanedLine = System.Text.RegularExpressions.Regex.Replace(currentLineText, 
                        @"\s*CLOSED:\s*\[[^\]]+\]", "");
                    
                    // Add new CLOSED timestamp if item is closed
                    string newLineText;
                    if (item.Closed.HasValue)
                    {
                        var closedTimestamp = item.Closed.Value.ToString("yyyy-MM-dd ddd HH:mm");
                        newLineText = $"{cleanedLine.TrimEnd()} CLOSED: [{closedTimestamp}]";
                    }
                    else
                    {
                        newLineText = cleanedLine.TrimEnd();
                    }
                    
                    // Replace the line
                    SourceEditor.Document.Replace(targetLine.Offset, targetLine.Length, newLineText);
                    
                    System.Diagnostics.Debug.WriteLine($"Debounce: Updated line with CLOSED timestamp: '{newLineText}'");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Debounce: Error updating CLOSED timestamp in editor: {ex.Message}");
            }
        }

        private async Task ParseSourceContent()
        {
            if (_orgService != null && !string.IsNullOrEmpty(_sourceDocument.Text))
            {
                try
                {
                    var items = await _orgService.ParseContentAsync(_sourceDocument.Text);
                    _orgService.Items.Clear();
                    foreach (var item in items)
                    {
                        _orgService.Items.Add(item);
                    }
                    OnPropertyChanged(nameof(Items));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error parsing org content: {ex.Message}", "Parse Error", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Saves the raw source document content directly to file, preserving exact formatting
        /// </summary>
        private async Task SaveSourceContentDirectly(string filePath = null)
        {
            var targetPath = filePath ?? _filePath;
            
            if (_sourceDocument != null && !string.IsNullOrEmpty(targetPath))
            {
                try
                {
                    // Save the exact source content as-is
                    await File.WriteAllTextAsync(targetPath, _sourceDocument.Text);
                    System.Diagnostics.Debug.WriteLine($"SaveSourceContentDirectly: Saved {_sourceDocument.Text.Length} characters to {targetPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SaveSourceContentDirectly: Error saving to {targetPath}: {ex.Message}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Ensures the source document content is synced to the org service before saving
        /// </summary>
        private async Task SyncSourceToService()
        {
            if (_orgService != null && _sourceDocument != null && !string.IsNullOrEmpty(_sourceDocument.Text))
            {
                try
                {
                    // Stop any pending parse timer to avoid conflicts
                    _sourceParseTimer?.Stop();
                    
                    // Force immediate sync of source content to service
                    var items = await _orgService.ParseContentAsync(_sourceDocument.Text);
                    _orgService.Items.Clear();
                    foreach (var item in items)
                    {
                        _orgService.Items.Add(item);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"SyncSourceToService: Synced {items.Count} items from source document");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SyncSourceToService: Error syncing content: {ex.Message}");
                    throw; // Re-throw so save operation can handle it
                }
            }
        }

        private void UpdateStatusProperties()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(ItemCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(OverdueCount));
            
            // Don't automatically reformat source content when switching tabs
            // This prevents spacing issues with list items and preserves manual formatting
            // The source document should only be updated when explicitly parsing/loading content
        }

        // IFileTab Implementation
        public async Task<bool> Save()
        {
            try
            {
                // Validate that we have content to save
                if (_sourceDocument == null || string.IsNullOrEmpty(_filePath))
                {
                    System.Diagnostics.Debug.WriteLine("Save: Cannot save - missing source document or file path");
                    MessageBox.Show("Cannot save: missing content or file path.", "Save Error", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
                
                // Log what we're about to save for debugging
                var contentLength = _sourceDocument.Text?.Length ?? 0;
                System.Diagnostics.Debug.WriteLine($"Save: Saving {contentLength} characters to {_filePath}");
                
                // Don't save if content is completely empty (potential sync issue)
                if (contentLength == 0)
                {
                    var result = MessageBox.Show(
                        "The file content is empty. This might overwrite existing content if the file was modified externally.\n\nDo you want to save the empty content?",
                        "Empty Content Warning",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                        
                    if (result != MessageBoxResult.Yes)
                    {
                        return false;
                    }
                }
                
                // Save the raw source content directly to preserve exact formatting
                await SaveSourceContentDirectly();
                
                // Then sync to service for consistency (but don't save again)
                await SyncSourceToService();
                
                IsModified = false;
                System.Diagnostics.Debug.WriteLine($"Save: Successfully saved {contentLength} characters");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Save: Error saving file: {ex.Message}");
                MessageBox.Show($"Error saving org file: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(newPath))
                {
                    var dialog = new SaveFileDialog
                    {
                        Filter = "Org files (*.org)|*.org|All files (*.*)|*.*",
                        DefaultExt = ".org",
                        FileName = Path.GetFileName(_filePath)
                    };

                    if (dialog.ShowDialog() != true)
                        return false;

                    newPath = dialog.FileName;
                }

                // Save the raw source content directly to the new path
                await SaveSourceContentDirectly(newPath);
                
                // Update paths and sync to service
                _filePath = newPath;
                _orgService.UpdateFilePath(newPath);
                await SyncSourceToService();
                
                IsModified = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving org file: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public void Reload()
        {
            // Force reload from disk to get latest content (important for sync scenarios)
            System.Diagnostics.Debug.WriteLine($"Reload: Reloading file from disk: {_filePath}");
            
            // Reset modified state since we're reloading
            IsModified = false;
            
            _ = LoadFile();
            
            // Force UI refresh of all properties
            Dispatcher.BeginInvoke(new Action(() =>
            {
                OnPropertyChanged(nameof(Items));
                OnPropertyChanged(nameof(ItemCount));
                OnPropertyChanged(nameof(CompletedCount));
                OnPropertyChanged(nameof(OverdueCount));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(SourceDocument));
                
                System.Diagnostics.Debug.WriteLine($"Reload: Forced UI refresh for {_filePath}");
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public string GetContent()
        {
            return _orgService?.SerializeContentAsync().Result ?? string.Empty;
        }

        public void OnTabSelected()
        {
            // Refresh when tab is selected
            UpdateStatusProperties();
        }

        public void OnTabDeselected()
        {
            // Remove auto-save behavior to match other tabs
            // The main window will handle prompting for unsaved changes
        }

        // Event Handlers
        private void OrgTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedItem = e.NewValue as OrgItem;
        }

        private void TreeViewItem_Selected(object sender, RoutedEventArgs e)
        {
            if (sender is TreeViewItem item && item.DataContext is OrgItem orgItem)
            {
                SelectedItem = orgItem;
                e.Handled = true;
            }
        }

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item && item.DataContext is OrgItem orgItem)
            {
                // Toggle expanded state
                orgItem.IsExpanded = !orgItem.IsExpanded;
                e.Handled = true;
            }
        }

        private async void StateButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is OrgItem item)
            {
                await _orgService.CycleStateAsync(item.Id);
            }
        }

        private async void PriorityButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is OrgItem item)
            {
                await _orgService.CyclePriorityAsync(item.Id);
            }
        }

        // Toolbar Event Handlers
        private async void AddItem_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("New Item", "Enter item title:");
            if (dialog.ShowDialog() == true)
            {
                var newItem = new OrgItem
                {
                    Title = dialog.InputText,
                    State = OrgState.TODO,
                    Level = 1
                };

                await _orgService.CreateItemAsync(newItem);
                OnPropertyChanged(nameof(Items));
            }
        }

        private async void AddChild_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select a parent item first.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new InputDialog("New Child Item", "Enter item title:");
            if (dialog.ShowDialog() == true)
            {
                var newItem = new OrgItem
                {
                    Title = dialog.InputText,
                    State = OrgState.TODO,
                    Level = SelectedItem.Level + 1
                };

                await _orgService.AddChildAsync(SelectedItem.Id, newItem);
                OnPropertyChanged(nameof(Items));
            }
        }

        private async void DeleteItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item to delete.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete '{SelectedItem.Title}'?", 
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _orgService.DeleteItemAsync(SelectedItem.Id);
                SelectedItem = null;
                OnPropertyChanged(nameof(Items));
            }
        }

        private async void Promote_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem != null)
            {
                await _orgService.PromoteItemAsync(SelectedItem.Id);
            }
        }

        private async void Demote_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem != null)
            {
                await _orgService.DemoteItemAsync(SelectedItem.Id);
            }
        }

        private async void AddTag_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item first.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new InputDialog("Add Tag", "Enter tag name:");
            if (dialog.ShowDialog() == true)
            {
                await _orgService.AddTagAsync(SelectedItem.Id, dialog.InputText);
            }
        }

        private async void SetScheduled_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item first.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DatePickerDialog("Set Scheduled Date", SelectedItem.Scheduled ?? DateTime.Today);
            if (dialog.ShowDialog() == true)
            {
                await _orgService.SetScheduledAsync(SelectedItem.Id, dialog.SelectedDate);
            }
        }

        private async void SetDeadline_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item first.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new DatePickerDialog("Set Deadline", SelectedItem.Deadline ?? DateTime.Today);
            if (dialog.ShowDialog() == true)
            {
                await _orgService.SetDeadlineAsync(SelectedItem.Id, dialog.SelectedDate);
            }
        }

        private async void AddLink_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new LinkDialog();
            if (dialog.ShowDialog() == true)
            {
                var linkText = $"[[{dialog.LinkTarget}]{(string.IsNullOrEmpty(dialog.LinkDescription) ? "" : $"[{dialog.LinkDescription}]")}]";
                
                // Add link to the item's content
                var newContent = string.IsNullOrEmpty(SelectedItem.Content) 
                    ? linkText 
                    : $"{SelectedItem.Content}\n{linkText}";
                
                SelectedItem.Content = newContent;
                await _orgService.UpdateItemAsync(SelectedItem);
            }
        }

        private async void RefileItem_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedItem == null)
            {
                MessageBox.Show("Please select an item to refile.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Get configuration service - try to use dependency injection, otherwise create new instance
                var configService = _configProvider != null 
                    ? new Core.Configuration.ConfigurationService(new Core.Configuration.JsonConfigurationStore(
                        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Universa", "config.json")))
                    : new Core.Configuration.ConfigurationService();
                    
                var globalAgendaService = new GlobalOrgAgendaService(configService);

                var dialog = new Dialogs.RefileDialog(SelectedItem, _orgService, configService, globalAgendaService);
                dialog.Owner = Window.GetWindow(this);
                
                var result = dialog.ShowDialog();
                
                if (result == true)
                {
                    // Refresh the current view since the item was moved
                    await LoadFile();
                    OnPropertyChanged(nameof(Items));
                    OnPropertyChanged(nameof(ItemCount));
                    OnPropertyChanged(nameof(CompletedCount));
                    OnPropertyChanged(nameof(OverdueCount));
                    
                    // Clear selection since the item no longer exists in this file
                    SelectedItem = null;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening refile dialog: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LinkButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is OrgItem item)
            {
                ShowLinksDialog(item);
            }
        }

        private void ViewCycle_Click(object sender, RoutedEventArgs e)
        {
            // This method is removed as per the new implementation
        }

        // Event handlers for UI buttons (Save/SaveAs removed - now handled by MainWindow)

        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            ExpandAllFoldings();
        }

        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            CollapseAllFoldings();
        }

        private async void ShowLinksDialog(OrgItem item)
        {
            var links = item.Links;
            if (!links.Any())
            {
                MessageBox.Show("No links found in this item.", "No Links", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var linksList = string.Join("\n", links.Select((link, index) => 
                $"{index + 1}. {link.Description} → {link.Target} ({link.Type})"));
            
            var result = MessageBox.Show(
                $"Links in '{item.Title}':\n\n{linksList}\n\nWould you like to follow the first link?",
                "Item Links",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result == MessageBoxResult.Yes && links.Any())
            {
                await FollowLink(links.First());
            }
        }

        private async Task FollowLink(OrgLink link)
        {
            try
            {
                switch (link.Type)
                {
                    case OrgLinkType.Web:
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = link.Target,
                            UseShellExecute = true
                        });
                        break;

                    case OrgLinkType.File:
                    case OrgLinkType.FileWithTarget:
                        var filePath = await _orgService.ResolveLinkTargetAsync(link);
                        if (File.Exists(filePath))
                        {
                            var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                            mainWindow?.OpenFileInEditor(filePath);
                        }
                        else
                        {
                            MessageBox.Show($"File not found: {filePath}", "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        break;

                    case OrgLinkType.Internal:
                        var targetItem = await _orgService.FollowLinkAsync(link);
                        if (targetItem != null)
                        {
                            // Select the target item in the tree
                            SelectedItem = targetItem;
                            // Optionally expand parents to make it visible
                            ExpandToItem(targetItem);
                        }
                        else
                        {
                            MessageBox.Show($"Internal link target not found: {link.Target}", "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        break;

                    case OrgLinkType.Id:
                        var idItem = await _orgService.FollowLinkAsync(link);
                        if (idItem != null)
                        {
                            SelectedItem = idItem;
                            ExpandToItem(idItem);
                        }
                        else
                        {
                            MessageBox.Show($"ID link target not found: {link.Target}", "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error following link: {ex.Message}", "Link Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExpandToItem(OrgItem item)
        {
            // Expand all parents to make the item visible
            var current = item.Parent;
            while (current != null)
            {
                current.IsExpanded = true;
                current = current.Parent;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        private void UpdateTabHeader()
        {
            if (Parent is TabItem tabItem)
            {
                var headerText = string.IsNullOrEmpty(FilePath) ? "Untitled" : Path.GetFileName(FilePath);
                if (IsModified)
                {
                    headerText += "*";
                }
                tabItem.Header = headerText;
            }
        }

        private void OnOrgStateConfigurationChanged(object sender, Services.OrgStateConfigurationChangedEventArgs e)
        {
            // Configuration changed - no need to reload the entire file, just update UI properties
            System.Diagnostics.Debug.WriteLine("OrgModeTab: Org state configuration changed, updating UI");
            UpdateStatusProperties();
        }
        
        public void Dispose()
        {
            if (_isDisposed) return;
            
            try
            {
                // Unsubscribe from events
                if (_orgService != null)
                {
                    _orgService.ItemChanged -= OnItemChanged;
                }
                
                // Unsubscribe from org state configuration changes
                Services.OrgStateConfigurationService.Instance.ConfigurationChanged -= OnOrgStateConfigurationChanged;
                
                // Dispose timers (auto-save timer removed, but handle gracefully if it exists)
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Stop();
                    _autoSaveTimer.Dispose();
                    _autoSaveTimer = null;
                }
                
                _sourceParseTimer?.Stop();
                _sourceParseTimer?.Dispose();
                
                _stateCyclingDebounceTimer?.Stop();
                _stateCyclingDebounceTimer?.Dispose();
                
                // Uninstall folding manager
                if (_foldingManager != null)
                {
                    FoldingManager.Uninstall(_foldingManager);
                    _foldingManager = null;
                }
                
                _isDisposed = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error disposing OrgModeTab: {ex.Message}");
            }
        }

        private bool CycleTodoStateAtCursor()
        {
            try
            {
                var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
                var lineText = SourceEditor.Document.GetText(line);
                
                System.Diagnostics.Debug.WriteLine($"CycleTodoStateAtCursor: Processing line {line.LineNumber}: '{lineText}' (offset={line.Offset}, length={line.Length})");
                
                // Get configured states for regex pattern from centralized service
                var stateService = Services.OrgStateConfigurationService.Instance;
                var statePattern = stateService.GetStatePattern();
                
                // Parse headline to extract information - use configured states in regex
                // Enhanced regex that excludes CLOSED timestamps from the title
                // Make it more strict to prevent capturing text from multiple lines
                var headlineRegex = $@"^(\*+)\s*(?:({statePattern})\s+)?(?:(\[#[ABC]\])\s+)?([^:\n\r]*?)(?:\s+CLOSED:\s*\[[^\]]+\])*(?:(\s+:[a-zA-Z0-9_@#%:]+:))?\s*$";
                var headlineMatch = System.Text.RegularExpressions.Regex.Match(lineText, headlineRegex);
                
                System.Diagnostics.Debug.WriteLine($"Using regex pattern: {headlineRegex}");
                
                if (headlineMatch.Success)
                {
                    var level = headlineMatch.Groups[1].Value.Length;
                    var currentState = headlineMatch.Groups[2].Value;
                    var title = headlineMatch.Groups[4].Value.Trim();
                    
                    System.Diagnostics.Debug.WriteLine($"Parsing headline: Level={level}, CurrentState='{currentState}', Title='{title}'");
                    
                    // Find the matching org item by level and title
                    var matchingItem = FindOrgItemByLevelAndTitle(level, title);
                    
                    // If we can't find the item, the items might be out of sync with the text
                    // Force a synchronization and try again - but only if we haven't just done a replacement
                    if (matchingItem == null)
                    {
                        // Check if this might be due to a recent text replacement causing temporary corruption
                        var timeSinceLastReplacement = DateTime.Now - _lastTextReplacement;
                        if (timeSinceLastReplacement.TotalMilliseconds < 50)
                        {
                            System.Diagnostics.Debug.WriteLine($"Item not found, but recent replacement detected ({timeSinceLastReplacement.TotalMilliseconds}ms ago) - skipping sync to avoid corruption");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Item not found, forcing immediate parse to sync items with text");
                            try
                            {
                                // Force immediate parsing to sync items with current text
                                // Use GetAwaiter().GetResult() for synchronous execution in UI context
                                var items = _orgService.ParseContentAsync(_sourceDocument.Text).GetAwaiter().GetResult();
                                _orgService.Items.Clear();
                                foreach (var item in items)
                                {
                                    _orgService.Items.Add(item);
                                }
                                
                                // Try to find the item again after sync
                                matchingItem = FindOrgItemByLevelAndTitle(level, title);
                                System.Diagnostics.Debug.WriteLine($"After forced sync, item found: {matchingItem != null}");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error during forced sync: {ex.Message}");
                            }
                        }
                    }
                    
                    if (matchingItem != null)
                    {
                        // Get the next state using centralized service
                        var currentStateForLookup = string.IsNullOrEmpty(currentState) ? "None" : currentState;
                        var nextState = stateService.GetNextState(currentStateForLookup);
                        var nextStateText = nextState?.Name ?? ""; // null means None state
                        
                        System.Diagnostics.Debug.WriteLine($"State transition: {currentStateForLookup} -> {(nextState?.Name ?? "None")}");
                        
                        // Debug: Show current state configuration
                        var allStates = stateService.GetAllStateNames().ToList();
                        System.Diagnostics.Debug.WriteLine($"Available states in cycle: [{string.Join(", ", allStates)}]");
                        
                        // Update the text immediately while preserving cursor position
                        var newLineText = ReplaceStateInHeadline(lineText, currentState, nextStateText);
                        System.Diagnostics.Debug.WriteLine($"Line replacement: '{lineText}' -> '{newLineText}'");
                        
                        // Save cursor position and line info before replacement
                        var originalCaretOffset = SourceEditor.CaretOffset;
                        var originalLineNumber = line.LineNumber;
                        var cursorPositionInLine = originalCaretOffset - line.Offset;
                        
                        // Get precise line boundaries - exclude line delimiter to prevent corruption
                        var startOffset = line.Offset;
                        var length = line.Length; // This excludes the line delimiter automatically
                        
                        // Double-check that we're only replacing the actual line content
                        var actualLineText = SourceEditor.Document.GetText(startOffset, length);
                        if (actualLineText != lineText)
                        {
                            System.Diagnostics.Debug.WriteLine($"WARNING: Line text mismatch detected!");
                            System.Diagnostics.Debug.WriteLine($"Expected: '{lineText}'");
                            System.Diagnostics.Debug.WriteLine($"Actual: '{actualLineText}'");
                            // Use the actual text to be safe
                            lineText = actualLineText;
                            newLineText = ReplaceStateInHeadline(actualLineText, currentState, nextStateText);
                        }
                        
                        // Perform the replacement
                        SourceEditor.Document.Replace(startOffset, length, newLineText);
                        _lastTextReplacement = DateTime.Now;
                        
                        // Get the updated line after replacement
                        var updatedLine = SourceEditor.Document.GetLineByNumber(originalLineNumber);
                        
                        // Simple and safe cursor positioning - just put cursor at start of title
                        var starsMatch = System.Text.RegularExpressions.Regex.Match(newLineText, @"^(\*+)\s*(?:([A-Z]+)\s+)?");
                        var titleStartPos = starsMatch.Success ? starsMatch.Length : 2; // Default to after "* "
                        
                        // Ensure titleStartPos is within bounds
                        titleStartPos = Math.Min(titleStartPos, newLineText.Length);
                        
                        // Calculate new cursor position with strict bounds checking
                        var newCursorPosition = updatedLine.Offset + titleStartPos;
                        
                        // Final safety check - ensure cursor is within document and line bounds
                        newCursorPosition = Math.Max(updatedLine.Offset, 
                                                   Math.Min(newCursorPosition, Math.Min(updatedLine.EndOffset, SourceEditor.Document.TextLength)));
                        
                        System.Diagnostics.Debug.WriteLine($"Cursor: {originalCaretOffset} -> {newCursorPosition} (line {originalLineNumber}, titleStart={titleStartPos}, lineLen={newLineText.Length})");
                        
                        try
                        {
                            SourceEditor.CaretOffset = newCursorPosition;
                        }
                        catch (ArgumentOutOfRangeException ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Cursor positioning failed: {ex.Message}. Falling back to line start.");
                            SourceEditor.CaretOffset = updatedLine.Offset;
                        }
                        
                                                 // Update the model state immediately (for UI consistency) but suppress automatic CLOSED timestamps
                         matchingItem.SuppressAutoClosedTimestamp(true);
                         try
                         {
                             if (!string.IsNullOrEmpty(nextStateText) && Enum.TryParse<OrgState>(nextStateText, out var newStateEnum))
                             {
                                 matchingItem.State = newStateEnum;
                             }
                             else
                             {
                                 matchingItem.State = OrgState.None;
                             }
                         }
                         finally
                         {
                             matchingItem.SuppressAutoClosedTimestamp(false);
                         }
                         
                         // Set up debounced CLOSED timestamp handling
                         _lastCycledItem = matchingItem;
                         _lastCycledState = nextStateText;
                         
                         // Reset the debounce timer - this delays CLOSED timestamp handling until user stops cycling
                         _stateCyclingDebounceTimer.Stop();
                         _stateCyclingDebounceTimer.Start();
                         
                         System.Diagnostics.Debug.WriteLine($"Debounce: Timer reset for '{matchingItem.Title}' -> '{nextStateText}' (CLOSED handling delayed)");
                         System.Diagnostics.Debug.WriteLine($"Debounce: Current item.Closed = {matchingItem.Closed?.ToString() ?? "null"}");
                        
                        return true;
                    }
                    else
                    {
                        // If we still can't find the item even after forced sync, fall back to text-only cycling
                        System.Diagnostics.Debug.WriteLine($"Could not find matching item for level={level}, title='{title}' even after sync, falling back to text-only cycling");
                        return CycleTodoStateInText(line, headlineMatch, currentState);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error cycling TODO state: {ex.Message}");
            }
            
            return false;
        }
        
        private bool CycleTodoStateInText(DocumentLine line, System.Text.RegularExpressions.Match headlineMatch, string currentState)
        {
            try
            {
                // Get the state configuration
                var stateService = Services.OrgStateConfigurationService.Instance;
                
                // Use the currentState parameter directly, don't try to re-parse
                System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: Input currentState='{currentState}'");
                
                // Convert current state to proper format for lookup
                var currentStateForLookup = string.IsNullOrEmpty(currentState) ? "None" : currentState;
                
                // Get next state
                var nextState = stateService.GetNextState(currentStateForLookup);
                var nextStateText = nextState?.Name ?? ""; // null means None state
                
                System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: {currentStateForLookup} -> {(nextState?.Name ?? "None")}");
                
                // Replace the line text while preserving cursor position
                var lineText = SourceEditor.Document.GetText(line);
                var newLineText = ReplaceStateInHeadline(lineText, currentState, nextStateText);
                
                // Save cursor position and line info before replacement
                var originalCaretOffset = SourceEditor.CaretOffset;
                var originalLineNumber = line.LineNumber;
                var cursorPositionInLine = originalCaretOffset - line.Offset;
                
                // Get precise line boundaries - exclude line delimiter to prevent corruption
                var startOffset = line.Offset;
                var length = line.Length; // This excludes the line delimiter automatically
                
                // Double-check that we're only replacing the actual line content
                var actualLineText = SourceEditor.Document.GetText(startOffset, length);
                if (actualLineText != lineText)
                {
                    System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: WARNING: Line text mismatch detected!");
                    System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: Expected: '{lineText}'");
                    System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: Actual: '{actualLineText}'");
                    // Use the actual text to be safe
                    lineText = actualLineText;
                    newLineText = ReplaceStateInHeadline(actualLineText, currentState, nextStateText);
                }
                
                // Update the document
                SourceEditor.Document.Replace(startOffset, length, newLineText);
                _lastTextReplacement = DateTime.Now;
                
                // Get the updated line after replacement
                var updatedLine = SourceEditor.Document.GetLineByNumber(originalLineNumber);
                
                // Simple and safe cursor positioning - just put cursor at start of title
                var starsMatch = System.Text.RegularExpressions.Regex.Match(newLineText, @"^(\*+)\s*(?:([A-Z]+)\s+)?");
                var titleStartPos = starsMatch.Success ? starsMatch.Length : 2; // Default to after "* "
                
                // Ensure titleStartPos is within bounds
                titleStartPos = Math.Min(titleStartPos, newLineText.Length);
                
                // Calculate new cursor position with strict bounds checking
                var newCursorPosition = updatedLine.Offset + titleStartPos;
                
                // Final safety check - ensure cursor is within document and line bounds
                newCursorPosition = Math.Max(updatedLine.Offset, 
                                           Math.Min(newCursorPosition, Math.Min(updatedLine.EndOffset, SourceEditor.Document.TextLength)));
                
                System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText - Cursor: {originalCaretOffset} -> {newCursorPosition} (line {originalLineNumber}, titleStart={titleStartPos}, lineLen={newLineText.Length})");
                
                try
                {
                    SourceEditor.CaretOffset = newCursorPosition;
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText - Cursor positioning failed: {ex.Message}. Falling back to line start.");
                    SourceEditor.CaretOffset = updatedLine.Offset;
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CycleTodoStateInText: Error: {ex.Message}");
                return false;
            }
        }
        
        private string ReplaceStateInHeadline(string lineText, string oldState, string newState)
        {
            System.Diagnostics.Debug.WriteLine($"ReplaceStateInHeadline: Input line: '{lineText}'");
            System.Diagnostics.Debug.WriteLine($"ReplaceStateInHeadline: Replacing '{oldState}' with '{newState}'");
            
            // Simple and robust approach: manually parse the headline components
            var trimmedLine = lineText.Trim();
            
            // Extract stars (level)
            var starsMatch = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^(\*+)");
            if (!starsMatch.Success)
            {
                System.Diagnostics.Debug.WriteLine($"ReplaceStateInHeadline: Not a valid headline, returning original");
                return lineText;
            }
            
            var stars = starsMatch.Groups[1].Value;
            var afterStars = trimmedLine.Substring(stars.Length).TrimStart();
            
            // Get configured states for pattern matching
            var stateService = Services.OrgStateConfigurationService.Instance;
            var allStates = stateService.GetAllStateNames().ToList();
            
            // Check if line starts with a state
            string currentState = "";
            string afterState = afterStars;
            
            foreach (var state in allStates)
            {
                if (afterStars.StartsWith($"{state} "))
                {
                    currentState = state;
                    afterState = afterStars.Substring(state.Length + 1).TrimStart();
                    break;
                }
            }
            
            // Extract priority if present
            string priority = "";
            string afterPriority = afterState;
            var priorityMatch = System.Text.RegularExpressions.Regex.Match(afterState, @"^(\[#[ABC]\])\s*");
            if (priorityMatch.Success)
            {
                priority = priorityMatch.Groups[1].Value;
                afterPriority = afterState.Substring(priorityMatch.Length).TrimStart();
            }
            
            // Extract tags if present (at the end)
            string tags = "";
            string title = afterPriority;
            var tagsMatch = System.Text.RegularExpressions.Regex.Match(afterPriority, @"^(.*?)\s+(:[a-zA-Z0-9_@#%:]+:)\s*$");
            if (tagsMatch.Success)
            {
                title = tagsMatch.Groups[1].Value.Trim();
                tags = tagsMatch.Groups[2].Value;
            }
            
            // Clean the title - remove any CLOSED timestamps that shouldn't be there
            title = System.Text.RegularExpressions.Regex.Replace(title, @"\s*CLOSED:\s*\[[^\]]+\]", "").Trim();
            
            System.Diagnostics.Debug.WriteLine($"ReplaceStateInHeadline: Parsed - Stars:'{stars}', CurrentState:'{currentState}', Priority:'{priority}', Title:'{title}', Tags:'{tags}'");
            
            // Build the new headline
            var result = stars;
            
            // Add new state if not empty
            if (!string.IsNullOrEmpty(newState))
            {
                result += $" {newState}";
            }
            
            // Add priority if present
            if (!string.IsNullOrEmpty(priority))
            {
                result += $" {priority}";
            }
            
            // Add title
            if (!string.IsNullOrEmpty(title))
            {
                result += $" {title}";
            }
            
            // Add tags if present
            if (!string.IsNullOrEmpty(tags))
            {
                result += $" {tags}";
            }
            
            System.Diagnostics.Debug.WriteLine($"ReplaceStateInHeadline: Result: '{result}'");
            return result;
        }
        
        private OrgItem FindOrgItemByLevelAndTitle(int level, string title)
        {
            // Search through all items to find one with matching level and title
            return FindOrgItemRecursive(_orgService.Items, level, title);
        }
        
        private OrgItem FindOrgItemRecursive(IEnumerable<OrgItem> items, int targetLevel, string targetTitle)
        {
            foreach (var item in items)
            {
                if (item.Level == targetLevel && string.Equals(item.Title, targetTitle, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
                
                // Search in children
                var childResult = FindOrgItemRecursive(item.Children, targetLevel, targetTitle);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }

        /// <summary>
        /// Gets the org item that the cursor is currently positioned on
        /// </summary>
        private OrgItem GetOrgItemAtCursor()
        {
            try
            {
                if (SourceEditor?.Document == null)
                    return null;

                var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
                var lineText = SourceEditor.Document.GetText(line);

                // Check if current line is a header
                var headlineRegex = new System.Text.RegularExpressions.Regex(@"^(\s*)(\*+)\s+(?:(TODO|NEXT|STARTED|WAITING|DEFERRED|PROJECT|SOMEDAY|DONE|CANCELLED)\s+)?(?:\[#([ABC])\]\s+)?(.*?)(?:\s+(:[a-zA-Z0-9_@#%:]+:))?$");
                var match = headlineRegex.Match(lineText);

                if (match.Success)
                {
                    var level = match.Groups[2].Value.Length;
                    var titleWithState = match.Groups[5].Value.Trim();
                    
                    // Try to find the org item by level and title
                    var orgItem = FindOrgItemByLevelAndTitle(level, titleWithState);
                    
                    if (orgItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Found item '{orgItem.Title}' at level {level}");
                        return orgItem;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Could not find org item for title '{titleWithState}' at level {level}");
                        
                        // CRITICAL FIX: If item not found, sync editor to service and try again
                        // This handles the case where editor content is ahead of service content
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Item not found, forcing sync to match editor content");
                            
                            // Force synchronous parsing to sync items with current text
                            var items = _orgService.ParseContentAsync(_sourceDocument.Text).GetAwaiter().GetResult();
                            _orgService.Items.Clear();
                            foreach (var item in items)
                            {
                                _orgService.Items.Add(item);
                            }
                            
                            // Try to find the item again after sync
                            orgItem = FindOrgItemByLevelAndTitle(level, titleWithState);
                            if (orgItem != null)
                            {
                                System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Found item after sync: '{orgItem.Title}' (ID: {orgItem.Id})");
                                return orgItem;
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Item still not found after sync");
                            }
                        }
                        catch (Exception syncEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Sync failed: {syncEx.Message}");
                        }
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor: Line '{lineText}' is not a header");
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetOrgItemAtCursor error: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Cycles through configured TODO tags for the current heading (similar to Emacs org-mode C-c C-q)
        /// </summary>
        private bool CycleTagsAtCursor()
        {
            try
            {
                if (SourceEditor?.Document == null)
                    return false;

                var line = SourceEditor.Document.GetLineByOffset(SourceEditor.CaretOffset);
                var lineText = SourceEditor.Document.GetText(line);

                // Check if current line is a header
                var headlineRegex = new System.Text.RegularExpressions.Regex(@"^(\s*)(\*+)\s+(?:(TODO|NEXT|STARTED|WAITING|DEFERRED|PROJECT|SOMEDAY|DONE|CANCELLED)\s+)?(?:\[#([ABC])\]\s+)?(.*?)(?:\s+(:[a-zA-Z0-9_@#%:]+:))?\s*$");
                var match = headlineRegex.Match(lineText);

                if (!match.Success)
                {
                    System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor: Line '{lineText}' is not a header");
                    return false;
                }

                // Extract components
                var indent = match.Groups[1].Value;
                var stars = match.Groups[2].Value;
                var state = match.Groups[3].Value;
                var priority = match.Groups[4].Value;
                var title = match.Groups[5].Value.Trim();
                var currentTags = match.Groups[6].Value;

                // Get configured TODO tags
                var configuredTags = _configProvider?.TodoTags ?? new[] { "work", "personal", "urgent", "project", "meeting", "home" };

                // Parse current tags
                var existingTags = new List<string>();
                if (!string.IsNullOrEmpty(currentTags))
                {
                    existingTags = currentTags.Trim(':').Split(':')
                        .Where(t => !string.IsNullOrEmpty(t))
                        .ToList();
                }

                // Find the next tag to apply
                string nextTag = null;
                if (!existingTags.Any())
                {
                    // No tags, add first configured tag
                    nextTag = configuredTags.FirstOrDefault();
                }
                else
                {
                    // Find current tag in configured list
                    var currentTag = existingTags.FirstOrDefault(tag => configuredTags.Contains(tag, StringComparer.OrdinalIgnoreCase));
                    if (currentTag != null)
                    {
                        var currentIndex = Array.FindIndex(configuredTags, t => t.Equals(currentTag, StringComparison.OrdinalIgnoreCase));
                        if (currentIndex >= 0)
                        {
                            var nextIndex = (currentIndex + 1) % configuredTags.Length;
                            nextTag = configuredTags[nextIndex];
                        }
                    }
                    else
                    {
                        // Current tag not in configured list, start with first
                        nextTag = configuredTags.FirstOrDefault();
                    }
                }

                if (string.IsNullOrEmpty(nextTag))
                    return false;

                // Build new line with updated tag
                var newLine = indent + stars;
                
                if (!string.IsNullOrEmpty(state))
                    newLine += $" {state}";
                
                if (!string.IsNullOrEmpty(priority))
                    newLine += $" {priority}";
                
                newLine += $" {title}";
                
                // Replace or add the tag based on configuration
                var newTags = new List<string>();
                
                // Check if we should replace all tags or preserve manual ones
                bool replaceAllTags = _configProvider?.TagCyclingReplacesAll ?? false;
                
                System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor: Existing tags: [{string.Join(", ", existingTags)}], ReplaceAllTags: {replaceAllTags}");
                
                if (!replaceAllTags)
                {
                    // Preserve non-configured tags (like manual tags)
                    var manualTags = existingTags.Where(tag => !configuredTags.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
                    newTags.AddRange(manualTags);
                    System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor: Preserving manual tags: [{string.Join(", ", manualTags)}]");
                }
                
                // Add the new configured tag
                newTags.Add(nextTag);
                
                System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor: Final tags: [{string.Join(", ", newTags)}]");
                
                if (newTags.Any())
                {
                    newLine += $" :{string.Join(":", newTags)}:";
                }

                // Replace the line
                SourceEditor.Document.Replace(line.Offset, line.Length, newLine);

                System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor: Updated line to '{newLine}'");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CycleTagsAtCursor error: {ex.Message}");
                return false;
            }
        }
    }

    // Helper dialog classes (simplified implementations)
    public class InputDialog : Window
    {
        public string InputText { get; private set; }

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new Label { Content = prompt, Margin = new Thickness(10) };
            Grid.SetRow(label, 0);

            var textBox = new TextBox { Margin = new Thickness(10, 0, 10, 10) };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(5, 0, 5, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(5, 0, 5, 0) };

            okButton.Click += (s, e) => { InputText = textBox.Text; DialogResult = true; };
            cancelButton.Click += (s, e) => { DialogResult = false; };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);

            Content = grid;
            textBox.Focus();
        }
    }

    public class DatePickerDialog : Window
    {
        public DateTime? SelectedDate { get; private set; }

        public DatePickerDialog(string title, DateTime? initialDate)
        {
            Title = title;
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var datePicker = new DatePicker 
            { 
                SelectedDate = initialDate,
                Margin = new Thickness(10)
            };
            Grid.SetRow(datePicker, 0);

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(5, 0, 5, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(5, 0, 5, 0) };
            var clearButton = new Button { Content = "Clear", Width = 75, Margin = new Thickness(5, 0, 5, 0) };

            okButton.Click += (s, e) => { SelectedDate = datePicker.SelectedDate; DialogResult = true; };
            cancelButton.Click += (s, e) => { DialogResult = false; };
            clearButton.Click += (s, e) => { SelectedDate = null; DialogResult = true; };

            buttonPanel.Children.Add(clearButton);
            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);

            Grid.SetRow(buttonPanel, 1);

            grid.Children.Add(datePicker);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }
    }

    public class LinkDialog : Window
    {
        public string LinkTarget { get; private set; }
        public string LinkDescription { get; private set; }

        public LinkDialog()
        {
            Title = "Add Link";
            Width = 400;
            Height = 200;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var targetLabel = new Label { Content = "Link Target (URL, file path, or heading):", Margin = new Thickness(10, 10, 10, 5) };
            Grid.SetRow(targetLabel, 0);

            var targetTextBox = new TextBox { Margin = new Thickness(10, 0, 10, 10) };
            Grid.SetRow(targetTextBox, 1);

            var descLabel = new Label { Content = "Description (optional):", Margin = new Thickness(10, 0, 10, 5) };
            Grid.SetRow(descLabel, 2);

            var descTextBox = new TextBox { Margin = new Thickness(10, 0, 10, 10) };
            Grid.SetRow(descTextBox, 3);

            var buttonPanel = new StackPanel 
            { 
                Orientation = Orientation.Horizontal, 
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(5, 0, 5, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 75, Margin = new Thickness(5, 0, 5, 0) };

            okButton.Click += (s, e) => { 
                LinkTarget = targetTextBox.Text; 
                LinkDescription = descTextBox.Text;
                DialogResult = !string.IsNullOrWhiteSpace(LinkTarget);
            };
            cancelButton.Click += (s, e) => { DialogResult = false; };

            buttonPanel.Children.Add(cancelButton);
            buttonPanel.Children.Add(okButton);

            grid.Children.Add(targetLabel);
            grid.Children.Add(targetTextBox);
            grid.Children.Add(descLabel);
            grid.Children.Add(descTextBox);

            var buttonRow = new RowDefinition { Height = GridLength.Auto };
            grid.RowDefinitions.Add(buttonRow);
            Grid.SetRow(buttonPanel, 4);
            grid.Children.Add(buttonPanel);

            Content = grid;
            targetTextBox.Focus();
        }
    }

    public class AgendaDay
    {
        public DateTime Date { get; set; }
        public string DateHeader { get; set; }
        public ObservableCollection<OrgItem> Items { get; set; }
    }

    public enum ViewMode
    {
        Outline,
        Agenda,
        Source
    }
} 