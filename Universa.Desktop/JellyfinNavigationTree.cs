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
using Universa.Desktop.Properties;

namespace Universa.Desktop
{
    public class JellyfinNavigationTree : NavigationTreeBase
    {
        protected override void OnNavigationOrderChanged()
        {
            var order = new List<string>();
            foreach (TreeViewItem item in Items)
            {
                if (item.DataContext is MediaItem mediaItem)
                {
                    order.Add(mediaItem.Id);
                }
            }
            Properties.Settings.Default.JellyfinNavigationOrder = string.Join(",", order);
            Properties.Settings.Default.Save();
        }

        public void LoadSavedOrder()
        {
            var savedOrder = Properties.Settings.Default.JellyfinNavigationOrder;
            if (!string.IsNullOrEmpty(savedOrder))
            {
                var reorderedItems = new List<TreeViewItem>();
                var ids = savedOrder.Split(',');

                // First, add items in the saved order
                foreach (var id in ids)
                {
                    var item = Items.Cast<TreeViewItem>()
                        .FirstOrDefault(i => (i.DataContext as MediaItem)?.Id == id);
                    if (item != null)
                    {
                        reorderedItems.Add(item);
                    }
                }

                // Add any new items that weren't in the saved order
                foreach (TreeViewItem item in Items)
                {
                    if (!reorderedItems.Contains(item))
                    {
                        reorderedItems.Add(item);
                    }
                }

                Items.Clear();
                foreach (var item in reorderedItems)
                {
                    Items.Add(item);
                }
            }
        }
    }
} 