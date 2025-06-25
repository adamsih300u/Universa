using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Universa.Desktop.Models;
using Universa.Desktop.Interfaces;
using System.Threading.Tasks;
using Universa.Desktop.Dialogs;
using System.Diagnostics;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Views;
using Universa.Desktop.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using System.Text;
using System.Text.Json;

namespace Universa.Desktop.Library
{
    public partial class LibraryNavigator : UserControl, INotifyPropertyChanged
    {
        // Add the attached property
        public static readonly DependencyProperty IsDragOverProperty =
            DependencyProperty.RegisterAttached(
                "IsDragOver",
                typeof(bool),
                typeof(LibraryNavigator),
                new PropertyMetadata(false));

        public static void SetIsDragOver(DependencyObject element, bool value)
        {
            element.SetValue(IsDragOverProperty, value);
        }

        public static bool GetIsDragOver(DependencyObject element)
        {
            return (bool)element.GetValue(IsDragOverProperty);
        }

        private IConfigurationService _configService;
        private bool _isRefreshing;
        private ObservableCollection<LibraryTreeItem> _rootItems;
        private Point _dragStartPoint;
        private bool _isDragging;
        private TreeViewItem _lastDragOverItem;
        private bool _isHandlingConfigChange = false;

        public event PropertyChangedEventHandler PropertyChanged;

        public static readonly DependencyProperty ParentMainWindowProperty =
            DependencyProperty.Register(
                nameof(ParentMainWindow),
                typeof(Views.MainWindow),
                typeof(LibraryNavigator),
                new PropertyMetadata(null));

        public Views.MainWindow ParentMainWindow
        {
            get => (Views.MainWindow)GetValue(ParentMainWindowProperty);
            set => SetValue(ParentMainWindowProperty, value);
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public ObservableCollection<LibraryTreeItem> RootItems
        {
            get => _rootItems;
            set
            {
                if (_rootItems != value)
                {
                    _rootItems = value;
                    OnPropertyChanged();
                }
            }
        }

        // Add event declaration
        public event EventHandler<LibraryNavigationEventArgs> SelectedItemChanged;

        public LibraryNavigator()
        {
            InitializeComponent();
            DataContext = this;
            _rootItems = new ObservableCollection<LibraryTreeItem>();
            
            // Subscribe to configuration changes
            _configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            if (_configService != null)
            {
                _configService.ConfigurationChanged += OnConfigurationChanged;
            }

            // Subscribe to Loaded event to initialize MainWindow
            this.Loaded += (s, e) =>
            {
                if (ParentMainWindow == null)
                {
                    ParentMainWindow = Application.Current.MainWindow as Views.MainWindow;
                    if (ParentMainWindow == null)
                    {
                        // Try to find the MainWindow by name
                        foreach (Window window in Application.Current.Windows)
                        {
                            if (window is Views.MainWindow mainWindow)
                            {
                                ParentMainWindow = mainWindow;
                                break;
                            }
                        }
                    }
                    Debug.WriteLine($"LibraryNavigator: MainWindow initialized in Loaded event: {(ParentMainWindow != null ? "success" : "failed")}");
                }
            };
            
            // Subscribe to window closing event to save expanded states
            if (Application.Current.MainWindow != null)
            {
                Application.Current.MainWindow.Closing += (s, e) => SaveExpandedStatesOnClose();
            }
        }

        private async void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            try
            {
                // Prevent recursive configuration updates
                if (_isHandlingConfigChange) return;
                _isHandlingConfigChange = true;

                Debug.WriteLine($"LibraryNavigator: Configuration changed - Key: {e.Key}");
                
                // Refresh when library path changes
                if (e.Key == ConfigurationKeys.Library.Path)
                {
                    var libraryPath = _configService?.Provider?.LibraryPath;
                    Debug.WriteLine($"LibraryNavigator: Library path changed to: {libraryPath}");
                    
                    if (!string.IsNullOrEmpty(libraryPath))
                    {
                        if (Directory.Exists(libraryPath))
                        {
                            Debug.WriteLine("LibraryNavigator: Library path exists, refreshing items");
                            await RefreshItems(false);
                        }
                        else
                        {
                            Debug.WriteLine("LibraryNavigator: Library path does not exist");
                            MessageBox.Show($"Library path does not exist: {libraryPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    else
                    {
                        Debug.WriteLine("LibraryNavigator: Library path is null or empty");
                    }
                }
                // Refresh when Jellyfin settings change
                else if (e.Key.StartsWith("services.jellyfin"))
                {
                    Debug.WriteLine("LibraryNavigator: Jellyfin settings changed, refreshing items");
                    await RefreshItems(false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibraryNavigator: Error in OnConfigurationChanged - {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                _isHandlingConfigChange = false;
            }
        }

        private async void OnTodosChanged()
        {
            if (!_isHandlingConfigChange)
            {
                await ToDoTracker.Instance.ScanTodoFilesAsync(_configService.Provider.LibraryPath);
            }
        }

        private Dictionary<string, bool> SaveExpandedStates()
        {
            var states = new Dictionary<string, bool>();
            foreach (var item in GetAllTreeViewItems(LibraryTreeView))
            {
                var libraryItem = item.DataContext as LibraryTreeItem;
                if (libraryItem != null)
                {
                    states[libraryItem.Path] = item.IsExpanded;
                }
            }
            return states;
        }

        public async Task RefreshItems(bool useCurrentState = true, Dictionary<string, bool> savedStates = null)
        {
            try
            {
                _isRefreshing = true;
                var currentStates = useCurrentState ? GetExpandedStates() : savedStates;

                RootItems = new ObservableCollection<LibraryTreeItem>();

                // Add Inbox item at the top
                RootItems.Add(new LibraryTreeItem
                {
                    Name = "Inbox",
                    Icon = "📥",
                    Type = Models.LibraryItemType.Inbox
                });

                // Add Global Agenda item
                RootItems.Add(new LibraryTreeItem
                {
                    Name = "📅 Global Agenda",
                    Icon = "🗓️",
                    Type = Models.LibraryItemType.GlobalAgenda
                });

                // Add services
                await LoadServices();

                // Create Library category and load folders into it
                if (!string.IsNullOrEmpty(_configService.Provider.LibraryPath))
                {
                    var libraryItem = new LibraryTreeItem
                    {
                        Name = "Library",
                        Type = Models.LibraryItemType.Category,
                        Icon = "📚",
                        Children = new ObservableCollection<LibraryTreeItem>()
                    };
                    RootItems.Add(libraryItem);
                    await LoadDirectory(_configService.Provider.LibraryPath, libraryItem.Children);
                }

                if (currentStates != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Restoring {currentStates.Count} expanded states...");
                    foreach (var kvp in currentStates)
                    {
                        System.Diagnostics.Debug.WriteLine($"  Will restore: {kvp.Key} = {kvp.Value}");
                    }
                    
                    // Force layout update before restoration
                    LibraryTreeView.UpdateLayout();
                    
                    // Use Dispatcher to ensure UI is ready
                    await Application.Current.Dispatcher.InvokeAsync(async () =>
                    {
                        await RestoreExpandedStatesRecursive(LibraryTreeView.Items, currentStates);
                    }, DispatcherPriority.Loaded);
                    
                    System.Diagnostics.Debug.WriteLine("Completed expanded state restoration");
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task RestoreExpandedStatesRecursive(ItemCollection items, Dictionary<string, bool> states)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                
                // Ensure container is generated
                var treeViewItem = LibraryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeViewItem == null)
                {
                    // Force container generation
                    LibraryTreeView.UpdateLayout();
                    treeViewItem = LibraryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                }
                
                if (treeViewItem == null) continue;

                var libraryItem = item as LibraryTreeItem;
                if (libraryItem?.Type == Models.LibraryItemType.Directory && !string.IsNullOrEmpty(libraryItem.Path))
                {
                    bool shouldExpand = states.ContainsKey(libraryItem.Path) && states[libraryItem.Path];
                    System.Diagnostics.Debug.WriteLine($"Restoring {libraryItem.Path}: shouldExpand = {shouldExpand}");
                    
                    if (shouldExpand)
                    {
                        // Load contents before expanding
                        if (libraryItem.Children == null)
                        {
                            libraryItem.Children = new ObservableCollection<LibraryTreeItem>();
                        }
                        
                        // Check if we need to load directory contents
                        if (libraryItem.Children.Count <= 1 && 
                            (libraryItem.Children.Count == 0 || libraryItem.Children[0].Path == null))
                        {
                            libraryItem.Children.Clear();
                            await LoadDirectory(libraryItem.Path, libraryItem.Children);
                        }
                        
                        treeViewItem.IsExpanded = true;
                        System.Diagnostics.Debug.WriteLine($"Expanded: {libraryItem.Path}");

                        // Allow UI to update before processing children
                        await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
                        
                        // Process children after parent is expanded
                        if (treeViewItem.Items != null && treeViewItem.Items.Count > 0)
                        {
                            await RestoreExpandedStatesRecursive(treeViewItem.Items, states);
                        }
                    }
                }
            }
        }

        private async Task LoadDirectory(string path, ObservableCollection<LibraryTreeItem> items)
        {
            try
            {
                Debug.WriteLine($"LibraryNavigator: Loading directory {path}");
                
                // Skip loading physical files for virtual paths
                if (path == "services://" || path == "Matrix" || path == "RSS" || path == "Jellyfin" || path == "Music")
                {
                    Debug.WriteLine("LibraryNavigator: Skipping virtual path");
                    return;
                }

                // Initialize items collection if null
                if (items == null)
                {
                    Debug.WriteLine("LibraryNavigator: Creating new items collection");
                    items = new ObservableCollection<LibraryTreeItem>();
                }
                else
                {
                    Debug.WriteLine("LibraryNavigator: Clearing existing items");
                    items.Clear();
                }

                // Get directory contents on background thread
                var directories = await Task.Run(() => 
                {
                    try 
                    {
                        Debug.WriteLine("LibraryNavigator: Getting directories");
                        var dirs = Directory.GetDirectories(path)
                            .Select(dir => new DirectoryInfo(dir))
                            .Where(dirInfo => (dirInfo.Attributes & FileAttributes.Hidden) == 0 && !dirInfo.Name.StartsWith("."))
                            .Select(dirInfo => new LibraryTreeItem
                            {
                                Name = dirInfo.Name,
                                Path = dirInfo.FullName,
                                Type = Models.LibraryItemType.Directory,
                                Icon = "📁",
                                Children = new ObservableCollection<LibraryTreeItem>()
                            })
                            .ToList();

                        // Add a dummy item to each directory to show expand arrow
                        foreach (var dir in dirs)
                        {
                            dir.Children.Add(new LibraryTreeItem { Path = null });
                        }

                        return dirs;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"LibraryNavigator: Error getting directories: {ex.Message}");
                        return new List<LibraryTreeItem>();
                    }
                });

                // Get files on background thread
                var files = await Task.Run(() => 
                {
                    try 
                    {
                        Debug.WriteLine("LibraryNavigator: Getting files");
                        return Directory.GetFiles(path)
                            .Select(file => new FileInfo(file))
                            .Where(fileInfo => (fileInfo.Attributes & FileAttributes.Hidden) == 0 && !fileInfo.Name.StartsWith("."))
                            .Select(fileInfo => new LibraryTreeItem
                            {
                                Name = fileInfo.Name,
                                Path = fileInfo.FullName,
                                Type = Models.LibraryItemType.File,
                                Icon = "📄"
                            })
                            .ToList();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"LibraryNavigator: Error getting files: {ex.Message}");
                        return new List<LibraryTreeItem>();
                    }
                });

                // Add directories and files to items collection on UI thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var dir in directories)
                    {
                        items.Add(dir);
                    }
                    foreach (var file in files)
                    {
                        items.Add(file);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibraryNavigator: Error in LoadDirectory - {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task LoadServices()
        {
            try
            {
                Debug.WriteLine("LibraryNavigator: Starting LoadServices");
                if (RootItems == null)
                {
                    Debug.WriteLine("LibraryNavigator: Initializing RootItems");
                    RootItems = new ObservableCollection<LibraryTreeItem>();
                }

                var config = _configService?.Provider;
                if (config == null)
                {
                    Debug.WriteLine("LibraryNavigator: Configuration provider is null");
                    return;
                }

                Debug.WriteLine("LibraryNavigator: Creating Services category");
                var servicesItem = new LibraryTreeItem
                {
                    Name = "Services",
                    Type = Models.LibraryItemType.Category,
                    Icon = "🔌",
                    Children = new ObservableCollection<LibraryTreeItem>()
                };

                try
                {
                    // Subsonic
                    Debug.WriteLine($"LibraryNavigator: Checking Subsonic configuration - URL: {!string.IsNullOrEmpty(config.SubsonicUrl)}, Username: {!string.IsNullOrEmpty(config.SubsonicUsername)}, Password: {!string.IsNullOrEmpty(config.SubsonicPassword)}");
                    if (!string.IsNullOrEmpty(config.SubsonicUrl) && 
                        !string.IsNullOrEmpty(config.SubsonicUsername) && 
                        !string.IsNullOrEmpty(config.SubsonicPassword))
                    {
                        Debug.WriteLine("LibraryNavigator: Adding Subsonic service");
                        servicesItem.Children.Add(new LibraryTreeItem
                        {
                            Name = !string.IsNullOrEmpty(config.SubsonicName) ? config.SubsonicName : "Subsonic",
                            Type = Models.LibraryItemType.Service,
                            Icon = "🎵",
                            Path = "services://subsonic",
                            Tag = "subsonic"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LibraryNavigator: Error adding Subsonic service - {ex.Message}");
                }

                try
                {
                    // Jellyfin
                    Debug.WriteLine($"LibraryNavigator: Checking Jellyfin configuration - URL: {!string.IsNullOrEmpty(config.JellyfinUrl)}, Username: {!string.IsNullOrEmpty(config.JellyfinUsername)}, Password: {!string.IsNullOrEmpty(config.JellyfinPassword)}");
                    if (!string.IsNullOrEmpty(config.JellyfinUrl) && 
                        !string.IsNullOrEmpty(config.JellyfinUsername) && 
                        !string.IsNullOrEmpty(config.JellyfinPassword))
                    {
                        Debug.WriteLine("LibraryNavigator: Adding Jellyfin service");
                        servicesItem.Children.Add(new LibraryTreeItem
                        {
                            Name = !string.IsNullOrEmpty(config.JellyfinName) ? config.JellyfinName : "Jellyfin",
                            Type = Models.LibraryItemType.Service,
                            Icon = "🎬",
                            Path = "services://jellyfin",
                            Tag = "jellyfin"
                        });
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"LibraryNavigator: Error adding Jellyfin service - {ex.Message}");
                }

                // Only add the services item if it has any children
                if (servicesItem.Children.Any())
                {
                    Debug.WriteLine($"LibraryNavigator: Adding Services category with {servicesItem.Children.Count} services");
                    RootItems.Add(servicesItem);
                }
                else
                {
                    Debug.WriteLine("LibraryNavigator: No services to add");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LibraryNavigator: Error in LoadServices - {ex.Message}");
            }
        }

        public void CollapseAllFolders()
        {
            foreach (var item in RootItems)
            {
                CollapseItemAndChildren(item);
            }
        }

        private void CollapseItemAndChildren(LibraryTreeItem item)
        {
            if (item.IsExpanded)
            {
                item.IsExpanded = false;
            }

            if (item.Children != null)
            {
                foreach (var child in item.Children)
                {
                    CollapseItemAndChildren(child);
                }
            }
        }

        private void LibraryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_isRefreshing) return;

            // Handle Inbox selection
            if (e.NewValue is TreeViewItem treeViewItem && treeViewItem.Name == "InboxItem")
            {
                var mainWindow = ParentMainWindow;
                if (mainWindow != null)
                {
                    // Create or activate Inbox tab
                    var existingTab = mainWindow.FindTab("Inbox");
                    if (existingTab == null)
                    {
                        var inboxTab = new InboxTab();
                        var tabItem = new TabItem
                        {
                            Header = "Inbox",
                            Content = inboxTab
                        };
                        mainWindow.MainTabControl.Items.Add(tabItem);
                        mainWindow.MainTabControl.SelectedItem = tabItem;
                    }
                    else
                    {
                        mainWindow.MainTabControl.SelectedItem = existingTab;
                    }
                }
                return;
            }

            // Handle regular items
            var selectedItem = e.NewValue as LibraryTreeItem;
            if (selectedItem != null)
            {
                if (selectedItem.Type == Models.LibraryItemType.Overview)
                {
                    ParentMainWindow?.OpenOverviewTab();
                }
                else if (selectedItem.Type == Models.LibraryItemType.GlobalAgenda)
                {
                    ParentMainWindow?.OpenGlobalAgendaTab();
                }
                else if (selectedItem.Type == Models.LibraryItemType.Service)
                {
                    ParentMainWindow?.HandleServiceNavigation(selectedItem);
                }
                else
                {
                    SelectedItemChanged?.Invoke(this, new LibraryNavigationEventArgs(selectedItem));
                }
            }
        }

        private void LibraryTreeView_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var treeViewItem = FindParent<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem != null)
            {
                treeViewItem.Focus();
                e.Handled = true;
            }
        }

        private void LibraryTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is FrameworkElement element && element.DataContext is LibraryTreeItem item)
            {
                if (item.Type == Models.LibraryItemType.Inbox)
                {
                    ParentMainWindow?.OpenInboxTab();
                    e.Handled = true;
                    return;
                }

                // Handle other item types...
                switch (item.Type)
                {
                    case Models.LibraryItemType.Overview:
                        ParentMainWindow?.OpenOverviewTab();
                        break;
                    case Models.LibraryItemType.GlobalAgenda:
                        ParentMainWindow?.OpenGlobalAgendaTab();
                        break;
                    case Models.LibraryItemType.Service:
                        ParentMainWindow?.HandleServiceNavigation(item);
                        break;
                    case Models.LibraryItemType.Category:
                        // Category nodes should just expand/collapse
                        item.IsExpanded = !item.IsExpanded;
                        break;
                    case Models.LibraryItemType.File:
                        if (File.Exists(item.Path))
                        {
                            ParentMainWindow?.OpenFileInEditor(item.Path);
                        }
                        break;
                }
                e.Handled = true;
            }
        }

        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            if (parent == null) return null;
            if (parent is T typedParent) return typedParent;
            return FindParent<T>(parent);
        }

        private async void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New Folder", "Enter folder name:");
            if (dialog.ShowDialog() == true)
            {
                await AddNewFolder(parentPath, dialog.InputText);
            }
        }

        private async void NewToDo_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var treeViewItem = menuItem?.Parent as ContextMenu;
            var parentItem = treeViewItem?.PlacementTarget as TreeViewItem;
            var parentPath = parentItem?.DataContext as LibraryTreeItem;

            if (parentPath != null)
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Save ToDo List As",
                    Filter = "ToDo files (*.todo)|*.todo",
                    DefaultExt = ".todo",
                    FileName = "NewToDo.todo",
                    InitialDirectory = parentPath.Path
                };

                if (dialog.ShowDialog() == true)
                {
                    var todo = new ToDo
                    {
                        Title = "New ToDo",
                        Description = "Enter your task description here",
                        StartDate = DateTime.Now,
                        IsCompleted = false,
                        FilePath = dialog.FileName
                    };

                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    var json = System.Text.Json.JsonSerializer.Serialize(new List<ToDo> { todo }, options);
                    File.WriteAllText(dialog.FileName, json);

                    // Refresh todo tracker
                    await ToDoTracker.Instance.ScanTodoFilesAsync(_configService.Provider.LibraryPath);

                    // Open the new todo file
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    mainWindow?.OpenFileInEditor(dialog.FileName);
                }
            }
        }

        private async void NewOrgFile_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New Org File", "Enter org file name:");
            if (dialog.ShowDialog() == true)
            {
                var fileName = dialog.InputText;
                if (!fileName.EndsWith(".org"))
                {
                    fileName += ".org";
                }

                // Create initial org-mode content with some example items
                var template = @"#+TITLE: " + Path.GetFileNameWithoutExtension(fileName) + @"

* PROJECT Sample Project [#A] :project:work:
  DEADLINE: <" + DateTime.Now.AddDays(30).ToString("yyyy-MM-dd ddd") + @">
  :PROPERTIES:
  :CATEGORY: Development
  :BUDGET: $5000
  :MANAGER: Project Manager
  :STATUS: Planning
  :END:
  
  This is an example project with multiple tasks.

** TODO Research phase [#A] :research:
   SCHEDULED: <" + DateTime.Now.ToString("yyyy-MM-dd ddd") + @">
   
   Research requirements and gather information.

** TODO Design phase [#B] :design:
   DEADLINE: <" + DateTime.Now.AddDays(10).ToString("yyyy-MM-dd ddd") + @">
   
   Create wireframes and mockups.

** TODO Implementation [#A] :development:
   
   Build the actual solution.

** TODO Testing and review [#C] :testing:
   
   Quality assurance and final review.

* TODO Individual task [#B] :personal:
  SCHEDULED: <" + DateTime.Now.AddDays(1).ToString("yyyy-MM-dd ddd") + @">
  
  Example of a standalone task not part of a project.

* SOMEDAY Learn new technology :learning:
  
  Something to consider for the future.
  
  Resources: [[https://example.com/tutorial][Online Tutorial]]

* Links and References :reference:
  
  Examples of different link types:
  - Web link: [[https://orgmode.org][Org-Mode Website]]
  - File link: [[./documents/notes.md][My Notes]]
  - Internal link: [[#Sample Project][Jump to Sample Project]]
";
                await AddNewFile(parentPath, fileName, template);
            }
        }

        private async void NewNote_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New Note", "Enter note name:");
            if (dialog.ShowDialog() == true)
            {
                var fileName = dialog.InputText;
                if (!fileName.EndsWith(".md"))
                {
                    fileName += ".md";
                }
                await AddNewFile(parentPath, fileName, "# New Note\n\nEnter your note here...");
            }
        }

        private async void NewManuscript_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var manuscriptDialog = new InputDialog("New Manuscript", "Enter manuscript name:");
            if (manuscriptDialog.ShowDialog() != true) return;

            var manuscriptName = manuscriptDialog.InputText;
            if (!manuscriptName.EndsWith(".md"))
            {
                manuscriptName += ".md";
            }

            var projectTitle = Path.GetFileNameWithoutExtension(manuscriptName);

            // Ask for optional reference files (following the same pattern as New Outline)
            var outlineDialog = new InputDialog("Outline File (Optional)", "Enter name for outline file (or leave blank):");
            var rulesDialog = new InputDialog("Rules File (Optional)", "Enter name for rules file (or leave blank):");
            var styleDialog = new InputDialog("Style Guide (Optional)", "Enter name for style guide file (or leave blank):");

            string outlineName = null;
            string rulesName = null;
            string styleName = null;

            // Get outline name (optional)
            if (outlineDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(outlineDialog.InputText))
            {
                outlineName = outlineDialog.InputText;
                if (!outlineName.EndsWith(".md")) outlineName += ".md";
            }

            // Get rules name (optional)
            if (rulesDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(rulesDialog.InputText))
            {
                rulesName = rulesDialog.InputText;
                if (!rulesName.EndsWith(".md")) rulesName += ".md";
            }

            // Get style guide name (optional)
            if (styleDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(styleDialog.InputText))
            {
                styleName = styleDialog.InputText;
                if (!styleName.EndsWith(".md")) styleName += ".md";
            }

            // Create manuscript with enhanced frontmatter
            var manuscriptTemplate = new StringBuilder();
            
            // Add frontmatter with reference files
            manuscriptTemplate.AppendLine("---");
            manuscriptTemplate.AppendLine("type: fiction");
            manuscriptTemplate.AppendLine($"title: {projectTitle}");
            manuscriptTemplate.AppendLine("author: ");
            manuscriptTemplate.AppendLine("genre: ");
            manuscriptTemplate.AppendLine("series: ");
            
            // Add reference file entries if specified
            if (outlineName != null)
            {
                manuscriptTemplate.AppendLine($"outline: {outlineName}");
            }
            if (rulesName != null)
            {
                manuscriptTemplate.AppendLine($"rules: {rulesName}");
            }
            if (styleName != null)
            {
                manuscriptTemplate.AppendLine($"style: {styleName}");
            }
            
            manuscriptTemplate.AppendLine("---");
            manuscriptTemplate.AppendLine();
            
            // Add body section
            manuscriptTemplate.AppendLine($"# {projectTitle}");
            manuscriptTemplate.AppendLine();
            
            // Create manuscript file (NOTE: We do NOT create the reference files - user requested they only be referenced)
            await AddNewFile(parentPath, manuscriptName, manuscriptTemplate.ToString());
        }

        private async Task CreateOptimalRulesFile(string parentPath, string fileName, string projectTitle, string seriesName)
        {
            var rulesTemplate = new StringBuilder();
            
            rulesTemplate.AppendLine($"# Rules: {projectTitle}");
            rulesTemplate.AppendLine();
            
            // Character section optimized for RulesParser
            rulesTemplate.AppendLine("[Characters]");
            rulesTemplate.AppendLine();
            rulesTemplate.AppendLine("# Main Character");
            rulesTemplate.AppendLine("- Age: (current age)");
            rulesTemplate.AppendLine("- Role: protagonist");
            rulesTemplate.AppendLine("- Background: (character background)");
            rulesTemplate.AppendLine("- Personality: (key personality traits)");
            rulesTemplate.AppendLine();
            
            if (!string.IsNullOrEmpty(seriesName))
            {
                rulesTemplate.AppendLine("[Timeline]");
                rulesTemplate.AppendLine();
                rulesTemplate.AppendLine($"Book 1 - {projectTitle} (not written yet)");
                rulesTemplate.AppendLine("- Main events: (key plot points)");
                rulesTemplate.AppendLine("- Character ages: Main Character (age)");
                rulesTemplate.AppendLine();
            }
            
            rulesTemplate.AppendLine("[Critical Facts]");
            rulesTemplate.AppendLine();
            rulesTemplate.AppendLine("- (Important world-building facts)");
            rulesTemplate.AppendLine("- (Character relationships)");
            rulesTemplate.AppendLine("- (Magic system/technology rules)");
            rulesTemplate.AppendLine();
            
            rulesTemplate.AppendLine("[Locations]");
            rulesTemplate.AppendLine();
            rulesTemplate.AppendLine("- Primary Setting: (main location description)");
            rulesTemplate.AppendLine("- Important Places: (key locations in the story)");
            rulesTemplate.AppendLine();
            
            rulesTemplate.AppendLine("[Organizations]");
            rulesTemplate.AppendLine();
            rulesTemplate.AppendLine("- (Relevant groups, institutions, or factions)");

            await AddNewFile(parentPath, fileName, rulesTemplate.ToString());
        }

        private async Task CreateOptimalStyleFile(string parentPath, string fileName, string projectTitle, string genre)
        {
            var styleTemplate = new StringBuilder();
            
            styleTemplate.AppendLine($"# Style Guide: {projectTitle}");
            styleTemplate.AppendLine();
            
            // Voice section optimized for StyleGuideParser
            styleTemplate.AppendLine("## Voice Rules - THESE MUST BE FOLLOWED");
            styleTemplate.AppendLine();
            styleTemplate.AppendLine("- Primary perspective: (first person / third person limited / etc.)");
            styleTemplate.AppendLine("- Narrative voice: (formal / casual / literary / etc.)");
            styleTemplate.AppendLine("- Tense: (past tense / present tense)");
            styleTemplate.AppendLine();
            
            // Genre-specific guidance
            if (genre == "Fantasy")
            {
                styleTemplate.AppendLine("## Fantasy Elements");
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("- Magic terminology: (consistent magic system terms)");
                styleTemplate.AppendLine("- World-building integration: (how to weave fantasy elements naturally)");
                styleTemplate.AppendLine();
            }
            else if (genre == "Science Fiction")
            {
                styleTemplate.AppendLine("## Science Fiction Elements");
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("- Technical accuracy: (how to handle scientific concepts)");
                styleTemplate.AppendLine("- Future terminology: (consistent futuristic language)");
                styleTemplate.AppendLine();
            }
            
            styleTemplate.AppendLine("## Dialogue Guidelines");
            styleTemplate.AppendLine();
            styleTemplate.AppendLine("- Character voice distinction: (how each character should sound unique)");
            styleTemplate.AppendLine("- Dialogue tags: (preferred attribution style)");
            styleTemplate.AppendLine("- Speech patterns: (formal vs casual, regional dialects, etc.)");
            styleTemplate.AppendLine();
            
            styleTemplate.AppendLine("## Description Style");
            styleTemplate.AppendLine();
            styleTemplate.AppendLine("- Sensory details: (which senses to emphasize)");
            styleTemplate.AppendLine("- Description length: (brief / moderate / detailed)");
            styleTemplate.AppendLine("- Metaphor usage: (preference for metaphors and similes)");
            styleTemplate.AppendLine();
            
            styleTemplate.AppendLine("## Pacing and Structure");
            styleTemplate.AppendLine();
            styleTemplate.AppendLine("- Chapter length: (target word count or pacing)");
            styleTemplate.AppendLine("- Scene transitions: (how to move between scenes)");
            styleTemplate.AppendLine("- Tension building: (methods for creating suspense)");
            styleTemplate.AppendLine();
            
            styleTemplate.AppendLine("## Writing Sample");
            styleTemplate.AppendLine();
            styleTemplate.AppendLine("(Include a 2-3 paragraph example of your target writing style here)");
            styleTemplate.AppendLine("(This sample will be used by the AI to match your voice and tone)");

            await AddNewFile(parentPath, fileName, styleTemplate.ToString());
        }

        private async Task CreateOptimalOutlineFile(string parentPath, string fileName, string projectTitle, string genre, string seriesName)
        {
            var outlineTemplate = new StringBuilder();
            
            // Frontmatter optimized for OutlineParser
            outlineTemplate.AppendLine("---");
            outlineTemplate.AppendLine("type: outline");
            outlineTemplate.AppendLine($"title: {projectTitle}");
            outlineTemplate.AppendLine("author: ");
            outlineTemplate.AppendLine($"genre: {genre}");
            if (!string.IsNullOrEmpty(seriesName))
            {
                outlineTemplate.AppendLine($"series: {seriesName}");
            }
            outlineTemplate.AppendLine("---");
            outlineTemplate.AppendLine();
            
            // Structure optimized for OutlineParser
            outlineTemplate.AppendLine("# Overall Synopsis");
            outlineTemplate.AppendLine("- Brief 2-3 sentence summary of the entire story");
            outlineTemplate.AppendLine();
            
            outlineTemplate.AppendLine("# Notes");
            outlineTemplate.AppendLine("- Important notes about themes, tone, special considerations");
            outlineTemplate.AppendLine("- Use bullet points for better parsing");
            outlineTemplate.AppendLine();
            
            outlineTemplate.AppendLine("# Characters");
            outlineTemplate.AppendLine("- Protagonists");
            outlineTemplate.AppendLine("  - Character Name - Brief description of role and personality");
            outlineTemplate.AppendLine("- Antagonists");
            outlineTemplate.AppendLine("  - Villain Name - Brief description of role and motivations");
            outlineTemplate.AppendLine("- Supporting Characters");
            outlineTemplate.AppendLine("  - Support Character - Brief description");
            outlineTemplate.AppendLine();
            
            outlineTemplate.AppendLine("# Outline");
            outlineTemplate.AppendLine("## Chapter 1");
            outlineTemplate.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
            outlineTemplate.AppendLine("- Key event or action that occurs");
            outlineTemplate.AppendLine("- Character development moment");
            outlineTemplate.AppendLine("- Plot advancement");
            outlineTemplate.AppendLine();
            
            outlineTemplate.AppendLine("## Chapter 2");
            outlineTemplate.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
            outlineTemplate.AppendLine("- Key event or action that occurs");
            outlineTemplate.AppendLine("- Character development moment");
            outlineTemplate.AppendLine("- Plot advancement");

            await AddNewFile(parentPath, fileName, outlineTemplate.ToString());
        }

        private async void NewOutline_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New Outline", "Enter outline name:");
            if (dialog.ShowDialog() == true)
            {
                var outlineName = dialog.InputText;
                if (!outlineName.EndsWith(".md"))
                {
                    outlineName += ".md";
                }

                // Ask for optional reference files
                var rulesDialog = new InputDialog("Rules File (Optional)", "Enter name for rules file (or leave blank):");
                var styleDialog = new InputDialog("Style Guide (Optional)", "Enter name for style guide file (or leave blank):");

                string rulesName = null;
                string styleName = null;

                // Get rules name (optional)
                if (rulesDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(rulesDialog.InputText))
                {
                    rulesName = rulesDialog.InputText;
                    if (!rulesName.EndsWith(".md")) rulesName += ".md";
                }

                // Get style guide name (optional)
                if (styleDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(styleDialog.InputText))
                {
                    styleName = styleDialog.InputText;
                    if (!styleName.EndsWith(".md")) styleName += ".md";
                }

                // Create outline file with proper formatting
                var outlineTemplate = new StringBuilder();
                
                // Add frontmatter
                outlineTemplate.AppendLine("---");
                outlineTemplate.AppendLine("type: outline");
                outlineTemplate.AppendLine("title: " + Path.GetFileNameWithoutExtension(outlineName));
                outlineTemplate.AppendLine("author: ");
                outlineTemplate.AppendLine("genre: ");
                outlineTemplate.AppendLine("series: ");
                
                // Add ref statements for associated files if they were specified
                if (rulesName != null)
                {
                    outlineTemplate.AppendLine($"rules: {rulesName}");
                }
                if (styleName != null)
                {
                    outlineTemplate.AppendLine($"style: {styleName}");
                }
                
                // Close frontmatter
                outlineTemplate.AppendLine("---");
                outlineTemplate.AppendLine();
                
                // Add outline structure
                outlineTemplate.AppendLine("# Overall Synopsis");
                outlineTemplate.AppendLine("- Brief 2-3 sentence summary of the entire story");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Notes");
                outlineTemplate.AppendLine("- Important notes about themes, tone, special considerations");
                outlineTemplate.AppendLine("- Use bullet points for better parsing");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Characters");
                outlineTemplate.AppendLine("- Protagonists");
                outlineTemplate.AppendLine("  - Character Name - Brief description of role and personality");
                outlineTemplate.AppendLine("- Antagonists");
                outlineTemplate.AppendLine("  - Villain Name - Brief description of role and motivations");
                outlineTemplate.AppendLine("- Supporting Characters");
                outlineTemplate.AppendLine("  - Support Character - Brief description");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Outline");
                outlineTemplate.AppendLine("## Chapter 1");
                outlineTemplate.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
                outlineTemplate.AppendLine("- Key event or action that occurs");
                outlineTemplate.AppendLine("- Character development moment");
                outlineTemplate.AppendLine("- Plot advancement");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("## Chapter 2");
                outlineTemplate.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
                outlineTemplate.AppendLine("- Key event or action that occurs");
                outlineTemplate.AppendLine("- Character development moment");
                outlineTemplate.AppendLine("- Plot advancement");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("## Chapter 3");
                outlineTemplate.AppendLine("Brief summary of what happens in this chapter (2-4 sentences)");
                outlineTemplate.AppendLine("- Key event or action that occurs");
                outlineTemplate.AppendLine("- Character development moment");
                outlineTemplate.AppendLine("- Plot advancement");
                outlineTemplate.AppendLine();
                
                // Create outline file
                await AddNewFile(parentPath, outlineName, outlineTemplate.ToString());

                // Create rules file if name provided
                if (rulesName != null && !File.Exists(Path.Combine(parentPath, rulesName)))
                {
                    await AddNewFile(parentPath, rulesName, "# Rules: " + Path.GetFileNameWithoutExtension(outlineName) + "\n\n");
                }

                // Create style guide file if name provided
                if (styleName != null && !File.Exists(Path.Combine(parentPath, styleName)))
                {
                    await AddNewFile(parentPath, styleName, "# Style Guide: " + Path.GetFileNameWithoutExtension(outlineName) + "\n\n");
                }
            }
        }

        private async void NewNonFiction_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get the main manuscript title
            var titleDialog = new InputDialog("New Non-Fiction Project", "Enter the title for your non-fiction work:");
            if (titleDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(titleDialog.InputText))
                return;

            var projectTitle = titleDialog.InputText;
            var safeTitle = string.Join("_", projectTitle.Split(Path.GetInvalidFileNameChars()));

            // Create file names
            var manuscriptName = $"{safeTitle}.md";
            var outlineName = $"{safeTitle}_Outline.md";
            var styleName = $"{safeTitle}_Style.md";
            var rulesName = $"{safeTitle}_Rules.md";
            var researchName = $"{safeTitle}_Research.md";
            var sourcesName = $"{safeTitle}_Sources.md";
            var timelineName = $"{safeTitle}_Timeline.md";

            // Ask for non-fiction type
            var typeDialog = new NonFictionTypeDialog();
            var nonfictionType = "general";
            var subjectMatter = "";
            var timePeriod = "";
            
            if (typeDialog.ShowDialog() == true)
            {
                nonfictionType = typeDialog.SelectedType;
                subjectMatter = typeDialog.SubjectMatter;
                timePeriod = typeDialog.TimePeriod;
            }

            try
            {
                // Create main manuscript file with proper frontmatter
                var manuscriptTemplate = new StringBuilder();
                
                // Add frontmatter
                manuscriptTemplate.AppendLine("---");
                manuscriptTemplate.AppendLine("type: nonfiction");
                manuscriptTemplate.AppendLine($"subtype: {nonfictionType}");
                manuscriptTemplate.AppendLine($"title: {projectTitle}");
                manuscriptTemplate.AppendLine("author: ");
                
                if (!string.IsNullOrEmpty(subjectMatter))
                {
                    manuscriptTemplate.AppendLine($"subject: {subjectMatter}");
                }
                
                if (!string.IsNullOrEmpty(timePeriod))
                {
                    manuscriptTemplate.AppendLine($"time_period: {timePeriod}");
                }
                
                // Add ref statements for associated files
                manuscriptTemplate.AppendLine($"outline: {outlineName}");
                manuscriptTemplate.AppendLine($"style: {styleName}");
                manuscriptTemplate.AppendLine($"rules: {rulesName}");
                manuscriptTemplate.AppendLine($"research: {researchName}");
                manuscriptTemplate.AppendLine($"sources: {sourcesName}");
                manuscriptTemplate.AppendLine($"timeline: {timelineName}");
                
                // Close frontmatter
                manuscriptTemplate.AppendLine("---");
                manuscriptTemplate.AppendLine();
                
                // Add basic manuscript structure
                manuscriptTemplate.AppendLine($"# {projectTitle}");
                manuscriptTemplate.AppendLine();
                
                if (nonfictionType == "biography" || nonfictionType == "autobiography")
                {
                    manuscriptTemplate.AppendLine("## Early Life");
                    manuscriptTemplate.AppendLine();
                    manuscriptTemplate.AppendLine("## Career");
                    manuscriptTemplate.AppendLine();
                    manuscriptTemplate.AppendLine("## Legacy");
                    manuscriptTemplate.AppendLine();
                }
                else
                {
                    manuscriptTemplate.AppendLine("## Introduction");
                    manuscriptTemplate.AppendLine();
                    manuscriptTemplate.AppendLine("## Chapter 1");
                    manuscriptTemplate.AppendLine();
                    manuscriptTemplate.AppendLine("## Conclusion");
                    manuscriptTemplate.AppendLine();
                }

                // Create outline file
                var outlineTemplate = new StringBuilder();
                outlineTemplate.AppendLine("---");
                outlineTemplate.AppendLine("type: outline");
                outlineTemplate.AppendLine($"title: {projectTitle} - Outline");
                outlineTemplate.AppendLine("author: ");
                outlineTemplate.AppendLine($"nonfiction_type: {nonfictionType}");
                if (!string.IsNullOrEmpty(subjectMatter))
                {
                    outlineTemplate.AppendLine($"subject: {subjectMatter}");
                }
                outlineTemplate.AppendLine($"style: {styleName}");
                outlineTemplate.AppendLine($"rules: {rulesName}");
                outlineTemplate.AppendLine("---");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Overall Synopsis");
                outlineTemplate.AppendLine($"- Brief summary of the {nonfictionType} work covering {subjectMatter ?? "the main subject"}");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Key Themes");
                outlineTemplate.AppendLine("- Major themes and topics to be covered");
                outlineTemplate.AppendLine("- Important messages and takeaways");
                outlineTemplate.AppendLine();
                outlineTemplate.AppendLine("# Structure");
                outlineTemplate.AppendLine("## Introduction");
                outlineTemplate.AppendLine("- Set the stage and introduce the subject");
                outlineTemplate.AppendLine("- Establish context and importance");
                outlineTemplate.AppendLine();
                
                if (nonfictionType == "biography" || nonfictionType == "autobiography")
                {
                    outlineTemplate.AppendLine("## Early Life");
                    outlineTemplate.AppendLine("- Birth and family background");
                    outlineTemplate.AppendLine("- Childhood experiences and formative events");
                    outlineTemplate.AppendLine("- Education and early influences");
                    outlineTemplate.AppendLine();
                    outlineTemplate.AppendLine("## Career and Achievements");
                    outlineTemplate.AppendLine("- Professional development");
                    outlineTemplate.AppendLine("- Major accomplishments and contributions");
                    outlineTemplate.AppendLine("- Challenges and setbacks");
                    outlineTemplate.AppendLine();
                    outlineTemplate.AppendLine("## Personal Life");
                    outlineTemplate.AppendLine("- Relationships and family");
                    outlineTemplate.AppendLine("- Personal struggles and triumphs");
                    outlineTemplate.AppendLine("- Character and personality");
                    outlineTemplate.AppendLine();
                    outlineTemplate.AppendLine("## Legacy and Impact");
                    outlineTemplate.AppendLine("- Lasting contributions and influence");
                    outlineTemplate.AppendLine("- How they are remembered");
                    outlineTemplate.AppendLine("- Lessons from their life");
                }
                else
                {
                    outlineTemplate.AppendLine("## Main Content Sections");
                    outlineTemplate.AppendLine("- Key points to be covered");
                    outlineTemplate.AppendLine("- Supporting evidence and examples");
                    outlineTemplate.AppendLine("- Logical flow and progression");
                    outlineTemplate.AppendLine();
                    outlineTemplate.AppendLine("## Conclusion");
                    outlineTemplate.AppendLine("- Summary of main points");
                    outlineTemplate.AppendLine("- Call to action or final thoughts");
                }

                // Create style guide file
                var styleTemplate = new StringBuilder();
                styleTemplate.AppendLine($"# Style Guide: {projectTitle}");
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("## Voice and Tone");
                
                switch (nonfictionType)
                {
                    case "biography":
                        styleTemplate.AppendLine("- Respectful and engaging narrative voice");
                        styleTemplate.AppendLine("- Balance between factual accuracy and compelling storytelling");
                        styleTemplate.AppendLine("- Third-person perspective with objective but empathetic tone");
                        break;
                    case "autobiography":
                        styleTemplate.AppendLine("- Authentic personal voice");
                        styleTemplate.AppendLine("- First-person perspective with honest reflection");
                        styleTemplate.AppendLine("- Balance between humility and confidence");
                        break;
                    case "academic":
                        styleTemplate.AppendLine("- Formal, scholarly tone");
                        styleTemplate.AppendLine("- Objective and analytical approach");
                        styleTemplate.AppendLine("- Evidence-based argumentation");
                        break;
                    default:
                        styleTemplate.AppendLine("- Clear, accessible prose");
                        styleTemplate.AppendLine("- Informative yet engaging tone");
                        styleTemplate.AppendLine("- Appropriate level of formality for the subject matter");
                        break;
                }
                
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("## Research and Accuracy");
                styleTemplate.AppendLine("- All facts must be verifiable and properly sourced");
                styleTemplate.AppendLine("- Cross-reference multiple sources when possible");
                styleTemplate.AppendLine("- Note areas requiring further fact-checking");
                styleTemplate.AppendLine("- Maintain objectivity while telling the story");
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("## Citation Style");
                styleTemplate.AppendLine("- Use appropriate citation format (APA, MLA, Chicago, etc.)");
                styleTemplate.AppendLine("- Include in-text citations for all borrowed material");
                styleTemplate.AppendLine("- Maintain comprehensive bibliography");
                styleTemplate.AppendLine();
                styleTemplate.AppendLine("## Writing Sample");
                styleTemplate.AppendLine("(Add a sample paragraph showing the desired tone and style)");

                // Create rules file
                var rulesTemplate = new StringBuilder();
                rulesTemplate.AppendLine($"# Rules & Facts: {projectTitle}");
                rulesTemplate.AppendLine();
                rulesTemplate.AppendLine("## Core Facts");
                rulesTemplate.AppendLine("- Key factual information that must be accurately represented");
                rulesTemplate.AppendLine("- Important dates, names, and events");
                rulesTemplate.AppendLine("- Verifiable claims and assertions");
                rulesTemplate.AppendLine();
                rulesTemplate.AppendLine("## Research Standards");
                rulesTemplate.AppendLine("- Primary sources preferred over secondary sources");
                rulesTemplate.AppendLine("- Multiple source verification for controversial claims");
                rulesTemplate.AppendLine("- Clear distinction between fact and interpretation");
                rulesTemplate.AppendLine();
                rulesTemplate.AppendLine("## Ethical Guidelines");
                rulesTemplate.AppendLine("- Respect privacy of living individuals");
                rulesTemplate.AppendLine("- Handle sensitive material with appropriate care");
                rulesTemplate.AppendLine("- Maintain integrity and honesty in presentation");

                // Create research file
                var researchTemplate = new StringBuilder();
                researchTemplate.AppendLine($"# Research Notes: {projectTitle}");
                researchTemplate.AppendLine();
                researchTemplate.AppendLine("## Primary Sources");
                researchTemplate.AppendLine("- Letters, diaries, official documents");
                researchTemplate.AppendLine("- Interviews and first-hand accounts");
                researchTemplate.AppendLine("- Original records and archives");
                researchTemplate.AppendLine();
                researchTemplate.AppendLine("## Secondary Sources");
                researchTemplate.AppendLine("- Books, articles, and scholarly works");
                researchTemplate.AppendLine("- Documentaries and media coverage");
                researchTemplate.AppendLine("- Expert analysis and commentary");
                researchTemplate.AppendLine();
                researchTemplate.AppendLine("## Key Facts and Details");
                researchTemplate.AppendLine("- Important dates and milestones");
                researchTemplate.AppendLine("- Significant quotes and statements");
                researchTemplate.AppendLine("- Contextual information and background");

                // Create sources file
                var sourcesTemplate = new StringBuilder();
                sourcesTemplate.AppendLine($"# Sources & Bibliography: {projectTitle}");
                sourcesTemplate.AppendLine();
                sourcesTemplate.AppendLine("## Books");
                sourcesTemplate.AppendLine("- Author, Title, Publisher, Year");
                sourcesTemplate.AppendLine();
                sourcesTemplate.AppendLine("## Articles");
                sourcesTemplate.AppendLine("- Author, \"Title,\" Publication, Date");
                sourcesTemplate.AppendLine();
                sourcesTemplate.AppendLine("## Online Sources");
                sourcesTemplate.AppendLine("- Website, \"Page Title,\" URL, Access Date");
                sourcesTemplate.AppendLine();
                sourcesTemplate.AppendLine("## Archives and Collections");
                sourcesTemplate.AppendLine("- Institution, Collection Name, Document Details");
                sourcesTemplate.AppendLine();
                sourcesTemplate.AppendLine("## Interviews");
                sourcesTemplate.AppendLine("- Name, Position, Interview Date, Format");

                // Create timeline file
                var timelineTemplate = new StringBuilder();
                timelineTemplate.AppendLine($"# Timeline: {projectTitle}");
                timelineTemplate.AppendLine();
                timelineTemplate.AppendLine("## Chronological Events");
                
                if (!string.IsNullOrEmpty(timePeriod))
                {
                    timelineTemplate.AppendLine($"### {timePeriod}");
                }
                
                timelineTemplate.AppendLine("- Date: Event description");
                timelineTemplate.AppendLine("- Date: Another significant event");
                timelineTemplate.AppendLine();
                timelineTemplate.AppendLine("## Key Milestones");
                timelineTemplate.AppendLine("- Birth/founding dates");
                timelineTemplate.AppendLine("- Major achievements");
                timelineTemplate.AppendLine("- Significant changes or transitions");
                timelineTemplate.AppendLine("- Death/conclusion dates");
                timelineTemplate.AppendLine();
                timelineTemplate.AppendLine("## Historical Context");
                timelineTemplate.AppendLine("- Contemporary events and developments");
                timelineTemplate.AppendLine("- Social and political climate");
                timelineTemplate.AppendLine("- Cultural and technological factors");

                // Create all files
                await AddNewFile(parentPath, manuscriptName, manuscriptTemplate.ToString());
                await AddNewFile(parentPath, outlineName, outlineTemplate.ToString());
                await AddNewFile(parentPath, styleName, styleTemplate.ToString());
                await AddNewFile(parentPath, rulesName, rulesTemplate.ToString());
                await AddNewFile(parentPath, researchName, researchTemplate.ToString());
                await AddNewFile(parentPath, sourcesName, sourcesTemplate.ToString());
                await AddNewFile(parentPath, timelineName, timelineTemplate.ToString());

                MessageBox.Show($"Non-fiction project '{projectTitle}' created successfully!\n\nFiles created:\n- {manuscriptName} (main manuscript)\n- {outlineName}\n- {styleName}\n- {rulesName}\n- {researchName}\n- {sourcesName}\n- {timelineName}", 
                    "Project Created", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating non-fiction project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New Project", "Enter project name:");
            if (dialog.ShowDialog() == true)
            {
                var fileName = dialog.InputText;
                if (!fileName.EndsWith(".project"))
                {
                    fileName += ".project";
                }

                // Create a new project with default values
                var project = new Project
                {
                    Title = dialog.InputText,
                    Goal = "",
                    CreatedDate = DateTime.Now,
                    LastModifiedDate = DateTime.Now
                };

                var settings = new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Include
                };
                var json = JsonConvert.SerializeObject(project, settings);
                await AddNewFile(parentPath, fileName, json);
            }
        }

        private async void Delete_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Delete_Click called");
                
                var menuItem = sender as MenuItem;
                System.Diagnostics.Debug.WriteLine($"MenuItem: {menuItem?.Name ?? "null"}");
                
                var contextMenu = menuItem?.Parent as ContextMenu;
                System.Diagnostics.Debug.WriteLine($"ContextMenu: {(contextMenu != null ? "found" : "null")}");
                
                var treeViewItem = contextMenu?.PlacementTarget as TreeViewItem;
                System.Diagnostics.Debug.WriteLine($"TreeViewItem: {(treeViewItem != null ? "found" : "null")}");
                
                var libraryItem = treeViewItem?.DataContext as LibraryTreeItem;
                System.Diagnostics.Debug.WriteLine($"LibraryItem: {libraryItem?.Name ?? "null"}");

                if (libraryItem != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempting to delete: {libraryItem.Name} at {libraryItem.Path}");
                    await DeleteItem(libraryItem);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No library item found to delete");
                    
                    // Alternative approach: try to get the selected item directly
                    var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
                    if (selectedItem != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"Using selected item instead: {selectedItem.Name}");
                        await DeleteItem(selectedItem);
                    }
                    else
                    {
                        MessageBox.Show("Unable to determine which item to delete. Please try selecting the item first.", 
                            "Delete Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in Delete_Click: {ex.Message}");
                MessageBox.Show($"Error during delete operation: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LibraryTreeView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _dragStartPoint = e.GetPosition(null);
        }

        private void LibraryTreeView_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && !_isDragging)
            {
                Point position = e.GetPosition(null);
                if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    StartDrag(e);
                }
            }
        }

        private void StartDrag(MouseEventArgs e)
        {
            var treeViewItem = FindParent<TreeViewItem>((DependencyObject)e.OriginalSource);
            if (treeViewItem == null) return;

            var item = treeViewItem.DataContext as LibraryTreeItem;
            if (item == null) return;

            // Don't allow dragging of special items
            if (item.Type == Models.LibraryItemType.Service || item.Type == Models.LibraryItemType.VirtualToDos)
                return;

            _isDragging = true;
            DragDrop.DoDragDrop(treeViewItem, item, DragDropEffects.Move);
            _isDragging = false;
        }

        private void LibraryTreeView_DragOver(object sender, DragEventArgs e)
        {
            var targetItem = GetTreeViewItemFromPoint(e.GetPosition(LibraryTreeView));
            
            // Clear previous drag over state
            if (_lastDragOverItem != null)
            {
                SetIsDragOver(_lastDragOverItem, false);
                _lastDragOverItem = null;
            }

            if (targetItem == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            var sourceItem = e.Data.GetData(typeof(LibraryTreeItem)) as LibraryTreeItem;
            var targetData = targetItem.DataContext as LibraryTreeItem;

            if (sourceItem == null || targetData == null)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            // Don't allow dropping on itself or non-directory items
            if (sourceItem == targetData || targetData.Type != Models.LibraryItemType.Directory)
            {
                e.Effects = DragDropEffects.None;
            }
            else
            {
                e.Effects = DragDropEffects.Move;
                // Set drag over state on valid target
                SetIsDragOver(targetItem, true);
                _lastDragOverItem = targetItem;
            }

            e.Handled = true;
        }

        private async void LibraryTreeView_Drop(object sender, DragEventArgs e)
        {
            try
            {
                // Clear any remaining drag over state
                if (_lastDragOverItem != null)
                {
                    SetIsDragOver(_lastDragOverItem, false);
                    _lastDragOverItem = null;
                }

                var targetItem = GetTreeViewItemFromPoint(e.GetPosition(LibraryTreeView));
                if (targetItem == null) return;

                var sourceItem = e.Data.GetData(typeof(LibraryTreeItem)) as LibraryTreeItem;
                var targetData = targetItem.DataContext as LibraryTreeItem;

                if (sourceItem == null || targetData == null || sourceItem == targetData)
                    return;

                // Only allow dropping into directories
                if (targetData.Type != Models.LibraryItemType.Directory)
                    return;

                var sourcePath = sourceItem.Path;
                var targetPath = Path.Combine(targetData.Path, Path.GetFileName(sourceItem.Path));

                // Don't move if source and target are the same
                if (sourcePath == targetPath)
                    return;

                // Check if target path already exists
                if (File.Exists(targetPath) || Directory.Exists(targetPath))
                {
                    MessageBox.Show($"An item with the name '{Path.GetFileName(sourcePath)}' already exists in the destination folder.",
                        "Cannot Move Item", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Move the file or directory
                if (sourceItem.Type == Models.LibraryItemType.File)
                {
                    // Use LibraryManager to move file (this handles versions)
                    Universa.Desktop.LibraryManager.Instance.MoveFile(sourcePath, targetPath);
                    
                    // Update the tree without full refresh
                    var sourceParent = FindParentItem(sourceItem);
                    if (sourceParent != null && sourceParent.Children != null)
                    {
                        sourceParent.Children.Remove(sourceItem);
                    }
                    
                    if (targetData.Children == null)
                    {
                        targetData.Children = new ObservableCollection<LibraryTreeItem>();
                    }
                    sourceItem.Path = targetPath;
                    targetData.Children.Add(sourceItem);

                    // Get the library path safely
                    var libraryPath = _configService?.Provider?.LibraryPath;
                    if (string.IsNullOrEmpty(libraryPath))
                    {
                        MessageBox.Show("Library path is not configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Sync the moved file
                    var relativePath = Path.GetRelativePath(libraryPath, targetPath);
                    await Managers.SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                }
                else if (sourceItem.Type == Models.LibraryItemType.Directory)
                {
                    Directory.Move(sourcePath, targetPath);
                    
                    // Update the tree without full refresh
                    var sourceParent = FindParentItem(sourceItem);
                    if (sourceParent != null && sourceParent.Children != null)
                    {
                        sourceParent.Children.Remove(sourceItem);
                    }
                    
                    if (targetData.Children == null)
                    {
                        targetData.Children = new ObservableCollection<LibraryTreeItem>();
                    }
                    sourceItem.Path = targetPath;
                    targetData.Children.Add(sourceItem);

                    // Sync the moved directory and its contents
                    await Managers.SyncManager.GetInstance().SynchronizeAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error moving item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private LibraryTreeItem FindParentItem(LibraryTreeItem item)
        {
            foreach (var rootItem in RootItems)
            {
                var parent = FindParentItemRecursive(rootItem, item);
                if (parent != null)
                {
                    return parent;
                }
            }
            return null;
        }

        private LibraryTreeItem FindParentItemRecursive(LibraryTreeItem current, LibraryTreeItem target)
        {
            if (current.Children != null)
            {
                if (current.Children.Contains(target))
                {
                    return current;
                }

                foreach (var child in current.Children)
                {
                    var result = FindParentItemRecursive(child, target);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        private TreeViewItem GetTreeViewItemFromPoint(Point point)
        {
            DependencyObject element = LibraryTreeView.InputHitTest(point) as DependencyObject;
            while (element != null && !(element is TreeViewItem))
            {
                element = VisualTreeHelper.GetParent(element);
            }
            return element as TreeViewItem;
        }

        private void SaveExpandedStatesOnClose()
        {
            try
            {
                if (_configService?.Provider == null || _isHandlingConfigChange) return;

                var expandedPaths = new List<string>();
                foreach (var item in GetAllTreeViewItems(LibraryTreeView))
                {
                    if (item.IsExpanded)
                    {
                        var treeItem = item.DataContext as LibraryTreeItem;
                        if (treeItem != null)
                        {
                            expandedPaths.Add(treeItem.Path);
                        }
                    }
                }

                _isHandlingConfigChange = true;
                try
                {
                    _configService.Provider.ExpandedPaths = expandedPaths.ToArray();
                    _configService.Save();
                }
                finally
                {
                    _isHandlingConfigChange = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving expanded states: {ex.Message}");
            }
        }

        private IEnumerable<TreeViewItem> GetAllTreeViewItems(ItemsControl control)
        {
            for (int i = 0; i < control.Items.Count; i++)
            {
                var item = control.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (item == null)
                {
                    control.UpdateLayout();
                    item = control.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                }

                if (item != null)
                {
                    yield return item;
                    foreach (var childItem in GetAllTreeViewItems(item))
                    {
                        yield return childItem;
                    }
                }
            }
        }

        private string GetUniqueFileName(string path, string baseName, string extension)
        {
            string fullPath = Path.Combine(path, baseName + extension);
            if (!File.Exists(fullPath)) return baseName + extension;

            int counter = 1;
            while (File.Exists(Path.Combine(path, $"{baseName} ({counter}){extension}")))
            {
                counter++;
            }
            return $"{baseName} ({counter}){extension}";
        }

        private string GetUniqueFolderName(string path, string baseName)
        {
            string fullPath = Path.Combine(path, baseName);
            if (!Directory.Exists(fullPath)) return baseName;

            int counter = 1;
            while (Directory.Exists(Path.Combine(path, $"{baseName} ({counter})")))
            {
                counter++;
            }
            return $"{baseName} ({counter})";
        }

        public async Task AddNewFolder(string parentPath, string folderName)
        {
            try
            {
                // Get unique folder name
                folderName = GetUniqueFolderName(parentPath, folderName);
                var newFolderPath = Path.Combine(parentPath, folderName);
                Directory.CreateDirectory(newFolderPath);

                var libraryPath = _configService?.Provider?.LibraryPath;
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    // Sync the new folder
                    var relativePath = Path.GetRelativePath(libraryPath, newFolderPath);
                    await Managers.SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                }

                // Add folder to tree without full refresh
                await AddFolderToTreeView(parentPath, folderName, newFolderPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task AddFileToTreeView(string parentPath, string fileName, string filePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Adding file to tree view: {fileName} in {parentPath}");
                
                // Find the parent directory item in the tree
                var parentItem = FindLibraryItemByPath(parentPath);
                if (parentItem != null && parentItem.Children != null)
                {
                    // Create new file item
                    var newFileItem = new LibraryTreeItem
                    {
                        Name = fileName,
                        Path = filePath,
                        Type = Models.LibraryItemType.File,
                        Icon = "📄"
                    };
                    
                    // Add to parent's children collection
                    parentItem.Children.Add(newFileItem);
                    System.Diagnostics.Debug.WriteLine($"Successfully added {fileName} to tree view");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Could not find parent item for {parentPath}, falling back to refresh");
                    // Fallback to refresh if we can't find the parent
                    await RefreshItems();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding file to tree view: {ex.Message}");
                // Fallback to refresh on error
                await RefreshItems();
            }
        }

        private async Task AddFolderToTreeView(string parentPath, string folderName, string folderPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Adding folder to tree view: {folderName} in {parentPath}");
                
                // Find the parent directory item in the tree
                var parentItem = FindLibraryItemByPath(parentPath);
                if (parentItem != null && parentItem.Children != null)
                {
                    // Create new folder item
                    var newFolderItem = new LibraryTreeItem
                    {
                        Name = folderName,
                        Path = folderPath,
                        Type = Models.LibraryItemType.Directory,
                        Icon = "📁",
                        Children = new ObservableCollection<LibraryTreeItem>()
                    };
                    
                    // Add a dummy item to show expand arrow
                    newFolderItem.Children.Add(new LibraryTreeItem { Path = null });
                    
                    // Add to parent's children collection
                    parentItem.Children.Add(newFolderItem);
                    System.Diagnostics.Debug.WriteLine($"Successfully added {folderName} to tree view");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Could not find parent item for {parentPath}, falling back to refresh");
                    // Fallback to refresh if we can't find the parent
                    await RefreshItems();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error adding folder to tree view: {ex.Message}");
                // Fallback to refresh on error
                await RefreshItems();
            }
        }

        private LibraryTreeItem FindLibraryItemByPath(string path)
        {
            System.Diagnostics.Debug.WriteLine($"Searching for library item with path: {path}");
            
            foreach (var rootItem in RootItems)
            {
                var found = FindLibraryItemByPathRecursive(rootItem, path);
                if (found != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Found library item: {found.Name}");
                    return found;
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"Could not find library item for path: {path}");
            return null;
        }

        private LibraryTreeItem FindLibraryItemByPathRecursive(LibraryTreeItem current, string path)
        {
            if (current.Path == path)
            {
                return current;
            }
            
            if (current.Children != null)
            {
                foreach (var child in current.Children)
                {
                    var result = FindLibraryItemByPathRecursive(child, path);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            
            return null;
        }

        private async Task RemoveItemFromTreeView(LibraryTreeItem itemToRemove)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Removing item from tree view: {itemToRemove.Name} at {itemToRemove.Path}");
                
                // Find the parent item
                var parentItem = FindParentLibraryItem(itemToRemove);
                if (parentItem != null && parentItem.Children != null)
                {
                    // Remove from parent's children collection
                    parentItem.Children.Remove(itemToRemove);
                    System.Diagnostics.Debug.WriteLine($"Successfully removed {itemToRemove.Name} from tree view");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Could not find parent item for {itemToRemove.Name}, falling back to refresh");
                    // Fallback to refresh if we can't find the parent
                    await RefreshItems();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error removing item from tree view: {ex.Message}");
                // Fallback to refresh on error
                await RefreshItems();
            }
        }

        private LibraryTreeItem FindParentLibraryItem(LibraryTreeItem itemToFind)
        {
            foreach (var rootItem in RootItems)
            {
                var parent = FindParentLibraryItemRecursive(rootItem, itemToFind);
                if (parent != null)
                {
                    return parent;
                }
            }
            return null;
        }

        private LibraryTreeItem FindParentLibraryItemRecursive(LibraryTreeItem current, LibraryTreeItem itemToFind)
        {
            if (current.Children != null)
            {
                if (current.Children.Contains(itemToFind))
                {
                    return current;
                }

                foreach (var child in current.Children)
                {
                    var result = FindParentLibraryItemRecursive(child, itemToFind);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }
            return null;
        }

        public async Task AddNewFile(string parentPath, string fileName, string template = "")
        {
            try
            {
                // Get unique file name
                string baseName = Path.GetFileNameWithoutExtension(fileName);
                string extension = Path.GetExtension(fileName);
                fileName = GetUniqueFileName(parentPath, baseName, extension);
                
                var filePath = Path.Combine(parentPath, fileName);
                File.WriteAllText(filePath, template);

                var libraryPath = _configService?.Provider?.LibraryPath;
                if (!string.IsNullOrEmpty(libraryPath))
                {
                    // Sync the new file
                    var relativePath = Path.GetRelativePath(libraryPath, filePath);
                    await Managers.SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                }

                // If it's an org file, notify any relevant trackers
                if (fileName.EndsWith(".org", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".todo", StringComparison.OrdinalIgnoreCase))
                {
                    // Legacy support for .todo files
                    if (fileName.EndsWith(".todo", StringComparison.OrdinalIgnoreCase))
                    {
                        await ToDoTracker.Instance.ScanTodoFilesAsync(parentPath);
                    }
                }

                // Add file to tree without full refresh
                await AddFileToTreeView(parentPath, fileName, filePath);

                // Open the new file in a tab
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.OpenFileInEditor(filePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task DeleteItem(LibraryTreeItem item)
        {
            try
            {
                if (item == null) return;

                // Don't allow deletion of special items
                if (item.Type == Models.LibraryItemType.Service || item.Type == Models.LibraryItemType.VirtualToDos)
                {
                    MessageBox.Show("Cannot delete this item.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string message = item.Type == Models.LibraryItemType.Directory
                    ? $"Are you sure you want to delete the folder '{item.Name}' and all its contents?"
                    : $"Are you sure you want to delete '{item.Name}'?";

                var result = MessageBox.Show(
                    message,
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result == MessageBoxResult.Yes)
                {
                    // Save current expanded states before deletion - ensure we preserve parent folder expansion
                    var expandedStates = GetExpandedStates() ?? new Dictionary<string, bool>();
                    
                    // Ensure the parent directory of the deleted item remains expanded
                    var parentDir = Path.GetDirectoryName(item.Path);
                    if (!string.IsNullOrEmpty(parentDir) && Directory.Exists(parentDir))
                    {
                        expandedStates[parentDir] = true;
                        
                        // Also ensure all ancestor directories remain expanded
                        var libraryPath = _configService?.Provider?.LibraryPath;
                        if (!string.IsNullOrEmpty(libraryPath))
                        {
                            var current = parentDir;
                            while (!string.IsNullOrEmpty(current) && current.StartsWith(libraryPath, StringComparison.OrdinalIgnoreCase) && current != libraryPath)
                            {
                                expandedStates[current] = true;
                                current = Path.GetDirectoryName(current);
                            }
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Preserved {expandedStates.Count} expanded states before deletion");

                    // Get list of files to be deleted (for closing tabs)
                    var filesToDelete = new List<string>();
                    if (item.Type == Models.LibraryItemType.Directory)
                    {
                        filesToDelete.AddRange(Directory.GetFiles(item.Path, "*.*", SearchOption.AllDirectories));
                        Directory.Delete(item.Path, true);
                        
                        // Remove the deleted directory and all its subdirectories from expanded states
                        var pathsToRemove = expandedStates.Keys
                            .Where(path => path.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
                            .ToList();
                        foreach (var path in pathsToRemove)
                        {
                            expandedStates.Remove(path);
                        }
                    }
                    else
                    {
                        filesToDelete.Add(item.Path);
                        
                        // Check if this is a versioned file and delete its version history
                        if (Universa.Desktop.LibraryManager.Instance.IsVersionedFile(item.Path))
                        {
                            try
                            {
                                var relativePath = Universa.Desktop.LibraryManager.Instance.GetRelativePath(item.Path);
                                var historyPath = Path.Combine(Configuration.Instance.LibraryPath, ".versions");
                                
                                System.Diagnostics.Debug.WriteLine($"Deleting version files for: {item.Name}");
                                System.Diagnostics.Debug.WriteLine($"Relative path: {relativePath}");
                                System.Diagnostics.Debug.WriteLine($"History path: {historyPath}");
                                
                                if (Directory.Exists(historyPath))
                                {
                                    // Find all version files for this file using multiple search patterns
                                    var versionFiles = new List<string>();
                                    
                                    // Search pattern 1: exact relative path with timestamp
                                    var pattern1Files = Directory.GetFiles(historyPath, $"{relativePath}.*", SearchOption.AllDirectories);
                                    versionFiles.AddRange(pattern1Files);
                                    
                                    // Search pattern 2: filename-based search (in case path structure differs)
                                    var fileName = Path.GetFileName(item.Path);
                                    var pattern2Files = Directory.GetFiles(historyPath, $"{fileName}.*", SearchOption.AllDirectories);
                                    versionFiles.AddRange(pattern2Files);
                                    
                                    // Search pattern 3: Look for any files containing the relative path
                                    var allVersionFiles = Directory.GetFiles(historyPath, "*.*", SearchOption.AllDirectories);
                                    var pattern3Files = allVersionFiles.Where(f => 
                                        Path.GetFileName(f).StartsWith(relativePath.Replace(Path.DirectorySeparatorChar, '_')) ||
                                        Path.GetFileName(f).StartsWith(relativePath.Replace(Path.DirectorySeparatorChar, '.')) ||
                                        f.Contains(relativePath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ||
                                        f.Contains(relativePath)
                                    );
                                    versionFiles.AddRange(pattern3Files);
                                    
                                    // Remove duplicates and delete
                                    var uniqueVersionFiles = versionFiles.Distinct().ToList();
                                    
                                    System.Diagnostics.Debug.WriteLine($"Found {uniqueVersionFiles.Count} version files to delete:");
                                    foreach (var versionFile in uniqueVersionFiles)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"  - {versionFile}");
                                        if (File.Exists(versionFile))
                                        {
                                            File.Delete(versionFile);
                                            System.Diagnostics.Debug.WriteLine($"    Deleted: {versionFile}");
                                        }
                                    }
                                    
                                    System.Diagnostics.Debug.WriteLine($"Successfully deleted {uniqueVersionFiles.Count} version files for {item.Name}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"History path does not exist: {historyPath}");
                                }
                            }
                            catch (Exception versionEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting version files: {versionEx.Message}");
                                System.Diagnostics.Debug.WriteLine($"Stack trace: {versionEx.StackTrace}");
                                // Don't prevent the main file deletion if version cleanup fails
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"File {item.Name} is not tracked as a versioned file");
                        }
                        
                        // Delete the main file
                        File.Delete(item.Path);
                    }

                    // Trigger a sync to update the server
                    await Managers.SyncManager.GetInstance().SynchronizeAsync();

                    // Close any open tabs for deleted files
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    if (mainWindow != null)
                    {
                        var tabsToClose = mainWindow.MainTabControl.Items.OfType<TabItem>()
                            .Where(tab => tab.Content is IFileTab fileTab && 
                                   fileTab.FilePath.StartsWith(item.Path, StringComparison.OrdinalIgnoreCase))
                            .ToList();

                        foreach (var tab in tabsToClose)
                        {
                            mainWindow.MainTabControl.Items.Remove(tab);
                        }
                    }

                    // If it's a todo file, notify the tracker
                    if (item.Type == Models.LibraryItemType.File && item.Name.EndsWith(".todo", StringComparison.OrdinalIgnoreCase))
                    {
                        await ToDoTracker.Instance.ScanTodoFilesAsync(Path.GetDirectoryName(item.Path));
                    }

                    // Remove item from tree without full refresh
                    await RemoveItemFromTreeView(item);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error deleting item: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void TreeViewItem_Expanded(object sender, RoutedEventArgs e)
        {
            var treeViewItem = e.OriginalSource as TreeViewItem;
            if (treeViewItem?.DataContext is LibraryTreeItem item && item.Type == Models.LibraryItemType.Directory)
            {
                // Skip if we're currently refreshing
                if (_isRefreshing) return;

                // Initialize Children collection if null
                if (item.Children == null)
                {
                    item.Children = new ObservableCollection<LibraryTreeItem>();
                }

                // Only load if we have a dummy item
                if (item.Children.Count == 1 && item.Children[0].Path == null)
                {
                    item.Children.Clear();
                    await LoadDirectory(item.Path, item.Children);
                }
            }
        }

        private async Task SelectAndExpandToPath(string path)
        {
            await RefreshItems();
            var treeItem = FindTreeViewItemForPath(path);
            if (treeItem != null)
            {
                treeItem.IsExpanded = true;
                if (treeItem.DataContext is LibraryTreeItem item)
                {
                    if (item.Children == null)
                    {
                        item.Children = new ObservableCollection<LibraryTreeItem>();
                    }
                    await LoadDirectory(path, item.Children);
                }
            }
        }

        private Dictionary<string, bool> GetExpandedStates()
        {
            var states = new Dictionary<string, bool>();
            System.Diagnostics.Debug.WriteLine("Collecting expanded states...");
            
            // Force update layout to ensure all containers are generated
            LibraryTreeView.UpdateLayout();
            
            // Use a more direct approach to collect expanded states
            CollectExpandedStatesFromTreeView(LibraryTreeView.Items, states);
            
            System.Diagnostics.Debug.WriteLine($"Collected {states.Count} expanded states");
            foreach (var kvp in states)
            {
                System.Diagnostics.Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }
            
            return states;
        }

        private void CollectExpandedStatesFromTreeView(ItemCollection items, Dictionary<string, bool> states)
        {
            if (items == null) return;
            
            for (int i = 0; i < items.Count; i++)
            {
                var container = LibraryTreeView.ItemContainerGenerator.ContainerFromIndex(i) as TreeViewItem;
                if (container?.DataContext is LibraryTreeItem libraryItem && 
                    libraryItem.Type == Models.LibraryItemType.Directory && 
                    !string.IsNullOrEmpty(libraryItem.Path))
                {
                    states[libraryItem.Path] = container.IsExpanded;
                    System.Diagnostics.Debug.WriteLine($"Collected state: {libraryItem.Path} = {container.IsExpanded}");
                    
                    // Recursively collect from children if expanded
                    if (container.IsExpanded && container.Items != null)
                    {
                        CollectExpandedStatesFromTreeView(container.Items, states);
                    }
                }
            }
        }

        private void CollectExpandedStates(ObservableCollection<LibraryTreeItem> items, Dictionary<string, bool> states)
        {
            if (items == null) return;
            
            foreach (var item in items)
            {
                if (item?.Type == Models.LibraryItemType.Directory && !string.IsNullOrEmpty(item.Path))
                {
                    var tvi = FindTreeViewItemForPath(item.Path);
                    if (tvi != null)
                    {
                        states[item.Path] = tvi.IsExpanded;
                    }
                }
                if (item?.Children != null)
                {
                    CollectExpandedStates(item.Children, states);
                }
            }
        }

        private TreeViewItem FindTreeViewItemForPath(string path)
        {
            foreach (var item in LibraryTreeView.Items)
            {
                var tvi = LibraryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (tvi != null)
                {
                    var found = FindTreeViewItemForPathRecursive(tvi, path);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private TreeViewItem FindTreeViewItemForPathRecursive(TreeViewItem item, string path)
        {
            var libraryItem = item.DataContext as LibraryTreeItem;
            if (libraryItem != null && libraryItem.Path == path)
            {
                return item;
            }

            if (item.HasItems)
            {
                foreach (var child in item.Items)
                {
                    var childItem = item.ItemContainerGenerator.ContainerFromItem(child) as TreeViewItem;
                    if (childItem != null)
                    {
                        var found = FindTreeViewItemForPathRecursive(childItem, path);
                        if (found != null) return found;
                    }
                }
            }

            return null;
        }

        private async void Rename_Click(object sender, RoutedEventArgs e)
        {
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            if (selectedItem == null || selectedItem.Type == Models.LibraryItemType.Service) return;

            if (string.IsNullOrEmpty(selectedItem.Path))
            {
                MessageBox.Show("Cannot rename item: path is missing.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new InputDialog("Rename", $"Enter new name for {selectedItem.Name}:");
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var newName = dialog.InputText;
                    if (string.IsNullOrEmpty(newName))
                    {
                        MessageBox.Show("New name cannot be empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var oldPath = selectedItem.Path;
                    var parentPath = Path.GetDirectoryName(oldPath);
                    if (string.IsNullOrEmpty(parentPath))
                    {
                        MessageBox.Show("Cannot rename root items.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    string newPath;

                    // Handle file extensions for files (not directories)
                    if (selectedItem.Type == Models.LibraryItemType.File)
                    {
                        var oldExtension = Path.GetExtension(oldPath);
                        var newExtension = Path.GetExtension(newName);

                        // If no new extension provided, keep the old one
                        if (string.IsNullOrEmpty(newExtension))
                        {
                            newName = newName + oldExtension;
                        }
                        // If a different extension is provided, ask for confirmation
                        else if (!string.Equals(oldExtension, newExtension, StringComparison.OrdinalIgnoreCase))
                        {
                            var result = MessageBox.Show(
                                $"Are you sure you want to change the file extension from {oldExtension} to {newExtension}?",
                                "Confirm Extension Change",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (result != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }
                    }

                    newPath = Path.Combine(parentPath, newName);

                    // Check if a file with the new name already exists
                    if (File.Exists(newPath) || Directory.Exists(newPath))
                    {
                        MessageBox.Show("An item with this name already exists.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Get the MainWindow instance
                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                    if (mainWindow == null) return;

                    // If it's a file and it's open in a tab, handle the tab
                    if (selectedItem.Type == Models.LibraryItemType.File)
                    {
                        var openTab = mainWindow.MainTabControl.Items.OfType<TabItem>()
                            .FirstOrDefault(tab => tab.Content is IFileTab fileTab && fileTab.FilePath == oldPath);
                        if (openTab != null)
                        {
                            // Update the tab's file path
                            if (openTab.Content is IFileTab fileTab)
                            {
                                fileTab.FilePath = newPath;
                            }
                        }
                    }

                    // For directory renames, we need to store and update expanded states
                    Dictionary<string, bool> expandedStates = null;
                    if (selectedItem.Type == Models.LibraryItemType.Directory)
                    {
                        expandedStates = GetExpandedStates();
                    }

                    // Perform the rename operation
                    if (selectedItem.Type == Models.LibraryItemType.Directory)
                    {
                        Directory.Move(oldPath, newPath);
                        
                        // Update path mappings in expanded states
                        if (expandedStates != null)
                        {
                            var updatedStates = new Dictionary<string, bool>();
                            foreach (var kvp in expandedStates)
                            {
                                string updatedPath = kvp.Key;
                                // Update paths that start with the old directory path
                                if (kvp.Key.StartsWith(oldPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    updatedPath = newPath + kvp.Key.Substring(oldPath.Length);
                                }
                                updatedStates[updatedPath] = kvp.Value;
                            }
                            expandedStates = updatedStates;
                        }
                        
                        // Sync the renamed directory and its contents
                        await Managers.SyncManager.GetInstance().SynchronizeAsync();
                    }
                    else
                    {
                        var configLibraryPath = _configService?.Provider?.LibraryPath;
                        if (!string.IsNullOrEmpty(configLibraryPath))
                        {
                            // Use LibraryManager to move file (this handles versions)
                            Universa.Desktop.LibraryManager.Instance.MoveFile(oldPath, newPath);
                            // Sync the renamed file
                            var relativePath = Path.GetRelativePath(configLibraryPath, newPath);
                            await Managers.SyncManager.GetInstance().HandleLocalFileChangeAsync(relativePath);
                        }
                    }

                    // If it's a todo file, notify the tracker
                    if (oldPath.EndsWith(".todo", StringComparison.OrdinalIgnoreCase))
                    {
                        await ToDoTracker.Instance.ScanTodoFilesAsync(Path.GetDirectoryName(oldPath));
                    }

                    // Instead of full refresh, do surgical update
                    if (selectedItem.Type == Models.LibraryItemType.File)
                    {
                        // For files, just update the name and path in the existing tree item
                        selectedItem.Name = newName;
                        selectedItem.Path = newPath;
                        
                        // Update the UI by triggering property change notification
                        OnPropertyChanged(nameof(RootItems));
                    }
                    else
                    {
                        // For directories, we need to refresh but preserve expanded states
                        await RefreshItems(true, expandedStates);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error renaming item: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void InboxItem_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (ParentMainWindow == null)
                {
                    ParentMainWindow = Application.Current.MainWindow as Views.MainWindow;
                }

                if (ParentMainWindow != null)
                {
                    ParentMainWindow.OpenInboxTab();
                    e.Handled = true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Error: Could not access main window to open Inbox tab");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in InboxItem_MouseDoubleClick: {ex.Message}");
            }
        }

        private void InboxItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is TreeViewItem item)
            {
                item.IsSelected = true;
                e.Handled = true;
            }
        }
    }

    public class LibraryNavigationEventArgs : EventArgs
    {
        public LibraryTreeItem SelectedItem { get; }

        public LibraryNavigationEventArgs(LibraryTreeItem selectedItem)
        {
            SelectedItem = selectedItem;
        }
    }
} 