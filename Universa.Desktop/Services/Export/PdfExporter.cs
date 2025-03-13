using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Snippets.Font;
using System.Text.Unicode;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// PDF exporter using PdfSharp library
    /// </summary>
    public class PdfExporter : IExporter
    {
        // Regular expressions for parsing markdown
        private static readonly Regex _headingRegex = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
        private static readonly Regex _imageRegex = new Regex(@"!\[(.*?)\]\((.+?)\)", RegexOptions.Compiled);
        private static readonly Regex _linkRegex = new Regex(@"\[(.*?)\]\((.+?)\)", RegexOptions.Compiled);
        private static readonly Regex _italicRegex = new Regex(@"(?<!\*)\*([^\*]+)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex _boldRegex = new Regex(@"\*\*([^\*]+)\*\*", RegexOptions.Compiled);
        
        // Additional regex for paragraph detection - improved to handle more markdown content patterns
        private static readonly Regex _paragraphRegex = new Regex(@"(?<=(\n\n|\r\n\r\n|^))(?!\s*#{1,6}\s+)(.+?)(?=(\n\n|\r\n\r\n|$))", RegexOptions.Singleline | RegexOptions.Compiled);
        
        // Dictionary to store heading page numbers for TOC
        private Dictionary<string, int> _headingPageNumbers;
        
        // Initialize font resolver for PdfSharp
        static PdfExporter()
        {
            // Register the global font resolver
            GlobalFontSettings.FontResolver = new FailsafeFontResolver();
            
            // Enable Unicode support
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        }
        
        /// <summary>
        /// Exports the document content to PDF format
        /// </summary>
        public async Task<bool> ExportAsync(string content, ExportOptions options)
        {
            try
            {
                Debug.WriteLine("Starting PDF export...");
                Debug.WriteLine($"Content length: {content?.Length ?? 0}");
                Debug.WriteLine($"Content first 100 chars: {content?.Substring(0, Math.Min(100, content?.Length ?? 0))}");
                
                // Track issues for reporting back to the user
                List<string> warnings = new List<string>();
                options.Warnings = warnings;
                
                // Ensure the output directory exists
                string outputDir = Path.GetDirectoryName(options.OutputPath);
                if (string.IsNullOrEmpty(outputDir))
                {
                    throw new ArgumentException("Invalid output path. Please specify a valid file path.");
                }
                
                if (!Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Create a new PDF document
                using (PdfDocument document = new PdfDocument())
                {
                    // Set document properties
                    document.Info.Title = options.Metadata.GetValueOrDefault("title", "Untitled");
                    document.Info.Author = options.Metadata.GetValueOrDefault("author", "Unknown Author");
                    
                    // Initialize the dictionary to store heading page numbers for TOC
                    _headingPageNumbers = new Dictionary<string, int>();
                    
                    // Get font family from options
                    string fontFamily = options.Metadata.GetValueOrDefault("fontFamily", "Arial");
                    double fontSize = double.Parse(options.Metadata.GetValueOrDefault("fontSize", "12"));
                    
                    Debug.WriteLine($"Using font family: {fontFamily}, size: {fontSize}");
                    
                    // Get page size and margins
                    XSize pageSize = GetPageSize(options);
                    double marginLeft = 50;
                    double marginTop = 50;
                    double marginRight = 50;
                    double marginBottom = 50;
                    
                    // Process the content
                    if (options.SplitOnHeadings)
                    {
                        // Get the heading levels to split on
                        var headingLevels = options.SplitOnHeadingLevels.Count > 0 
                            ? options.SplitOnHeadingLevels 
                            : new List<int> { 1, 2 }; // Default to H1 and H2
                        
                        Debug.WriteLine($"Splitting on heading levels: {string.Join(", ", headingLevels)}");
                        
                        // Find all headings
                        var headings = _headingRegex.Matches(content)
                            .Cast<Match>()
                            .Select(m => new 
                            { 
                                Level = m.Groups[1].Value.Length, 
                                Title = m.Groups[2].Value.Trim(),
                                Index = m.Index,
                                Length = m.Length
                            })
                            .Where(h => headingLevels.Contains(h.Level))
                            .OrderBy(h => h.Index)
                            .ToList();
                        
                        Debug.WriteLine($"Found {headings.Count} headings to split on");
                        
                        if (headings.Count > 0)
                        {
                            // Split content into sections based on headings
                            for (int i = 0; i < headings.Count; i++)
                            {
                                int startIndex = headings[i].Index;
                                int endIndex = (i < headings.Count - 1) ? headings[i + 1].Index : content.Length;
                                string sectionContent = content.Substring(startIndex, endIndex - startIndex);
                                
                                // Add section to document
                                AddSection(document, sectionContent, headings[i].Level, headings[i].Title, fontFamily, fontSize, pageSize, marginLeft, marginTop, marginRight, marginBottom, options);
                            }
                        }
                        else
                        {
                            // No headings found, add the entire content as one section
                            AddSection(document, content, 0, "Untitled", fontFamily, fontSize, pageSize, marginLeft, marginTop, marginRight, marginBottom, options);
                        }
                    }
                    else
                    {
                        // Add the entire content as one section
                        AddSection(document, content, 0, "Untitled", fontFamily, fontSize, pageSize, marginLeft, marginTop, marginRight, marginBottom, options);
                    }
                    
                    // Add table of contents if requested
                    if (options.IncludeToc)
                    {
                        DrawTableOfContents(document, fontFamily, fontSize, pageSize, marginLeft, marginTop, marginRight, marginBottom);
                    }
                    
                    // Add cover page if requested
                    if (options.IncludeCover)
                    {
                        // Create a new cover page and insert it at the beginning
                        PdfPage coverPage = new PdfPage();
                        document.InsertPage(0, coverPage);
                        coverPage.Width = pageSize.Width;
                        coverPage.Height = pageSize.Height;
                        
                        // Get title and author from metadata
                        string title = options.Metadata.GetValueOrDefault("title", "Untitled");
                        string author = options.Metadata.GetValueOrDefault("author", "Unknown Author");
                        
                        // Add the cover page content
                        AddCoverPage(document, title, author, fontFamily, fontSize, pageSize, marginLeft, marginTop, marginRight, marginBottom);
                        
                        // Update page numbers after adding cover page
                        AddPageNumbers(document, fontFamily, fontSize, marginBottom);
                    }
                    
                    // Save the document
                    document.Save(options.OutputPath);
                }
                
                Debug.WriteLine("PDF export completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting to PDF: {ex.Message}");
                Debug.WriteLine($"Exception type: {ex.GetType().FullName}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                    Debug.WriteLine($"Inner exception type: {ex.InnerException.GetType().FullName}");
                    Debug.WriteLine($"Inner exception stack trace: {ex.InnerException.StackTrace}");
                }
                
                if (options.Warnings == null)
                {
                    options.Warnings = new List<string>();
                }
                
                string errorMessage = $"Export error: {ex.Message}";
                if (ex.InnerException != null)
                {
                    errorMessage += $" ({ex.InnerException.Message})";
                }
                
                options.Warnings.Add(errorMessage);
                
                return false;
            }
        }
        
        /// <summary>
        /// Gets the page size based on the export options
        /// </summary>
        private XSize GetPageSize(ExportOptions options)
        {
            string pageSize = options.Metadata.GetValueOrDefault("pageSize", "A4");
            
            switch (pageSize.ToUpper())
            {
                case "A3":
                    return new XSize(1190, 1684); // A3 in points
                case "A4":
                    return new XSize(595, 842); // A4 in points
                case "A5":
                    return new XSize(420, 595); // A5 in points
                case "LETTER":
                    return new XSize(612, 792); // Letter in points
                case "LEGAL":
                    return new XSize(612, 1008); // Legal in points
                default:
                    return new XSize(595, 842); // Default to A4
            }
        }
        
        /// <summary>
        /// Gets the font based on the font family name
        /// </summary>
        private XFont GetFont(string fontFamily, double fontSize, bool isBold = false, bool isItalic = false)
        {
            XFontStyleEx style = XFontStyleEx.Regular;
            
            if (isBold && isItalic)
                style = XFontStyleEx.BoldItalic;
            else if (isBold)
                style = XFontStyleEx.Bold;
            else if (isItalic)
                style = XFontStyleEx.Italic;
            
            // Try to map common font names to standard fonts
            switch (fontFamily.ToLower())
            {
                case "times new roman":
                case "times":
                case "serif":
                    return new XFont("Times New Roman", fontSize, style);
                case "helvetica":
                case "arial":
                case "sans-serif":
                    return new XFont("Arial", fontSize, style);
                case "courier":
                case "courier new":
                case "monospace":
                case "cascadia code":
                    return new XFont("Courier New", fontSize, style);
                default:
                    // Try to use the specified font, fallback to Arial if not available
                    try
                    {
                        return new XFont(fontFamily, fontSize, style);
                    }
                    catch
                    {
                        return new XFont("Arial", fontSize, style);
                    }
            }
        }
        
        /// <summary>
        /// Adds a section of content to the document
        /// </summary>
        private void AddSection(PdfDocument document, string content, int level, string title, 
            string fontFamily, double fontSize, XSize pageSize, double marginLeft, double marginTop, 
            double marginRight, double marginBottom, ExportOptions options)
        {
            // Log the content being processed
            Debug.WriteLine($"Processing section with title: {title}, level: {level}");
            Debug.WriteLine($"Content length: {content.Length} characters");
            
            // Normalize line endings
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            
            // Create a new page
            PdfPage page = document.AddPage();
            page.Width = pageSize.Width;
            page.Height = pageSize.Height;
            
            // Define the text area
            XRect textArea = new XRect(
                marginLeft,
                marginTop,
                page.Width - marginLeft - marginRight,
                page.Height - marginTop - marginBottom);
            
            // Extract content blocks (headings and paragraphs)
            List<ContentBlock> contentBlocks = new List<ContentBlock>();
            
            // First, extract headings
            MatchCollection headingMatches = _headingRegex.Matches(content);
            foreach (Match match in headingMatches)
            {
                int headingLevel = int.Parse(match.Groups[1].Value);
                string headingText = match.Groups[2].Value.Trim();
                
                contentBlocks.Add(new ContentBlock
                {
                    Type = ContentBlockType.Heading,
                    Content = $"<h{headingLevel}>{headingText}</h{headingLevel}>",
                    Level = headingLevel,
                    Position = match.Index
                });
                
                // Log the heading found
                Debug.WriteLine($"Found heading level {headingLevel}: {headingText}");
            }
            
            // Now extract paragraphs
            // Split content by double newlines to get paragraphs
            string[] paragraphSplit = content.Split(new[] { "\n\n" }, StringSplitOptions.None);
            
            for (int i = 0; i < paragraphSplit.Length; i++)
            {
                string para = paragraphSplit[i].Trim();
                if (string.IsNullOrWhiteSpace(para))
                    continue;
                
                // Skip if this is a heading (already processed)
                if (_headingRegex.IsMatch(para))
                    continue;
                
                // Find the position of this paragraph in the original content
                int position = content.IndexOf(para);
                
                // Process markdown formatting
                para = ProcessMarkdownFormatting(para);
                
                contentBlocks.Add(new ContentBlock
                {
                    Type = ContentBlockType.Paragraph,
                    Content = para,
                    Level = 0,
                    Position = position
                });
                
                // Log the paragraph found
                Debug.WriteLine($"Found paragraph: {para.Substring(0, Math.Min(50, para.Length))}...");
            }
            
            // Sort content blocks by their position in the original text
            contentBlocks = contentBlocks.OrderBy(b => b.Position).ToList();
            
            // Log the number of content blocks found
            Debug.WriteLine($"Found {contentBlocks.Count} content blocks ({contentBlocks.Count(b => b.Type == ContentBlockType.Heading)} headings, {contentBlocks.Count(b => b.Type == ContentBlockType.Paragraph)} paragraphs)");
            
            // Add the section title as a heading if provided
            if (!string.IsNullOrEmpty(title))
            {
                using (XGraphics gfx = XGraphics.FromPdfPage(page))
                {
                    // Get heading font size from options if available
                    double headingFontSize = fontSize;
                    if (options != null && options.HeadingFontSizes != null && options.HeadingFontSizes.TryGetValue(level, out double customSize))
                    {
                        headingFontSize = customSize;
                    }
                    else
                    {
                        // Default heading sizes if not specified in options
                        headingFontSize = fontSize * (1.5 - (level - 1) * 0.1);
                    }
                    
                    // Get heading font family from options if available
                    string headingFontFamily = fontFamily;
                    if (options != null && options.HeadingFontFamilies != null && options.HeadingFontFamilies.TryGetValue(level, out string customFont))
                    {
                        headingFontFamily = customFont;
                    }
                    
                    // Create heading font
                    XFont headingFont = GetFont(headingFontFamily, headingFontSize, true);
                    
                    // Get alignment for this heading level
                    XStringAlignment alignment = XStringAlignment.Near; // Default to left
                    
                    if (options != null && options.HeadingAlignments != null && options.HeadingAlignments.TryGetValue(level, out var headingAlignment))
                    {
                        switch (headingAlignment)
                        {
                            case ExportOptions.TextAlignment.Center:
                                alignment = XStringAlignment.Center;
                                break;
                            case ExportOptions.TextAlignment.Right:
                                alignment = XStringAlignment.Far;
                                break;
                            case ExportOptions.TextAlignment.Justify:
                                // PdfSharp doesn't support justify, fallback to left
                                alignment = XStringAlignment.Near;
                                break;
                        }
                    }
                    
                    // Create a rectangle for the heading
                    XRect headingRect = new XRect(
                        textArea.Left,
                        textArea.Top,
                        textArea.Width,
                        headingFontSize * 1.5);
                    
                    // Create string format with the specified alignment
                    XStringFormat format = new XStringFormat { Alignment = alignment };
                    
                    // Draw the heading
                    gfx.DrawString(title, headingFont, XBrushes.Black, headingRect, format);
                    
                    // Store the page number for this heading in the dictionary
                    if (_headingPageNumbers != null)
                    {
                        _headingPageNumbers[title] = document.Pages.Count;
                    }
                    
                    // Move down for the next paragraph
                    textArea.Y += headingFontSize * 2;
                }
            }
            
            // Convert content blocks to string array for processing
            string[] paragraphs = contentBlocks.Select(b => b.Content).ToArray();
            
            // Process paragraphs
            ProcessParagraphs(document, paragraphs, page, textArea, fontFamily, fontSize, 
                pageSize, marginLeft, marginTop, marginRight, marginBottom, options);
                
            // Add page numbers
            AddPageNumbers(document, fontFamily, fontSize, marginBottom);
        }
        
        /// <summary>
        /// Process markdown formatting in text
        /// </summary>
        private string ProcessMarkdownFormatting(string text)
        {
            // Process bold text
            text = _boldRegex.Replace(text, "<b>$1</b>");
            
            // Process italic text
            text = _italicRegex.Replace(text, "<i>$1</i>");
            
            // Process links
            text = _linkRegex.Replace(text, "$1");
            
            // Process images (just remove them for now)
            text = _imageRegex.Replace(text, "");
            
            return text;
        }
        
        /// <summary>
        /// Content block types
        /// </summary>
        private enum ContentBlockType
        {
            Heading,
            Paragraph
        }
        
        /// <summary>
        /// Represents a block of content (heading or paragraph)
        /// </summary>
        private class ContentBlock
        {
            public ContentBlockType Type { get; set; }
            public string Content { get; set; }
            public int Level { get; set; }
            public int Position { get; set; }
        }
        
        /// <summary>
        /// Process and draw paragraphs on pages
        /// </summary>
        private void ProcessParagraphs(PdfDocument document, string[] paragraphs, PdfPage page, XRect textArea, 
            string fontFamily, double fontSize, XSize pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom, ExportOptions options)
        {
            // Create graphics object for the current page
            XGraphics gfx = null;
            
            try
            {
                gfx = XGraphics.FromPdfPage(page);
                
                // Create regular font
                XFont regularFont = GetFont(options.BodyFontFamily ?? fontFamily, options.BodyFontSize > 0 ? options.BodyFontSize : fontSize);
                
                // Log the body font being used
                Debug.WriteLine($"Using body font: {(options.BodyFontFamily ?? fontFamily)}, size: {(options.BodyFontSize > 0 ? options.BodyFontSize : fontSize)}");
                
                // Process each paragraph
                foreach (string paragraph in paragraphs)
                {
                    // Skip empty paragraphs
                    if (string.IsNullOrWhiteSpace(paragraph))
                    {
                        textArea.Y += fontSize * 0.5;
                        continue;
                    }
                    
                    // Log the paragraph being processed
                    Debug.WriteLine($"Processing paragraph: {paragraph.Substring(0, Math.Min(50, paragraph.Length))}...");
                    
                    // Check if this is a heading
                    if (paragraph.StartsWith("<h") && paragraph.Contains("</h"))
                    {
                        // Extract heading level and text
                        int startIdx = paragraph.IndexOf('>') + 1;
                        int endIdx = paragraph.IndexOf("</h");
                        if (startIdx > 0 && endIdx > startIdx)
                        {
                            string headingText = paragraph.Substring(startIdx, endIdx - startIdx);
                            int level = int.Parse(paragraph.Substring(2, 1));
                            
                            // Get heading font size from options if available
                            double headingFontSize = fontSize;
                            if (options.HeadingFontSizes != null && options.HeadingFontSizes.TryGetValue(level, out double customSize))
                            {
                                headingFontSize = customSize;
                            }
                            else
                            {
                                // Default heading sizes if not specified in options
                                headingFontSize = fontSize * (1.5 - (level - 1) * 0.1);
                            }
                            
                            // Get heading font family from options if available
                            string headingFontFamily = fontFamily;
                            if (options.HeadingFontFamilies != null && options.HeadingFontFamilies.TryGetValue(level, out string customFont))
                            {
                                headingFontFamily = customFont;
                            }
                            
                            // Create heading font
                            XFont headingFont = GetFont(headingFontFamily, headingFontSize, true);
                            
                            // Get alignment for this heading level
                            XStringAlignment alignment = XStringAlignment.Near; // Default to left
                            
                            if (options != null && options.HeadingAlignments.TryGetValue(level, out var headingAlignment))
                            {
                                switch (headingAlignment)
                                {
                                    case ExportOptions.TextAlignment.Center:
                                        alignment = XStringAlignment.Center;
                                        break;
                                    case ExportOptions.TextAlignment.Right:
                                        alignment = XStringAlignment.Far;
                                        break;
                                    case ExportOptions.TextAlignment.Justify:
                                        // PdfSharp doesn't support justify, fallback to left
                                        alignment = XStringAlignment.Near;
                                        break;
                                }
                            }
                            
                            // Check if we need a new page
                            if (textArea.Bottom - textArea.Top < headingFontSize * 2)
                            {
                                // We need to create a new page
                                // First dispose the current graphics object
                                gfx.Dispose();
                                gfx = null;
                                
                                // Add a new page
                                page = document.AddPage();
                                page.Width = pageSize.Width;
                                page.Height = pageSize.Height;
                                
                                // Create new graphics object
                                gfx = XGraphics.FromPdfPage(page);
                                
                                // Reset text area
                                textArea = new XRect(
                                    marginLeft,
                                    marginTop,
                                    page.Width - marginLeft - marginRight,
                                    page.Height - marginTop - marginBottom);
                            }
                            
                            // Create a rectangle for the heading
                            XRect headingRect = new XRect(
                                textArea.Left,
                                textArea.Top,
                                textArea.Width,
                                headingFontSize * 1.5);
                            
                            // Create string format with the specified alignment
                            XStringFormat format = new XStringFormat { Alignment = alignment };
                            
                            // Draw the heading
                            gfx.DrawString(headingText, headingFont, XBrushes.Black, headingRect, format);
                            
                            // Log the heading that was drawn
                            Debug.WriteLine($"Drew heading level {level}: {headingText}");
                            
                            // Move down for the next paragraph
                            textArea.Y += headingFontSize * 2;
                        }
                    }
                    else
                    {
                        // Regular paragraph
                        // Get body text alignment from options
                        XStringAlignment alignment = XStringAlignment.Near; // Default to left
                        
                        if (options != null)
                        {
                            switch (options.BodyTextAlignment)
                            {
                                case ExportOptions.TextAlignment.Center:
                                    alignment = XStringAlignment.Center;
                                    break;
                                case ExportOptions.TextAlignment.Right:
                                    alignment = XStringAlignment.Far;
                                    break;
                                case ExportOptions.TextAlignment.Justify:
                                    // PdfSharp doesn't support justify, fallback to left
                                    alignment = XStringAlignment.Near;
                                    break;
                            }
                        }
                        
                        // Clean up any HTML-like tags that might be left
                        string cleanText = paragraph.Replace("<b>", "").Replace("</b>", "")
                                                  .Replace("<i>", "").Replace("</i>", "")
                                                  .Trim();
                        
                        // Debug the paragraph content
                        Debug.WriteLine($"Processing paragraph text: {cleanText.Substring(0, Math.Min(50, cleanText.Length))}...");
                        
                        // Skip if the paragraph is empty after cleaning
                        if (string.IsNullOrWhiteSpace(cleanText))
                        {
                            textArea.Y += fontSize * 0.5;
                            continue;
                        }
                        
                        // Ensure the text is properly wrapped
                        DrawWrappedText(gfx, cleanText, regularFont, XBrushes.Black, textArea, alignment, fontSize, 
                            ref page, document, pageSize, marginLeft, marginTop, marginRight, marginBottom);
                    }
                }
            }
            finally
            {
                // Always dispose the graphics object
                if (gfx != null)
                {
                    gfx.Dispose();
                }
            }
        }
        
        /// <summary>
        /// Draws text with proper wrapping and page breaks
        /// </summary>
        private void DrawWrappedText(XGraphics gfx, string text, XFont font, XBrush brush, XRect rect, 
            XStringAlignment alignment, double fontSize, ref PdfPage page, PdfDocument document, 
            XSize pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom)
        {
            // Measure the text to see if it fits
            XSize textSize = gfx.MeasureString(text, font);
            
            // If the text is too wide, we need to wrap it
            if (textSize.Width > rect.Width)
            {
                // Split the paragraph into words
                string[] words = text.Split(' ');
                StringBuilder currentLine = new StringBuilder();
                double currentY = rect.Y;
                
                foreach (string word in words)
                {
                    // Skip empty words
                    if (string.IsNullOrWhiteSpace(word))
                        continue;
                    
                    // Try adding the next word
                    string testLine = currentLine.Length > 0 
                        ? currentLine.ToString() + " " + word 
                        : word;
                    
                    // Measure the test line
                    XSize testSize = gfx.MeasureString(testLine, font);
                    
                    // If it fits, add it to the current line
                    if (testSize.Width <= rect.Width)
                    {
                        if (currentLine.Length > 0)
                            currentLine.Append(" ");
                        currentLine.Append(word);
                    }
                    else
                    {
                        // If it doesn't fit, draw the current line and start a new one
                        if (currentLine.Length > 0)
                        {
                            // Check if we need a new page
                            if (currentY + fontSize > rect.Bottom)
                            {
                                // We need to create a new page
                                // First dispose the current graphics object
                                gfx.Dispose();
                                
                                // Add a new page
                                page = document.AddPage();
                                page.Width = pageSize.Width;
                                page.Height = pageSize.Height;
                                
                                // Create new graphics object
                                gfx = XGraphics.FromPdfPage(page);
                                
                                // Reset position
                                currentY = marginTop;
                            }
                            
                            // Draw the line
                            XRect lineRect = new XRect(rect.X, currentY, rect.Width, fontSize * 1.2);
                            gfx.DrawString(currentLine.ToString(), font, brush, lineRect, 
                                new XStringFormat { Alignment = alignment, LineAlignment = XLineAlignment.Near });
                            
                            // Debug the line being drawn
                            Debug.WriteLine($"Drew line: {currentLine.ToString().Substring(0, Math.Min(30, currentLine.Length))}...");
                            
                            // Move down for the next line
                            currentY += fontSize * 1.2;
                            
                            // Start a new line with the current word
                            currentLine.Clear();
                            currentLine.Append(word);
                        }
                        else
                        {
                            // The word itself is too long, we'll have to draw it anyway
                            // Check if we need a new page
                            if (currentY + fontSize > rect.Bottom)
                            {
                                // We need to create a new page
                                // First dispose the current graphics object
                                gfx.Dispose();
                                
                                // Add a new page
                                page = document.AddPage();
                                page.Width = pageSize.Width;
                                page.Height = pageSize.Height;
                                
                                // Create new graphics object
                                gfx = XGraphics.FromPdfPage(page);
                                
                                // Reset position
                                currentY = marginTop;
                            }
                            
                            // Draw the word
                            XRect lineRect = new XRect(rect.X, currentY, rect.Width, fontSize * 1.2);
                            gfx.DrawString(word, font, brush, lineRect, 
                                new XStringFormat { Alignment = alignment, LineAlignment = XLineAlignment.Near });
                            
                            // Debug the word being drawn
                            Debug.WriteLine($"Drew long word: {word}");
                            
                            // Move down for the next line
                            currentY += fontSize * 1.2;
                            
                            // Start a new line
                            currentLine.Clear();
                        }
                    }
                }
                
                // Draw any remaining text
                if (currentLine.Length > 0)
                {
                    // Check if we need a new page
                    if (currentY + fontSize > rect.Bottom)
                    {
                        // We need to create a new page
                        // First dispose the current graphics object
                        gfx.Dispose();
                        
                        // Add a new page
                        page = document.AddPage();
                        page.Width = pageSize.Width;
                        page.Height = pageSize.Height;
                        
                        // Create new graphics object
                        gfx = XGraphics.FromPdfPage(page);
                        
                        // Reset position
                        currentY = marginTop;
                    }
                    
                    // Draw the line
                    XRect lineRect = new XRect(rect.X, currentY, rect.Width, fontSize * 1.2);
                    gfx.DrawString(currentLine.ToString(), font, brush, lineRect, 
                        new XStringFormat { Alignment = alignment, LineAlignment = XLineAlignment.Near });
                    
                    // Debug the final line being drawn
                    Debug.WriteLine($"Drew final line: {currentLine.ToString().Substring(0, Math.Min(30, currentLine.Length))}...");
                    
                    // Move down for the next paragraph
                    currentY += fontSize * 2;
                }
                
                // Update the rectangle position
                rect.Y = currentY;
            }
            else
            {
                // Check if we need a new page
                if (rect.Y + textSize.Height > rect.Bottom)
                {
                    // We need to create a new page
                    // First dispose the current graphics object
                    gfx.Dispose();
                    
                    // Add a new page
                    page = document.AddPage();
                    page.Width = pageSize.Width;
                    page.Height = pageSize.Height;
                    
                    // Create new graphics object
                    gfx = XGraphics.FromPdfPage(page);
                    
                    // Reset text area
                    rect = new XRect(
                        marginLeft,
                        marginTop,
                        page.Width - marginLeft - marginRight,
                        page.Height - marginTop - marginBottom);
                }
                
                // Create a rectangle for the paragraph
                XRect paragraphRect = new XRect(
                    rect.Left,
                    rect.Top,
                    rect.Width,
                    textSize.Height);
                
                // Create string format with the specified alignment
                XStringFormat format = new XStringFormat { Alignment = alignment };
                
                // Draw the paragraph
                gfx.DrawString(text, font, brush, paragraphRect, format);
                
                // Debug the paragraph being drawn
                Debug.WriteLine($"Drew simple paragraph: {text.Substring(0, Math.Min(50, text.Length))}...");
                
                // Move down for the next paragraph
                rect.Y += textSize.Height + fontSize;
            }
        }
        
        /// <summary>
        /// Adds page numbers to all pages in the document
        /// </summary>
        private void AddPageNumbers(PdfDocument document, string fontFamily, double fontSize, double marginBottom)
        {
            for (int i = 1; i <= document.PageCount; i++)
            {
                // Get the page
                PdfPage pdfPage = document.Pages[i - 1];
                
                // Create graphics for the page
                using (XGraphics pageGfx = XGraphics.FromPdfPage(pdfPage, XGraphicsPdfPageOptions.Append))
                {
                    // Create font for page number
                    XFont pageNumberFont = GetFont(fontFamily, fontSize * 0.8);
                    
                    // Draw page number at the bottom center
                    string pageNumber = i.ToString();
                    
                    // Create a rectangle for the page number that's positioned at the bottom center
                    // Make sure it's high enough to not overlap with other page numbers
                    XRect pageNumberRect = new XRect(
                        0,                          // X position (left edge)
                        pdfPage.Height - 20,        // Y position (20 points from bottom)
                        pdfPage.Width,              // Width (full page width)
                        15);                        // Height (15 points tall)
                    
                    // Use center alignment for both horizontal and vertical
                    XStringFormat format = new XStringFormat
                    {
                        Alignment = XStringAlignment.Center,
                        LineAlignment = XLineAlignment.Center
                    };
                    
                    // Draw the page number
                    pageGfx.DrawString(pageNumber, pageNumberFont, XBrushes.Black, pageNumberRect, format);
                }
            }
        }
        
        /// <summary>
        /// Draws the table of contents
        /// </summary>
        private void DrawTableOfContents(PdfDocument document, string fontFamily, double fontSize, 
            XSize pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom)
        {
            // Create a new page for TOC at the beginning
            PdfPage tocPage = new PdfPage();
            document.InsertPage(0, tocPage);
            tocPage.Width = pageSize.Width;
            tocPage.Height = pageSize.Height;
            
            // Define the text area
            XRect textArea = new XRect(
                marginLeft,
                marginTop,
                tocPage.Width - marginLeft - marginRight,
                tocPage.Height - marginTop - marginBottom);
            
            // Sort headings by their page number
            List<KeyValuePair<string, int>> sortedHeadings = new List<KeyValuePair<string, int>>();
            if (_headingPageNumbers != null && _headingPageNumbers.Count > 0)
            {
                sortedHeadings = _headingPageNumbers.OrderBy(h => h.Value).ToList();
            }
            
            // Current page for TOC
            PdfPage currentTocPage = tocPage;
            
            // Process TOC entries in batches, creating new pages as needed
            DrawTocContent(document, currentTocPage, sortedHeadings, fontFamily, fontSize, textArea, 
                pageSize, marginLeft, marginTop, marginRight, marginBottom);
            
            // Add page numbers to the TOC page
            AddPageNumbers(document, fontFamily, fontSize, marginBottom);
        }
        
        /// <summary>
        /// Draws the content of the table of contents
        /// </summary>
        private void DrawTocContent(PdfDocument document, PdfPage tocPage, List<KeyValuePair<string, int>> sortedHeadings,
            string fontFamily, double fontSize, XRect textArea, XSize pageSize, 
            double marginLeft, double marginTop, double marginRight, double marginBottom)
        {
            // Create graphics object for drawing
            using (XGraphics gfx = XGraphics.FromPdfPage(tocPage))
            {
                // Create title font
                XFont titleFont = GetFont(fontFamily, fontSize * 1.5, true);
                
                // Draw TOC title with center alignment
                XStringFormat centerFormat = new XStringFormat { Alignment = XStringAlignment.Center };
                gfx.DrawString("Table of Contents", titleFont, XBrushes.Black, textArea, centerFormat);
                
                // Move down for the entries
                textArea.Y += fontSize * 3;
                
                // Create left alignment format for TOC entries
                XStringFormat leftFormat = new XStringFormat { Alignment = XStringAlignment.Near };
                XStringFormat rightFormat = new XStringFormat { Alignment = XStringAlignment.Far };
                
                // Draw TOC entries
                if (sortedHeadings.Count > 0)
                {
                    int entriesProcessed = 0;
                    
                    while (entriesProcessed < sortedHeadings.Count)
                    {
                        // Calculate how many entries can fit on this page
                        int entriesPerPage = (int)((tocPage.Height - textArea.Y - marginBottom) / (fontSize * 1.5));
                        entriesPerPage = Math.Max(1, entriesPerPage); // Ensure at least one entry per page
                        
                        // Process entries for this page
                        int entriesToProcess = Math.Min(entriesPerPage, sortedHeadings.Count - entriesProcessed);
                        
                        for (int i = 0; i < entriesToProcess; i++)
                        {
                            var heading = sortedHeadings[entriesProcessed + i];
                            string title = heading.Key;
                            int pageNumber = heading.Value;
                            
                            // Adjust page number for TOC insertion (add 1 because TOC is page 1)
                            pageNumber += 1;
                            
                            // Create font for this entry
                            XFont headingFont = GetFont(fontFamily, fontSize);
                            
                            // Calculate the width of the entry text
                            XSize entrySize = gfx.MeasureString(title, headingFont);
                            
                            // Create a rectangle for the entry text
                            XRect entryRect = new XRect(
                                textArea.Left,
                                textArea.Top,
                                textArea.Width - 50, // Leave space for page number
                                fontSize * 1.5);
                            
                            // Draw the entry
                            gfx.DrawString(title, headingFont, XBrushes.Black, entryRect, leftFormat);
                            
                            // Create a rectangle for the page number
                            XRect pageNumberRect = new XRect(
                                textArea.Right - 40,
                                textArea.Top,
                                40,
                                fontSize * 1.5);
                            
                            // Draw the page number
                            gfx.DrawString(pageNumber.ToString(), headingFont, XBrushes.Black, pageNumberRect, rightFormat);
                            
                            // Move down for the next entry
                            textArea.Y += fontSize * 1.5;
                        }
                        
                        entriesProcessed += entriesToProcess;
                        
                        // If there are more entries to process, create a new page
                        if (entriesProcessed < sortedHeadings.Count)
                        {
                            // Create a new page for the next batch of TOC entries
                            PdfPage newTocPage = document.AddPage();
                            newTocPage.Width = pageSize.Width;
                            newTocPage.Height = pageSize.Height;
                            
                            // Reset text area for the new page
                            textArea = new XRect(
                                marginLeft,
                                marginTop,
                                newTocPage.Width - marginLeft - marginRight,
                                newTocPage.Height - marginTop - marginBottom);
                            
                            // Update the current page and create a new graphics object
                            tocPage = newTocPage;
                            
                            // Exit the current using block
                            break;
                        }
                    }
                    
                    // If we still have entries to process, recursively call this method with the new page
                    if (entriesProcessed < sortedHeadings.Count)
                    {
                        // Create a new list with the remaining entries
                        var remainingHeadings = sortedHeadings.Skip(entriesProcessed).ToList();
                        
                        // Process the remaining entries on the new page
                        DrawTocContent(document, tocPage, remainingHeadings, fontFamily, fontSize, textArea,
                            pageSize, marginLeft, marginTop, marginRight, marginBottom);
                    }
                }
                else
                {
                    // No headings found
                    XFont regularFont = GetFont(fontFamily, fontSize);
                    gfx.DrawString("No headings found in document.", regularFont, XBrushes.Black, 
                        new XRect(textArea.Left, textArea.Top, textArea.Width, fontSize * 1.5), leftFormat);
                }
            }
        }
        
        /// <summary>
        /// Adds a cover page to the document
        /// </summary>
        private void AddCoverPage(PdfDocument document, string title, string author, string fontFamily, double fontSize,
            XSize pageSize, double marginLeft, double marginTop, double marginRight, double marginBottom)
        {
            // Use the first page if it already exists
            PdfPage coverPage = document.Pages.Count > 0 ? document.Pages[0] : document.AddPage();
            coverPage.Width = pageSize.Width;
            coverPage.Height = pageSize.Height;
            
            // Create graphics object for drawing
            using (XGraphics gfx = XGraphics.FromPdfPage(coverPage))
            {
                // Define the text area
                XRect textArea = new XRect(
                    marginLeft,
                    marginTop,
                    coverPage.Width - marginLeft - marginRight,
                    coverPage.Height - marginTop - marginBottom);
                
                // Create title font
                XFont titleFont = GetFont(fontFamily, fontSize * 2, true);
                
                // Create author font
                XFont authorFont = GetFont(fontFamily, fontSize * 1.2);
                
                // Create date font
                XFont dateFont = GetFont(fontFamily, fontSize);
                
                // Center alignment for all text
                XStringFormat centerFormat = new XStringFormat { Alignment = XStringAlignment.Center };
                
                // Calculate vertical position for title (centered in the top half of the page)
                double titleY = marginTop + (coverPage.Height - marginTop - marginBottom) / 4;
                
                // Draw title
                XRect titleRect = new XRect(
                    marginLeft,
                    titleY,
                    coverPage.Width - marginLeft - marginRight,
                    fontSize * 3);
                
                gfx.DrawString(title ?? "Untitled Document", titleFont, XBrushes.Black, titleRect, centerFormat);
                
                // Draw author if provided
                if (!string.IsNullOrEmpty(author))
                {
                    XRect authorRect = new XRect(
                        marginLeft,
                        titleY + fontSize * 4,
                        coverPage.Width - marginLeft - marginRight,
                        fontSize * 2);
                    
                    gfx.DrawString($"By {author}", authorFont, XBrushes.Black, authorRect, centerFormat);
                }
                
                // Draw date at the bottom
                XRect dateRect = new XRect(
                    marginLeft,
                    coverPage.Height - marginBottom - fontSize * 3,
                    coverPage.Width - marginLeft - marginRight,
                    fontSize * 2);
                
                gfx.DrawString(DateTime.Now.ToString("MMMM d, yyyy"), dateFont, XBrushes.Black, dateRect, centerFormat);
            }
        }
    }
} 