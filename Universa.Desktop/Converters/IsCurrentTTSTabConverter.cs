using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

namespace Universa.Desktop.Converters
{
    public class IsCurrentTTSTabConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || Application.Current.MainWindow is not MainWindow mainWindow)
                return false;

            var field = typeof(MainWindow).GetField("_currentTTSTab", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field == null)
                return false;

            var currentTTSTab = field.GetValue(mainWindow);
            var field2 = typeof(MainWindow).GetField("_isTTSPlaying",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var isTTSPlaying = field2?.GetValue(mainWindow) as bool? ?? false;

            return value == currentTTSTab && isTTSPlaying;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 