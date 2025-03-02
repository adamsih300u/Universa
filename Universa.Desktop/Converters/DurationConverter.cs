using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class DurationConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimeSpan duration)
            {
                if (duration.TotalHours >= 1)
                {
                    return $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}";
                }
                return $"{duration.Minutes:D2}:{duration.Seconds:D2}";
            }
            else if (value is string durationString)
            {
                if (TimeSpan.TryParse(durationString, out TimeSpan parsedDuration))
                {
                    if (parsedDuration.TotalHours >= 1)
                    {
                        return $"{(int)parsedDuration.TotalHours}:{parsedDuration.Minutes:D2}:{parsedDuration.Seconds:D2}";
                    }
                    return $"{parsedDuration.Minutes:D2}:{parsedDuration.Seconds:D2}";
                }
            }

            return "--:--";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 