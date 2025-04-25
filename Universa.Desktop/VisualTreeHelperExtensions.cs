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

        /// <summary>
        /// Finds a child of the specified type in the visual tree.
        /// </summary>
        /// <typeparam name="T">The type of the child to find.</typeparam>
        /// <param name="parent">The parent object to search within.</param>
        /// <returns>The first child of the specified type, or null if no child found.</returns>
        public static T FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T result)
                    return result;
                
                var descendant = FindVisualChild<T>(child);
                if (descendant != null)
                    return descendant;
            }
            
            return null;
        }
    }
} 