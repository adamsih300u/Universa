using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class PriceChangeColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is decimal decimalValue)
            {
                if (decimalValue > 0)
                {
                    // Use theme resource for positive values
                    return System.Windows.Application.Current.Resources["PositiveChangeBrush"] as SolidColorBrush 
                        ?? new SolidColorBrush(Colors.Green);
                }
                else if (decimalValue < 0)
                {
                    // Use theme resource for negative values
                    return System.Windows.Application.Current.Resources["NegativeChangeBrush"] as SolidColorBrush 
                        ?? new SolidColorBrush(Colors.Red);
                }
            }
            
            // Default color for zero or non-decimal values - use theme resource
            return System.Windows.Application.Current.Resources["NeutralChangeBrush"] as SolidColorBrush 
                ?? new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 