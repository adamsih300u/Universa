using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Status manager adapter for AvalonEdit that provides word count and status information
    /// Maintains compatibility with AI Chat Sidebar integrations
    /// </summary>
    public class AvalonEditStatusManager : IMarkdownStatusManager
    {
        private TextEditor _textEditor;
        private TextBlock _statusTextBlock;
        private readonly IChapterNavigationService _chapterNavigationService;

        public event EventHandler<StatusUpdateEventArgs> StatusUpdated;

        public AvalonEditStatusManager(IChapterNavigationService chapterNavigationService = null)
        {
            _chapterNavigationService = chapterNavigationService;
        }

        public void Initialize(TextBox editor, TextBlock statusTextBlock)
        {
            // This adapter is designed for AvalonEdit, so we need a different initialization approach
            _statusTextBlock = statusTextBlock;
        }

        public void Initialize(TextEditor avalonEditor, TextBlock statusTextBlock)
        {
            _textEditor = avalonEditor;
            _statusTextBlock = statusTextBlock;

            // Subscribe to text changes for live updates
            if (_textEditor != null)
            {
                _textEditor.Document.TextChanged += (s, e) => UpdateStatus(_textEditor.Text);
                _textEditor.TextArea.Caret.PositionChanged += (s, e) => UpdateStatus(_textEditor.Text);
            }
        }

        public void UpdateStatus(string content, string chapterInfo = null)
        {
            if (_statusTextBlock == null)
                return;

            try
            {
                var wordCount = CalculateWordCount(content);
                var characterCount = CalculateCharacterCount(content);
                var paragraphCount = CountParagraphs(content);
                var currentChapter = GetCurrentChapterInfo();

                var readingTime = CalculateReadingTime(wordCount);
                var effectiveChapterInfo = chapterInfo ?? currentChapter;
                var statusText = FormatStatusText(wordCount, characterCount, readingTime, effectiveChapterInfo);

                _statusTextBlock.Text = statusText;
                
                // Fire status updated event
                StatusUpdated?.Invoke(this, new StatusUpdateEventArgs
                {
                    StatusText = statusText,
                    WordCount = wordCount,
                    CharacterCount = characterCount,
                    ReadingTime = readingTime,
                    ChapterInfo = effectiveChapterInfo
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error updating status: {ex.Message}");
                _statusTextBlock.Text = "Status unavailable";
            }
        }

        public int CalculateWordCount(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Remove markdown formatting for more accurate word count
            var cleanContent = RemoveMarkdownFormatting(content);
            
            // Split by whitespace and count non-empty entries
            return cleanContent
                .Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
        }

        public int CalculateCharacterCount(string content)
        {
            return content?.Length ?? 0;
        }

        public string CalculateReadingTime(int wordCount)
        {
            if (wordCount == 0) return "0 min";
            
            // Average reading speed: 200-250 words per minute
            const int wordsPerMinute = 225;
            var minutes = Math.Ceiling(wordCount / (double)wordsPerMinute);
            
            if (minutes < 60)
                return $"{minutes} min";
            
            var hours = Math.Floor(minutes / 60);
            var remainingMinutes = minutes % 60;
            
            if (remainingMinutes == 0)
                return $"{hours}h";
            
            return $"{hours}h {remainingMinutes}m";
        }

        public string FormatStatusText(int wordCount, int charCount, string readingTime, string chapterInfo = null)
        {
            var statusText = $"Words: {wordCount:N0} | Characters: {charCount:N0} | Reading: {readingTime}";
            
            if (!string.IsNullOrEmpty(chapterInfo))
            {
                statusText += $" | {chapterInfo}";
            }

            return statusText;
        }

        public void UpdateStatusWithChapter(string text, string chapterInfo)
        {
            UpdateStatus(text, chapterInfo);
        }

        private int CountParagraphs(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Count paragraphs by splitting on double newlines
            var paragraphs = content
                .Split(new string[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Count();

            return Math.Max(1, paragraphs); // At least 1 paragraph if there's any content
        }

        private string RemoveMarkdownFormatting(string content)
        {
            if (string.IsNullOrEmpty(content))
                return string.Empty;

            // Remove common markdown formatting
            var cleaned = content;
            
            // Remove headers
            cleaned = Regex.Replace(cleaned, @"^#{1,6}\s+", "", RegexOptions.Multiline);
            
            // Remove bold and italic
            cleaned = Regex.Replace(cleaned, @"\*\*([^*]+)\*\*", "$1");
            cleaned = Regex.Replace(cleaned, @"\*([^*]+)\*", "$1");
            cleaned = Regex.Replace(cleaned, @"__([^_]+)__", "$1");
            cleaned = Regex.Replace(cleaned, @"_([^_]+)_", "$1");
            
            // Remove inline code
            cleaned = Regex.Replace(cleaned, @"`([^`]+)`", "$1");
            
            // Remove links
            cleaned = Regex.Replace(cleaned, @"\[([^\]]*)\]\([^)]*\)", "$1");
            
            // Remove blockquotes
            cleaned = Regex.Replace(cleaned, @"^>\s*", "", RegexOptions.Multiline);
            
            // Remove list markers
            cleaned = Regex.Replace(cleaned, @"^\s*[-*+]\s+", "", RegexOptions.Multiline);
            cleaned = Regex.Replace(cleaned, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);

            return cleaned;
        }

        private string GetCurrentChapterInfo()
        {
            try
            {
                if (_chapterNavigationService == null)
                    return string.Empty;

                var currentChapterTitle = _chapterNavigationService.GetCurrentChapterTitle();
                var currentChapterIndex = _chapterNavigationService.GetCurrentChapterIndex();
                var allChapters = _chapterNavigationService.GetChapterPositions();

                if (!string.IsNullOrEmpty(currentChapterTitle) && currentChapterTitle != "No chapter")
                {
                    var totalChapters = allChapters?.Count ?? 0;
                    if (totalChapters > 0)
                    {
                        return $"Chapter {currentChapterIndex + 1}/{totalChapters}: {currentChapterTitle}";
                    }
                    else
                    {
                        return $"Current: {currentChapterTitle}";
                    }
                }

                return string.Empty;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting chapter info: {ex.Message}");
                return string.Empty;
            }
        }

        public void SetTemporaryStatus(string message, TimeSpan duration)
        {
            if (_statusTextBlock == null)
                return;

            var originalText = _statusTextBlock.Text;
            _statusTextBlock.Text = message;

            // Restore original status after duration
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = duration
            };

            timer.Tick += (s, e) =>
            {
                timer.Stop();
                if (_textEditor != null)
                {
                    UpdateStatus(_textEditor.Text);
                }
                else
                {
                    _statusTextBlock.Text = originalText;
                }
            };

            timer.Start();
        }
    }
} 