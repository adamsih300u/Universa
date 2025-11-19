using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for parsing outlines and providing chapter-specific context for fiction writing
    /// </summary>
    public class OutlineChapterService
    {
        private readonly OutlineParser _outlineParser = new OutlineParser();
        private OutlineParser.ParsedOutline _parsedOutline;
        private string _rawOutline;

        public class ChapterContext
        {
            public int ChapterNumber { get; set; }
            public string ChapterTitle { get; set; }
            public OutlineParser.Chapter CurrentChapter { get; set; }
            public OutlineParser.Chapter PreviousChapter { get; set; }
            public OutlineParser.Chapter NextChapter { get; set; }
            public List<string> StoryWideThemes { get; set; } = new List<string>();
            public List<string> CriticalPlotPoints { get; set; } = new List<string>();
            public Dictionary<string, string> BackgroundSections { get; set; } = new Dictionary<string, string>();
        }

        /// <summary>
        /// Sets and parses the outline content
        /// </summary>
        public void SetOutline(string outlineContent)
        {
            if (string.IsNullOrEmpty(outlineContent))
            {
                _rawOutline = null;
                _parsedOutline = null;
                return;
            }

            _rawOutline = outlineContent;
            
            try
            {
                _parsedOutline = _outlineParser.Parse(outlineContent);
                Debug.WriteLine($"Successfully parsed outline:");
                Debug.WriteLine($"  - {_parsedOutline.Chapters.Count} chapters");
                Debug.WriteLine($"  - {_parsedOutline.Characters.Count} characters");
                Debug.WriteLine($"  - {_parsedOutline.Sections.Count} sections");
                Debug.WriteLine($"  - {_parsedOutline.MajorPlotPoints.Count} major plot points");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing outline: {ex.Message}");
                _parsedOutline = null;
            }
        }

        /// <summary>
        /// Extracts the current chapter number from manuscript content based on cursor position
        /// Uses the unified ChapterDetectionService for consistent detection
        /// </summary>
        public int GetCurrentChapterNumber(string manuscriptContent, int cursorPosition)
        {
            // Delegate to the unified chapter detection service
            return ChapterDetectionService.GetCurrentChapterNumber(manuscriptContent, cursorPosition);
        }

        /// <summary>
        /// Gets comprehensive chapter context for the current chapter
        /// </summary>
        public ChapterContext GetChapterContext(int currentChapterNumber)
        {
            if (_parsedOutline == null)
            {
                Debug.WriteLine("No parsed outline available for chapter context");
                return new ChapterContext { ChapterNumber = currentChapterNumber };
            }

            var context = new ChapterContext
            {
                ChapterNumber = currentChapterNumber
            };

            // Find current chapter in outline
            context.CurrentChapter = _parsedOutline.Chapters
                .FirstOrDefault(c => c.Number == currentChapterNumber);

            if (context.CurrentChapter != null)
            {
                context.ChapterTitle = context.CurrentChapter.Title;
                Debug.WriteLine($"Found current chapter in outline: Chapter {currentChapterNumber} - {context.ChapterTitle}");
            }

            // Find adjacent chapters
            context.PreviousChapter = _parsedOutline.Chapters
                .FirstOrDefault(c => c.Number == currentChapterNumber - 1);

            context.NextChapter = _parsedOutline.Chapters
                .FirstOrDefault(c => c.Number == currentChapterNumber + 1);

            // Simplified - skip complex theme and plot point extraction that was causing confusion

            // Extract background sections (synopsis, notes, etc.)
            foreach (var section in _parsedOutline.Sections.Values)
            {
                if (section.Type == OutlineParser.SectionType.Background ||
                    section.Type == OutlineParser.SectionType.Introduction ||
                    section.Type == OutlineParser.SectionType.ThematicNotes)
                {
                    context.BackgroundSections[section.Title] = section.Content;
                }
            }

            return context;
        }

        /// <summary>
        /// Builds a simple outline prompt section showing raw chapter text
        /// </summary>
        public string BuildChapterOutlinePrompt(ChapterContext context, bool isFullChapterGeneration = false)
        {
            // Fallback to raw outline if parsing failed or context is null
            if (context == null || _parsedOutline == null || string.IsNullOrEmpty(_rawOutline))
            {
                if (!string.IsNullOrEmpty(_rawOutline))
                {
                    return $"\n=== STORY OUTLINE ===\n{_rawOutline}";
                }
                return string.Empty;
            }

            var prompt = new StringBuilder();

            // Story overview (complete synopsis)
            if (context.BackgroundSections.Any())
            {
                prompt.AppendLine("\n=== STORY OVERVIEW ===");
                
                var synopsis = context.BackgroundSections
                    .Where(kvp => kvp.Key.Contains("synopsis", StringComparison.OrdinalIgnoreCase) ||
                                 kvp.Key.Contains("summary", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                
                if (!string.IsNullOrEmpty(synopsis.Value))
                {
                    prompt.AppendLine(synopsis.Value);
                }
            }

            // Previous Chapter Outline
            if (context.PreviousChapter != null)
            {
                prompt.AppendLine($"\n=== PREVIOUS CHAPTER OUTLINE: Chapter {context.PreviousChapter.Number} ===");
                if (!string.IsNullOrEmpty(context.PreviousChapter.Title))
                {
                    prompt.AppendLine($"Title: {context.PreviousChapter.Title}");
                }
                prompt.AppendLine(context.PreviousChapter.Summary);
            }

            // Current Chapter Outline (raw text)
            if (context.CurrentChapter != null)
            {
                prompt.AppendLine($"\n=== CURRENT CHAPTER OUTLINE: Chapter {context.ChapterNumber} ===");
                if (!string.IsNullOrEmpty(context.ChapterTitle))
                {
                    prompt.AppendLine($"Title: {context.ChapterTitle}");
                }
                prompt.AppendLine(context.CurrentChapter.Summary);
            }

            // Next Chapter Outline
            if (context.NextChapter != null)
            {
                prompt.AppendLine($"\n=== NEXT CHAPTER OUTLINE: Chapter {context.NextChapter.Number} ===");
                if (!string.IsNullOrEmpty(context.NextChapter.Title))
                {
                    prompt.AppendLine($"Title: {context.NextChapter.Title}");
                }
                prompt.AppendLine(context.NextChapter.Summary);
            }

            return prompt.ToString();
        }

        /// <summary>
        /// Validates if a generated chapter matches the outline expectations
        /// </summary>
        public List<string> ValidateChapterAgainstOutline(string generatedChapter, int chapterNumber)
        {
            var issues = new List<string>();
            
            if (_parsedOutline == null)
                return issues;

            var outlineChapter = _parsedOutline.Chapters.FirstOrDefault(c => c.Number == chapterNumber);
            if (outlineChapter == null)
                return issues;

            // Note: Character and location validation disabled due to parsing issues

            // Check for key events (basic keyword matching)
            foreach (var keyEvent in outlineChapter.KeyEvents)
            {
                var keywords = ExtractKeywords(keyEvent.Description);
                bool foundEvent = keywords.Any(keyword => 
                    generatedChapter.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                if (!foundEvent)
                {
                    issues.Add($"Possible missing key event: {keyEvent.Description}");
                }
            }

            return issues;
        }

        /// <summary>
        /// Gets a summary of what should happen in the current chapter
        /// </summary>
        public string GetChapterExpectationSummary(int chapterNumber)
        {
            if (_parsedOutline == null)
                return "No outline available for chapter expectations.";

            var chapter = _parsedOutline.Chapters.FirstOrDefault(c => c.Number == chapterNumber);
            if (chapter == null)
                return $"Chapter {chapterNumber} not found in outline.";

            var summary = new StringBuilder();
            summary.AppendLine($"Chapter {chapterNumber}: {chapter.Title}");
            
            if (chapter.KeyEvents.Any())
            {
                summary.AppendLine("Expected events:");
                foreach (var evt in chapter.KeyEvents)
                {
                    summary.AppendLine($"• {evt.Description}");
                }
            }

            // Note: Character and location parsing has been simplified to avoid extraction errors

            return summary.ToString();
        }

        private string TruncateForContext(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }

        private List<string> ExtractKeywords(string text)
        {
            if (string.IsNullOrEmpty(text))
                return new List<string>();

            // Simple keyword extraction - split on common delimiters and take meaningful words
            var words = text.Split(new[] { ' ', ',', '.', ';', ':', '-', '(', ')', '[', ']' }, 
                                  StringSplitOptions.RemoveEmptyEntries)
                           .Where(w => w.Length > 3 && !IsCommonWord(w))
                           .ToList();

            return words;
        }

        private bool IsCommonWord(string word)
        {
            var commonWords = new[] { "the", "and", "but", "for", "are", "with", "his", "her", "they", "that", "this", "from", "have", "been", "will", "would", "could", "should" };
            return commonWords.Contains(word.ToLowerInvariant());
        }
    }
} 