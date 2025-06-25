using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Folding strategy for markdown files that creates foldable regions based on header levels
    /// </summary>
    public class MarkdownFoldingStrategy
    {
        private static readonly Regex HeaderRegex = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);
        
        /// <summary>
        /// Creates folding sections for markdown headers
        /// </summary>
        public IEnumerable<NewFolding> CreateNewFoldings(TextDocument document, out int firstErrorOffset)
        {
            firstErrorOffset = -1;
            var foldings = new List<NewFolding>();
            
            if (document == null || document.TextLength == 0)
                return foldings;
            
            var headerStack = new Stack<HeaderInfo>();
            
            System.Diagnostics.Debug.WriteLine($"Creating markdown foldings for document with {document.LineCount} lines");
            
            // Use AvalonEdit's document line iteration
            for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
            {
                var line = document.GetLineByNumber(lineNumber);
                var lineText = document.GetText(line);
                var match = HeaderRegex.Match(lineText);
                
                if (match.Success)
                {
                    var level = match.Groups[1].Value.Length; // Number of # characters
                    var title = match.Groups[2].Value.Trim();
                    
                    System.Diagnostics.Debug.WriteLine($"Found header at line {lineNumber}: Level {level}, Title '{title}'");
                    
                    // Close headers at the same or deeper level
                    while (headerStack.Count > 0 && headerStack.Peek().Level >= level)
                    {
                        var headerToClose = headerStack.Pop();
                        var endOffset = line.Offset;
                        
                        if (endOffset > headerToClose.StartOffset)
                        {
                            var folding = CreateFolding(document, headerToClose, endOffset);
                            if (folding != null)
                            {
                                foldings.Add(folding);
                                System.Diagnostics.Debug.WriteLine($"Created folding: {folding.StartOffset} to {folding.EndOffset}");
                            }
                        }
                    }
                    
                    // Push new header onto stack
                    var startOffset = line.EndOffset; // Start folding after the header line
                    headerStack.Push(new HeaderInfo
                    {
                        Level = level,
                        Title = title,
                        StartOffset = startOffset,
                        LineNumber = lineNumber
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
            System.Diagnostics.Debug.WriteLine($"Total markdown foldings created: {validFoldings.Count}");
            
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
                
                System.Diagnostics.Debug.WriteLine($"Updating markdown foldings: {validFoldings.Count} valid out of {newFoldings.Count()} total");
                
                manager.UpdateFoldings(validFoldings, firstErrorOffset);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating markdown foldings: {ex.Message}");
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
                // Adjust end offset to not include trailing whitespace
                while (endOffset > header.StartOffset && char.IsWhiteSpace(document.GetCharAt(endOffset - 1)))
                {
                    endOffset--;
                }
                
                if (endOffset <= header.StartOffset)
                    return null;
                
                return new NewFolding(header.StartOffset, endOffset)
                {
                    Name = header.Title,
                    DefaultClosed = false
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating folding for header '{header.Title}': {ex.Message}");
                return null;
            }
        }
        
        private struct HeaderInfo
        {
            public int Level;
            public string Title;
            public int StartOffset;
            public int LineNumber;
        }
    }
} 