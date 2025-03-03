using System;
using System.Collections.Generic;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Options for document export
    /// </summary>
    public class ExportOptions
    {
        /// <summary>
        /// Defines possible text alignment options
        /// </summary>
        public enum TextAlignment
        {
            Left,
            Center,
            Right,
            Justify
        }
        
        /// <summary>
        /// Gets or sets the output path for the exported document
        /// </summary>
        public string OutputPath { get; set; }
        
        /// <summary>
        /// Gets or sets whether to include a table of contents
        /// </summary>
        public bool IncludeToc { get; set; }
        
        /// <summary>
        /// Gets or sets whether to split the document on headings
        /// </summary>
        public bool SplitOnHeadings { get; set; }
        
        /// <summary>
        /// Gets or sets whether to include a cover page
        /// </summary>
        public bool IncludeCover { get; set; }
        
        /// <summary>
        /// Gets or sets the heading levels to split on
        /// </summary>
        public List<int> SplitOnHeadingLevels { get; set; }
        
        /// <summary>
        /// Gets or sets the document metadata
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; }
        
        /// <summary>
        /// Gets or sets warnings that occurred during export
        /// </summary>
        public List<string> Warnings { get; set; }
        
        /// <summary>
        /// Gets or sets the alignment for each heading level (1-6)
        /// </summary>
        public Dictionary<int, TextAlignment> HeadingAlignments { get; set; }
        
        /// <summary>
        /// Initializes a new instance of the ExportOptions class
        /// </summary>
        public ExportOptions()
        {
            SplitOnHeadingLevels = new List<int>();
            Metadata = new Dictionary<string, string>();
            Warnings = new List<string>();
            HeadingAlignments = new Dictionary<int, TextAlignment>();
            
            // Set default alignments
            for (int i = 1; i <= 6; i++)
            {
                HeadingAlignments[i] = TextAlignment.Left;
            }
        }
    }
} 