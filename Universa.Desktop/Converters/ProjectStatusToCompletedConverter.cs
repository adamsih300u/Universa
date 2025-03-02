using System;
using System.Globalization;
using System.Windows.Data;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    public class ProjectStatusToCompletedConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProjectStatus status)
            {
                return status == ProjectStatus.Completed;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 