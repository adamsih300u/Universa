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

        private void OnTodosChanged()
        {
            if (!_isRefreshing)
            {
                Application.Current.Dispatcher.BeginInvoke(async () => await RefreshItems(true));
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

                // Add Overview item
                RootItems.Add(new LibraryTreeItem
                {
                    Name = "Overview",
                    Icon = "📊",
                    Type = Models.LibraryItemType.Overview
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
                    await RestoreExpandedStatesRecursive(LibraryTreeView.Items, currentStates);
                }
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        private async Task RestoreExpandedStatesRecursive(ItemCollection items, Dictionary<string, bool> states)
        {
            foreach (var item in items)
            {
                var treeViewItem = LibraryTreeView.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
                if (treeViewItem == null) continue;

                var libraryItem = item as LibraryTreeItem;
                if (libraryItem?.Type == Models.LibraryItemType.Directory)
                {
                    bool shouldExpand = states.ContainsKey(libraryItem.Path) && states[libraryItem.Path];
                    if (shouldExpand)
                    {
                        // Load contents before expanding
                        if (libraryItem.Children == null)
                        {
                            libraryItem.Children = new ObservableCollection<LibraryTreeItem>();
                        }
                        await LoadDirectory(libraryItem.Path, libraryItem.Children);
                        treeViewItem.IsExpanded = true;

                        // Process children after parent is expanded
                        if (treeViewItem.Items != null)
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
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            var libraryPath = _configService?.Provider?.LibraryPath;
            var parentPath = selectedItem?.Type == Models.LibraryItemType.Directory ? selectedItem.Path : libraryPath;

            if (string.IsNullOrEmpty(parentPath))
            {
                MessageBox.Show("Please configure a library path in settings first.", "Library Path Not Set", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialog("New ToDo", "Enter ToDo name:");
            if (dialog.ShowDialog() == true)
            {
                var fileName = dialog.InputText;
                if (!fileName.EndsWith(".todo"))
                {
                    fileName += ".todo";
                }
                await AddNewFile(parentPath, fileName, "[]");
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

            var dialog = new InputDialog("New Manuscript", "Enter manuscript name:");
            if (dialog.ShowDialog() == true)
            {
                var manuscriptName = dialog.InputText;
                if (!manuscriptName.EndsWith(".md"))
                {
                    manuscriptName += ".md";
                }

                // Ask for custom file names first
                var outlineDialog = new InputDialog("Outline File", "Enter name for outline file");
                var rulesDialog = new InputDialog("Rules File", "Enter name for rules file");
                var styleDialog = new InputDialog("Style Guide", "Enter name for style guide file");

                string outlineName = null;
                string rulesName = null;
                string styleName = null;

                // Get outline name
                if (outlineDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(outlineDialog.InputText))
                {
                    outlineName = outlineDialog.InputText;
                    if (!outlineName.EndsWith(".md")) outlineName += ".md";
                }

                // Get rules name
                if (rulesDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(rulesDialog.InputText))
                {
                    rulesName = rulesDialog.InputText;
                    if (!rulesName.EndsWith(".md")) rulesName += ".md";
                }

                // Get style guide name
                if (styleDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(styleDialog.InputText))
                {
                    styleName = styleDialog.InputText;
                    if (!styleName.EndsWith(".md")) styleName += ".md";
                }

                // Create manuscript file with proper formatting and ref statements
                var manuscriptTemplate = new StringBuilder();
                
                // Add frontmatter
                manuscriptTemplate.AppendLine("---");
                manuscriptTemplate.AppendLine("#fiction");
                
                // Add ref statements for associated files if they were specified
                if (rulesName != null)
                {
                    manuscriptTemplate.AppendLine($"rules: {rulesName}");
                }
                if (styleName != null)
                {
                    manuscriptTemplate.AppendLine($"style: {styleName}");
                }
                if (outlineName != null)
                {
                    manuscriptTemplate.AppendLine($"outline: {outlineName}");
                }
                
                // Add other metadata fields
                manuscriptTemplate.AppendLine("title: " + Path.GetFileNameWithoutExtension(manuscriptName));
                manuscriptTemplate.AppendLine("author: ");
                manuscriptTemplate.AppendLine("authorfirst: ");
                manuscriptTemplate.AppendLine("authorlast: ");
                manuscriptTemplate.AppendLine("cover: ");
                
                // Close frontmatter
                manuscriptTemplate.AppendLine("---");
                manuscriptTemplate.AppendLine();
                
                // Add body section
                manuscriptTemplate.AppendLine("# " + Path.GetFileNameWithoutExtension(manuscriptName));
                manuscriptTemplate.AppendLine();
                
                // Create manuscript file
                await AddNewFile(parentPath, manuscriptName, manuscriptTemplate.ToString());

                // Create outline file if name provided
                if (outlineName != null && !File.Exists(Path.Combine(parentPath, outlineName)))
                {
                    await AddNewFile(parentPath, outlineName, "# Outline: " + Path.GetFileNameWithoutExtension(manuscriptName) + "\n\n");
                }

                // Create rules file if name provided
                if (rulesName != null && !File.Exists(Path.Combine(parentPath, rulesName)))
                {
                    await AddNewFile(parentPath, rulesName, "# Rules: " + Path.GetFileNameWithoutExtension(manuscriptName) + "\n\n");
                }

                // Create style guide file if name provided
                if (styleName != null && !File.Exists(Path.Combine(parentPath, styleName)))
                {
                    await AddNewFile(parentPath, styleName, "# Style Guide: " + Path.GetFileNameWithoutExtension(manuscriptName) + "\n\n");
                }
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
            var selectedItem = LibraryTreeView.SelectedItem as LibraryTreeItem;
            if (selectedItem == null) return;

            await DeleteItem(selectedItem);
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
                // Save current expanded states before any changes
                var expandedStates = GetExpandedStates();
                
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

                    // Ensure all parent folders are expanded
                    var current = parentPath;
                    while (!string.IsNullOrEmpty(current) && current.StartsWith(libraryPath))
                    {
                        expandedStates[current] = true;
                        current = Path.GetDirectoryName(current);
                    }
                }
                expandedStates[newFolderPath] = true;

                // Refresh with saved states
                await RefreshItems(true, expandedStates);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public async Task AddNewFile(string parentPath, string fileName, string template = "")
        {
            try
            {
                // Save current expanded states before any changes
                var expandedStates = new Dictionary<string, bool>();
                foreach (var item in GetAllTreeViewItems(LibraryTreeView))
                {
                    if (item.DataContext is LibraryTreeItem libraryItem && !string.IsNullOrEmpty(libraryItem.Path))
                    {
                        expandedStates[libraryItem.Path] = item.IsExpanded;
                    }
                }

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

                // If it's a todo file, notify the tracker
                if (fileName.EndsWith(".todo", StringComparison.OrdinalIgnoreCase))
                {
                    ToDoTracker.Instance.ScanTodoFiles(parentPath);
                }

                // Ensure the parent folder is expanded if it exists
                if (!string.IsNullOrEmpty(parentPath))
                {
                    expandedStates[parentPath] = true;
                }

                // Refresh with saved states
                await RefreshItems(true, expandedStates);

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
                    // Save current expanded states before deletion
                    var expandedStates = GetExpandedStates() ?? new Dictionary<string, bool>();

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
                        ToDoTracker.Instance.ScanTodoFiles(Path.GetDirectoryName(item.Path));
                    }

                    // Refresh with saved states
                    await RefreshItems(true, expandedStates);
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
            CollectExpandedStates(RootItems, states);
            return states;
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

                    // Store expanded states before refresh
                    var expandedStates = GetExpandedStates();

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

                    // Perform the rename operation
                    if (selectedItem.Type == Models.LibraryItemType.Directory)
                    {
                        Directory.Move(oldPath, newPath);
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
                        ToDoTracker.Instance.ScanTodoFiles(Path.GetDirectoryName(oldPath));
                    }

                    // Refresh with saved states
                    await RefreshItems(true, expandedStates);
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