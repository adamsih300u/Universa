using System;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using Universa.Desktop.Helpers;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Handles UI events and keyboard shortcuts for org-mode editor
    /// </summary>
    public class OrgModeUIEventHandler : IOrgModeUIEventHandler
    {
        private TextEditor _editor;
        private IOrgModeFoldingManager _foldingManager;
        
        public event EventHandler<TodoStateCycleEventArgs> TodoStateCycleRequested;
        public event EventHandler<TagCycleEventArgs> TagCycleRequested;
        public event EventHandler RefileRequested;
        public event EventHandler<FoldedHeaderEnterEventArgs> FoldedHeaderEnterRequested;
        
        public OrgModeUIEventHandler(IOrgModeFoldingManager foldingManager)
        {
            _foldingManager = foldingManager ?? throw new ArgumentNullException(nameof(foldingManager));
        }
        
        public void Initialize(TextEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            SetupKeyboardShortcuts();
        }
        
        private void SetupKeyboardShortcuts()
        {
            // Use PreviewKeyDown to intercept Enter before AvalonEdit's internal handlers
            _editor.TextArea.PreviewKeyDown += OnPreviewKeyDown;
            _editor.TextArea.KeyDown += OnKeyDown;
        }
        
        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter - Handle new lines, list items, and folded headers FIRST
            if (e.Key == Key.Enter && !e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                System.Diagnostics.Debug.WriteLine($"PreviewKeyDown: Enter key pressed at offset {_editor.CaretOffset}");
                
                // First check if we're at the end of a folded header line
                var args = new FoldedHeaderEnterEventArgs { CursorPosition = _editor.CaretOffset };
                FoldedHeaderEnterRequested?.Invoke(this, args);
                
                if (args.Handled)
                {
                    System.Diagnostics.Debug.WriteLine("HandleEnterOnFoldedHeader succeeded - handling enter");
                    e.Handled = true;
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine("No special Enter handling - letting AvalonEdit handle it");
            }

            // Backspace - Prevent deletion of folded content
            if (e.Key == Key.Back)
            {
                System.Diagnostics.Debug.WriteLine($"Backspace key detected. Caret offset: {_editor.CaretOffset}");
                var caretOffset = _editor.CaretOffset;

                if (_foldingManager != null)
                {
                    // Get the line the caret is on
                    var line = _editor.Document.GetLineByOffset(caretOffset);

                    // Check if the caret is at the visual end of the line's content
                    if (caretOffset == line.EndOffset - line.DelimiterLength)
                    {
                        // This would require access to folding manager's internals
                        // For now, delegate this to the folding manager
                        // TODO: Add method to folding manager for backspace protection
                        System.Diagnostics.Debug.WriteLine("Backspace protection delegated to folding manager");
                    }
                }
            }
        }
        
        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            // Folding shortcuts
            HandleFoldingShortcuts(e);
            
            // TAB/Shift+TAB for proper org mode behavior (promote/demote headers, indent lists)
            if (e.Key == Key.Tab)
            {
                if (HandleTabKey(e)) return;
            }

            // Ctrl+Return for state cycling
            if (e.Key == Key.Return && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var args = new TodoStateCycleEventArgs { CursorPosition = _editor.CaretOffset };
                TodoStateCycleRequested?.Invoke(this, args);
                if (args.Handled)
                {
                    e.Handled = true;
                }
                return;
            }

            // Ctrl+R for refile
            if (e.Key == Key.R && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
            {
                RefileRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }

            // Ctrl+Shift+T for tag cycling
            if (e.Key == Key.T && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
            {
                var args = new TagCycleEventArgs { CursorPosition = _editor.CaretOffset };
                TagCycleRequested?.Invoke(this, args);
                if (args.Handled)
                {
                    e.Handled = true;
                }
                return;
            }
            
            // Ctrl+Tab for folding (since Tab now does promote/demote)
            if (e.Key == Key.Tab && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
            {
                var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
                var lineText = _editor.Document.GetText(line);
                
                if (System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\s*\*+\s+"))
                {
                    _foldingManager?.TryToggleFoldingAtCursor(true);
                    e.Handled = true;
                }
                return;
            }
        }
        
        private void HandleFoldingShortcuts(KeyEventArgs e)
        {
            // Ctrl+Shift+[ - Collapse all foldings
            if (e.Key == Key.OemOpenBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
            {
                _foldingManager?.CollapseAll();
                e.Handled = true;
                return;
            }
            
            // Ctrl+Shift+] - Expand all foldings
            if (e.Key == Key.OemCloseBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift))
            {
                _foldingManager?.ExpandAll();
                e.Handled = true;
                return;
            }
            
            // Ctrl+[ - Collapse current folding
            if (e.Key == Key.OemOpenBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _foldingManager?.CollapseCurrentFolding();
                e.Handled = true;
                return;
            }
            
            // Ctrl+] - Expand current folding
            if (e.Key == Key.OemCloseBrackets && e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Control))
            {
                _foldingManager?.ExpandCurrentFolding();
                e.Handled = true;
                return;
            }
        }
        
        private bool HandleTabKey(KeyEventArgs e)
        {
            bool isShiftPressed = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
            
            // Use OrgModeListSupport for list handling
            if (OrgModeListSupport.HandleTabKey(_editor, isShiftPressed))
            {
                e.Handled = true;
                return true;
            }
            
            // Handle header promotion/demotion
            var line = _editor.Document.GetLineByOffset(_editor.CaretOffset);
            var lineText = _editor.Document.GetText(line);
            
            // Check if current line is a header
            var headerMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"^(\s*)(\*+)(\s+.*)$");
            if (headerMatch.Success)
            {
                var indent = headerMatch.Groups[1].Value;
                var stars = headerMatch.Groups[2].Value;
                var titlePart = headerMatch.Groups[3].Value;
                
                string newLineText;
                if (isShiftPressed)
                {
                    // Promote (reduce level)
                    if (stars.Length > 1)
                    {
                        newLineText = indent + stars.Substring(1) + titlePart;
                    }
                    else
                    {
                        return false; // Can't promote further
                    }
                }
                else
                {
                    // Demote (increase level)
                    newLineText = indent + "*" + stars + titlePart;
                }
                
                // Replace the line
                _editor.Document.Replace(line.Offset, line.Length, newLineText);
                e.Handled = true;
                return true;
            }
            
            return false;
        }
    }
} 