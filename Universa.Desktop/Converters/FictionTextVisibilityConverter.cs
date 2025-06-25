using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class FictionTextVisibilityConverter : IValueConverter
    {
        private static readonly Regex _originalTextPattern = new Regex(
            @"```\s*\r?\nOriginal text:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nChanged to:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
        private static readonly Regex _insertionPattern = new Regex(
            @"```\s*\r?\nInsert after:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nNew text:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string content && !string.IsNullOrEmpty(content))
            {
                bool hasFictionText = _originalTextPattern.IsMatch(content) || _insertionPattern.IsMatch(content);
                
                // If parameter is "Inverse", return opposite visibility
                bool inverse = parameter?.ToString() == "Inverse";
                
                if (inverse)
                {
                    return hasFictionText ? Visibility.Collapsed : Visibility.Visible;
                }
                else
                {
                    return hasFictionText ? Visibility.Visible : Visibility.Collapsed;
                }
            }
            
            // Default to visible for regular text if parameter is "Inverse"
            bool isInverse = parameter?.ToString() == "Inverse";
            return isInverse ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 