using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class ChatColumnWidthConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length != 2 || !(values[0] is Visibility visibility) || !(values[1] is double totalWidth))
                return new GridLength(0);

            if (visibility == Visibility.Collapsed)
                return new GridLength(0);

            // Calculate chat width as 25% of window width, with min and max constraints
            double chatWidth = Math.Min(Math.Max(totalWidth * 0.25, 250), 400);
            return new GridLength(chatWidth);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 