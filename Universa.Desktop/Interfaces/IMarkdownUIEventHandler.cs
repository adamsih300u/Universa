using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for handling UI events in markdown editors
    /// </summary>
    public interface IMarkdownUIEventHandler
    {
        /// <summary>
        /// Event fired when the modified state should change
        /// </summary>
        event EventHandler<bool> ModifiedStateChanged;

        /// <summary>
        /// Initializes the event handler with the required UI controls
        /// </summary>
        void Initialize(TextBox editor, Func<string> getFilePath, Action<bool> setModified);

        /// <summary>
        /// Sets callback methods for operations that require access to MarkdownTab internals
        /// </summary>
        void SetCallbacks(
            Action showFrontmatterDialog,
            Action toggleFrontmatterVisibility,
            Action updateToggleButtonAppearance,
            Action refreshEditorContent);

        /// <summary>
        /// Handles bold button click
        /// </summary>
        void HandleBoldButtonClick();

        /// <summary>
        /// Handles italic button click
        /// </summary>
        void HandleItalicButtonClick();

        /// <summary>
        /// Handles TTS button click
        /// </summary>
        void HandleTTSButtonClick();

        /// <summary>
        /// Handles frontmatter button click
        /// </summary>
        void HandleFrontmatterButtonClick();

        /// <summary>
        /// Handles toggle frontmatter button click
        /// </summary>
        void HandleToggleFrontmatterButtonClick();

        /// <summary>
        /// Handles heading button clicks
        /// </summary>
        void HandleHeadingButtonClick(int level);

        /// <summary>
        /// Handles font combo box selection changed
        /// </summary>
        void HandleFontSelectionChanged(FontFamily selectedFont);

        /// <summary>
        /// Handles font size combo box selection changed
        /// </summary>
        void HandleFontSizeSelectionChanged(double fontSize);

        /// <summary>
        /// Handles refresh versions button click
        /// </summary>
        void HandleRefreshVersionsButtonClick();
    }
} 