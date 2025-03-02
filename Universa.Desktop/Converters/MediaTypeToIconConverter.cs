using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    public class MediaTypeToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is MediaItemType mediaType)
            {
                string iconPath = mediaType switch
                {
                    MediaItemType.MovieLibrary => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/movie-library.png",
                    MediaItemType.TVLibrary => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/tv-library.png",
                    MediaItemType.Movie => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/movie.png",
                    MediaItemType.Series => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/tv-series.png",
                    MediaItemType.Season => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/tv-season.png",
                    MediaItemType.Episode => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/tv-episode.png",
                    MediaItemType.MusicLibrary => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/music-library.png",
                    MediaItemType.Album => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/album.png",
                    MediaItemType.Song => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/song.png",
                    MediaItemType.Folder => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/folder.png",
                    MediaItemType.Library => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/library.png",
                    _ => "pack://application:,,,/Universa.Desktop;component/Resources/Icons/default.png"
                };

                try
                {
                    return new BitmapImage(new Uri(iconPath));
                }
                catch (Exception)
                {
                    // If icon loading fails, return null instead of crashing
                    return null;
                }
            }

            return null;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 