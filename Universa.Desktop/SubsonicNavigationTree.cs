using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using Universa.Desktop.Properties;

namespace Universa.Desktop
{
    public class SubsonicNavigationTree : NavigationTreeBase
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
            Properties.Settings.Default.SubsonicNavigationOrder = string.Join(",", order);
            Properties.Settings.Default.Save();
        }

        public void LoadSavedOrder()
        {
            var savedOrder = Properties.Settings.Default.SubsonicNavigationOrder;
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