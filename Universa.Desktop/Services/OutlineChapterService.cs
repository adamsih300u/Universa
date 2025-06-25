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

            // Extract story-wide themes
            context.StoryWideThemes = _parsedOutline.Themes
                .Select(t => t.Description)
                .ToList();

            // Extract critical plot points
            context.CriticalPlotPoints = _parsedOutline.MajorPlotPoints
                .Where(p => p.Importance == OutlineParser.PlotImportance.Critical)
                .Select(p => p.Description)
                .ToList();

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
        /// Builds an enhanced outline prompt section for chapter generation
        /// </summary>
        public string BuildChapterOutlinePrompt(ChapterContext context, bool isFullChapterGeneration = false)
        {
            if (context == null || _parsedOutline == null)
            {
                // Fallback to raw outline if parsing failed
                if (!string.IsNullOrEmpty(_rawOutline))
                {
                    return $"\n=== STORY OUTLINE ===\n{_rawOutline}";
                }
                return string.Empty;
            }

            var prompt = new StringBuilder();

            // Story-wide context (condensed for focus)
            if (context.BackgroundSections.Any() || context.StoryWideThemes.Any())
            {
                prompt.AppendLine("\n=== STORY OVERVIEW ===");
                
                // Add synopsis/background (condensed)
                var synopsis = context.BackgroundSections
                    .Where(kvp => kvp.Key.Contains("synopsis", StringComparison.OrdinalIgnoreCase) ||
                                 kvp.Key.Contains("summary", StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                
                if (!string.IsNullOrEmpty(synopsis.Value))
                {
                    prompt.AppendLine("Story Synopsis:");
                    prompt.AppendLine(TruncateForContext(synopsis.Value, 300));
                }

                // Add key themes
                if (context.StoryWideThemes.Any())
                {
                    prompt.AppendLine("\nKey Themes:");
                    foreach (var theme in context.StoryWideThemes.Take(3))
                    {
                        prompt.AppendLine($"• {theme}");
                    }
                }
            }

            // Current chapter focus (detailed)
            if (context.CurrentChapter != null)
            {
                prompt.AppendLine($"\n=== CURRENT CHAPTER FOCUS: Chapter {context.ChapterNumber} ===");
                
                if (!string.IsNullOrEmpty(context.ChapterTitle))
                {
                    prompt.AppendLine($"Title: {context.ChapterTitle}");
                }

                // Key events for this chapter
                if (context.CurrentChapter.KeyEvents.Any())
                {
                    prompt.AppendLine("\n🎯 STRUCTURAL OBJECTIVES (Create scenes that accomplish these goals):");
                    foreach (var evt in context.CurrentChapter.KeyEvents)
                    {
                        prompt.AppendLine($"• ACHIEVE: {evt.Description}");
                    }
                    prompt.AppendLine("📝 Transform these objectives into original prose - do NOT expand the text above directly.");
                }

                // Characters expected in this chapter
                if (context.CurrentChapter.CharactersPresent.Any())
                {
                    prompt.AppendLine("\nCharacters Present:");
                    foreach (var character in context.CurrentChapter.CharactersPresent)
                    {
                        prompt.AppendLine($"• {character}");
                    }
                }

                // Locations for this chapter
                if (context.CurrentChapter.Locations.Any())
                {
                    prompt.AppendLine("\nLocations/Settings:");
                    foreach (var location in context.CurrentChapter.Locations)
                    {
                        prompt.AppendLine($"• {location}");
                    }
                }

                // POV information
                if (!string.IsNullOrEmpty(context.CurrentChapter.PointOfView))
                {
                    prompt.AppendLine($"\nPoint of View: {context.CurrentChapter.PointOfView}");
                }

                // Scene structure if available
                if (context.CurrentChapter.Scenes.Any())
                {
                    prompt.AppendLine("\n🎬 SCENE OBJECTIVES (Creative goals to achieve, not text to expand):");
                    foreach (var scene in context.CurrentChapter.Scenes)
                    {
                        prompt.AppendLine($"• TARGET: {scene.Title}");
                        if (!string.IsNullOrEmpty(scene.Focus))
                        {
                            prompt.AppendLine($"  PURPOSE: {scene.Focus}");
                        }
                        if (scene.KeyActions.Any())
                        {
                            prompt.AppendLine($"  ACCOMPLISH: {string.Join("; ", scene.KeyActions.Take(2))}");
                        }
                    }
                    prompt.AppendLine("🎨 Create original narrative content that accomplishes these scene purposes.");
                }

                // Chapter summary/notes
                if (!string.IsNullOrEmpty(context.CurrentChapter.Summary))
                {
                    prompt.AppendLine("\nChapter Notes:");
                    prompt.AppendLine(context.CurrentChapter.Summary);
                }
            }

            // Chapter continuity context
            prompt.AppendLine("\n=== CHAPTER CONTINUITY ===");
            
            if (context.PreviousChapter != null)
            {
                prompt.AppendLine($"Previous Chapter {context.PreviousChapter.Number}: {context.PreviousChapter.Title}");
                if (!string.IsNullOrEmpty(context.PreviousChapter.Summary))
                {
                    prompt.AppendLine($"  Summary: {TruncateForContext(context.PreviousChapter.Summary, 150)}");
                }
            }

            if (context.NextChapter != null)
            {
                prompt.AppendLine($"Next Chapter {context.NextChapter.Number}: {context.NextChapter.Title}");
                if (!string.IsNullOrEmpty(context.NextChapter.Summary))
                {
                    prompt.AppendLine($"  Preview: {TruncateForContext(context.NextChapter.Summary, 150)}");
                }
            }

            // Critical plot points for reference
            if (context.CriticalPlotPoints.Any())
            {
                prompt.AppendLine("\n=== CRITICAL PLOT POINTS (Overall Story) ===");
                foreach (var plotPoint in context.CriticalPlotPoints.Take(5))
                {
                    prompt.AppendLine($"• {plotPoint}");
                }
            }

            // Special instructions for full chapter generation
            if (isFullChapterGeneration)
            {
                prompt.AppendLine("\n=== CHAPTER GENERATION CONTEXT ===");
                prompt.AppendLine("You are creating a complete chapter based on the outline structure above.");
            }

            // Add full outline reference (condensed)
            if (!string.IsNullOrEmpty(_rawOutline))
            {
                prompt.AppendLine("\n=== FULL OUTLINE (STRUCTURAL REFERENCE ONLY) ===");
                prompt.AppendLine("📋 Complete outline for broader context - USE AS GUIDANCE, NOT SOURCE TEXT:");
                prompt.AppendLine(_rawOutline);
                prompt.AppendLine("\n⚠️ CRITICAL: This outline is for structure only. Create completely original prose that achieves these plot objectives.");
            }

            // Removed hardcoded scene creation mandate - relying on style guide content instead

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

            // Check for expected characters
            foreach (var expectedChar in outlineChapter.CharactersPresent)
            {
                if (!generatedChapter.Contains(expectedChar, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Missing expected character: {expectedChar}");
                }
            }

            // Check for expected locations
            foreach (var expectedLocation in outlineChapter.Locations)
            {
                if (!generatedChapter.Contains(expectedLocation, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Missing expected location: {expectedLocation}");
                }
            }

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

            if (chapter.CharactersPresent.Any())
            {
                summary.AppendLine($"Characters: {string.Join(", ", chapter.CharactersPresent)}");
            }

            if (chapter.Locations.Any())
            {
                summary.AppendLine($"Locations: {string.Join(", ", chapter.Locations)}");
            }

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