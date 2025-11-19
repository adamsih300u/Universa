using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class BoolToIconConverter : IValueConverter
    {
        public string TrueValue { get; set; } = "✓";
        public string FalseValue { get; set; } = "✗";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 