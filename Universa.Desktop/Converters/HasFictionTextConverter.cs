using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace Universa.Desktop.Converters
{
    public class HasFictionTextConverter : IValueConverter
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
                return _originalTextPattern.IsMatch(content) || _insertionPattern.IsMatch(content);
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 