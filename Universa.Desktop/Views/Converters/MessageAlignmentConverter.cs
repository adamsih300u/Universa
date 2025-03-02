using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Universa.Desktop.Views
{
    public class MessageAlignmentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool isUserMessage = (bool)value;
            return isUserMessage ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 