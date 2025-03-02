using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Windows;

namespace Universa.Desktop.Converters
{
    public class NullImageConverter : IValueConverter
    {
        private static readonly ConcurrentDictionary<string, WeakReference<BitmapImage>> _imageCache = 
            new ConcurrentDictionary<string, WeakReference<BitmapImage>>();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || string.IsNullOrEmpty(value.ToString()))
                return null;

            string imageUrl = value.ToString();

            // Try to get from cache first
            if (_imageCache.TryGetValue(imageUrl, out WeakReference<BitmapImage> weakRef))
            {
                if (weakRef.TryGetTarget(out BitmapImage cachedImage))
                {
                    return cachedImage;
                }
                // Remove from cache if the reference is dead
                _imageCache.TryRemove(imageUrl, out _);
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                image.DecodePixelWidth = 150;
                image.UriSource = new Uri(imageUrl);
                
                // Use a low priority to prevent UI blocking
                image.DecodeFailed += (s, e) => 
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to decode image: {e.ErrorException.Message}");
                };
                
                image.EndInit();

                if (image.CanFreeze)
                {
                    image.Freeze();
                }

                _imageCache.TryAdd(imageUrl, new WeakReference<BitmapImage>(image));
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 