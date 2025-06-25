using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for handling chapter navigation in markdown documents
    /// </summary>
    public class ChapterNavigationService : IChapterNavigationService
    {
        private TextBox _editor;
        private ScrollViewer _scrollViewer;
        private List<(int position, string title)> _chapterPositions = new List<(int, string)>();
        
        public event EventHandler<NavigationFeedbackEventArgs> NavigationFeedback;
        
        public bool HasChapters => _chapterPositions.Count > 0;
        public int ChapterCount => _chapterPositions.Count;
        
        public void Initialize(TextBox editor, ScrollViewer scrollViewer)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _scrollViewer = scrollViewer; // Can be null, will use fallback methods
        }
        
        public void UpdateChapterPositions(string text)
        {
            try
            {
                _chapterPositions.Clear();
                
                if (string.IsNullOrEmpty(text))
                    return;

                // Find all headings (H1-H3) that could be considered chapters/sections
                // Priority: H2 first (traditional chapters), then H1 (main sections), then H3 (subsections)
                var headingMatches = Regex.Matches(text, @"^(#{1,3})\s+(.+)$", RegexOptions.Multiline);
                
                // First, try to find H2 headings (traditional chapters)
                var h2Matches = headingMatches.Cast<Match>().Where(m => m.Groups[1].Value.Length == 2).ToList();
                
                if (h2Matches.Any())
                {
                    // Use H2 headings as chapters
                    foreach (var match in h2Matches)
                    {
                        var title = match.Groups[2].Value.Trim();
                        var position = match.Index;
                        
                        // Validate that position is within text bounds
                        if (position < text.Length)
                        {
                            _chapterPositions.Add((position, $"Chapter: {title}"));
                        }
                    }
                }
                else
                {
                    // Fallback to H1 headings if no H2 found
                    var h1Matches = headingMatches.Cast<Match>().Where(m => m.Groups[1].Value.Length == 1).ToList();
                    
                    if (h1Matches.Any())
                    {
                        foreach (var match in h1Matches)
                        {
                            var title = match.Groups[2].Value.Trim();
                            var position = match.Index;
                            
                            // Validate that position is within text bounds
                            if (position < text.Length)
                            {
                                _chapterPositions.Add((position, $"Section: {title}"));
                            }
                        }
                    }
                    else
                    {
                        // Last resort: use H3 headings
                        var h3Matches = headingMatches.Cast<Match>().Where(m => m.Groups[1].Value.Length == 3).ToList();
                        
                        foreach (var match in h3Matches)
                        {
                            var title = match.Groups[2].Value.Trim();
                            var position = match.Index;
                            
                            // Validate that position is within text bounds
                            if (position < text.Length)
                            {
                                _chapterPositions.Add((position, $"Subsection: {title}"));
                            }
                        }
                    }
                }

                // Sort chapters by position to ensure correct order
                _chapterPositions.Sort((a, b) => a.position.CompareTo(b.position));

                Debug.WriteLine($"Found {_chapterPositions.Count} navigable sections");
                
                // Debug output of found chapters
                for (int i = 0; i < _chapterPositions.Count; i++)
                {
                    Debug.WriteLine($"  Chapter {i}: {_chapterPositions[i].title} at position {_chapterPositions[i].position}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating chapter positions: {ex.Message}");
                OnNavigationFeedback($"Error analyzing document structure: {ex.Message}", false);
                _chapterPositions.Clear(); // Clear invalid data
            }
        }
        
        public void NavigateToNextChapter()
        {
            try
            {
                Debug.WriteLine("NavigateToNextChapter called");
                
                if (_editor == null)
                {
                    OnNavigationFeedback("Editor not initialized", false);
                    return;
                }
                
                if (_chapterPositions.Count == 0)
                {
                    Debug.WriteLine("No chapter positions cached, updating...");
                    UpdateChapterPositions(_editor.Text);
                    if (_chapterPositions.Count == 0)
                    {
                        // No sections found, show a brief message
                        OnNavigationFeedback("No sections found. Use headings (# Title, ## Chapter, ### Section) to create navigable sections.", false);
                        return;
                    }
                }

                int currentPosition = _editor.CaretIndex;
                Debug.WriteLine($"Current cursor position: {currentPosition}");
                Debug.WriteLine($"Available chapters: {_chapterPositions.Count}");
                
                // Find the next chapter after the current cursor position
                var nextChapter = _chapterPositions.FirstOrDefault(ch => ch.position > currentPosition);
                
                if (nextChapter.position > 0 && !string.IsNullOrEmpty(nextChapter.title))
                {
                    Debug.WriteLine($"Found next chapter at position {nextChapter.position}: {nextChapter.title}");
                    NavigateToChapterInternal(nextChapter.position, nextChapter.title);
                }
                else
                {
                    // No next chapter found - check if we should wrap around or stay at current
                    if (_chapterPositions.Count > 0)
                    {
                        // Only wrap if we're clearly before the last chapter
                        var lastChapter = _chapterPositions[_chapterPositions.Count - 1];
                        if (currentPosition < lastChapter.position - 50) // Give some buffer
                        {
                            var firstChapter = _chapterPositions[0];
                            Debug.WriteLine($"Wrapping to first chapter at position {firstChapter.position}: {firstChapter.title}");
                            NavigateToChapterInternal(firstChapter.position, firstChapter.title);
                            OnNavigationFeedback("Reached end of document. Navigated to first chapter.");
                        }
                        else
                        {
                            OnNavigationFeedback("Already at the last chapter.", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to next chapter: {ex.Message}");
                OnNavigationFeedback($"Error navigating to next chapter: {ex.Message}", false);
            }
        }
        
        public void NavigateToPreviousChapter()
        {
            try
            {
                Debug.WriteLine("NavigateToPreviousChapter called");
                
                if (_editor == null)
                {
                    OnNavigationFeedback("Editor not initialized", false);
                    return;
                }
                
                if (_chapterPositions.Count == 0)
                {
                    Debug.WriteLine("No chapter positions cached, updating...");
                    UpdateChapterPositions(_editor.Text);
                    if (_chapterPositions.Count == 0)
                    {
                        OnNavigationFeedback("No sections found. Use headings (# Title, ## Chapter, ### Section) to create navigable sections.", false);
                        return;
                    }
                }

                int currentPosition = _editor.CaretIndex;
                Debug.WriteLine($"Current cursor position: {currentPosition}");
                Debug.WriteLine($"Available chapters: {_chapterPositions.Count}");
                
                // Find the previous chapter before the current cursor position
                // Use a more specific approach to avoid the default tuple issue
                (int position, string title) prevChapter = (-1, null);
                
                for (int i = _chapterPositions.Count - 1; i >= 0; i--)
                {
                    if (_chapterPositions[i].position < currentPosition)
                    {
                        prevChapter = _chapterPositions[i];
                        break;
                    }
                }
                
                if (prevChapter.position >= 0)
                {
                    Debug.WriteLine($"Found previous chapter at position {prevChapter.position}: {prevChapter.title}");
                    NavigateToChapterInternal(prevChapter.position, prevChapter.title);
                }
                else
                {
                    // No previous chapter found - check if we should wrap around or stay at current
                    if (_chapterPositions.Count > 0)
                    {
                        // Only wrap if we're clearly past the first chapter
                        var firstChapter = _chapterPositions[0];
                        if (currentPosition > firstChapter.position + 50) // Give some buffer
                        {
                            var lastChapter = _chapterPositions[_chapterPositions.Count - 1];
                            Debug.WriteLine($"Wrapping to last chapter at position {lastChapter.position}: {lastChapter.title}");
                            NavigateToChapterInternal(lastChapter.position, lastChapter.title);
                            OnNavigationFeedback("Reached beginning of document. Navigated to last chapter.");
                        }
                        else
                        {
                            OnNavigationFeedback("Already at the first chapter.", false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to previous chapter: {ex.Message}");
                OnNavigationFeedback($"Error navigating to previous chapter: {ex.Message}", false);
            }
        }
        
        public void NavigateToChapter(int chapterIndex)
        {
            try
            {
                if (chapterIndex < 0 || chapterIndex >= _chapterPositions.Count)
                {
                    OnNavigationFeedback($"Invalid chapter index: {chapterIndex}", false);
                    return;
                }
                
                var chapter = _chapterPositions[chapterIndex];
                NavigateToChapterInternal(chapter.position, chapter.title);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to chapter {chapterIndex}: {ex.Message}");
                OnNavigationFeedback($"Error navigating to chapter: {ex.Message}", false);
            }
        }
        
        private void NavigateToChapterInternal(int position, string title)
        {
            try
            {
                if (_editor == null)
                {
                    OnNavigationFeedback("Editor not initialized", false);
                    return;
                }
                
                // Validate that the position is within the current text bounds
                if (position < 0 || position >= _editor.Text.Length)
                {
                    Debug.WriteLine($"Invalid chapter position {position}, text length is {_editor.Text.Length}");
                    OnNavigationFeedback($"Chapter position is no longer valid. Document may have changed.", false);
                    
                    // Refresh chapter positions and try again
                    UpdateChapterPositions(_editor.Text);
                    return;
                }
                
                // Set cursor position to the chapter heading
                _editor.CaretIndex = position;
                
                // Scroll to the chapter position
                if (_scrollViewer != null)
                {
                    // Get the line number of the chapter
                    var line = _editor.GetLineIndexFromCharacterIndex(position);
                    
                    // Use a more accurate method to get the actual position
                    try
                    {
                        var rect = _editor.GetRectFromCharacterIndex(position);
                        double targetOffset = rect.Top;
                        
                        // Scroll so the chapter heading appears near the top (with small margin)
                        double marginFromTop = _editor.FontSize * 2; // 2 lines margin from top
                        double scrollToOffset = Math.Max(0, targetOffset - marginFromTop);
                        
                        _scrollViewer.ScrollToVerticalOffset(scrollToOffset);
                        Debug.WriteLine($"Scrolled to precise offset {scrollToOffset} for character position {position} (rect.Top: {rect.Top})");
                    }
                    catch (Exception ex)
                    {
                        // Fallback to line-based calculation if GetRectFromCharacterIndex fails
                        Debug.WriteLine($"GetRectFromCharacterIndex failed: {ex.Message}, using fallback");
                        double lineHeight = _editor.FontSize * 1.7; // 1.7 is our line height multiplier
                        double targetOffset = line * lineHeight;
                        double marginFromTop = lineHeight * 2; // 2 lines margin from top
                        double scrollToOffset = Math.Max(0, targetOffset - marginFromTop);
                        _scrollViewer.ScrollToVerticalOffset(scrollToOffset);
                        Debug.WriteLine($"Scrolled to calculated offset {scrollToOffset} for line {line}");
                    }
                }
                else
                {
                    // Fallback to the old method if ScrollViewer not found
                    var line = _editor.GetLineIndexFromCharacterIndex(position);
                    _editor.ScrollToLine(line);
                    Debug.WriteLine("Used fallback scrolling method");
                }
                
                // Focus the editor
                _editor.Focus();
                
                // Show brief notification of current chapter
                OnNavigationFeedback(title);
                
                Debug.WriteLine($"Successfully navigated to chapter: {title} at position {position}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error navigating to chapter: {ex.Message}");
                OnNavigationFeedback($"Error navigating to chapter: {ex.Message}", false);
            }
        }
        
        public int GetCurrentChapterIndex()
        {
            try
            {
                if (_editor == null || _chapterPositions.Count == 0)
                    return -1;

                int currentPosition = _editor.CaretIndex;
                
                // Ensure we have up-to-date chapter positions
                if (_chapterPositions.Count == 0)
                {
                    UpdateChapterPositions(_editor.Text);
                    if (_chapterPositions.Count == 0)
                        return -1;
                }
                
                // Find the chapter that the cursor is currently in
                // We want the latest chapter that starts at or before the current position
                int bestMatch = -1;
                
                for (int i = 0; i < _chapterPositions.Count; i++)
                {
                    var chapterPos = _chapterPositions[i].position;
                    
                    // If we're at or past this chapter's start position
                    if (currentPosition >= chapterPos)
                    {
                        bestMatch = i;
                    }
                    else
                    {
                        // We've found a chapter that starts after our current position
                        // So the previous one (bestMatch) is our chapter
                        break;
                    }
                }
                
                // Additional validation: if we're very close to the start of the next chapter
                // (within 10 characters), consider us still in the current chapter
                if (bestMatch >= 0 && bestMatch < _chapterPositions.Count - 1)
                {
                    var nextChapterPos = _chapterPositions[bestMatch + 1].position;
                    if (currentPosition > nextChapterPos - 10 && currentPosition < nextChapterPos)
                    {
                        // We're very close to the next chapter, stay with current
                        // This prevents jittery chapter detection near boundaries
                    }
                }
                
                Debug.WriteLine($"Current position {currentPosition} maps to chapter index {bestMatch}" + 
                    (bestMatch >= 0 ? $" ({_chapterPositions[bestMatch].title})" : ""));
                
                return bestMatch;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current chapter index: {ex.Message}");
                return -1;
            }
        }
        
        public string GetCurrentChapterTitle()
        {
            try
            {
                int chapterIndex = GetCurrentChapterIndex();
                if (chapterIndex >= 0 && chapterIndex < _chapterPositions.Count)
                {
                    return _chapterPositions[chapterIndex].title;
                }
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting current chapter title: {ex.Message}");
                return null;
            }
        }
        
        public IReadOnlyList<(int position, string title)> GetChapterPositions()
        {
            return _chapterPositions.AsReadOnly();
        }
        
        private void OnNavigationFeedback(string message, bool isSuccess = true)
        {
            NavigationFeedback?.Invoke(this, new NavigationFeedbackEventArgs(message, isSuccess));
        }
    }
} 