using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class GreaterThanZeroConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return false;

            if (value is int intValue)
                return intValue > 0;

            if (value is double doubleValue)
                return doubleValue > 0;

            if (value is long longValue)
                return longValue > 0;

            if (int.TryParse(value.ToString(), out int parsedValue))
                return parsedValue > 0;

            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 