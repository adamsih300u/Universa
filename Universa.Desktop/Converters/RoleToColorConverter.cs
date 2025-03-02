using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Universa.Desktop
{
    public class RoleToColorConverter : IValueConverter
    {
        public string UserColor { get; set; } = "#569CD6";
        public string AssistantColor { get; set; } = "#4EC9B0";

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string role)
            {
                var colorStr = role.ToLower() == "user" ? UserColor : AssistantColor;
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorStr));
            }
            return new SolidColorBrush(Colors.Transparent);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 