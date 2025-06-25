using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class GlobalOrgAgendaService
    {
        private readonly IConfigurationService _configService;
        private readonly Dictionary<string, IOrgModeService> _fileServices;
        private readonly List<OrgItemWithSource> _allItems;
        private DateTime _lastRefresh = DateTime.MinValue;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(5); // Minimum time between refreshes

        public event EventHandler ItemsChanged;

        public GlobalOrgAgendaService(IConfigurationService configService)
        {
            _configService = configService;
            _fileServices = new Dictionary<string, IOrgModeService>();
            _allItems = new List<OrgItemWithSource>();
            
            // Subscribe to org state configuration changes for immediate updates
            OrgStateConfigurationService.Instance.ConfigurationChanged += OnOrgStateConfigurationChanged;
        }

        public async Task<List<OrgItemWithSource>> GetAllItemsAsync()
        {
            // Return cached items if recently refreshed
            if (_allItems.Any() && DateTime.Now - _lastRefresh < _refreshInterval)
            {
                return _allItems.ToList();
            }

            // Only refresh if needed
            await RefreshAllFilesAsync();
            return _allItems.ToList();
        }

        public async Task<List<OrgItemWithSource>> GetOverdueItemsAsync()
        {
            try
            {
                var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
                if (allItems == null)
                {
                    System.Diagnostics.Debug.WriteLine("GetOverdueItemsAsync: allItems is null");
                    return new List<OrgItemWithSource>();
                }

                System.Diagnostics.Debug.WriteLine($"GetOverdueItemsAsync: Processing {allItems.Count} items");

                // Apply Days Behind limit
                var daysBehind = _configService.Provider.AgendaDaysBehind;
                var cutoffDate = DateTime.Today.AddDays(-daysBehind);
                var validItems = new List<OrgItemWithSource>();
                
                foreach (var item in allItems)
                {
                    try
                    {
                        if (item?.Item != null && item.Item.IsOverdue && !item.Item.IsCompleted)
                        {
                            // Check if the item is within the Days Behind limit
                            var itemDate = item.Item.Scheduled ?? item.Item.Deadline;
                            if (itemDate.HasValue && itemDate.Value >= cutoffDate)
                            {
                                System.Diagnostics.Debug.WriteLine($"Overdue item: '{item.Item.Title}' IsOverdue={item.Item.IsOverdue} Date={itemDate}");
                                validItems.Add(item);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"Skipping old overdue item: '{item.Item.Title}' Date={itemDate} (older than {daysBehind} days)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error checking item overdue status: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Item: {item?.Item?.Title ?? "null item"}");
                        // Skip problematic items
                    }
                }

                System.Diagnostics.Debug.WriteLine($"GetOverdueItemsAsync: Found {validItems.Count} overdue items within {daysBehind} days");

                // Sort safely
                try
                {
                    return validItems.OrderBy(i => i.Item.Deadline ?? DateTime.MaxValue).ToList();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error sorting overdue items: {ex.Message}");
                    return validItems; // Return unsorted if sorting fails
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in GetOverdueItemsAsync: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new List<OrgItemWithSource>(); // Return empty list on error
            }
        }

        public async Task<List<OrgItemWithSource>> GetScheduledItemsAsync(DateTime? date = null)
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            var targetDate = date ?? DateTime.Today;
            return allItems.Where(i => i?.Item != null && i.Item.Scheduled?.Date == targetDate && !i.Item.IsCompleted)
                          .OrderBy(i => i.Item.Scheduled)
                          .ToList();
        }

        public async Task<List<OrgItemWithSource>> GetDeadlineItemsAsync(DateTime? date = null)
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            var targetDate = date ?? DateTime.Today;
            return allItems.Where(i => i?.Item != null && i.Item.Deadline?.Date == targetDate && !i.Item.IsCompleted)
                          .OrderBy(i => i.Item.Deadline)
                          .ToList();
        }

        public async Task<List<OrgItemWithSource>> GetTodayItemsAsync()
        {
            var today = DateTime.Today;
            System.Diagnostics.Debug.WriteLine($"GetTodayItemsAsync: Looking for items on {today:yyyy-MM-dd}");
            
            var scheduled = await GetScheduledItemsAsync(today);
            var deadlines = await GetDeadlineItemsAsync(today);
            
            System.Diagnostics.Debug.WriteLine($"GetTodayItemsAsync: Found {scheduled.Count} scheduled items and {deadlines.Count} deadline items for today");
            
            // Combine and deduplicate
            var todayItems = scheduled.Union(deadlines, new OrgItemWithSourceComparer()).ToList();
            System.Diagnostics.Debug.WriteLine($"GetTodayItemsAsync: Total today items after deduplication: {todayItems.Count}");
            
            return todayItems.OrderBy(i => i.Item.Scheduled ?? i.Item.Deadline).ToList();
        }

        public async Task<List<GlobalAgendaDay>> GetUpcomingDaysAsync(int daysAhead = 14)
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            var upcomingDays = new List<GlobalAgendaDay>();
            
            System.Diagnostics.Debug.WriteLine($"GetUpcomingDaysAsync: Checking {daysAhead} days ahead from {DateTime.Today:yyyy-MM-dd}");

            for (int i = 1; i <= daysAhead; i++)
            {
                var date = DateTime.Today.AddDays(i);
                var dayItems = allItems.Where(item =>
                    item?.Item != null &&
                    (item.Item.Scheduled?.Date == date || item.Item.Deadline?.Date == date) &&
                    !item.Item.IsCompleted)
                    .OrderBy(item => item.Item.Scheduled ?? item.Item.Deadline)
                    .ToList();

                if (dayItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"GetUpcomingDaysAsync: Found {dayItems.Count} items for {date:yyyy-MM-dd}");
                    upcomingDays.Add(new GlobalAgendaDay
                    {
                        Date = date,
                        DateHeader = $"📅 {date:dddd, MMMM dd}",
                        Items = dayItems
                    });
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"GetUpcomingDaysAsync: Total upcoming days with items: {upcomingDays.Count}");
            return upcomingDays;
        }

        /// <summary>
        /// Gets all agenda items organized by date, including overdue, today, and upcoming days
        /// This provides a unified chronological view of all items regardless of type
        /// </summary>
        public async Task<List<GlobalAgendaDay>> GetAllDaysAsync(int daysAhead = 14)
        {
            // Use cached items only - do NOT trigger a refresh here to avoid infinite loops
            var allItems = _allItems.ToList();
            var agendaDays = new List<GlobalAgendaDay>();
            
            System.Diagnostics.Debug.WriteLine($"GetAllDaysAsync: Organizing {allItems.Count} cached items by date");

            // 1. OVERDUE ITEMS - Items with dates before today (limited by Days Behind setting)
            var daysBehind = _configService.Provider.AgendaDaysBehind;
            var cutoffDate = DateTime.Today.AddDays(-daysBehind);
            
            var overdueItems = allItems.Where(item =>
                item?.Item != null &&
                !item.Item.IsCompleted &&
                (item.Item.Scheduled?.Date < DateTime.Today || item.Item.Deadline?.Date < DateTime.Today) &&
                // Only include items within the Days Behind limit
                ((item.Item.Scheduled?.Date >= cutoffDate) || (item.Item.Deadline?.Date >= cutoffDate)))
                .OrderBy(item => item.Item.Scheduled ?? item.Item.Deadline ?? DateTime.MinValue)
                .ToList();

            if (overdueItems.Any())
            {
                agendaDays.Add(new GlobalAgendaDay
                {
                    Date = DateTime.Today.AddDays(-1), // Placeholder date for overdue
                    DateHeader = "⚠️ OVERDUE",
                    Items = overdueItems
                });
            }

            // 2. TODAY - All items scheduled or due today
            var todayItems = allItems.Where(item =>
                item?.Item != null &&
                !item.Item.IsCompleted &&
                (item.Item.Scheduled?.Date == DateTime.Today || 
                 item.Item.Deadline?.Date == DateTime.Today ||
                 // Include calendar events and standalone timestamps for today
                 (item.Item.HasDate && item.Item.Scheduled?.Date == DateTime.Today)))
                .Union(allItems.Where(item =>
                    item?.Item != null &&
                    item.Item.State == OrgState.None && // Calendar events
                    item.Item.HasDate &&
                    item.Item.Scheduled?.Date == DateTime.Today), new OrgItemWithSourceComparer())
                .OrderBy(item => item.Item.Scheduled ?? item.Item.Deadline ?? DateTime.Now)
                .ToList();

            if (todayItems.Any())
            {
                agendaDays.Add(new GlobalAgendaDay
                {
                    Date = DateTime.Today,
                    DateHeader = $"📅 TODAY - {DateTime.Today:dddd, MMMM dd}",
                    Items = todayItems
                });
            }

            // 3. FUTURE DAYS - All items scheduled for upcoming days
            for (int i = 1; i <= daysAhead; i++)
            {
                var date = DateTime.Today.AddDays(i);
                
                // Get all items for this date (TODOs, calendar events, etc.)
                var dayItems = allItems.Where(item =>
                    item?.Item != null &&
                    (// TODOs and tasks (not completed)
                     (!item.Item.IsCompleted && 
                      (item.Item.Scheduled?.Date == date || item.Item.Deadline?.Date == date)) ||
                     // Calendar events (State=None with date)
                     (item.Item.State == OrgState.None && 
                      item.Item.HasDate && 
                      item.Item.Scheduled?.Date == date)))
                    .OrderBy(item => 
                        // Sort by time if available, otherwise by type (calendar events first, then by state)
                        item.Item.State == OrgState.None ? 0 : 1)
                    .ThenBy(item => item.Item.Scheduled ?? item.Item.Deadline ?? DateTime.MaxValue)
                    .ThenBy(item => item.Item.Title)
                    .ToList();

                if (dayItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"GetAllDaysAsync: Found {dayItems.Count} items for {date:yyyy-MM-dd}");
                    agendaDays.Add(new GlobalAgendaDay
                    {
                        Date = date,
                        DateHeader = $"📅 {date:dddd, MMMM dd}",
                        Items = dayItems
                    });
                }
            }

            // 4. UNSCHEDULED TODOs (grouped by category)
            var stateConfig = GetStateConfiguration();
            var unscheduledItems = allItems.Where(item =>
                item?.Item != null &&
                !item.Item.HasDate &&
                !item.Item.IsCompleted &&
                item.Item.State != OrgState.None && // Exclude calendar events
                stateConfig.RequiresAction(item.Item.State.ToString()))
                .ToList();

            if (unscheduledItems.Any())
            {
                // Group by category
                var groupedByCategory = unscheduledItems
                    .GroupBy(item => GetItemCategory(item))
                    .OrderBy(group => group.Key == "Uncategorized" ? "z" : group.Key) // Show categorized items first
                    .ToList();

                foreach (var categoryGroup in groupedByCategory)
                {
                    var categoryItems = categoryGroup
                        .OrderBy(item => item.Item.Priority)
                        .ThenBy(item => item.Item.Title)
                        .ToList();

                    var categoryName = categoryGroup.Key;
                    
                    agendaDays.Add(new GlobalAgendaDay
                    {
                        Date = DateTime.MaxValue.AddDays(-Math.Abs(categoryGroup.Key.GetHashCode() % 1000)), // Safe unique placeholder dates
                        DateHeader = $"{categoryName} TODOs",
                        Items = categoryItems
                    });
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"GetAllDaysAsync: Total agenda days: {agendaDays.Count}");
            return agendaDays;
        }

        public async Task<List<OrgItemWithSource>> GetActionRequiredItemsAsync()
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            var stateConfig = GetStateConfiguration();
            
            System.Diagnostics.Debug.WriteLine($"GetActionRequiredItemsAsync: Processing {allItems.Count} items");
            
            // Show ALL TODO items (exclude calendar events with State=None)
            var actionItems = allItems.Where(i => 
                i?.Item != null &&
                !i.Item.IsCompleted && 
                i.Item.State != OrgState.None && // Exclude calendar events  
                stateConfig.RequiresAction(i.Item.State.ToString()))
                .OrderBy(i => i.Item.Priority) // Sort by priority (A, B, C)
                .ThenBy(i => i.Item.Deadline ?? DateTime.MaxValue) // Then by deadline
                .ThenBy(i => i.Item.Scheduled ?? DateTime.MaxValue) // Then by scheduled
                .ThenBy(i => i.Item.Title) // Finally by title
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"GetActionRequiredItemsAsync: Found {actionItems.Count} TODO items (excluding calendar events)");
            
            foreach (var item in actionItems)
            {
                var scheduleInfo = item.Item.Scheduled?.ToString("yyyy-MM-dd") ?? "unscheduled";
                var deadlineInfo = item.Item.Deadline?.ToString("yyyy-MM-dd") ?? "no deadline";
                System.Diagnostics.Debug.WriteLine($"TODO item: '{item.Item?.Title}' State={item.Item?.State} Scheduled={scheduleInfo} Deadline={deadlineInfo}");
            }
            
            return actionItems;
        }

        // New method: Get calendar events (State=None items, including those without dates)
        public async Task<List<OrgItemWithSource>> GetCalendarEventsAsync()
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            
            // Debug: Check all State=None items first
            var stateNoneItems = allItems.Where(i => i?.Item != null && i.Item.State == OrgState.None).ToList();
            System.Diagnostics.Debug.WriteLine($"GetCalendarEventsAsync: Found {stateNoneItems.Count} items with State=None");
            
            foreach (var item in stateNoneItems)
            {
                var scheduleInfo = item.Item.Scheduled?.ToString("yyyy-MM-dd") ?? "no schedule";
                var deadlineInfo = item.Item.Deadline?.ToString("yyyy-MM-dd") ?? "no deadline";
                var hasDate = item.Item.Scheduled.HasValue || item.Item.Deadline.HasValue;
                System.Diagnostics.Debug.WriteLine($"State=None item: '{item.Item?.Title}' Scheduled={scheduleInfo} Deadline={deadlineInfo} HasDate={hasDate}");
            }
            
            // For now, show ALL State=None items (calendar events), even without explicit dates
            var calendarItems = stateNoneItems
                .OrderBy(i => i.Item.Scheduled ?? i.Item.Deadline ?? DateTime.MaxValue)
                .ThenBy(i => i.Item.Title)
                .ToList();
            
            System.Diagnostics.Debug.WriteLine($"GetCalendarEventsAsync: Returning {calendarItems.Count} calendar events");
            
            return calendarItems;
        }

        // New method: Get unscheduled TODO items grouped by category
        public async Task<Dictionary<string, List<OrgItemWithSource>>> GetUnscheduledTodosByCategoryAsync()
        {
            var allItems = _allItems.Any() ? _allItems.ToList() : await GetAllItemsAsync();
            var stateConfig = GetStateConfiguration();
            
            var unscheduledItems = allItems.Where(i => 
                i?.Item != null &&
                !i.Item.IsCompleted && 
                stateConfig.RequiresAction(i.Item.State.ToString()) &&
                !i.Item.IsOverdue && // Exclude overdue items (they appear in overdue section)
                i.Item.Scheduled == null && // Only unscheduled items
                i.Item.Deadline == null) // Only items without deadlines
                .ToList();
            
            var groupedByCategory = unscheduledItems
                .GroupBy(item => GetItemCategory(item))
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.Item.Priority)
                        .ThenBy(item => item.Item.Title)
                        .ToList()
                );
            
            System.Diagnostics.Debug.WriteLine($"GetUnscheduledTodosByCategoryAsync: Found {unscheduledItems.Count} unscheduled TODO items in {groupedByCategory.Count} categories");
            
            return groupedByCategory;
        }

        // Force refresh - only call this explicitly when needed
        public async Task ForceRefreshAsync()
        {
            _lastRefresh = DateTime.MinValue; // Reset cache
            await RefreshAllFilesAsync();
        }

        public async Task RefreshAllFilesAsync()
        {
            try
            {
                // Check if we should skip refresh due to timing
                if (DateTime.Now - _lastRefresh < _refreshInterval && _allItems.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Skipping refresh, last refresh was {(DateTime.Now - _lastRefresh).TotalSeconds:F1} seconds ago");
                    return;
                }

                _allItems.Clear();
                System.Diagnostics.Debug.WriteLine("RefreshAllFilesAsync: Cleared all items");

                if (!_configService.Provider.EnableGlobalAgenda)
                {
                    System.Diagnostics.Debug.WriteLine("RefreshAllFilesAsync: Global agenda is disabled");
                    return;
                }

                var filesToScan = new List<string>();

                // Add explicitly configured files
                try
                {
                    var configuredFiles = _configService.Provider.OrgAgendaFiles ?? new string[0];
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Found {configuredFiles.Length} configured files");
                    filesToScan.AddRange(configuredFiles);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Error getting configured files: {ex.Message}");
                }

                // Add files from configured directories
                try
                {
                    var configuredDirs = _configService.Provider.OrgAgendaDirectories ?? new string[0];
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Found {configuredDirs.Length} configured directories");
                    
                    foreach (var directory in configuredDirs)
                    {
                        try
                        {
                            if (Directory.Exists(directory))
                            {
                                var orgFiles = Directory.GetFiles(directory, "*.org", SearchOption.AllDirectories);
                                System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Found {orgFiles.Length} org files in {directory}");
                                filesToScan.AddRange(orgFiles);
                            }
                            else
                            {
                                System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Directory does not exist: {directory}");
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Error scanning directory {directory}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Error getting configured directories: {ex.Message}");
                }

                // Remove duplicates
                try
                {
                    filesToScan = filesToScan.Distinct().ToList();
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Total unique files to scan: {filesToScan.Count}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Error removing duplicates: {ex.Message}");
                    return;
                }

                // Load items from each file
                foreach (var filePath in filesToScan)
                {
                    try
                    {
                        await LoadFileItemsAsync(filePath);
                    }
                    catch (Exception ex)
                    {
                        // Log error but continue with other files
                        System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Error loading org file {filePath}: {ex.Message}");
                    }
                }

                _lastRefresh = DateTime.Now; // Update refresh timestamp
                System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Loaded {_allItems.Count} total items");
                
                // Debug: Log details about loaded items
                foreach (var item in _allItems)
                {
                    System.Diagnostics.Debug.WriteLine($"Loaded item: '{item.Item?.Title}' State={item.Item?.State} IsCompleted={item.Item?.IsCompleted} IsOverdue={item.Item?.IsOverdue} Deadline={item.Item?.Deadline} Scheduled={item.Item?.Scheduled}");
                }
                
                // Fire event AFTER setting the refresh timestamp to prevent loops
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: General error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"RefreshAllFilesAsync: Stack trace: {ex.StackTrace}");
            }
        }

        private async Task LoadFileItemsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: File does not exist: {filePath}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Loading file: {filePath}");

                // Create or get existing service for this file
                if (!_fileServices.TryGetValue(filePath, out var service))
                {
                    try
                    {
                        service = new OrgModeService(filePath);
                        _fileServices[filePath] = service;
                        System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Created new service for: {filePath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Error creating OrgModeService for {filePath}: {ex.Message}");
                        return;
                    }
                }

                try
                {
                    await service.LoadFromFileAsync(filePath);
                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Loaded file content for: {filePath}");
                    
                                    // Debug: Show sample content from both files
                if (filePath.Contains("Tasks.org") || filePath.Contains("calendar.org"))
                {
                    try
                    {
                        var content = await System.IO.File.ReadAllTextAsync(filePath);
                        var lines = content.Split('\n').Take(15).ToArray();
                        System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: First 15 lines of {filePath}:");
                        for (int i = 0; i < lines.Length; i++)
                        {
                            System.Diagnostics.Debug.WriteLine($"  Line {i+1}: '{lines[i].Trim()}'");
                        }
                    }
                    catch (Exception debugEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Error reading file for debug: {debugEx.Message}");
                    }
                }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Error loading file {filePath}: {ex.Message}");
                    return;
                }

                // Extract all items from this file
                try
                {
                    var fileItems = await service.GetAllItemsAsync();
                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Retrieved {fileItems?.Count() ?? 0} items from: {filePath}");
                    
                    if (fileItems != null)
                    {
                        foreach (var item in fileItems)
                        {
                            if (item != null) // Add null check
                            {
                                try
                                {
                                    _allItems.Add(new OrgItemWithSource
                                    {
                                        Item = item,
                                        SourceFile = filePath,
                                        SourceFileName = Path.GetFileName(filePath),
                                        Service = service
                                    });
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Error adding item '{item.Title}' from {filePath}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Error getting items from {filePath}: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: General error for {filePath}: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"LoadFileItemsAsync: Stack trace: {ex.StackTrace}");
            }
        }

        public OrgStateConfiguration GetStateConfiguration()
        {
            // Use the centralized org state configuration service
            return OrgStateConfigurationService.Instance.GetConfiguration();
        }

        /// <summary>
        /// Gets the category for an org item, checking both Properties["CATEGORY"] and tags
        /// </summary>
        private string GetItemCategory(OrgItemWithSource itemWithSource)
        {
            var item = itemWithSource?.Item;
            if (item == null) return "Uncategorized";

            // First check Properties["CATEGORY"] (most explicit)
            if (item.Properties?.ContainsKey("CATEGORY") == true)
            {
                var category = item.Properties["CATEGORY"];
                if (!string.IsNullOrWhiteSpace(category))
                    return category.Trim();
            }

            // Use the first tag as category
            if (item.Tags?.Any() == true)
            {
                var firstTag = item.Tags.First();
                if (!string.IsNullOrWhiteSpace(firstTag))
                    return firstTag.Trim();
            }

            return "Uncategorized";
        }

        public async Task<bool> UpdateItemStateAsync(OrgItemWithSource itemWithSource, string newState)
        {
            try
            {
                if (itemWithSource?.Service != null && itemWithSource.Item != null)
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Updating '{itemWithSource.Item.Title}' from {itemWithSource.Item.State} to {newState}");
                    
                    // Parse the new state to enum
                    if (Enum.TryParse<OrgState>(newState, out var state))
                    {
                        await itemWithSource.Service.SetStateAsync(itemWithSource.Item.Id, state);
                        await itemWithSource.Service.SaveToFileAsync();
                        
                        System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Successfully updated and saved '{itemWithSource.Item.Title}' to {newState}");
                        
                        // Update the item's state in our cached collection
                        itemWithSource.Item.State = state;
                        
                        return true;
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Failed to parse state '{newState}' to OrgState enum");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Invalid itemWithSource - Service={itemWithSource?.Service != null}, Item={itemWithSource?.Item != null}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Error updating state: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"UpdateItemStateAsync: Stack trace: {ex.StackTrace}");
            }
            return false;
        }

        public void Dispose()
        {
            // Unsubscribe from org state configuration changes
            OrgStateConfigurationService.Instance.ConfigurationChanged -= OnOrgStateConfigurationChanged;
            
            foreach (var service in _fileServices.Values)
            {
                if (service is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            _fileServices.Clear();
            _allItems.Clear();
        }

        private void OnOrgStateConfigurationChanged(object sender, EventArgs e)
        {
            // Handle org state configuration changes
            System.Diagnostics.Debug.WriteLine("Org state configuration changed. Refreshing agenda...");
            _lastRefresh = DateTime.MinValue; // Reset cache
            Task.Run(async () => await RefreshAllFilesAsync());
        }
    }

    public class OrgItemWithSource
    {
        public OrgItem Item { get; set; }
        public string SourceFile { get; set; }
        public string SourceFileName { get; set; }
        public IOrgModeService Service { get; set; }
    }

    public class GlobalAgendaDay
    {
        public DateTime Date { get; set; }
        public string DateHeader { get; set; }
        public List<OrgItemWithSource> Items { get; set; }
    }

    public class OrgItemWithSourceComparer : IEqualityComparer<OrgItemWithSource>
    {
        public bool Equals(OrgItemWithSource x, OrgItemWithSource y)
        {
            if (x == null || y == null) return false;
            if (x.Item == null || y.Item == null) return false;
            return x.Item.Id == y.Item.Id && x.SourceFile == y.SourceFile;
        }

        public int GetHashCode(OrgItemWithSource obj)
        {
            if (obj?.Item == null) return 0;
            return HashCode.Combine(obj.Item.Id, obj.SourceFile);
        }
    }
} 