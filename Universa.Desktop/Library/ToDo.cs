using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Universa.Desktop.Library
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
        private string _recurrenceUnit;  // "hour", "day", "week", "month", "year"

        public string Title
        {
            get => _title;
            set
            {
                if (_title != value)
                {
                    _title = value;
                    OnPropertyChanged(nameof(Title));
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
                    OnPropertyChanged(nameof(Description));
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
                    OnPropertyChanged(nameof(StartDate));
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
                    OnPropertyChanged(nameof(DueDate));
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
                    OnPropertyChanged(nameof(CompletedDate));
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
                    OnPropertyChanged(nameof(IsCompleted));
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
                    OnPropertyChanged(nameof(IsExpanded));
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
                    OnPropertyChanged(nameof(HasSubTask));
                }
            }
        }

        public ObservableCollection<ToDo> SubTasks
        {
            get => _subTasks;
            set
            {
                if (_subTasks != value)
                {
                    _subTasks = value;
                    OnPropertyChanged(nameof(SubTasks));
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
                    OnPropertyChanged(nameof(FilePath));
                }
            }
        }

        public List<string> Tags
        {
            get => _tags;
            set
            {
                if (_tags != value)
                {
                    _tags = value;
                    OnPropertyChanged(nameof(Tags));
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
                    OnPropertyChanged(nameof(IsRecurring));
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
                    OnPropertyChanged(nameof(RecurrenceInterval));
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
                    OnPropertyChanged(nameof(RecurrenceUnit));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ToDo()
        {
            _title = string.Empty;
            _description = string.Empty;
            _subTasks = new ObservableCollection<ToDo>();
            _tags = new List<string>();
            _recurrenceUnit = "day";  // Default to daily recurrence
            _recurrenceInterval = 1;   // Default to 1 unit interval
        }
    }
} 