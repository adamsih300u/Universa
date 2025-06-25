using System;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Helpers;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for setting up and configuring markdown editors
    /// </summary>
    public interface IMarkdownEditorSetupService
    {
        /// <summary>
        /// Sets up the editor with all necessary configurations and event handlers
        /// </summary>
        /// <param name="editor">The TextBox editor to configure</param>
        /// <param name="editorScrollViewer">The ScrollViewer containing the editor</param>
        /// <param name="onNavigateToNextChapter">Callback for next chapter navigation</param>
        /// <param name="onNavigateToPreviousChapter">Callback for previous chapter navigation</param>
        /// <param name="onScrollByPage">Callback for page scrolling</param>
        /// <param name="onTextChanged">Callback for text changed events</param>
        /// <param name="onSelectionChanged">Callback for selection changed events</param>
        /// <param name="onScrollChanged">Callback for scroll changed events</param>
        /// <returns>The configured TextHighlighter instance</returns>
        TextHighlighter SetupEditor(
            TextBox editor,
            ScrollViewer editorScrollViewer,
            Action onNavigateToNextChapter,
            Action onNavigateToPreviousChapter,
            Action<bool> onScrollByPage,
            Action<object, TextChangedEventArgs> onTextChanged,
            Action<object, RoutedEventArgs> onSelectionChanged,
            Action<object, ScrollChangedEventArgs> onScrollChanged);

        /// <summary>
        /// Applies theme settings to the editor
        /// </summary>
        /// <param name="editor">The editor to apply theme to</param>
        /// <param name="isDarkMode">Whether dark mode is enabled</param>
        void ApplyTheme(TextBox editor, bool isDarkMode);

        /// <summary>
        /// Gets a ScrollViewer from a dependency object tree
        /// </summary>
        /// <param name="depObj">The dependency object to search</param>
        /// <returns>The found ScrollViewer or null</returns>
        ScrollViewer GetScrollViewer(DependencyObject depObj);
    }
} 