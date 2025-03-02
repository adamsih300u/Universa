using System;
using System.Windows;
using System.Windows.Data;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Converters
{
    public class CodeBlockVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string content)
            {
                // Use regex to match code blocks with flexible whitespace
                var codeBlockPattern = @"```\s*\r?\nOriginal code:.*?```.*?```\s*\r?\nChanged to:.*?```";
                var hasCodeBlocks = Regex.IsMatch(content, codeBlockPattern, RegexOptions.Singleline);
                
                return hasCodeBlocks ? Visibility.Visible : Visibility.Collapsed;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 