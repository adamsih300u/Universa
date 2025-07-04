using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Core;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Core.Logging;
using Universa.Desktop.Library;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Tabs;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Universa.Desktop.Controls;
using Universa.Desktop.Services;
using Universa.Desktop.Managers;
using Universa.Desktop.Core.TTS;
using ServiceLocator = Universa.Desktop.Services.ServiceLocator;
using Universa.Desktop.ViewModels;
using System.Windows.Input;
using System.Runtime.InteropServices;
using Universa.Desktop.Windows;
using Universa.Desktop.Core.Theme;

namespace Universa.Desktop.Views
{
    public class TextEditorControl : UserControl
    {
        private readonly TextBox _textBox;

        public TextEditorControl(string text)
        {
            _textBox = new TextBox
            {
                Text = text,
                AcceptsReturn = true,
                AcceptsTab = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            Content = _textBox;
        }

        public string Text
        {
            get => _textBox.Text;
            set => _textBox.Text = value;
        }
    }

    public partial class MainWindow : BaseMainWindow, IMediaWindow
    {
        public static MainWindow Instance { get; private set; }
        public MediaControlBar MediaControlBar => mediaControlBar;
        public new MediaPlayerManager MediaPlayerManager => base._mediaPlayerManager;
        public new TabControl MainTabControl => TabControl;
        public new TTSClient TTSClient => base.TTSClient;
        public new LibraryNavigator LibraryNavigator => base.LibraryNavigator;

        private readonly IConfigurationService _configService;
        private readonly MediaElement _mediaElement;
        private readonly TTSManager _ttsManager;
        private readonly WeatherManager _weatherManager;
        private readonly MediaControlsManager _mediaControlsManager;
        private bool _isLibraryCollapsed = false;
        private bool _isChatCollapsed = false;
        private double _lastLibraryWidth = 250;
        private double _lastChatWidth = 300;

        // IMediaWindow interface implementation
        MediaElement IMediaWindow.MediaPlayer => _mediaElement;
        TextBlock IMediaWindow.NowPlayingText => mediaControlBar?.FindName("NowPlayingText") as TextBlock;
        Slider IMediaWindow.TimelineSlider => mediaControlBar?.FindName("TimelineSlider") as Slider;
        TextBlock IMediaWindow.TimeInfo => mediaControlBar?.FindName("TimeInfo") as TextBlock;
        Button IMediaWindow.VolumeButton => mediaControlBar?.FindName("VolumeButton") as Button;
        Slider IMediaWindow.VolumeSlider => mediaControlBar?.FindName("VolumeSlider") as Slider;
        Grid IMediaWindow.MediaControlsGrid => mediaControlBar?.FindName("MediaControlsGrid") as Grid;
        Button IMediaWindow.PlayPauseButton => mediaControlBar?.FindName("PlayPauseButton") as Button;
        Button IMediaWindow.PreviousButton => mediaControlBar?.FindName("PreviousButton") as Button;
        Button IMediaWindow.NextButton => mediaControlBar?.FindName("NextButton") as Button;
        Button IMediaWindow.ShuffleButton => mediaControlBar?.FindName("ShuffleButton") as Button;
        Button IMediaWindow.MuteButton => mediaControlBar?.FindName("MuteButton") as Button;
        MediaControlBar IMediaWindow.MediaControlBar => MediaControlBar;
        MediaPlayerManager IMediaWindow.MediaPlayerManager => MediaPlayerManager;

        public ICommand CloseTabCommand { get; private set; }
        public ICommand ToggleTTSCommand { get; private set; }

        public MainWindow(IConfigurationService configService)
            : base(configService)
        {
            InitializeComponent();
            Instance = this;
            
            // Add window activation handling
            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;
            
            // Add keyboard shortcut handling
            this.KeyDown += MainWindow_KeyDown;

            // Add closing event to save chat history
            this.Closing += MainWindow_Closing;

            // Ensure window style is set correctly immediately
            this.WindowStyle = WindowStyle.SingleBorderWindow;

            // Add Loaded event handler to ensure theme is applied after window is fully loaded
            this.Loaded += async (s, e) => {
                // Apply theme after window is fully loaded
                var theme = _configService.Provider.CurrentTheme ?? "Light";
                ApplyTheme(theme);
                
                // Force a window state change to refresh the title bar
                var currentState = this.WindowState;
                if (currentState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                    this.Dispatcher.BeginInvoke(new Action(() => {
                        this.WindowState = WindowState.Maximized;
                    }), DispatcherPriority.Render);
                }
                else
                {
                    this.WindowState = WindowState.Maximized;
                    this.Dispatcher.BeginInvoke(new Action(() => {
                        this.WindowState = currentState;
                    }), DispatcherPriority.Render);
                }

                // Initialize ToDoTracker asynchronously
                await ToDoTracker.Instance.InitializeAsync(_configService.Provider.LibraryPath);
            };

            _configService = configService;
            _mediaElement = MediaPlayer;

            // Restore window position and size
            RestoreWindowState();

            // Initialize width values from configuration
            _lastLibraryWidth = _configService.Provider.GetValue<double>(ConfigurationKeys.Library.LastWidth);
            if (_lastLibraryWidth <= 0) _lastLibraryWidth = 250;
            _lastChatWidth = _configService.Provider.GetValue<double>(ConfigurationKeys.Chat.LastWidth);
            if (_lastChatWidth <= 0) _lastChatWidth = 300;
            _isLibraryCollapsed = !_configService.Provider.GetValue<bool>(ConfigurationKeys.Library.IsExpanded);
            _isChatCollapsed = !_configService.Provider.GetValue<bool>(ConfigurationKeys.Chat.IsExpanded);

            // Only initialize MediaPlayerManager if we have a valid MediaElement
            if (_mediaElement != null)
            {
                base._mediaPlayerManager = new MediaPlayerManager(this);
                
                // Initialize MediaControlBar
                if (mediaControlBar != null)
                {
                    // Ensure the control bar is hidden by default
                    mediaControlBar.Visibility = Visibility.Collapsed;
                    
                    mediaControlBar.Initialize(base._mediaPlayerManager);
                    base._mediaPlayerManager.PlaybackStarted += OnPlaybackStarted;
                    base._mediaPlayerManager.PlaybackStopped += OnPlaybackStopped;

                    // Create MediaControlsManager with controls from MediaControlBar
                    _mediaControlsManager = new MediaControlsManager(
                        this,
                        base._mediaPlayerManager,
                        mediaControlBar,  // UIElement
                        mediaControlBar.FindName("PlayPauseButton") as Button,
                        mediaControlBar.FindName("PreviousButton") as Button,
                        mediaControlBar.FindName("NextButton") as Button,
                        mediaControlBar.FindName("ShuffleButton") as Button,
                        mediaControlBar.FindName("MuteButton") as ButtonBase,
                        mediaControlBar.FindName("VolumeSlider") as Slider,
                        mediaControlBar.FindName("TimeInfo") as TextBlock,
                        mediaControlBar.FindName("TimelineSlider") as Slider,
                        mediaControlBar.FindName("NowPlayingText") as TextBlock,
                        mediaControlBar.FindName("MediaControlsGrid") as Grid
                    );
                    base._mediaPlayerManager.SetControlsManager(_mediaControlsManager);
                }
            }
            else
            {
                // Hide media control bar if no media element is available
                if (mediaControlBar != null)
                {
                    mediaControlBar.Visibility = Visibility.Collapsed;
                }
            }
            
            DataContext = this;
            
            // Initialize time display
            var timeDisplayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            timeDisplayTimer.Tick += (s, e) =>
            {
                if (TimeDisplay != null)
                {
                    var now = DateTime.Now;
                    TimeDisplay.Text = now.ToString("dddd, MMMM d'th', yyyy - h:mm tt");
                }
            };
            timeDisplayTimer.Start();

            // Initialize weather manager
            _weatherManager = new WeatherManager(WeatherDisplay, MoonPhaseDisplay, MoonPhaseDescription, _configService);
            
            // Initialize commands
            CloseTabCommand = new RelayCommand(tabItem =>
            {
                if (tabItem != null)
                {
                    CloseTab((TabItem)tabItem);
                }
            });

            ToggleTTSCommand = new RelayCommand(tabItem =>
            {
                if (tabItem != null && tabItem is TabItem currentTab && currentTab.Content is ITTSSupport ttsSupport)
                {
                    if (ttsSupport.IsPlaying)
                    {
                        ttsSupport.StopTTS();
                    }
                    else
                    {
                        var text = ttsSupport.GetTextToSpeak();
                        if (!string.IsNullOrEmpty(text))
                        {
                            _ttsManager.StartTTS(text, currentTab);
                        }
                    }
                }
            });

            // Subscribe to MediaPlayerManager events if it exists
            if (MediaPlayerManager != null)
            {
                MediaPlayerManager.PlaybackStarted += OnPlaybackStarted;
                MediaPlayerManager.PlaybackStopped += OnPlaybackStopped;
            }

            // Initialize TTSManager
            _ttsManager = new TTSManager(this);

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;

            // Add Loaded event handler for theme initialization
            this.Loaded += (s, e) =>
            {
                var theme = _configService.Provider.CurrentTheme;
                if (!string.IsNullOrEmpty(theme))
                {
                    ApplyTheme(theme);
                }

                // Refresh library navigator
                if (libraryNavigator != null)
                {
                    libraryNavigator.RefreshItems(false);
                }
            };

            // Restore sidebar states from configuration
            RestoreSidebarStates();
            
            // Restore previously open tabs
            RestoreOpenTabs();

            // Set up GridSplitter drag completed event for Library Navigator
            var splitter = this.FindName("NavigationSplitter") as GridSplitter;
            if (splitter != null)
            {
                splitter.DragCompleted += (s, e) =>
                {
                    _configService.Provider.SetValue(ConfigurationKeys.Library.LastWidth, NavigationColumn.ActualWidth);
                    _configService.Provider.Save();
                };
            }

            // Set up GridSplitter drag completed event for Chat Sidebar
            var chatSplitter = this.FindName("ChatSplitter") as GridSplitter;
            if (chatSplitter != null)
            {
                chatSplitter.DragCompleted += (s, e) =>
                {
                    _configService.Provider.SetValue(ConfigurationKeys.Chat.LastWidth, ChatColumn.ActualWidth);
                    _configService.Provider.Save();
                };
            }

            // Handle window closing
            this.Closing += async (s, e) =>
            {
                // Check all tabs for unsaved changes
                var unsavedTabs = MainTabControl.Items.OfType<TabItem>()
                    .Where(tab => tab.Content is IFileTab fileTab && fileTab.IsModified)
                    .ToList();

                if (unsavedTabs.Any())
                {
                    var result = MessageBox.Show(
                        $"You have {unsavedTabs.Count} tab(s) with unsaved changes. Would you like to save them before closing?",
                        "Unsaved Changes",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Cancel)
                    {
                        e.Cancel = true;
                        return;
                    }
                    if (result == MessageBoxResult.Yes)
                    {
                        foreach (var tab in unsavedTabs)
                        {
                            if (tab.Content is IFileTab fileTab)
                            {
                                if (!await fileTab.Save())
                                {
                                    // If any save fails, cancel closing
                                    e.Cancel = true;
                                    return;
                                }
                            }
                        }
                    }
                }

                SaveWindowState();
                SaveSidebarStates();
                SaveOpenTabs();
            };
        }

        private void MainWindow_Activated(object sender, EventArgs e)
        {
            // Force proper window activation
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                // Temporarily set window to topmost to ensure proper activation
                var wasTopmost = this.Topmost;
                this.Topmost = true;
                this.Topmost = wasTopmost;
            }

            // Ensure proper focus
            this.Focus();
            if (this.IsVisible)
            {
                // Force layout update
                this.InvalidateVisual();
                this.UpdateLayout();
            }
        }

        private void MainWindow_Deactivated(object sender, EventArgs e)
        {
            // Clear any invalid focus states when window loses focus
            if (Keyboard.FocusedElement is FrameworkElement focused)
            {
                Keyboard.ClearFocus();
            }
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (this.WindowState == WindowState.Normal)
            {
                // Ensure proper focus when window is restored
                this.Dispatcher.BeginInvoke(new Action(() =>
                {
                    this.Focus();
                    this.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
                }), DispatcherPriority.Input);
            }
        }

        private void OnPlaybackStarted(object sender, EventArgs e)
        {
            if (mediaControlBar != null)
            {
                mediaControlBar.Visibility = Visibility.Visible;
            }
        }

        private void OnPlaybackStopped(object sender, EventArgs e)
        {
            if (mediaControlBar != null && !MediaPlayerManager.HasPlaylist)
            {
                mediaControlBar.Visibility = Visibility.Collapsed;
            }
        }

        private async void CloseTab(TabItem tabItem)
        {
            // Cleanup for tab content
            if (tabItem.Content is IDisposable disposable)
            {
                disposable.Dispose();
            }
            
            // Notify ChatSidebarViewModel about tab closing
            var chatSidebar = FindName("chatSidebar") as Views.ChatSidebar;
            if (chatSidebar?.DataContext is ViewModels.ChatSidebarViewModel chatVM)
            {
                chatVM.HandleEditorTabClosed(tabItem.Content);
            }
            
            // If it's a file tab, check if it needs saving
            if (tabItem.Content is IFileTab fileTab && fileTab.IsModified)
            {
                var result = MessageBox.Show(
                    "You have unsaved changes. Would you like to save them before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Cancel)
                {
                    return;
                }
                if (result == MessageBoxResult.Yes)
                {
                    if (!await fileTab.Save())
                    {
                        // If save failed, don't close the tab
                        return;
                    }
                }
            }

            MainTabControl.Items.Remove(tabItem);
            SaveOpenTabs(); // Save tabs state when a tab is closed
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            if (e.Key == ConfigurationKeys.Theme.Current)
            {
                ApplyTheme(_configService.Provider.CurrentTheme);
            }
        }

        private async Task<bool> ConvertLegacyFileToOrgMode(string legacyPath, string orgPath)
        {
            try
            {
                var extension = Path.GetExtension(legacyPath).ToLower();
                var orgContent = new StringBuilder();

                // Add org-mode header
                orgContent.AppendLine($"#+TITLE: {Path.GetFileNameWithoutExtension(orgPath)}");
                orgContent.AppendLine($"#+CREATED: {DateTime.Now:yyyy-MM-dd}");
                orgContent.AppendLine($"#+CONVERTED_FROM: {extension} format");
                orgContent.AppendLine();

                if (extension == ".todo")
                {
                    // Convert TODO file
                    var content = await File.ReadAllTextAsync(legacyPath);
                    var todos = System.Text.Json.JsonSerializer.Deserialize<List<Models.ToDo>>(content);
                    
                    orgContent.AppendLine("* Tasks");
                    orgContent.AppendLine();
                    
                    foreach (var todo in todos ?? new List<Models.ToDo>())
                    {
                        var state = todo.IsCompleted ? "DONE" : "TODO";
                        var priority = !string.IsNullOrEmpty(todo.Priority) ? $" [#{todo.Priority}]" : "";
                        var tags = todo.Tags?.Any() == true ? $" :{string.Join(":", todo.Tags)}:" : "";
                        
                        orgContent.AppendLine($"** {state}{priority} {todo.Title}{tags}");
                        
                        if (todo.StartDate.HasValue)
                            orgContent.AppendLine($"   SCHEDULED: <{todo.StartDate.Value:yyyy-MM-dd ddd}>");
                        if (todo.DueDate.HasValue)
                            orgContent.AppendLine($"   DEADLINE: <{todo.DueDate.Value:yyyy-MM-dd ddd}>");
                        if (todo.IsCompleted && todo.CompletedDate.HasValue)
                            orgContent.AppendLine($"   CLOSED: [{todo.CompletedDate.Value:yyyy-MM-dd ddd HH:mm}]");
                        
                        if (!string.IsNullOrEmpty(todo.Description))
                        {
                            orgContent.AppendLine();
                            orgContent.AppendLine($"   {todo.Description}");
                        }
                        
                        orgContent.AppendLine();
                    }
                }
                else if (extension == ".project")
                {
                    // Convert Project file
                    var content = await File.ReadAllTextAsync(legacyPath);
                    var project = System.Text.Json.JsonSerializer.Deserialize<Models.Project>(content);
                    
                    if (project != null)
                    {
                        var state = project.Status == Models.ProjectStatus.Completed ? "DONE" : "TODO";
                        orgContent.AppendLine($"* {state} {project.Title}");
                        
                        if (project.DueDate.HasValue)
                            orgContent.AppendLine($"  DEADLINE: <{project.DueDate.Value:yyyy-MM-dd ddd}>");
                        if (project.StartDate.HasValue)
                            orgContent.AppendLine($"  SCHEDULED: <{project.StartDate.Value:yyyy-MM-dd ddd}>");
                        
                        orgContent.AppendLine("  :PROPERTIES:");
                        orgContent.AppendLine($"  :CATEGORY: {project.Category ?? "Project"}");
                        orgContent.AppendLine($"  :STATUS: {project.Status}");
                        orgContent.AppendLine($"  :CREATED: {project.CreatedDate:yyyy-MM-dd}");
                        orgContent.AppendLine("  :END:");
                        orgContent.AppendLine();
                        
                        if (!string.IsNullOrEmpty(project.Goal))
                        {
                            orgContent.AppendLine($"  {project.Goal}");
                            orgContent.AppendLine();
                        }
                        
                        if (project.Tasks?.Any() == true)
                        {
                            orgContent.AppendLine("** Tasks");
                            foreach (var task in project.Tasks)
                            {
                                var taskState = task.IsCompleted ? "DONE" : "TODO";
                                orgContent.AppendLine($"*** {taskState} {task.Title}");
                                if (!string.IsNullOrEmpty(task.Description))
                                {
                                    orgContent.AppendLine($"    {task.Description}");
                                }
                                orgContent.AppendLine();
                            }
                        }
                    }
                }

                await File.WriteAllTextAsync(orgPath, orgContent.ToString());
                
                // Optionally move legacy file to .bak
                var backupPath = legacyPath + ".bak";
                if (File.Exists(backupPath))
                    File.Delete(backupPath);
                File.Move(legacyPath, backupPath);
                
                MessageBox.Show($"Successfully converted to {Path.GetFileName(orgPath)}\nOriginal file backed up as {Path.GetFileName(backupPath)}", 
                    "Conversion Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error converting file: {ex.Message}", "Conversion Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        #region Menu Item Click Handlers
        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                OpenFolderInTab(dialog.SelectedPath);
            }
        }

        private void OpenFolderInTab(string folderPath)
        {
            // TODO: Implement folder opening logic
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog();
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // TODO: Implement file opening logic
            }
        }

        private async void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl.SelectedItem is TabItem selectedTab && 
                selectedTab.Content is IFileTab fileTab)
            {
                await fileTab.Save();
            }
        }

        private async void SaveFileAs_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl.SelectedItem is TabItem selectedTab && 
                selectedTab.Content is IFileTab fileTab)
            {
                await fileTab.SaveAs();
            }
        }

        private async void CloseFile_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl.SelectedItem is TabItem selectedTab)
            {
                await Application.Current.Dispatcher.InvokeAsync(() => CloseTab(selectedTab));
            }
        }

        private void ExportFile_Click(object sender, RoutedEventArgs e)
        {
            // Check if there's a tab selected and it's a MarkdownTabAvalon
            if (MainTabControl.SelectedItem is TabItem tabItem && 
                tabItem.Content is Views.MarkdownTabAvalon markdownTab)
            {
                // Create and show the export window
                var exportWindow = new ExportWindow(markdownTab);
                exportWindow.Owner = this;
                exportWindow.ShowDialog();
            }
            else if (MainTabControl.SelectedItem is TabItem selectedTab && 
                     selectedTab.Content is IFileTab fileTab)
            {
                // A tab that implements IFileTab but is not a MarkdownTab
                MessageBox.Show("Only markdown documents can be exported. Please select a markdown document tab.", 
                    "Export Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // No tab selected or tab doesn't implement IFileTab
                MessageBox.Show("Please select a document tab to export.", 
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async void BackupLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_configService.Provider.LibraryPath))
            {
                MessageBox.Show("Library path is not configured. Please set it in Settings first.", 
                    "Backup Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var backupManager = new LibraryBackupManager(this, _configService.Provider.LibraryPath);
            await backupManager.CreateBackupAsync();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSettings();
        }

        private void ToggleChat_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement chat toggle
        }

        private void OpenMatrixChat_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Implement Matrix chat opening
        }

        private void OpenMusicTab(object sender, RoutedEventArgs e)
        {
            OpenMusicTab();
        }

        private void OpenRssTab(object sender, RoutedEventArgs e)
        {
            // TODO: Implement RSS tab opening
        }

        private void OpenGameTab_Click(object sender, RoutedEventArgs e)
        {
            OpenGameTab();
        }

        private void OpenOverviewTab_Click(object sender, RoutedEventArgs e)
        {
            OpenOverviewTab();
        }

        private void OpenGlobalAgendaTab_Click(object sender, RoutedEventArgs e)
        {
            OpenGlobalAgendaTab();
        }

        public void OpenOverviewTab()
        {
            // Check if overview tab is already open
            foreach (TabItem existingTab in MainTabControl.Items)
            {
                if (existingTab.Content is OverviewTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            // Create new overview tab
            var overviewTab = new OverviewTab();
            var newTab = new TabItem
            {
                Header = "Overview",
                Content = overviewTab,
                Tag = "overview"
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        public void OpenGlobalAgendaTab()
        {
            // Check if global agenda tab is already open
            foreach (TabItem existingTab in MainTabControl.Items)
            {
                if (existingTab.Content is Tabs.GlobalAgendaTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            // Create new global agenda tab
            var globalAgendaTab = new Tabs.GlobalAgendaTab(_configService);
            var newTab = new TabItem
            {
                Header = "🗓️ Global Agenda",
                Content = globalAgendaTab,
                Tag = "globalagenda"
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems.Count > 0 && e.RemovedItems[0] is TabItem removedTab)
            {
                // Handle cleanup for the tab being removed from selection
                if (removedTab.Content is IFileTab fileTab)
                {
                    fileTab.OnTabDeselected();
                }
            }

            if (e.AddedItems.Count > 0 && e.AddedItems[0] is TabItem selectedTab)
            {
                // Update window title
                Title = $"Universa - {selectedTab.Header}";

                // Handle specific tab types
                if (selectedTab.Content is IFileTab fileTab)
                {
                    fileTab.OnTabSelected();
                }
            }
        }
        #endregion

        public override async void OpenFileInEditor(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            if (!System.IO.File.Exists(filePath))
            {
                MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Check if the file is already open
            var existingTab = FindTab(filePath);
            if (existingTab != null)
            {
                MainTabControl.SelectedItem = existingTab;
                return;
            }

            var extension = System.IO.Path.GetExtension(filePath).ToLower();
            var title = System.IO.Path.GetFileName(filePath);

            // Create appropriate editor based on file type
            UserControl editor;
            switch (extension)
            {
                            case ".md":
                var frontmatterProcessor = ServiceLocator.Instance.GetService<IFrontmatterProcessor>();
                var searchService = ServiceLocator.Instance.GetService<IMarkdownSearchService>();
                var chapterNavigationService = ServiceLocator.Instance.GetService<IChapterNavigationService>();
                var fontService = ServiceLocator.Instance.GetService<IMarkdownFontService>();
                var fileService = ServiceLocator.Instance.GetService<IMarkdownFileService>();
                var uiEventHandler = ServiceLocator.Instance.GetService<IMarkdownUIEventHandler>();
                var statusManager = ServiceLocator.Instance.GetService<IMarkdownStatusManager>();
                var editorSetupService = ServiceLocator.Instance.GetService<IMarkdownEditorSetupService>();
                
                // Use new AvalonEdit-based MarkdownTab (TTS removed for simplicity)
                editor = new Views.MarkdownTabAvalon(filePath, frontmatterProcessor, searchService, 
                    chapterNavigationService, fontService, fileService, 
                    uiEventHandler, statusManager, editorSetupService);
                break;
                case ".org":
                    editor = new Tabs.OrgModeTab(filePath);
                    break;
                case ".todo":
                case ".project":
                    // Legacy file types - suggest migration to org-mode
                    var result = MessageBox.Show(
                        $"This is a legacy {extension.Substring(1).ToUpper()} file format. Would you like to convert it to the new Org-Mode format (.org)?",
                        "Legacy File Format",
                        MessageBoxButton.YesNoCancel,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Convert to org-mode
                        var orgPath = Path.ChangeExtension(filePath, ".org");
                        if (await ConvertLegacyFileToOrgMode(filePath, orgPath))
                        {
                            editor = new Tabs.OrgModeTab(orgPath);
                            title = Path.GetFileName(orgPath);
                        }
                        else
                        {
                            editor = new EditorTab(filePath);
                        }
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        // Open as plain text
                        editor = new EditorTab(filePath);
                    }
                    else
                    {
                        // Cancel - don't open file
                        return;
                    }
                    break;
                case ".overview":
                    editor = new OverviewTab();
                    break;
                default:
                    editor = new EditorTab(filePath);
                    break;
            }

            // Add the new tab
            var newTab = new TabItem
            {
                Header = title,
                Content = editor,
                Tag = filePath
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        public void OpenMusicTab(string name = null)
        {
            // Check if music tab is already open
            foreach (TabItem existingTab in MainTabControl.Items)
            {
                if (existingTab.Content is MusicTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            // Create new music tab
            var musicTab = new MusicTab(this);
            var newTab = new TabItem
            {
                Header = name ?? "Music",
                Content = musicTab,
                Tag = "music"
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        public void OpenGameTab()
        {
            // Check if game tab is already open
            foreach (TabItem existingTab in MainTabControl.Items)
            {
                if (existingTab.Content is GameTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            // Create new game tab
            var gameTab = new GameTab();
            var newTab = new TabItem
            {
                Header = "Stock Trader",
                Content = gameTab,
                Tag = "game"
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        private void ShowSettings()
        {
            var settingsWindow = new SettingsWindow(_configService);
            settingsWindow.Owner = this;
            
            if (settingsWindow.ShowDialog() == true)
            {
                // Refresh services that depend on settings
                RefreshServices();
            }
        }

        private void RefreshServices()
        {
            // Refresh AI services
            var aiService = ServiceLocator.Instance.GetService<IAIService>();
            if (aiService != null)
            {
                aiService.RefreshConfiguration();
            }

            // Refresh weather service
            var weatherService = ServiceLocator.Instance.GetService<IWeatherService>();
            if (weatherService != null)
            {
                weatherService.RefreshConfiguration();
            }

            // Refresh sync service
            var syncService = ServiceLocator.Instance.GetService<ISyncService>();
            if (syncService != null)
            {
                syncService.RefreshConfiguration();
            }

            // Refresh Matrix service
            var matrixService = ServiceLocator.Instance.GetService<IMatrixService>();
            if (matrixService != null)
            {
                matrixService.RefreshConfiguration();
            }

            // Refresh Subsonic service
            var subsonicService = ServiceLocator.Instance.GetService<ISubsonicService>();
            if (subsonicService != null)
            {
                subsonicService.RefreshConfiguration();
            }
        }

        // Update application-wide resources
        public void ApplyTheme(string themeName)
        {
            if (string.IsNullOrEmpty(themeName))
                return;

            // Get theme colors from configuration
            var theme = _configService.Provider.GetTheme(themeName);
            if (theme == null)
                return;

            try
            {
                // Apply theme colors to application resources
                var resources = Application.Current.Resources;

                // Apply window colors
                resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
                resources["MenuBackgroundBrush"] = new SolidColorBrush(theme.MenuBackground);
                resources["MenuForeground"] = new SolidColorBrush(theme.MenuForeground);
                resources["TextBrush"] = new SolidColorBrush(theme.ContentForeground);
                resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);

                // Apply title bar colors
                if (Application.Current.MainWindow != null)
                {
                    var isDarkTheme = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase);
                    var titleBarBackground = isDarkTheme ? Color.FromRgb(32, 32, 32) : Color.FromRgb(240, 240, 240);
                    var titleBarForeground = isDarkTheme ? Colors.White : Colors.Black;
                    var buttonHoverColor = isDarkTheme ? Color.FromRgb(48, 48, 48) : Color.FromRgb(229, 229, 229);

                    try
                    {
                        // Ensure window style is set correctly
                        Application.Current.MainWindow.WindowStyle = WindowStyle.SingleBorderWindow;
                        
                        // Update system chrome colors
                        var dwmApi = new DwmApi();
                        var hwnd = new WindowInteropHelper(Application.Current.MainWindow).Handle;
                        if (hwnd != IntPtr.Zero)
                        {
                            dwmApi.SetTitleBarColor(hwnd, titleBarBackground, titleBarForeground, buttonHoverColor);
                            
                            // Force window to refresh its non-client area
                            NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                                SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE |
                                SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_FRAMECHANGED);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error setting title bar colors: {ex.Message}");
                    }
                }

                // Apply tab colors
                resources["TabBackground"] = new SolidColorBrush(theme.TabBackground);
                resources["TabForeground"] = new SolidColorBrush(theme.TabForeground);
                resources["ActiveTabBackground"] = new SolidColorBrush(theme.ActiveTabBackground);
                resources["ActiveTabForeground"] = new SolidColorBrush(theme.ActiveTabForeground);

                // Apply content colors
                resources["ContentBackground"] = new SolidColorBrush(theme.ContentBackground);
                resources["ContentForeground"] = new SolidColorBrush(theme.ContentForeground);

                // Apply accent color
                resources["AccentColor"] = new SolidColorBrush(theme.AccentColor);

                // Apply to window
                Background = new SolidColorBrush(theme.WindowBackground);
                Foreground = new SolidColorBrush(theme.ContentForeground);

                // Update system theme
                if (themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.SetDarkMode();
                }
                else if (themeName.Equals("Light", StringComparison.OrdinalIgnoreCase))
                {
                    ThemeManager.SetLightMode();
                }

                // Update all theme-aware tabs
                if (TabControl != null)
                {
                    foreach (TabItem tab in TabControl.Items)
                    {
                        if (tab?.Content is IThemeAware themeAwareTab)
                        {
                            themeAwareTab.ApplyTheme(themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying theme: {ex.Message}", "Theme Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Add DwmApi helper class
        private class DwmApi
        {
            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

            // DWM attribute constants
            private const int DWMWA_CAPTION_COLOR = 35;
            private const int DWMWA_TEXT_COLOR = 36;
            private const int DWMWA_BORDER_COLOR = 34;
            private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

            public void SetTitleBarColor(IntPtr hwnd, Color background, Color foreground, Color buttonHover)
            {
                if (hwnd == IntPtr.Zero) return;

                try
                {
                    // Set dark mode first if needed
                    bool isDarkMode = background.R < 128 && background.G < 128 && background.B < 128;
                    int darkModeValue = isDarkMode ? 1 : 0;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkModeValue, sizeof(int));

                    // Set caption color
                    int bgColor = (background.R) | (background.G << 8) | (background.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref bgColor, sizeof(int));

                    // Set text color
                    int fgColor = (foreground.R) | (foreground.G << 8) | (foreground.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref fgColor, sizeof(int));

                    // Set border color
                    int borderColor = (buttonHover.R) | (buttonHover.G << 8) | (buttonHover.B << 16);
                    DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting title bar color: {ex.Message}");
                }
            }
        }

        // Add native methods
        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, SetWindowPosFlags uFlags);
        }

        [Flags]
        private enum SetWindowPosFlags : uint
        {
            SWP_NOMOVE = 0x0002,
            SWP_NOSIZE = 0x0001,
            SWP_NOZORDER = 0x0004,
            SWP_FRAMECHANGED = 0x0020
        }

        public void OpenMediaTab(string serviceName)
        {
            // First check if a tab for this service already exists
            foreach (TabItem existingTab in MainTabControl.Items)
            {
                if (serviceName.ToLower() == "jellyfin" && existingTab.Content is MediaTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
                else if ((serviceName.ToLower() == "music" || 
                         serviceName.ToLower() == "subsonic" || 
                         serviceName.ToLower() == "navidrome") && 
                        existingTab.Content is MusicTab)
                {
                    MainTabControl.SelectedItem = existingTab;
                    return;
                }
            }

            // If no existing tab was found, create a new one
            switch (serviceName.ToLower())
            {
                case "music":
                case "subsonic":
                case "navidrome":
                    var config = _configService.Provider;
                    var displayName = !string.IsNullOrEmpty(config.SubsonicName) ? config.SubsonicName : "Music";
                    OpenMusicTab(displayName);
                    break;
                case "jellyfin":
                    var jellyfinService = ServiceLocator.Instance.GetRequiredService<JellyfinService>();
                    var mediaTab = new MediaTab(jellyfinService);
                    var tabItem = new TabItem
                    {
                        Header = "Jellyfin",
                        Content = mediaTab
                    };
                    MainTabControl.Items.Add(tabItem);
                    MainTabControl.SelectedItem = tabItem;
                    break;
                case "audiobookshelf":
                    MessageBox.Show("Audiobookshelf support coming soon!", "Not Implemented");
                    break;
                default:
                    MessageBox.Show($"Unknown media service: {serviceName}", "Error");
                    break;
            }
        }

        public void HandleServiceNavigation(LibraryTreeItem item)
        {
            if (item == null) return;

            if (item.Type == LibraryItemType.Service || item.Type == LibraryItemType.Category)
            {
                var serviceName = item.Name;
                OpenMediaTab(serviceName);
            }
            else
            {
                // Handle other item types
            }
        }

        public void OpenMatrixChatFromNavigator()
        {
            try
            {
                if (string.IsNullOrEmpty(_configService.Provider.MatrixServerUrl) || 
                    string.IsNullOrEmpty(_configService.Provider.MatrixUsername) || 
                    string.IsNullOrEmpty(_configService.Provider.MatrixPassword))
                {
                    ShowSettings();
                    return;
                }

                // TODO: Implement Matrix chat tab
                MessageBox.Show("Matrix chat support coming soon!", "Not Implemented");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening Matrix chat: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public TabItem FindTab(string identifier)
        {
            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Tag?.ToString() == identifier || 
                    (tab.Content is IFileTab fileTab && fileTab.FilePath == identifier) ||
                    (tab.Header?.ToString() == identifier))
                {
                    return tab;
                }
            }
            return null;
        }

        private void RestoreSidebarStates()
        {
            // Restore Library Navigator state
            _lastLibraryWidth = _configService.Provider.GetValue<double>(ConfigurationKeys.Library.LastWidth);
            if (_lastLibraryWidth <= 0) _lastLibraryWidth = 250; // Default width

            _lastChatWidth = _configService.Provider.GetValue<double>(ConfigurationKeys.Chat.LastWidth);
            if (_lastChatWidth <= 0) _lastChatWidth = 300; // Default width

            _isLibraryCollapsed = !_configService.Provider.GetValue<bool>(ConfigurationKeys.Library.IsExpanded);
            _isChatCollapsed = !_configService.Provider.GetValue<bool>(ConfigurationKeys.Chat.IsExpanded);

            // Apply the restored states
            if (_isLibraryCollapsed)
            {
                NavigationColumn.MinWidth = 0;
                NavigationColumn.Width = new GridLength(16);
                SplitterColumn.Width = new GridLength(0);
                LibraryCollapseRotation.Angle = 180;
                LibraryNavigatorPanel.Visibility = Visibility.Collapsed;
                CollapseLibraryButton.BorderThickness = new Thickness(0, 1, 1, 1);
            }
            else
            {
                NavigationColumn.MinWidth = 150;
                NavigationColumn.Width = new GridLength(_lastLibraryWidth);
                SplitterColumn.Width = new GridLength(5);
            }

            if (_isChatCollapsed)
            {
                ChatColumn.Width = new GridLength(16);
                ChatSplitter.Width = 0;
                ChatCollapseRotation.Angle = 180;
                ChatSidebar.Visibility = Visibility.Collapsed;
            }
            else
            {
                ChatColumn.Width = new GridLength(_lastChatWidth);
                ChatSplitter.Width = 5;
            }
        }

        private void SaveSidebarStates()
        {
            // Save the current width values to configuration
            if (!_isLibraryCollapsed)
            {
                _lastLibraryWidth = NavigationColumn.ActualWidth;
                _configService.Provider.SetValue(ConfigurationKeys.Library.LastWidth, _lastLibraryWidth);
            }
            else
            {
                _configService.Provider.SetValue(ConfigurationKeys.Library.LastWidth, _lastLibraryWidth);
            }

            if (!_isChatCollapsed)
            {
                _lastChatWidth = ChatColumn.ActualWidth;
                _configService.Provider.SetValue(ConfigurationKeys.Chat.LastWidth, _lastChatWidth);
            }
            else
            {
                _configService.Provider.SetValue(ConfigurationKeys.Chat.LastWidth, _lastChatWidth);
            }

            _configService.Provider.SetValue(ConfigurationKeys.Library.IsExpanded, !_isLibraryCollapsed);
            _configService.Provider.SetValue(ConfigurationKeys.Chat.IsExpanded, !_isChatCollapsed);
            _configService.Provider.Save();
        }

        private void CollapseLibrary_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLibraryCollapsed)
            {
                // Store the current width before collapsing
                _lastLibraryWidth = NavigationColumn.ActualWidth;
                NavigationColumn.MinWidth = 0;
                NavigationColumn.Width = new GridLength(16);
                SplitterColumn.Width = new GridLength(0);
                LibraryCollapseRotation.Angle = 180;
                LibraryNavigatorPanel.Visibility = Visibility.Collapsed;
                CollapseLibraryButton.BorderThickness = new Thickness(0, 1, 1, 1);
            }
            else
            {
                // Restore the previous width
                NavigationColumn.MinWidth = 150;
                NavigationColumn.Width = new GridLength(_lastLibraryWidth);
                SplitterColumn.Width = new GridLength(5);
                LibraryCollapseRotation.Angle = 0;
                LibraryNavigatorPanel.Visibility = Visibility.Visible;
                CollapseLibraryButton.BorderThickness = new Thickness(1);
            }
            _isLibraryCollapsed = !_isLibraryCollapsed;
            SaveSidebarStates();
        }

        private void CollapseChat_Click(object sender, RoutedEventArgs e)
        {
            if (!_isChatCollapsed)
            {
                // Store the current width before collapsing
                _lastChatWidth = ChatColumn.ActualWidth;
                ChatColumn.Width = new GridLength(16);
                ChatSplitter.Width = 0;
                ChatCollapseRotation.Angle = 180;
                ChatSidebar.Visibility = Visibility.Collapsed;
            }
            else
            {
                // Restore the previous width
                ChatColumn.Width = new GridLength(_lastChatWidth);
                ChatSplitter.Width = 5;
                ChatCollapseRotation.Angle = 0;
                ChatSidebar.Visibility = Visibility.Visible;
            }
            _isChatCollapsed = !_isChatCollapsed;
            SaveSidebarStates();
        }

        private void SaveOpenTabs()
        {
            var openTabs = new List<string>();
            string activeTab = null;

            foreach (TabItem tab in MainTabControl.Items)
            {
                if (tab.Tag != null)
                {
                    var tagStr = tab.Tag.ToString();
                    // For file tabs, store the file path
                    if (File.Exists(tagStr) || tagStr == "music" || tagStr == "overview" || tagStr == "globalagenda")
                    {
                        openTabs.Add(tagStr);
                        if (tab == MainTabControl.SelectedItem)
                        {
                            activeTab = tagStr;
                        }
                    }
                }
                else if (tab.Content is MusicTab)
                {
                    openTabs.Add("music");
                    if (tab == MainTabControl.SelectedItem)
                    {
                        activeTab = "music";
                    }
                }
                else if (tab.Content is OverviewTab)
                {
                    openTabs.Add("overview");
                    if (tab == MainTabControl.SelectedItem)
                    {
                        activeTab = "overview";
                    }
                }
                else if (tab.Content is Tabs.GlobalAgendaTab)
                {
                    openTabs.Add("globalagenda");
                    if (tab == MainTabControl.SelectedItem)
                    {
                        activeTab = "globalagenda";
                    }
                }
                else if (tab.Content is ProjectTab projectTab && projectTab.FilePath != null)
                {
                    openTabs.Add(projectTab.FilePath);
                    if (tab == MainTabControl.SelectedItem)
                    {
                        activeTab = projectTab.FilePath;
                    }
                }
            }
            
            _configService.Provider.SetValue(ConfigurationKeys.Library.OpenTabs, string.Join("|", openTabs));
            _configService.Provider.SetValue(ConfigurationKeys.Library.ActiveTab, activeTab);
            _configService.Provider.Save();
        }

        private void RestoreOpenTabs()
        {
            var openTabsString = _configService.Provider.GetValue<string>(ConfigurationKeys.Library.OpenTabs);
            var activeTab = _configService.Provider.GetValue<string>(ConfigurationKeys.Library.ActiveTab);
            if (string.IsNullOrEmpty(openTabsString)) return;

            TabItem activeTabItem = null;
            var openTabs = openTabsString.Split('|', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var tab in openTabs)
            {
                TabItem newTab = null;
                switch (tab.ToLower())
                {
                    case "music":
                        OpenMusicTab();
                        newTab = MainTabControl.Items[MainTabControl.Items.Count - 1] as TabItem;
                        break;
                    case "overview":
                        OpenOverviewTab();
                        newTab = MainTabControl.Items[MainTabControl.Items.Count - 1] as TabItem;
                        break;
                    case "globalagenda":
                        OpenGlobalAgendaTab();
                        newTab = MainTabControl.Items[MainTabControl.Items.Count - 1] as TabItem;
                        break;
                    default:
                        // Assume it's a file path
                        if (File.Exists(tab))
                        {
                            var extension = Path.GetExtension(tab).ToLower();
                            if (extension == ".org" || extension == ".md" || extension == ".project" || extension == ".todo")
                            {
                                OpenFileInEditor(tab);
                                newTab = MainTabControl.Items[MainTabControl.Items.Count - 1] as TabItem;
                            }
                        }
                        break;
                }

                if (newTab != null && tab == activeTab)
                {
                    activeTabItem = newTab;
                }
            }

            // Set the active tab after all tabs are restored
            if (activeTabItem != null)
            {
                MainTabControl.SelectedItem = activeTabItem;
            }
            else if (MainTabControl.Items.Count > 0)
            {
                MainTabControl.SelectedItem = MainTabControl.Items[0];
            }
        }

        private void SaveWindowState()
        {
            if (WindowState == WindowState.Normal)
            {
                _configService.Provider.SetValue(ConfigurationKeys.Window.Left, Left);
                _configService.Provider.SetValue(ConfigurationKeys.Window.Top, Top);
                _configService.Provider.SetValue(ConfigurationKeys.Window.Width, Width);
                _configService.Provider.SetValue(ConfigurationKeys.Window.Height, Height);
            }
            _configService.Provider.SetValue(ConfigurationKeys.Window.State, WindowState.ToString());
            _configService.Provider.Save();
        }

        private void RestoreWindowState()
        {
            try
            {
                // Ensure window style is set correctly
                WindowStyle = WindowStyle.SingleBorderWindow;
                
                // Get saved window state
                var savedState = _configService.Provider.GetValue<string>(ConfigurationKeys.Window.State);
                if (Enum.TryParse<WindowState>(savedState, out var windowState))
                {
                    WindowState = windowState;
                }

                // Only restore size and position if we have all values and we're not maximized
                if (windowState != WindowState.Maximized)
                {
                    var left = _configService.Provider.GetValue<double>(ConfigurationKeys.Window.Left);
                    var top = _configService.Provider.GetValue<double>(ConfigurationKeys.Window.Top);
                    var width = _configService.Provider.GetValue<double>(ConfigurationKeys.Window.Width);
                    var height = _configService.Provider.GetValue<double>(ConfigurationKeys.Window.Height);

                    // Only apply if we have valid values
                    if (width > 0 && height > 0)
                    {
                        Width = width;
                        Height = height;

                        // Ensure the window is visible on the current screen setup
                        var virtualScreenWidth = SystemParameters.VirtualScreenWidth;
                        var virtualScreenHeight = SystemParameters.VirtualScreenHeight;

                        if (left >= 0 && top >= 0 && 
                            left + width <= virtualScreenWidth && 
                            top + height <= virtualScreenHeight)
                        {
                            Left = left;
                            Top = top;
                        }
                    }
                }
                
                // Force a refresh of the window chrome
                this.Dispatcher.BeginInvoke(new Action(() => {
                    var hwnd = new WindowInteropHelper(this).Handle;
                    if (hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE |
                            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_FRAMECHANGED);
                    }
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                // If anything goes wrong, use default size
                Width = 1024;
                Height = 768;
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.I && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                OpenInboxTab();
                e.Handled = true;
            }
            // Debug command to test enhanced text search (Ctrl+Shift+T)
            else if (e.Key == Key.T && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                RunTextSearchTests();
                e.Handled = true;
            }
        }

        private void RunTextSearchTests()
        {
            try
            {
                Debug.WriteLine("=== Running Enhanced Text Search Tests ===");
                Tests.EnhancedTextSearchServiceTests.RunAllTests();
                MessageBox.Show("Enhanced Text Search tests completed successfully! Check the debug output for details.", 
                    "Test Results", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Test failed: {ex.Message}");
                MessageBox.Show($"Enhanced Text Search tests failed: {ex.Message}", 
                    "Test Results", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void OpenInboxTab()
        {
            // Check if inbox tab is already open
            var existingTab = FindTab("Inbox");
            if (existingTab != null)
            {
                MainTabControl.SelectedItem = existingTab;
                return;
            }

            // Create new inbox tab
            var inboxTab = new InboxTab();
            var newTab = new TabItem
            {
                Header = "Inbox",
                Content = inboxTab,
                Tag = "Inbox"
            };

            MainTabControl.Items.Add(newTab);
            MainTabControl.SelectedItem = newTab;
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                Debug.WriteLine("MainWindow_Closing: Saving chat history...");
                
                // Save chat history
                if (ChatSidebar?.ViewModel != null)
                {
                    ChatSidebar.ViewModel.SaveState();
                    Debug.WriteLine("MainWindow_Closing: Chat history saved successfully");
                }
                else
                {
                    Debug.WriteLine("MainWindow_Closing: ChatSidebar or ViewModel is null");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainWindow_Closing: Error saving chat history: {ex.Message}");
            }
        }

        public void RefreshOpenFileTab(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            var tab = FindTab(filePath);
            if (tab?.Content is IFileTab fileTab)
            {
                System.Diagnostics.Debug.WriteLine($"RefreshOpenFileTab: Found tab for {filePath}, tab type: {fileTab.GetType().Name}");
                
                // Check if it's an OrgModeTab and log the current item count
                if (fileTab is Tabs.OrgModeTab orgTab)
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshOpenFileTab: Before reload - OrgModeTab has {orgTab.Items.Count} items");
                }
                
                // Force reload the file content
                fileTab.Reload();
                
                // Reset modified state since we're reloading from disk
                fileTab.IsModified = false;
                
                // Check item count after reload
                if (fileTab is Tabs.OrgModeTab orgTabAfter)
                {
                    System.Diagnostics.Debug.WriteLine($"RefreshOpenFileTab: After reload - OrgModeTab has {orgTabAfter.Items.Count} items");
                }
                
                System.Diagnostics.Debug.WriteLine($"RefreshOpenFileTab: Completed refresh for {filePath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"RefreshOpenFileTab: No tab found for {filePath} or tab is not IFileTab");
            }
        }

        public void RefreshOpenFileTabs(IEnumerable<string> filePaths)
        {
            foreach (var filePath in filePaths)
            {
                RefreshOpenFileTab(filePath);
            }
        }
    }

    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Func<object, bool> _canExecute;

        public RelayCommand(Action<object> execute, Func<object, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute(parameter);
        }

        public void Execute(object parameter)
        {
            _execute(parameter);
        }
    }
} 