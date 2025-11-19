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
        private DispatcherTimer _memoryMonitorTimer;

        // SPLENDID: Debounced streaming support to prevent UI jumping
        private DispatcherTimer _streamingDebounceTimer;
        private string _pendingStreamContent;
        private Models.ChatMessage _currentStreamingMessage;
        private ChatTabViewModel _currentStreamingTab;
        private readonly object _streamingLock = new object();

        private Dictionary<Views.MarkdownTabAvalon, ChatTabViewModel> _markdownTabAssociations = new Dictionary<Views.MarkdownTabAvalon, ChatTabViewModel>();
        private Views.MainWindow _mainWindow; // Store reference for proper cleanup

        public Action ScrollToBottomAction { get; set; }

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
                    // Save current input text to the previous tab before switching
                    if (_selectedTab != null)
                    {
                        _selectedTab.InputText = InputText;
                        // Unsubscribe from old tab's property changes
                        _selectedTab.PropertyChanged -= OnSelectedTabPropertyChanged;
                    }
                    
                    _selectedTab = value;
                    
                    // When tab changes, update input text and messages
                    if (_selectedTab != null)
                    {
                        // SPLENDID: Always directly reference the tab's Messages collection and force UI refresh
                        Messages = _selectedTab.IsContextMode ? 
                            _selectedTab.Messages : 
                            _selectedTab.ChatModeMessages;
                        InputText = _selectedTab.InputText;
                        // Update UI to reflect this tab's settings
                        OnPropertyChanged(nameof(Messages));
                        OnPropertyChanged(nameof(IsContextMode));
                        OnPropertyChanged(nameof(SelectedModel));
                        
                        // BULLY: Critical fix for per-tab chain isolation
                        // Notify UI that chain-related properties have changed for the new tab
                        OnPropertyChanged("SelectedTab.AvailableChains");
                        OnPropertyChanged("SelectedTab.SelectedChain");
                        
                        // Subscribe to new tab's property changes for real-time updates
                        _selectedTab.PropertyChanged += OnSelectedTabPropertyChanged;
                        
                        // Update markdown tab associations for the current editor
                        UpdateMarkdownTabAssociationForCurrentTab();
                        
                        // SPLENDID: Conditionally scroll when switching tabs - only if no messages yet or switching to a tab with recent activity
                        if (_selectedTab.Messages.Count == 0 || 
                            (_selectedTab.Messages.Count > 0 && _selectedTab.Messages.Last().Timestamp > DateTime.Now.AddMinutes(-5)))
                        {
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                            {
                                ScrollToBottom();
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
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

        public ICommand UnlockTabCommand { get; private set; }

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
                                SelectedTab.AddMessage(systemMessage);
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
                            SelectedTab?.AddMessage(systemMessage);
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
        public ICommand SendOrStopCommand { get; private set; }
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
            
            // Setup memory monitoring timer
            _memoryMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(5) // Check memory every 5 minutes
            };
            _memoryMonitorTimer.Tick += MonitorMemoryUsage;
            _memoryMonitorTimer.Start();
            
            // BULLY: Initialize streaming debounce timer for smooth UI updates
            _streamingDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150) // Update UI every 150ms max during streaming
            };
            _streamingDebounceTimer.Tick += OnStreamingDebounceTimerTick;
            
            // SPLENDID: Initialize commands with proper async support
            SendCommand = new AsyncRelayCommand(async () => await SendMessageAsync());
            SendOrStopCommand = new AsyncRelayCommand(async () => await SendOrStopAsync());
            ToggleModeCommand = new RelayCommand(_ => UpdateMode());
            VerifyDeviceCommand = new AsyncRelayCommand(async () => await VerifyDeviceAsync());
            RetryCommand = new RelayCommand<Models.ChatMessage>(param => _ = RetryMessageAsync(param));
            StopThinkingCommand = new RelayCommand<Models.ChatMessage>(param => StopThinking(param));
            
            // New commands for tabs
            AddTabCommand = new RelayCommand(_ => AddNewTab());
            CloseTabCommand = new RelayCommand(param => CloseTab(param as ChatTabViewModel));
            ClearHistoryCommand = new RelayCommand(_ => ClearHistory());
            UnlockTabCommand = new RelayCommand(param => UnlockTab(param as ChatTabViewModel));

            // Subscribe to messages collection changes
            _messages.CollectionChanged += Messages_CollectionChanged;

            // Subscribe to window state changes to handle scroll restoration after maximize/minimize
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.StateChanged += MainWindow_StateChanged;
                mainWindow.SizeChanged += MainWindow_SizeChanged;
                
                // BULLY FIX: Subscribe to MainTabControl selection changes for file type detection
                if (mainWindow is Views.MainWindow universaMainWindow)
                {
                    _mainWindow = universaMainWindow; // Store reference for cleanup
                    
                    if (universaMainWindow.MainTabControl != null)
                    {
                        universaMainWindow.MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
                        Debug.WriteLine("ChatSidebarViewModel: Successfully subscribed to MainTabControl.SelectionChanged");
                    }
                    else
                    {
                        // MainTabControl might not be loaded yet, try again after window is loaded
                        universaMainWindow.Loaded += (s, e) =>
                        {
                            if (universaMainWindow.MainTabControl != null)
                            {
                                universaMainWindow.MainTabControl.SelectionChanged += MainTabControl_SelectionChanged;
                                Debug.WriteLine("ChatSidebarViewModel: Successfully subscribed to MainTabControl.SelectionChanged (after window loaded)");
                            }
                            else
                            {
                                Debug.WriteLine("ChatSidebarViewModel: MainTabControl still null after window loaded");
                            }
                        };
                        Debug.WriteLine("ChatSidebarViewModel: MainTabControl not ready, will subscribe after window loads");
                    }
                }
                else
                {
                    Debug.WriteLine("ChatSidebarViewModel: Could not cast to Views.MainWindow");
                }
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
                    Debug.WriteLine($"OnModelsChanged: Event received with {models.Count} models");
                    foreach (var model in models)
                    {
                        Debug.WriteLine($"  - {model.DisplayName} ({model.Provider}: {model.Name})");
                    }
                    
                    Debug.WriteLine("OnModelsChanged: Refreshing available models");
                    AvailableModels.Clear();
                    foreach (var model in models)
                    {
                        AvailableModels.Add(model);
                    }
                    Debug.WriteLine($"OnModelsChanged: Added {AvailableModels.Count} models to collection");

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
                        
                        // BULLY FIX: Immediately detect file type and update chain dropdown
                        // Use Dispatcher to stay on UI thread for accessing AvalonEdit content  
                        Application.Current.Dispatcher.BeginInvoke(new Action(async () =>
                        {
                            try
                            {
                                Debug.WriteLine($"Tab switch - attempting to get content for: {newMarkdownTab.FilePath}");
                                string content = await GetCurrentContent();
                                Debug.WriteLine($"Tab switch - got content length: {content?.Length ?? 0} for file: {newMarkdownTab.FilePath}");
                                
                                if (!string.IsNullOrEmpty(content))
                                {
                                    string detectedFileType = DetectFileType(content, newMarkdownTab.FilePath);
                                    Debug.WriteLine($"Tab switch detected file type: '{detectedFileType ?? "null"}' for file: {newMarkdownTab.FilePath}");
                                    
                                    // Show frontmatter snippet for debugging
                                    if (content.StartsWith("---"))
                                    {
                                        int endIndex = content.IndexOf("\n---", 3);
                                        if (endIndex > 0)
                                        {
                                            string frontmatter = content.Substring(0, Math.Min(endIndex + 4, 200));
                                            Debug.WriteLine($"Frontmatter found: {frontmatter}");
                                        }
                                    }
                                    else
                                    {
                                        Debug.WriteLine("No frontmatter found (file doesn't start with ---)");
                                    }
                                    
                                    // Update immediately since we're already on UI thread
                                    if (SelectedTab != null)
                                    {
                                        Debug.WriteLine($"Updating SelectedTab '{SelectedTab.Name}' with file type: {detectedFileType ?? "null"}");
                                        SelectedTab.UpdateFileType(detectedFileType);
                                    }
                                    else
                                    {
                                        Debug.WriteLine("SelectedTab is null, cannot update file type");
                                    }
                                }
                                else
                                {
                                    Debug.WriteLine("No content available, cannot detect file type");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error detecting file type on tab switch: {ex.Message}");
                            }
                        }), System.Windows.Threading.DispatcherPriority.Background);
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
                Debug.WriteLine("RefreshModels: Starting model refresh...");
                var models = await _modelProvider.GetModels();
                
                Debug.WriteLine($"RefreshModels: Retrieved {models.Count} models from provider");
                foreach (var model in models)
                {
                    Debug.WriteLine($"  - {model.DisplayName} ({model.Provider}: {model.Name})");
                }
                
                // Update models on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableModels.Clear();
                    foreach (var model in models)
                    {
                        AvailableModels.Add(model);
                    }

                    Debug.WriteLine($"RefreshModels: Added {AvailableModels.Count} models to AvailableModels collection");

                    // If we have models but none selected, select the first one
                    if (AvailableModels.Any() && SelectedModel == null)
                    {
                        SelectedModel = AvailableModels.First();
                        Debug.WriteLine($"RefreshModels: Auto-selected first model: {SelectedModel.DisplayName}");
                    }
                    else if (SelectedModel != null)
                    {
                        Debug.WriteLine($"RefreshModels: Current SelectedModel: {SelectedModel.DisplayName}");
                    }
                    else
                    {
                        Debug.WriteLine("RefreshModels: No models available and SelectedModel is null");
                    }
                    
                    Debug.WriteLine($"RefreshModels: Final AvailableModels count: {AvailableModels.Count}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RefreshModels: Error refreshing models: {ex.Message}");
                Debug.WriteLine($"RefreshModels: Stack trace: {ex.StackTrace}");
            }
        }

        private void Messages_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // SPLENDID: Use conditional scroll that respects user position rather than always forcing scroll
                // The ChatSidebar UI will handle the scroll position detection
                ScrollToBottomAction?.Invoke();
                
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
                
                // No need to save scroll position - we'll scroll to bottom after mode switch
                
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
                        var persona = GetEffectivePersona();
                        var service = new GeneralChatService(apiKey, selectedModel.Name, selectedModel.Provider, selectedModel.IsThinkingMode, persona);
                        
                        // Initialize with previous messages, preserving reasoning for context
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
                                    // Preserve reasoning_details for chain-of-thought context
                                    if (!string.IsNullOrEmpty(msg.Reasoning) || msg.ReasoningDetails != null)
                                    {
                                        service.AddAssistantMessage(msg.Content, msg.Reasoning, msg.ReasoningDetails);
                                    }
                                    else
                                    {
                                        service.AddAssistantMessage(msg.Content);
                                    }
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
                
                // SPLENDID: Conditionally scroll after mode change - let user stay at their current position if viewing older messages
                // Only auto-scroll if switching to a mode with recent activity
                var activeMessages = currentTab.IsContextMode ? currentTab.Messages : currentTab.ChatModeMessages;
                if (activeMessages.Count == 0 || 
                    (activeMessages.Count > 0 && activeMessages.Last().Timestamp > DateTime.Now.AddMinutes(-5)))
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        ScrollToBottom();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
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
                var apiKey = GetApiKey(SelectedModel.Provider);
                if (string.IsNullOrEmpty(apiKey))
                {
                    throw new InvalidOperationException($"No API key found for provider {SelectedModel.Provider}");
                }
                
                // If we already have a service and context doesn't require refresh, return it
                if (SelectedTab.Service != null && !SelectedTab.ContextRequiresRefresh)
                {
                    return SelectedTab.Service;
                }

                bool isTabContextMode = SelectedTab.IsContextMode;

                if (!isTabContextMode)
                {
                    // Simple chat mode - create basic service
                    var persona = GetEffectivePersona();
                    SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                    SelectedTab.ContextRequiresRefresh = false;
                    return SelectedTab.Service;
                }

                // SPLENDID: Check if tab is locked to a specific file and chain
                if (SelectedTab.IsLocked)
                {
                    System.Diagnostics.Debug.WriteLine($"Using locked association - File: {SelectedTab.LockedFilePath}, Chain: {SelectedTab.LockedChainType}");
                    
                    // Use the locked file path and chain type
                    string lockedFilePath = SelectedTab.LockedFilePath;
                    ChainType chainType = SelectedTab.LockedChainType.Value;
                    
                    // Read content from the locked file
                    string lockedContent = string.Empty;
                    if (System.IO.File.Exists(lockedFilePath))
                    {
                        lockedContent = await System.IO.File.ReadAllTextAsync(lockedFilePath);
                    }
                    
                    string lockedLibraryPath = DetermineAppropriateLibraryPath(lockedFilePath, lockedContent);
                    
                    // Create service based on locked chain type
                    var lockedService = await CreateServiceByChain(chainType, apiKey, SelectedModel.Name, SelectedModel.Provider, lockedFilePath, lockedLibraryPath, lockedContent, SelectedTab.AssociatedEditor);
                    
                    if (lockedService != null)
                    {
                        SelectedTab.Service = lockedService;
                        SelectedTab.ContextRequiresRefresh = false;
                        
                        // Set up event handlers for specialized services
                        if (lockedService is FictionWritingBeta fictionBeta)
                        {
                            fictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                        }
                        else if (lockedService is OutlineWritingBeta outlineBeta)
                        {
                            outlineBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                        }
                        else if (lockedService is ProofreadingBeta proofreadingBeta)
                        {
                            proofreadingBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                        }
                        else if (lockedService is StoryAnalysisBeta storyAnalysisBeta)
                        {
                            storyAnalysisBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                        }
                        
                        return lockedService;
                    }
                    else
                    {
                        // Fallback to general chat if locked service creation fails
                        var persona = GetEffectivePersona();
                        SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                        SelectedTab.ContextRequiresRefresh = false;
                        return SelectedTab.Service;
                    }
                }

                // Context mode - need to determine file type and create specialized service
                var currentMarkdownTab = GetCurrentMarkdownTab();
                if (currentMarkdownTab?.FilePath == null)
                {
                    // No file context, use general chat
                    var persona = GetEffectivePersona();
                    SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                    SelectedTab.ContextRequiresRefresh = false;
                    return SelectedTab.Service;
                }

                string filePath = currentMarkdownTab.FilePath;
                string content = await GetCurrentContent();
                string libraryPath = DetermineAppropriateLibraryPath(filePath, content);

                System.Diagnostics.Debug.WriteLine($"GetOrCreateService: Creating service for file '{filePath}' with library path '{libraryPath}'");

                // SPLENDID: Skip file type detection for locked tabs - they maintain their locked chain
                if (!SelectedTab.IsLocked)
                {
                    // Detect file type and update the tab
                    string detectedFileType = DetectFileType(content, filePath);
                    SelectedTab.UpdateFileType(detectedFileType);
                    
                    System.Diagnostics.Debug.WriteLine($"Detected file type: {detectedFileType}, Selected chain: {SelectedTab.SelectedChain?.DisplayName}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Tab is locked - skipping file type detection, maintaining chain: {SelectedTab.LockedChainType}");
                }

                // If no chain is selected, default to Chat
                if (SelectedTab.SelectedChain == null)
                {
                    var persona = GetEffectivePersona();
                    SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                    SelectedTab.ContextRequiresRefresh = false;
                    return SelectedTab.Service;
                }

                // Create service based on selected chain
                var service = await CreateServiceByChain(SelectedTab.SelectedChain.Type, apiKey, SelectedModel.Name, SelectedModel.Provider, filePath, libraryPath, content, currentMarkdownTab);
                
                if (service != null)
                {
                    SelectedTab.Service = service;
                    SelectedTab.ContextRequiresRefresh = false;
                    
                    // SPLENDID: Lock the tab to this file and chain if it's not a General Chat
                    if (SelectedTab.SelectedChain.Type != ChainType.Chat && !SelectedTab.IsLocked)
                    {
                        SelectedTab.LockToFileAndChain(filePath, SelectedTab.SelectedChain.Type);
                        System.Diagnostics.Debug.WriteLine($"Auto-locked chat tab to file '{filePath}' with chain '{SelectedTab.SelectedChain.Type}'");
                    }
                    
                    // Set up event handlers for specialized services
                    if (service is FictionWritingBeta fictionBeta)
                    {
                        fictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                    }
                    else if (service is OutlineWritingBeta outlineBeta)
                    {
                        outlineBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                    }
                    else if (service is ProofreadingBeta proofreadingBeta)
                    {
                        proofreadingBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                    }
                    else if (service is StoryAnalysisBeta storyAnalysisBeta)
                    {
                        storyAnalysisBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                    }
                    
                    return service;
                }
                else
                {
                    // Fallback to general chat
                    var persona = GetEffectivePersona();
                    SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                    SelectedTab.ContextRequiresRefresh = false;
                    return SelectedTab.Service;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating service: {ex}");
                
                // Fallback to general chat service
                var apiKey = GetApiKey(SelectedModel.Provider);
                var persona = GetEffectivePersona();
                SelectedTab.Service = new GeneralChatService(apiKey, SelectedModel.Name, SelectedModel.Provider, false, persona);
                SelectedTab.ContextRequiresRefresh = false;
                return SelectedTab.Service;
            }
        }

        /// <summary>
        /// Gets the effective persona for the current chat - tab-specific persona overrides default setting
        /// </summary>
        private string GetEffectivePersona()
        {
            // Check if current tab has a persona set (tab-specific override)
            if (!string.IsNullOrWhiteSpace(SelectedTab?.CurrentPersona))
            {
                return SelectedTab.CurrentPersona;
            }
            
            // Fall back to default persona from settings
            return _configService?.Provider?.DefaultChatPersona;
        }

        private string DetectFileType(string content, string filePath)
        {
            // PRIMARY: Check for explicit type in frontmatter (type: fiction)
            string explicitType = GetExplicitFileType(content, filePath);
            if (!string.IsNullOrEmpty(explicitType))
            {
                System.Diagnostics.Debug.WriteLine($"File type detected from frontmatter 'type:' field: {explicitType}");
                return explicitType;
            }
            
            System.Diagnostics.Debug.WriteLine("No 'type:' field found in frontmatter, file type unknown");
            
            // OPTIONAL: Fallback to content-based detection only for legacy files without frontmatter
            // This is kept for backward compatibility but frontmatter should be the standard
            /*
            if (IsOutlineFile(content, filePath))
                return "outline";
            if (IsRulesFile(content, filePath))
                return "rules"; 
            if (IsFictionFile(content, filePath))
                return "fiction";
            if (IsNonFictionFile(content, filePath))
                return "nonfiction";
            */
                
            return null; // Unknown type - user should add frontmatter with 'type:' field
        }

        private async Task<BaseLangChainService> CreateServiceByChain(ChainType chainType, string apiKey, string modelName, AIProvider provider, string filePath, string libraryPath, string content, IFileTab currentMarkdownTab)
        {
            try
            {
                BaseLangChainService service = null;
                
                switch (chainType)
                {
                    case ChainType.Chat:
                        System.Diagnostics.Debug.WriteLine("Creating GeneralChatService for Chat chain - INDEPENDENT mode, no file content");
                        // SPLENDID: Determine persona from current tab or default setting
                        var persona = GetEffectivePersona();
                        service = new GeneralChatService(apiKey, modelName, provider, false, persona);
                        // SPLENDID: General Chat should NOT tie to file content - keep it completely independent
                        // Do NOT call UpdateContextAsync(content) - this keeps it as pure general chat
                        System.Diagnostics.Debug.WriteLine($"Chat service created with persona: {persona ?? "Default Assistant"}");
                        break;
                        
                    case ChainType.FictionWriting:
                        System.Diagnostics.Debug.WriteLine("Creating FictionWritingBeta for Fiction Writing chain");
                        var fictionService = await FictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                        await fictionService.UpdateContentAndInitialize(content);
                        if (currentMarkdownTab != null)
                        {
                            fictionService.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                        }
                        service = fictionService;
                        break;
                        
                    case ChainType.Proofreader:
                        System.Diagnostics.Debug.WriteLine("Creating ProofreadingBeta for Proofreader chain");
                        var proofreadingService = await ProofreadingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                        await proofreadingService.UpdateContentAndInitialize(content);
                        if (currentMarkdownTab != null)
                        {
                            proofreadingService.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                        }
                        service = proofreadingService;
                        break;
                        
                    case ChainType.StoryAnalysis:
                        System.Diagnostics.Debug.WriteLine("Creating StoryAnalysisBeta for Story Analysis chain");
                        var storyAnalysisService = await StoryAnalysisBeta.GetInstance(apiKey, modelName, provider, filePath);
                        await storyAnalysisService.UpdateContentAndInitialize(content);
                        service = storyAnalysisService;
                        break;
                        
                    case ChainType.OutlineWriter:
                        System.Diagnostics.Debug.WriteLine("Creating OutlineWritingBeta for Outline Writer chain");
                        var outlineService = await OutlineWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                        await outlineService.UpdateContentAndInitialize(content);
                        if (currentMarkdownTab != null)
                        {
                            outlineService.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                        }
                        service = outlineService;
                        break;
                        
                    case ChainType.RulesWriter:
                        System.Diagnostics.Debug.WriteLine("Creating RulesWritingBeta for Rules Writer chain");
                        var rulesService = await RulesWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                        await rulesService.UpdateContentAndInitialize(content);
                        service = rulesService;
                        break;
                        
                    case ChainType.CharacterDevelopment:
                        System.Diagnostics.Debug.WriteLine("Creating CharacterDevelopmentChain for Character Development chain");
                        var fileReferenceService = new FileReferenceService(libraryPath);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            fileReferenceService.SetCurrentFilePath(filePath);
                        }
                        var textSearchService = new EnhancedTextSearchService();
                        var chapterSearchService = new ChapterSearchService(textSearchService);
                        var characterStoryAnalysisService = new CharacterStoryAnalysisService(chapterSearchService, fileReferenceService, libraryPath);
                        var characterService = new CharacterDevelopmentChain(apiKey, modelName, provider, fileReferenceService, characterStoryAnalysisService);
                        await characterService.UpdateContextAsync(content);
                        // Note: CharacterDevelopmentChain doesn't have UpdateCursorPosition method
                        service = characterService;
                        break;
                        
                    case ChainType.StyleGuide:
                        System.Diagnostics.Debug.WriteLine("Creating StyleGuideChain for Style Guide chain");
                        var styleFileReferenceService = new FileReferenceService(libraryPath);
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            styleFileReferenceService.SetCurrentFilePath(filePath);
                        }
                        var styleService = new StyleGuideChain(apiKey, modelName, provider, styleFileReferenceService);
                        styleService.SetCurrentFilePath(filePath);
                        await styleService.UpdateContextAsync(content);
                        service = styleService;
                        break;
                        
                    default:
                        System.Diagnostics.Debug.WriteLine($"Unknown chain type: {chainType}, falling back to GeneralChatService - INDEPENDENT mode");
                        var fallbackPersona = GetEffectivePersona();
                        service = new GeneralChatService(apiKey, modelName, provider, false, fallbackPersona);
                        // SPLENDID: Default fallback should also be independent general chat
                        // Do NOT call UpdateContextAsync(content)
                        System.Diagnostics.Debug.WriteLine($"Fallback chat service created with persona: {fallbackPersona ?? "Default Assistant"}");
                        break;
                }
                
                return service;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating service for chain {chainType}: {ex}");
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
                
                // Immediately update the cursor position in the active service if it supports cursor positions
            if (SelectedTab?.Service is FictionWritingBeta betaService)
            {
                    Debug.WriteLine($"Directly updating cursor position in active FictionWritingBeta service: {newPosition}");
                betaService.UpdateCursorPosition(newPosition);
            }
            else if (SelectedTab?.Service is ProofreadingBeta proofreadingService)
            {
                    Debug.WriteLine($"Directly updating cursor position in active ProofreadingBeta service: {newPosition}");
                proofreadingService.UpdateCursorPosition(newPosition);
                    
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
                    var userMessage = new Models.ChatMessage("user", userInput, true)
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider
                    };
                    // Add user message using the tab's AddMessage method (with cleanup)
                    originatingTab.AddMessage(userMessage);

                    // Create response message that will be updated with streaming content
                    var responseMessage = new Models.ChatMessage("assistant", "")
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider,
                        IsThinking = true
                    };
                    
                    // Add response message using the tab's AddMessage method (with cleanup)
                    originatingTab.AddMessage(responseMessage);
                    
                    // SPLENDID: Only update Messages if the collection reference changed to avoid binding issues
                    if (originatingTab == SelectedTab)
                    {
                        var expectedMessages = originatingTab.IsContextMode ? 
                            originatingTab.Messages : 
                            originatingTab.ChatModeMessages;
                        
                        if (Messages != expectedMessages)
                        {
                            Messages = expectedMessages;
                        }
                        OnPropertyChanged(nameof(Messages));
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
                            
                            var errorMessage = new Models.ChatMessage("system", "Failed to create language service. Please check your settings and try again.", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true
                            };
                            originatingTab.AddMessage(errorMessage);
                            
                            // If this tab is the currently selected one, update the Messages property
                            if (originatingTab == SelectedTab)
                            {
                                Messages = activeMessages;
                            }
                            return;
                        }
                        Debug.WriteLine($"Service created, processing request... Type: {service.GetType().Name}");
                        
                        // BULLY: Create debounced streaming handler to prevent UI jumping
                        Action<string> contentUpdateHandler = (updatedContent) => {
                            try
                            {
                                // Use debounced update instead of immediate UI update
                                Application.Current.Dispatcher.BeginInvoke(new Action(() => {
                                    try
                                    {
                                        // Null check to prevent race condition with error handling
                                        if (responseMessage != null && updatedContent != null)
                                        {
                                            // Queue the update through the debounce system
                                            QueueStreamingUpdate(updatedContent, responseMessage, originatingTab);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error queuing streaming content update: {ex.Message}");
                                    }
                                }), System.Windows.Threading.DispatcherPriority.Background);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error in debounced contentUpdateHandler: {ex.Message}");
                            }
                        };
                        
                        // Subscribe to content updates from streaming
                        service.OnContentUpdated += contentUpdateHandler;
                        
                        try
                        {
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
                        }
                        finally
                        {
                            // Always unsubscribe from content updates to prevent memory leaks
                            try
                            {
                                service.OnContentUpdated -= contentUpdateHandler;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error unsubscribing from content updates: {ex.Message}");
                            }
                        }
                        
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
                            
                            var cancelMessage = new Models.ChatMessage("assistant", "Request cancelled by user.")
                            {
                                ModelName = originatingTab.SelectedModel.Name,
                                Provider = originatingTab.SelectedModel.Provider
                            };
                            originatingTab.AddMessage(cancelMessage);
                            
                            // SPLENDID: Update the Messages property and force UI refresh
                            if (originatingTab == SelectedTab)
                            {
                                Messages = cancelMessages;
                                OnPropertyChanged(nameof(Messages));
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
                            var interruptedMessage = new Models.ChatMessage("system", 
                                "The response was interrupted. This could be due to network issues or server timeouts. " +
                                "You can try sending your message again.", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true,
                                CanRetry = true
                            };
                            originatingTab.AddMessage(interruptedMessage);
                        }
                        else
                        {
                            var generalErrorMessage = new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                            {
                                LastUserMessage = userInput,
                                IsError = true
                            };
                            originatingTab.AddMessage(generalErrorMessage);
                        }
                        
                        // SPLENDID: Update the Messages property and force UI refresh
                        if (originatingTab == SelectedTab)
                        {
                            Messages = errorMessages;
                            OnPropertyChanged(nameof(Messages));
                        }
                        return;
                    }

                    // SPLENDID: Final update with completed response and force UI refresh
                    responseMessage.IsThinking = false;
                    responseMessage.Content = response;
                    
                    // Force UI refresh for the completed message
                    if (originatingTab == SelectedTab)
                    {
                        OnPropertyChanged(nameof(Messages));
                    }
                    
                    // SPLENDID: Conditionally scroll to bottom - let ChatSidebar UI decide based on user position
                    ScrollToBottom();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in SendMessageAsync: {ex}");
                    var activeMessages = originatingTab.IsContextMode ? 
                        originatingTab.Messages : 
                        originatingTab.ChatModeMessages;
                        
                    var outerErrorMessage = new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                    {
                        LastUserMessage = userInput,
                        IsError = true
                    };
                    originatingTab.AddMessage(outerErrorMessage);
                    
                    // SPLENDID: Update the Messages property and force UI refresh
                    if (originatingTab == SelectedTab)
                    {
                        Messages = activeMessages;
                        OnPropertyChanged(nameof(Messages));
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
                var fallbackErrorMessage = new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                {
                    LastUserMessage = InputText,
                    IsError = true
                };
                SelectedTab?.AddMessage(fallbackErrorMessage);
            }
        }
        
        private void ScrollToBottom()
        {
            // Use the delegate to request scrolling from the UI
            ScrollToBottomAction?.Invoke();
        }
        

        
        /// <summary>
        /// Handles window state changes (maximize/minimize) to restore scroll position
        /// </summary>
        private void MainWindow_StateChanged(object sender, EventArgs e)
        {
            // SPLENDID: Only auto-scroll on window state change if there are recent messages
            // This prevents disrupting users viewing older chat history
            if (SelectedTab?.Messages.Count > 0)
            {
                var lastMessage = SelectedTab.Messages.Last();
                if (lastMessage.Timestamp > DateTime.Now.AddMinutes(-10)) // Only if recent activity
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        ScrollToBottom();
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
        }
        
        /// <summary>
        /// Handles window size changes to ensure chat scrolls to bottom
        /// </summary>
        private void MainWindow_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
        {
            // SPLENDID: Only auto-scroll on window resize if there are recent messages
            // This prevents disrupting users viewing older chat history during window operations
            if (SelectedTab?.Messages.Count > 0)
            {
                var lastMessage = SelectedTab.Messages.Last();
                if (lastMessage.Timestamp > DateTime.Now.AddMinutes(-10)) // Only if recent activity
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        ScrollToBottom();
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
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
                            // SPLENDID: Don't update name here if tab will be locked later - let LockToFileAndChain handle it
                            if (!SelectedTab.IsLocked)
                            {
                                SelectedTab.Name = $"Chat - {fileName}";
                            }
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
                else if (SelectedTab?.Service is ProofreadingBeta proofreadingService)
                {
                    proofreadingService.SetCurrentFilePath(markdownTab.FilePath);
                    
                    // Make sure to update the cursor position in the service
                    // Update service with current cursor position
                    proofreadingService.UpdateCursorPosition(cursorPosition);
                    Debug.WriteLine($"Updated ProofreadingBeta cursor position to {cursorPosition} in GetCurrentContent");
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
                                // SPLENDID: Don't update name here if tab will be locked later - let LockToFileAndChain handle it
                                if (!SelectedTab.IsLocked)
                                {
                                    SelectedTab.Name = $"Chat - {fileName}";
                                }
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
        
        /// <summary>
        /// SPLENDID: Handles property changes from the selected tab to keep UI in sync
        /// </summary>
        private void OnSelectedTabPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Forward chain-related property changes to update the UI bindings
            if (e.PropertyName == nameof(ChatTabViewModel.SelectedChain))
            {
                OnPropertyChanged("SelectedTab.SelectedChain");
                System.Diagnostics.Debug.WriteLine($"Forwarded SelectedChain property change from tab '{SelectedTab?.Name}'");
            }
            else if (e.PropertyName == nameof(ChatTabViewModel.AvailableChains))
            {
                OnPropertyChanged("SelectedTab.AvailableChains");
                System.Diagnostics.Debug.WriteLine($"Forwarded AvailableChains property change from tab '{SelectedTab?.Name}'");
            }
            else if (e.PropertyName == nameof(ChatTabViewModel.SelectedModel))
            {
                OnPropertyChanged(nameof(SelectedModel));
                System.Diagnostics.Debug.WriteLine($"Forwarded SelectedModel property change from tab '{SelectedTab?.Name}'");
            }
            else if (e.PropertyName == nameof(ChatTabViewModel.IsContextMode))
            {
                OnPropertyChanged(nameof(IsContextMode));
                System.Diagnostics.Debug.WriteLine($"Forwarded IsContextMode property change from tab '{SelectedTab?.Name}'");
            }
        }

        /// <summary>
        /// BULLY: Debounced streaming content handler to prevent UI jumping
        /// Batches streaming updates to max 6-7 times per second for smooth experience
        /// </summary>
        private void QueueStreamingUpdate(string content, Models.ChatMessage message, ChatTabViewModel tab)
        {
            lock (_streamingLock)
            {
                _pendingStreamContent = content;
                _currentStreamingMessage = message;
                _currentStreamingTab = tab;
                
                // Start or restart the debounce timer
                _streamingDebounceTimer.Stop();
                _streamingDebounceTimer.Start();
            }
        }

        /// <summary>
        /// SPLENDID: Timer tick handler that applies debounced streaming updates
        /// </summary>
        private void OnStreamingDebounceTimerTick(object sender, EventArgs e)
        {
            _streamingDebounceTimer.Stop();
            
            lock (_streamingLock)
            {
                if (_currentStreamingMessage != null && _pendingStreamContent != null)
                {
                    try
                    {
                        // Apply the batched content update
                        _currentStreamingMessage.Content = _pendingStreamContent;
                        
                        // SPLENDID: Only scroll if this is the currently selected tab and message is recent
                        // Avoid disrupting user if they're viewing older messages during streaming
                        if (_currentStreamingTab == SelectedTab)
                        {
                            ScrollToBottom();
                        }
                        
                        // Clear the pending update
                        _pendingStreamContent = null;
                        _currentStreamingMessage = null;
                        _currentStreamingTab = null;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in debounced streaming update: {ex.Message}");
                    }
                }
            }
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

        private async Task SendOrStopAsync()
        {
            if (IsBusy)
            {
                // Stop the current operation
                StopThinking(null);
                IsBusy = false;
            }
            else
            {
                // Send a new message
                await SendMessageAsync();
            }
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
        private async void AddNewTab(string name = null)
        {
            // Create tab with generic name initially if not specified
            var defaultName = name ?? "New Chat";
            var newTab = new ChatTabViewModel(defaultName);
            
            // Set initial model based on current selection or default
            newTab.SelectedModel = _selectedModel;
            
            // Add tab immediately to UI
            Tabs.Add(newTab);
            SelectedTab = newTab; // This will automatically subscribe to property changes via the SelectedTab setter
            
            // Asynchronously detect file type and populate chains if we have a current markdown tab
            var currentMarkdownTab = GetCurrentMarkdownTab();
            if (currentMarkdownTab?.FilePath != null)
            {
                try
                {
                    // Get current content to detect file type asynchronously
                    var content = await GetCurrentContent();
                    string detectedFileType = DetectFileType(content, currentMarkdownTab.FilePath);
                    
                    // Update the tab with detected file type on UI thread
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // SPLENDID: Set the associated editor so auto-locking can work
                        newTab.AssociatedEditor = currentMarkdownTab;
                        newTab.UpdateFileType(detectedFileType);
                        
                        System.Diagnostics.Debug.WriteLine($"New tab created with detected file type: {detectedFileType}");
                        System.Diagnostics.Debug.WriteLine($"Tab associated with editor: {currentMarkdownTab?.FilePath ?? "None"}");
                        
                        // If we detected a specific file type and it's not just generic "Chat", 
                        // update the name to indicate chain selection is available
                        if (!string.IsNullOrEmpty(detectedFileType) && name == null)
                        {
                            // Keep the generic name until chain selection
                            newTab.Name = "Select Chain...";
                        }
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error detecting file type for new tab: {ex.Message}");
                }
            }
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
                    
                    // SPLENDID: Restore locked tab properties
                    if (tabData.IsLocked && !string.IsNullOrEmpty(tabData.LockedFilePath) && !string.IsNullOrEmpty(tabData.LockedChainType))
                    {
                        if (Enum.TryParse<ChainType>(tabData.LockedChainType, out var chainType))
                        {
                            tab.LockToFileAndChain(tabData.LockedFilePath, chainType);
                            System.Diagnostics.Debug.WriteLine($"Restored locked tab: {tab.Name} -> {tabData.LockedFilePath} ({chainType})");
                        }
                    }
                    
                    // SPLENDID: Restore persona setting
                    if (!string.IsNullOrEmpty(tabData.CurrentPersona))
                    {
                        tab.CurrentPersona = tabData.CurrentPersona;
                        System.Diagnostics.Debug.WriteLine($"Restored persona for tab '{tab.Name}': {tabData.CurrentPersona}");
                    }

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
                    
                    // SPLENDID: Conditionally scroll to bottom after loading tabs - only if recent activity
                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(500) // Give window time to finish initializing
                    };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        
                        // Only scroll if the selected tab has recent messages (within last hour)
                        if (SelectedTab?.Messages.Count > 0)
                        {
                            var lastMessage = SelectedTab.Messages.Last();
                            if (lastMessage.Timestamp > DateTime.Now.AddHours(-1))
                            {
                                ScrollToBottom();
                            }
                        }
                        else if (SelectedTab?.Messages.Count == 0)
                        {
                            // Always scroll for empty tabs (ready for new conversation)
                            ScrollToBottom();
                        }
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
                // SPLENDID: Check for locked tabs first
                if (tab.IsLocked && tab.LockedFilePath == closedFilePath)
                {
                    Debug.WriteLine($"Unlocking tab {tab.Name} because locked file was closed: {closedFilePath}");
                    tab.Unlock();
                    // Reset to a generic name since the file is no longer available
                    tab.Name = "Chat";
                }
                
                if (tab.AssociatedEditor != null && tab.AssociatedFilePath == closedFilePath)
                {
                    Debug.WriteLine($"Clearing association for tab {tab.Name} because editor was closed");
                    tab.AssociatedEditor = null;
                    
                    // Optional: Update tab name to remove file reference if not locked
                    if (!tab.IsLocked && tab.Name.Contains(System.IO.Path.GetFileName(closedFilePath)))
                    {
                        tab.Name = "Chat";
                    }
                }
                else if (tab.Tag is string tagPath && tagPath == closedFilePath)
                {
                    Debug.WriteLine($"Clearing tag association for tab {tab.Name} because editor was closed");
                    tab.Tag = null;
                    
                    // Optional: Update tab name to remove file reference if not locked
                    if (!tab.IsLocked && tab.Name.Contains(System.IO.Path.GetFileName(closedFilePath)))
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
                            string value = line.Substring(colonIndex + 1).Trim(); // Preserve original case for file paths
                            
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
                            string value = line.Substring(colonIndex + 1).Trim(); // Preserve original case for file paths
                            
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

        private string GetExplicitFileType(string content, string filePath)
        {
            // First, check frontmatter for explicit type declaration
            if (!string.IsNullOrEmpty(content) && content.StartsWith("---"))
            {
                int endIndex = content.IndexOf("\n---", 3);
                if (endIndex > 0)
                {
                    string frontmatter = content.Substring(0, endIndex + 4).ToLowerInvariant();
                    
                    // Parse frontmatter for explicit type
                    var lines = frontmatter.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (string line in lines)
                    {
                        int colonIndex = line.IndexOf(':');
                        if (colonIndex > 0)
                        {
                            string key = line.Substring(0, colonIndex).Trim();
                            string value = line.Substring(colonIndex + 1).Trim();
                            
                            if (key == "type")
                            {
                                Debug.WriteLine($"Found explicit type in frontmatter: {value}");
                                return value; // Return exactly what the user specified
                            }
                        }
                    }
                }
            }
            
            Debug.WriteLine("No explicit type found in frontmatter, will use content-based detection");
            return null; // No explicit type found
        }

        private BaseLangChainService CreateServiceByType(string explicitType, string apiKey, string modelName, AIProvider provider, bool isThinkingMode, string filePath, string libraryPath, string content)
        {
            switch (explicitType?.ToLowerInvariant())
            {
                case "rules":
                    Debug.WriteLine("Creating RulesWritingBeta service based on explicit frontmatter type");
                    return RulesWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath).Result;
                    
                case "fiction":
                case "novel":
                case "story":
                    Debug.WriteLine("Creating FictionWritingBeta service based on explicit frontmatter type");
                    return FictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath).Result;
                    
                case "nonfiction":
                case "non-fiction":
                case "biography":
                case "autobiography":
                case "memoir":
                case "history":
                case "academic":
                    Debug.WriteLine("Creating NonFictionWritingBeta service based on explicit frontmatter type");
                    return NonFictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath).Result;
                    
                case "outline":
                    Debug.WriteLine("Creating OutlineWritingBeta service based on explicit frontmatter type");
                    return OutlineWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath).Result;
                    
                case "proofreading":
                case "proofread":
                case "editing":
                case "copyedit":
                case "copy-edit":
                    Debug.WriteLine("Creating ProofreadingBeta service based on explicit frontmatter type");
                    return ProofreadingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath).Result;
                    
                case "characters":
                case "character":
                    Debug.WriteLine("Creating CharacterDevelopmentChain service based on explicit frontmatter type");
                    var fileReferenceService = new FileReferenceService(libraryPath);
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        fileReferenceService.SetCurrentFilePath(filePath);
                    }
                    var textSearchService = new EnhancedTextSearchService();
                    var chapterSearchService = new ChapterSearchService(textSearchService);
                    var storyAnalysisService = new CharacterStoryAnalysisService(chapterSearchService, fileReferenceService, libraryPath);
                    return new CharacterDevelopmentChain(apiKey, modelName, provider, fileReferenceService, storyAnalysisService);
                    
                default:
                    Debug.WriteLine($"Unknown explicit type '{explicitType}', falling back to content-based detection");
                    return null; // Fall back to content-based detection
            }
        }

        private async Task CreateServiceByContentDetection(bool isTabContextMode, IFileTab currentMarkdownTab, string apiKey, string modelName, AIProvider provider, bool isThinkingMode, string filePath, string libraryPath, string content)
        {
            // Fallback to content-based detection using the old priority system
            // Check for outline files first
            bool isOutline = IsOutlineFile(content, filePath);
            Debug.WriteLine($"IsOutlineFile result: {isOutline}");
            
            if (isOutline)
            {
                Debug.WriteLine("Creating OutlineWritingBeta service for outline file");
                
                try
                {
                    var outlineService = await OutlineWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                    SelectedTab.Service = outlineService;
                    
                    await SelectedTab.Service.UpdateContextAsync(content);
                    
                    if (SelectedTab.Service is OutlineWritingBeta outlineBeta)
                    {
                        outlineBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                        outlineBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                    }
                    
                    Debug.WriteLine("Successfully created OutlineWritingBeta service");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating OutlineWritingBeta service: {ex}");
                    var persona = GetEffectivePersona();
                    SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode, persona);
                    // SPLENDID: General chat should be independent, don't pass file content
                    // await SelectedTab.Service.UpdateContextAsync(content);
                }
            }
            else
            {
                // Check for rules files second
                bool isRules = IsRulesFile(content, filePath);
                Debug.WriteLine($"IsRulesFile result: {isRules}");
                
                if (isRules)
                {
                    Debug.WriteLine("Creating RulesWritingBeta service for rules file");
                    
                    try
                    {
                        var rulesService = await RulesWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                        SelectedTab.Service = rulesService;
                        
                        await SelectedTab.Service.UpdateContextAsync(content);
                        
                        Debug.WriteLine($"Successfully created RulesWritingBeta service");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error creating RulesWritingBeta service: {ex.Message}");
                        var persona = GetEffectivePersona();
                        SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode, persona);
                        // SPLENDID: General chat should be independent, don't pass file content
                        // await SelectedTab.Service.UpdateContextAsync(content);
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
                            var fictionService = await FictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                            SelectedTab.Service = fictionService;
                            
                            await SelectedTab.Service.UpdateContextAsync(content);
                            
                            if (SelectedTab.Service is FictionWritingBeta fictionBeta)
                            {
                                fictionBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                                fictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                            }
                            
                            Debug.WriteLine("Successfully created FictionWritingBeta service");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error creating FictionWritingBeta service: {ex}");
                            var persona = GetEffectivePersona();
                            SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode, persona);
                            // SPLENDID: General chat should be independent, don't pass file content
                            // await SelectedTab.Service.UpdateContextAsync(content);
                        }
                    }
                    else
                    {
                        // Check for non-fiction files fourth
                        bool isNonFiction = IsNonFictionFile(content, filePath);
                        Debug.WriteLine($"IsNonFictionFile result: {isNonFiction}");
                        
                        if (isNonFiction)
                        {
                            Debug.WriteLine("Creating NonFictionWritingBeta service for non-fiction file");
                            
                            try
                            {
                                var nonfictionService = await NonFictionWritingBeta.GetInstance(apiKey, modelName, provider, filePath, libraryPath);
                                SelectedTab.Service = nonfictionService;
                                
                                await SelectedTab.Service.UpdateContextAsync(content);
                                
                                if (SelectedTab.Service is NonFictionWritingBeta nonfictionBeta)
                                {
                                    nonfictionBeta.UpdateCursorPosition(currentMarkdownTab.LastKnownCursorPosition);
                                    nonfictionBeta.OnRetryingOverloadedRequest += RetryEventHandler;
                                }
                                
                                Debug.WriteLine("Successfully created NonFictionWritingBeta service");
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error creating NonFictionWritingBeta service: {ex}");
                                var persona = GetEffectivePersona();
                                SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode, persona);
                                // SPLENDID: General chat should be independent, don't pass file content
                                // await SelectedTab.Service.UpdateContextAsync(content);
                            }
                        }
                        else
                        {
                            Debug.WriteLine("Creating GeneralChatService for markdown file that doesn't match any specific type - INDEPENDENT mode");
                            var persona = GetEffectivePersona();
                            SelectedTab.Service = new GeneralChatService(apiKey, modelName, provider, isThinkingMode, persona);
                            // SPLENDID: Keep general chat independent, do not pass file content
                            // await SelectedTab.Service.UpdateContextAsync(content);
                        }
                    }
                }
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
                Debug.WriteLine($"Error in GetCurrentMarkdownTab: {ex.Message}");
                return null;
            }
        }

        // BULLY FIX: Add cleanup method to prevent memory leaks
        public void Cleanup()
        {
            try
            {
                Debug.WriteLine("ChatSidebarViewModel: Starting cleanup...");
                
                // Unsubscribe from all event handlers
                if (_modelProvider != null)
                {
                    _modelProvider.ModelsChanged -= OnModelsChanged;
                }
                
                if (_mainWindow != null)
                {
                    _mainWindow.StateChanged -= MainWindow_StateChanged;
                    _mainWindow.SizeChanged -= MainWindow_SizeChanged;
                    
                    if (_mainWindow.MainTabControl != null)
                    {
                        _mainWindow.MainTabControl.SelectionChanged -= MainTabControl_SelectionChanged;
                        Debug.WriteLine("ChatSidebarViewModel: Unsubscribed from MainTabControl.SelectionChanged");
                    }
                }
                
                // Clean up auto-save timer
                if (_autoSaveTimer != null)
                {
                    _autoSaveTimer.Stop();
                    _autoSaveTimer = null;
                }
                
                // Clean up memory monitor timer
                if (_memoryMonitorTimer != null)
                {
                    _memoryMonitorTimer.Stop();
                    _memoryMonitorTimer = null;
                }
                
                // Clean up streaming debounce timer
                if (_streamingDebounceTimer != null)
                {
                    _streamingDebounceTimer.Stop();
                    _streamingDebounceTimer = null;
                }
                
                // Clean up cancellation tokens
                if (_cancellationTokenSource != null)
                {
                    try { if (!_cancellationTokenSource.Token.IsCancellationRequested) { _cancellationTokenSource.Cancel(); } } 
                    catch (ObjectDisposedException) { /* ignore */ }
                    _cancellationTokenSource = null;
                }
                
                // Clean up message requests
                foreach (var kvp in _messageRequests)
                {
                    try
                    {
                        if (kvp.Value != null && !kvp.Value.Token.IsCancellationRequested) { kvp.Value.Cancel(); }
                        kvp.Value?.Dispose();
                    }
                    catch (ObjectDisposedException) { /* ignore */ }
                }
                _messageRequests.Clear();
                
                // Dispose tab services
                foreach (var tab in Tabs)
                {
                    try
                    {
                        tab.Service?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing tab service: {ex.Message}");
                    }
                }
                
                Debug.WriteLine("ChatSidebarViewModel: Cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during ChatSidebarViewModel cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Unlocks a chat tab, allowing it to dynamically adapt to context again
        /// </summary>
        private void UnlockTab(ChatTabViewModel tab)
        {
            if (tab != null && tab.IsLocked)
            {
                System.Diagnostics.Debug.WriteLine($"Manually unlocking tab: {tab.Name}");
                tab.Unlock();
                
                // Reset to generic name and clear service to restart fresh
                tab.Name = "Chat";
                tab.Service = null;
                tab.ContextRequiresRefresh = true;
                
                // Clear any association as well
                tab.AssociatedEditor = null;
                
                System.Diagnostics.Debug.WriteLine($"Tab unlocked successfully");
            }
        }
        
        /// <summary>
        /// Monitors memory usage and triggers cleanup when needed
        /// </summary>
        private void MonitorMemoryUsage(object sender, EventArgs e)
        {
            try
            {
                // Get current process memory usage
                var process = System.Diagnostics.Process.GetCurrentProcess();
                long memoryMB = process.WorkingSet64 / (1024 * 1024);
                
                // Threshold for triggering cleanup (1.5 GB)
                const long MEMORY_THRESHOLD_MB = 1536;
                
                // Calculate total estimated memory from all tabs
                long totalTabMemory = 0;
                int totalMessages = 0;
                
                foreach (var tab in Tabs)
                {
                    totalTabMemory += tab.GetEstimatedMemoryUsage();
                    totalMessages += tab.Messages.Count + tab.ChatModeMessages.Count;
                }
                
                long totalTabMemoryMB = totalTabMemory / (1024 * 1024);
                
                System.Diagnostics.Debug.WriteLine($"Memory monitoring: Process={memoryMB}MB, TabMessages={totalTabMemoryMB}MB, TotalMessages={totalMessages}");
                
                // Trigger cleanup if memory usage is high
                if (memoryMB > MEMORY_THRESHOLD_MB || totalTabMemoryMB > 100)
                {
                    System.Diagnostics.Debug.WriteLine($"High memory usage detected ({memoryMB}MB), triggering cleanup...");
                    TriggerMemoryCleanup();
                }
                
                // Optimize message storage every 10 minutes regardless of memory usage
                if (DateTime.Now.Minute % 10 == 0)
                {
                    OptimizeAllTabStorage();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in memory monitoring: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Triggers memory cleanup across all tabs
        /// </summary>
        private void TriggerMemoryCleanup()
        {
            int cleanedTabs = 0;
            
            foreach (var tab in Tabs)
            {
                int messagesBefore = tab.Messages.Count + tab.ChatModeMessages.Count;
                
                // Force cleanup if tab has too many messages
                if (messagesBefore > 30)
                {
                    // Trigger cleanup by adding a dummy message and removing it
                    // This will cause CleanupMessagesIfNeeded to run
                    var tempMessage = new Models.ChatMessage("system", "temp");
                    tab.AddMessage(tempMessage);
                    tab.Messages.Remove(tempMessage);
                    tab.ChatModeMessages.Remove(tempMessage);
                    
                    int messagesAfter = tab.Messages.Count + tab.ChatModeMessages.Count;
                    if (messagesAfter < messagesBefore)
                    {
                        cleanedTabs++;
                    }
                }
            }
            
            // Force garbage collection after cleanup
            if (cleanedTabs > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Memory cleanup complete: cleaned {cleanedTabs} tabs");
                GC.Collect(2, GCCollectionMode.Optimized);
                GC.WaitForPendingFinalizers();
            }
        }
        
        /// <summary>
        /// Optimizes message storage for all tabs
        /// </summary>
        private void OptimizeAllTabStorage()
        {
            foreach (var tab in Tabs)
            {
                tab.OptimizeMessageStorage();
            }
            
            System.Diagnostics.Debug.WriteLine("Completed storage optimization for all tabs");
        }
        
        /// <summary>
        /// Gets total memory usage statistics for debugging
        /// </summary>
        public string GetMemoryUsageStats()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            long processMemoryMB = process.WorkingSet64 / (1024 * 1024);
            
            long totalTabMemory = 0;
            int totalMessages = 0;
            
            foreach (var tab in Tabs)
            {
                totalTabMemory += tab.GetEstimatedMemoryUsage();
                totalMessages += tab.Messages.Count + tab.ChatModeMessages.Count;
            }
            
            long totalTabMemoryMB = totalTabMemory / (1024 * 1024);
            
            return $"Process: {processMemoryMB}MB | Tab Messages: {totalTabMemoryMB}MB | Total Messages: {totalMessages} | Tabs: {Tabs.Count}";
        }
    }
}