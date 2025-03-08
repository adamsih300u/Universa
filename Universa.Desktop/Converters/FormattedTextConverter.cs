using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Documents;

namespace Universa.Desktop.Converters
{
    /// <summary>
    /// Converts a string to a FlowDocument with formatted code blocks
    /// </summary>
    public class FormattedTextConverter : IValueConverter
    {
        /// <summary>
        /// Converts a string to a FlowDocument with formatted code blocks
        /// </summary>
        /// <param name="value">The string to convert</param>
        /// <param name="targetType">The target type</param>
        /// <param name="parameter">The converter parameter</param>
        /// <param name="culture">The culture</param>
        /// <returns>A FlowDocument with formatted code blocks</returns>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string content)
            {
                var document = new FlowDocument();
                CodeBlockFormatter.FormatContent(content, document);
                return document;
            }
            
            return new FlowDocument();
        }

        /// <summary>
        /// Not implemented
        /// </summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 