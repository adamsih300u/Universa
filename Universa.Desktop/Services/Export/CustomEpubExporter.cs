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
        private static readonly Regex _linkRegex = new Regex(@"\[(.*?)\]\((.+?)\)", RegexOptions.Compiled);
        private static readonly Regex _italicRegex = new Regex(@"(?<!\*)\*([^\*]+)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex _boldRegex = new Regex(@"\*\*([^\*]+)\*\*", RegexOptions.Compiled);
        
        /// <summary>
        /// Exports the document content to ePub format
        /// </summary>
        public async Task<bool> ExportAsync(string content, ExportOptions options)
        {
            try
            {
                Debug.WriteLine("Starting custom ePub export...");
                
                // Track issues for reporting back to the user
                List<string> warnings = new List<string>();
                
                // Create a temporary directory for the ePub contents
                string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    // Create the required directory structure
                    Directory.CreateDirectory(Path.Combine(tempDir, "META-INF"));
                    Directory.CreateDirectory(Path.Combine(tempDir, "OPS"));
                    Directory.CreateDirectory(Path.Combine(tempDir, "OPS", "images"));
                    
                    // Create the mimetype file
                    File.WriteAllText(Path.Combine(tempDir, "mimetype"), "application/epub+zip");
                    
                    // Process the content to extract chapters based on headings
                    var chapters = new List<(string title, string content)>();
                    string coverImagePath = null;
                    bool coverImageSpecifiedButNotFound = false;
                    
                    // Extract cover image if present
                    if (options.IncludeCover)
                    {
                        // First check if there's a cover specified in the metadata (from frontmatter)
                        string coverPathFromMetadata = null;
                        if (options.Metadata != null && 
                            options.Metadata.TryGetValue("cover", out coverPathFromMetadata) && 
                            !string.IsNullOrWhiteSpace(coverPathFromMetadata))
                        {
                            Debug.WriteLine($"Found cover image in frontmatter: {coverPathFromMetadata}");
                            
                            // Debug all metadata
                            Debug.WriteLine("All metadata from frontmatter:");
                            foreach (var kvp in options.Metadata)
                            {
                                Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
                            }
                            
                            string originalImagePath = coverPathFromMetadata;
                            
                            // Handle relative paths
                            if (!Path.IsPathRooted(coverPathFromMetadata))
                            {
                                string baseDir = Path.GetDirectoryName(options.OutputPath);
                                if (baseDir == null)
                                {
                                    baseDir = Directory.GetCurrentDirectory();
                                }
                                
                                // Get the source file directory if available
                                string sourceFileDir = null;
                                if (options.Metadata.TryGetValue("_sourcePath", out string sourcePath) && !string.IsNullOrEmpty(sourcePath))
                                {
                                    sourceFileDir = Path.GetDirectoryName(sourcePath);
                                    Debug.WriteLine($"Source file directory: {sourceFileDir}");
                                }
                                
                                // Try different approaches to find the image
                                var possiblePathsList = new List<string>
                                {
                                    Path.Combine(baseDir, coverPathFromMetadata),
                                    coverPathFromMetadata,
                                    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)), coverPathFromMetadata)
                                };
                                
                                // Add source file directory path if available
                                if (!string.IsNullOrEmpty(sourceFileDir))
                                {
                                    possiblePathsList.Add(Path.Combine(sourceFileDir, coverPathFromMetadata));
                                    
                                    // If the path starts with ./ or ../, it's relative to the source file
                                    if (coverPathFromMetadata.StartsWith("./") || coverPathFromMetadata.StartsWith("../"))
                                    {
                                        string resolvedPath = Path.GetFullPath(Path.Combine(sourceFileDir, coverPathFromMetadata));
                                        possiblePathsList.Add(resolvedPath);
                                    }
                                }
                                
                                string[] possiblePaths = possiblePathsList.ToArray();
                                
                                Debug.WriteLine("Trying to find cover image at the following paths:");
                                foreach (string path in possiblePaths)
                                {
                                    Debug.WriteLine($"  Checking: {path} - Exists: {File.Exists(path)}");
                                }
                                
                                bool found = false;
                                foreach (string path in possiblePaths)
                                {
                                    if (File.Exists(path))
                                    {
                                        coverPathFromMetadata = path;
                                        found = true;
                                        break;
                                    }
                                }
                                
                                if (!found)
                                {
                                    coverImageSpecifiedButNotFound = true;
                                    warnings.Add($"Cover image specified in frontmatter not found: {originalImagePath}");
                                    Debug.WriteLine($"Cover image from frontmatter not found: {originalImagePath}");
                                }
                            }
                            else if (!File.Exists(coverPathFromMetadata))
                            {
                                coverImageSpecifiedButNotFound = true;
                                warnings.Add($"Cover image specified in frontmatter not found: {originalImagePath}");
                                Debug.WriteLine($"Cover image from frontmatter not found: {originalImagePath}");
                            }
                            
                            if (File.Exists(coverPathFromMetadata))
                            {
                                // Use a standardized filename for the cover image
                                string coverFileName = "cover" + Path.GetExtension(coverPathFromMetadata);
                                
                                // Set the path relative to OPS directory for use in HTML references
                                coverImagePath = "images/" + coverFileName;
                                
                                // Copy the image to the OPS/images directory
                                File.Copy(coverPathFromMetadata, Path.Combine(tempDir, "OPS", "images", coverFileName), true);
                                
                                Debug.WriteLine($"Cover image from frontmatter copied: {coverPathFromMetadata} to {coverImagePath}");
                            }
                        }
                        
                        // If no cover was found in metadata, look for the first image in the content
                        if (coverImagePath == null)
                        {
                            var coverMatch = _imageRegex.Match(content);
                            if (coverMatch.Success)
                            {
                                string altText = coverMatch.Groups[1].Value;
                                string imagePath = coverMatch.Groups[2].Value;
                                string originalImagePath = imagePath; // Store original path for error reporting
                                
                                // Handle relative paths
                                if (!Path.IsPathRooted(imagePath))
                                {
                                    string baseDir = Path.GetDirectoryName(options.OutputPath);
                                    if (baseDir == null)
                                    {
                                        baseDir = Directory.GetCurrentDirectory();
                                    }
                                    
                                    // Try different approaches to find the image
                                    string[] possiblePaths = new string[]
                                    {
                                        Path.Combine(baseDir, imagePath),
                                        imagePath,
                                        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(options.OutputPath)), imagePath)
                                    };
                                    
                                    Debug.WriteLine("Trying to find cover image at the following paths:");
                                    foreach (string path in possiblePaths)
                                    {
                                        Debug.WriteLine($"  Checking: {path} - Exists: {File.Exists(path)}");
                                    }
                                    
                                    bool found = false;
                                    foreach (string path in possiblePaths)
                                    {
                                        if (File.Exists(path))
                                        {
                                            imagePath = path;
                                            found = true;
                                            break;
                                        }
                                    }
                                    
                                    if (!found)
                                    {
                                        coverImageSpecifiedButNotFound = true;
                                        warnings.Add($"Cover image not found: {originalImagePath}");
                                        Debug.WriteLine($"Cover image not found: {originalImagePath}");
                                    }
                                }
                                else if (!File.Exists(imagePath))
                                {
                                    coverImageSpecifiedButNotFound = true;
                                    warnings.Add($"Cover image not found: {originalImagePath}");
                                    Debug.WriteLine($"Cover image not found: {originalImagePath}");
                                }
                                
                                if (File.Exists(imagePath))
                                {
                                    // Use a standardized filename for the cover image
                                    string coverFileName = "cover" + Path.GetExtension(imagePath);
                                    
                                    // Set the path relative to OPS directory for use in HTML references
                                    coverImagePath = "images/" + coverFileName;
                                    
                                    // Copy the image to the OPS/images directory
                                    File.Copy(imagePath, Path.Combine(tempDir, "OPS", "images", coverFileName), true);
                                    
                                    // Remove the cover image from the content
                                    content = content.Replace(coverMatch.Value, "");
                                    
                                    Debug.WriteLine($"Cover image found and copied: {imagePath} to {coverImagePath}");
                                }
                            }
                            else
                            {
                                if (coverImagePath == null && !coverImageSpecifiedButNotFound)
                                {
                                    warnings.Add("Cover image option was selected, but no image was found in the document or frontmatter.");
                                    Debug.WriteLine("No cover image found in content or frontmatter");
                                }
                            }
                        }
                    }
                    
                    // Copy all images in the content to the images directory
                    CopyContentImagesToImagesDirectory(content, tempDir, Path.GetDirectoryName(options.OutputPath), warnings);
                    
                    // Copy all linked resources to the ePub directory
                    CopyLinkedResourcesToEpubDirectory(content, tempDir, Path.GetDirectoryName(options.OutputPath), warnings);
                    
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
                            string title = "Chapter 1";
                            
                            // Try to get a title from metadata
                            if (options.Metadata != null && options.Metadata.TryGetValue("title", out string metadataTitle))
                            {
                                title = metadataTitle;
                            }
                            
                            chapters.Add((title, content));
                        }
                    }
                    else
                    {
                        // No splitting, use the entire content as a single chapter
                        string title = "Chapter 1";
                        
                        // Try to get a title from metadata
                        if (options.Metadata != null && options.Metadata.TryGetValue("title", out string metadataTitle))
                        {
                            title = metadataTitle;
                        }
                        
                        chapters.Add((title, content));
                    }
                    
                    // Create HTML files for each chapter
                    var chapterFiles = new List<string>();
                    for (int i = 0; i < chapters.Count; i++)
                    {
                        string chapterFileName = $"chapter{i + 1}.html";
                        string chapterPath = Path.Combine(tempDir, "OPS", chapterFileName);
                        
                        // Convert markdown to HTML (simple conversion)
                        string htmlContent = ConvertMarkdownToHtml(chapters[i].content, options);
                        
                        // Create the HTML file with CSS for heading alignments
                        StringBuilder cssBuilder = new StringBuilder();
                        cssBuilder.AppendLine("        body { font-family: serif; margin: 5%; line-height: 1.5; }");
                        cssBuilder.AppendLine("        h1, h2, h3, h4, h5, h6 { font-family: sans-serif; }");
                        cssBuilder.AppendLine("        p { margin: 0.5em 0; }");
                        cssBuilder.AppendLine("        .indented-line { margin-left: 0; display: inline; }");
                        cssBuilder.AppendLine("        .indented-para { text-indent: 0; }");
                        
                        // Add alignment styles for each heading level
                        foreach (var alignment in options.HeadingAlignments)
                        {
                            string textAlign = alignment.Value.ToString().ToLower();
                            cssBuilder.AppendLine($"        h{alignment.Key} {{ text-align: {textAlign}; }}");
                        }
                        
                        string chapterHtml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head>
    <title>{chapters[i].title}</title>
    <style>
{cssBuilder.ToString()}
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
                        // Get the book title for the cover page
                        string bookTitle = "Cover";
                        if (options.Metadata != null && options.Metadata.TryGetValue("title", out string metadataTitle))
                        {
                            bookTitle = metadataTitle;
                        }
                        
                        // Create CSS for the cover page
                        StringBuilder coverCssBuilder = new StringBuilder();
                        coverCssBuilder.AppendLine("        body { margin: 0; padding: 0; text-align: center; }");
                        coverCssBuilder.AppendLine("        img { max-width: 100%; max-height: 100vh; }");
                        coverCssBuilder.AppendLine("        h1, h2, h3, h4, h5, h6 { font-family: sans-serif; }");
                        
                        // Add alignment styles for each heading level
                        foreach (var alignment in options.HeadingAlignments)
                        {
                            string textAlign = alignment.Value.ToString().ToLower();
                            coverCssBuilder.AppendLine($"        h{alignment.Key} {{ text-align: {textAlign}; }}");
                        }
                        
                        string coverHtml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"">
<head>
    <title>Cover - {bookTitle}</title>
    <style>
{coverCssBuilder.ToString()}
    </style>
</head>
<body>
    <div>
        <img src=""{coverImagePath}"" alt=""Cover for {bookTitle}"" />
    </div>
</body>
</html>";
                        
                        File.WriteAllText(Path.Combine(tempDir, "OPS", "cover.html"), coverHtml);
                        chapterFiles.Insert(0, "cover.html");
                        
                        Debug.WriteLine("Cover HTML file created with image reference: " + coverImagePath);
                    }
                    
                    // Create a simple container.xml
                    string containerXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<container version=""1.0"" xmlns=""urn:oasis:names:tc:opendocument:xmlns:container"">
    <rootfiles>
        <rootfile full-path=""OPS/content.opf"" media-type=""application/oebps-package+xml""/>
    </rootfiles>
</container>";
                    
                    File.WriteAllText(Path.Combine(tempDir, "META-INF", "container.xml"), containerXml);
                    
                    // Create table of contents if requested
                    string tocNavHtml = "";
                    if (options.IncludeToc)
                    {
                        // Create CSS for the TOC
                        StringBuilder tocCssBuilder = new StringBuilder();
                        tocCssBuilder.AppendLine("        body { font-family: sans-serif; margin: 5%; }");
                        tocCssBuilder.AppendLine("        nav[epub|type='toc'] > ol { list-style-type: none; }");
                        tocCssBuilder.AppendLine("        nav[epub|type='toc'] > ol > li { margin: 0.5em 0; }");
                        
                        // Add alignment styles for each heading level
                        foreach (var alignment in options.HeadingAlignments)
                        {
                            string textAlign = alignment.Value.ToString().ToLower();
                            tocCssBuilder.AppendLine($"        h{alignment.Key} {{ text-align: {textAlign}; }}");
                        }
                        
                        StringBuilder tocBuilder = new StringBuilder();
                        tocBuilder.AppendLine(@"<?xml version=""1.0"" encoding=""utf-8""?>
<!DOCTYPE html>
<html xmlns=""http://www.w3.org/1999/xhtml"" xmlns:epub=""http://www.idpf.org/2007/ops"">
<head>
    <title>Table of Contents</title>
    <style>
" + tocCssBuilder.ToString() + @"
    </style>
</head>
<body>
    <nav epub:type=""toc"" id=""toc"">
        <h1>Table of Contents</h1>
        <ol>");
                        
                        // Do NOT add cover page to TOC
                        
                        // Add chapters to TOC
                        for (int i = 0; i < chapters.Count; i++)
                        {
                            // Get the correct chapter file index based on whether we have a cover
                            int fileIndex = coverImagePath != null ? i + 1 : i;
                            
                            // Use the actual chapter title instead of "Chapter X"
                            string chapterTitle = chapters[i].title;
                            
                            tocBuilder.AppendLine($"            <li><a href=\"{chapterFiles[fileIndex]}\">{chapterTitle}</a></li>");
                        }
                        
                        tocBuilder.AppendLine(@"        </ol>
    </nav>
</body>
</html>");
                        
                        tocNavHtml = tocBuilder.ToString();
                        File.WriteAllText(Path.Combine(tempDir, "OPS", "toc.html"), tocNavHtml);
                        
                        // Insert the TOC after the cover (if present) or at the beginning
                        int tocInsertPosition = coverImagePath != null ? 1 : 0;
                        chapterFiles.Insert(tocInsertPosition, "toc.html");
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
                        string properties = "";
                        
                        // Mark the cover.html file as the cover
                        if (chapterFiles[i] == "cover.html")
                        {
                            properties = " properties=\"cover\"";
                        }
                        
                        contentOpfBuilder.AppendLine($"        <item id=\"{id}\" href=\"{chapterFiles[i]}\" media-type=\"application/xhtml+xml\"{properties}/>");
                    }
                    
                    // Add cover image to manifest if present
                    if (coverImagePath != null)
                    {
                        string extension = Path.GetExtension(coverImagePath).ToLower();
                        string mediaType = extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : "image/png";
                        contentOpfBuilder.AppendLine($"        <item id=\"cover-image\" href=\"{coverImagePath}\" media-type=\"{mediaType}\" properties=\"cover-image\"/>");
                    }
                    
                    // Add all content images to manifest
                    var contentImages = GetContentImages(content);
                    foreach (string imagePath in contentImages)
                    {
                        // Skip if this is the cover image (already added)
                        if (coverImagePath != null && imagePath == coverImagePath)
                            continue;
                            
                        string extension = Path.GetExtension(imagePath).ToLower();
                        string mediaType = extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : 
                                          extension == ".png" ? "image/png" : 
                                          extension == ".gif" ? "image/gif" : 
                                          extension == ".svg" ? "image/svg+xml" : "image/png";
                        
                        string id = "image-" + Path.GetFileNameWithoutExtension(imagePath).Replace(" ", "-").Replace(".", "-");
                        contentOpfBuilder.AppendLine($"        <item id=\"{id}\" href=\"{imagePath}\" media-type=\"{mediaType}\"/>");
                    }
                    
                    // Add all linked resources to manifest
                    var linkedResources = GetLinkedResources(content);
                    foreach (string resourcePath in linkedResources)
                    {
                        // Skip if this is already added
                        if (contentImages.Contains(resourcePath))
                            continue;
                            
                        string extension = Path.GetExtension(resourcePath).ToLower();
                        string mediaType = extension == ".jpg" || extension == ".jpeg" ? "image/jpeg" : 
                                          extension == ".png" ? "image/png" : 
                                          extension == ".gif" ? "image/gif" : 
                                          extension == ".svg" ? "image/svg+xml" : 
                                          extension == ".html" || extension == ".htm" ? "application/xhtml+xml" :
                                          extension == ".css" ? "text/css" :
                                          extension == ".js" ? "application/javascript" :
                                          extension == ".pdf" ? "application/pdf" :
                                          "application/octet-stream";
                        
                        string id = "resource-" + Path.GetFileNameWithoutExtension(resourcePath).Replace(" ", "-").Replace(".", "-");
                        contentOpfBuilder.AppendLine($"        <item id=\"{id}\" href=\"{resourcePath}\" media-type=\"{mediaType}\"/>");
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
                    
                    File.WriteAllText(Path.Combine(tempDir, "OPS", "content.opf"), contentOpfBuilder.ToString());
                    
                    // Create the ePub file (ZIP archive)
                    if (File.Exists(options.OutputPath))
                    {
                        File.Delete(options.OutputPath);
                    }
                    
                    // Ensure the output directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(options.OutputPath));
                    
                    try
                    {
                        // Create the ZIP archive manually to ensure mimetype is first and uncompressed
                        using (var zipArchive = ZipFile.Open(options.OutputPath, ZipArchiveMode.Create))
                        {
                            // Add mimetype file first without compression
                            var mimetypeEntry = zipArchive.CreateEntry("mimetype", CompressionLevel.NoCompression);
                            using (var writer = new StreamWriter(mimetypeEntry.Open()))
                            {
                                writer.Write("application/epub+zip");
                            }
                            
                            // Add the rest of the files
                            AddDirectoryToZip(zipArchive, tempDir, "");
                        }
                        
                        Debug.WriteLine($"ePub export completed successfully: {options.OutputPath}");
                        
                        // If there were warnings, return them to the caller
                        if (warnings.Count > 0)
                        {
                            options.Warnings = warnings;
                        }
                        
                        return true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating ZIP archive: {ex.Message}");
                        return false;
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
        private string ConvertMarkdownToHtml(string markdown, ExportOptions options)
        {
            // Replace headings with alignment
            markdown = _headingRegex.Replace(markdown, m => 
            {
                int level = m.Groups[1].Value.Length;
                string title = m.Groups[2].Value;
                
                // We'll handle alignment through CSS in the HTML file, not inline styles
                // This ensures the alignment is applied consistently
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
                
                // Ensure the image path is relative to the images directory
                if (!src.StartsWith("images/"))
                {
                    src = "images/" + src;
                }
                
                return $"<img src=\"{src}\" alt=\"{alt}\" />";
            });
            
            // Replace hyperlinks
            markdown = _linkRegex.Replace(markdown, m => 
            {
                string text = m.Groups[1].Value;
                string href = m.Groups[2].Value;
                
                // Handle internal links (links to other chapters)
                if (href.StartsWith("#"))
                {
                    // For internal anchors, keep as is
                    return $"<a href=\"{href}\">{text}</a>";
                }
                // Handle external links (URLs)
                else if (href.StartsWith("http://") || href.StartsWith("https://"))
                {
                    return $"<a href=\"{href}\">{text}</a>";
                }
                // Handle relative links to other files
                else
                {
                    // For relative links to other files, we need to ensure they point to the correct HTML file
                    // This is a simple implementation - you might need to enhance this based on your specific needs
                    string extension = Path.GetExtension(href);
                    if (!string.IsNullOrEmpty(extension))
                    {
                        // If it's a link to another markdown file, convert to HTML
                        if (extension.ToLower() == ".md")
                        {
                            string baseName = Path.GetFileNameWithoutExtension(href);
                            return $"<a href=\"{baseName}.html\">{text}</a>";
                        }
                    }
                    
                    // For other types of links, keep as is
                    return $"<a href=\"{href}\">{text}</a>";
                }
            });
            
            // Replace bold text (must be done before italic to avoid conflicts)
            markdown = _boldRegex.Replace(markdown, m => 
            {
                string text = m.Groups[1].Value;
                return $"<strong>{text}</strong>";
            });
            
            // Replace italic text
            markdown = _italicRegex.Replace(markdown, m => 
            {
                string text = m.Groups[1].Value;
                return $"<em>{text}</em>";
            });
            
            // Replace paragraphs (simple implementation)
            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            StringBuilder htmlBuilder = new StringBuilder();
            bool inParagraph = false;
            
            foreach (string line in lines)
            {
                // Check if the line is empty (without trimming to preserve indentation)
                bool isEmptyLine = string.IsNullOrWhiteSpace(line);
                
                // Check if the line starts with HTML tags (after trimming)
                string trimmedLine = line.Trim();
                bool isHtmlTag = trimmedLine.StartsWith("<h") || trimmedLine.StartsWith("<img");
                
                if (isEmptyLine)
                {
                    if (inParagraph)
                    {
                        htmlBuilder.AppendLine("</p>");
                        inParagraph = false;
                    }
                }
                else if (isHtmlTag)
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
                        // Count leading whitespace for paragraph start
                        int leadingWhitespace = 0;
                        foreach (char c in line)
                        {
                            if (c == ' ')
                                leadingWhitespace++;
                            else if (c == '\t')
                                leadingWhitespace += 4; // Convert tab to 4 spaces
                            else
                                break;
                        }

                        string indentClass = leadingWhitespace > 0 ? " class=\"indented-para\"" : "";
                        htmlBuilder.AppendLine($"<p{indentClass}>");
                        
                        string content = line.TrimStart();
                        if (leadingWhitespace > 0)
                        {
                            htmlBuilder.AppendLine($"<span style=\"padding-left: {leadingWhitespace * 0.5}em;\">{content}</span>");
                        }
                        else
                        {
                            htmlBuilder.AppendLine(content);
                        }
                        
                        inParagraph = true;
                    }
                    else
                    {
                        // Add a line break between lines in the same paragraph
                        htmlBuilder.AppendLine("<br/>");
                        
                        // Handle indentation for continuation lines
                        int leadingWhitespace = 0;
                        foreach (char c in line)
                        {
                            if (c == ' ')
                                leadingWhitespace++;
                            else if (c == '\t')
                                leadingWhitespace += 4; // Convert tab to 4 spaces
                            else
                                break;
                        }
                        
                        string content = line.TrimStart();
                        if (leadingWhitespace > 0)
                        {
                            htmlBuilder.AppendLine($"<span class=\"indented-line\" style=\"padding-left: {leadingWhitespace * 0.5}em;\">{content}</span>");
                        }
                        else
                        {
                            htmlBuilder.AppendLine(content);
                        }
                    }
                }
            }
            
            if (inParagraph)
            {
                htmlBuilder.AppendLine("</p>");
            }
            
            return htmlBuilder.ToString();
        }
        
        /// <summary>
        /// Copies all images found in the content to the images directory
        /// </summary>
        private void CopyContentImagesToImagesDirectory(string content, string tempDir, string baseDir, List<string> warnings)
        {
            try
            {
                // Find all images in the content
                var matches = _imageRegex.Matches(content);
                foreach (Match match in matches)
                {
                    string imagePath = match.Groups[2].Value;
                    string originalImagePath = imagePath; // Store for error reporting
                    
                    // Skip if this is a URL
                    if (imagePath.StartsWith("http://") || imagePath.StartsWith("https://"))
                        continue;
                    
                    // Handle relative paths
                    if (!Path.IsPathRooted(imagePath))
                    {
                        if (baseDir == null)
                        {
                            baseDir = Directory.GetCurrentDirectory();
                        }
                        
                        // Try different approaches to find the image
                        string[] possiblePaths = new string[]
                        {
                            Path.Combine(baseDir, imagePath),
                            imagePath,
                            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(baseDir)), imagePath)
                        };
                        
                        Debug.WriteLine("Trying to find cover image at the following paths:");
                        foreach (string path in possiblePaths)
                        {
                            Debug.WriteLine($"  Checking: {path} - Exists: {File.Exists(path)}");
                        }
                        
                        bool found = false;
                        foreach (string path in possiblePaths)
                        {
                            if (File.Exists(path))
                            {
                                imagePath = path;
                                found = true;
                                break;
                            }
                        }
                        
                        if (!found)
                        {
                            warnings.Add($"Image not found: {originalImagePath}");
                            Debug.WriteLine($"Content image not found: {originalImagePath}");
                            continue;
                        }
                    }
                    else if (!File.Exists(imagePath))
                    {
                        warnings.Add($"Image not found: {originalImagePath}");
                        Debug.WriteLine($"Content image not found: {originalImagePath}");
                        continue;
                    }
                    
                    if (File.Exists(imagePath))
                    {
                        string fileName = Path.GetFileName(imagePath);
                        string destPath = Path.Combine(tempDir, "OPS", "images", fileName);
                        
                        // Copy the image to the OPS/images directory
                        File.Copy(imagePath, destPath, true);
                        
                        Debug.WriteLine($"Content image copied: {imagePath} to images/{fileName}");
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Error processing images: {ex.Message}");
                Debug.WriteLine($"Error copying content images: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Gets all image paths from the content
        /// </summary>
        private List<string> GetContentImages(string content)
        {
            var images = new List<string>();
            var matches = _imageRegex.Matches(content);
            
            foreach (Match match in matches)
            {
                string imagePath = match.Groups[2].Value;
                
                // Skip URLs
                if (imagePath.StartsWith("http://") || imagePath.StartsWith("https://"))
                    continue;
                
                // Ensure the path is relative to the images directory
                if (Path.IsPathRooted(imagePath))
                {
                    imagePath = "images/" + Path.GetFileName(imagePath);
                }
                else if (!imagePath.StartsWith("images/"))
                {
                    imagePath = "images/" + imagePath;
                }
                
                if (!images.Contains(imagePath))
                {
                    images.Add(imagePath);
                }
            }
            
            return images;
        }
        
        /// <summary>
        /// Extracts linked resources from the content
        /// </summary>
        private List<string> GetLinkedResources(string content)
        {
            var resources = new List<string>();
            var matches = _linkRegex.Matches(content);
            
            foreach (Match match in matches)
            {
                string href = match.Groups[2].Value;
                
                // Skip internal anchors and external URLs
                if (href.StartsWith("#") || href.StartsWith("http://") || href.StartsWith("https://"))
                    continue;
                
                // Handle relative links to other files
                string extension = Path.GetExtension(href);
                if (!string.IsNullOrEmpty(extension))
                {
                    // If it's a link to another markdown file, convert to HTML
                    if (extension.ToLower() == ".md")
                    {
                        string baseName = Path.GetFileNameWithoutExtension(href);
                        string htmlPath = baseName + ".html";
                        if (!resources.Contains(htmlPath))
                        {
                            resources.Add(htmlPath);
                        }
                    }
                    else
                    {
                        // For other types of resources, add as is
                        if (!resources.Contains(href))
                        {
                            resources.Add(href);
                        }
                    }
                }
            }
            
            return resources;
        }
        
        /// <summary>
        /// Copies linked resources to the ePub package
        /// </summary>
        private void CopyLinkedResourcesToEpubDirectory(string content, string tempDir, string baseDir, List<string> warnings)
        {
            var resources = GetLinkedResources(content);
            
            foreach (string resourcePath in resources)
            {
                try
                {
                    // Skip URLs
                    if (resourcePath.StartsWith("http://") || resourcePath.StartsWith("https://"))
                        continue;
                    
                    // Try to find the resource in the base directory
                    string sourcePath = Path.Combine(baseDir, resourcePath);
                    if (File.Exists(sourcePath))
                    {
                        // Create the target directory if it doesn't exist
                        string targetDir = Path.GetDirectoryName(Path.Combine(tempDir, "OPS", resourcePath));
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.CreateDirectory(targetDir);
                        }
                        
                        // Copy the resource
                        string targetPath = Path.Combine(tempDir, "OPS", resourcePath);
                        File.Copy(sourcePath, targetPath, true);
                    }
                    else
                    {
                        warnings.Add($"Could not find linked resource: {resourcePath}");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Error copying linked resource {resourcePath}: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Adds a directory to a ZIP archive
        /// </summary>
        private void AddDirectoryToZip(ZipArchive archive, string sourceDir, string entryDir)
        {
            // Add files
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                // Skip mimetype file (already added)
                if (Path.GetFileName(filePath) == "mimetype")
                    continue;
                    
                string entryName = Path.Combine(entryDir, Path.GetFileName(filePath)).Replace('\\', '/');
                archive.CreateEntryFromFile(filePath, entryName);
            }
            
            // Add subdirectories
            foreach (string dirPath in Directory.GetDirectories(sourceDir))
            {
                string dirName = Path.GetFileName(dirPath);
                string newEntryDir = Path.Combine(entryDir, dirName).Replace('\\', '/');
                AddDirectoryToZip(archive, dirPath, newEntryDir);
            }
        }
    }
} 