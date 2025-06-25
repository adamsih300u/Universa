using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class OrgModeService : IOrgModeService
    {
        private ObservableCollection<OrgItem> _items;
        private string _filePath;
        private Dictionary<string, OrgItem> _itemLookup;

        public event EventHandler<OrgItemChangedEventArgs> ItemChanged;

        public string FilePath => _filePath;
        public ObservableCollection<OrgItem> Items => _items;

        public OrgModeService(string filePath)
        {
            _filePath = filePath;
            _items = new ObservableCollection<OrgItem>();
            _itemLookup = new Dictionary<string, OrgItem>();
        }

        public async Task LoadFromFileAsync(string filePath)
        {
            _filePath = filePath;
            
            if (!File.Exists(filePath))
            {
                // Don't create files automatically - fail gracefully
                System.Diagnostics.Debug.WriteLine($"OrgModeService: File does not exist: {filePath}");
                _items = new ObservableCollection<OrgItem>();
                RebuildLookup();
                
                // Throw an exception to let the caller know the file doesn't exist
                // This prevents empty content from being created
                throw new FileNotFoundException($"Org file not found: {filePath}");
            }

            try
            {
                var content = await File.ReadAllTextAsync(filePath);
                System.Diagnostics.Debug.WriteLine($"OrgModeService: Loaded {content.Length} characters from {filePath}");
                _items = await ParseContentAsync(content);
                RebuildLookup();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeService: Error reading file {filePath}: {ex.Message}");
                // Re-throw to let caller handle the error appropriately
                throw;
            }
        }

        public async Task SaveToFileAsync()
        {
            if (string.IsNullOrEmpty(_filePath))
                throw new InvalidOperationException("No file path specified");
            
            System.Diagnostics.Debug.WriteLine($"OrgModeService.SaveToFileAsync: About to save to {_filePath}");
            await SaveToFileAsync(_filePath);
        }

        public async Task SaveToFileAsync(string filePath)
        {
            System.Diagnostics.Debug.WriteLine($"OrgModeService.SaveToFileAsync: Serializing {_items.Count} items to {filePath}");
            var content = await SerializeContentAsync();
            System.Diagnostics.Debug.WriteLine($"OrgModeService.SaveToFileAsync: Serialized {content.Length} characters");
            
            await File.WriteAllTextAsync(filePath, content);
            System.Diagnostics.Debug.WriteLine($"OrgModeService.SaveToFileAsync: Successfully wrote to file");
            
            _filePath = filePath;
        }

        public async Task<ObservableCollection<OrgItem>> ParseContentAsync(string content)
        {
            var items = new ObservableCollection<OrgItem>();
            var lines = content.Split('\n');
            var stack = new Stack<OrgItem>();
            OrgItem currentItem = null;
            var contentBuilder = new StringBuilder();
            var inPropertiesBlock = false;
            var currentProperties = new Dictionary<string, string>();

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var trimmedLine = line.TrimStart();

                // Skip file-level properties (lines starting with #+)
                if (trimmedLine.StartsWith("#+"))
                    continue;

                // Check for headline
                var headlineMatch = Regex.Match(line, @"^(\*+)\s*(?:(TODO|NEXT|STARTED|WAITING|DEFERRED|DONE|CANCELLED)\s+)?(?:\[#([ABC])\]\s+)?(.*?)(?:\s+(:[a-zA-Z0-9_@#%:]+:))?\s*$");
                
                if (headlineMatch.Success)
                {
                    // Save content from previous item
                    if (currentItem != null)
                    {
                        currentItem.Content = contentBuilder.ToString().Trim();
                        contentBuilder.Clear();
                    }

                    // Parse the headline
                    var level = headlineMatch.Groups[1].Value.Length;
                    var stateStr = headlineMatch.Groups[2].Value;
                    var priorityStr = headlineMatch.Groups[3].Value;
                    var title = headlineMatch.Groups[4].Value.Trim();
                    var tagsStr = headlineMatch.Groups[5].Value;

                    var item = new OrgItem
                    {
                        Level = level,
                        Title = title,
                        FilePath = _filePath
                    };

                    // Parse state
                    if (!string.IsNullOrEmpty(stateStr) && Enum.TryParse<OrgState>(stateStr, out var state))
                    {
                        item.State = state;
                    }

                    // Parse priority
                    if (!string.IsNullOrEmpty(priorityStr) && Enum.TryParse<OrgPriority>(priorityStr, out var priority))
                    {
                        item.Priority = priority;
                    }

                    // Parse tags
                    if (!string.IsNullOrEmpty(tagsStr))
                    {
                        var tags = tagsStr.Trim(':').Split(':')
                            .Where(t => !string.IsNullOrEmpty(t))
                            .ToList();
                        item.Tags = tags;
                    }

                    // Handle hierarchy
                    while (stack.Count > 0 && stack.Peek().Level >= level)
                    {
                        stack.Pop();
                    }

                    if (stack.Count > 0)
                    {
                        stack.Peek().AddChild(item);
                    }
                    else
                    {
                        items.Add(item);
                    }

                    stack.Push(item);
                    currentItem = item;
                    continue;
                }

                // Handle special lines
                if (trimmedLine.StartsWith("CLOSED:") && currentItem != null)
                {
                    var closedMatch = Regex.Match(trimmedLine, @"CLOSED:\s*\[([0-9]{4}-[0-9]{2}-[0-9]{2}[^\]]*)\]");
                    if (closedMatch.Success && DateTime.TryParse(closedMatch.Groups[1].Value, out var closedDate))
                    {
                        currentItem.Closed = closedDate;
                    }
                    continue;
                }

                if (trimmedLine.StartsWith("SCHEDULED:") && currentItem != null)
                {
                    var scheduledMatch = Regex.Match(trimmedLine, @"SCHEDULED:\s*<([0-9]{4}-[0-9]{2}-[0-9]{2}[^>]*)>");
                    if (scheduledMatch.Success && DateTime.TryParse(scheduledMatch.Groups[1].Value, out var scheduledDate))
                    {
                        currentItem.Scheduled = scheduledDate;
                    }
                    continue;
                }

                if (trimmedLine.StartsWith("DEADLINE:") && currentItem != null)
                {
                    var deadlineMatch = Regex.Match(trimmedLine, @"DEADLINE:\s*<([0-9]{4}-[0-9]{2}-[0-9]{2}[^>]*)>");
                    if (deadlineMatch.Success && DateTime.TryParse(deadlineMatch.Groups[1].Value, out var deadlineDate))
                    {
                        currentItem.Deadline = deadlineDate;
                    }
                    continue;
                }

                // Parse standalone timestamps (for Beorg calendar entries)
                // Format: <2025-06-27 Fri> or <2025-06-28 Sat 07:30-15:00>
                if (currentItem != null)
                {
                    var standaloneTimestampMatch = Regex.Match(trimmedLine, @"^\s*<([0-9]{4}-[0-9]{2}-[0-9]{2}[^>]*)>");
                    if (standaloneTimestampMatch.Success)
                    {
                        var timestampText = standaloneTimestampMatch.Groups[1].Value;
                        
                        // Extract date part (before any time information)
                        var datePart = timestampText.Split(' ')[0]; // "2025-06-27"
                        
                        if (DateTime.TryParse(datePart, out var timestampDate))
                        {
                            // For calendar events (State=None), use this as the scheduled date
                            // This gives calendar events proper date ordering
                            if (currentItem.State == OrgState.None)
                            {
                                currentItem.Scheduled = timestampDate;
                            }
                            else
                            {
                                // For TODO items, if no scheduled date is set, use this
                                currentItem.Scheduled ??= timestampDate;
                            }
                        }
                        continue;
                    }
                }

                // Properties drawer
                if (trimmedLine == ":PROPERTIES:")
                {
                    inPropertiesBlock = true;
                    currentProperties.Clear();
                    continue;
                }

                if (trimmedLine == ":END:" && inPropertiesBlock)
                {
                    if (currentItem != null)
                    {
                        currentItem.Properties = new Dictionary<string, string>(currentProperties);
                    }
                    inPropertiesBlock = false;
                    continue;
                }

                if (inPropertiesBlock)
                {
                    var propMatch = Regex.Match(trimmedLine, @":([^:]+):\s*(.*)");
                    if (propMatch.Success)
                    {
                        currentProperties[propMatch.Groups[1].Value.Trim()] = propMatch.Groups[2].Value.Trim();
                    }
                    continue;
                }

                // Regular content
                if (currentItem != null)
                {
                    contentBuilder.AppendLine(line);
                }
            }

            // Save content from last item
            if (currentItem != null)
            {
                currentItem.Content = contentBuilder.ToString().Trim();
            }

            return items;
        }

        public async Task<string> SerializeContentAsync()
        {
            var builder = new StringBuilder();
            
            // Add file-level properties
            builder.AppendLine($"#+TITLE: {Path.GetFileNameWithoutExtension(_filePath)}");
            builder.AppendLine($"#+CREATED: {DateTime.Now:yyyy-MM-dd}");
            builder.AppendLine();

            foreach (var item in _items)
            {
                await SerializeItemRecursive(item, builder);
            }

            return builder.ToString();
        }

        public string GetContent()
        {
            return SerializeContentAsync().Result;
        }

        public async Task<string> SerializeItemAsync(OrgItem item)
        {
            var builder = new StringBuilder();
            await SerializeItemRecursive(item, builder);
            return builder.ToString();
        }

        private async Task SerializeItemRecursive(OrgItem item, StringBuilder builder)
        {
            // Build headline
            var headline = new StringBuilder();
            headline.Append(new string('*', item.Level));
            
            if (item.State != OrgState.None)
            {
                headline.Append($" {item.State}");
            }

            if (item.Priority != OrgPriority.None)
            {
                headline.Append($" [#{item.Priority}]");
            }

            headline.Append($" {item.Title}");

            if (item.Tags.Any())
            {
                headline.Append($" :{string.Join(":", item.Tags)}:");
            }

            builder.AppendLine(headline.ToString());

            // Add CLOSED timestamp if completed
            if (item.Closed.HasValue)
            {
                builder.AppendLine($"  CLOSED: [{item.Closed.Value:yyyy-MM-dd ddd HH:mm}]");
            }

            // Add SCHEDULED if set
            if (item.Scheduled.HasValue)
            {
                builder.AppendLine($"  SCHEDULED: <{item.Scheduled.Value:yyyy-MM-dd ddd}>");
            }

            // Add DEADLINE if set
            if (item.Deadline.HasValue)
            {
                builder.AppendLine($"  DEADLINE: <{item.Deadline.Value:yyyy-MM-dd ddd}>");
            }

            // Add properties if any
            if (item.Properties.Any())
            {
                builder.AppendLine("  :PROPERTIES:");
                foreach (var prop in item.Properties)
                {
                    builder.AppendLine($"  :{prop.Key}: {prop.Value}");
                }
                builder.AppendLine("  :END:");
            }

            // Add content
            if (!string.IsNullOrEmpty(item.Content))
            {
                var contentLines = item.Content.Split('\n');
                foreach (var line in contentLines)
                {
                    builder.AppendLine($"  {line}");
                }
            }

            builder.AppendLine();

            // Serialize children
            foreach (var child in item.Children)
            {
                await SerializeItemRecursive(child, builder);
            }
        }

        public async Task<OrgItem> CreateItemAsync(OrgItem item)
        {
            item.Id = Guid.NewGuid().ToString();
            item.FilePath = _filePath;
            _items.Add(item);
            _itemLookup[item.Id] = item;
            NotifyItemChanged(item.Id, OrgItemChangeType.Created, item);
            return item;
        }

        public async Task UpdateItemAsync(OrgItem item)
        {
            if (_itemLookup.ContainsKey(item.Id))
            {
                _itemLookup[item.Id] = item;
                NotifyItemChanged(item.Id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task DeleteItemAsync(string id)
        {
            System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Attempting to delete item with ID: {id}");
            
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Found item '{item.Title}' to delete");
                
                if (item.Parent != null)
                {
                    System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Removing item from parent '{item.Parent.Title}'");
                    item.Parent.RemoveChild(item);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Removing item from root collection");
                    _items.Remove(item);
                    System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Root collection now has {_items.Count} items");
                }
                
                _itemLookup.Remove(id);
                System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Removed from lookup, now tracking {_itemLookup.Count} items");
                
                NotifyItemChanged(id, OrgItemChangeType.Deleted, item);
                System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Notified of item deletion");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"OrgModeService.DeleteItemAsync: Item with ID {id} not found!");
            }
        }

        public async Task<OrgItem> GetItemByIdAsync(string id)
        {
            return _itemLookup.TryGetValue(id, out var item) ? item : null;
        }

        public async Task<IEnumerable<OrgItem>> GetAllItemsAsync()
        {
            var allItems = new List<OrgItem>();
            foreach (var item in _items)
            {
                allItems.Add(item);
                allItems.AddRange(item.GetAllDescendants());
            }
            return allItems;
        }

        public async Task<IEnumerable<OrgItem>> GetItemsByStateAsync(OrgState state)
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(i => i.State == state);
        }

        public async Task<IEnumerable<OrgItem>> GetItemsByTagAsync(string tag)
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(i => i.Tags.Contains(tag));
        }

        public async Task<IEnumerable<OrgItem>> GetItemsByPriorityAsync(OrgPriority priority)
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(i => i.Priority == priority);
        }

        public async Task<IEnumerable<OrgItem>> GetOverdueItemsAsync()
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(i => i.IsOverdue);
        }

        public async Task<IEnumerable<OrgItem>> GetScheduledItemsAsync(DateTime? date = null)
        {
            var allItems = await GetAllItemsAsync();
            var targetDate = date ?? DateTime.Today;
            return allItems.Where(i => i.Scheduled?.Date == targetDate);
        }

        public async Task<IEnumerable<OrgItem>> GetDeadlineItemsAsync(DateTime? date = null)
        {
            var allItems = await GetAllItemsAsync();
            var targetDate = date ?? DateTime.Today;
            return allItems.Where(i => i.Deadline?.Date == targetDate);
        }

        public async Task CycleStateAsync(string id)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.CycleState();
                NotifyItemChanged(id, OrgItemChangeType.StateChanged, item);
            }
        }

        public async Task SetStateAsync(string id, OrgState state)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.State = state;
                NotifyItemChanged(id, OrgItemChangeType.StateChanged, item);
            }
        }

        public async Task CyclePriorityAsync(string id)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.CyclePriority();
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task SetPriorityAsync(string id, OrgPriority priority)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.Priority = priority;
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task SetScheduledAsync(string id, DateTime? scheduled)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.Scheduled = scheduled;
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task SetDeadlineAsync(string id, DateTime? deadline)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.Deadline = deadline;
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task AddChildAsync(string parentId, OrgItem child)
        {
            var parent = await GetItemByIdAsync(parentId);
            if (parent != null)
            {
                child.Id = Guid.NewGuid().ToString();
                child.FilePath = _filePath;
                parent.AddChild(child);
                _itemLookup[child.Id] = child;
                NotifyItemChanged(child.Id, OrgItemChangeType.Created, child);
            }
        }

        public async Task RemoveChildAsync(string parentId, string childId)
        {
            var parent = await GetItemByIdAsync(parentId);
            var child = await GetItemByIdAsync(childId);
            if (parent != null && child != null)
            {
                parent.RemoveChild(child);
                _itemLookup.Remove(childId);
                NotifyItemChanged(childId, OrgItemChangeType.Deleted, child);
            }
        }

        public async Task MoveItemAsync(string itemId, string newParentId)
        {
            var item = await GetItemByIdAsync(itemId);
            var newParent = await GetItemByIdAsync(newParentId);
            
            if (item != null)
            {
                // Remove from current parent
                if (item.Parent != null)
                {
                    item.Parent.RemoveChild(item);
                }
                else
                {
                    _items.Remove(item);
                }

                // Add to new parent
                if (newParent != null)
                {
                    newParent.AddChild(item);
                }
                else
                {
                    item.Parent = null;
                    item.Level = 1;
                    _items.Add(item);
                }

                NotifyItemChanged(itemId, OrgItemChangeType.Moved, item);
            }
        }

        public async Task PromoteItemAsync(string id)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null && item.Level > 1)
            {
                item.Level--;
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task DemoteItemAsync(string id)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.Level++;
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task AddTagAsync(string id, string tag)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.AddTag(tag);
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task RemoveTagAsync(string id, string tag)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.RemoveTag(tag);
                NotifyItemChanged(id, OrgItemChangeType.Modified, item);
            }
        }

        public async Task<IEnumerable<string>> GetAllTagsAsync()
        {
            var allItems = await GetAllItemsAsync();
            return allItems.SelectMany(i => i.Tags).Distinct().OrderBy(t => t);
        }

        public async Task SetPropertyAsync(string id, string key, string value)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null)
            {
                item.SetProperty(key, value);
                NotifyItemChanged(id, OrgItemChangeType.PropertyChanged, item);
            }
        }

        public async Task<string> GetPropertyAsync(string id, string key)
        {
            var item = await GetItemByIdAsync(id);
            return item?.GetProperty(key);
        }

        public async Task RemovePropertyAsync(string id, string key)
        {
            var item = await GetItemByIdAsync(id);
            if (item != null && item.Properties.ContainsKey(key))
            {
                item.Properties.Remove(key);
                NotifyItemChanged(id, OrgItemChangeType.PropertyChanged, item);
            }
        }

        public async Task<IEnumerable<OrgItem>> SearchAsync(string query)
        {
            var allItems = await GetAllItemsAsync();
            var lowerQuery = query.ToLower();
            return allItems.Where(i => 
                i.Title.ToLower().Contains(lowerQuery) ||
                i.Content.ToLower().Contains(lowerQuery) ||
                i.Tags.Any(t => t.ToLower().Contains(lowerQuery))
            );
        }

        public async Task<IEnumerable<OrgItem>> FilterAsync(Func<OrgItem, bool> predicate)
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(predicate);
        }

        public void RefreshItems()
        {
            RebuildLookup();
        }

        public void UpdateFilePath(string newPath)
        {
            _filePath = newPath;
            foreach (var item in _items)
            {
                UpdateFilePathRecursive(item, newPath);
            }
        }

        // Link operations implementation
        public async Task<IEnumerable<OrgLink>> GetAllLinksAsync()
        {
            var allItems = await GetAllItemsAsync();
            return allItems.SelectMany(item => item.Links);
        }

        public async Task<IEnumerable<OrgItem>> GetItemsWithLinksAsync()
        {
            var allItems = await GetAllItemsAsync();
            return allItems.Where(item => item.HasLinks);
        }

        public async Task<OrgItem> FollowLinkAsync(OrgLink link)
        {
            switch (link.Type)
            {
                case OrgLinkType.Internal:
                    return await FindInternalTargetAsync(link.Target);
                case OrgLinkType.Id:
                    var idTarget = link.Target.Substring(3); // Remove "id:" prefix
                    return await GetItemByIdAsync(idTarget);
                case OrgLinkType.File:
                case OrgLinkType.FileWithTarget:
                    // For file links, we would need to open the file
                    // This is handled at the UI level
                    return null;
                default:
                    return null;
            }
        }

        public async Task<bool> ValidateLinkAsync(OrgLink link)
        {
            try
            {
                switch (link.Type)
                {
                    case OrgLinkType.Web:
                        return Uri.TryCreate(link.Target, UriKind.Absolute, out var uri) && 
                               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
                    
                    case OrgLinkType.File:
                        var filePath = await ResolveLinkTargetAsync(link);
                        return File.Exists(filePath);
                    
                    case OrgLinkType.Internal:
                        var target = await FindInternalTargetAsync(link.Target);
                        return target != null;
                    
                    case OrgLinkType.Id:
                        var idTarget = link.Target.Substring(3);
                        var item = await GetItemByIdAsync(idTarget);
                        return item != null;
                    
                    case OrgLinkType.FileWithTarget:
                        var parts = link.Target.Split(new[] { "::" }, StringSplitOptions.None);
                        if (parts.Length != 2) return false;
                        return File.Exists(await ResolveFilePathAsync(parts[0]));
                    
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> ResolveLinkTargetAsync(OrgLink link)
        {
            switch (link.Type)
            {
                case OrgLinkType.File:
                    return await ResolveFilePathAsync(link.Target);
                case OrgLinkType.FileWithTarget:
                    var parts = link.Target.Split(new[] { "::" }, StringSplitOptions.None);
                    return parts.Length > 0 ? await ResolveFilePathAsync(parts[0]) : link.Target;
                case OrgLinkType.Web:
                    return link.Target;
                default:
                    return link.Target;
            }
        }

        private async Task<OrgItem> FindInternalTargetAsync(string target)
        {
            var allItems = await GetAllItemsAsync();
            
            // Look for exact title match
            var exactMatch = allItems.FirstOrDefault(item => 
                item.Title.Equals(target, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null) return exactMatch;
            
            // Look for heading-style target (remove # if present)
            var headingTarget = target.StartsWith("#") ? target.Substring(1) : target;
            return allItems.FirstOrDefault(item => 
                item.Title.Equals(headingTarget, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string> ResolveFilePathAsync(string target)
        {
            // Remove file: prefix if present
            if (target.StartsWith("file:"))
                target = target.Substring(5);
            
            // Handle absolute paths
            if (Path.IsPathRooted(target))
                return target;
            
            // Handle relative paths - resolve relative to current file's directory
            var currentDir = Path.GetDirectoryName(_filePath);
            return Path.GetFullPath(Path.Combine(currentDir, target));
        }

        private void UpdateFilePathRecursive(OrgItem item, string filePath)
        {
            item.FilePath = filePath;
            foreach (var child in item.Children)
            {
                UpdateFilePathRecursive(child, filePath);
            }
        }

        private void RebuildLookup()
        {
            _itemLookup.Clear();
            foreach (var item in _items)
            {
                AddToLookupRecursive(item);
            }
        }

        private void AddToLookupRecursive(OrgItem item)
        {
            _itemLookup[item.Id] = item;
            foreach (var child in item.Children)
            {
                AddToLookupRecursive(child);
            }
        }

        private void NotifyItemChanged(string itemId, OrgItemChangeType changeType, OrgItem item)
        {
            ItemChanged?.Invoke(this, new OrgItemChangedEventArgs
            {
                ItemId = itemId,
                ChangeType = changeType,
                Item = item
            });
        }
    }
} 