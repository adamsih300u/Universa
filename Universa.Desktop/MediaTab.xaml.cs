using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Runtime.CompilerServices;
using Universa.Desktop.Properties;
using Universa.Desktop.Converters;
using Universa.Desktop.Cache;
using Universa.Desktop.Controls;
using Universa.Desktop.Models;
using BooleanToVisibilityConverter = System.Windows.Controls.BooleanToVisibilityConverter;
using System.Threading;
using System.Net.Http;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Services;

namespace Universa.Desktop
{
    public partial class MediaTab : UserControl
    {
        private readonly MediaTabViewModel _viewModel;
        private DateTime _lastClickTime = DateTime.MinValue;
        private const double DOUBLE_CLICK_THRESHOLD = 500; // milliseconds

        public MediaTab(JellyfinService jellyfinService)
        {
            InitializeComponent();
            _viewModel = new MediaTabViewModel(jellyfinService);
            DataContext = _viewModel;
        }

        private void MediaTabNavigationTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is MediaItem selectedItem)
            {
                _viewModel.NavigateToItemCommand.Execute(selectedItem);
            }
        }

        private void TreeViewItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem treeViewItem && 
                treeViewItem.DataContext is MediaItem item &&
                (item.Type == MediaItemType.Series || item.Type == MediaItemType.Season))
            {
                // Toggle expansion state
                treeViewItem.IsExpanded = !treeViewItem.IsExpanded;

                if (treeViewItem.IsExpanded)
                {
                    _viewModel.ExpandItemCommand.Execute(item);
                }

                // Mark the event as handled to prevent it from bubbling
                e.Handled = true;
            }
        }

        private void MediaTabContentListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (MediaTabContentListView.SelectedItem is MediaItem selectedItem)
            {
                if (selectedItem.Type == MediaItemType.Movie || 
                    selectedItem.Type == MediaItemType.Episode)
                {
                    _viewModel.PlayItemCommand.Execute(selectedItem);
                }
            }
        }

        private void MediaTabContentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MediaTabContentListView.SelectedItem is MediaItem selectedItem)
            {
                if (selectedItem.Type != MediaItemType.Movie && 
                    selectedItem.Type != MediaItemType.Episode)
                {
                    _viewModel.NavigateToItemCommand.Execute(selectedItem);
                }
            }
        }
    }
} 