using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Diagnostics;
using System.Net.Http;
using System.IO;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Commands;
using Universa.Desktop.Views;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Core.Logging;
using System.Windows.Threading;
using Universa.Desktop.Library;
using System.Text;
using System.Threading;
using Universa.Desktop.Tabs;
using Universa.Desktop.Interfaces;
using System.Text.RegularExpressions;

namespace Universa.Desktop.ViewModels
{
    public class ChatSidebarViewModel : INotifyPropertyChanged
    {
        private readonly ModelProvider _modelProvider;
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        // Services are now stored per-tab in ChatTabViewModel.Service
        private ObservableCollection<Models.ChatMessage> _messages;
        private ObservableCollection<Models.ChatMessage> _chatModeMessages;
        private ObservableCollection<AIModelInfo> _availableModels;
        private AIModelInfo _selectedModel;
        private string _inputText;
        private bool _isContextMode = true;
        private IFileTab _currentEditor;
        private MusicTab _currentMusicTab;
        private bool _isInitializing = true;
        private string _lastUserMessage;
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isDeviceVerified;
        
        // New fields to support tab improvements
        private Dictionary<Models.ChatMessage, CancellationTokenSource> _messageRequests = new Dictionary<Models.ChatMessage, CancellationTokenSource>();
        private bool _isBusy;
        
        // New fields for tab support
        private ObservableCollection<ChatTabViewModel> _tabs;
        private ChatTabViewModel _selectedTab;

        private readonly ChatHistoryService _chatHistoryService;
        private DispatcherTimer _autoSaveTimer;

        private Dictionary<Views.MarkdownTabAvalon, ChatTabViewModel> _markdownTabAssociations = new Dictionary<Views.MarkdownTabAvalon, ChatTabViewModel>();

        public ScrollViewer ChatScrollViewer { get; set; }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy != value)
                {
                    _isBusy = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // Expose tabs for binding
        public ObservableCollection<ChatTabViewModel> Tabs
        {
            get => _tabs;
            set
            {
                if (_tabs != value)
                {
                    _tabs = value;
                    OnPropertyChanged();
                }
            }
        }

        // Selected tab property
        public ChatTabViewModel SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (_selectedTab != value)
                {
                    // Save current scroll position before switching tabs
                    if (_selectedTab != null && ChatScrollViewer != null)
                    {
                        _selectedTab.CurrentScrollPosition = ChatScrollViewer.VerticalOffset;
                        System.Diagnostics.Debug.WriteLine($"Saved scroll position for tab '{_selectedTab.Name}': {_selectedTab.CurrentScrollPosition}");
                    }
                    
                    // Save current input text to the previous tab before switching
                    if (_selectedTab != null)
                    {
                        _selectedTab.InputText = InputText;
                    }
                    
                    _selectedTab = value;
                    
                    // When tab changes, update input text and messages
                    if (_selectedTab != null)
                    {
                        // Always directly reference the tab's Messages collection
                        Messages = _selectedTab.IsContextMode ? 
                            _selectedTab.Messages : 
                            _selectedTab.ChatModeMessages;
                        InputText = _selectedTab.InputText;
                        // Update UI to reflect this tab's settings
                        OnPropertyChanged(nameof(IsContextMode));
                        OnPropertyChanged(nameof(SelectedModel));
                        
                        // Update markdown tab associations for the current editor
                        UpdateMarkdownTabAssociationForCurrentTab();
                        
                        // Restore scroll position after a short delay to allow UI to update
                        System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                        {
                            RestoreScrollPosition();
                        }), System.Windows.Threading.DispatcherPriority.Background);
                    }
                    
                    OnPropertyChanged();
                    
                    // Save tabs when selection changes
                    SaveTabs();
                }
            }
        }

        // New command for adding tabs
        public ICommand AddTabCommand { get; private set; }
        
        // New command for closing tabs
        public ICommand CloseTabCommand { get; private set; }

        public ICommand ClearHistoryCommand { get; private set; }

        public ObservableCollection<Models.ChatMessage> Messages
        {
            get => _messages;
            set
            {
                if (_messages != value)
                {
                    _messages = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<AIModelInfo> AvailableModels
        {
            get => _availableModels;
            set
            {
                if (_availableModels != value)
                {
                    _availableModels = value;
                    OnPropertyChanged();
                }
            }
        }

        public AIModelInfo SelectedModel
        {
            get => SelectedTab?.SelectedModel ?? _selectedModel;
            set
            {
                if (SelectedTab != null)
                {
                    if (SelectedTab.SelectedModel != value)
                    {
                        SelectedTab.SelectedModel = value;
                        OnPropertyChanged();
                        
                        if (value != null)
                        {
                            Configuration.Instance.LastUsedModel = value.Name;
                            Configuration.Instance.Save();
                            
                            // Dispose of the current service
                            DisposeCurrentService();
                            
                            // Only add the system message if not during initialization
                            if (!_isInitializing)
                            {
                                // Add a system message indicating the model change
                                var systemMessage = new Models.ChatMessage("system", $"Switched to {value.DisplayName}")
                                {
                                    ModelName = value.Name,
                                    Provider = value.Provider
                                };
                                Messages.Add(systemMessage);
                                
                                // If in chat mode, update chat mode messages and recreate the service
                                if (!IsContextMode)
                                {
                                                                _chatModeMessages.Add(systemMessage);
                            var apiKey = GetApiKey(value.Provider);
                            // Create a new instance for this tab instead of using singleton  
                            SelectedTab.Service = new GeneralChatService(apiKey, value.Name, value.Provider, value.IsThinkingMode);
                                }
                            }
                        }
                    }
                }
                else if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged();
                    // Same logic for when there's no selected tab (fallback)
                    if (value != null)
                    {
                        Configuration.Instance.LastUsedModel = value.Name;
                        Configuration.Instance.Save();
                        
                        // Dispose of the current service
                        DisposeCurrentService();
                        
                        // Only add the system message if not during initialization
                        if (!_isInitializing)
                        {
                            // Add a system message indicating the model change
                            var systemMessage = new Models.ChatMessage("system", $"Switched to {value.DisplayName}")
                            {
                                ModelName = value.Name,
                                Provider = value.Provider
                            };
                            Messages.Add(systemMessage);
                            
                            // If in chat mode, update chat mode messages and recreate the service
                            if (!IsContextMode)
                            {
                                _chatModeMessages.Add(systemMessage);
                                var apiKey = GetApiKey(value.Provider);
                                // Create a new instance for this tab instead of using singleton
                                SelectedTab.Service = new GeneralChatService(apiKey, value.Name, value.Provider, value.IsThinkingMode);
                            }
                        }
                    }
                }
            }
        }

        public string InputText
        {
            get => _inputText;
            set
            {
                if (_inputText != value)
                {
                    _inputText = value;
                    OnPropertyChanged();
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public bool IsContextMode
        {
            get => SelectedTab?.IsContextMode ?? _isContextMode;
            set
            {
                Debug.WriteLine($"IsContextMode changing from {(SelectedTab?.IsContextMode ?? _isContextMode)} to {value}");
                if (SelectedTab != null)
                {
                    if (SelectedTab.IsContextMode != value)
                    {
                        SelectedTab.IsContextMode = value;
                        OnPropertyChanged();
                        Debug.WriteLine("Calling UpdateMode()");
                        UpdateMode();
                    }
                }
                else
                {
                    if (_isContextMode != value)
                    {
                        _isContextMode = value;
                        OnPropertyChanged();
                        UpdateMode();
                    }
                }
            }
        }

        public ICommand SendCommand { get; private set; }
        public ICommand ToggleModeCommand { get; private set; }
        public ICommand VerifyDeviceCommand { get; private set; }
        public ICommand RetryCommand { get; private set; }
        public ICommand StopThinkingCommand { get; private set; }

        public string MatrixUserId
        {
            get => MatrixClient.Instance.UserId;
        }

        public string MatrixDeviceId
        {
            get => MatrixClient.Instance.DeviceId;
        }

        public bool IsDeviceVerified
        {
            get => _isDeviceVerified;
            set
            {
                _isDeviceVerified = value;
                OnPropertyChanged();
            }
        }

        public ChatSidebarViewModel(ModelProvider modelProvider = null)
        {
            _isInitializing = true;
            
            // Initialize collections
            _messages = new ObservableCollection<Models.ChatMessage>();
            _chatModeMessages = new ObservableCollection<Models.ChatMessage>();
            _availableModels = new ObservableCollection<AIModelInfo>();
            _tabs = new ObservableCollection<ChatTabViewModel>();
            
            // Initialize services
            _configService = ServiceLocator.Instance.GetRequiredService<IConfigurationService>();
            _config = _configService.Provider;
            _modelProvider = modelProvider ?? new ModelProvider(_configService);
            _modelProvider.ModelsChanged += OnModelsChanged;
            _chatHistoryService = ChatHistoryService.Instance;
            
            // Setup autosave timer
            _autoSaveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2) // Save every 2 minutes
            };
            _autoSaveTimer.Tick += (s, e) => SaveTabs();
            _autoSaveTimer.Start();
            
            // Initialize commands
            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            ToggleModeCommand = new RelayCommand(_ => UpdateMode());
            VerifyDeviceCommand = new RelayCommand(async _ => await VerifyDeviceAsync());
            RetryCommand = new RelayCommand(async param => await RetryMessageAsync(param as Models.ChatMessage));
            StopThinkingCommand = new RelayCommand(param => StopThinking(param as Models.ChatMessage));
            
            // New commands for tabs
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as ChatTabViewModel));
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());

            // Subscribe to messages collection changes
            _messages.CollectionChanged += Messages_CollectionChanged;

            // Subscribe to window state changes to handle scroll restoration after maximize/minimize
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.StateChanged += MainWindow_StateChanged;
                mainWindow.SizeChanged += MainWindow_SizeChanged;
            }

            // Load saved tabs or create initial tab if none exist
            if (!LoadSavedTabs())
            {
                // Create initial tab if no saved tabs
                AddNewTab("General Chat");
            }

            // Load initial models and complete initialization
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await RefreshModels();
            _isInitializing = false;
        }

        private void OnModelsChanged(object sender, List<AIModelInfo> models)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    Debug.WriteLine("OnModelsChanged: Refreshing available models");
                    AvailableModels.Clear();
                    foreach (var model in models)
                    {
                        AvailableModels.Add(model);
                    }

                    // Restore last used model if available
                    if (!string.IsNullOrEmpty(Configuration.Instance.LastUsedModel))
                    {
                        Debug.WriteLine($"Looking for last used model: {Configuration.Instance.LastUsedModel}");
                        var lastModel = models.FirstOrDefault(m => m.Name == Configuration.Instance.LastUsedModel);
                        if (lastModel != null)
                        {
                            Debug.WriteLine($"Found last used model: {lastModel.Name}");
                            _selectedModel = lastModel;
                        }
                    }

                    if (_selectedModel == null && models.Count > 0)
                    {
                        Debug.WriteLine($"No saved model found, using first available: {models[0].Name}");
                        _selectedModel = models[0];
                    }
                    
                    // Restore tab models from saved data
                    Debug.WriteLine($"Restoring models for {Tabs.Count} tabs");
                    foreach (var tab in Tabs)
                    {
                        if (tab.Tag != null)
                        {
                            try
                            {
                                dynamic savedModelInfo = tab.Tag;
                                string modelName = savedModelInfo.ModelName;
                                string providerStr = savedModelInfo.ModelProvider;
                                
                                Debug.WriteLine($"Tab '{tab.Name}' has saved model: {modelName}, provider: {providerStr}");
                                
                                if (!string.IsNullOrEmpty(modelName) && !string.IsNullOrEmpty(providerStr))
                                {
                                    // Try to parse the provider
                                    if (Enum.TryParse<AIProvider>(providerStr, out var provider))
                                    {
                                        Debug.WriteLine($"Successfully parsed provider: {provider}");
                                        
                                        // Find the model in available models
                                        var model = models.FirstOrDefault(m => 
                                            m.Name == modelName && m.Provider == provider);
                                            
                                        if (model != null)
                                        {
                                            Debug.WriteLine($"Found matching model for tab: {model.Name}");
                                            tab.SelectedModel = model;
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"Could not find exact model match, falling back to default");
                                            // Fallback to default model
                                            tab.SelectedModel = _selectedModel;
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine($"Could not parse provider string: {providerStr}");
                                        tab.SelectedModel = _selectedModel;
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("Model name or provider is empty, using default model");
                                    tab.SelectedModel = _selectedModel;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error restoring model for tab: {ex.Message}");
                                tab.SelectedModel = _selectedModel;
                            }
                            
                            // Clear the tag as we don't need it anymore
                            tab.Tag = null;
                        }
                        else if (tab.SelectedModel == null)
                        {
                            Debug.WriteLine($"Tab has no saved model info, using default model");
                            tab.SelectedModel = _selectedModel;
                        }
                    }
                    
                    // Force UI update for the selected tab
                    OnPropertyChanged(nameof(SelectedModel));
                    
                    // Save state after restoring to ensure future consistency
                    SaveTabs();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in OnModelsChanged: {ex.Message}");
                }
            });
        }

        private void MainTabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            Debug.WriteLine("MainTabControl_SelectionChanged called");
            if (e.RemovedItems.Count > 0)
            {
                if (e.RemovedItems[0] is System.Windows.Controls.TabItem oldTab)
                {
                    if (oldTab.Content is MusicTab oldMusicTab)
                    {
                        UnsubscribeFromMusicTab(oldMusicTab);
                    }
                    else if (oldTab.Content is EditorTab oldEditorTab)
                    {
                        Debug.WriteLine("Unsubscribing from old editor tab");
                        if (_currentEditor != null && _currentEditor.FilePath == oldEditorTab.FilePath)
                        {
                            _currentEditor = null;
                        }
                    }
                    else if (oldTab.Content is Views.MarkdownTabAvalon oldMarkdownTab)
                    {
                        Debug.WriteLine("Unsubscribing from old markdown tab");
                        UnsubscribeFromMarkdownTab(oldMarkdownTab);
                    }
                }
            }

            if (e.AddedItems.Count > 0)
            {
                if (e.AddedItems[0] is System.Windows.Controls.TabItem newTab)
                {
                    if (newTab.Content is MusicTab newMusicTab)
                    {
                        SubscribeToMusicTab(newMusicTab);
                    }
                    else if (newTab.Content is EditorTab newEditorTab)
                    {
                        Debug.WriteLine($"Setting current editor to new tab: {newEditorTab.FilePath}");
                        _currentEditor = newEditorTab;
                    }
                    else if (newTab.Content is Views.MarkdownTabAvalon newMarkdownTab)
                    {
                        Debug.WriteLine($"Setting current markdown tab: {newMarkdownTab.FilePath}");
                        _currentEditor = newMarkdownTab;
                        SubscribeToMarkdownTab(newMarkdownTab);
                        
                        // Update the association with the current chat tab
                        UpdateMarkdownTabAssociationForCurrentTab();
                    }
                }
            }
        }

        private void SubscribeToMusicTab(MusicTab musicTab)
        {
            if (_currentMusicTab != musicTab)
            {
                UnsubscribeFromMusicTab(_currentMusicTab);
                _currentMusicTab = musicTab;
                
                if (_currentMusicTab != null)
                {
                    Debug.WriteLine("Subscribing to music tab property changes");
                    _currentMusicTab.PropertyChanged += MusicTab_PropertyChanged;
                }
            }
        }

        private void UnsubscribeFromMusicTab(MusicTab musicTab)
        {
            if (musicTab != null)
            {
                Debug.WriteLine("Unsubscribing from music tab property changes");
                musicTab.PropertyChanged -= MusicTab_PropertyChanged;
            }
        }

        private void MusicTab_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MusicTab.ContentListView_Control))
            {
                _ = RefreshMusicContext();
            }
        }

        private async Task RefreshMusicContext()
        {
            if (_currentMusicTab != null)
            {
                var selectedItems = _currentMusicTab.ContentListView_Control?.SelectedItems;
                if (selectedItems?.Cast<object>().Any() == true)
                {
                    // Process selected items
                    var selectedTracks = selectedItems.Cast<MusicItem>()
                        .Select(item => $"{item.Name} by {item.ArtistName} from {item.Album}")
                        .ToList();
                    
                    // Update context with selected tracks
                    await UpdateContext($"Selected music tracks: {string.Join(", ", selectedTracks)}");
                }
                else
                {
                    // Clear music context if nothing is selected
                    await UpdateContext(null);
                }
            }
        }

        private async Task RefreshModels()
        {
            try
            {
                Debug.WriteLine("Refreshing available AI models...");
                var models = await _modelProvider.GetModels();
                
                // Update models on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    foreach (var model in models)
                    {
                        AvailableModels.Add(model);
                    }

                    // If we have models but none selected, select the first one
                    if (AvailableModels.Any() && SelectedModel == null)
                    {
                        SelectedModel = AvailableModels.First();
                    }
                    
                    Debug.WriteLine($"Loaded {AvailableModels.Count} models");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing models: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                bool shouldAutoScroll = false;
                
                // Check if user is near the bottom before auto-scrolling
                if (ChatScrollViewer != null)
                {
                    var scrollableHeight = ChatScrollViewer.ScrollableHeight;
                    var currentOffset = ChatScrollViewer.VerticalOffset;
                    var threshold = 50; // pixels from bottom
                    
                    // Auto-scroll only if user is near the bottom or if this is the first message
                    shouldAutoScroll = (scrollableHeight == 0) || // No scrollable content yet
                                     (scrollableHeight - currentOffset <= threshold); // Near bottom
                }
                else
                {
                    shouldAutoScroll = true; // Default to auto-scroll if no scroll viewer
                }
                
                if (shouldAutoScroll)
                {
                    // Scroll to the bottom of the chat view when new messages are added
                    ChatScrollViewer?.ScrollToEnd();
                }
                else
                {
                    // Save the current position so we don't lose it
                    SaveCurrentScrollPosition();
                }
                
                // Save chat history after each new message
                SaveState();
            }
        }

        private void UpdateMode()
        {
            try
            {
                // Get the current tab and its context mode setting
                var currentTab = SelectedTab;
                if (currentTab == null) return;
                
                // Save current scroll position before switching modes
                if (ChatScrollViewer != null)
                {
                    currentTab.CurrentScrollPosition = ChatScrollViewer.VerticalOffset;
                    System.Diagnostics.Debug.WriteLine($"Saved scroll position for mode switch in tab '{currentTab.Name}': {currentTab.CurrentScrollPosition}");
                }
                
                bool isContextMode = currentTab.IsContextMode;
                
                Debug.WriteLine($"UpdateMode - Current mode: {(isContextMode ? "Context" : "Chat")}");
                
                // Dispose of the current service for this tab
                if (currentTab.Service != null)
                {
                    currentTab.Service.Dispose();
                    currentTab.Service = null;
                }
                
                if (isContextMode)
                {
                    Debug.WriteLine("Switching to context mode");
                    // Use the tab's main message collection for context mode
                    Messages = currentTab.Messages;
                }
                else
                {
                    Debug.WriteLine("Switching to chat mode");
                    // Use the tab's chat-specific message collection
                    Messages = currentTab.ChatModeMessages;
                    
                    // Create a new service instance for chat mode
                    var selectedModel = currentTab.SelectedModel;
                    if (selectedModel != null)
                    {
                        var apiKey = GetApiKey(selectedModel.Provider);
                        Debug.WriteLine($"Creating new GeneralChatService with provider: {selectedModel.Provider}");
                        var service = new GeneralChatService(apiKey, selectedModel.Name, selectedModel.Provider, selectedModel.IsThinkingMode);
                        
                        // Initialize with previous messages
                        if (currentTab.ChatModeMessages.Count > 0)
                        {
                            bool systemMessageAdded = false;
                            foreach (var msg in currentTab.ChatModeMessages)
                            {
                                if (msg.Role == "system" && !systemMessageAdded)
                                {
                                    service.AddSystemMessage(msg.Content);
                                    systemMessageAdded = true;
                                }
                                else if (msg.Role == "user")
                                {
                                    service.AddUserMessage(msg.Content);
                                }
                                else if (msg.Role == "assistant")
                                {
                                    service.AddAssistantMessage(msg.Content);
                                }
                            }
                        }
                        
                        currentTab.Service = service;
                    }
                }
                
                Debug.WriteLine("Clearing input and raising property changes");
                InputText = string.Empty;
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(IsContextMode));
                
                // Restore scroll position after UI updates
                System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    RestoreScrollPosition();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateMode: {ex}");
                MessageBox.Show($"Error switching modes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisposeCurrentService()
        {
            try
            {
                // Dispose of the current tab's service
                if (SelectedTab?.Service != null)
                {
                    SelectedTab.Service.Dispose();
                    SelectedTab.Service = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error disposing services: {ex.Message}");
            }
        }

        private void RetryEventHandler(object sender, RetryEventArgs args)
        {
            Debug.WriteLine($"Retry event received: {args.RetryCount}/{args.MaxRetries}, delay: {args.DelayMs}ms");
            
            var thinkingMessage = Messages.LastOrDefault(m => m.Role == "assistant" && 
                (m.Content.StartsWith("Thinking") || m.Content.StartsWith("⏳")));
            if (thinkingMessage != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    thinkingMessage.Content = $"⏳ API overloaded. Retry {args.RetryCount}/{args.MaxRetries} in {args.DelayMs/1000.0:F1}s...";
                    // Force UI update
                    OnPropertyChanged(nameof(Messages));
                });
            }
        }

        private async Task<BaseLangChainService> GetOrCreateService()
        {
            try
            {
                // Make absolutely sure we have a selected tab
                if (SelectedTab == null)
                {
                    Debug.WriteLine("GetOrCreateService: SelectedTab is null, cannot create service");
                    return null;
                }

                Debug.WriteLine($"GetOrCreateService: Tab name: {SelectedTab.Name}, IsContextMode: {SelectedTab.IsContextMode}");

                // CRITICAL FIX: Check the tab's own service first, not the global _currentService
                if (SelectedTab.Service != null)
                {
                    try
                    {
                        // Use the ThrowIfDisposed method indirectly by calling a harmless method
                        await SelectedTab.Service.UpdateContextAsync(string.Empty);
                        Debug.WriteLine($"Reusing existing service instance for tab: {SelectedTab.Name}");
                        return SelectedTab.Service;
                    }
                    catch (ObjectDisposedException)
                    {
                        // Service is disposed, continue to create a new one
                        Debug.WriteLine($"Tab's service is disposed, creating a new one for tab: {SelectedTab.Name}");
                        // Clear the reference to avoid reusing the disposed service
                        SelectedTab.Service = null;
                    }
                }

                // Get the API key and other parameters
                var apiKey = GetApiKey(SelectedTab.SelectedModel?.Provider ?? AIProvider.OpenAI);
                var modelName = SelectedTab.SelectedModel?.Name ?? "gpt-4";
                var provider = SelectedTab.SelectedModel?.Provider ?? AIProvider.OpenAI;
                var isThinkingMode = SelectedTab.SelectedModel?.IsThinkingMode ?? false;

                // IMPORTANT: Use the tab's context mode, not the global property
                var isTabContextMode = SelectedTab.IsContextMode;
                Debug.WriteLine($"Using tab's context mode: {isTabContextMode}");

                // Get the current markdown tab from the main window directly
                IFileTab currentMarkdownTab = GetCurrentMarkdownTab();
                Debug.WriteLine($"Current markdown tab: {currentMarkdownTab?.FilePath ?? "null"}");

                // If we're in context mode and have a markdown tab, create a specialized service
                if (isTabContextMode && currentMarkdownTab != null)
                {
                    var filePath = currentMarkdownTab.FilePath;
                    var content = await GetCurrentContent();
                    
                    Debug.WriteLine($"Processing markdown tab with file path: {filePath}");
                    
                    // Check for outline files first
                    bool isOutline = IsOutlineFile(content, filePath);
                    Debug.WriteLine($"IsOutlineFile result: {isOutline}");
                    
                    if (isOutline)
                    {
                        Debug.WriteLine("Creating OutlineWritingBeta service for outline file");
                        
                        try
                        {
                            // CRITICAL: Determine the correct library path that will handle relative path references
                            string libraryPath = DetermineAppropriateLibraryPath(filePath, content);
                            Debug.WriteLine($"Using determined library path: {libraryPath}");
                            
                            // Create OutlineWritingBeta service with the properly determined library path
                            var outlineService = await OutlineWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                            SelectedTab.Service = outlineService;
                            
                            // Update the content immediately
                            await SelectedTab.Service.UpdateContextAsync(content);
                            
                            // Set cursor position if it's an OutlineWritingBeta
                            if (SelectedTab.Service is OutlineWritingBeta outlineBeta)
                            {
                                outlineBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                                Debug.WriteLine($"Updated cursor position to: {currentMarkdownTab.LastKnownCursorPosition}");
                                
                                // Subscribe to retry events
                                outlineBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                            }
                            
                            Debug.WriteLine("Successfully created OutlineWritingBeta service");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error creating OutlineWritingBeta service: {ex}");
                            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                            
                            // Fallback to general chat service
                            Debug.WriteLine("Falling back to GeneralChatService due to OutlineWritingBeta creation failure");
                            SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                            await SelectedTab.Service.UpdateContextAsync(content);
                        }
                    }
                    else
                    {
                        // Check for non-fiction files second
                        bool isNonFiction = IsNonFictionFile(content, filePath);
                        Debug.WriteLine($"IsNonFictionFile result: {isNonFiction}");
                        
                        if (isNonFiction)
                        {
                            Debug.WriteLine("Creating NonFictionWritingBeta service for non-fiction file");
                            
                            try
                            {
                                // CRITICAL: Determine the correct library path that will handle relative path references
                                string libraryPath = DetermineAppropriateLibraryPath(filePath, content);
                                Debug.WriteLine($"Using determined library path: {libraryPath}");
                                
                                // Create NonFictionWritingBeta service with the properly determined library path
                                var nonfictionService = await NonFictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                                SelectedTab.Service = nonfictionService;
                                
                                // Update the content immediately
                                await SelectedTab.Service.UpdateContextAsync(content);
                                
                                // Set cursor position if it's a NonFictionWritingBeta
                                if (SelectedTab.Service is NonFictionWritingBeta nonfictionBeta)
                                {
                                    nonfictionBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                                    Debug.WriteLine($"Updated cursor position to: {currentMarkdownTab.LastKnownCursorPosition}");
                                    
                                    // Subscribe to retry events
                                    nonfictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                                }
                                
                                Debug.WriteLine("Successfully created NonFictionWritingBeta service");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error creating NonFictionWritingBeta service: {ex}");
                                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                
                                // Fallback to general chat service
                                Debug.WriteLine("Falling back to GeneralChatService due to NonFictionWritingBeta creation failure");
                                SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                                await SelectedTab.Service.UpdateContextAsync(content);
                            }
                        }
                        else
                        {
                            // Check for fiction files third
                            bool isFiction = IsFictionFile(content, filePath);
                            Debug.WriteLine($"IsFictionFile result: {isFiction}");
                            
                            if (isFiction)
                            {
                                Debug.WriteLine("Creating FictionWritingBeta service for fiction file");
                                
                                try
                                {
                                    // CRITICAL: Determine the correct library path that will handle relative path references
                                    string libraryPath = DetermineAppropriateLibraryPath(filePath, content);
                                    Debug.WriteLine($"Using determined library path: {libraryPath}");
                                    
                                    // Create FictionWritingBeta service with the properly determined library path
                                    var fictionService = await FictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                                    SelectedTab.Service = fictionService;
                                    
                                    // Update the content immediately
                                    await SelectedTab.Service.UpdateContextAsync(content);
                                    
                                    // Set cursor position if it's a FictionWritingBeta
                                    if (SelectedTab.Service is FictionWritingBeta fictionBeta)
                                    {
                                        fictionBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                                        Debug.WriteLine($"Updated cursor position to: {currentMarkdownTab.LastKnownCursorPosition}");
                                        
                                        // Subscribe to retry events
                                        fictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                                    }
                                    
                                    Debug.WriteLine("Successfully created FictionWritingBeta service");
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error creating FictionWritingBeta service: {ex}");
                                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                    
                                    // Fallback to general chat service
                                    Debug.WriteLine("Falling back to GeneralChatService due to FictionWritingBeta creation failure");
                                    SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                                    await SelectedTab.Service.UpdateContextAsync(content);
                                }
                            }
                            else
                            {
                                // Check for rules files fourth
                                bool isRules = IsRulesFile(content, filePath);
                                Debug.WriteLine($"IsRulesFile result: {isRules}");
                                
                                if (isRules)
                                {
                                    Debug.WriteLine("Creating RulesWritingBeta service for rules file");
                                    
                                    try
                                    {
                                        // CRITICAL: Determine the correct library path that will handle relative path references
                                        string libraryPath = DetermineAppropriateLibraryPath(filePath, content);
                                        Debug.WriteLine($"Using determined library path: {libraryPath}");
                                        
                                        // Create RulesWritingBeta service with the properly determined library path
                                        var rulesService = await RulesWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                                        SelectedTab.Service = rulesService;
                                        
                                        // Update the content immediately
                                        await SelectedTab.Service.UpdateContextAsync(content);
                                        
                                        Debug.WriteLine($"Successfully created RulesWritingBeta service");
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error creating RulesWritingBeta service: {ex.Message}");
                                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                        // Create a general context service as fallback
                                        Debug.WriteLine("Falling back to GeneralChatService due to RulesWritingBeta creation failure");
                                        SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                                        await SelectedTab.Service.UpdateContextAsync(content);
                                    }
                                }
                                else
                                {
                                    // Check for non-fiction files fifth
                                    bool isNonFictionFile = IsNonFictionFile(content, filePath);
                                    Debug.WriteLine($"IsNonFictionFile result: {isNonFictionFile}");
                                    
                                    if (isNonFictionFile)
                                    {
                                        Debug.WriteLine("Creating NonFictionWritingBeta service for non-fiction file");
                                        
                                        try
                                        {
                                            // CRITICAL: Determine the correct library path that will handle relative path references
                                            string libraryPath = DetermineAppropriateLibraryPath(filePath, content);
                                            Debug.WriteLine($"Using determined library path: {libraryPath}");
                                            
                                            // Create NonFictionWritingBeta service with the properly determined library path
                                            var nonFictionService = await NonFictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                                            SelectedTab.Service = nonFictionService;
                                            
                                            // Update the content immediately
                                            await SelectedTab.Service.UpdateContextAsync(content);
                                            
                                            Debug.WriteLine($"Successfully created NonFictionWritingBeta service");
                                        }
                                        catch (Exception ex)
                                        {
                                            Debug.WriteLine($"Error creating NonFictionWritingBeta service: {ex.Message}");
                                            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                                            // Create a general context service as fallback
                                            Debug.WriteLine("Falling back to GeneralChatService due to NonFictionWritingBeta creation failure");
                                            SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                                            await SelectedTab.Service.UpdateContextAsync(content);
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("Creating GeneralChatService for non-outline, non-fiction, non-rules, non-nonfiction markdown file");
                                        SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                                        await SelectedTab.Service.UpdateContextAsync(content);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // Create a general chat service for non-context mode or non-markdown tabs
                    Debug.WriteLine($"Creating GeneralChatService for {(isTabContextMode ? "context mode without markdown tab" : "non-context mode")}");
                    
                    // CRITICAL FIX: Create a NEW instance for each tab instead of using singleton
                    SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode);
                    
                    // If we have content from another context, update it
                    if (isTabContextMode)
                    {
                        var content = await GetCurrentContent();
                        if (!string.IsNullOrEmpty(content))
                        {
                            await SelectedTab.Service.UpdateContextAsync(content);
                        }
                    }
                }

                // The service is already stored in the tab
                Debug.WriteLine($"Service created and stored in tab: {SelectedTab.Name}");
                return SelectedTab.Service;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating service: {ex}");
                throw;
            }
        }

        // Add this helper method to determine the appropriate library path
        private string DetermineAppropriateLibraryPath(string filePath, string content)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return null;
            
                // Start with the file's directory
                string fileDir = Path.GetDirectoryName(filePath);
            
                // CRITICAL: Check if content contains references to parent directories
                if (!string.IsNullOrEmpty(content) && 
                   (content.Contains("ref rules: ../") || 
                    content.Contains("ref style: ../") || 
                    content.Contains("ref outline: ../") ||
                    // More generic check for any ../ reference
                    (content.Contains("ref") && content.Contains("../"))))
                {
                    Debug.WriteLine("Content contains references to parent directory, adjusting library path");
                    
                    // Get the parent directory to accommodate ../ references
                    string parentDir = Path.GetDirectoryName(fileDir);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        Debug.WriteLine($"Setting library path to parent directory: {parentDir}");
                        return parentDir;
                    }
                }
                
                // CRITICAL: Check for multiple levels of parent references (../../)
                if (!string.IsNullOrEmpty(content) && content.Contains("../../"))
                {
                    Debug.WriteLine("Content contains references to grandparent directory, adjusting library path");
                    
                    // Get the grandparent directory to accommodate ../../ references
                    string parentDir = Path.GetDirectoryName(fileDir);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        string grandparentDir = Path.GetDirectoryName(parentDir);
                        if (!string.IsNullOrEmpty(grandparentDir))
                        {
                            Debug.WriteLine($"Setting library path to grandparent directory: {grandparentDir}");
                            return grandparentDir;
                        }
                    }
                }
                
                // Default to the file directory if no parent references found
                Debug.WriteLine($"Using file directory as library path: {fileDir}");
                return fileDir;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error determining library path: {ex.Message}");
                // Fallback to file directory
                return Path.GetDirectoryName(filePath);
            }
        }

        // Helper to find markdown tab
        private IFileTab GetCurrentMarkdownTab()
        {
            try
            {
                // Try to use the _currentEditor first if it's a markdown tab
                if (_currentEditor is Views.MarkdownTabAvalon markdownTab)
                {
                    Debug.WriteLine($"Using _currentEditor as MarkdownTabAvalon: {markdownTab.FilePath}");
                    return markdownTab;
                }
                else if (_currentEditor is Views.MarkdownTabAvalon markdownTabAvalon)
                {
                    Debug.WriteLine($"Using _currentEditor as MarkdownTabAvalon: {markdownTabAvalon.FilePath}");
                    return markdownTabAvalon;
                }
                
                // Otherwise, try to get it from the main window's tab control
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                if (mainWindow != null)
                {
                    var selectedTabItem = mainWindow.MainTabControl?.SelectedItem as TabItem;
                    if (selectedTabItem?.Content is Views.MarkdownTabAvalon selectedMarkdownTab)
                    {
                        Debug.WriteLine($"Found MarkdownTabAvalon from MainTabControl: {selectedMarkdownTab.FilePath}");
                        return selectedMarkdownTab;
                    }
                    else if (selectedTabItem?.Content is Views.MarkdownTabAvalon selectedMarkdownTabAvalon)
                    {
                        Debug.WriteLine($"Found MarkdownTabAvalon from MainTabControl: {selectedMarkdownTabAvalon.FilePath}");
                        return selectedMarkdownTabAvalon;
                    }
                }
                
                // Look for any tab with the same filename as in the chat tab name
                if (SelectedTab != null && SelectedTab.Name.StartsWith("Chat - "))
                {
                    string chatFileName = SelectedTab.Name.Substring(7); // Remove "Chat - " prefix
                    
                    // Search through all tabs
                    if (mainWindow != null)
                    {
                        foreach (TabItem tabItem in mainWindow.MainTabControl.Items)
                        {
                            if (tabItem.Content is Views.MarkdownTabAvalon mdTab)
                            {
                                string tabFileName = Path.GetFileName(mdTab.FilePath);
                                if (tabFileName == chatFileName || SelectedTab.Name.Contains(tabFileName))
                                {
                                    Debug.WriteLine($"Found matching MarkdownTabAvalon by filename: {mdTab.FilePath}");
                                    return mdTab;
                                }
                            }
                            else if (tabItem.Content is Views.MarkdownTabAvalon mdTabAvalon)
                            {
                                string tabFileName = Path.GetFileName(mdTabAvalon.FilePath);
                                if (tabFileName == chatFileName || SelectedTab.Name.Contains(tabFileName))
                                {
                                    Debug.WriteLine($"Found matching MarkdownTabAvalon by filename: {mdTabAvalon.FilePath}");
                                    return mdTabAvalon;
                                }
                            }
                        }
                    }
                }
                
                Debug.WriteLine("No markdown tab found");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetCurrentMarkdownTab: {ex.Message}`");
                return null;
            }
        }

        private void MarkdownTab_CursorPositionChanged(object sender, int newPosition)
        {
                                // Cursor position tracking for context updates
            
            if (sender is Views.MarkdownTabAvalon markdownTab)
            {
                // Find the associated chat tab
                ChatTabViewModel associatedTab = null;
                if (_markdownTabAssociations.TryGetValue(markdownTab, out associatedTab))
                {
                    Debug.WriteLine($"Found associated chat tab: {associatedTab.Name}");
                }
                else
                {
                    // If no specific association exists, use the selected tab
                    associatedTab = SelectedTab;
                    Debug.WriteLine("No specific tab association found, using selected tab");
                    
                    // Create the association now to prevent future lookups
                    if (associatedTab != null)
                    {
                        _markdownTabAssociations[markdownTab] = associatedTab;
                        Debug.WriteLine($"Created new association between markdown tab and chat tab: {associatedTab.Name}");
                    }
                }
                
                // Ensure we never pass an invalid cursor position (0 is valid, but make sure it's explicit)
                if (newPosition < 0)
                {
                    newPosition = 0;
                    Debug.WriteLine("Corrected negative cursor position to 0");
                }
                
                // Store the cursor position in the tab's Tag for future reference
                if (associatedTab != null)
                {
                    // Get or create the tag dictionary
                    Dictionary<string, object> tagDict;
                    if (associatedTab.Tag is Dictionary<string, object> existingDict)
                    {
                        tagDict = existingDict;
                    }
                    else
                    {
                        tagDict = new Dictionary<string, object>();
                        
                        // If old Tag contained file path as string, preserve it
                        if (associatedTab.Tag is string filePath)
                        {
                            tagDict["FilePath"] = filePath;
                        }
                        // If old Tag was just an int cursor position, preserve file path from markdown tab
                        else if (associatedTab.Tag is int)
                        {
                            tagDict["FilePath"] = markdownTab.FilePath;
                        }
                    }
                    
                    // Update cursor position
                    tagDict["CursorPosition"] = newPosition;
                    associatedTab.Tag = tagDict;
                    
                    Debug.WriteLine($"Stored cursor position {newPosition} in tab tag dictionary");
                }
                
                // Save cursor position in any other tabs that might be related to the same file
                foreach (var pair in _markdownTabAssociations)
                {
                    if (pair.Key != markdownTab && 
                        pair.Key.FilePath == markdownTab.FilePath && 
                        pair.Value != associatedTab)
                    {
                        // Get or create the tag dictionary for the related tab
                        Dictionary<string, object> relatedTagDict;
                        if (pair.Value.Tag is Dictionary<string, object> existingDict)
                        {
                            relatedTagDict = existingDict;
                        }
                        else
                        {
                            relatedTagDict = new Dictionary<string, object>();
                            
                            // If old Tag contained file path as string, preserve it
                            if (pair.Value.Tag is string filePath)
                            {
                                relatedTagDict["FilePath"] = filePath;
                            }
                            else
                            {
                                relatedTagDict["FilePath"] = markdownTab.FilePath;
                            }
                        }
                        
                        // Update cursor position
                        relatedTagDict["CursorPosition"] = newPosition;
                        pair.Value.Tag = relatedTagDict;
                        
                        Debug.WriteLine($"Updated cursor position in related tab: {pair.Value.Name}");
                    }
                }
                
                // Immediately update the cursor position in the active service if it's fiction
            if (SelectedTab?.Service is FictionWritingBeta betaService)
            {
                    Debug.WriteLine($"Directly updating cursor position in active FictionWritingBeta service: {newPosition}");
                betaService.UpdateCursorPosition(newPosition);
                    
                    // Mark context as requiring refresh when cursor moves significantly (more than 50 characters)
                    if (associatedTab != null)
                    {
                        // Static threshold for now - could be made proportional to document size
                        const int cursorMovementThreshold = 50; 
                        
                        // Get the last known position if available
                        int lastPosition = -1;
                        
                        // Try to get from Tag dictionary
                        if (associatedTab.Tag is Dictionary<string, object> tagDict &&
                            tagDict.TryGetValue("CursorPosition", out object posValue) &&
                            posValue is int lastPos && 
                            lastPos != newPosition) // Skip if we just updated to this position
                        {
                            lastPosition = lastPos;
                        }
                        // Legacy tag handling (simple int)
                        else if (associatedTab.Tag is int legacyPos && legacyPos != newPosition)
                        {
                            lastPosition = legacyPos;
                        }
                        
                        // If cursor moved significantly, mark for refresh
                        if (lastPosition != -1 && Math.Abs(newPosition - lastPosition) > cursorMovementThreshold)
                        {
                            // Cursor moved significantly, mark for context refresh
                            associatedTab.ContextRequiresRefresh = true;
                        }
                    }
                }
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText) || SelectedTab == null)
                return;

            try
            {
                // Ensure any previous generation is fully stopped
                if (_cancellationTokenSource != null)
                {
                    Debug.WriteLine("Cleaning up previous generation...");
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                // Store the current tab that initiated the message
                var originatingTab = SelectedTab;
                
                // Save the input text to the current tab
                originatingTab.InputText = InputText;
                
                var userInput = InputText;
                InputText = string.Empty;
                _lastUserMessage = userInput;

                try
                {
                    var apiKey = GetApiKey(originatingTab.SelectedModel?.Provider ?? AIProvider.Anthropic);
                    if (string.IsNullOrEmpty(apiKey) && originatingTab.SelectedModel?.Provider != AIProvider.Ollama)
                    {
                        MessageBox.Show($"Please configure your {originatingTab.SelectedModel?.Provider} API key in settings.", "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Information);
                        InputText = userInput;  // Restore the input text
                        return;
                    }

                    // Check if SelectedModel is null and handle it
                    if (originatingTab.SelectedModel == null)
                    {
                        Debug.WriteLine("SelectedModel is null for the originating tab");
                        
                        // Try to set a default model if available
                        if (AvailableModels.Count > 0)
                        {
                            Debug.WriteLine("Setting default model from available models");
                            originatingTab.SelectedModel = AvailableModels[0];
                        }
                        else
                        {
                            // No models available, show error and return
                            MessageBox.Show("No AI models available. Please check your connection and restart the application.", 
                                "No Models Available", MessageBoxButton.OK, MessageBoxImage.Error);
                            InputText = userInput;  // Restore the input text
                            return;
                        }
                    }

                    // Add user message after API key check to the originating tab
                    var userMessage = new Models.ChatMessage("user", userInput)
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider
                    };
                    // Add to the correct message collection based on context mode
                    if (originatingTab.IsContextMode)
                    {
                        originatingTab.Messages.Add(userMessage);
                    }
                    else
                    {
                        originatingTab.ChatModeMessages.Add(userMessage);
                    }

                    // Create response message that will be updated with streaming content
                    var responseMessage = new Models.ChatMessage("assistant", "")
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider,
                        IsThinking = true
                    };
                    
                    // Add to the correct message collection based on context mode
                    if (originatingTab.IsContextMode)
                    {
                        originatingTab.Messages.Add(responseMessage);
                    }
                    else
                    {
                        originatingTab.ChatModeMessages.Add(responseMessage);
                    }
                    
                    // If this tab is the currently selected one, update the Messages property
                    if (originatingTab == SelectedTab)
                    {
                        Messages = originatingTab.IsContextMode ? 
                            originatingTab.Messages : 
                            originatingTab.ChatModeMessages;
                    }

                    // Create a new cancellation token source for this request
                    _cancellationTokenSource = new CancellationTokenSource();

                    string content = null;
                    string response = null;

                    if (originatingTab.IsContextMode)
                    {
                        content = await GetCurrentContent();
                        Debug.WriteLine($"Current content retrieved: {(content != null ? $"Length: {content.Length}" : "null")}");
                        
                        // Check if it's fiction
                        bool isFiction = IsFictionFile(content, _currentEditor?.FilePath);
                        Debug.WriteLine($"IsFictionFile check in SendMessageAsync: {isFiction}, file: {_currentEditor?.FilePath ?? "unknown"}");
                        
                        if (_currentEditor != null)
                        {
                            Debug.WriteLine($"Current editor file path: {_currentEditor.FilePath}");
                        }
                    }

                    try
                    {
                        // IMPORTANT: Only get a new service if needed, don't dispose the existing one here
                        // This can lead to ObjectDisposedException
                        BaseLangChainService service;
                        if (originatingTab.Service != null)
                        {
                            try
                            {
                                // Try to use the existing service
                                await originatingTab.Service.UpdateContextAsync(string.Empty);
                                service = originatingTab.Service;
                                Debug.WriteLine("Using existing service for the tab");
                            }
                            catch (ObjectDisposedException)
                            {
                                // If it's disposed, create a new one
                                Debug.WriteLine("Tab's service was disposed, creating new service");
                                service = await GetOrCreateService();
                                originatingTab.Service = service;
                            }
                        }
                        else
                        {
                            // No existing service, create a new one
                            Debug.WriteLine("No service exists for tab, creating new one");
                            service = await GetOrCreateService();
                            originatingTab.Service = service;
                        }
                        
                        if (service == null)
                        {
                            var activeMessages = originatingTab.IsContextMode ? 
                                originatingTab.Messages : 
                                originatingTab.ChatModeMessages;
                                
                            activeMessages.Remove(responseMessage);
                            activeMessages.Add(new Models.ChatMessage("system", "Failed to create language service. Please check your settings and try again.", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true
                            });
                            
                            // If this tab is the currently selected one, update the Messages property
                            if (originatingTab == SelectedTab)
                            {
                                Messages = activeMessages;
                            }
                            return;
                        }
                        Debug.WriteLine($"Service created, processing request... Type: {service.GetType().Name}");
                        
                        // Create a local handler for the streaming updates
                        Action<string> contentUpdateHandler = (updatedContent) => {
                            Application.Current.Dispatcher.InvokeAsync(() => {
                                // Update the message content as it streams in
                                responseMessage.Content = updatedContent;
                                // Auto scroll to new content
                                ScrollToBottom();
                            });
                        };
                        
                        // Subscribe to content updates from streaming
                        service.OnContentUpdated += contentUpdateHandler;
                        
                        // Pass the cancellation token to the service
                        response = await Task.Run(async () => {
                            try {
                                // If we have a FictionWritingBeta service, make sure to directly set _lastUserMessage
                                // in addition to passing it as the request parameter to ensure triggers work
                                if (service is FictionWritingBeta fictionBeta)
                                {
                                    Debug.WriteLine($"Using FictionWritingBeta service with trigger detection. User message: '{userInput}'");
                                    // Make sure the user message is passed as the request parameter
                                    return await service.ProcessRequest(content, userInput, _cancellationTokenSource.Token);
                                }
                                else
                                {
                                    return await service.ProcessRequest(content, userInput, _cancellationTokenSource.Token);
                                }
                            }
                            catch (OperationCanceledException) {
                                return "Request cancelled by user.";
                            }
                        });
                        
                        // Unsubscribe from content updates
                        service.OnContentUpdated -= contentUpdateHandler;
                        
                        // IMPORTANT: Don't dispose the service here, it will be reused
                    }
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                        {
                            Debug.WriteLine("Request was cancelled by user");
                            var cancelMessages = originatingTab.IsContextMode ? 
                                originatingTab.Messages : 
                                originatingTab.ChatModeMessages;
                                
                            cancelMessages.Remove(responseMessage);
                            cancelMessages.Add(new Models.ChatMessage("assistant", "Request cancelled by user.")
                            {
                                ModelName = originatingTab.SelectedModel.Name,
                                Provider = originatingTab.SelectedModel.Provider
                            });
                            
                            // If this tab is the currently selected one, update the Messages property
                            if (originatingTab == SelectedTab)
                            {
                                Messages = cancelMessages;
                            }
                            return;
                        }
                        
                        Debug.WriteLine($"Error processing request: {ex}");
                        var errorMessages = originatingTab.IsContextMode ? 
                            originatingTab.Messages : 
                            originatingTab.ChatModeMessages;
                            
                        errorMessages.Remove(responseMessage);
                        
                        // Handle premature ending specifically
                        if (ex is HttpRequestException httpEx && 
                            (httpEx.Message.Contains("prematurely") || httpEx.Message.Contains("interrupted")))
                        {
                            errorMessages.Add(new Models.ChatMessage("system", 
                                "The response was interrupted. This could be due to network issues or server timeouts. " +
                                "You can try sending your message again.", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true,
                                CanRetry = true
                            });
                        }
                        else
                        {
                            errorMessages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true
                            });
                        }
                        
                        // If this tab is the currently selected one, update the Messages property
                        if (originatingTab == SelectedTab)
                        {
                            Messages = errorMessages;
                        }
                        return;
                    }

                    // Final update with completed response
                    responseMessage.IsThinking = false;
                    responseMessage.Content = response;
                    
                    // Force scroll to bottom one last time
                    ScrollToBottom();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in SendMessageAsync: {ex}");
                    var activeMessages = originatingTab.IsContextMode ? 
                        originatingTab.Messages : 
                        originatingTab.ChatModeMessages;
                        
                    activeMessages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                    {
                        LastUserMessage = userInput,
                        IsError = true
                    });
                    
                    // If this tab is the currently selected one, update the Messages property
                    if (originatingTab == SelectedTab)
                    {
                        Messages = activeMessages;
                    }
                }
                finally
                {
                    // Dispose the cancellation token source
                    _cancellationTokenSource?.Dispose();
                    _cancellationTokenSource = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SendMessageAsync: {ex}");
                Messages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                {
                    LastUserMessage = InputText,
                    IsError = true
                });
            }
        }
        
        private void ScrollToBottom()
        {
            if (ChatScrollViewer != null)
            {
                ChatScrollViewer.ScrollToEnd();
            }
        }
        
        /// <summary>
        /// Restores the scroll position for the current tab and mode
        /// </summary>
        private void RestoreScrollPosition()
        {
            if (ChatScrollViewer != null && SelectedTab != null)
            {
                var targetPosition = SelectedTab.CurrentScrollPosition;
                System.Diagnostics.Debug.WriteLine($"Restoring scroll position for tab '{SelectedTab.Name}' mode '{(SelectedTab.IsContextMode ? "Context" : "Chat")}': {targetPosition}");
                
                if (targetPosition > 0)
                {
                    // Multiple attempts with increasing delays to handle window initialization
                    RestoreScrollPositionWithRetry(targetPosition, 0);
                }
            }
        }
        
        /// <summary>
        /// Attempts to restore scroll position with retry logic for better timing
        /// </summary>
        private void RestoreScrollPositionWithRetry(double targetPosition, int attemptCount)
        {
            if (ChatScrollViewer == null || SelectedTab == null || attemptCount >= 5)
            {
                return;
            }
            
            try
            {
                // Check if ScrollViewer has content and is ready
                if (ChatScrollViewer.ScrollableHeight > 0 && ChatScrollViewer.IsLoaded)
                {
                    ChatScrollViewer.ScrollToVerticalOffset(targetPosition);
                    System.Diagnostics.Debug.WriteLine($"Successfully restored scroll position on attempt {attemptCount + 1}: {targetPosition}");
                    
                    // Verify the scroll position was actually set
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        var actualPosition = ChatScrollViewer.VerticalOffset;
                        if (Math.Abs(actualPosition - targetPosition) > 10) // Allow some tolerance
                        {
                            System.Diagnostics.Debug.WriteLine($"Scroll position verification failed. Expected: {targetPosition}, Actual: {actualPosition}. Retrying...");
                            // Try once more after a longer delay
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                            {
                                ChatScrollViewer.ScrollToVerticalOffset(targetPosition);
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
                else
                {
                    // Not ready yet, schedule another attempt
                    var delay = TimeSpan.FromMilliseconds(100 * (attemptCount + 1)); // Increasing delay
                    System.Diagnostics.Debug.WriteLine($"ScrollViewer not ready for restoration (attempt {attemptCount + 1}). ScrollableHeight: {ChatScrollViewer.ScrollableHeight}, IsLoaded: {ChatScrollViewer.IsLoaded}. Retrying in {delay.TotalMilliseconds}ms");
                    
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = delay
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        RestoreScrollPositionWithRetry(targetPosition, attemptCount + 1);
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in RestoreScrollPositionWithRetry (attempt {attemptCount + 1}): {ex.Message}");
            }
        }
        
        /// <summary>
        /// Saves the current scroll position for the active tab
        /// </summary>
        private void SaveCurrentScrollPosition()
        {
            if (ChatScrollViewer != null && SelectedTab != null)
            {
                SelectedTab.CurrentScrollPosition = ChatScrollViewer.VerticalOffset;
            }
        }
        
        /// <summary>
        /// Handles window state changes (maximize/minimize) to restore scroll position
        /// </summary>
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow state changed, scheduling scroll position restoration");
            // Schedule restoration after window state change completes
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                RestoreScrollPosition();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        
        /// <summary>
        /// Handles window size changes to restore scroll position
        /// </summary>
        private void MainWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("MainWindow size changed, scheduling scroll position restoration");
            // Schedule restoration after size change completes
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                RestoreScrollPosition();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
        
        private async Task RetryMessageAsync(Models.ChatMessage errorMessage)
        {
            if (errorMessage == null || string.IsNullOrEmpty(errorMessage.LastUserMessage))
            {
                Debug.WriteLine("Cannot retry: No message to retry or LastUserMessage is empty");
                return;
            }

            // Store the last user message
            string userInput = errorMessage.LastUserMessage;
            Debug.WriteLine($"Retrying message: {userInput}");

            // Remove the error message
            Messages.Remove(errorMessage);

            // Set the input text to the last user message
            InputText = userInput;

            // Send the message again
            await SendMessageAsync();
        }

        private string GetApiKey(AIProvider provider)
        {
            switch (provider)
            {
                case AIProvider.OpenAI:
                    return _config.OpenAIApiKey;
                case AIProvider.Anthropic:
                    return _config.AnthropicApiKey;
                case AIProvider.XAI:
                    return _config.XAIApiKey;
                case AIProvider.Ollama:
                    return null; // Ollama doesn't require an API key
                case AIProvider.OpenRouter:
                    return _config.OpenRouterApiKey;
                default:
                    throw new ArgumentException($"Unsupported provider: {provider}");
            }
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    var textBox = sender as TextBox;
                    if (textBox != null)
                    {
                        int caretIndex = textBox.CaretIndex;
                        textBox.SelectedText = Environment.NewLine;
                        textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                        e.Handled = true;
                    }
                }
                else if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    e.Handled = true;
                    SendCommand?.Execute(null);
                }
            }
        }

        private async Task<string> GetCurrentContent()
        {
            Debug.WriteLine("Getting current content");
            
            // Get the active window's main tab control
            var mainWindow = Application.Current.MainWindow as Views.MainWindow;
            if (mainWindow == null)
            {
                Debug.WriteLine("Main window not found");
                return string.Empty;
            }
            
            // Get the selected tab from the main tab control
            var selectedTab = mainWindow.MainTabControl?.SelectedItem as System.Windows.Controls.TabItem;
            
            // Check for both MarkdownTab and MarkdownTabAvalon
            IFileTab markdownTab = null;
            string editorContent = string.Empty;
            int cursorPosition = 0;

            if (selectedTab?.Content is Views.MarkdownTabAvalon oldMarkdownTab)
            {
                markdownTab = oldMarkdownTab;
                editorContent = oldMarkdownTab.MarkdownDocument?.Text ?? string.Empty;
                cursorPosition = markdownTab.LastKnownCursorPosition;
                Debug.WriteLine($"Found MarkdownTabAvalon: {markdownTab.FilePath}");
            }
            else if (selectedTab?.Content is Views.MarkdownTabAvalon avalonMarkdownTab)
            {
                markdownTab = avalonMarkdownTab;
                editorContent = avalonMarkdownTab.MarkdownDocument?.Text ?? string.Empty;
                cursorPosition = markdownTab.LastKnownCursorPosition;
                Debug.WriteLine($"Found MarkdownTabAvalon: {markdownTab.FilePath}");
            }

            if (markdownTab != null)
            {
                // Auto-associate this markdown tab with the current chat tab if in context mode and no association exists
                if (SelectedTab != null && SelectedTab.IsContextMode && SelectedTab.AssociatedEditor == null)
                {
                    Debug.WriteLine($"Auto-associating markdown tab with chat tab: {SelectedTab.Name}");
                    SelectedTab.AssociatedEditor = markdownTab;
                    
                    // Update the tab name to include the file name if it doesn't already
                    string fileName = System.IO.Path.GetFileName(markdownTab.FilePath);
                    if (!SelectedTab.Name.Contains(fileName))
                    {
                        // Only update if original name is generic or doesn't include the file name
                        if (SelectedTab.Name.StartsWith("Chat ") || SelectedTab.Name.StartsWith("New Chat") || SelectedTab.Name == "General Chat")
                        {
                            SelectedTab.Name = $"Chat - {fileName}";
                        }
                    }
                }
                
                // Store filepath in a separate property instead of inserting it into the content
                if (SelectedTab?.Service is FictionWritingBeta fictionService)
                {
                    fictionService.SetCurrentFilePath(markdownTab.FilePath);
                    
                    // Make sure to update the cursor position in the service
                    // Update service with current cursor position
                    fictionService.UpdateCursorPosition(cursorPosition);
                }
                
                // Check if we have a file path and if the file exists
                if (!string.IsNullOrEmpty(markdownTab.FilePath) && System.IO.File.Exists(markdownTab.FilePath))
                {
                    // Check if there are unsaved changes
                    if (markdownTab.IsModified)
                    {
                        try
                        {
                            Debug.WriteLine("Detected unsaved changes, using hybrid approach to preserve both frontmatter and edits");
                            
                            // Read the file from disk to get the frontmatter
                            string fileContent = await System.IO.File.ReadAllTextAsync(markdownTab.FilePath);
                        
                            // Extract frontmatter if it exists
                            if (fileContent.StartsWith("---\n") || fileContent.StartsWith("---\r\n"))
                            {
                                // Find the closing delimiter
                                int endIndex = fileContent.IndexOf("\n---", 4);
                                if (endIndex > 0)
                        {
                                    // Extract frontmatter including delimiters
                                    string frontmatter = fileContent.Substring(0, endIndex + 4); // +4 to include the closing delimiter
                                    
                                    // If there's a newline after the closing delimiter, include it too
                                    if (endIndex + 4 < fileContent.Length && fileContent[endIndex + 4] == '\n')
                                    {
                                        frontmatter += "\n";
                                    }
                                    
                                    Debug.WriteLine("Found frontmatter in file, combining with current editor content");
                                    
                                    // Return combined content: frontmatter + current editor content
                                    return frontmatter + editorContent;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error handling unsaved changes: {ex.Message}");
                        }
                    }
                    else
                    {
                        try
                        {
                            // No unsaved changes, read directly from file
                            Debug.WriteLine($"No unsaved changes, reading content directly from file: {markdownTab.FilePath}");
                            return await System.IO.File.ReadAllTextAsync(markdownTab.FilePath);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error reading file directly: {ex.Message}");
                        }
                        }
                    }
                    
                // Fall back to editor content if reading file fails or if no file path
                if (!string.IsNullOrEmpty(editorContent))
                {
                    Debug.WriteLine($"Using editor content: {editorContent.Substring(0, Math.Min(20, editorContent.Length))}...");
                    return editorContent;
                }
                
                Debug.WriteLine("Warning: MarkdownTab content could not be retrieved");
                return string.Empty;
            }
            else if (selectedTab?.Content is EditorTab editorTab)
            {
                var content = editorTab.GetContent();
                if (!string.IsNullOrEmpty(content))
                {
                    Debug.WriteLine($"Retrieved content from EditorTab: {editorTab.FilePath}");
                    
                    // Auto-associate this editor with the current chat tab if in context mode and no association exists
                    if (SelectedTab != null && SelectedTab.IsContextMode && SelectedTab.AssociatedEditor == null)
                    {
                        Debug.WriteLine($"Auto-associating editor tab with chat tab: {SelectedTab.Name}");
                        SelectedTab.AssociatedEditor = editorTab;
                        
                        // Update the tab name to include the file name if it doesn't already
                        string fileName = System.IO.Path.GetFileName(editorTab.FilePath);
                        if (!SelectedTab.Name.Contains(fileName))
                        {
                            // Only update if original name is generic or doesn't include the file name
                            if (SelectedTab.Name.StartsWith("Chat ") || SelectedTab.Name.StartsWith("New Chat") || SelectedTab.Name == "General Chat")
                            {
                                SelectedTab.Name = $"Chat - {fileName}";
                            }
                        }
                    }
                    
                    // Store filepath in a separate property instead of inserting it into the content
                    if (SelectedTab?.Service is FictionWritingBeta fictionService)
                    {
                        fictionService.SetCurrentFilePath(editorTab.FilePath);
                    }
                    
                    // Return the content without modification
                    return content;
                }
                Debug.WriteLine("Warning: EditorTab content is empty");
                return string.Empty;
            }
            else if (selectedTab?.Content is ProjectTab projectTab)
            {
                // Return empty string since ProjectChain will handle gathering the full context
                Debug.WriteLine($"ProjectTab detected: {projectTab.FilePath} - Context will be handled by ProjectChain");
                return string.Empty;
            }
            else if (_currentMusicTab != null)
            {
                var selectedItems = _currentMusicTab.ContentListView_Control?.SelectedItems;
                if (selectedItems != null && selectedItems.Count > 0)
                {
                    var selectedTracks = selectedItems.Cast<MusicItem>()
                        .Select(item => $"{item.Name} by {item.ArtistName} from {item.Album}")
                        .ToList();
                    return $"Selected music tracks: {string.Join(", ", selectedTracks)}";
                }
            }
            
            Debug.WriteLine("No supported tab type found");
            return string.Empty;
        }

        private async Task VerifyDeviceAsync()
        {
            try
            {
                var client = MatrixClient.Instance;
                if (!client.IsConnected)
                {
                    MessageBox.Show("Please connect to Matrix first.", "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var devices = await client.GetDevices();
                if (devices == null || !devices.Any())
                {
                    MessageBox.Show("No devices found to verify.", "No Devices", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Show device selection dialog
                var deviceSelectionWindow = new Views.DeviceSelectionWindow(devices);
                deviceSelectionWindow.Owner = Application.Current.MainWindow;
                if (deviceSelectionWindow.ShowDialog() == true)
                {
                    var selectedDevice = deviceSelectionWindow.SelectedDevice;
                    await client.StartVerificationWithDevice(selectedDevice.DeviceId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during device verification: {ex.Message}");
                MessageBox.Show($"Failed to verify device: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task UpdateContext(string context)
        {
            try
            {
                if (SelectedTab?.Service != null)
                {
                    await SelectedTab.Service.UpdateContextAsync(context);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error updating context");
            }
        }

        private IEnumerable<ProjectTask> GetAllSubtasks(ProjectTask task)
        {
            if (task.Subtasks == null || !task.Subtasks.Any())
                return Enumerable.Empty<ProjectTask>();

            return task.Subtasks.Concat(task.Subtasks.SelectMany(GetAllSubtasks));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private AIProvider GetProviderFromModelId(string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                return AIProvider.None;

            if (modelId.StartsWith("gpt-") || modelId.StartsWith("text-davinci-"))
                return AIProvider.OpenAI;
            if (modelId.StartsWith("claude-"))
                return AIProvider.Anthropic;
            if (modelId.StartsWith("grok-"))
                return AIProvider.XAI;
            if (modelId.StartsWith("openrouter/"))
                return AIProvider.OpenRouter;
        
            // Assume Ollama for any other model ID
            return AIProvider.Ollama;
        }

        private void StopThinking(Models.ChatMessage thinkingMessage)
        {
            try
            {
                if (_cancellationTokenSource != null)
                {
                    Debug.WriteLine("Cancelling current generation...");
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource.Dispose();
                    _cancellationTokenSource = null;
                }

                // Remove the thinking message
                if (thinkingMessage != null)
                {
                    var activeMessages = SelectedTab?.IsContextMode ?? true ? 
                        SelectedTab?.Messages : 
                        SelectedTab?.ChatModeMessages;
                        
                    if (activeMessages != null && activeMessages.Contains(thinkingMessage))
                    {
                        activeMessages.Remove(thinkingMessage);
                    }
                }

                // Dispose of the current service to ensure a clean state
                if (SelectedTab?.Service != null)
                {
                    Debug.WriteLine("Disposing service after cancellation");
                    SelectedTab.Service.Dispose();
                    SelectedTab.Service = null;
                }

                // Mark context as requiring refresh
                if (SelectedTab != null)
                {
                    SelectedTab.ContextRequiresRefresh = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in StopThinking: {ex.Message}");
            }
        }

        private void OnInputTextChanged()
        {
            // Debounce the input text changes
            if (string.IsNullOrWhiteSpace(InputText))
            {
                return;
            }

            // Implement debouncing logic here if needed
        }

        // Add a new tab
        private void AddNewTab(string name = null)
        {
            var newTab = new ChatTabViewModel(name ?? $"Chat {Tabs.Count + 1}");
            // Set initial model based on current selection or default
            newTab.SelectedModel = _selectedModel;
            Tabs.Add(newTab);
            SelectedTab = newTab;
        }
        
        // Close a tab
        private void CloseTab(ChatTabViewModel tab)
        {
            if (tab == null || Tabs.Count <= 1) return;
            
            int index = Tabs.IndexOf(tab);
            Tabs.Remove(tab);
            
            // Select another tab if we closed the selected one
            if (tab == SelectedTab)
            {
                // Select the tab to the left or the first tab
                SelectedTab = index > 0 ? Tabs[index - 1] : Tabs[0];
            }
        }

        private bool LoadSavedTabs()
        {
            try
            {
                var sessionData = _chatHistoryService.LoadChatHistory();
                if (sessionData == null || sessionData.Tabs == null || sessionData.Tabs.Count == 0)
                {
                    return false;
                }

                // Process each saved tab
                foreach (var tabData in sessionData.Tabs)
                {
                    var tab = new ChatTabViewModel(tabData.Name);
                    
                    // Set tab properties
                    tab.IsContextMode = tabData.IsContextMode;
                    tab.InputText = tabData.InputText ?? string.Empty;
                    
                    // Restore scroll positions
                    tab.ContextModeScrollPosition = tabData.ContextModeScrollPosition;
                    tab.ChatModeScrollPosition = tabData.ChatModeScrollPosition;
                    
                    // Add messages
                    if (tabData.Messages != null)
                    {
                        foreach (var msg in tabData.Messages)
                        {
                            tab.Messages.Add(msg);
                        }
                    }
                    
                    if (tabData.ChatModeMessages != null)
                    {
                        foreach (var msg in tabData.ChatModeMessages)
                        {
                            tab.ChatModeMessages.Add(msg);
                        }
                    }
                    
                    Tabs.Add(tab);
                }
                
                // Select the previously selected tab or the first one
                if (Tabs.Count > 0)
                {
                    int index = Math.Min(sessionData.SelectedTabIndex, Tabs.Count - 1);
                    SelectedTab = Tabs[index];
                    
                    // Restore model selection when models are loaded
                    // We'll do this in OnModelsChanged
                    foreach (var tab in Tabs)
                    {
                        tab.Tag = new
                        {
                            ModelName = sessionData.Tabs[Tabs.IndexOf(tab)].ModelName,
                            ModelProvider = sessionData.Tabs[Tabs.IndexOf(tab)].ModelProvider
                        };
                    }
                    
                    // Schedule scroll position restoration after UI is fully loaded
                    // Use a longer delay to account for window state changes during startup
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500) // Give window time to finish initializing
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        System.Diagnostics.Debug.WriteLine("Starting delayed scroll position restoration for app startup");
                        RestoreScrollPosition();
                    };
                    timer.Start();
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading chat tabs: {ex.Message}");
            }
            
            return false;
        }
        
        private void SaveTabs()
        {
            try
            {
                // Save current scroll position before saving
                SaveCurrentScrollPosition();
                
                int selectedIndex = SelectedTab != null ? Tabs.IndexOf(SelectedTab) : 0;
                _chatHistoryService.SaveChatHistory(Tabs, selectedIndex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving chat tabs: {ex.Message}");
            }
        }
        
        public void SaveState()
        {
            SaveTabs();
        }
        
        public void ClearHistory()
        {
            if (SelectedTab == null) return;
            
            var result = MessageBox.Show(
                "Are you sure you want to clear all messages in this tab?", 
                "Clear Chat History", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                // Clear both message collections for the current tab
                SelectedTab.Messages.Clear();
                SelectedTab.ChatModeMessages.Clear();
                
                // Dispose of the current service to start fresh
                DisposeCurrentService();
                
                // Mark context as requiring refresh
                SelectedTab.ContextRequiresRefresh = true;
                
                // Reset any tags that were used for cursor position
                if (SelectedTab.Tag is int)
                {
                    SelectedTab.Tag = null;
                }
                
                // Update the active messages collection
                OnPropertyChanged(nameof(Messages));
                
                // Save the updated state
                SaveTabs();
            }
        }

        // Add this new method to handle editor tab closing
        public void HandleEditorTabClosed(object editorTabObj)
        {
            // If an editor tab is closed, we need to clean up any associations
            if (editorTabObj == null) return;

            string closedFilePath = null;
            
            if (editorTabObj is EditorTab editor)
            {
                closedFilePath = editor.FilePath;
            }
            else if (editorTabObj is Views.MarkdownTabAvalon closedMdTab)
            {
                closedFilePath = closedMdTab.FilePath;
            }
            else if (editorTabObj is TabItem tabItem && tabItem.Tag is string path)
            {
                closedFilePath = path;
            }
            
            if (string.IsNullOrEmpty(closedFilePath)) return;
            
            // Check all tabs for associations with this file
            foreach (var tab in Tabs)
            {
                if (tab.AssociatedEditor != null && tab.AssociatedFilePath == closedFilePath)
                {
                    Debug.WriteLine($"Clearing association for tab {tab.Name} because editor was closed");
                    tab.AssociatedEditor = null;
                    
                    // Optional: Update tab name to remove file reference
                    if (tab.Name.Contains(System.IO.Path.GetFileName(closedFilePath)))
                    {
                        tab.Name = "Chat";
                    }
                }
                else if (tab.Tag is string tagPath && tagPath == closedFilePath)
                {
                    Debug.WriteLine($"Clearing tag association for tab {tab.Name} because editor was closed");
                    tab.Tag = null;
                    
                    // Optional: Update tab name to remove file reference
                    if (tab.Name.Contains(System.IO.Path.GetFileName(closedFilePath)))
                    {
                        tab.Name = "Chat";
                    }
                }
            }
        }

        private bool IsOutlineFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is outline: {filePath ?? "unknown"}");
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Simplified frontmatter check - just look for key indicators directly
            if (content.StartsWith("---"))
            {
                // Find the closing delimiter
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract raw frontmatter as a string
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    Debug.WriteLine($"Examining frontmatter: Length={frontmatter.Length}");
                    
                    // Direct string checks for outline indicators
                    if (frontmatter.Contains("type: outline") || 
                        frontmatter.Contains("type:outline"))
                    {
                        Debug.WriteLine("Outline file detected based on frontmatter indicators");
                        return true;
                    }
                    
                    // Print the actual content of the frontmatter for debugging
                    Debug.WriteLine($"Frontmatter content (first 100 chars): {frontmatter.Substring(0, Math.Min(100, frontmatter.Length))}...");
                }
            }
            
            // Check for outline by path patterns
            if (!string.IsNullOrEmpty(filePath)) 
            {
                var lowerPath = filePath.ToLowerInvariant();
                
                // Check common outline-related folder patterns
                if (lowerPath.Contains("\\outlines\\") || 
                    lowerPath.Contains("\\outline\\") ||
                    lowerPath.Contains("\\plot\\") ||
                    lowerPath.Contains("\\plots\\"))
                {
                    Debug.WriteLine($"Outline file detected based on directory pattern: {filePath}");
                    return true;
                }
            }
            
            // Additional content-based detection (for files without frontmatter)
            // Check if the content appears to be an outline by looking for typical outline patterns
            if (content.Contains("# Overall Synopsis") || 
                content.Contains("# Synopsis") || 
                content.Contains("# Characters") ||
                content.Contains("# Outline") ||
                (content.Contains("## Chapter ") && content.Contains("# Characters")))
            {
                Debug.WriteLine("Outline file detected based on content patterns");
                return true;
            }
            
            Debug.WriteLine("Not detected as an outline file");
            return false;
        }

        private bool IsFictionFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is fiction: {filePath ?? "unknown"}");
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Simplified frontmatter check - just look for key indicators directly
            if (content.StartsWith("---"))
            {
                // Find the closing delimiter
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract raw frontmatter as a string
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    Debug.WriteLine($"Examining frontmatter: Length={frontmatter.Length}");
                    
                    // CRITICAL FIX: Check for explicit rules file FIRST to avoid false fiction detection
                    if (frontmatter.Contains("type: rules") || frontmatter.Contains("type:rules"))
                    {
                        Debug.WriteLine("This is actually a rules file, not fiction - skipping fiction detection");
                        return false;
                    }
                    
                    // Direct string checks for fiction indicators
                    if (frontmatter.Contains("type: fiction") || 
                        frontmatter.Contains("type:fiction") ||
                        frontmatter.Contains("type: novel") || 
                        frontmatter.Contains("type:novel") || 
                        frontmatter.Contains("\nfiction:") || 
                        frontmatter.Contains("\nfiction\n") ||
                        frontmatter.Contains("ref rules:") ||
                        frontmatter.Contains("ref style:") ||
                        frontmatter.Contains("ref outline:"))
                    {
                        Debug.WriteLine("Fiction file detected based on frontmatter indicators");
                        return true;
                    }
                    
                    // Print the actual content of the frontmatter for debugging
                    Debug.WriteLine($"Frontmatter content (first 100 chars): {frontmatter.Substring(0, Math.Min(100, frontmatter.Length))}...");
                }
            }
            
            // Check for fiction by path patterns
            if (!string.IsNullOrEmpty(filePath)) 
            {
                var lowerPath = filePath.ToLowerInvariant();
                
                // Check common fiction-related folder patterns
                if (lowerPath.Contains("\\fiction\\") || 
                    lowerPath.Contains("\\novels\\") ||
                    lowerPath.Contains("\\manuscripts\\") ||
                    lowerPath.Contains("\\manuscript\\") ||
                    lowerPath.Contains("\\stories\\") ||
                    lowerPath.Contains("\\story\\"))
                {
                    Debug.WriteLine($"Fiction file detected based on directory pattern: {filePath}");
                    return true;
                }
            }
            
            // Additional content-based detection (for files without frontmatter)
            // Check if the content appears to be fiction by looking for chapter patterns
            if (content.Contains("# Chapter ") || 
                content.Contains("## Chapter ") || 
                Regex.IsMatch(content, @"Chapter \d+"))
            {
                Debug.WriteLine("Fiction file detected based on chapter patterns");
                            return true;
                        }
                        
            // Try a detailed frontmatter check with key-value parsing as fallback
            // This is more complex but catches edge cases
            if (content.StartsWith("---"))
            {
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract frontmatter content (excluding delimiters)
                    string frontmatterContent = content.Substring(4, endIndex - 4);
                    
                    // Parse frontmatter line by line
                    Dictionary<string, string> frontmatterDict = new Dictionary<string, string>();
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string line in lines)
                        {
                            // Look for key-value pairs (key: value)
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex > 0)
                            {
                            string key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                            string value = line.Substring(colonIndex + 1).Trim().ToLowerInvariant();
                            
                            frontmatterDict[key] = value;
                            Debug.WriteLine($"Parsed frontmatter: '{key}' = '{value}'");
                            
                            // CRITICAL FIX: Explicit check for rules files first
                            if (key == "type" && value == "rules")
                            {
                                Debug.WriteLine("This is actually a rules file, not fiction - skipping fiction detection");
                                return false;
                            }
                            
                            // Check keys/values for fiction indicators
                            if (key == "type" && (value == "fiction" || value == "novel" || value == "story"))
                            {
                                Debug.WriteLine("Fiction file detected based on 'type' key in frontmatter");
                                return true;
                            }
                            else if (key.Contains("ref") && (value.Contains("rule") || value.Contains("style") || value.Contains("outline")))
                            {
                                Debug.WriteLine("Fiction file detected based on references in frontmatter");
                                return true;
                            }
                        }
                    }
                    
                    // Check for author + references pattern
                    if (frontmatterDict.ContainsKey("author") &&
                        (frontmatterDict.Keys.Any(k => k.Contains("ref")) || 
                         frontmatterDict.ContainsKey("outline") ||
                         frontmatterDict.ContainsKey("rules") ||
                         frontmatterDict.ContainsKey("style")))
                    {
                        Debug.WriteLine("Fiction file detected based on author + references pattern");
                            return true;
                    }
                }
            }
            
            Debug.WriteLine("Not detected as a fiction file");
            return false;
        }

        private bool IsRulesFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is rules: {filePath ?? "unknown"}");
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Simplified frontmatter check - just look for key indicators directly
            if (content.StartsWith("---"))
            {
                // Find the closing delimiter
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract raw frontmatter as a string
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    Debug.WriteLine($"Examining frontmatter: Length={frontmatter.Length}");
                    
                    // Direct string checks for rules indicators
                    if (frontmatter.Contains("type: rules") || 
                        frontmatter.Contains("type:rules"))
                    {
                        Debug.WriteLine("Rules file detected based on frontmatter indicators");
                        return true;
                    }
                    
                    // Print the actual content of the frontmatter for debugging
                    Debug.WriteLine($"Frontmatter content (first 100 chars): {frontmatter.Substring(0, Math.Min(100, frontmatter.Length))}...");
                }
            }
            
            // Check for rules by path patterns
            if (!string.IsNullOrEmpty(filePath)) 
            {
                var lowerPath = filePath.ToLowerInvariant();
                
                // Check common rules-related file patterns
                if (lowerPath.Contains("rules.md") || 
                    lowerPath.Contains("rules.") ||
                    lowerPath.Contains("\\rules\\") ||
                    lowerPath.Contains("\\universe\\") ||
                    lowerPath.Contains("\\worldbuilding\\") ||
                    lowerPath.Contains("\\characters\\"))
                {
                    Debug.WriteLine($"Rules file detected based on file pattern: {filePath}");
                    return true;
                }
            }
            
            // Additional content-based detection (for files without frontmatter)
            // Check if the content appears to be rules by looking for typical patterns
            if (content.Contains("[Background]") || 
                content.Contains("[Series Synopsis]") || 
                content.Contains("[Character Profiles]") ||
                content.Contains("# Character Profiles") ||
                content.Contains("- Book 1 -") ||
                content.Contains("- Book 2 -") ||
                (content.Contains("Book ") && content.Contains("Character") && content.Contains("Synopsis")))
            {
                Debug.WriteLine("Rules file detected based on content patterns");
                return true;
            }
            
            Debug.WriteLine("Not detected as a rules file");
            return false;
        }

        private bool IsNonFictionFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is non-fiction: {filePath ?? "unknown"}");
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Simplified frontmatter check - just look for key indicators directly
            if (content.StartsWith("---"))
            {
                // Find the closing delimiter
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract raw frontmatter as a string
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    Debug.WriteLine($"Examining frontmatter: Length={frontmatter.Length}");
                    
                    // Direct string checks for non-fiction indicators
                    if (frontmatter.Contains("type: nonfiction") || 
                        frontmatter.Contains("type:nonfiction") ||
                        frontmatter.Contains("subtype: biography") ||
                        frontmatter.Contains("subtype: autobiography") ||
                        frontmatter.Contains("subtype: memoir") ||
                        frontmatter.Contains("subtype: history") ||
                        frontmatter.Contains("subtype: academic") ||
                        frontmatter.Contains("subtype: journalism") ||
                        frontmatter.Contains("subject:") ||
                        frontmatter.Contains("time_period:") ||
                        frontmatter.Contains("research:") ||
                        frontmatter.Contains("sources:") ||
                        frontmatter.Contains("timeline:"))
                    {
                        Debug.WriteLine("Non-fiction file detected based on frontmatter indicators");
                        return true;
                    }
                    
                    // Print the actual content of the frontmatter for debugging
                    Debug.WriteLine($"Frontmatter content (first 100 chars): {frontmatter.Substring(0, Math.Min(100, frontmatter.Length))}...");
                }
            }
            
            // Check for non-fiction by path patterns
            if (!string.IsNullOrEmpty(filePath)) 
            {
                var lowerPath = filePath.ToLowerInvariant();
                
                // Check common non-fiction related folder patterns
                if (lowerPath.Contains("\\biography\\") || 
                    lowerPath.Contains("\\biographies\\") ||
                    lowerPath.Contains("\\autobiography\\") ||
                    lowerPath.Contains("\\memoir\\") ||
                    lowerPath.Contains("\\memoirs\\") ||
                    lowerPath.Contains("\\history\\") ||
                    lowerPath.Contains("\\academic\\") ||
                    lowerPath.Contains("\\research\\") ||
                    lowerPath.Contains("\\nonfiction\\") ||
                    lowerPath.Contains("\\non-fiction\\"))
                {
                    Debug.WriteLine($"Non-fiction file detected based on directory pattern: {filePath}");
                    return true;
                }
            }
            
            // Additional content-based detection (for files without frontmatter)
            // Check if the content appears to be non-fiction by looking for typical patterns
            if (content.Contains("born in") || 
                content.Contains("died in") || 
                content.Contains("according to") ||
                content.Contains("research shows") ||
                content.Contains("studies indicate") ||
                content.Contains("historical records") ||
                content.Contains("documented") ||
                content.Contains("evidence suggests") ||
                content.Contains("bibliography") ||
                content.Contains("sources") ||
                content.Contains("timeline"))
            {
                Debug.WriteLine("Non-fiction file detected based on content patterns");
                return true;
            }
                        
            // Try a detailed frontmatter check with key-value parsing as fallback
            // This is more complex but catches edge cases
            if (content.StartsWith("---"))
            {
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    // Extract frontmatter content (excluding delimiters)
                    string frontmatterContent = content.Substring(4, endIndex - 4);
                    
                    // Parse frontmatter line by line
                    Dictionary<string, string> frontmatterDict = new Dictionary<string, string>();
                    string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                    foreach (string line in lines)
                    {
                        // Look for key-value pairs (key: value)
                        int colonIndex = line.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            string key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
                            string value = line.Substring(colonIndex + 1).Trim().ToLowerInvariant();
                            
                            frontmatterDict[key] = value;
                            Debug.WriteLine($"Parsed frontmatter: '{key}' = '{value}'");
                            
                            // Check keys/values for non-fiction indicators
                            if (key == "type" && value == "nonfiction")
                            {
                                Debug.WriteLine("Non-fiction file detected based on 'type' key in frontmatter");
                                return true;
                            }
                            else if (key == "subtype" && (value == "biography" || value == "autobiography" || 
                                   value == "memoir" || value == "history" || value == "academic" || value == "journalism"))
                            {
                                Debug.WriteLine("Non-fiction file detected based on 'subtype' key in frontmatter");
                                return true;
                            }
                            else if (key == "subject" || key == "time_period" || key == "research" || 
                                   key == "sources" || key == "timeline")
                            {
                                Debug.WriteLine("Non-fiction file detected based on non-fiction specific keys in frontmatter");
                                return true;
                            }
                        }
                    }
                }
            }
            
            Debug.WriteLine("Not detected as a non-fiction file");
            return false;
        }

        private void SubscribeToMarkdownTab(Views.MarkdownTabAvalon markdownTab)
        {
            Debug.WriteLine($"Subscribing to markdown tab cursor position changes: {markdownTab.FilePath}");
            markdownTab.CursorPositionChanged += MarkdownTab_CursorPositionChanged;
            
            // Associate this markdown tab with the currently selected chat tab
            if (SelectedTab != null)
            {
                _markdownTabAssociations[markdownTab] = SelectedTab;
                Debug.WriteLine($"Associated markdown tab with chat tab: {SelectedTab.Name}");
            }
        }

        private void UnsubscribeFromMarkdownTab(Views.MarkdownTabAvalon markdownTab)
        {
            Debug.WriteLine($"Unsubscribing from markdown tab cursor position changes: {markdownTab.FilePath}");
            markdownTab.CursorPositionChanged -= MarkdownTab_CursorPositionChanged;
            
            // Remove the association when unsubscribing
            if (_markdownTabAssociations.ContainsKey(markdownTab))
            {
                _markdownTabAssociations.Remove(markdownTab);
                Debug.WriteLine("Removed markdown tab association");
            }
        }

        private void UpdateMarkdownTabAssociationForCurrentTab()
        {
            // If we have a current markdown tab, associate it with the currently selected chat tab
            if (_currentEditor is Views.MarkdownTabAvalon currentMarkdownTab && SelectedTab != null)
            {
                Debug.WriteLine($"Updating association: Markdown tab {currentMarkdownTab.FilePath} -> Chat tab {SelectedTab.Name}");
                _markdownTabAssociations[currentMarkdownTab] = SelectedTab;
            }
        }

        // Add this method to help find the root writing directory
        private string FindRootWritingDirectory(string filePath)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                    return null;
            
                // Start with the file's directory
                string currentDir = System.IO.Path.GetDirectoryName(filePath);
            
                // Keep track of directories we've checked to avoid infinite loops
                HashSet<string> checkedDirs = new HashSet<string>();
            
                // Navigate up the directory tree looking for common writing root directories
                while (!string.IsNullOrEmpty(currentDir) && !checkedDirs.Contains(currentDir))
                {
                    checkedDirs.Add(currentDir);
            
                    // Check if this directory or its parent contains markers of a writing root
                    if (IsLikelyWritingRoot(currentDir))
                    {
                        Debug.WriteLine($"Found likely writing root directory: {currentDir}");
                        return currentDir;
                    }
            
                    // Check if this is as far as we should go (i.e., we've reached the user's document root)
                    if (IsDocumentRoot(currentDir))
                    {
                        Debug.WriteLine($"Stopping at document root: {currentDir}");
                        return currentDir;
                    }
            
                    // Move up to parent directory
                    currentDir = System.IO.Path.GetDirectoryName(currentDir);
                }
            
                // If no clear writing root was found, return null
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in FindRootWritingDirectory: {ex.Message}");
                return null;
            }
        }

        private bool IsLikelyWritingRoot(string directory)
        {
            try
            {
                // Check directory name for common writing project root names
                string dirName = System.IO.Path.GetFileName(directory).ToLowerInvariant();
            
                if (dirName == "writing" || 
                    dirName == "manuscripts" || 
                    dirName == "books" ||
                    dirName == "fiction" ||
                    dirName == "projects")
                {
                    return true;
                }
            
                // Check for common files that might indicate a writing project root
                string[] commonFiles = { "style.md", "rules.md", "outline.md", "notes.md", "characters.md" };
                foreach (var file in commonFiles)
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(directory, file)))
                    {
                        return true;
                    }
                }
            
                // Check for common subdirectories that might indicate a writing project root
                string[] commonSubdirs = { "chapters", "drafts", "outlines", "research", "characters", "notes" };
                foreach (var subdir in commonSubdirs)
                {
                    if (System.IO.Directory.Exists(System.IO.Path.Combine(directory, subdir)))
                    {
                        return true;
                    }
                }
            
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in IsLikelyWritingRoot: {ex.Message}");
                return false;
            }
        }

        private bool IsDocumentRoot(string directory)
        {
            try
            {
                // Check if this is a common document root directory
                string dirName = System.IO.Path.GetFileName(directory).ToLowerInvariant();
            
                // Common document root directories
                if (dirName == "documents" || 
                    dirName == "docs" ||
                    dirName == "my documents" ||
                    dirName == "onedrive" ||
                    dirName == "dropbox" ||
                    dirName == "google drive" ||
                    dirName == "icloud" ||
                    dirName == "nextcloud" ||
                    dirName == "sync" ||
                    dirName == "creative cloud files")
                {
                    return true;
                }
            
                // If this is the root of a drive, stop here
                if (System.IO.Path.GetPathRoot(directory) == directory)
                {
                    return true;
                }
            
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in IsDocumentRoot: {ex.Message}");
                return false;
            }
        }
    }
}



