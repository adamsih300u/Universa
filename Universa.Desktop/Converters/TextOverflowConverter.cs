using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class TextOverflowState
    {
        public bool IsOverflowing { get; set; }
        public double ScrollOffset { get; set; }
    }

    public class TextOverflowConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string text && !string.IsNullOrEmpty(text))
            {
                var textBlock = parameter as TextBlock;
                if (textBlock != null)
                {
                    // Get the parent ScrollViewer
                    var scrollViewer = textBlock.Parent as ScrollViewer;
                    if (scrollViewer != null)
                    {
                        // Measure the actual text width
                        var formattedText = new FormattedText(
                            text,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(textBlock.FontFamily, textBlock.FontStyle, textBlock.FontWeight, textBlock.FontStretch),
                            textBlock.FontSize,
                            Brushes.Black,
                            VisualTreeHelper.GetDpi(textBlock).PixelsPerDip);

                        var textWidth = formattedText.Width;
                        var containerWidth = scrollViewer.ActualWidth;

                        return new TextOverflowState 
                        {
                            IsOverflowing = textWidth > containerWidth,
                            ScrollOffset = -(textWidth - containerWidth)
                        };
                    }
                }
            }
            return new TextOverflowState { IsOverflowing = false, ScrollOffset = 0 };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 