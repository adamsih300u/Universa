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
        /// Initializes a new instance of the ExportOptions class
        /// </summary>
        public ExportOptions()
        {
            SplitOnHeadingLevels = new List<int>();
            Metadata = new Dictionary<string, string>();
        }
    }
} 