using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop
{
    public class BoolToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool isExpanded && isExpanded ? 90 : 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 