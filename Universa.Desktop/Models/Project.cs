using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using Universa.Desktop.Library;
using System.Collections.ObjectModel;

namespace Universa.Desktop.Models
{
    public enum ProjectStatus
    {
        NotStarted,
        NotReady,
        Started,
        Deferred,
        Completed
    }

    public class LogEntry
    {
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class RelativeReference
    {
        public string RelativePath { get; set; }
        public string Description { get; set; }
    }

    public class Project : INotifyPropertyChanged
    {
        private string _title;
        private string _goal;
        private string _category;
        private DateTime _createdDate;
        private DateTime? _dueDate;
        private DateTime _lastModifiedDate;
        private DateTime? _startDate;
        private DateTime? _completedDate;
        private ObservableCollection<ProjectTask> _tasks;
        private List<Library.ProjectDependency> _dependencies;
        private List<RelativeReference> _relativeReferences;
        private string _filePath;
        private ProjectStatus _status;
        private ObservableCollection<LogEntry> _logEntries;

        public Project()
        {
            CreatedDate = DateTime.Now;
            LastModifiedDate = DateTime.Now;
            Tasks = new ObservableCollection<ProjectTask>();
            Dependencies = new List<Library.ProjectDependency>();
            RelativeReferences = new List<RelativeReference>();
            LogEntries = new ObservableCollection<LogEntry>();
            Status = ProjectStatus.NotStarted;
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
                }
            }
        }

        public string Goal
        {
            get => _goal;
            set
            {
                if (_goal != value)
                {
                    _goal = value;
                    OnPropertyChanged();
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

        public DateTime? DueDate
        {
            get => _dueDate;
            set
            {
                if (_dueDate != value)
                {
                    _dueDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public DateTime LastModifiedDate
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

        public DateTime? StartDate
        {
            get => _startDate;
            set
            {
                if (_startDate != value)
                {
                    _startDate = value;
                    OnPropertyChanged();
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
                }
            }
        }

        public ObservableCollection<ProjectTask> Tasks
        {
            get => _tasks;
            set
            {
                if (_tasks != value)
                {
                    _tasks = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<Library.ProjectDependency> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies != value)
                {
                    _dependencies = value;
                    OnPropertyChanged();
                }
            }
        }

        public List<RelativeReference> RelativeReferences
        {
            get => _relativeReferences;
            set
            {
                if (_relativeReferences != value)
                {
                    _relativeReferences = value;
                    OnPropertyChanged();
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

        public ProjectStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    // Check if we need to prevent the status change
                    if (value == ProjectStatus.Started)
                    {
                        if (!CanStart())
                        {
                            var unfinishedDeps = Dependencies
                                .Where(d => d.IsHardDependency)
                                .Select(d => GetDependencyDisplayName(d.FilePath))
                                .Where(name => !string.IsNullOrEmpty(name));
                                
                            throw new InvalidOperationException(
                                $"Cannot start project: The following hard dependencies must be completed first:\n" +
                                string.Join("\n", unfinishedDeps));
                        }
                    }

                    // If we're adding a hard dependency and we're not already in NotReady status,
                    // check if we need to switch to NotReady
                    if (_status != ProjectStatus.NotReady && !CanStart())
                    {
                        _status = ProjectStatus.NotReady;
                    }
                    else
                    {
                        _status = value;
                        
                        // Set StartDate when project is started
                        if (value == ProjectStatus.Started && !StartDate.HasValue)
                        {
                            StartDate = DateTime.Now;
                        }
                        // Set CompletedDate when project is completed
                        else if (value == ProjectStatus.Completed)
                        {
                            CompletedDate = DateTime.Now;
                        }
                        else if (value != ProjectStatus.Completed)
                        {
                            CompletedDate = null;
                        }
                    }
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<LogEntry> LogEntries
        {
            get => _logEntries;
            set
            {
                if (_logEntries != value)
                {
                    _logEntries = value;
                    OnPropertyChanged();
                }
            }
        }

        private string GetDependencyDisplayName(string filePath)
        {
            // Check if dependency is a project
            var project = Library.ProjectTracker.Instance.GetAllProjects()
                .FirstOrDefault(p => p.FilePath == filePath);
            if (project != null)
            {
                return $"Project: {project.Title}";
            }

            // Check if dependency is a todo
            var todo = Library.ToDoTracker.Instance.GetAllTodos()
                .FirstOrDefault(t => t.FilePath == filePath);
            if (todo != null)
            {
                return $"ToDo: {todo.Title}";
            }

            return null;
        }

        public bool CanStart()
        {
            // Check if all hard dependencies are completed
            foreach (var dependency in Dependencies.Where(d => d.IsHardDependency))
            {
                // Check if dependency is a project
                var project = Library.ProjectTracker.Instance.GetAllProjects()
                    .FirstOrDefault(p => p.FilePath == dependency.FilePath);
                if (project != null && project.Status != ProjectStatus.Completed)
                {
                    return false;
                }

                // Check if dependency is a todo
                var todo = Library.ToDoTracker.Instance.GetAllTodos()
                    .FirstOrDefault(t => t.FilePath == dependency.FilePath);
                if (todo != null && !todo.IsCompleted)
                {
                    return false;
                }
            }
            return true;
        }

        public bool HasUnfinishedDependencies()
        {
            foreach (var dependency in Dependencies)
            {
                // Check if dependency is a project
                var project = Library.ProjectTracker.Instance.GetAllProjects()
                    .FirstOrDefault(p => p.FilePath == dependency.FilePath);
                if (project != null && project.Status != ProjectStatus.Completed)
                {
                    return true;
                }

                // Check if dependency is a todo
                var todo = Library.ToDoTracker.Instance.GetAllTodos()
                    .FirstOrDefault(t => t.FilePath == dependency.FilePath);
                if (todo != null && !todo.IsCompleted)
                {
                    return true;
                }
            }
            return false;
        }

        public void AddDependency(Library.ProjectDependency dependency)
        {
            _dependencies.Add(dependency);
            // If it's a hard dependency and we're not in NotReady status, check if we need to switch
            if (dependency.IsHardDependency && _status != ProjectStatus.NotReady && !CanStart())
            {
                Status = ProjectStatus.NotReady;
            }
            OnPropertyChanged(nameof(Dependencies));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class ProjectTask : INotifyPropertyChanged
    {
        private string _title;
        private string _description;
        private bool _isCompleted;
        private DateTime? _startDate;
        private DateTime? _dueDate;
        private ObservableCollection<ProjectTask> _subtasks;
        private ObservableCollection<string> _dependencies;

        public ProjectTask()
        {
            _subtasks = new ObservableCollection<ProjectTask>();
            _dependencies = new ObservableCollection<string>();
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
                    OnPropertyChanged();
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
                }
            }
        }

        public ObservableCollection<ProjectTask> Subtasks
        {
            get => _subtasks;
            set
            {
                if (_subtasks != value)
                {
                    _subtasks = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<string> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies != value)
                {
                    _dependencies = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 