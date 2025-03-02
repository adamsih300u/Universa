using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class MessageBackgroundConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isUser)
            {
                return isUser 
                    ? new SolidColorBrush(Color.FromRgb(240, 240, 240))  // Light gray for user messages
                    : new SolidColorBrush(Color.FromRgb(220, 248, 198)); // Light green for AI messages
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 