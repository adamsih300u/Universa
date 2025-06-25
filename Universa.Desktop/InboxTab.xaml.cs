using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;
using System.Windows.Threading;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using System.Windows.Media;

namespace Universa.Desktop
{
    public partial class InboxTab : UserControl, INotifyPropertyChanged, IFileTab
    {
        public int LastKnownCursorPosition { get; private set; } = 0;
        
        private ObservableCollection<InboxItem> _items;
        private ICollectionView _itemsView;
        private Views.MainWindow _mainWindow;
        private readonly IConfigurationService _configService;
        private string _title = "Inbox";

        public event PropertyChangedEventHandler PropertyChanged;

        public ObservableCollection<InboxItem> Items
        {
            get => _items;
            set
            {
                _items = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
            }
        }

        public string FilePath 
        { 
            get => "Inbox";
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
            set { } // No-op as this tab is never directly modified
        }

        public InboxTab()
        {
            InitializeComponent();
            _items = new ObservableCollection<InboxItem>();
            _mainWindow = Application.Current.MainWindow as Views.MainWindow;
            _configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            
            if (_mainWindow == null)
            {
                MessageBox.Show("Error: Could not access main window.", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DataContext = this;
            InitializeCollectionView();

            // Delay loading items to ensure Configuration is initialized
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_configService?.Provider?.LibraryPath != null)
                {
                    LoadItems();
                }
            }), DispatcherPriority.Loaded);
        }

        private void InitializeCollectionView()
        {
            _itemsView = CollectionViewSource.GetDefaultView(_items);
            _itemsView.SortDescriptions.Add(new SortDescription("CreatedDate", ListSortDirection.Descending));
        }

        private void LoadItems()
        {
            try
            {
                _items.Clear();

                var libraryPath = _configService?.Provider?.LibraryPath;
                if (string.IsNullOrEmpty(libraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("Library path is not configured");
                    return;
                }

                var universaPath = Path.Combine(libraryPath, ".universa");
                if (!Directory.Exists(universaPath))
                {
                    Directory.CreateDirectory(universaPath);
                }

                var inboxPath = Path.Combine(universaPath, "inbox.json");
                if (File.Exists(inboxPath))
                {
                    var content = File.ReadAllText(inboxPath);
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    var items = JsonSerializer.Deserialize<List<InboxItem>>(content, options);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            _items.Add(item);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading inbox items: {ex.Message}");
                MessageBox.Show($"Error loading inbox items: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _itemsView?.Refresh();
        }

        private void UserControl_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                QuickAddTextBox.Focus();
                e.Handled = true;
            }
        }

        private void QuickAdd_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && sender is TextBox textBox)
            {
                var text = textBox.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    var item = new InboxItem
                    {
                        Title = text,
                        Type = "Note",
                        CreatedDate = DateTime.Now,
                        Content = text
                    };

                    _items.Add(item);
                    SaveItems(); // Save items after adding a new one
                    textBox.Clear();
                }
            }
        }

        private void SaveItems()
        {
            try
            {
                var libraryPath = _configService?.Provider?.LibraryPath;
                System.Diagnostics.Debug.WriteLine($"Saving inbox items. Library path: {libraryPath}");
                
                if (string.IsNullOrEmpty(libraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("Error: Library path is null or empty");
                    MessageBox.Show("Error: Library path is not configured.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var universaPath = Path.Combine(libraryPath, ".universa");
                System.Diagnostics.Debug.WriteLine($"Universa path: {universaPath}");
                
                if (!Directory.Exists(universaPath))
                {
                    System.Diagnostics.Debug.WriteLine($"Creating .universa directory at: {universaPath}");
                    Directory.CreateDirectory(universaPath);
                }

                var inboxPath = Path.Combine(universaPath, "inbox.json");
                System.Diagnostics.Debug.WriteLine($"Inbox path: {inboxPath}");
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                
                var itemsList = _items.ToList();
                System.Diagnostics.Debug.WriteLine($"Serializing {itemsList.Count} items");
                
                var json = JsonSerializer.Serialize(itemsList, options);
                System.Diagnostics.Debug.WriteLine($"Writing to file: {inboxPath}");
                
                File.WriteAllText(inboxPath, json);
                System.Diagnostics.Debug.WriteLine("Successfully saved inbox items");
            }
            catch (Exception ex)
            {
                var error = $"Error saving inbox items: {ex.Message}\nStack trace: {ex.StackTrace}";
                System.Diagnostics.Debug.WriteLine(error);
                MessageBox.Show($"Error saving inbox items: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessAsProject_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var listViewItem = contextMenu?.PlacementTarget as ListViewItem;
            var item = listViewItem?.DataContext as InboxItem;

            System.Diagnostics.Debug.WriteLine($"ProcessAsProject_Click - Item: {item?.Title ?? "null"}");

            if (item != null)
            {
                try
                {
                    var saveDialog = new Microsoft.Win32.SaveFileDialog
                    {
                        Title = "Save Project As",
                        Filter = "Project files (*.project)|*.project",
                        DefaultExt = ".project",
                        FileName = $"{item.Title.Replace(" ", "_")}.project",
                        InitialDirectory = _configService?.Provider?.LibraryPath
                    };

                    if (saveDialog.ShowDialog() == true)
                    {
                        var project = new Project
                        {
                            Title = item.Title,
                            Goal = item.Content,
                            CreatedDate = DateTime.Now,
                            Status = ProjectStatus.NotStarted,
                            FilePath = saveDialog.FileName
                        };

                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true
                        };
                        var json = JsonSerializer.Serialize(project, options);
                        File.WriteAllText(saveDialog.FileName, json);

                        // Remove from inbox and save
                        _items.Remove(item);
                        SaveItems();

                        // Refresh project tracker
                        ProjectTracker.Instance.ScanProjectFiles(_configService.Provider.LibraryPath);

                        // Open the new project
                        _mainWindow?.OpenFileInEditor(saveDialog.FileName);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error creating project: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ProcessAsTodo_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var listViewItem = contextMenu?.PlacementTarget as ListViewItem;
            var item = listViewItem?.DataContext as InboxItem;

            System.Diagnostics.Debug.WriteLine($"ProcessAsTodo_Click - Item: {item?.Title ?? "null"}");

            if (item != null)
            {
                try
                {
                    // First, let's find all existing .todo files
                    var libraryPath = _configService?.Provider?.LibraryPath;
                    if (string.IsNullOrEmpty(libraryPath))
                    {
                        MessageBox.Show("Library path is not configured.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var todoFiles = Directory.GetFiles(libraryPath, "*.todo", SearchOption.AllDirectories);
                    
                    if (todoFiles.Length > 0)
                    {
                        // Create dialog to choose between existing todo file or new one
                        var dialog = new TaskDialog
                        {
                            Title = "Add to ToDo List",
                            MainInstruction = "Where would you like to add this task?",
                            Content = "You can add this task to an existing ToDo list or create a new one.",
                            AllowCancel = true,
                            MainIcon = TaskDialogIcon.Information
                        };

                        var newFileButton = new TaskDialogCommandLinkButton("newFile", "Create New ToDo List", "Create a new .todo file for this task");
                        dialog.Buttons.Add(newFileButton);

                        foreach (var file in todoFiles)
                        {
                            var fileName = Path.GetFileName(file);
                            var button = new TaskDialogCommandLinkButton(file, $"Add to {fileName}", $"Add this task to {fileName}");
                            dialog.Buttons.Add(button);
                        }

                        var result = dialog.Show();
                        
                        if (result == "newFile")
                        {
                            CreateNewTodoFile(item);
                        }
                        else if (!string.IsNullOrEmpty(result))
                        {
                            AddToExistingTodoFile(item, result);
                        }
                    }
                    else
                    {
                        // No existing todo files, create new one
                        CreateNewTodoFile(item);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error processing todo: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CreateNewTodoFile(InboxItem item)
        {
            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save ToDo List As",
                Filter = "ToDo files (*.todo)|*.todo",
                DefaultExt = ".todo",
                FileName = $"{item.Title.Replace(" ", "_")}.todo",
                InitialDirectory = _configService?.Provider?.LibraryPath
            };

            if (saveDialog.ShowDialog() == true)
            {
                var todo = new ToDo
                {
                    Title = item.Title,
                    Description = item.Content,
                    StartDate = DateTime.Now,
                    IsCompleted = false,
                    FilePath = saveDialog.FileName
                };

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(new List<ToDo> { todo }, options);
                File.WriteAllText(saveDialog.FileName, json);

                // Remove from inbox and save
                _items.Remove(item);
                SaveItems();

                // Refresh todo tracker
                OnTodosChanged();

                // Open the new todo file
                _mainWindow?.OpenFileInEditor(saveDialog.FileName);
            }
        }

        private void AddToExistingTodoFile(InboxItem item, string filePath)
        {
            try
            {
                var content = File.ReadAllText(filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    WriteIndented = true
                };

                var todos = JsonSerializer.Deserialize<List<ToDo>>(content, options) ?? new List<ToDo>();

                var newTodo = new ToDo
                {
                    Title = item.Title,
                    Description = item.Content,
                    StartDate = DateTime.Now,
                    IsCompleted = false,
                    FilePath = filePath
                };

                todos.Add(newTodo);

                var json = JsonSerializer.Serialize(todos, options);
                File.WriteAllText(filePath, json);

                // Remove from inbox and save
                _items.Remove(item);
                SaveItems();

                // Refresh todo tracker
                OnTodosChanged();

                // Open the todo file
                _mainWindow?.OpenFileInEditor(filePath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error adding to todo file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem?.Parent as ContextMenu;
            var listViewItem = contextMenu?.PlacementTarget as ListViewItem;
            var item = listViewItem?.DataContext as InboxItem;

            System.Diagnostics.Debug.WriteLine($"Delete_Click - Item: {item?.Title ?? "null"}");

            if (item != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{item.Title}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _items.Remove(item);
                    SaveItems(); // Save items after removing one
                }
            }
        }

        public void Reload()
        {
            LoadItems();
        }

        public async Task<bool> Save()
        {
            return await Task.FromResult(true); // Virtual file, always returns true
        }

        public async Task<bool> SaveAs(string newPath = null)
        {
            return await Task.FromResult(true); // Virtual file, always returns true
        }

        /// <summary>
        /// Gets the content of the inbox tab
        /// </summary>
        /// <returns>The content as a string</returns>
        public string GetContent()
        {
            // For inbox tab, we'll return a simple representation of the inbox items
            // This is a placeholder implementation since inbox content isn't typically exported
            return "Inbox content is not available for export.";
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private async void OnTodosChanged()
        {
            await ToDoTracker.Instance.ScanTodoFilesAsync(_configService.Provider.LibraryPath);
        }

        public void OnTabSelected()
        {
            // Refresh the inbox items when tab is selected
            RefreshInboxItems();
        }

        public void OnTabDeselected()
        {
            // Save any pending changes when tab is deselected
            SaveInboxItems();
        }

        private void RefreshInboxItems()
        {
            LoadItems();
        }

        private void SaveInboxItems()
        {
            SaveItems();
        }
    }

    public class InboxItem : INotifyPropertyChanged
    {
        private string _title;
        private string _type;
        private string _content;
        private DateTime _createdDate;
        private string _filePath;
        private object _originalItem;

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title
        {
            get => _title;
            set
            {
                _title = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title)));
            }
        }

        public string Type
        {
            get => _type;
            set
            {
                _type = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Type)));
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                _content = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            }
        }

        public DateTime CreatedDate
        {
            get => _createdDate;
            set
            {
                _createdDate = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CreatedDate)));
            }
        }

        public string FilePath
        {
            get => _filePath;
            set
            {
                _filePath = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath)));
            }
        }

        public object OriginalItem
        {
            get => _originalItem;
            set
            {
                _originalItem = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OriginalItem)));
            }
        }
    }

    public class TaskDialogCommandLinkButton
    {
        public string Id { get; }
        public string Text { get; }
        public string Description { get; }

        public TaskDialogCommandLinkButton(string id, string text, string description)
        {
            Id = id;
            Text = text;
            Description = description;
        }
    }

    public class TaskDialog
    {
        public string Title { get; set; }
        public string MainInstruction { get; set; }
        public string Content { get; set; }
        public bool AllowCancel { get; set; }
        public TaskDialogIcon MainIcon { get; set; }
        public List<TaskDialogCommandLinkButton> Buttons { get; } = new List<TaskDialogCommandLinkButton>();

        public string Show()
        {
            var dialog = new Window
            {
                Title = Title,
                Width = 500,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var mainGrid = new Grid { Margin = new Thickness(10) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var instructionText = new TextBlock
            {
                Text = MainInstruction,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(instructionText, 0);

            var contentText = new TextBlock
            {
                Text = Content,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20)
            };
            Grid.SetRow(contentText, 1);

            var buttonsPanel = new StackPanel { Orientation = Orientation.Vertical };
            Grid.SetRow(buttonsPanel, 2);

            string result = null;

            foreach (var button in Buttons)
            {
                var buttonGrid = new Grid { Margin = new Thickness(0, 0, 0, 5) };
                var btn = new Button
                {
                    Height = 50,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(10)
                };

                var buttonContent = new StackPanel { Margin = new Thickness(5) };
                buttonContent.Children.Add(new TextBlock
                {
                    Text = button.Text,
                    FontWeight = FontWeights.SemiBold
                });
                buttonContent.Children.Add(new TextBlock
                {
                    Text = button.Description,
                    Foreground = Application.Current.Resources["SubtleBrush"] as Brush
                });

                btn.Content = buttonContent;
                btn.Click += (s, e) =>
                {
                    result = button.Id;
                    dialog.DialogResult = true;
                };

                buttonGrid.Children.Add(btn);
                buttonsPanel.Children.Add(buttonGrid);
            }

            if (AllowCancel)
            {
                var cancelButton = new Button
                {
                    Content = "Cancel",
                    Width = 75,
                    Height = 23,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                    IsCancel = true
                };
                Grid.SetRow(cancelButton, 3);
                mainGrid.Children.Add(cancelButton);
            }

            mainGrid.Children.Add(instructionText);
            mainGrid.Children.Add(contentText);
            mainGrid.Children.Add(buttonsPanel);

            dialog.Content = mainGrid;

            return dialog.ShowDialog() == true ? result : null;
        }
    }

    public enum TaskDialogIcon
    {
        None,
        Information,
        Warning,
        Error,
        Shield
    }
} 