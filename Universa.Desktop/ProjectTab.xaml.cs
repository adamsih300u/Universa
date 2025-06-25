using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using Universa.Desktop.Dialogs;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Text;
using System.Diagnostics;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;

namespace Universa.Desktop
{
    public partial class ProjectTab : UserControl, INotifyPropertyChanged, IFileTab
    {
        public int LastKnownCursorPosition { get; private set; } = 0;
        
        private Project _project;
        private string _filePath;
        private bool _isModified;
        private Point _dragStartPoint;
        private bool _isDragging;
        private ProjectTask _draggedTask;
        private ObservableCollection<Models.DependencyItem> _dependencies;
        private bool _isContentLoaded = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                Title = Path.GetFileName(value);
            }
        }

        public string Title
        {
            get => _project?.Title;
            set
            {
                if (_project != null && _project.Title != value)
                {
                    _project.Title = value;
                    OnPropertyChanged(nameof(Title));
                    IsModified = true;
                }
            }
        }

        public bool IsModified
        {
            get => _isModified;
            set
            {
                if (_isModified != value)
                {
                    _isModified = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsModified)));
                }
            }
        }

        public Project Project => _project;

        public string Goal
        {
            get => _project?.Goal;
            set
            {
                if (_project != null && _project.Goal != value)
                {
                    _project.Goal = value;
                    OnPropertyChanged();
                    IsModified = true;
                }
            }
        }

        public ObservableCollection<Models.DependencyItem> Dependencies
        {
            get => _dependencies;
            set
            {
                if (_dependencies != value)
                {
                    _dependencies = value;
                    OnPropertyChanged(nameof(Dependencies));
                }
            }
        }

        public ProjectTab(string filePath)
        {
            InitializeComponent();
            FilePath = filePath;
            DataContext = this;

            // Initialize status combo box
            StatusComboBox.ItemsSource = Enum.GetValues(typeof(ProjectStatus));

            // Subscribe to ProjectTracker events
            ProjectTracker.Instance.ProjectsChanged += OnProjectsChanged;
            ProjectTracker.Instance.CategoriesChanged += OnCategoriesChanged;

            LoadFile(filePath);
            UpdateDependenciesList();
        }

        private void OnProjectsChanged()
        {
            UpdateDependenciesList();
        }

        private void OnCategoriesChanged()
        {
            // Categories feature removed
        }

        private void UpdateCategoryComboBox()
        {
            // Categories feature removed
        }

        private void UpdateDependenciesList()
        {
            if (_project == null) return;

            // Filter dependencies for the ComboBoxes
            var hardDependencies = _project.Dependencies.Where(d => d.IsHardDependency).ToList();
            var softDependencies = _project.Dependencies.Where(d => !d.IsHardDependency).ToList();

            // Update the ItemsSource for each ComboBox
            HardDependenciesComboBox.ItemsSource = hardDependencies;
            SoftDependenciesComboBox.ItemsSource = softDependencies;
        }

        private void UpdateReferencesList()
        {
            // No need to manually update since we're using direct binding to Project.RelativeReferences
        }

        public void LoadFile(string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                _project = JsonSerializer.Deserialize<Project>(content, options);
                _project.FilePath = filePath;
                
                // Initialize collections if they're null
                if (_project.Dependencies == null)
                    _project.Dependencies = new List<ProjectDependency>();
                if (_project.RelativeReferences == null)
                    _project.RelativeReferences = new List<RelativeReference>();
                if (_project.LogEntries == null)
                    _project.LogEntries = new ObservableCollection<LogEntry>();
                if (_project.Tasks == null)
                    _project.Tasks = new ObservableCollection<ProjectTask>();

                Title = _project.Title;
                _filePath = filePath;

                // Set up the status combo box
                StatusComboBox.ItemsSource = Enum.GetValues(typeof(ProjectStatus));
                StatusComboBox.SelectedItem = _project.Status;

                // Set up tasks
                TasksItemsControl.ItemsSource = _project.Tasks;
                foreach (var task in _project.Tasks)
                {
                    SubscribeToTaskEvents(task);
                }

                DataContext = this;
                IsModified = false;
                _isContentLoaded = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading project file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SubscribeToTaskEvents(ProjectTask task)
        {
            task.PropertyChanged += Task_PropertyChanged;
            if (task.Subtasks != null)
            {
                foreach (var subtask in task.Subtasks)
                {
                    SubscribeToTaskEvents(subtask);
                }
            }
        }

        private void Task_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ProjectTask.IsCompleted))
            {
                var task = sender as ProjectTask;
                if (task != null)
                {
                    // Update subtasks if this is a parent task
                    if (task.Subtasks != null)
                    {
                        foreach (var subtask in task.Subtasks)
                        {
                            subtask.IsCompleted = task.IsCompleted;
                        }
                    }
                    
                    // Mark the project as modified
                    IsModified = true;
                    
                    // Force save to ensure changes are persisted
                    SaveFile();
                }
            }
            else
            {
                // For other property changes, just mark as modified
                IsModified = true;
            }
        }

        private void Task_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox)
            {
                var task = checkBox.DataContext as ProjectTask;
                if (task != null)
                {
                    // Update the IsCompleted property (this will trigger Task_PropertyChanged)
                    task.IsCompleted = checkBox.IsChecked ?? false;
                    
                    // Save the changes
                    SaveFile();
                }
            }
        }

        private void SaveFile()
        {
            if (string.IsNullOrEmpty(_filePath)) return;
            
            if (_project != null)
            {
                // No need to create new collection, use existing one
                _project.LastModifiedDate = DateTime.Now;
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
                };
                var json = JsonSerializer.Serialize(_project, options);
                File.WriteAllText(_filePath, json);
                IsModified = false;

                // Notify ProjectTracker that the file has changed
                var libraryPath = Configuration.Instance.LibraryPath;
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    ProjectTracker.Instance.ScanProjectFiles(libraryPath);
                }
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveFile();
        }

        private void AddTask_Click(object sender, RoutedEventArgs e)
        {
            // Ensure the tasks section is expanded
            TasksExpandButton.IsChecked = true;

            var newTask = new ProjectTask 
            { 
                Title = "New Task"
                // StartDate and DueDate are now optional and not set by default
            };
            SubscribeToTaskEvents(newTask);
            
            // Add to the project's tasks collection
            if (_project.Tasks == null)
            {
                _project.Tasks = new ObservableCollection<ProjectTask>();
            }
            _project.Tasks.Add(newTask);
            IsModified = true;
        }

        private void AddSubtask_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is ProjectTask parentTask)
            {
                var newSubtask = new ProjectTask 
                { 
                    Title = "New Subtask"
                    // StartDate and DueDate are now optional and not set by default
                };
                SubscribeToTaskEvents(newSubtask);
                
                if (parentTask.Subtasks == null)
                {
                    parentTask.Subtasks = new ObservableCollection<ProjectTask>();
                }
                parentTask.Subtasks.Add(newSubtask);
                IsModified = true;
            }
        }

        private void RemoveSubtask_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is ProjectTask subtask)
            {
                // Find the parent task
                var parentTask = _project.Tasks.FirstOrDefault(t => t.Subtasks?.Contains(subtask) == true);
                if (parentTask?.Subtasks != null)
                {
                    parentTask.Subtasks.Remove(subtask);
                    IsModified = true;
                }
            }
        }

        private void LoadDependencies()
        {
            var dependencies = new List<Models.DependencyItem>();
            
            // Add projects (except current)
            var projects = ProjectTracker.Instance.GetAllProjects()
                .Where(p => p.FilePath != _filePath)
                .Select(p => new Models.DependencyItem 
                { 
                    FilePath = p.FilePath,
                    DisplayName = $"Project: {p.Title}",
                    Type = DependencyType.Project
                });
            dependencies.AddRange(projects);

            // Add ToDos from ToDoTracker
            System.Diagnostics.Debug.WriteLine("Loading ToDos for dependencies");
            var todos = ToDoTracker.Instance.GetAllTodos();
            foreach (var todo in todos)
            {
                if (todo != null && !string.IsNullOrEmpty(todo.Title))
                {
                    dependencies.Add(new Models.DependencyItem
                    {
                        FilePath = todo.FilePath,
                        DisplayName = $"ToDo: {todo.Title}",
                        Type = DependencyType.ToDo
                    });
                    System.Diagnostics.Debug.WriteLine($"Added dependency: {todo.Title}");
                }
            }

            Dependencies.Clear();
            foreach (var dependency in dependencies)
            {
                Dependencies.Add(dependency);
            }
        }

        private List<Models.DependencyItem> GetAvailableTaskDependencies(ProjectTask currentTask)
        {
            var dependencies = new List<Models.DependencyItem>();
            
            // Add external dependencies (projects and todos)
            LoadDependencies();

            // Add internal tasks (except the current task and its subtasks)
            var allProjectTasks = GetAllTasksRecursive(_project.Tasks);
            var currentTaskAndSubtasks = GetAllTasksRecursive(new[] { currentTask });
            
            var availableInternalTasks = allProjectTasks
                .Except(currentTaskAndSubtasks)
                .Select(t => new Models.DependencyItem 
                { 
                    FilePath = $"{_filePath}#{t.Title}",  // Use a special format for internal task references
                    DisplayName = $"Task: {t.Title}",
                    Type = DependencyType.Task,
                    Task = t
                });
            
            dependencies.AddRange(availableInternalTasks);

            return dependencies;
        }

        private IEnumerable<ProjectTask> GetAllTasksRecursive(IEnumerable<ProjectTask> tasks)
        {
            if (tasks == null) return Enumerable.Empty<ProjectTask>();
            
            return tasks.Concat(tasks.SelectMany(t => GetAllTasksRecursive(t.Subtasks ?? Enumerable.Empty<ProjectTask>())));
        }

        private void AddHardDependency_Click(object sender, RoutedEventArgs e)
        {
            if (_project != null)
            {
                var availableDependencies = GetDependencyOptions();
                var dialog = new DependencyDialog(availableDependencies);

                if (dialog.ShowDialog() == true && dialog.SelectedDependency != null)
                {
                    var newDependency = new ProjectDependency
                    {
                        FilePath = dialog.SelectedDependency.FilePath,
                        IsHardDependency = true
                    };
                    _project.Dependencies.Add(newDependency);
                    IsModified = true;

                    // Check if we need to update status
                    if (_project.HasUnfinishedDependencies() && _project.Status != ProjectStatus.NotReady)
                    {
                        _project.Status = ProjectStatus.NotReady;
                    }
                }
            }
        }

        private void AddSoftDependency_Click(object sender, RoutedEventArgs e)
        {
            if (_project != null)
            {
                var availableDependencies = GetDependencyOptions();
                var dialog = new DependencyDialog(availableDependencies);

                if (dialog.ShowDialog() == true && dialog.SelectedDependency != null)
                {
                    var newDependency = new ProjectDependency
                    {
                        FilePath = dialog.SelectedDependency.FilePath,
                        IsHardDependency = false
                    };
                    _project.Dependencies.Add(newDependency);
                    IsModified = true;
                }
            }
        }

        private void AddTaskDependency_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is ProjectTask task)
            {
                var availableDependencies = GetAvailableTaskDependencies(task);
                var dialog = new DependencyDialog(availableDependencies);

                if (dialog.ShowDialog() == true && dialog.SelectedDependency != null)
                {
                    task.Dependencies.Add(dialog.SelectedDependency.FilePath);
                    IsModified = true;
                }
            }
        }

        private string GetDependencyDisplayName(string filePath)
        {
            // Check if it's an internal task reference
            if (filePath.StartsWith($"{_filePath}#"))
            {
                var taskName = filePath.Substring(filePath.IndexOf('#') + 1);
                return $"Task: {taskName}";
            }

            // Check if it's a task from another project
            if (filePath.Contains("#"))
            {
                var projectPath = filePath.Substring(0, filePath.IndexOf('#'));
                var taskName = filePath.Substring(filePath.IndexOf('#') + 1);
                
                var project = ProjectTracker.Instance.GetAllProjects()
                    .FirstOrDefault(p => p.FilePath == projectPath);
                
                if (project != null)
                {
                    return $"Task: {taskName} (in {project.Title})";
                }
            }

            // Check if it's a project
            var projectRef = ProjectTracker.Instance.GetAllProjects()
                .FirstOrDefault(p => p.FilePath == filePath);
            if (projectRef != null)
            {
                return $"Project: {projectRef.Title}";
            }

            // Check if it's a todo
            var todo = ToDoTracker.Instance.GetAllTodos()
                .FirstOrDefault(t => t.FilePath == filePath);
            if (todo != null)
            {
                var todoFileName = Path.GetFileNameWithoutExtension(todo.FilePath);
                return $"ToDo: {todo.Title} (in {todoFileName})";
            }

            return Path.GetFileName(filePath);
        }

        private void RemoveTaskDependency_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button button) || !(button.DataContext is string dependency))
            {
                return;
            }

            var parentElement = VisualTreeHelper.GetParent(button);
            while (parentElement != null && !(parentElement is FrameworkElement element && element.DataContext is ProjectTask))
            {
                parentElement = VisualTreeHelper.GetParent(parentElement);
            }

            if (parentElement is FrameworkElement parentFrameworkElement && 
                parentFrameworkElement.DataContext is ProjectTask task &&
                task.Dependencies != null)
            {
                task.Dependencies.Remove(dependency);
                IsModified = true;
            }
        }

        private void DeleteTask_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            if (menuItem?.DataContext is ProjectTask task)
            {
                var message = task.Subtasks != null && task.Subtasks.Any() 
                    ? $"Do you want to delete '{task.Title}' and its subtasks?"
                    : $"Do you want to delete '{task.Title}'?";

                var result = MessageBox.Show(message, "Delete Task", 
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _project.Tasks.Remove(task);
                    IsModified = true;
                }
            }
        }

        private void DeleteSubtask_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var textBox = ((ContextMenu)menuItem.Parent).PlacementTarget as TextBox;
            if (textBox?.DataContext is ProjectTask subtask)
            {
                var result = MessageBox.Show($"Do you want to delete '{subtask.Title}'?", 
                    "Delete Subtask", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Find the parent task
                    var parentTask = _project.Tasks.FirstOrDefault(t => t.Subtasks?.Contains(subtask) == true);
                    if (parentTask?.Subtasks != null)
                    {
                        parentTask.Subtasks.Remove(subtask);
                        IsModified = true;
                    }
                }
            }
        }

        private void RemoveDependency_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var button = sender as Button;
                var item = button?.DataContext as DependencyDisplayItem;
                
                if (button == null || item == null || _project?.Dependencies == null)
                {
                    System.Diagnostics.Debug.WriteLine("RemoveDependency_Click: Null check failed");
                    System.Diagnostics.Debug.WriteLine($"Button: {button != null}, Item: {item != null}, Project: {_project != null}, Dependencies: {_project?.Dependencies != null}");
                    return;
                }

                System.Diagnostics.Debug.WriteLine($"Removing dependency: {item.FilePath}");
                var dependency = _project.Dependencies.FirstOrDefault(d => d != null && d.FilePath == item.FilePath);
                
                if (dependency != null)
                {
                    _project.Dependencies.Remove(dependency);
                    UpdateDependenciesList();
                    IsModified = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Could not find matching dependency to remove");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RemoveDependency_Click: {ex}");
            }
        }

        private void ViewDependencies_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button?.DataContext is ProjectTask task && task.Dependencies != null && task.Dependencies.Any())
            {
                var dependencyList = new System.Text.StringBuilder();
                dependencyList.AppendLine("Task Dependencies:");
                dependencyList.AppendLine();

                foreach (var dependency in task.Dependencies)
                {
                    dependencyList.AppendLine($"• {GetDependencyDisplayName(dependency)}");
                }

                MessageBox.Show(dependencyList.ToString(), 
                    $"Dependencies for {task.Title}", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Information);
            }
        }

        public void Reload()
        {
            LoadFile(_filePath);
        }

        public async Task<bool> Save()
        {
            try
            {
                await Task.Run(() => SaveFile());
                return true;
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error saving project: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return false;
            }
        }

        public async Task<bool> SaveAs(string newPath)
        {
            try
            {
                var oldPath = _filePath;
                _filePath = newPath;
                
                if (await Save())
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Update the project's file path
                        if (_project != null)
                        {
                            _project.FilePath = newPath;
                        }
                        
                        // Update the title
                        Title = Path.GetFileName(newPath);
                    });
                    
                    return true;
                }
                else
                {
                    _filePath = oldPath; // Restore the old path if save failed
                    return false;
                }
            }
            catch (Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error saving project as {newPath}: {ex.Message}", "Save As Error", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                return false;
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Task_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _dragStartPoint = e.GetPosition(null);
                _draggedTask = element.DataContext as ProjectTask;
            }
        }

        private void Task_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging && _draggedTask != null)
            {
                Point position = e.GetPosition(null);
                
                // Check if mouse has moved far enough to start drag
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    try 
                    {
                        StartDrag(sender as DependencyObject);
                    }
                    catch (InvalidOperationException)
                    {
                        // Ignore drag operation if dispatcher is busy
                        _isDragging = false;
                        _draggedTask = null;
                    }
                }
            }
        }

        private void StartDrag(DependencyObject dragSource)
        {
            if (dragSource == null) return;
            
            try
            {
                _isDragging = true;
                DragDrop.DoDragDrop(dragSource, _draggedTask, DragDropEffects.Move);
            }
            finally
            {
                _isDragging = false;
                _draggedTask = null;
            }
        }

        private void Task_DragEnter(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(2);
                border.BorderBrush = Application.Current.Resources["AccentColor"] as Brush;
            }
        }

        private void Task_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(1);
                border.BorderBrush = Application.Current.Resources["BorderBrush"] as Brush;
            }
        }

        private void Task_Drop(object sender, DragEventArgs e)
        {
            if (sender is FrameworkElement element && 
                element.DataContext is ProjectTask dropTarget && 
                e.Data.GetData(typeof(ProjectTask)) is ProjectTask draggedTask)
            {
                int draggedIndex = _project.Tasks.IndexOf(draggedTask);
                int targetIndex = _project.Tasks.IndexOf(dropTarget);

                if (draggedIndex != -1 && targetIndex != -1)
                {
                    _project.Tasks.Move(draggedIndex, targetIndex);
                    IsModified = true;
                }
            }

            // Reset border
            if (sender is Border border)
            {
                border.BorderThickness = new Thickness(1);
                border.BorderBrush = Application.Current.Resources["BorderBrush"] as Brush;
            }
        }

        private void SaveProject()
        {
            SaveFile();
        }

        private async void AddReference_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Reference File",
                Filter = "All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Get the path relative to the project file
                    string projectDir = Path.GetDirectoryName(_project.FilePath);
                    string relativePath = Path.GetRelativePath(projectDir, dialog.FileName);

                    // Prompt for description
                    var descriptionDialog = new TextInputDialog("Enter Reference Description", "Description (optional):");
                    string description = "";
                    if (descriptionDialog.ShowDialog() == true)
                    {
                        description = descriptionDialog.Result;
                    }

                    _project.RelativeReferences.Add(new RelativeReference
                    {
                        RelativePath = relativePath,
                        Description = description
                    });

                    SaveProject();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error adding reference: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void RemoveReference_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var reference = (RelativeReference)button.DataContext;
            _project.RelativeReferences.Remove(reference);
            SaveProject();
        }

        private void AddLogEntry_Click(object sender, RoutedEventArgs e)
        {
            var content = NewLogEntryTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(content))
            {
                if (_project.LogEntries == null)
                {
                    _project.LogEntries = new ObservableCollection<LogEntry>();
                }

                var logEntry = new LogEntry
                {
                    Content = content,
                    Timestamp = DateTime.Now
                };
                _project.LogEntries.Add(logEntry);
                NewLogEntryTextBox.Clear();
                IsModified = true;
                SaveFile();
            }
        }

        private void OpenReference_Click(object sender, RoutedEventArgs e)
        {
            var button = (Button)sender;
            var reference = (RelativeReference)button.DataContext;

            try
            {
                string projectDir = Path.GetDirectoryName(_project.FilePath);
                string fullPath = Path.GetFullPath(Path.Combine(projectDir, reference.RelativePath));

                if (File.Exists(fullPath))
                {
                    // Try to open the file in Universa first
                    var fileExtension = Path.GetExtension(fullPath).ToLower();
                    if (fileExtension == ".md" || fileExtension == ".todo" || fileExtension == ".project")
                    {
                        var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                        mainWindow?.OpenFileInEditor(fullPath);
                    }
                    else
                    {
                        // If not a Universa file type, open with default system application
                        var startInfo = new ProcessStartInfo
                        {
                            FileName = fullPath,
                            UseShellExecute = true
                        };
                        Process.Start(startInfo);
                    }
                }
                else
                {
                    MessageBox.Show($"Referenced file not found: {fullPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening reference: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add TextInputDialog class
        private class TextInputDialog : Window
        {
            private TextBox _inputTextBox;
            public string Result { get; private set; }

            public TextInputDialog(string title, string prompt)
            {
                Title = title;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Width = 400;
                Height = 150;
                ResizeMode = ResizeMode.NoResize;

                var grid = new Grid { Margin = new Thickness(10) };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var promptText = new TextBlock
                {
                    Text = prompt,
                    Margin = new Thickness(0, 0, 0, 5)
                };
                Grid.SetRow(promptText, 0);

                _inputTextBox = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, 10)
                };
                Grid.SetRow(_inputTextBox, 1);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };
                Grid.SetRow(buttonPanel, 2);

                var okButton = new Button
                {
                    Content = "OK",
                    Width = 75,
                    Height = 23,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                okButton.Click += (s, e) => { Result = _inputTextBox.Text; DialogResult = true; };

                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 75,
                    Height = 23,
                    IsCancel = true
                };

                buttonPanel.Children.Add(okButton);
                buttonPanel.Children.Add(cancelButton);

                grid.Children.Add(promptText);
                grid.Children.Add(_inputTextBox);
                grid.Children.Add(buttonPanel);

                Content = grid;
            }
        }

        private void StatusComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && _project != null)
            {
                var newStatus = (ProjectStatus)comboBox.SelectedItem;
                if (newStatus == ProjectStatus.Completed && _project.Status != ProjectStatus.Completed)
                {
                    // Archive the project when it's marked as completed
                    ArchiveProject();
                }
                _project.Status = newStatus;
                IsModified = true;
                SaveFile();
            }
        }

        private void ArchiveProject()
        {
            try
            {
                // Create archive file path
                var archivePath = Path.ChangeExtension(FilePath, ".project.archive");
                
                // Save the project to the archive file
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNameCaseInsensitive = true
                };
                
                var archiveContent = JsonSerializer.Serialize(_project, options);
                File.WriteAllText(archivePath, archiveContent);
                
                // Delete the original file since it's now archived
                File.Delete(FilePath);
                
                // Update the project's file path to point to the archive
                _project.FilePath = archivePath;
                
                // Notify ProjectTracker to refresh
                ProjectTracker.Instance.ScanProjectFiles(Configuration.Instance.LibraryPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error archiving project: {ex.Message}", "Archive Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gets the content of the project tab
        /// </summary>
        /// <returns>The content as a string</returns>
        public string GetContent()
        {
            // For project tab, we'll return a simple representation of the project
            // This is a placeholder implementation since project content isn't typically exported
            return "Project content is not available for export.";
        }

        private List<Models.DependencyItem> GetDependencyOptions()
        {
            var dependencies = new List<Models.DependencyItem>();
            
            // Add projects (except current)
            var projects = ProjectTracker.Instance.GetAllProjects()
                .Where(p => p.FilePath != _filePath)
                .Select(p => new Models.DependencyItem 
                { 
                    FilePath = p.FilePath,
                    DisplayName = $"Project: {p.Title}",
                    Type = DependencyType.Project
                });
            dependencies.AddRange(projects);

            // Add ToDos from ToDoTracker
            var todos = ToDoTracker.Instance.GetAllTodos();
            foreach (var todo in todos)
            {
                if (todo != null && !string.IsNullOrEmpty(todo.Title))
                {
                    dependencies.Add(new Models.DependencyItem
                    {
                        FilePath = todo.FilePath,
                        DisplayName = $"ToDo: {todo.Title}",
                        Type = DependencyType.ToDo
                    });
                }
            }

            return dependencies;
        }

        public void OnTabSelected()
        {
            // Load project data when tab is selected if not already loaded
            if (!_isContentLoaded && !string.IsNullOrEmpty(FilePath))
            {
                LoadProjectData();
            }
        }

        public void OnTabDeselected()
        {
            // Save any pending changes when tab is deselected
            if (IsModified)
            {
                Save().ConfigureAwait(false);
            }
        }

        private void LoadProjectData()
        {
            // Implementation of LoadProjectData method
        }
    }
} 