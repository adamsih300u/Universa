using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows;

namespace Universa.Desktop.Views
{
    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isUserMessage = (bool)value;
            bool isDarkMode = Application.Current.Resources["WindowBackgroundBrush"] is SolidColorBrush bgBrush && 
                            IsDarkColor(bgBrush.Color);
            
            if (isUserMessage)
            {
                // User message - use accent color
                if (Application.Current.Resources["BorderBrush"] is SolidColorBrush borderBrush)
                {
                    var color = borderBrush.Color;
                    return new SolidColorBrush(isDarkMode 
                        ? Color.FromArgb(255, (byte)(color.R * 0.8), (byte)(color.G * 0.8), (byte)(color.B * 0.8))
                        : Color.FromArgb(230, color.R, color.G, color.B));
                }
            }
            
            // AI message
            if (Application.Current.Resources["WindowBackgroundBrush"] is SolidColorBrush windowBrush)
            {
                var baseColor = windowBrush.Color;
                if (isDarkMode)
                {
                    // In dark mode, make the bubble slightly lighter than the window background
                    return new SolidColorBrush(Color.FromArgb(255, 
                        (byte)Math.Min(baseColor.R + 30, 255),
                        (byte)Math.Min(baseColor.G + 30, 255),
                        (byte)Math.Min(baseColor.B + 30, 255)));
                }
                else
                {
                    // In light mode, make the bubble slightly darker than the window background
                    return new SolidColorBrush(Color.FromArgb(255,
                        (byte)Math.Max(baseColor.R - 30, 0),
                        (byte)Math.Max(baseColor.G - 30, 0),
                        (byte)Math.Max(baseColor.B - 30, 0)));
                }
            }

            // Fallback colors
            return isUserMessage 
                ? new SolidColorBrush(isDarkMode ? Color.FromRgb(60, 60, 60) : Color.FromRgb(200, 200, 200))
                : new SolidColorBrush(isDarkMode ? Color.FromRgb(45, 45, 45) : Color.FromRgb(240, 240, 240));
        }

        private bool IsDarkColor(Color color)
        {
            // Calculate relative luminance
            double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
            return luminance < 0.5;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 