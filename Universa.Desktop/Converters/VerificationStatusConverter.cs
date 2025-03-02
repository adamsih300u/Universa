using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class VerificationStatusConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isVerified)
            {
                return isVerified ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) // Green
                                : new SolidColorBrush(Color.FromRgb(244, 67, 54));  // Red
            }
            return new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray for unknown state
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 