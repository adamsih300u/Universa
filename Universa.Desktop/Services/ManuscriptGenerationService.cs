using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for intelligent manuscript generation and content insertion
    /// Handles automatic chapter positioning, outline-based generation, and manuscript assembly
    /// </summary>
    public class ManuscriptGenerationService
    {
        public class ChapterInsertionInfo
        {
            public int ChapterNumber { get; set; }
            public string Title { get; set; }
            public int InsertionPosition { get; set; }
            public InsertionType Type { get; set; }
            public string ExistingContent { get; set; }
        }

        public enum InsertionType
        {
            AtBeginning,      // Chapter 1 - after frontmatter
            AfterPrevious,    // Insert after previous chapter
            AtEnd,            // Append to end of document
            Replace           // Replace existing chapter
        }

        public class GenerationProgress
        {
            public int CurrentChapter { get; set; }
            public int TotalChapters { get; set; }
            public string CurrentAction { get; set; }
            public bool IsComplete { get; set; }
            public List<string> Errors { get; set; } = new List<string>();
        }

        /// <summary>
        /// Determines the optimal insertion position for new content based on chapter number
        /// </summary>
        public ChapterInsertionInfo DetermineInsertionPosition(string existingContent, int targetChapter, string newContent)
        {
            var info = new ChapterInsertionInfo
            {
                ChapterNumber = targetChapter
            };

            Debug.WriteLine($"[ManuscriptService] Determining insertion position for Chapter {targetChapter}");

            // Extract chapter title from new content
            var titleMatch = Regex.Match(newContent, @"^##?\s*Chapter\s+\d+\s*:?\s*(.+)$", RegexOptions.Multiline);
            if (titleMatch.Success)
            {
                info.Title = titleMatch.Groups[1].Value.Trim();
            }

            // If content is empty or minimal, insert at beginning
            if (string.IsNullOrWhiteSpace(existingContent) || existingContent.Trim().Length < 100)
            {
                info.InsertionPosition = GetContentStartPosition(existingContent);
                info.Type = InsertionType.AtBeginning;
                Debug.WriteLine($"[ManuscriptService] Empty content - inserting Chapter {targetChapter} at beginning (position {info.InsertionPosition})");
                return info;
            }

            // Find existing chapter boundaries
            var existingChapters = ExtractChapterNumbers(existingContent);
            Debug.WriteLine($"[ManuscriptService] Found existing chapters: [{string.Join(", ", existingChapters)}]");

            // Check if target chapter already exists
            if (existingChapters.Contains(targetChapter))
            {
                info.InsertionPosition = FindChapterPosition(existingContent, targetChapter);
                info.Type = InsertionType.Replace;
                info.ExistingContent = ExtractExistingChapterContent(existingContent, targetChapter);
                Debug.WriteLine($"[ManuscriptService] Chapter {targetChapter} exists - replacing at position {info.InsertionPosition}");
                return info;
            }

            // For Chapter 1, always insert at beginning
            if (targetChapter == 1)
            {
                info.InsertionPosition = GetContentStartPosition(existingContent);
                info.Type = InsertionType.AtBeginning;
                Debug.WriteLine($"[ManuscriptService] Inserting Chapter 1 at beginning (position {info.InsertionPosition})");
                return info;
            }

            // Find the highest numbered existing chapter that's less than target
            var previousChapter = existingChapters.Where(c => c < targetChapter).DefaultIfEmpty(0).Max();
            Debug.WriteLine($"[ManuscriptService] Target Chapter {targetChapter}, Previous Chapter: {previousChapter}");

            if (previousChapter > 0)
            {
                // Insert after the previous chapter
                info.InsertionPosition = FindPositionAfterChapter(existingContent, previousChapter);
                info.Type = InsertionType.AfterPrevious;
                Debug.WriteLine($"[ManuscriptService] Inserting Chapter {targetChapter} after Chapter {previousChapter} at position {info.InsertionPosition}");
            }
            else
            {
                // No previous chapters found, but we want to maintain proper order
                // Find where this chapter should be inserted relative to existing chapters
                var nextChapter = existingChapters.Where(c => c > targetChapter).DefaultIfEmpty(int.MaxValue).Min();
                
                if (nextChapter < int.MaxValue)
                {
                    // Insert before the next chapter
                    info.InsertionPosition = FindChapterPosition(existingContent, nextChapter);
                    info.Type = InsertionType.AfterPrevious; // We'll insert before this position
                    Debug.WriteLine($"[ManuscriptService] Inserting Chapter {targetChapter} before Chapter {nextChapter} at position {info.InsertionPosition}");
                }
                else
                {
                    // No chapters after this one, append to end
                    info.InsertionPosition = existingContent.Length;
                    info.Type = InsertionType.AtEnd;
                    Debug.WriteLine($"[ManuscriptService] No chapters after Chapter {targetChapter} - appending to end (position {info.InsertionPosition})");
                }
            }

            return info;
        }

        /// <summary>
        /// Inserts content at the determined optimal position
        /// </summary>
        public string InsertContentAtPosition(string existingContent, string newContent, ChapterInsertionInfo insertionInfo)
        {
            var result = new StringBuilder();

            Debug.WriteLine($"[ManuscriptService] Inserting Chapter {insertionInfo.ChapterNumber} using {insertionInfo.Type} at position {insertionInfo.InsertionPosition}");

            switch (insertionInfo.Type)
            {
                case InsertionType.AtBeginning:
                    // Insert after frontmatter but before any existing content
                    var contentStart = insertionInfo.InsertionPosition;
                    result.Append(existingContent.Substring(0, contentStart));
                    result.AppendLine(newContent);
                    result.AppendLine(); // Add spacing
                    result.Append(existingContent.Substring(contentStart));
                    Debug.WriteLine($"[ManuscriptService] Inserted at beginning - before: {existingContent.Substring(0, Math.Min(50, contentStart))}, after: {existingContent.Substring(contentStart, Math.Min(50, existingContent.Length - contentStart))}");
                    break;

                case InsertionType.AfterPrevious:
                    // Insert after the previous chapter or before next chapter
                    result.Append(existingContent.Substring(0, insertionInfo.InsertionPosition));
                    if (!existingContent.Substring(0, insertionInfo.InsertionPosition).EndsWith("\n"))
                    {
                        result.AppendLine();
                    }
                    result.AppendLine();
                    result.AppendLine(newContent);
                    result.AppendLine();
                    result.Append(existingContent.Substring(insertionInfo.InsertionPosition));
                    Debug.WriteLine($"[ManuscriptService] Inserted after previous - content length before: {insertionInfo.InsertionPosition}, after: {existingContent.Length - insertionInfo.InsertionPosition}");
                    break;

                case InsertionType.AtEnd:
                    // Append to the end
                    result.Append(existingContent);
                    if (!existingContent.EndsWith("\n"))
                    {
                        result.AppendLine();
                    }
                    result.AppendLine();
                    result.AppendLine(newContent);
                    Debug.WriteLine($"[ManuscriptService] Appended to end - total content length: {existingContent.Length}");
                    break;

                case InsertionType.Replace:
                    // Replace existing chapter content
                    var chapterStart = FindChapterPosition(existingContent, insertionInfo.ChapterNumber);
                    var chapterEnd = FindChapterEndPosition(existingContent, insertionInfo.ChapterNumber);
                    
                    result.Append(existingContent.Substring(0, chapterStart));
                    result.AppendLine(newContent);
                    result.Append(existingContent.Substring(chapterEnd));
                    Debug.WriteLine($"[ManuscriptService] Replaced chapter - start: {chapterStart}, end: {chapterEnd}");
                    break;
            }

            var finalContent = result.ToString();
            Debug.WriteLine($"[ManuscriptService] Final content length: {finalContent.Length}");
            
            return finalContent;
        }

        /// <summary>
        /// Extracts outline information for automatic manuscript generation
        /// </summary>
        public List<(int ChapterNumber, string Title, string Summary)> ExtractChaptersFromOutline(string outlineContent)
        {
            var chapters = new List<(int, string, string)>();

            if (string.IsNullOrEmpty(outlineContent))
                return chapters;

            // Parse outline for chapter information
            var chapterMatches = Regex.Matches(outlineContent, 
                @"^##?\s*Chapter\s+(\d+)\s*(?:[-:]\s*(.+?))?$(.+?)(?=^##?\s*Chapter\s+\d+|$)", 
                RegexOptions.Multiline | RegexOptions.Singleline);

            foreach (Match match in chapterMatches)
            {
                if (int.TryParse(match.Groups[1].Value, out var chapterNum))
                {
                    var title = match.Groups[2].Success ? match.Groups[2].Value.Trim() : $"Chapter {chapterNum}";
                    var summary = match.Groups[3].Value.Trim();
                    
                    chapters.Add((chapterNum, title, summary));
                }
            }

            return chapters.OrderBy(c => c.Item1).ToList(); // Use Item1 for ChapterNumber
        }

        /// <summary>
        /// Creates a prompt for generating a specific chapter based on outline and context
        /// </summary>
        public string BuildChapterGenerationPrompt(int chapterNumber, string chapterTitle, string chapterSummary, 
            string previousChapterContent = null, string nextChapterSummary = null)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine($"Generate Chapter {chapterNumber}: {chapterTitle}");
            prompt.AppendLine();
            prompt.AppendLine("CHAPTER REQUIREMENTS:");
            prompt.AppendLine($"- Chapter Number: {chapterNumber}");
            prompt.AppendLine($"- Chapter Title: {chapterTitle}");
            prompt.AppendLine($"- Chapter Summary: {chapterSummary}");
            prompt.AppendLine();

            if (!string.IsNullOrEmpty(previousChapterContent))
            {
                prompt.AppendLine("PREVIOUS CHAPTER CONTEXT:");
                prompt.AppendLine("Build naturally from the events and character states established in the previous chapter.");
                prompt.AppendLine();
            }

            if (!string.IsNullOrEmpty(nextChapterSummary))
            {
                prompt.AppendLine("UPCOMING CHAPTER CONTEXT:");
                prompt.AppendLine($"This chapter should set up events for: {nextChapterSummary}");
                prompt.AppendLine();
            }

            prompt.AppendLine("GENERATION INSTRUCTIONS:");
            prompt.AppendLine("- Start with the chapter heading: ## Chapter " + chapterNumber + (string.IsNullOrEmpty(chapterTitle) ? "" : ": " + chapterTitle));
            prompt.AppendLine("- Write complete narrative prose suitable for a published novel");
            prompt.AppendLine("- Include detailed character actions, dialogue, and scene descriptions");
            prompt.AppendLine("- Maintain consistency with established character personalities and relationships");
            prompt.AppendLine("- Follow the scene structure and plot points outlined in the chapter summary");
            prompt.AppendLine("- End at a natural chapter conclusion or appropriate cliffhanger");

            return prompt.ToString();
        }

        // Helper methods for position detection

        private int GetContentStartPosition(string content)
        {
            if (string.IsNullOrEmpty(content))
                return 0;

            // Look for end of frontmatter
            if (content.StartsWith("---"))
            {
                var endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Skip past closing delimiter and any following newlines
                    var position = endIndex + 4;
                    while (position < content.Length && (content[position] == '\n' || content[position] == '\r'))
                    {
                        position++;
                    }
                    return position;
                }
            }

            return 0;
        }

        public List<int> ExtractChapterNumbers(string content)
        {
            var chapters = new List<int>();
            var matches = Regex.Matches(content, @"^##?\s*Chapter\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            
            foreach (Match match in matches)
            {
                if (int.TryParse(match.Groups[1].Value, out var chapterNum))
                {
                    chapters.Add(chapterNum);
                }
            }

            return chapters.Distinct().OrderBy(c => c).ToList();
        }

        private int FindChapterPosition(string content, int chapterNumber)
        {
            var pattern = $@"^##?\s*Chapter\s+{chapterNumber}(?:\D|$)";
            var match = Regex.Match(content, pattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var position = match.Success ? match.Index : content.Length;
            Debug.WriteLine($"[ManuscriptService] FindChapterPosition for Chapter {chapterNumber}: found at {position} (success: {match.Success})");
            return position;
        }

        private int FindChapterEndPosition(string content, int chapterNumber)
        {
            var chapterStart = FindChapterPosition(content, chapterNumber);
            if (chapterStart >= content.Length)
            {
                Debug.WriteLine($"[ManuscriptService] FindChapterEndPosition for Chapter {chapterNumber}: chapter start beyond content length, returning {content.Length}");
                return content.Length;
            }

            // Find the start of the next chapter
            var nextChapterPattern = @"^##?\s*Chapter\s+(\d+)";
            var searchContent = content.Substring(chapterStart + 1);
            var matches = Regex.Matches(searchContent, nextChapterPattern, RegexOptions.Multiline | RegexOptions.IgnoreCase);
            
            if (matches.Count > 0)
            {
                var endPosition = chapterStart + 1 + matches[0].Index;
                Debug.WriteLine($"[ManuscriptService] FindChapterEndPosition for Chapter {chapterNumber}: found next chapter at {endPosition} (relative: {matches[0].Index})");
                return endPosition;
            }

            Debug.WriteLine($"[ManuscriptService] FindChapterEndPosition for Chapter {chapterNumber}: no next chapter found, returning end of content {content.Length}");
            return content.Length;
        }

        private int FindPositionAfterChapter(string content, int chapterNumber)
        {
            var position = FindChapterEndPosition(content, chapterNumber);
            Debug.WriteLine($"[ManuscriptService] FindPositionAfterChapter for Chapter {chapterNumber}: position {position}");
            return position;
        }

        public string ExtractExistingChapterContent(string content, int chapterNumber)
        {
            var start = FindChapterPosition(content, chapterNumber);
            var end = FindChapterEndPosition(content, chapterNumber);
            
            if (start < content.Length && end > start)
            {
                return content.Substring(start, end - start).Trim();
            }

            return string.Empty;
        }
    }
} 