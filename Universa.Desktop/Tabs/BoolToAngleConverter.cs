using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Tabs
{
    public class BoolToAngleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isExpanded)
            {
                return isExpanded ? 180 : 0;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double angle)
            {
                return Math.Abs(angle) > 90;
            }
            
            return false;
        }
    }
} 