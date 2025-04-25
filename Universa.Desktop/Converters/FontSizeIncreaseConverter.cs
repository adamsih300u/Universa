using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    /// <summary>
    /// Increases a font size by a specified parameter value
    /// </summary>
    public class FontSizeIncreaseConverter : IValueConverter
    {
        /// <summary>
        /// Increases the font size by the parameter amount
        /// </summary>
        /// <param name="value">The current font size</param>
        /// <param name="targetType">The target type</param>
        /// <param name="parameter">Amount to increase (defaults to 2 if not specified)</param>
        /// <param name="culture">The culture</param>
        /// <returns>The increased font size</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double currentSize)
            {
                double increase = 2; // Default increase

                if (parameter != null)
                {
                    if (parameter is double doubleParam)
                    {
                        increase = doubleParam;
                    }
                    else if (parameter is int intParam)
                    {
                        increase = intParam;
                    }
                    else if (double.TryParse(parameter.ToString(), out double parsedValue))
                    {
                        increase = parsedValue;
                    }
                }

                return currentSize + increase;
            }

            return value;
        }

        /// <summary>
        /// Not implemented
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 