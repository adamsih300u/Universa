using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using Universa.Desktop.Services;

namespace Universa.Desktop.Models
{
    public enum OrgState
    {
        None,       // No TODO keyword
        TODO,
        NEXT,
        STARTED,
        WAITING,
        DEFERRED,
        PROJECT,    // For project headings
        SOMEDAY,    // For someday/maybe items
        DONE,
        CANCELLED
    }

    public enum OrgPriority
    {
        None,
        A,  // [#A] - High
        B,  // [#B] - Medium  
        C   // [#C] - Low
    }

    public class OrgItem : INotifyPropertyChanged
    {
        private string _id;
        private int _level;
        private OrgState _state;
        private OrgPriority _priority;
        private string _title;
        private string _content;
        private List<string> _tags;
        private Dictionary<string, string> _properties;
        private DateTime? _scheduled;
        private DateTime? _deadline;
        private DateTime? _closed;
        private DateTime _created;
        private DateTime _lastModified;
        private ObservableCollection<OrgItem> _children;
        private string _filePath;
        private OrgItem _parent;
        private bool _isExpanded;
        private bool _suppressAutoClosedTimestamp;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Id
        {
            get => _id ??= Guid.NewGuid().ToString();
            set
            {
                if (_id != value)
                {
                    _id = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Level
        {
            get => _level;
            set
            {
                if (_level != value)
                {
                    _level = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Indent));
                }
            }
        }

        public OrgState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    var oldState = _state;
                    _state = value;
                    
                    // Auto-set CLOSED timestamp when completing (unless suppressed for debounced cycling)
                    if (!_suppressAutoClosedTimestamp)
                    {
                        if (IsCompleted && !Closed.HasValue)
                        {
                            Closed = DateTime.Now;
                        }
                        else if (!IsCompleted && Closed.HasValue)
                        {
                            Closed = null;
                        }
                    }
                    
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsCompleted));
                    OnPropertyChanged(nameof(StateDisplay));
                    UpdateLastModified();
                }
            }
        }

        public OrgPriority Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(PriorityDisplay));
                    UpdateLastModified();
                }
            }
        }

        public string Title
        {
            get => _title ?? string.Empty;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string Content
        {
            get => _content ?? string.Empty;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public List<string> Tags
        {
            get => _tags ??= new List<string>();
            set
            {
                if (_tags != value)
                {
                    _tags = value ?? new List<string>();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TagsDisplay));
                    UpdateLastModified();
                }
            }
        }

        public Dictionary<string, string> Properties
        {
            get => _properties ??= new Dictionary<string, string>();
            set
            {
                if (_properties != value)
                {
                    _properties = value ?? new Dictionary<string, string>();
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public DateTime? Scheduled
        {
            get => _scheduled;
            set
            {
                if (_scheduled != value)
                {
                    _scheduled = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ScheduledDisplay));
                    UpdateLastModified();
                }
            }
        }

        public DateTime? Deadline
        {
            get => _deadline;
            set
            {
                if (_deadline != value)
                {
                    _deadline = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DeadlineDisplay));
                    OnPropertyChanged(nameof(IsOverdue));
                    UpdateLastModified();
                }
            }
        }

        public DateTime? Closed
        {
            get => _closed;
            set
            {
                if (_closed != value)
                {
                    _closed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ClosedDisplay));
                }
            }
        }

        public DateTime Created
        {
            get => _created;
            set
            {
                if (_created != value)
                {
                    _created = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime LastModified
        {
            get => _lastModified;
            set
            {
                if (_lastModified != value)
                {
                    _lastModified = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<OrgItem> Children
        {
            get => _children ??= new ObservableCollection<OrgItem>();
            set
            {
                if (_children != value)
                {
                    _children = value ?? new ObservableCollection<OrgItem>();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasChildren));
                }
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public OrgItem Parent
        {
            get => _parent;
            set
            {
                if (_parent != value)
                {
                    _parent = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();
                }
            }
        }

        // Computed Properties
        public bool IsCompleted => State == OrgState.DONE || State == OrgState.CANCELLED;
        public bool HasChildren => Children?.Any() ?? false;
        public string Indent => new string(' ', Level * 2);
        public string StateDisplay => State == OrgState.None ? "" : State.ToString();
        public string PriorityDisplay => Priority == OrgPriority.None ? "" : $"[#{Priority}]";
        public string TagsDisplay => (Tags?.Any() ?? false) ? $":{string.Join(":", Tags)}:" : "";
        public string ScheduledDisplay => Scheduled?.ToString("yyyy-MM-dd ddd") ?? "";
        public string DeadlineDisplay => Deadline?.ToString("yyyy-MM-dd ddd") ?? "";
        public string ClosedDisplay => Closed?.ToString("yyyy-MM-dd ddd HH:mm") ?? "";
        public bool IsOverdue => !IsCompleted && Deadline.HasValue && Deadline.Value.Date < DateTime.Today;

        // Date and agenda properties
        public bool HasDate => Scheduled.HasValue || Deadline.HasValue;
        public bool IsCalendarEvent => State == OrgState.None && HasDate;
        public bool IsActionableItem => State != OrgState.None && !IsCompleted;

        // Project-specific properties
        public bool IsProject => (Tags?.Contains("project", StringComparer.OrdinalIgnoreCase) ?? false) || 
                                 State.ToString().Contains("PROJECT", StringComparison.OrdinalIgnoreCase) ||
                                 (Properties?.ContainsKey("PROJECT") ?? false) ||
                                 (Properties?.ContainsKey("CATEGORY") ?? false);
        
        public string ProjectCategory => (Properties?.ContainsKey("CATEGORY") ?? false) ? Properties["CATEGORY"] : "";
        public string ProjectBudget => (Properties?.ContainsKey("BUDGET") ?? false) ? Properties["BUDGET"] : "";
        public string ProjectClient => (Properties?.ContainsKey("CLIENT") ?? false) ? Properties["CLIENT"] : "";
        public string ProjectManager => (Properties?.ContainsKey("MANAGER") ?? false) ? Properties["MANAGER"] : "";
        public string ProjectStatus => (Properties?.ContainsKey("STATUS") ?? false) ? Properties["STATUS"] : "";
        
        // Project progress tracking
        public int TotalTasks => GetAllDescendants()?.Count() ?? 0;
        public int CompletedTasks => GetAllDescendants()?.Count(item => item.IsCompleted) ?? 0;
        public int OverdueTasks => GetAllDescendants()?.Count(item => item.IsOverdue) ?? 0;
        public double ProgressPercentage => TotalTasks > 0 ? (double)CompletedTasks / TotalTasks * 100 : 0;

        // Link-related properties
        public List<OrgLink> Links => ExtractLinksFromContent();
        public bool HasLinks => Links?.Any() ?? false;

        public OrgItem()
        {
            _level = 1;
            _state = OrgState.None;
            _priority = OrgPriority.None;
            _title = string.Empty;
            _content = string.Empty;
            _tags = new List<string>();
            _properties = new Dictionary<string, string>();
            _children = new ObservableCollection<OrgItem>();
            _created = DateTime.Now;
            _lastModified = DateTime.Now;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateLastModified()
        {
            LastModified = DateTime.Now;
        }

        public void AddChild(OrgItem child)
        {
            child.Parent = this;
            child.Level = Level + 1;
            child.FilePath = FilePath;
            Children.Add(child);
            OnPropertyChanged(nameof(HasChildren));
            UpdateLastModified();
        }

        public void RemoveChild(OrgItem child)
        {
            child.Parent = null;
            Children.Remove(child);
            OnPropertyChanged(nameof(HasChildren));
            UpdateLastModified();
        }

        public void AddTag(string tag)
        {
            if (!string.IsNullOrEmpty(tag) && !Tags.Contains(tag))
            {
                Tags.Add(tag);
                OnPropertyChanged(nameof(Tags));
                OnPropertyChanged(nameof(TagsDisplay));
                UpdateLastModified();
            }
        }

        public void RemoveTag(string tag)
        {
            if (Tags.Remove(tag))
            {
                OnPropertyChanged(nameof(Tags));
                OnPropertyChanged(nameof(TagsDisplay));
                UpdateLastModified();
            }
        }

        public void SetProperty(string key, string value)
        {
            if (Properties.ContainsKey(key))
            {
                Properties[key] = value;
            }
            else
            {
                Properties.Add(key, value);
            }
            OnPropertyChanged(nameof(Properties));
            UpdateLastModified();
        }

        public string GetProperty(string key)
        {
            return Properties.TryGetValue(key, out var value) ? value : null;
        }

        public void CycleState()
        {
            // Use the same configuration approach as OrgModeTab
            var configuredStates = GetConfiguredStates();
            var currentStateName = State.ToString();
            
            // Find current state index
            var currentIndex = Array.IndexOf(configuredStates, currentStateName);
            
            // If current state not found, start from beginning
            if (currentIndex == -1)
            {
                currentIndex = -1; // Will become 0 after increment
            }
            
            // Get next state (cycle back to first if at end)
            var nextIndex = (currentIndex + 1) % configuredStates.Length;
            var nextStateName = configuredStates[nextIndex];
            
            // Convert to enum and set
            if (Enum.TryParse<OrgState>(nextStateName, out var newState))
            {
                State = newState;
            }
            else
            {
                // Fallback to TODO if parsing fails
                State = OrgState.TODO;
            }
        }

        private string[] GetConfiguredStates()
        {
            // Try to get configuration from service locator
            try
            {
                var configService = ServiceLocator.Instance?.GetService<Core.Configuration.IConfigurationService>();
                if (configService != null)
                {
                    var todoStates = configService.Provider.OrgTodoStates?.ToArray() ?? new string[0];
                    var doneStates = configService.Provider.OrgDoneStates?.ToArray() ?? new string[0];
                    var noActionStates = configService.Provider.OrgNoActionStates?.ToArray() ?? new string[0];
                    
                    // Combine all states in order: todo -> no-action -> done
                    var allStates = todoStates.Concat(noActionStates).Concat(doneStates).ToArray();
                    
                    if (allStates.Length > 0)
                    {
                        return allStates;
                    }
                }
            }
            catch
            {
                // Fall back to defaults if configuration fails
            }
            
            // Default state sequence (same as OrgModeTab fallback)
            return new[] { "TODO", "STARTED", "DELEGATED", "SOMEDAY", "WAITING", "DONE", "CANCELLED" };
        }

        public void CyclePriority()
        {
            Priority = Priority switch
            {
                OrgPriority.None => OrgPriority.A,
                OrgPriority.A => OrgPriority.B,
                OrgPriority.B => OrgPriority.C,
                OrgPriority.C => OrgPriority.None,
                _ => OrgPriority.None
            };
        }

        /// <summary>
        /// Temporarily suppresses automatic CLOSED timestamp setting during state cycling
        /// </summary>
        public void SuppressAutoClosedTimestamp(bool suppress)
        {
            _suppressAutoClosedTimestamp = suppress;
        }

        public IEnumerable<OrgItem> GetAllDescendants()
        {
            foreach (var child in Children)
            {
                yield return child;
                foreach (var descendant in child.GetAllDescendants())
                {
                    yield return descendant;
                }
            }
        }

        public IEnumerable<OrgItem> GetIncompleteItems()
        {
            var items = new List<OrgItem>();
            if (!IsCompleted && State != OrgState.None)
            {
                items.Add(this);
            }
            items.AddRange(Children.SelectMany(c => c.GetIncompleteItems()));
            return items;
        }

        public int GetCompletionPercentage()
        {
            var allItems = GetAllDescendants().Where(i => i.State != OrgState.None).ToList();
            if (!allItems.Any()) return 0;
            
            var completedItems = allItems.Where(i => i.IsCompleted).Count();
            return (int)((double)completedItems / allItems.Count * 100);
        }

        // Link extraction method
        private List<OrgLink> ExtractLinksFromContent()
        {
            var links = new List<OrgLink>();
            if (string.IsNullOrEmpty(Title) && string.IsNullOrEmpty(Content))
                return links;

            var combinedText = $"{Title} {Content}";
            
            // Regex for org-mode links: [[link][description]] or [[link]]
            var linkPattern = @"\[\[([^\]]+)\](?:\[([^\]]*)\])?\]";
            var matches = System.Text.RegularExpressions.Regex.Matches(combinedText, linkPattern);

            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                var target = match.Groups[1].Value;
                var description = match.Groups[2].Success ? match.Groups[2].Value : target;
                
                links.Add(new OrgLink
                {
                    Target = target,
                    Description = description,
                    Type = DetermineLinkType(target)
                });
            }

            return links;
        }

        private OrgLinkType DetermineLinkType(string target)
        {
            if (target.StartsWith("http://") || target.StartsWith("https://"))
                return OrgLinkType.Web;
            if (target.StartsWith("file:"))
                return OrgLinkType.File;
            if (target.StartsWith("#"))
                return OrgLinkType.Internal;
            if (target.StartsWith("id:"))
                return OrgLinkType.Id;
            if (target.Contains("::"))
                return OrgLinkType.FileWithTarget;
            if (System.IO.Path.HasExtension(target))
                return OrgLinkType.File;
            
            return OrgLinkType.Internal;
        }
    }

    // Supporting classes for links
    public class OrgLink
    {
        public string Target { get; set; }
        public string Description { get; set; }
        public OrgLinkType Type { get; set; }
    }

    public enum OrgLinkType
    {
        Web,           // [[https://example.com]]
        File,          // [[file:path/to/file.txt]] or [[./document.md]]
        Internal,      // [[#heading]] or [[Custom Target]]
        Id,            // [[id:some-uuid]]
        FileWithTarget // [[file:path/to/file.org::*Heading]]
    }
} 