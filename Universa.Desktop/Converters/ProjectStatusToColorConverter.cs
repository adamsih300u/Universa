using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class ProjectStatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is ProjectStatus status)
            {
                return status switch
                {
                    ProjectStatus.NotStarted => new SolidColorBrush(Color.FromRgb(128, 128, 128)),  // Grey
                    ProjectStatus.NotReady => new SolidColorBrush(Color.FromRgb(220, 53, 69)),      // Red
                    ProjectStatus.Started => new SolidColorBrush(Color.FromRgb(40, 167, 69)),       // Green
                    ProjectStatus.Deferred => new SolidColorBrush(Color.FromRgb(255, 193, 7)),      // Yellow
                    ProjectStatus.Completed => new SolidColorBrush(Color.FromRgb(0, 123, 255)),     // Blue
                    _ => new SolidColorBrush(Colors.Gray)
                };
            }
            return new SolidColorBrush(Colors.Gray);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 