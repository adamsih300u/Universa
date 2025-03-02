using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Universa.Desktop.Windows;

namespace Universa.Desktop
{
    public class TabWidthConverter : MarkupExtension, IValueConverter
    {
        private static TabWidthConverter _instance;
        private const double PADDING = 36; // 8px left + 8px right + 16px close button + 4px close button margin
        private const double EXTRA_PADDING_PER_CHAR = 1.0; // Extra padding per character for longer titles
        private const double BOLD_PADDING = 4; // Extra padding for bold text
        private const double LONG_TITLE_THRESHOLD = 15; // Characters before considering a title "long"
        private const double EXTRA_CLOSE_PADDING = 4; // Additional padding for close button on long titles

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            return _instance ?? (_instance = new TabWidthConverter());
        }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double width && Application.Current.MainWindow is BaseMainWindow mainWindow)
            {
                var tabControl = mainWindow.TabControlInstance;
                if (tabControl == null || tabControl.Items.Count == 0) return 200;

                double availableWidth = width - 20; // Account for container margins
                int tabCount = tabControl.Items.Count;

                // Calculate natural width for each tab
                double totalNaturalWidth = 0;
                foreach (TabItem tab in tabControl.Items)
                {
                    if (tab.Header is TextBlock headerBlock)
                    {
                        var text = headerBlock.Text ?? string.Empty;
                        var formattedText = new FormattedText(
                            text,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface(headerBlock.FontFamily, headerBlock.FontStyle, headerBlock.FontWeight, headerBlock.FontStretch),
                            headerBlock.FontSize > 0 ? headerBlock.FontSize : 12,
                            headerBlock.Foreground ?? Brushes.Black,
                            VisualTreeHelper.GetDpi(headerBlock).PixelsPerDip);

                        double naturalWidth = formattedText.Width + PADDING;
                        
                        // Add extra padding for longer titles
                        if (text.Length > LONG_TITLE_THRESHOLD)
                        {
                            naturalWidth += text.Length * EXTRA_PADDING_PER_CHAR;
                            naturalWidth += EXTRA_CLOSE_PADDING; // Extra space for close button on long titles
                        }

                        // Add extra padding for bold text
                        if (headerBlock.FontWeight == FontWeights.Bold)
                        {
                            naturalWidth += BOLD_PADDING;
                        }

                        totalNaturalWidth += naturalWidth;
                    }
                    else if (tab.Header != null)
                    {
                        totalNaturalWidth += 120; // Default width for non-TextBlock headers
                    }
                }

                // If we have enough space, use natural width
                if (totalNaturalWidth <= availableWidth)
                {
                    return totalNaturalWidth / tabCount;
                }

                // Otherwise, distribute available space evenly
                double widthPerTab = availableWidth / tabCount;
                return Math.Max(80, widthPerTab); // Minimum width of 80px
            }
            return 200;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 