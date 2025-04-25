using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;

namespace Universa.Desktop.Models
{
    public class ToDo : INotifyPropertyChanged
    {
        private string _title;
        private string _description;
        private DateTime? _startDate;
        private DateTime? _dueDate;
        private DateTime? _completedDate;
        private bool _isCompleted;
        private bool _isExpanded;
        private bool _hasSubTask;
        private ObservableCollection<ToDo> _subTasks;
        private string _filePath;
        private List<string> _tags;
        private string _subTask;
        private bool _isRecurring;
        private int _recurrenceInterval;
        private string _recurrenceUnit;
        private string _priority;
        private string _category;
        private string _assignedTo;
        private string _notes;
        private DateTime _createdDate;
        private DateTime? _lastModifiedDate;
        private string _parentId;
        private string _id;

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

        public string ParentId
        {
            get => _parentId;
            set
            {
                if (_parentId != value)
                {
                    _parentId = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Title
        {
            get => _title;
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

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                {
                    _description = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate != value)
                {
                    _startDate = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public DateTime? DueDate
        {
            get => _dueDate;
            set
            {
                if (_dueDate != value)
                {
                    _dueDate = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public DateTime? CompletedDate
        {
            get => _completedDate;
            set
            {
                if (_completedDate != value)
                {
                    _completedDate = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            set
            {
                if (_isCompleted != value)
                {
                    _isCompleted = value;
                    if (value)
                    {
                        CompletedDate = DateTime.Now;
                    }
                    else
                    {
                        CompletedDate = null;
                    }
                    OnPropertyChanged();
                    UpdateLastModified();
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

        public bool HasSubTask
        {
            get => _hasSubTask;
            set
            {
                if (_hasSubTask != value)
                {
                    _hasSubTask = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<ToDo> SubTasks
        {
            get => _subTasks ??= new ObservableCollection<ToDo>();
            set
            {
                if (_subTasks != value)
                {
                    _subTasks = value;
                    OnPropertyChanged();
                    UpdateLastModified();
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

        public List<string> Tags
        {
            get => _tags ??= new List<string>();
            set
            {
                if (_tags != value)
                {
                    _tags = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string SubTask
        {
            get => _subTask;
            set
            {
                if (_subTask != value)
                {
                    _subTask = value;
                    _hasSubTask = !string.IsNullOrEmpty(value);
                    OnPropertyChanged(nameof(SubTask));
                    OnPropertyChanged(nameof(HasSubTask));
                    UpdateLastModified();
                }
            }
        }

        public bool IsRecurring
        {
            get => _isRecurring;
            set
            {
                if (_isRecurring != value)
                {
                    _isRecurring = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public int RecurrenceInterval
        {
            get => _recurrenceInterval;
            set
            {
                if (_recurrenceInterval != value)
                {
                    _recurrenceInterval = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string RecurrenceUnit
        {
            get => _recurrenceUnit;
            set
            {
                if (_recurrenceUnit != value)
                {
                    _recurrenceUnit = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string Priority
        {
            get => _priority;
            set
            {
                if (_priority != value)
                {
                    _priority = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string Category
        {
            get => _category;
            set
            {
                if (_category != value)
                {
                    _category = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string AssignedTo
        {
            get => _assignedTo;
            set
            {
                if (_assignedTo != value)
                {
                    _assignedTo = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public string Notes
        {
            get => _notes;
            set
            {
                if (_notes != value)
                {
                    _notes = value;
                    OnPropertyChanged();
                    UpdateLastModified();
                }
            }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set
            {
                if (_createdDate != value)
                {
                    _createdDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime? LastModifiedDate
        {
            get => _lastModifiedDate;
            set
            {
                if (_lastModifiedDate != value)
                {
                    _lastModifiedDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public ToDo()
        {
            _title = string.Empty;
            _description = string.Empty;
            _subTasks = new ObservableCollection<ToDo>();
            _tags = new List<string>();
            _recurrenceUnit = "day";
            _recurrenceInterval = 1;
            _createdDate = DateTime.Now;
            _lastModifiedDate = DateTime.Now;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void UpdateLastModified()
        {
            LastModifiedDate = DateTime.Now;
        }

        public void AddSubTask(ToDo subTask)
        {
            subTask.ParentId = Id;
            SubTasks.Add(subTask);
            HasSubTask = true;
            UpdateLastModified();
        }

        public void RemoveSubTask(ToDo subTask)
        {
            SubTasks.Remove(subTask);
            HasSubTask = SubTasks.Any();
            UpdateLastModified();
        }

        public void AddTag(string tag)
        {
            if (!Tags.Contains(tag))
            {
                Tags.Add(tag);
                UpdateLastModified();
            }
        }

        public void RemoveTag(string tag)
        {
            if (Tags.Remove(tag))
            {
                UpdateLastModified();
            }
        }

        public bool HasTag(string tag)
        {
            return Tags.Contains(tag);
        }

        public bool IsOverdue => !IsCompleted && DueDate.HasValue && DueDate.Value < DateTime.Now;
        public bool IsDueToday => !IsCompleted && DueDate.HasValue && DueDate.Value.Date == DateTime.Today;
        public bool IsDueThisWeek => !IsCompleted && DueDate.HasValue && 
            DueDate.Value.Date >= DateTime.Today && 
            DueDate.Value.Date <= DateTime.Today.AddDays(7);
    }
} 