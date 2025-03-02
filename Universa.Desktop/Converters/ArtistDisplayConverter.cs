using System;
using System.Globalization;
using System.Windows.Data;

namespace Universa.Desktop.Views
{
    public class ArtistDisplayConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string albumArtist && !string.IsNullOrEmpty(albumArtist))
            {
                var artist = (parameter as string) ?? "";
                
                // If the track artist is different from the album artist, show both
                if (!string.IsNullOrEmpty(artist) && artist != albumArtist)
                {
                    return $" (from {albumArtist})";
                }
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 