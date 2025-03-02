using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class TimeSpanToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime dateTime)
            {
                var timeSpan = DateTime.Now - dateTime;
                if (timeSpan.TotalDays >= 1)
                {
                    return $"{(int)timeSpan.TotalDays}d ago";
                }
                if (timeSpan.TotalHours >= 1)
                {
                    return $"{(int)timeSpan.TotalHours}h ago";
                }
                if (timeSpan.TotalMinutes >= 1)
                {
                    return $"{(int)timeSpan.TotalMinutes}m ago";
                }
                return "Just now";
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 