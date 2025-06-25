using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Folding strategy for org-mode files that creates foldable regions based on header levels
    /// </summary>
    public class OrgModeFoldingStrategy
    {
        private static readonly Regex HeaderRegex = new Regex(@"^(\*+)\s+(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);
        
        /// <summary>
        /// Creates folding sections for org-mode headers
        /// </summary>
        public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
        {
            firstErrorOffset = -1;
            var foldings = new List<NewFolding>();
            
            if (document == null || document.TextLength == 0)
                return foldings;
            
            var headerStack = new Stack<HeaderInfo>();
            
            System.Diagnostics.Debug.WriteLine($"Creating foldings for document with {document.LineCount} lines");
            
            // Use AvalonEdit's document line iteration instead of string splitting
            for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
            {
                var documentLine = document.GetLineByNumber(lineNumber);
                var lineText = document.GetText(documentLine);
                var match = HeaderRegex.Match(lineText);
                
                if (match.Success)
                {
                    var level = match.Groups[1].Value.Length; // Number of * characters
                    var title = match.Groups[2].Value.Trim();
                    var lineStart = documentLine.Offset;
                    
                    System.Diagnostics.Debug.WriteLine($"Found header at line {lineNumber}: Level {level}, '{title}' (offset {lineStart})");
                    
                    // Close any headers at the same level or deeper
                    while (headerStack.Count > 0 && headerStack.Peek().Level >= level)
                    {
                        var headerToClose = headerStack.Pop();
                        
                        // The fold must end on the line just before the new header starts.
                        // This prevents the new header from being included in the previous section's fold.
                        var previousLine = document.GetLineByNumber(lineNumber - 1);
                        var endOffset = previousLine.EndOffset;

                        System.Diagnostics.Debug.WriteLine($"Closing header '{headerToClose.Title}' from {headerToClose.StartOffset} to {endOffset} (before line {lineNumber})");
                        
                        if (endOffset > headerToClose.StartOffset)
                        {
                            // We pass the raw endOffset here; CreateFolding will calculate the safe boundary.
                            var folding = CreateFolding(document, headerToClose, endOffset);
                            if (folding != null)
                            {
                                foldings.Add(folding);
                                System.Diagnostics.Debug.WriteLine($"Created folding: {folding.StartOffset} to {folding.EndOffset}");
                            }
                        }
                    }
                    
                    // Add new header to stack
                    headerStack.Push(new HeaderInfo
                    {
                        Level = level,
                        Title = title,
                        StartOffset = lineStart,
                        LineIndex = lineNumber - 1 // Convert to 0-based for consistency
                    });
                }
            }
            
            // Close remaining headers at end of document
            while (headerStack.Count > 0)
            {
                var headerToClose = headerStack.Pop();
                var endOffset = document.TextLength;
                
                System.Diagnostics.Debug.WriteLine($"Closing final header '{headerToClose.Title}' from {headerToClose.StartOffset} to {endOffset}");
                
                if (endOffset > headerToClose.StartOffset)
                {
                    var folding = CreateFolding(document, headerToClose, endOffset);
                    if (folding != null)
                    {
                        foldings.Add(folding);
                        System.Diagnostics.Debug.WriteLine($"Created final folding: {folding.StartOffset} to {folding.EndOffset}");
                    }
                }
            }
            
            var validFoldings = foldings.Where(f => f != null).OrderBy(f => f.StartOffset).ToList();
            System.Diagnostics.Debug.WriteLine($"Total foldings created: {validFoldings.Count}");
            
            return validFoldings;
        }
        
        /// <summary>
        /// Updates the foldings in the folding manager using this strategy
        /// </summary>
        public void UpdateFoldings(FoldingManager manager, TextDocument document)
        {
            try
            {
                int firstErrorOffset;
                var newFoldings = CreateNewFoldings(document, out firstErrorOffset);
                
                // Validate foldings before applying them
                var validFoldings = newFoldings.Where(f => ValidateFolding(f, document)).ToList();
                
                System.Diagnostics.Debug.WriteLine($"Updating foldings: {validFoldings.Count} valid out of {newFoldings.Count()} total");
                
                manager.UpdateFoldings(validFoldings, firstErrorOffset);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating foldings: {ex.Message}");
            }
        }
        
        private bool ValidateFolding(NewFolding folding, TextDocument document)
        {
            // Basic validation
            if (folding == null)
                return false;
                
            if (folding.StartOffset < 0 || folding.EndOffset < 0)
                return false;
                
            if (folding.StartOffset >= folding.EndOffset)
                return false;
                
            if (folding.EndOffset > document.TextLength)
                return false;
                
            // Ensure the folding doesn't start and end on the same line
            try
            {
                var startLine = document.GetLineByOffset(folding.StartOffset);
                var endLine = document.GetLineByOffset(folding.EndOffset);
                
                if (startLine.LineNumber >= endLine.LineNumber)
                    return false;
            }
            catch (Exception)
            {
                return false;
            }
            
            return true;
        }
        
        private NewFolding CreateFolding(TextDocument document, HeaderInfo header, int endOffset)
        {
            try
            {
                // Find the header line
                var headerLine = document.GetLineByNumber(header.LineIndex + 1);
                
                // The fold should start *after* the header line, including its newline characters.
                var foldStart = headerLine.EndOffset;

                // Only create folding if there's meaningful content after the header.
                if (endOffset <= foldStart)
                {
                    System.Diagnostics.Debug.WriteLine($"Skipping folding for '{header.Title}': no content after header (start: {foldStart}, end: {endOffset})");
                    return null;
                }

                var folding = new NewFolding(foldStart, endOffset)
                {
                    Name = "...", // Use "..." for the folded text
                    DefaultClosed = false
                };
                
                System.Diagnostics.Debug.WriteLine($"Successfully created folding for '{header.Title}': {foldStart} to {endOffset}");
                return folding;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating folding for '{header.Title}': {ex.Message}");
                return null;
            }
        }
        
        private class HeaderInfo
        {
            public int Level { get; set; }
            public string Title { get; set; }
            public int StartOffset { get; set; }
            public int LineIndex { get; set; }
        }
    }
} 