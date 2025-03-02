using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    public class MediaTypeToEpisodeVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MediaItemType mediaType)
            {
                return mediaType == MediaItemType.Episode ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 