using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Interfaces
{
    public interface IOrgModeService
    {
        event EventHandler<OrgItemChangedEventArgs> ItemChanged;

        string FilePath { get; }
        ObservableCollection<OrgItem> Items { get; }

        // File operations
        Task LoadFromFileAsync(string filePath);
        Task SaveToFileAsync();
        Task SaveToFileAsync(string filePath);

        // Content operations
        Task<ObservableCollection<OrgItem>> ParseContentAsync(string content);
        Task<string> SerializeContentAsync();
        Task<string> SerializeItemAsync(OrgItem item);
        string GetContent();

        // Item operations
        Task<OrgItem> CreateItemAsync(OrgItem item);
        Task UpdateItemAsync(OrgItem item);
        Task DeleteItemAsync(string id);
        Task<OrgItem> GetItemByIdAsync(string id);
        Task<IEnumerable<OrgItem>> GetAllItemsAsync();
        Task<IEnumerable<OrgItem>> GetItemsByStateAsync(OrgState state);
        Task<IEnumerable<OrgItem>> GetItemsByTagAsync(string tag);
        Task<IEnumerable<OrgItem>> GetItemsByPriorityAsync(OrgPriority priority);
        Task<IEnumerable<OrgItem>> GetOverdueItemsAsync();
        Task<IEnumerable<OrgItem>> GetScheduledItemsAsync(DateTime? date = null);
        Task<IEnumerable<OrgItem>> GetDeadlineItemsAsync(DateTime? date = null);

        // State operations
        Task CycleStateAsync(string id);
        Task SetStateAsync(string id, OrgState state);
        Task CyclePriorityAsync(string id);
        Task SetPriorityAsync(string id, OrgPriority priority);

        // Scheduling operations
        Task SetScheduledAsync(string id, DateTime? scheduled);
        Task SetDeadlineAsync(string id, DateTime? deadline);

        // Hierarchy operations
        Task AddChildAsync(string parentId, OrgItem child);
        Task RemoveChildAsync(string parentId, string childId);
        Task MoveItemAsync(string itemId, string newParentId);
        Task PromoteItemAsync(string id);
        Task DemoteItemAsync(string id);

        // Tag operations
        Task AddTagAsync(string id, string tag);
        Task RemoveTagAsync(string id, string tag);
        Task<IEnumerable<string>> GetAllTagsAsync();

        // Property operations
        Task SetPropertyAsync(string id, string key, string value);
        Task<string> GetPropertyAsync(string id, string key);
        Task RemovePropertyAsync(string id, string key);

        // Search and filter operations
        Task<IEnumerable<OrgItem>> SearchAsync(string query);
        Task<IEnumerable<OrgItem>> FilterAsync(Func<OrgItem, bool> predicate);

        // Link operations
        Task<IEnumerable<OrgLink>> GetAllLinksAsync();
        Task<IEnumerable<OrgItem>> GetItemsWithLinksAsync();
        Task<OrgItem> FollowLinkAsync(OrgLink link);
        Task<bool> ValidateLinkAsync(OrgLink link);
        Task<string> ResolveLinkTargetAsync(OrgLink link);

        // Utility operations
        void RefreshItems();
        void UpdateFilePath(string newPath);
    }

    public class OrgItemChangedEventArgs : EventArgs
    {
        public string ItemId { get; set; }
        public OrgItemChangeType ChangeType { get; set; }
        public OrgItem Item { get; set; }
    }

    public enum OrgItemChangeType
    {
        Created,
        Modified,
        Deleted,
        StateChanged,
        Moved,
        PropertyChanged
    }
} 