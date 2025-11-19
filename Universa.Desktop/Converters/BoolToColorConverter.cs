using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object TrueValue { get; set; } = "#FF0000"; // Red
        public object FalseValue { get; set; } = "#00FF00"; // Green

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                var selectedValue = boolValue ? TrueValue : FalseValue;
                
                // If it's already a brush, return it
                if (selectedValue is Brush brush)
                    return brush;
                
                // If it's a string color, convert it to a brush
                if (selectedValue is string colorStr)
                {
                    try
                    {
                        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
                    }
                    catch
                    {
                        // Fallback to transparent if color parsing fails
                        return new SolidColorBrush(Colors.Transparent);
                    }
                }
                
                return selectedValue;
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 