using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for handling org-mode refile operations - moving items between files and hierarchies
    /// </summary>
    public class OrgRefileService
    {
        private readonly IConfigurationService _configService;
        private readonly GlobalOrgAgendaService _globalAgendaService;

        public OrgRefileService(IConfigurationService configService, GlobalOrgAgendaService globalAgendaService)
        {
            _configService = configService;
            _globalAgendaService = globalAgendaService;
        }

        /// <summary>
        /// Gets all available refile targets (files and items that can accept children)
        /// </summary>
        public async Task<List<RefileTarget>> GetRefileTargetsAsync()
        {
            var targets = new List<RefileTarget>();

            // Get all items from global agenda
            var allItems = await _globalAgendaService.GetAllItemsAsync();
            
            // Group by file
            var fileGroups = allItems.GroupBy(item => item.SourceFile);

            foreach (var fileGroup in fileGroups)
            {
                var fileName = Path.GetFileName(fileGroup.Key);
                
                // Add file root as a target
                targets.Add(new RefileTarget
                {
                    Type = RefileTargetType.File,
                    FilePath = fileGroup.Key,
                    DisplayPath = $"📁 {fileName}",
                    Level = 0,
                    Item = null,
                    Service = fileGroup.First().Service
                });

                // Add items that can have children (projects, high-level items)
                foreach (var itemWithSource in fileGroup.OrderBy(i => i.Item?.Level).ThenBy(i => i.Item?.Title))
                {
                    if (itemWithSource.Item != null && CanReceiveChildren(itemWithSource.Item))
                    {
                        var indent = new string(' ', itemWithSource.Item.Level * 2);
                        var stateDisplay = itemWithSource.Item.State != OrgState.None ? $"[{itemWithSource.Item.State}] " : "";
                        
                        targets.Add(new RefileTarget
                        {
                            Type = RefileTargetType.Item,
                            FilePath = fileGroup.Key,
                            DisplayPath = $"{indent}├─ {stateDisplay}{itemWithSource.Item.Title}",
                            Level = itemWithSource.Item.Level,
                            Item = itemWithSource.Item,
                            Service = itemWithSource.Service
                        });
                    }
                }
            }

            return targets;
        }

        /// <summary>
        /// Moves an item to a new location (different file or parent)
        /// </summary>
        public async Task<bool> RefileItemAsync(OrgItem sourceItem, IOrgModeService sourceService, RefileTarget target)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Starting refile of '{sourceItem?.Title}' (ID: {sourceItem?.Id})");
                
                if (sourceItem == null || sourceService == null || target == null)
                {
                    System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Null parameter check failed - sourceItem: {sourceItem != null}, sourceService: {sourceService != null}, target: {target != null}");
                    return false;
                }

                // Cannot refile to self or own descendants
                if (target.Item != null && IsDescendantOf(target.Item, sourceItem))
                {
                    System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Cannot refile to descendant - aborting");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Creating deep copy of item");
                // Step 1: Create a deep copy of the item
                var itemCopy = CloneItem(sourceItem);
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Created copy with ID: {itemCopy.Id}");

                // Step 2: Add to target location
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Adding to target - Type: {target.Type}, FilePath: {target.FilePath}");
                if (target.Type == RefileTargetType.File)
                {
                    // Refile to file root
                    itemCopy.Level = 1;
                    itemCopy.Parent = null;
                    itemCopy.FilePath = target.FilePath;
                    await target.Service.CreateItemAsync(itemCopy);
                    System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Added item to file root");
                }
                else if (target.Type == RefileTargetType.Item && target.Item != null)
                {
                    // Refile under another item
                    itemCopy.Level = target.Item.Level + 1;
                    itemCopy.FilePath = target.FilePath;
                    await target.Service.AddChildAsync(target.Item.Id, itemCopy);
                    System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Added item as child of {target.Item.Title}");
                }

                // Step 3: Save target file
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: About to save target file");
                await target.Service.SaveToFileAsync();
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Saved target file: {target.FilePath}");

                // Step 4: Remove from source (this also saves source file)
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: About to delete item '{sourceItem.Title}' (ID: {sourceItem.Id}) from source");
                await sourceService.DeleteItemAsync(sourceItem.Id);
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Deleted item from source service");
                
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: About to save source file");
                await sourceService.SaveToFileAsync();
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Saved source file: {sourceService.FilePath}");

                // Step 5: Refresh global agenda
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: About to refresh global agenda");
                await _globalAgendaService.ForceRefreshAsync();
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Refreshed global agenda");

                System.Diagnostics.Debug.WriteLine($"RefileItemAsync: Refile completed successfully");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"RefileItemAsync stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Bulk refile multiple items to the same target
        /// </summary>
        public async Task<(int success, int total)> RefileItemsAsync(List<(OrgItem item, IOrgModeService service)> sourceItems, RefileTarget target)
        {
            int successCount = 0;
            int totalCount = sourceItems.Count;

            foreach (var (item, service) in sourceItems)
            {
                if (await RefileItemAsync(item, service, target))
                {
                    successCount++;
                }
            }

            return (successCount, totalCount);
        }

        /// <summary>
        /// Quick refile to commonly used locations (inbox, projects, someday, etc.)
        /// </summary>
        public async Task<List<QuickRefileTarget>> GetQuickRefileTargetsAsync()
        {
            var quickTargets = new List<QuickRefileTarget>();

            // Get configured quick refile targets from settings
            var configuredTargets = _configService.Provider.OrgQuickRefileTargets ?? new Dictionary<string, string>();

            foreach (var kvp in configuredTargets)
            {
                var target = await ResolveQuickTarget(kvp.Key, kvp.Value);
                if (target != null)
                {
                    quickTargets.Add(target);
                }
            }

            // Add default targets if not configured
            if (!quickTargets.Any())
            {
                quickTargets.AddRange(await GetDefaultQuickTargetsAsync());
            }

            return quickTargets;
        }

        /// <summary>
        /// Search for potential refile targets by title/content
        /// </summary>
        public async Task<List<RefileTarget>> SearchRefileTargetsAsync(string query)
        {
            var allTargets = await GetRefileTargetsAsync();
            var lowerQuery = query.ToLower();

            return allTargets.Where(target =>
                target.DisplayPath.ToLower().Contains(lowerQuery) ||
                (target.Item?.Title.ToLower().Contains(lowerQuery) ?? false) ||
                (target.Item?.Content.ToLower().Contains(lowerQuery) ?? false) ||
                Path.GetFileName(target.FilePath).ToLower().Contains(lowerQuery)
            ).ToList();
        }

        /// <summary>
        /// Get recently used refile targets for quick access
        /// </summary>
        public async Task<List<RefileTarget>> GetRecentRefileTargetsAsync()
        {
            // TODO: Implement based on user's refile history
            // For now, return most common targets
            var allTargets = await GetRefileTargetsAsync();
            
            return allTargets
                .Where(t => t.Type == RefileTargetType.File || 
                           (t.Item?.IsProject ?? false))
                .Take(10)
                .ToList();
        }

        #region Private Helper Methods

        private bool CanReceiveChildren(OrgItem item)
        {
            // Items that can receive children:
            // 1. Project items
            // 2. High-level items (level 1-2)
            // 3. Items that already have children
            // 4. Items without TODO states (probably headings)
            
            return item.IsProject ||
                   item.Level <= 2 ||
                   item.HasChildren ||
                   item.State == OrgState.None ||
                   item.State == OrgState.PROJECT;
        }

        private bool IsDescendantOf(OrgItem potentialAncestor, OrgItem item)
        {
            var current = item.Parent;
            while (current != null)
            {
                if (current.Id == potentialAncestor.Id)
                    return true;
                current = current.Parent;
            }
            return false;
        }

        private OrgItem CloneItem(OrgItem source)
        {
            var clone = new OrgItem
            {
                Id = Guid.NewGuid().ToString(), // New ID
                Title = source.Title,
                Content = source.Content,
                State = source.State,
                Priority = source.Priority,
                Level = source.Level, // Will be adjusted during refile
                Scheduled = source.Scheduled,
                Deadline = source.Deadline,
                Closed = source.Closed,
                Created = source.Created,
                LastModified = DateTime.Now,
                IsExpanded = source.IsExpanded
            };

            // Clone tags
            foreach (var tag in source.Tags)
            {
                clone.AddTag(tag);
            }

            // Clone properties
            foreach (var kvp in source.Properties)
            {
                clone.SetProperty(kvp.Key, kvp.Value);
            }

            // Recursively clone children
            foreach (var child in source.Children)
            {
                var childClone = CloneItem(child);
                clone.AddChild(childClone);
            }

            return clone;
        }

        private async Task<QuickRefileTarget> ResolveQuickTarget(string name, string path)
        {
            try
            {
                // Parse path format: "file.org" or "file.org::*Heading"
                var parts = path.Split(new[] { "::" }, StringSplitOptions.RemoveEmptyEntries);
                var filePath = parts[0];
                var heading = parts.Length > 1 ? parts[1].TrimStart('*').Trim() : null;

                // Resolve to full path if relative
                if (!Path.IsPathRooted(filePath))
                {
                    var baseDir = Path.GetDirectoryName(_configService.Provider.LibraryPath) ?? 
                                  Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                    filePath = Path.Combine(baseDir, filePath);
                }

                if (!File.Exists(filePath))
                    return null;

                // Find the target service and item
                var allItems = await _globalAgendaService.GetAllItemsAsync();
                var fileItems = allItems.Where(i => i.SourceFile.Equals(filePath, StringComparison.OrdinalIgnoreCase));
                
                if (!fileItems.Any())
                    return null;

                var service = fileItems.First().Service;
                OrgItem targetItem = null;

                if (!string.IsNullOrEmpty(heading))
                {
                    // Find specific heading
                    targetItem = fileItems.FirstOrDefault(i => 
                        i.Item?.Title.Equals(heading, StringComparison.OrdinalIgnoreCase) ?? false)?.Item;
                    
                    if (targetItem == null)
                        return null;
                }

                return new QuickRefileTarget
                {
                    Name = name,
                    Target = new RefileTarget
                    {
                        Type = targetItem == null ? RefileTargetType.File : RefileTargetType.Item,
                        FilePath = filePath,
                        DisplayPath = targetItem == null ? $"📁 {Path.GetFileName(filePath)}" : $"📄 {targetItem.Title}",
                        Level = targetItem?.Level ?? 0,
                        Item = targetItem,
                        Service = service
                    }
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ResolveQuickTarget error for {name}:{path} - {ex.Message}");
                return null;
            }
        }

        private async Task<List<QuickRefileTarget>> GetDefaultQuickTargetsAsync()
        {
            var defaults = new List<QuickRefileTarget>();
            
            // Try to find common org files
            var commonFiles = new[] { "inbox.org", "tasks.org", "projects.org", "someday.org", "reference.org" };
            var allItems = await _globalAgendaService.GetAllItemsAsync();
            
            foreach (var fileName in commonFiles)
            {
                var matchingFile = allItems.FirstOrDefault(i => 
                    Path.GetFileName(i.SourceFile).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                
                if (matchingFile != null)
                {
                    defaults.Add(new QuickRefileTarget
                    {
                        Name = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant(),
                        Target = new RefileTarget
                        {
                            Type = RefileTargetType.File,
                            FilePath = matchingFile.SourceFile,
                            DisplayPath = $"📁 {fileName}",
                            Level = 0,
                            Item = null,
                            Service = matchingFile.Service
                        }
                    });
                }
            }

            return defaults;
        }

        #endregion
    }

    public class RefileTarget
    {
        public RefileTargetType Type { get; set; }
        public string FilePath { get; set; }
        public string DisplayPath { get; set; }
        public int Level { get; set; }
        public OrgItem Item { get; set; }
        public IOrgModeService Service { get; set; }
    }

    public class QuickRefileTarget
    {
        public string Name { get; set; }
        public RefileTarget Target { get; set; }
    }

    public enum RefileTargetType
    {
        File,
        Item
    }
} 