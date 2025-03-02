using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Universa.Desktop
{
    public enum Theme
    {
        Light,
        Dark,
        Default,
        Custom
    }

    public class ThemeDefinition
    {
        public string Name { get; set; }
        public Color WindowBackground { get; set; }
        public Color MenuBackground { get; set; }
        public Color BorderColor { get; set; }
        public Color TextColor { get; set; }
        public Color ButtonBackground { get; set; }
        public Color ListItemSelectedBackground { get; set; }
        public Color ListItemSelectedInactiveBackground { get; set; }
        public Color ListItemHoverBackground { get; set; }
        public Color ActiveTabBackground { get; set; }
        public Color InactiveTabBackground { get; set; }
        public Color MediaControlsBackground { get; set; }

        public static ThemeDefinition Default => new ThemeDefinition
        {
            Name = "Default",
            WindowBackground = Colors.White,
            MenuBackground = Color.FromRgb(240, 240, 240),
            BorderColor = Color.FromRgb(171, 173, 179),
            TextColor = Colors.Black,
            ButtonBackground = Color.FromRgb(240, 240, 240),
            ListItemSelectedBackground = Color.FromRgb(96, 165, 250),
            ListItemSelectedInactiveBackground = Color.FromRgb(37, 99, 235),
            ListItemHoverBackground = Color.FromRgb(226, 232, 240),
            ActiveTabBackground = Colors.White,
            InactiveTabBackground = Color.FromRgb(240, 240, 240),
            MediaControlsBackground = Color.FromRgb(240, 240, 240)
        };

        public static ThemeDefinition Dark => new ThemeDefinition
        {
            Name = "Dark",
            WindowBackground = Color.FromRgb(30, 30, 30),
            MenuBackground = Color.FromRgb(37, 37, 37),
            BorderColor = Color.FromRgb(63, 63, 63),
            TextColor = Colors.White,
            ButtonBackground = Color.FromRgb(60, 60, 60),
            ListItemSelectedBackground = Color.FromRgb(30, 41, 59),
            ListItemSelectedInactiveBackground = Color.FromRgb(9, 71, 113),
            ListItemHoverBackground = Color.FromRgb(45, 55, 72),
            ActiveTabBackground = Color.FromRgb(45, 45, 45),
            InactiveTabBackground = Color.FromRgb(30, 30, 30),
            MediaControlsBackground = Color.FromRgb(48, 48, 48)
        };

        public static ThemeDefinition Custom => new ThemeDefinition
        {
            Name = "Custom",
            WindowBackground = Color.FromRgb(45, 45, 45),
            MenuBackground = Color.FromRgb(52, 52, 52),
            BorderColor = Color.FromRgb(78, 78, 78),
            TextColor = Colors.White,
            ButtonBackground = Color.FromRgb(75, 75, 75),
            ListItemSelectedBackground = Color.FromRgb(30, 41, 59),
            ListItemSelectedInactiveBackground = Color.FromRgb(9, 71, 113),
            ListItemHoverBackground = Color.FromRgb(45, 55, 72),
            ActiveTabBackground = Color.FromRgb(60, 60, 60),
            InactiveTabBackground = Color.FromRgb(45, 45, 45),
            MediaControlsBackground = Color.FromRgb(63, 63, 63)
        };

        public ResourceDictionary ToResourceDictionary()
        {
            var dict = new ResourceDictionary();
            
            // Basic window colors
            dict["WindowBackgroundBrush"] = new SolidColorBrush(WindowBackground);
            dict["MenuBackgroundBrush"] = new SolidColorBrush(MenuBackground);
            dict["BorderBrush"] = new SolidColorBrush(BorderColor);
            dict["TextBrush"] = new SolidColorBrush(TextColor);
            dict["ButtonBackgroundBrush"] = new SolidColorBrush(ButtonBackground);
            
            // List and selection colors
            dict["ListItemBackgroundBrush"] = new SolidColorBrush(WindowBackground);
            dict["ListItemSelectedBackgroundBrush"] = new SolidColorBrush(ListItemSelectedBackground);
            dict["ListItemSelectedInactiveBackgroundBrush"] = new SolidColorBrush(ListItemSelectedInactiveBackground);
            dict["ListItemHoverBackgroundBrush"] = new SolidColorBrush(ListItemHoverBackground);
            
            // Tab colors
            dict["TabBackgroundBrush"] = new SolidColorBrush(MenuBackground);
            dict["ActiveTabBackgroundBrush"] = new SolidColorBrush(ActiveTabBackground);
            dict["InactiveTabBackgroundBrush"] = new SolidColorBrush(InactiveTabBackground);

            // Media controls
            dict["MediaControlsBackgroundBrush"] = new SolidColorBrush(MediaControlsBackground);
            
            // Control colors
            dict["ControlBackgroundBrush"] = new SolidColorBrush(MenuBackground);
            dict["HighlightBrush"] = new SolidColorBrush(ListItemSelectedBackground);
            dict["PlaceholderTextBrush"] = new SolidColorBrush(Color.FromArgb(128, TextColor.R, TextColor.G, TextColor.B));
            dict["HighlightedTextBrush"] = new SolidColorBrush(TextColor);
            dict["HighlightTextBrush"] = new SolidColorBrush(TextColor);
            dict["InactiveSelectionHighlightBrush"] = new SolidColorBrush(ListItemSelectedInactiveBackground);
            dict["InactiveSelectionHighlightTextBrush"] = new SolidColorBrush(TextColor);
            dict["InactiveSelectionTextBrush"] = new SolidColorBrush(TextColor);
            dict["SelectedItemBrush"] = new SolidColorBrush(ListItemSelectedBackground);
            dict["SelectedItemTextBrush"] = new SolidColorBrush(TextColor);
            dict["InactiveSelectedItemBrush"] = new SolidColorBrush(ListItemSelectedInactiveBackground);
            dict["InactiveSelectedItemTextBrush"] = new SolidColorBrush(TextColor);
            dict["HoverBackgroundBrush"] = new SolidColorBrush(ListItemHoverBackground);
            
            return dict;
        }
    }

    public class CustomThemeManager
    {
        private static readonly string ThemesFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Universa",
            "custom_themes.json"
        );

        private static List<ThemeDefinition> _customThemes;

        public static List<ThemeDefinition> CustomThemes
        {
            get
            {
                if (_customThemes == null)
                {
                    LoadCustomThemes();
                }
                return _customThemes;
            }
        }

        private static void LoadCustomThemes()
        {
            try
            {
                if (File.Exists(ThemesFilePath))
                {
                    var json = File.ReadAllText(ThemesFilePath);
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                    };
                    _customThemes = System.Text.Json.JsonSerializer.Deserialize<List<ThemeDefinition>>(json, options);
                }
                else
                {
                    _customThemes = new List<ThemeDefinition>();
                }
            }
            catch
            {
                _customThemes = new List<ThemeDefinition>();
            }
        }

        public static void SaveCustomTheme(ThemeDefinition theme)
        {
            if (_customThemes == null)
            {
                LoadCustomThemes();
            }

            var existing = _customThemes.FirstOrDefault(t => t.Name == theme.Name);
            if (existing != null)
            {
                _customThemes.Remove(existing);
            }
            _customThemes.Add(theme);

            var directory = Path.GetDirectoryName(ThemesFilePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            };
            var json = System.Text.Json.JsonSerializer.Serialize(_customThemes, options);
            File.WriteAllText(ThemesFilePath, json);
        }

        public static void DeleteCustomTheme(string themeName)
        {
            if (_customThemes == null)
            {
                LoadCustomThemes();
            }

            var theme = _customThemes.FirstOrDefault(t => t.Name == themeName);
            if (theme != null)
            {
                _customThemes.Remove(theme);
                var options = new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
                };
                var json = System.Text.Json.JsonSerializer.Serialize(_customThemes, options);
                File.WriteAllText(ThemesFilePath, json);
            }
        }
    }
} 