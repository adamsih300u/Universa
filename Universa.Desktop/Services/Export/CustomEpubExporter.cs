using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Custom exporter for ePub format without external dependencies
    /// </summary>
    public class CustomEpubExporter : IExporter
    {
        // Regular expressions for parsing markdown
        private static readonly Regex _headingRegex = new Regex(@"^(#{1,6})\s+(.+)$", RegexOptions.Multiline);
        private static readonly Regex _imageRegex = new Regex(@"!\[(.*?)\]\((.+?)\)", RegexOptions.Compiled);
        
        /// <summary>
        /// Exports the document content to ePub format
        /// </summary>
        public async Task<bool> ExportAsync(string content, ExportOptions options)
        {
            try
            {
                Debug.WriteLine("Starting custom ePub export...");
                
                // Create a temporary directory for the ePub contents
                string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    // Create the required directory structure
                    Directory.CreateDirectory(Path.Combine(tempDir, "META-INF"));
                    Directory.CreateDirectory(Path.Combine(tempDir, "OEBPS"));
                    Directory.CreateDirectory(Path.Combine(tempDir, "OEBPS", "images"));
                    
                    // Create the mimetype file
                    File.WriteAllText(Path.Combine(tempDir, "mimetype"), "application/epub+zip");
                    
                    // Process the content to extract chapters based on headings
                    var chapters = new List<(string title, string content)>();
                    string coverImagePath = null;
                    
                    // Extract cover image if present
                    if (options.IncludeCover)
                    {
                        var coverMatch = _imageRegex.Match(content);
                        if (coverMatch.Success)
                        {
                            string altText = coverMatch.Groups[1].Value;
                            string imagePath = coverMatch.Groups[2].Value;
                            
                            // Handle relative paths
                            if (!Path.IsPathRooted(imagePath))
                            {
                                string baseDir = Path.GetDirectoryName(options.OutputPath);
                                imagePath = Path.Combine(baseDir, imagePath);
                            }
                            
                            if (File.Exists(imagePath))
                            {
                                string coverFileName = "cover" + Path.GetExtension(imagePath);
                                coverImagePath = Path.Combine("images", coverFileName);
                                
                                // Copy the image to the OEBPS/images directory
                                File.Copy(imagePath, Path.Combine(tempDir, "OEBPS", coverImagePath), true);
                                
                                // Remove the cover image from the content
                                content = content.Replace(coverMatch.Value, "");
                            }
                        }
                    }
                    
                    // Split content into chapters if requested
                    if (options.SplitOnHeadings)
                    {
                        // Get the heading levels to split on
                        var headingLevels = options.SplitOnHeadingLevels.Count > 0 
                            ? options.SplitOnHeadingLevels 
                            : new List<int> { 1, 2 }; // Default to H1 and H2
                        
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
                        
                        if (headings.Count > 0)
                        {
                            // Add a default chapter for content before the first heading
                            if (headings[0].Index > 0)
                            {
                                string introContent = content.Substring(0, headings[0].Index).Trim();
                                if (!string.IsNullOrWhiteSpace(introContent))
                                {
                                    chapters.Add(("Introduction", introContent));
                                }
                            }
                            
                            // Process each heading and its content
                            for (int i = 0; i < headings.Count; i++)
                            {
                                var heading = headings[i];
                                int startIndex = heading.Index + heading.Length;
                                int endIndex = (i < headings.Count - 1) ? headings[i + 1].Index : content.Length;
                                string chapterContent = content.Substring(startIndex, endIndex - startIndex).Trim();
                                
                                // Add the heading back to the chapter content
                                chapterContent = $"<h{heading.Level}>{heading.Title}</h{heading.Level}>\n{chapterContent}";
                                
                                chapters.Add((heading.Title, chapterContent));
                            }
                        }
                        else
                        {
                            // No headings found, use the entire content as a single chapter
                            chapters.Add(("Chapter 1", content));
                        }
                    }
                    else
                    {
                        // No splitting, use the entire content as a single chapter
                        chapters.Add(("Chapter 1", content));
                    }
                    
                    // Create HTML files for each chapter
                    var chapterFiles = new List<string>();
                    for (int i = 0; i < chapters.Count; i++)
                    {
                        string chapterFileName = $"chapter{i + 1}.html";
                        string chapterPath = Path.Combine(tempDir, "OEBPS", chapterFileName);
                        
                        // Convert markdown to HTML (simple conversion)
                        string htmlContent = ConvertMarkdownToHtml(chapters[i].content);
                        
                        // Create the HTML file
                        string chapterHtml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head>
    <title>{chapters[i].title}</title>
    <style>
        body {{ font-family: serif; margin: 5%; line-height: 1.5; }}
        h1, h2, h3, h4, h5, h6 {{ font-family: sans-serif; }}
    </style>
</head>
<body>
    {htmlContent}
</body>
</html>";
                        
                        File.WriteAllText(chapterPath, chapterHtml);
                        chapterFiles.Add(chapterFileName);
                    }
                    
                    // Create a cover HTML file if needed
                    if (coverImagePath != null)
                    {
                        string coverHtml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head>
    <title>Cover</title>
    <style>
        body {{ margin: 0; padding: 0; text-align: center; }}
        img {{ max-width: 100%; max-height: 100vh; }}
    </style>
</head>
<body>
    <div>
        <img src=""{coverImagePath}"" alt=""Cover"" />
    </div>
</body>
</html>";
                        
                        File.WriteAllText(Path.Combine(tempDir, "OEBPS", "cover.html"), coverHtml);
                        chapterFiles.Insert(0, "cover.html");
                    }
                    
                    // Create a simple container.xml
                    string containerXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
    <rootfiles>
        <rootfile full-path=""OEBPS/content.opf"" media-type=""application/oebps-package+xml""/>
    </rootfiles>
</container>";
                    
                    File.WriteAllText(Path.Combine(tempDir, "META-INF", "container.xml"), containerXml);
                    
                    // Create table of contents if requested
                    string tocNavHtml = "";
                    if (options.IncludeToc)
                    {
                        StringBuilder tocBuilder = new StringBuilder();
                        tocBuilder.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"" xmlns:epub=""http://www.idpf.org/2007/ops"">
<head>
    <title>Table of Contents</title>
    <style>
        body { font-family: sans-serif; margin: 5%; }
        nav[epub|type='toc'] > ol { list-style-type: none; }
        nav[epub|type='toc'] > ol > li { margin: 0.5em 0; }
    </style>
</head>
<body>
    <nav epub:type=""toc"" id=""toc"">
        <h1>Table of Contents</h1>
        <ol>");
                        
                        for (int i = 0; i < chapters.Count; i++)
                        {
                            tocBuilder.AppendLine($"            <li><a href=\"{chapterFiles[i]}\">{chapters[i].title}</a></li>");
                        }
                        
                        tocBuilder.AppendLine(@"        </ol>
    </nav>
</body>
</html>");
                        
                        tocNavHtml = tocBuilder.ToString();
                        File.WriteAllText(Path.Combine(tempDir, "OEBPS", "toc.html"), tocNavHtml);
                        chapterFiles.Insert(coverImagePath != null ? 1 : 0, "toc.html");
                    }
                    
                    // Create the content.opf file
                    StringBuilder contentOpfBuilder = new StringBuilder();
                    contentOpfBuilder.AppendLine($@"<?xml version=""1.0"" encoding=""UTF-8""?>
<package xmlns=""http://www.idpf.org/2007/opf"" unique-identifier=""BookID"" version=""3.0"">
    <metadata xmlns:dc=""http://purl.org/dc/elements/1.1/"" xmlns:opf=""http://www.idpf.org/2007/opf"">
        <dc:title>{options.Metadata.GetValueOrDefault("title", "Untitled")}</dc:title>
        <dc:creator>{options.Metadata.GetValueOrDefault("author", "Unknown Author")}</dc:creator>
        <dc:language>{options.Metadata.GetValueOrDefault("language", "en")}</dc:language>
        <dc:identifier id=""BookID"">urn:uuid:{Guid.NewGuid()}</dc:identifier>
        <meta property=""dcterms:modified"">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</meta>");
                    
                    if (coverImagePath != null)
                    {
                        contentOpfBuilder.AppendLine($"        <meta name=\"cover\" content=\"cover-image\"/>");
                    }
                    
                    contentOpfBuilder.AppendLine(@"    </metadata>
    <manifest>");
                    
                    // Add all chapter files to the manifest
                    for (int i = 0; i < chapterFiles.Count; i++)
                    {
                        string id = chapterFiles[i].Replace(".", "-");
                        contentOpfBuilder.AppendLine($"        <item id=\"{id}\" href=\"{chapterFiles[i]}\" media-type=\"application/xhtml+xml\"/>");
                    }
                    
                    // Add cover image to manifest if present
                    if (coverImagePath != null)
                    {
                        string extension = Path.GetExtension(coverImagePath).ToLower();
                        string mediaType = extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : "image/png";
                        contentOpfBuilder.AppendLine($"        <item id=\"cover-image\" href=\"{coverImagePath}\" media-type=\"{mediaType}\"/>");
                    }
                    
                    // Add TOC to manifest if present
                    if (options.IncludeToc)
                    {
                        contentOpfBuilder.AppendLine("        <item id=\"toc\" href=\"toc.html\" media-type=\"application/xhtml+xml\" properties=\"nav\"/>");
                    }
                    
                    contentOpfBuilder.AppendLine(@"    </manifest>
    <spine>");
                    
                    // Add all chapter files to the spine
                    foreach (string file in chapterFiles)
                    {
                        string id = file.Replace(".", "-");
                        contentOpfBuilder.AppendLine($"        <itemref idref=\"{id}\"/>");
                    }
                    
                    contentOpfBuilder.AppendLine(@"    </spine>
</package>");
                    
                    File.WriteAllText(Path.Combine(tempDir, "OEBPS", "content.opf"), contentOpfBuilder.ToString());
                    
                    // Create the ePub file (ZIP archive)
                    if (File.Exists(options.OutputPath))
                    {
                        File.Delete(options.OutputPath);
                    }
                    
                    // Ensure the output directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath));
                    
                    // Create a temporary file for the ZIP archive
                    string tempZipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
                    
                    try
                    {
                        // Create the ZIP archive
                        ZipFile.CreateFromDirectory(tempDir, tempZipPath);
                        
                        // Move the temporary ZIP file to the output path
                        File.Move(tempZipPath, options.OutputPath);
                        
                        Debug.WriteLine($"ePub export completed successfully: {options.OutputPath}");
                        return true;
                    }
                    finally
                    {
                        // Clean up the temporary ZIP file if it still exists
                        if (File.Exists(tempZipPath))
                        {
                            File.Delete(tempZipPath);
                        }
                    }
                }
                finally
                {
                    // Clean up the temporary directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error cleaning up temporary directory: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error exporting to ePub: {ex.Message}");
                Debug.WriteLine(ex.StackTrace);
                return false;
            }
        }
        
        /// <summary>
        /// Converts markdown to HTML (simple implementation)
        /// </summary>
        private string ConvertMarkdownToHtml(string markdown)
        {
            // Replace headings
            markdown = _headingRegex.Replace(markdown, m => 
            {
                int level = m.Groups[1].Value.Length;
                string title = m.Groups[2].Value;
                return $"<h{level}>{title}</h{level}>";
            });
            
            // Replace images
            markdown = _imageRegex.Replace(markdown, m => 
            {
                string alt = m.Groups[1].Value;
                string src = m.Groups[2].Value;
                
                // Extract just the filename for relative paths
                if (Path.IsPathRooted(src))
                {
                    src = Path.GetFileName(src);
                }
                
                return $"<img src=\"images/{src}\" alt=\"{alt}\" />";
            });
            
            // Replace paragraphs (simple implementation)
            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            StringBuilder htmlBuilder = new StringBuilder();
            bool inParagraph = false;
            
            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                
                if (string.IsNullOrWhiteSpace(trimmedLine))
                {
                    if (inParagraph)
                    {
                        htmlBuilder.AppendLine("</p>");
                        inParagraph = false;
                    }
                }
                else if (trimmedLine.StartsWith("<h") || trimmedLine.StartsWith("<img"))
                {
                    if (inParagraph)
                    {
                        htmlBuilder.AppendLine("</p>");
                        inParagraph = false;
                    }
                    
                    htmlBuilder.AppendLine(trimmedLine);
                }
                else
                {
                    if (!inParagraph)
                    {
                        htmlBuilder.AppendLine("<p>");
                        inParagraph = true;
                    }
                    
                    htmlBuilder.AppendLine(trimmedLine);
                }
            }
            
            if (inParagraph)
            {
                htmlBuilder.AppendLine("</p>");
            }
            
            return htmlBuilder.ToString();
        }
    }
} 