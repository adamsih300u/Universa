using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Data;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Views;
using Universa.Desktop.Models;
using System.Windows.Threading;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Universa.Desktop.Core;
using Universa.Desktop.Services;

namespace Universa.Desktop
{
    public partial class OverviewTab : UserControl, INotifyPropertyChanged, IFileTab
    {
        public int LastKnownCursorPosition { get; private set; } = 0;
        
        private ObservableCollection<Project> _projects;
        private ObservableCollection<ToDo> _todos;
        private ICollectionView _projectsView;
        private ICollectionView _todosView;
        private string _currentFilter = string.Empty;
        private Views.MainWindow _mainWindow;
        private string _searchText = string.Empty;
        private bool _showCompletedProjects;
        private bool _showCompletedTodos;
        private string _title = "Overview";

        public event PropertyChangedEventHandler PropertyChanged;

        public string FilePath 
        { 
            get => "Overview";
            set { } // No-op as this is a virtual file
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

        public bool IsModified 
        { 
            get => false;
            set { } // No-op as this tab is never modified
        }

        public async Task<bool> Save() 
        { 
            return await Task.FromResult(true); // Always returns true as this is a virtual file
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            return await Task.FromResult(true); // Always returns true as this is a virtual file
        }

        public void Reload()
        {
            LoadData(); // Refresh all data
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<Project> Projects
        {
            get => _projects;
            set
            {
                if (_projects != value)
                {
                    _projects = value;
                    OnPropertyChanged(nameof(Projects));
                }
            }
        }

        public ObservableCollection<ToDo> Todos
        {
            get => _todos;
            set
            {
                if (_todos != value)
                {
                    _todos = value;
                    OnPropertyChanged(nameof(Todos));
                }
            }
        }

        public bool ShowCompletedProjects
        {
            get => _showCompletedProjects;
            set
            {
                if (_showCompletedProjects != value)
                {
                    _showCompletedProjects = value;
                    OnPropertyChanged(nameof(ShowCompletedProjects));
                    _projectsView?.Refresh();
                }
            }
        }

        public bool ShowCompletedTodos
        {
            get => _showCompletedTodos;
            set
            {
                if (_showCompletedTodos != value)
                {
                    _showCompletedTodos = value;
                    OnPropertyChanged(nameof(ShowCompletedTodos));
                    LoadData(); // Reload data to include/exclude archived todos
                }
            }
        }

        public OverviewTab()
        {
            InitializeComponent();
            _projects = new ObservableCollection<Project>();
            _todos = new ObservableCollection<ToDo>();
            _mainWindow = Application.Current.MainWindow as Views.MainWindow;

            // Subscribe to tracker events
            ProjectTracker.Instance.ProjectsChanged += OnProjectsChanged;
            ToDoTracker.Instance.TodosChanged += OnTodosChanged;

            // Initialize collection views
            InitializeCollectionViews();

            // Load initial data
            LoadData();
        }

        private void InitializeCollectionViews()
        {
            _projectsView = CollectionViewSource.GetDefaultView(_projects);
            _todosView = CollectionViewSource.GetDefaultView(_todos);
            
            _projectsView.Filter = FilterProject;
            _todosView.Filter = FilterTodo;

            ProjectsListView.ItemsSource = _projectsView;
            TodosListView.ItemsSource = _todosView;
        }

        private bool FilterProject(object item)
        {
            if (item is Project project)
            {
                bool matchesSearch = string.IsNullOrWhiteSpace(_searchText) ||
                                   project.Title?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true ||
                                   project.Goal?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true;

                bool matchesCompletion = ShowCompletedProjects || project.Status != ProjectStatus.Completed;

                return matchesSearch && matchesCompletion;
            }
            return false;
        }

        private bool FilterTodo(object item)
        {
            if (item is ToDo todo)
            {
                try
                {
                    bool matchesSearch = string.IsNullOrWhiteSpace(_searchText) ||
                                       todo.Title?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true ||
                                       todo.Description?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true;

                    bool isArchived = todo.FilePath?.EndsWith(".todo.archive") == true;
                    
                    // If it's an archived item, only show it when ShowCompletedTodos is true
                    if (isArchived)
                    {
                        return ShowCompletedTodos && matchesSearch;
                    }

                    // For non-archived items, show based on completion status
                    bool matchesCompletion = ShowCompletedTodos || !todo.IsCompleted;
                    return matchesSearch && matchesCompletion;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error filtering todo: {ex.Message}");
                    return false;
                }
            }
            return false;
        }

        private void LoadData()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(LoadData);
                return;
            }

            try
            {
                // Load Projects
                Projects.Clear();
                var projects = ProjectTracker.Instance.GetAllProjects();
                foreach (var project in projects)
                {
                    Projects.Add(project);
                }

                // Load ToDos from ToDoTracker
                Todos.Clear();
                var todos = ToDoTracker.Instance.GetAllTodos();
                foreach (var todo in todos)
                {
                    System.Diagnostics.Debug.WriteLine($"Adding todo: Title='{todo.Title}', FilePath='{todo.FilePath}', IsCompleted={todo.IsCompleted}");
                    Todos.Add(todo);
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded total of {Todos.Count} todos from ToDoTracker");

                // Refresh views
                _projectsView?.Refresh();
                _todosView?.Refresh();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in LoadData: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void OnProjectsChanged()
        {
            LoadData();
        }

        private void OnTodosChanged()
        {
            LoadData();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _searchText = textBox.Text;
                _projectsView?.Refresh();
                _todosView?.Refresh();
            }
        }

        private void ProjectItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is Project project)
            {
                _mainWindow?.OpenFileInEditor(project.FilePath);
            }
        }

        private void TodoItem_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ToDo todo)
            {
                _mainWindow?.OpenFileInEditor(todo.FilePath);
            }
        }

        private void Filename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Hyperlink hyperlink && hyperlink.Tag is string filePath)
            {
                _mainWindow?.OpenFileInEditor(filePath);
            }
        }

        private async void AIAnalysis_Click(object sender, RoutedEventArgs e)
        {
            // Prepare the data for AI analysis
            var analysisData = new
            {
                Projects = _projects.Select(p => new
                {
                    p.Title,
                    p.Goal,
                    p.Status,
                    p.DueDate,
                    Tasks = p.Tasks.Select(t => new
                    {
                        t.Title,
                        t.Description,
                        t.IsCompleted,
                        t.StartDate,
                        t.DueDate,
                        t.Dependencies
                    }).ToList(),
                    Dependencies = p.Dependencies.Select(d => new
                    {
                        d.FilePath,
                        d.IsHardDependency
                    }).ToList()
                }).ToList(),
                Todos = _todos.Select(t => new
                {
                    t.Title,
                    t.Description,
                    t.IsCompleted,
                    t.StartDate,
                    t.DueDate,
                    t.Tags
                }).ToList()
            };

            // TODO: Send this data to the AI service for analysis
            // This will be implemented when we add the AI integration
            MessageBox.Show("AI Analysis feature coming soon!", "Not Implemented", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Title_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                string filePath = null;
                if (element.DataContext is Project project)
                {
                    filePath = project.FilePath;
                }
                else if (element.DataContext is ToDo todo)
                {
                    filePath = todo.FilePath;
                }

                if (!string.IsNullOrEmpty(filePath))
                {
                    // First try to find if the tab is already open
                    var existingTab = _mainWindow?.FindTab(filePath);
                    if (existingTab != null)
                    {
                        // Switch to the existing tab
                        _mainWindow.MainTabControl.SelectedItem = existingTab;
                    }
                    else
                    {
                        // Open the file in a new tab
                        _mainWindow?.OpenFileInEditor(filePath);
                    }
                }
            }
        }

        private void ToDo_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && checkBox.DataContext is ToDo todo)
            {
                if (todo.IsCompleted)
                {
                    todo.CompletedDate = DateTime.Now;
                }
                else
                {
                    todo.CompletedDate = null;
                }

                // Find the original file and update it
                var filePath = todo.FilePath;
                if (!string.IsNullOrEmpty(filePath))
                {
                    try
                    {
                        var content = File.ReadAllText(filePath);
                        var options = new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        };

                        var todos = JsonSerializer.Deserialize<List<ToDo>>(content, options);
                        var existingTodo = todos?.FirstOrDefault(t => t.Title == todo.Title);
                        if (existingTodo != null)
                        {
                            existingTodo.IsCompleted = todo.IsCompleted;
                            existingTodo.CompletedDate = todo.CompletedDate;

                            var updatedContent = JsonSerializer.Serialize(todos, options);
                            File.WriteAllText(filePath, updatedContent);

                            // Notify ToDoTracker that the file has changed
                            ToDoTracker.Instance.ScanTodoFilesAsync(Configuration.Instance.LibraryPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating todo completion status: {ex.Message}");
                    }
                }

                // Refresh the views
                _todosView?.Refresh();
            }
        }

        /// <summary>
        /// Gets the content of the overview tab
        /// </summary>
        /// <returns>The content as a string</returns>
        public string GetContent()
        {
            // For overview tab, we'll return a simple representation
            // This is a placeholder implementation since overview content isn't typically exported
            return "Overview content is not available for export.";
        }

        public void OnTabSelected()
        {
            // Refresh overview data when tab is selected
            RefreshOverviewData();
        }

        public void OnTabDeselected()
        {
            // No cleanup needed for overview tab
        }

        private void RefreshOverviewData()
        {
            LoadData();
        }
    }
} 