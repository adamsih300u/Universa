using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for managing font settings in markdown editors
    /// </summary>
    public class MarkdownFontService : IMarkdownFontService
    {
        private readonly IConfigurationService _configService;
        private string _currentFont;

        public MarkdownFontService(IConfigurationService configService)
        {
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public IEnumerable<FontFamily> GetAvailableFonts()
        {
            return System.Windows.Media.Fonts.SystemFontFamilies
                .OrderBy(f => f.Source)
                .ToList();
        }

        public void SetupFonts(ComboBox fontComboBox, ComboBox fontSizeComboBox, TextBox editor)
        {
            try
            {
                // Check if all required controls are initialized
                if (fontComboBox == null || fontSizeComboBox == null || editor == null)
                {
                    Debug.WriteLine("MarkdownFontService.SetupFonts: One or more UI controls are null - skipping font setup");
                    return;
                }

                // Get all installed fonts
                var fonts = GetAvailableFonts().ToList();

                // Populate the ComboBox
                fontComboBox.ItemsSource = fonts;

                // Load saved font preference
                var savedFont = _configService.Provider.GetValue<string>(ConfigurationKeys.Editor.Font);
                if (!string.IsNullOrEmpty(savedFont))
                {
                    var fontFamily = fonts.FirstOrDefault(f => f.Source == savedFont);
                    if (fontFamily != null)
                    {
                        fontComboBox.SelectedItem = fontFamily;
                        ApplyFont(fontFamily, editor, fontComboBox);
                    }
                }
                else
                {
                    // Use default font
                    var defaultFont = GetDefaultFont();
                    fontComboBox.SelectedItem = defaultFont;
                    ApplyFont(defaultFont, editor, fontComboBox);
                }

                // Load saved font size preference
                var savedFontSize = _configService.Provider.GetValue<double>(ConfigurationKeys.Editor.FontSize);
                if (savedFontSize > 0)
                {
                    var fontSizeItem = fontSizeComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => double.Parse(item.Content.ToString()) == savedFontSize);
                    if (fontSizeItem != null)
                    {
                        fontSizeComboBox.SelectedItem = fontSizeItem;
                        ApplyFontSize(savedFontSize, editor, fontSizeComboBox);
                    }
                }
                else
                {
                    // Use default font size
                    var defaultSize = GetDefaultFontSize();
                    var defaultSizeItem = fontSizeComboBox.Items.Cast<ComboBoxItem>()
                        .FirstOrDefault(item => double.Parse(item.Content.ToString()) == defaultSize);
                    if (defaultSizeItem != null)
                    {
                        fontSizeComboBox.SelectedItem = defaultSizeItem;
                        ApplyFontSize(defaultSize, editor, fontSizeComboBox);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting up fonts: {ex.Message}");
                MessageBox.Show($"Error setting up fonts: {ex.Message}", "Font Setup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ApplyFont(FontFamily font, TextBox editor, ComboBox fontComboBox)
        {
            if (font == null || editor == null) 
            {
                Debug.WriteLine("MarkdownFontService.ApplyFont: font or editor is null - skipping font application");
                return;
            }

            _currentFont = font.Source;
            editor.FontFamily = font;

            // Update all other open MarkdownTabs
            SynchronizeFontAcrossTabs(font, fontComboBox, isSize: false);
        }

        public void ApplyFontSize(double fontSize, TextBox editor, ComboBox fontSizeComboBox)
        {
            if (editor == null)
            {
                Debug.WriteLine("MarkdownFontService.ApplyFontSize: editor is null - skipping font size application");
                return;
            }

            editor.FontSize = fontSize;

            // Update all other open MarkdownTabs
            SynchronizeFontSizeAcrossTabs(fontSize, fontSizeComboBox);
        }

        public string GetCurrentFont()
        {
            return _currentFont;
        }

        public void OnFontSelectionChanged(FontFamily selectedFont, TextBox editor, ComboBox fontComboBox)
        {
            if (selectedFont == null || editor == null)
            {
                Debug.WriteLine("MarkdownFontService.OnFontSelectionChanged: selectedFont or editor is null - skipping");
                return;
            }

            ApplyFont(selectedFont, editor, fontComboBox);
            _configService.Provider.SetValue(ConfigurationKeys.Editor.Font, selectedFont.Source);
            _configService.Provider.Save();
        }

        public void OnFontSizeSelectionChanged(double fontSize, TextBox editor, ComboBox fontSizeComboBox)
        {
            if (editor == null)
            {
                Debug.WriteLine("MarkdownFontService.OnFontSizeSelectionChanged: editor is null - skipping");
                return;
            }

            ApplyFontSize(fontSize, editor, fontSizeComboBox);
            _configService.Provider.SetValue(ConfigurationKeys.Editor.FontSize, fontSize);
            _configService.Provider.Save();
        }

        public FontFamily GetDefaultFont()
        {
            var fonts = GetAvailableFonts().ToList();
            
            // Default to Cascadia Code if available, otherwise use the first monospace font
            return fonts.FirstOrDefault(f => f.Source == "Cascadia Code") 
                ?? fonts.FirstOrDefault(f => f.Source.Contains("Mono") || f.Source.Contains("Consolas"))
                ?? fonts.First();
        }

        public double GetDefaultFontSize()
        {
            return 12.0; // Default to 12pt
        }

        private void SynchronizeFontAcrossTabs(FontFamily font, ComboBox currentFontComboBox, bool isSize)
        {
            try
            {
                // Update all other open MarkdownTabs
                if (Application.Current.MainWindow is Views.MainWindow mainWindow)
                {
                    foreach (TabItem tab in mainWindow.MainTabControl.Items)
                    {
                        if (tab.Content is Views.MarkdownTabAvalon markdownTab)
                        {
                            // Skip the current tab to avoid infinite recursion
                            // Note: MarkdownTabAvalon doesn't have FontComboBox, so this may need to be updated
                            markdownTab.MarkdownEditor.FontFamily = font;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error synchronizing font across tabs: {ex.Message}");
            }
        }

        private void SynchronizeFontSizeAcrossTabs(double fontSize, ComboBox currentFontSizeComboBox)
        {
            try
            {
                // Update all other open MarkdownTabs
                if (Application.Current.MainWindow is Views.MainWindow mainWindow)
                {
                    foreach (TabItem tab in mainWindow.MainTabControl.Items)
                    {
                        if (tab.Content is Views.MarkdownTabAvalon markdownTab)
                        {
                            // Apply font size to AvalonEdit MarkdownTabAvalon
                            markdownTab.MarkdownEditor.FontSize = fontSize;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error synchronizing font size across tabs: {ex.Message}");
            }
        }
    }
} 