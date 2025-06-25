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
        private static readonly Regex HeaderRegex = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Multiline | RegexOptions.Compiled);

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

                        _chapterPositions.Add(new ChapterPosition
                        {
                            Level = level,
                            Title = title,
                            CharacterPosition = currentPosition,
                            LineNumber = i + 1
                        });
                    }

                    currentPosition += line.Length + 1; // +1 for newline
                }

                System.Diagnostics.Debug.WriteLine($"Updated chapter positions: {_chapterPositions.Count} chapters found");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating chapter positions: {ex.Message}");
            }
        }

        public void NavigateToNextChapter()
        {
            if (_textEditor == null || _chapterPositions.Count == 0)
            {
                ShowFeedback("No chapters found", false);
                return;
            }

            var currentOffset = _textEditor.CaretOffset;
            var nextChapter = _chapterPositions.FirstOrDefault(c => c.CharacterPosition > currentOffset);

            if (nextChapter != null)
            {
                NavigateToChapter(nextChapter);
                ShowFeedback($"Navigated to: {nextChapter.Title}", true);
            }
            else
            {
                ShowFeedback("Already at last chapter", false);
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
            var previousChapter = _chapterPositions.LastOrDefault(c => c.CharacterPosition < currentOffset);

            if (previousChapter != null)
            {
                NavigateToChapter(previousChapter);
                ShowFeedback($"Navigated to: {previousChapter.Title}", true);
            }
            else
            {
                ShowFeedback("Already at first chapter", false);
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