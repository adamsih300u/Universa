using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop
{
    public static class ControlExtensions
    {
        public static ScrollViewer GetScrollViewer(this ListBox listBox)
        {
            if (VisualTreeHelper.GetChildrenCount(listBox) == 0)
                return null;

            var child = VisualTreeHelper.GetChild(listBox, 0);
            if (child == null)
                return null;

            var scrollViewer = child as ScrollViewer;
            if (scrollViewer != null)
                return scrollViewer;

            var border = child as Border;
            if (border != null && VisualTreeHelper.GetChildrenCount(border) > 0)
            {
                return VisualTreeHelper.GetChild(border, 0) as ScrollViewer;
            }

            return null;
        }
    }
} 