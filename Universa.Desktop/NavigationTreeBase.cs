using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Universa.Desktop
{
    public class NavigationTreeBase : TreeView
    {
        private Point _startPoint;
        private bool _isDragging = false;
        private TreeViewItem _draggedItem = null;

        public NavigationTreeBase()
        {
            AllowDrop = true;
            PreviewMouseLeftButtonDown += NavigationTree_PreviewMouseLeftButtonDown;
            PreviewMouseMove += NavigationTree_PreviewMouseMove;
            Drop += NavigationTree_Drop;
        }

        private void NavigationTree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _startPoint = e.GetPosition(null);
        }

        private void NavigationTree_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);

                if (Math.Abs(position.X - _startPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _startPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var treeViewItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                    if (treeViewItem != null)
                    {
                        _draggedItem = treeViewItem;
                        _isDragging = true;
                        DragDrop.DoDragDrop(this, treeViewItem, DragDropEffects.Move);
                        _isDragging = false;
                        _draggedItem = null;
                    }
                }
            }
        }

        private void NavigationTree_Drop(object sender, DragEventArgs e)
        {
            if (_draggedItem != null)
            {
                var targetItem = FindAncestor<TreeViewItem>((DependencyObject)e.OriginalSource);
                if (targetItem != null && targetItem != _draggedItem)
                {
                    // Get the indices
                    int sourceIndex = Items.IndexOf(_draggedItem);
                    int targetIndex = Items.IndexOf(targetItem);

                    // Only allow reordering at the root level
                    if (sourceIndex != -1 && targetIndex != -1)
                    {
                        // Remove from old position and insert at new position
                        Items.RemoveAt(sourceIndex);
                        Items.Insert(targetIndex, _draggedItem);

                        // Notify that the order has changed
                        OnNavigationOrderChanged();
                    }
                }
            }
        }

        private T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            do
            {
                if (current is T)
                {
                    return (T)current;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

        protected virtual void OnNavigationOrderChanged()
        {
            // Override this in derived classes to save the new order
        }
    }
} 