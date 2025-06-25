using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Service for managing font settings in markdown editors
    /// </summary>
    public interface IMarkdownFontService
    {
        /// <summary>
        /// Gets all available system fonts
        /// </summary>
        IEnumerable<FontFamily> GetAvailableFonts();

        /// <summary>
        /// Sets up font selection for a markdown tab
        /// </summary>
        void SetupFonts(ComboBox fontComboBox, ComboBox fontSizeComboBox, TextBox editor);

        /// <summary>
        /// Applies a font to the editor and synchronizes across all tabs
        /// </summary>
        void ApplyFont(FontFamily font, TextBox editor, ComboBox fontComboBox);

        /// <summary>
        /// Applies a font size to the editor and synchronizes across all tabs
        /// </summary>
        void ApplyFontSize(double fontSize, TextBox editor, ComboBox fontSizeComboBox);

        /// <summary>
        /// Gets the current font family
        /// </summary>
        string GetCurrentFont();

        /// <summary>
        /// Handles font selection change
        /// </summary>
        void OnFontSelectionChanged(FontFamily selectedFont, TextBox editor, ComboBox fontComboBox);

        /// <summary>
        /// Handles font size selection change
        /// </summary>
        void OnFontSizeSelectionChanged(double fontSize, TextBox editor, ComboBox fontSizeComboBox);

        /// <summary>
        /// Gets the default font family
        /// </summary>
        FontFamily GetDefaultFont();

        /// <summary>
        /// Gets the default font size
        /// </summary>
        double GetDefaultFontSize();
    }
} 