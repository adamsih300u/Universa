using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;
using System.Windows.Media;
using System.Windows;
using Universa.Desktop.Helpers;

namespace Universa.Desktop.Controls
{
    /// <summary>
    /// Enhanced TextEditor for org-mode files with folding and click interactions
    /// </summary>
    public class OrgModeEditor : TextEditor
    {
        private static readonly Regex HeaderRegex = new Regex(@"^(\*+)\s+(.*)$", RegexOptions.Compiled);
        private OrgModeInlineFormatter _inlineFormatter;
        
        public OrgModeEditor()
        {
            // Set up inline formatting
            _inlineFormatter = new OrgModeInlineFormatter();
            this.TextArea.TextView.LineTransformers.Add(_inlineFormatter);
            
            // Set up mouse handling for click-to-fold
            this.TextArea.TextView.MouseDown += TextView_MouseDown;
            this.TextArea.TextView.VisualLinesChanged += TextView_VisualLinesChanged;
        }
        
        private void TextView_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1 && e.ChangedButton == MouseButton.Left)
            {
                var position = this.GetPositionFromPoint(e.GetPosition(this.TextArea.TextView));
                if (position.HasValue)
                {
                    var line = this.Document.GetLineByNumber(position.Value.Line);
                    var lineText = this.Document.GetText(line);
                    var match = HeaderRegex.Match(lineText);
                    
                    if (match.Success)
                    {
                        // Check if click was on the header stars
                        var clickColumn = position.Value.Column;
                        var starsLength = match.Groups[1].Value.Length;
                        
                        if (clickColumn <= starsLength + 1) // +1 for the space after stars
                        {
                            ToggleFoldingForLine(line);
                            e.Handled = true;
                        }
                    }
                }
            }
        }
        
        private void TextView_VisualLinesChanged(object sender, EventArgs e)
        {
            // Add visual indicators for foldable headers
            UpdateHeaderVisuals();
        }
        
        private void UpdateHeaderVisuals()
        {
            // This would add visual indicators like [+]/[-] or ▶/▼ next to headers
            // For now, we rely on AvalonEdit's built-in folding UI
        }
        
        private void ToggleFoldingForLine(DocumentLine line)
        {
            var foldingManager = this.TextArea.GetService(typeof(FoldingManager)) as FoldingManager;
            if (foldingManager != null)
            {
                var foldings = foldingManager.GetFoldingsAt(line.Offset);
                var folding = foldings.FirstOrDefault();
                
                if (folding != null)
                {
                    folding.IsFolded = !folding.IsFolded;
                }
            }
        }
        
        /// <summary>
        /// Cycles through folding states: FOLDED → CHILDREN → SUBTREE → FOLDED
        /// This mimics Emacs org-mode TAB behavior
        /// </summary>
        public void CycleFoldingAtCursor()
        {
            var line = this.Document.GetLineByOffset(this.CaretOffset);
            var lineText = this.Document.GetText(line);
            var match = HeaderRegex.Match(lineText);
            
            if (match.Success)
            {
                var foldingManager = this.TextArea.GetService(typeof(FoldingManager)) as FoldingManager;
                if (foldingManager != null)
                {
                    var currentLevel = match.Groups[1].Value.Length;
                    CycleFoldingState(foldingManager, line, currentLevel);
                }
            }
        }
        
        private void CycleFoldingState(FoldingManager foldingManager, DocumentLine headerLine, int headerLevel)
        {
            // Get all foldings at this level and deeper
            var allFoldings = foldingManager.AllFoldings.ToList();
            var relevantFoldings = allFoldings.Where(f => 
                f.StartOffset >= headerLine.Offset && 
                IsWithinSection(f, headerLine, headerLevel)).ToList();
            
            if (!relevantFoldings.Any()) return;
            
            // Determine current state and cycle to next
            var currentState = GetCurrentFoldingState(relevantFoldings, headerLevel);
            
            switch (currentState)
            {
                case FoldingState.Expanded:
                    // Fold this section only
                    SetFoldingState(relevantFoldings, headerLevel, true, false);
                    break;
                    
                case FoldingState.Folded:
                    // Show children but fold subsections
                    SetFoldingState(relevantFoldings, headerLevel, false, true);
                    break;
                    
                case FoldingState.Children:
                    // Expand everything
                    SetFoldingState(relevantFoldings, headerLevel, false, false);
                    break;
            }
        }
        
        private FoldingState GetCurrentFoldingState(System.Collections.Generic.List<FoldingSection> foldings, int headerLevel)
        {
            var thisLevelFoldings = foldings.Where(f => GetFoldingLevel(f) == headerLevel).ToList();
            var childFoldings = foldings.Where(f => GetFoldingLevel(f) > headerLevel).ToList();
            
            if (thisLevelFoldings.Any(f => f.IsFolded))
                return FoldingState.Folded;
                
            if (childFoldings.Any(f => f.IsFolded))
                return FoldingState.Children;
                
            return FoldingState.Expanded;
        }
        
        private void SetFoldingState(System.Collections.Generic.List<FoldingSection> foldings, int headerLevel, bool foldThis, bool foldChildren)
        {
            foreach (var folding in foldings)
            {
                var level = GetFoldingLevel(folding);
                if (level == headerLevel)
                {
                    folding.IsFolded = foldThis;
                }
                else if (level > headerLevel)
                {
                    folding.IsFolded = foldChildren;
                }
            }
        }
        
        private bool IsWithinSection(FoldingSection folding, DocumentLine headerLine, int headerLevel)
        {
            // Check if this folding belongs to the section starting at headerLine
            // This is a simplified version - a more complete implementation would
            // properly parse the org structure
            return folding.StartOffset > headerLine.Offset;
        }
        
        private int GetFoldingLevel(FoldingSection folding)
        {
            // Get the header level for this folding by examining the line
            var line = this.Document.GetLineByOffset(folding.StartOffset);
            var lineText = this.Document.GetText(line);
            var match = HeaderRegex.Match(lineText);
            
            return match.Success ? match.Groups[1].Value.Length : 1;
        }
        
        private enum FoldingState
        {
            Expanded,   // All content visible
            Folded,     // Section is folded
            Children    // Section expanded but subsections folded
        }
    }
} 