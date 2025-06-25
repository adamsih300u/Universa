using System;
using System.Windows.Controls;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for managing status information display in markdown editors
    /// </summary>
    public interface IMarkdownStatusManager
    {
        /// <summary>
        /// Event fired when status information should be updated
        /// </summary>
        event EventHandler<StatusUpdateEventArgs> StatusUpdated;

        /// <summary>
        /// Initializes the status manager with the required UI controls
        /// </summary>
        void Initialize(TextBox editor, TextBlock statusDisplay);

        /// <summary>
        /// Updates the status display with current document statistics
        /// </summary>
        void UpdateStatus(string text, string chapterInfo = null);

        /// <summary>
        /// Calculates word count for the given text
        /// </summary>
        int CalculateWordCount(string text);

        /// <summary>
        /// Calculates character count for the given text
        /// </summary>
        int CalculateCharacterCount(string text);

        /// <summary>
        /// Calculates estimated reading time for the given text
        /// </summary>
        string CalculateReadingTime(int wordCount);

        /// <summary>
        /// Formats the complete status text with all statistics
        /// </summary>
        string FormatStatusText(int wordCount, int charCount, string readingTime, string chapterInfo = null);

        /// <summary>
        /// Updates status with chapter information
        /// </summary>
        void UpdateStatusWithChapter(string text, string chapterInfo);
    }

    /// <summary>
    /// Event arguments for status updates
    /// </summary>
    public class StatusUpdateEventArgs : EventArgs
    {
        public string StatusText { get; set; }
        public int WordCount { get; set; }
        public int CharacterCount { get; set; }
        public string ReadingTime { get; set; }
        public string ChapterInfo { get; set; }
    }
} 