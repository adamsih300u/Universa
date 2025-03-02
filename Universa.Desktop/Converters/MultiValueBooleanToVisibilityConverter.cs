using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    public class MultiValueBooleanToVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return Visibility.Collapsed;

            // First value should be the ShowCompleted boolean
            bool showCompleted = values[0] is bool && (bool)values[0];

            // Second value could be either a boolean (IsCompleted) or ProjectStatus
            bool isCompleted;
            if (values[1] is bool)
            {
                isCompleted = (bool)values[1];
            }
            else if (values[1] is ProjectStatus status)
            {
                isCompleted = status == ProjectStatus.Completed;
            }
            else
            {
                return Visibility.Collapsed;
            }

            return (showCompleted && isCompleted) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 