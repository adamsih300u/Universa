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
        /// Gets or sets the font family for each heading level (1-6)
        /// </summary>
        public Dictionary<int, string> HeadingFontFamilies { get; set; }
        
        /// <summary>
        /// Gets or sets the font size for each heading level (1-6)
        /// </summary>
        public Dictionary<int, double> HeadingFontSizes { get; set; }
        
        /// <summary>
        /// Gets or sets the font family for body text
        /// </summary>
        public string BodyFontFamily { get; set; }
        
        /// <summary>
        /// Gets or sets the font size for body text
        /// </summary>
        public double BodyFontSize { get; set; }
        
        /// <summary>
        /// Gets or sets the alignment for body text
        /// </summary>
        public TextAlignment BodyTextAlignment { get; set; }
        
        /// <summary>
        /// Initializes a new instance of the ExportOptions class
        /// </summary>
        public ExportOptions()
        {
            SplitOnHeadingLevels = new List<int>();
            Metadata = new Dictionary<string, string>();
            Warnings = new List<string>();
            HeadingAlignments = new Dictionary<int, TextAlignment>();
            HeadingFontFamilies = new Dictionary<int, string>();
            HeadingFontSizes = new Dictionary<int, double>();
            
            // Set default alignments
            for (int i = 1; i <= 6; i++)
            {
                HeadingAlignments[i] = TextAlignment.Left;
                HeadingFontFamilies[i] = "Arial";
                
                // Default heading sizes (decreasing for each level)
                switch (i)
                {
                    case 1: HeadingFontSizes[i] = 18; break;
                    case 2: HeadingFontSizes[i] = 16; break;
                    case 3: HeadingFontSizes[i] = 14; break;
                    case 4: HeadingFontSizes[i] = 13; break;
                    case 5: HeadingFontSizes[i] = 12; break;
                    case 6: HeadingFontSizes[i] = 11; break;
                }
            }
            
            // Default body text settings
            BodyFontFamily = "Arial";
            BodyFontSize = 11;
            BodyTextAlignment = TextAlignment.Left;
        }
    }
} 