using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Adapter service that bridges chapter navigation functionality with AvalonEdit
    /// Maintains all existing AI Chat Sidebar integration for Fiction Chain Beta, etc.
    /// </summary>
    public class AvalonEditChapterNavigationAdapter : IChapterNavigationService
    {
        private TextEditor _textEditor;
        private readonly List<ChapterPosition> _chapterPositions = new List<ChapterPosition>();
        private static readonly Regex HeaderRegex = new Regex(@"^(#{1,6})\s*(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

        public event EventHandler<NavigationFeedbackEventArgs> NavigationFeedback;

        public bool HasChapters => _chapterPositions.Count > 0;
        public int ChapterCount => _chapterPositions.Count;

        public void Initialize(TextBox editor, ScrollViewer scrollViewer)
        {
            // This adapter is designed for AvalonEdit, so we need a different initialization approach
            // We'll initialize with the AvalonEdit TextEditor directly via the alternative Initialize method
        }

        public void Initialize(TextEditor avalonEditor)
        {
            _textEditor = avalonEditor;
            
            // Subscribe to document changes to update chapter positions
            _textEditor.Document.TextChanged += (s, e) => UpdateChapterPositions(_textEditor.Text);
        }

        public void UpdateChapterPositions(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _chapterPositions.Clear();
                return;
            }

            try
            {
                _chapterPositions.Clear();
                var lines = content.Split('\n');
                var currentPosition = 0;

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    var match = HeaderRegex.Match(line);

                    if (match.Success)
                    {
                        var level = match.Groups[1].Value.Length;
                        var title = match.Groups[2].Value.Trim();

                        // FILTER OUT REFERENCE MATERIAL: Only include story chapters (H2) or major sections (H1)
                        // Skip H3+ as they're usually subsections, and filter out obvious reference material
                        if (IsStoryChapter(level, title))
                        {
                            _chapterPositions.Add(new ChapterPosition
                            {
                                Level = level,
                                Title = title,
                                CharacterPosition = currentPosition,
                                LineNumber = i + 1
                            });
                            
                            System.Diagnostics.Debug.WriteLine($"Added story chapter: Level {level}, Title '{title}' at position {currentPosition}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"Skipped reference material: Level {level}, Title '{title}'");
                        }
                    }

                    currentPosition += line.Length + 1; // +1 for newline
                }

                System.Diagnostics.Debug.WriteLine($"Updated chapter positions: {_chapterPositions.Count} story chapters found (filtered from potential reference material)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating chapter positions: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Determines if a header represents a story chapter vs reference material
        /// </summary>
        private bool IsStoryChapter(int level, string title)
        {
            // Only consider H2 headers as navigable chapters per user preference
            if (level != 2)
                return false;
            
            // Filter out only truly obvious reference material with minimal blacklist
            var lowerTitle = title.ToLowerInvariant();
            
            // Skip only the most obvious reference/metadata sections
            if (lowerTitle.StartsWith("ref:") ||
                lowerTitle.StartsWith("note:") ||
                lowerTitle.StartsWith("todo:") ||
                lowerTitle.StartsWith("meta:") ||
                lowerTitle == "references" ||
                lowerTitle == "bibliography" ||
                lowerTitle == "appendix" ||
                lowerTitle == "index" ||
                lowerTitle == "glossary")
            {
                System.Diagnostics.Debug.WriteLine($"FILTERED OUT reference material: '{title}'");
                return false;
            }
            
            // Accept ALL other H2 headers as navigable chapters
            // This allows creative chapter naming while filtering only obvious non-content
            System.Diagnostics.Debug.WriteLine($"ACCEPTED H2 chapter: '{title}'");
            return true;
        }

        public void NavigateToNextChapter()
        {
            if (_textEditor == null || _chapterPositions.Count == 0)
            {
                ShowFeedback("No chapters found", false);
                return;
            }

            var currentOffset = _textEditor.CaretOffset;
            System.Diagnostics.Debug.WriteLine($"NavigateToNextChapter: Current offset: {currentOffset}, Chapter count: {_chapterPositions.Count}");
            
            // BULLY DEBUG: Show all chapter positions to understand the layout
            System.Diagnostics.Debug.WriteLine("All chapters:");
            for (int i = 0; i < Math.Min(10, _chapterPositions.Count); i++) // Show first 10
            {
                var ch = _chapterPositions[i];
                System.Diagnostics.Debug.WriteLine($"  [{i}] {ch.Title} at position {ch.CharacterPosition}");
            }
            if (_chapterPositions.Count > 10)
            {
                System.Diagnostics.Debug.WriteLine($"  ... and {_chapterPositions.Count - 10} more chapters");
                var lastFew = _chapterPositions.Skip(_chapterPositions.Count - 3).Take(3);
                foreach (var ch in lastFew)
                {
                    System.Diagnostics.Debug.WriteLine($"  [{_chapterPositions.IndexOf(ch)}] {ch.Title} at position {ch.CharacterPosition}");
                }
            }
            
            // BULLY FIX: Ensure chapters are sorted by position before navigation
            var sortedChapters = _chapterPositions.OrderBy(c => c.CharacterPosition).ToList();
            
            // Find current chapter index based on cursor position
            int currentChapterIndex = -1;
            for (int i = 0; i < sortedChapters.Count; i++)
            {
                if (currentOffset >= sortedChapters[i].CharacterPosition)
                {
                    currentChapterIndex = i;
                }
                else
                {
                    break;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Current chapter index: {currentChapterIndex} (of {sortedChapters.Count - 1})");
            if (currentChapterIndex >= 0 && currentChapterIndex < sortedChapters.Count)
            {
                System.Diagnostics.Debug.WriteLine($"Currently in: {sortedChapters[currentChapterIndex].Title}");
            }
            
            // Navigate to next chapter
            if (currentChapterIndex >= 0 && currentChapterIndex < sortedChapters.Count - 1)
            {
                var nextChapter = sortedChapters[currentChapterIndex + 1];
                NavigateToChapter(nextChapter);
                ShowFeedback($"Next: {nextChapter.Title}", true);
                System.Diagnostics.Debug.WriteLine($"NavigateToNextChapter: Successfully navigated to {nextChapter.Title} at position {nextChapter.CharacterPosition}");
            }
            else if (currentChapterIndex == sortedChapters.Count - 1)
            {
                // Already at the last chapter
                ShowFeedback("Already at last chapter", false);
                System.Diagnostics.Debug.WriteLine($"NavigateToNextChapter: Already at last chapter");
            }
            else
            {
                // Cursor is before first chapter or after last chapter
                if (currentOffset < sortedChapters[0].CharacterPosition)
                {
                    // Before first chapter, go to first
                    var firstChapter = sortedChapters[0];
                    NavigateToChapter(firstChapter);
                    ShowFeedback($"First: {firstChapter.Title}", true);
                    System.Diagnostics.Debug.WriteLine($"NavigateToNextChapter: Moved to first chapter {firstChapter.Title}");
                }
                else
                {
                    // After last chapter
                    ShowFeedback("At end of document", false);
                    System.Diagnostics.Debug.WriteLine($"NavigateToNextChapter: At end of document");
                }
            }
        }

        public void NavigateToPreviousChapter()
        {
            if (_textEditor == null || _chapterPositions.Count == 0)
            {
                ShowFeedback("No chapters found", false);
                return;
            }

            var currentOffset = _textEditor.CaretOffset;
            System.Diagnostics.Debug.WriteLine($"NavigateToPreviousChapter: Current offset: {currentOffset}, Chapter count: {_chapterPositions.Count}");
            
            // BULLY FIX: Ensure chapters are sorted by position before navigation
            var sortedChapters = _chapterPositions.OrderBy(c => c.CharacterPosition).ToList();
            
            // Find current chapter index based on cursor position
            int currentChapterIndex = -1;
            for (int i = 0; i < sortedChapters.Count; i++)
            {
                if (currentOffset >= sortedChapters[i].CharacterPosition)
                {
                    currentChapterIndex = i;
                }
                else
                {
                    break;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Current chapter index: {currentChapterIndex} (of {sortedChapters.Count - 1})");
            if (currentChapterIndex >= 0 && currentChapterIndex < sortedChapters.Count)
            {
                System.Diagnostics.Debug.WriteLine($"Currently in: {sortedChapters[currentChapterIndex].Title}");
            }
            
            // Navigate to previous chapter
            if (currentChapterIndex > 0)
            {
                var previousChapter = sortedChapters[currentChapterIndex - 1];
                NavigateToChapter(previousChapter);
                ShowFeedback($"Previous: {previousChapter.Title}", true);
                System.Diagnostics.Debug.WriteLine($"NavigateToPreviousChapter: Successfully navigated to {previousChapter.Title} at position {previousChapter.CharacterPosition}");
            }
            else if (currentChapterIndex == 0)
            {
                // Already at the first chapter
                ShowFeedback("Already at first chapter", false);
                System.Diagnostics.Debug.WriteLine($"NavigateToPreviousChapter: Already at first chapter");
            }
            else
            {
                // Cursor is before first chapter or in an invalid state
                if (sortedChapters.Count > 0)
                {
                    var firstChapter = sortedChapters[0];
                    NavigateToChapter(firstChapter);
                    ShowFeedback($"First: {firstChapter.Title}", true);
                    System.Diagnostics.Debug.WriteLine($"NavigateToPreviousChapter: Moved to first chapter {firstChapter.Title}");
                }
                else
                {
                    ShowFeedback("At beginning of document", false);
                    System.Diagnostics.Debug.WriteLine($"NavigateToPreviousChapter: At beginning");
                }
            }
        }

        public void NavigateToChapter(int chapterIndex)
        {
            if (_textEditor == null || chapterIndex < 0 || chapterIndex >= _chapterPositions.Count)
            {
                ShowFeedback("Invalid chapter index", false);
                return;
            }

            var chapter = _chapterPositions[chapterIndex];
            NavigateToChapter(chapter);
            ShowFeedback($"Navigated to: {chapter.Title}", true);
        }

        private void NavigateToChapter(ChapterPosition chapter)
        {
            try
            {
                _textEditor.CaretOffset = chapter.CharacterPosition;
                _textEditor.ScrollToLine(chapter.LineNumber);
                _textEditor.Focus();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error navigating to chapter: {ex.Message}");
                ShowFeedback($"Error navigating to chapter: {ex.Message}", false);
            }
        }

        public int GetCurrentChapterIndex()
        {
            if (_textEditor == null || _chapterPositions.Count == 0)
                return -1;

            var currentOffset = _textEditor.CaretOffset;
            
            for (int i = _chapterPositions.Count - 1; i >= 0; i--)
            {
                if (_chapterPositions[i].CharacterPosition <= currentOffset)
                {
                    return i;
                }
            }

            return -1;
        }

        public string GetCurrentChapterTitle()
        {
            var currentIndex = GetCurrentChapterIndex();
            if (currentIndex >= 0 && currentIndex < _chapterPositions.Count)
            {
                return _chapterPositions[currentIndex].Title;
            }

            return "No chapter";
        }

        public IReadOnlyList<(int position, string title)> GetChapterPositions()
        {
            return _chapterPositions.Select(c => (c.CharacterPosition, c.Title)).ToList().AsReadOnly();
        }

        public List<ChapterPosition> GetAllChapters()
        {
            return new List<ChapterPosition>(_chapterPositions);
        }

        private void ShowFeedback(string message, bool isSuccess)
        {
            NavigationFeedback?.Invoke(this, new NavigationFeedbackEventArgs(message, isSuccess));
        }

        public class ChapterPosition
        {
            public int Level { get; set; }
            public string Title { get; set; }
            public int CharacterPosition { get; set; }
            public int LineNumber { get; set; }
        }
    }
} 