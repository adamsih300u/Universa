using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isPlayed)
            {
                // Unplayed items (false) are bold
                return isPlayed ? FontWeights.Normal : FontWeights.Bold;
            }
            return FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 