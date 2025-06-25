using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Interface for chapter navigation service in markdown documents
    /// </summary>
    public interface IChapterNavigationService
    {
        /// <summary>
        /// Event raised when navigation occurs to provide feedback
        /// </summary>
        event EventHandler<NavigationFeedbackEventArgs> NavigationFeedback;
        
        /// <summary>
        /// Initialize the service with editor and scroll viewer
        /// </summary>
        void Initialize(TextBox editor, ScrollViewer scrollViewer);
        
        /// <summary>
        /// Update chapter positions based on current text content
        /// </summary>
        void UpdateChapterPositions(string text);
        
        /// <summary>
        /// Navigate to the next chapter from current cursor position
        /// </summary>
        void NavigateToNextChapter();
        
        /// <summary>
        /// Navigate to the previous chapter from current cursor position
        /// </summary>
        void NavigateToPreviousChapter();
        
        /// <summary>
        /// Navigate to a specific chapter by index
        /// </summary>
        void NavigateToChapter(int chapterIndex);
        
        /// <summary>
        /// Get the index of the current chapter based on cursor position
        /// </summary>
        int GetCurrentChapterIndex();
        
        /// <summary>
        /// Get the title of the current chapter
        /// </summary>
        string GetCurrentChapterTitle();
        
        /// <summary>
        /// Get all chapter positions and titles
        /// </summary>
        IReadOnlyList<(int position, string title)> GetChapterPositions();
        
        /// <summary>
        /// Check if there are any chapters in the document
        /// </summary>
        bool HasChapters { get; }
        
        /// <summary>
        /// Get the number of chapters found
        /// </summary>
        int ChapterCount { get; }
    }
    
    /// <summary>
    /// Event args for navigation feedback
    /// </summary>
    public class NavigationFeedbackEventArgs : EventArgs
    {
        public string Message { get; }
        public bool IsSuccess { get; }
        
        public NavigationFeedbackEventArgs(string message, bool isSuccess = true)
        {
            Message = message;
            IsSuccess = isSuccess;
        }
    }
} 