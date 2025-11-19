using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using Universa.Desktop.Helpers;
using Universa.Desktop.Interfaces;
using Timer = System.Timers.Timer;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// High-performance folding manager for org-mode with throttling and intelligent updates
    /// </summary>
    public class OrgModeFoldingManager : IOrgModeFoldingManager
    {
        private TextEditor _editor;
        private FoldingManager _foldingManager;
        private OrgModeFoldingStrategy _foldingStrategy;
        private Timer _updateTimer;
        private bool _isDisposed;
        private bool _updatePending;
        private readonly object _updateLock = new object();
        private CancellationTokenSource _updateCancellationSource;
        
        // Performance optimization
        private const int UPDATE_DELAY_MS = 500; // Throttle updates to max every 500ms
        private string _lastDocumentHash = string.Empty;
        private int _lastDocumentLength = 0;
        
        public event EventHandler FoldingStateChanged;
        
        public OrgModeFoldingManager()
        {
            _foldingStrategy = new OrgModeFoldingStrategy();
            
            // Set up throttled update timer
            _updateTimer = new Timer(UPDATE_DELAY_MS) { AutoReset = false };
            _updateTimer.Elapsed += OnUpdateTimerElapsed;
        }
        
        public void Initialize(TextEditor editor)
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(OrgModeFoldingManager));
            
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            
            // Install folding manager
            if (_foldingManager == null && _editor.Document != null)
            {
                _foldingManager = FoldingManager.Install(_editor.TextArea);
                
                // Initial folding update
                ForceUpdateFoldings();
                
                // Subscribe to document changes for throttled updates
                _editor.Document.TextChanged += OnDocumentTextChanged;
            }
        }
        
        private void OnDocumentTextChanged(object sender, EventArgs e)
        {
            if (_isDisposed || _editor?.Document == null) return;
            
            // Throttle updates - only update after user stops typing
            lock (_updateLock)
            {
                _updatePending = true;
                _updateTimer.Stop();
                _updateTimer.Start();
            }
        }
        
        private async void OnUpdateTimerElapsed(object sender, ElapsedEventArgs e)
        {
            if (_isDisposed) return;
            
            try
            {
                await UpdateFoldingsAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeFoldingManager: Error in timer update: {ex.Message}");
            }
        }
        
        public async Task UpdateFoldingsAsync(CancellationToken cancellationToken = default)
        {
            if (_isDisposed || _foldingManager == null || _editor?.Document == null) return;
            
            try
            {
                // Cancel any pending update
                _updateCancellationSource?.Cancel();
                _updateCancellationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                
                var token = _updateCancellationSource.Token;
                
                // Quick check if document actually changed
                var currentLength = _editor.Document.TextLength;
                var currentHash = await Task.Run(() => GetDocumentHash(_editor.Document), token);
                
                if (currentHash == _lastDocumentHash && currentLength == _lastDocumentLength)
                {
                    return; // No changes, skip update
                }
                
                // Perform folding update off UI thread
                await Task.Run(() =>
                {
                    if (token.IsCancellationRequested) return;
                    
                    // Dispatch back to UI thread for folding update
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            if (token.IsCancellationRequested || _isDisposed) return;
                            
                            _foldingStrategy.UpdateFoldings(_foldingManager, _editor.Document);
                            
                            // Update cache
                            _lastDocumentHash = currentHash;
                            _lastDocumentLength = currentLength;
                            
                            // Reset pending flag
                            lock (_updateLock)
                            {
                                _updatePending = false;
                            }
                            
                            FoldingStateChanged?.Invoke(this, EventArgs.Empty);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"OrgModeFoldingManager: Error updating foldings: {ex.Message}");
                        }
                    }));
                }, token);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeFoldingManager: Error in UpdateFoldingsAsync: {ex.Message}");
            }
        }
        
        public void ForceUpdateFoldings()
        {
            if (_isDisposed || _foldingManager == null || _editor?.Document == null) return;
            
            try
            {
                _foldingStrategy.UpdateFoldings(_foldingManager, _editor.Document);
                
                // Update cache
                _lastDocumentHash = GetDocumentHash(_editor.Document);
                _lastDocumentLength = _editor.Document.TextLength;
                
                FoldingStateChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeFoldingManager: Error in ForceUpdateFoldings: {ex.Message}");
            }
        }
        
        public void CollapseAll()
        {
            if (_isDisposed || _foldingManager == null) return;
            
            foreach (var folding in _foldingManager.AllFoldings)
            {
                folding.IsFolded = true;
            }
            
            FoldingStateChanged?.Invoke(this, EventArgs.Empty);
        }
        
        public void ExpandAll()
        {
            if (_isDisposed || _foldingManager == null) return;
            
            foreach (var folding in _foldingManager.AllFoldings)
            {
                folding.IsFolded = false;
            }
            
            FoldingStateChanged?.Invoke(this, EventArgs.Empty);
        }
        
        public bool TryToggleFoldingAtCursor(bool expand = true)
        {
            if (_isDisposed || _foldingManager == null || _editor == null) return false;
            
            var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
            var lineText = _editor.Document.GetText(line);
            
            // Check if current line is a header
            if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
            {
                var folding = _foldingManager.GetFoldingsAt(line.Offset).FirstOrDefault();
                if (folding != null)
                {
                    folding.IsFolded = expand ? !folding.IsFolded : false;
                    FoldingStateChanged?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }
            return false;
        }
        
        public void CollapseCurrentFolding()
        {
            var folding = FindFoldingForCurrentPosition();
            if (folding != null)
            {
                folding.IsFolded = true;
                FoldingStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        public void ExpandCurrentFolding()
        {
            var folding = FindFoldingForCurrentPosition();
            if (folding != null)
            {
                folding.IsFolded = false;
                FoldingStateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        
        public bool HandleEnterOnFoldedHeader()
        {
            if (_isDisposed || _foldingManager == null || _editor == null) return false;
            
            var caretOffset = _editor.CaretOffset;
            var line = _editor.Document.GetLineByOffset(caretOffset);
            var lineText = _editor.Document.GetText(line);
            
            // Check if current line is a header
            var headerMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"^(\s*)(\*+)\s+(.*)$");
            if (!headerMatch.Success) return false;
            
            // Find any folded section that starts right after this line
            var allFoldings = _foldingManager.AllFoldings.Where(f => f.IsFolded).ToList();
            var lineEnd = line.EndOffset;
            var associatedFolding = allFoldings.FirstOrDefault(f => f.StartOffset == lineEnd);
            
            if (associatedFolding != null)
            {
                // Unfold the region
                associatedFolding.IsFolded = false;
                
                // Position cursor at the end of the unfolded content (without adding extra newlines)
                var insertPosition = associatedFolding.EndOffset;
                _editor.CaretOffset = insertPosition;
                
                // Manually trigger update
                ForceUpdateFoldings();
                
                return true;
            }
            
            return false;
        }
        
        private FoldingSection FindFoldingForCurrentPosition()
        {
            if (_isDisposed || _foldingManager == null || _editor == null) return null;
            
            var caretOffset = _editor.CaretOffset;
            var line = _editor.Document.GetLineByOffset(caretOffset);
            var lineText = _editor.Document.GetText(line);
            
            // Check if current line is a header
            if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
            {
                // For header lines, find folding that starts after this line
                var lineEnd = line.EndOffset;
                return _foldingManager.AllFoldings
                    .Where(f => f.StartOffset >= lineEnd && f.StartOffset <= lineEnd + 50)
                    .OrderBy(f => f.StartOffset)
                    .FirstOrDefault();
            }
            else
            {
                // For non-header lines, find containing folding
                return _foldingManager.GetFoldingsAt(caretOffset).FirstOrDefault() ??
                       _foldingManager.AllFoldings
                           .Where(f => f.StartOffset <= caretOffset && f.EndOffset >= caretOffset)
                           .OrderByDescending(f => f.StartOffset)
                           .FirstOrDefault();
            }
        }
        
        private string GetDocumentHash(TextDocument document)
        {
            // Simple hash based on length and first/last 100 chars for performance
            if (document.TextLength == 0) return "empty";
            
            var start = document.TextLength > 100 ? document.GetText(0, 100) : document.Text;
            var end = document.TextLength > 200 ? document.GetText(document.TextLength - 100, 100) : "";
            
            return $"{document.TextLength}:{start.GetHashCode()}:{end.GetHashCode()}";
        }
        
        public void Dispose()
        {
            if (_isDisposed) return;
            
            _isDisposed = true;
            
            try
            {
                // Unsubscribe from events
                if (_editor?.Document != null)
                {
                    _editor.Document.TextChanged -= OnDocumentTextChanged;
                }
                
                // Cancel any pending operations
                _updateCancellationSource?.Cancel();
                _updateCancellationSource?.Dispose();
                
                // Dispose timer
                _updateTimer?.Stop();
                _updateTimer?.Dispose();
                
                // Uninstall folding manager
                if (_foldingManager != null)
                {
                    FoldingManager.Uninstall(_foldingManager);
                    _foldingManager = null;
                }
                
                _editor = null;
                _foldingStrategy = null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeFoldingManager: Error during disposal: {ex.Message}");
            }
        }
    }
} 