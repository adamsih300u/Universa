using System;
using System.ComponentModel;
using System.Collections.Generic;

namespace Universa.Desktop.Models
{
    public class ToDoItem : INotifyPropertyChanged
    {
        private bool _isCompleted;
        private string _title;
        private DateTime? _startDate;
        private DateTime? _dueDate;
        private string[] _additionalInfo = new string[5];
        private bool _isExpanded;
        private string _sourceFile;
        private List<string> _tags = new List<string>();

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

        public string[] AdditionalInfo
        {
            get => _additionalInfo;
            set
            {
                if (_additionalInfo != value)
                {
                    _additionalInfo = value;
                    OnPropertyChanged(nameof(AdditionalInfo));
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

        public string SourceFile
        {
            get => _sourceFile;
            set
            {
                if (_sourceFile != value)
                {
                    _sourceFile = value;
                    OnPropertyChanged(nameof(SourceFile));
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
                    _tags = value ?? new List<string>();
                    OnPropertyChanged(nameof(Tags));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 