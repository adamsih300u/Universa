using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Universa.Desktop
{
    public static class VisualTreeHelperExtensions
    {
        public static T GetVisualDescendant<T>(this DependencyObject parent) where T : Visual
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var descendant = GetVisualDescendant<T>(child);
                if (descendant != null) return descendant;
            }

            return null;
        }

        public static IEnumerable<T> GetVisualChildren<T>(this DependencyObject parent) where T : Visual
        {
            if (parent == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) yield return typed;
            }
        }
    }
} 