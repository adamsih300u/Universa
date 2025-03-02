using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Controls
{
    public class MetadataValueConverter : IValueConverter
    {
        public string Key { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Dictionary<string, string> metadata && metadata.TryGetValue(Key, out string val))
            {
                if (Key == "Duration" && !string.IsNullOrEmpty(val))
                {
                    // Convert duration to a readable format (assuming duration is in minutes)
                    if (int.TryParse(val, out int minutes))
                    {
                        return $"{minutes / 60}:{minutes % 60:D2}";
                    }
                }
                return val;
            }
            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 