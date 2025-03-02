using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Universa.Desktop.Views;

namespace Universa.Desktop.Converters
{
    public class NavigatorVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TabItem tabItem)
            {
                // Hide library navigator for tabs with specialized navigators
                if (tabItem.Content is MusicTab || 
                    tabItem.Content is MediaTab || 
                    tabItem.Content is ChatTab)
                {
                    return Visibility.Collapsed;
                }
                
                // Show library navigator for all other tabs
                return Visibility.Visible;
            }
            
            // Show library navigator by default
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 