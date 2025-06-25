using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    /// <summary>
    /// Converts org-mode state strings to configured colors
    /// </summary>
    public class OrgStateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return new SolidColorBrush(Colors.Gray);

            string stateName;
            
            // Handle both OrgState enum and string values
            if (value is OrgState state)
            {
                stateName = state == OrgState.None ? "" : state.ToString();
            }
            else
            {
                stateName = value.ToString();
            }

            // Get configured color for this state
            var colorHex = ConfigurationProvider.Instance.GetStateColor(stateName);
            
            try
            {
                // Convert hex color to brush
                var color = (Color)ColorConverter.ConvertFromString(colorHex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Fall back to default gray if color parsing fails
                return new SolidColorBrush(Colors.Gray);
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 