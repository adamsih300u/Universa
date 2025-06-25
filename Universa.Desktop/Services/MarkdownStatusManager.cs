using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Threading;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for managing status information display in markdown editors
    /// </summary>
    public class MarkdownStatusManager : IMarkdownStatusManager
    {
        private TextBox _editor;
        private TextBlock _statusDisplay;
        private string _lastChapterInfo = "";
        private const int WORDS_PER_MINUTE = 225; // Average reading speed

        public event EventHandler<StatusUpdateEventArgs> StatusUpdated;

        public void Initialize(TextBox editor, TextBlock statusDisplay)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _statusDisplay = statusDisplay ?? throw new ArgumentNullException(nameof(statusDisplay));
        }

        public async void UpdateStatus(string text, string chapterInfo = null)
        {
            try
            {
                // Check if initialized
                if (_statusDisplay == null)
                {
                    Debug.WriteLine("MarkdownStatusManager not initialized - skipping status update");
                    return;
                }

                await Task.Run(() =>
                {
                    var wordCount = CalculateWordCount(text);
                    var charCount = CalculateCharacterCount(text);
                    var readingTime = CalculateReadingTime(wordCount);
                    var statusText = FormatStatusText(wordCount, charCount, readingTime, chapterInfo);

                    // Update UI on the UI thread
                    _statusDisplay.Dispatcher.Invoke(() =>
                    {
                        _statusDisplay.Text = statusText;
                        
                        // Fire event for any listeners
                        StatusUpdated?.Invoke(this, new StatusUpdateEventArgs
                        {
                            StatusText = statusText,
                            WordCount = wordCount,
                            CharacterCount = charCount,
                            ReadingTime = readingTime,
                            ChapterInfo = chapterInfo
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating status: {ex.Message}");
            }
        }

        public void UpdateStatusWithChapter(string text, string chapterInfo)
        {
            try
            {
                // Check if initialized
                if (_statusDisplay == null)
                {
                    Debug.WriteLine("MarkdownStatusManager not initialized - skipping status update with chapter");
                    return;
                }

                // Only update if chapter info has changed to avoid constant UI updates
                if (_lastChapterInfo == chapterInfo) return;
                
                _lastChapterInfo = chapterInfo;
                
                var wordCount = CalculateWordCount(text);
                var charCount = CalculateCharacterCount(text);
                var readingTime = CalculateReadingTime(wordCount);
                var statusText = FormatStatusText(wordCount, charCount, readingTime, chapterInfo);
                
                // Update UI directly since this is called from UI thread
                _statusDisplay.Text = statusText;
                
                // Fire event for any listeners
                StatusUpdated?.Invoke(this, new StatusUpdateEventArgs
                {
                    StatusText = statusText,
                    WordCount = wordCount,
                    CharacterCount = charCount,
                    ReadingTime = readingTime,
                    ChapterInfo = chapterInfo
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating status with chapter: {ex.Message}");
            }
        }

        public int CalculateWordCount(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public int CalculateCharacterCount(string text)
        {
            return text?.Length ?? 0;
        }

        public string CalculateReadingTime(int wordCount)
        {
            var readingTimeMinutes = Math.Max(1, (int)Math.Ceiling(wordCount / (double)WORDS_PER_MINUTE));
            return readingTimeMinutes == 1 ? "1 minute" : $"{readingTimeMinutes} minutes";
        }

        public string FormatStatusText(int wordCount, int charCount, string readingTime, string chapterInfo = null)
        {
            var baseText = $"Words: {wordCount} | Characters: {charCount} | Reading time: {readingTime}";
            
            if (!string.IsNullOrEmpty(chapterInfo))
            {
                return baseText + chapterInfo;
            }
            
            return baseText;
        }
    }
} 