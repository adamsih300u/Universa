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

namespace Universa.Desktop.ViewModels
{
    public class ChatSidebarViewModel : INotifyPropertyChanged
    {
        private readonly ModelProvider _modelProvider;
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private BaseLangChainService _currentService;
        private GeneralChatService _generalChatService;
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

        private Dictionary<MarkdownTab, ChatTabViewModel> _markdownTabAssociations = new Dictionary<MarkdownTab, ChatTabViewModel>();

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
                                    _generalChatService = GeneralChatService.GetInstance(apiKey, value.Name, value.Provider, value.IsThinkingMode);
                                    _currentService = _generalChatService;
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
                                _generalChatService = GeneralChatService.GetInstance(apiKey, value.Name, value.Provider, value.IsThinkingMode);
                                _currentService = _generalChatService;
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
                    else if (oldTab.Content is MarkdownTab oldMarkdownTab)
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
                    else if (newTab.Content is MarkdownTab newMarkdownTab)
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
                // Scroll to the bottom of the chat view when new messages are added
                ChatScrollViewer?.ScrollToEnd();
                
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
                
                bool isContextMode = currentTab.IsContextMode;
                
                Debug.WriteLine($"UpdateMode - Current mode: {(isContextMode ? "Context" : "Chat")}");
                if (isContextMode)
                {
                    Debug.WriteLine("Switching to context mode");
                    _currentService = null;
                    // Use the tab's main message collection for context mode
                    Messages = currentTab.Messages;
                }
                else
                {
                    Debug.WriteLine("Switching to chat mode");
                    // Use the tab's chat-specific message collection
                    Messages = currentTab.ChatModeMessages;
                    
                    if (_generalChatService == null)
                    {
                        var selectedModel = currentTab.SelectedModel;
                        if (selectedModel != null)
                        {
                            var apiKey = GetApiKey(selectedModel.Provider);
                            Debug.WriteLine($"Creating new GeneralChatService with provider: {selectedModel.Provider}");
                            _generalChatService = GeneralChatService.GetInstance(apiKey, selectedModel.Name, selectedModel.Provider, selectedModel.IsThinkingMode);
                        }
                    }
                    _currentService = _generalChatService;
                }
                
                Debug.WriteLine("Clearing input and raising property changes");
                InputText = string.Empty;
                OnPropertyChanged(nameof(Messages));
                OnPropertyChanged(nameof(IsContextMode));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateMode: {ex}");
                MessageBox.Show($"Error switching modes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisposeCurrentService()
        {
            // Unsubscribe from events before disposing
            if (_currentService is FictionWritingBeta betaService)
            {
                Debug.WriteLine("Unsubscribing from FictionWritingBeta events");
                betaService.OnRetryingOverloadedRequest -= RetryEventHandler;
            }
            else if (_currentService is FictionWritingChain chainService)
            {
                Debug.WriteLine("Unsubscribing from FictionWritingChain events");
                chainService.OnRetryingOverloadedRequest -= RetryEventHandler;
            }
            
            // Dispose of the service
            if (_currentService != null)
            {
                Debug.WriteLine("Disposing current service");
                _currentService.Dispose();
            _currentService = null;
            }
        }

        private void RetryEventHandler(object sender, RetryEventArgs args)
        {
            var thinkingMessage = Messages.LastOrDefault(m => m.Role == "assistant" && 
                (m.Content.StartsWith("Thinking") || m.Content.StartsWith("⏳")));
            if (thinkingMessage != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    thinkingMessage.Content = $"⏳ Anthropic API overloaded. Retry {args.RetryCount}/{args.MaxRetries} in {args.DelayMs/1000.0:F1}s...";
                    // Force UI update
                    OnPropertyChanged(nameof(Messages));
                });
            }
        }

        private async Task<BaseLangChainService> GetOrCreateService()
        {
            if (_isInitializing) 
            {
                Debug.WriteLine("Cannot create service: System is still initializing");
                return null;
            }

            try
            {
                if (SelectedModel == null)
                {
                    Debug.WriteLine("Cannot create service: No model selected");
                    throw new InvalidOperationException("No AI model is selected. Please select a model from the dropdown.");
                }

            var apiKey = _modelProvider.GetApiKey(SelectedModel.Provider);
                if (string.IsNullOrEmpty(apiKey) && SelectedModel.Provider != AIProvider.Ollama)
            {
                    Debug.WriteLine($"Cannot create service: No API key configured for {SelectedModel.Provider}");
                    throw new InvalidOperationException($"No API key configured for {SelectedModel.Provider}. Please configure it in settings.");
            }

            // Check if context refresh is needed
            bool needsContextRefresh = false;
            if (IsContextMode && SelectedTab != null && SelectedTab.ContextRequiresRefresh)
            {
                Debug.WriteLine("Context requires refreshing due to content changes");
                needsContextRefresh = true;
                SelectedTab.ContextRequiresRefresh = false;
            }

            // If we're in chat mode, use the general chat service
            if (!IsContextMode)
            {
                if (_generalChatService == null)
                {
                        try
                        {
                            Debug.WriteLine($"Creating new GeneralChatService with model {SelectedModel.Name}");
                    _generalChatService = GeneralChatService.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, SelectedModel.IsThinkingMode);
                    
                    // Initialize service with previous messages from this tab
                    if (SelectedTab != null && SelectedTab.ChatModeMessages.Count > 0)
                    {
                        bool systemMessageAdded = false;
                        
                        // Add previous messages to the service's memory
                        foreach (var msg in SelectedTab.ChatModeMessages)
                        {
                            if (msg.Role == "system")
                            {
                                if (!systemMessageAdded)
                                {
                                    _generalChatService.AddSystemMessage(msg.Content);
                                    systemMessageAdded = true;
                                }
                            }
                            else if (msg.Role == "user")
                            {
                                _generalChatService.AddUserMessage(msg.Content);
                            }
                            else if (msg.Role == "assistant")
                            {
                                _generalChatService.AddAssistantMessage(msg.Content);
                            }
                        }
                        
                        Debug.WriteLine($"Initialized ChatService with {SelectedTab.ChatModeMessages.Count} previous messages");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error creating GeneralChatService: {ex.Message}");
                            throw new InvalidOperationException($"Failed to initialize chat service: {ex.Message}");
                    }
                }
                return _generalChatService;
            }

            // We're in context mode, check if we need to refresh the context
            bool needsRefresh = false;
            
            if (SelectedTab != null && SelectedTab.ContextRequiresRefresh)
            {
                Debug.WriteLine("Context requires refreshing due to content changes");
                needsRefresh = true;
                SelectedTab.ContextRequiresRefresh = false;
            }
            
            // Check if the current service is null, which would require a refresh
            if (_currentService == null)
            {
                Debug.WriteLine("Current service is null, creating new service");
                needsRefresh = true;
            }

                try
                {
            if (needsRefresh)
            {
                var fileContent = await GetCurrentContent();
                if (string.IsNullOrEmpty(fileContent))
                {
                    Debug.WriteLine("Warning: No content available to refresh context");
                }
                else
                {
                            // Get the current file path
                            string filePath = null;
                            
                            // Get the current cursor position information
                            int cursorPosition = -1;
                            MarkdownTab currentMarkdownTab = null;
                            
                            // Get the cursor position from the current editor if it's a markdown tab
                            if (_currentEditor is MarkdownTab mdTab)
                            {
                                currentMarkdownTab = mdTab;
                                cursorPosition = mdTab.LastKnownCursorPosition;
                                filePath = mdTab.FilePath;
                                Debug.WriteLine($"Found cursor position from current markdown tab: {cursorPosition}");
                            }
                            
                            // Get the current file path
                            if (filePath == null)
                            {
                                if (SelectedTab?.AssociatedEditor != null)
                                {
                                    filePath = SelectedTab.AssociatedFilePath;
                                }
                                else
                                {
                                    var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                                    var selectedTabItem = mainWindow?.MainTabControl?.SelectedItem as System.Windows.Controls.TabItem;
                                    
                                    if (selectedTabItem?.Content is MarkdownTab tabContentMdTab)
                                    {
                                        filePath = tabContentMdTab.FilePath;
                                        if (currentMarkdownTab == null)
                                        {
                                            currentMarkdownTab = tabContentMdTab;
                                            cursorPosition = tabContentMdTab.LastKnownCursorPosition;
                                            Debug.WriteLine($"Found cursor position from TabItem markdown tab: {cursorPosition}");
                                        }
                                    }
                                    else if (selectedTabItem?.Content is EditorTab editorTab)
                                    {
                                        filePath = editorTab.FilePath;
                                    }
                                }
                            }
                            
                            // Get cursor position from associated tab if not already set
                            if (cursorPosition < 0 && SelectedTab?.Tag is int lastPos)
                            {
                                cursorPosition = lastPos;
                                Debug.WriteLine($"Found cursor position from tab tag: {cursorPosition}");
                            }
                            
                            // Ensure cursor position is valid
                            if (cursorPosition < 0)
                            {
                                cursorPosition = 0;
                                Debug.WriteLine("Reset negative cursor position to 0");
                            }
                            
                            Debug.WriteLine($"File path for detection: {filePath ?? "unknown"}");
                            Debug.WriteLine($"Using cursor position: {cursorPosition}");
                            
                            // Check if it's a fiction file using our specialized method
                            bool isFictionFile = IsFictionFile(fileContent, filePath);
                            
                            // Dispose old service if it exists
                            if (_currentService != null)
                            {
                                _currentService.Dispose();
                                _currentService = null;
                            }
                            
                            // Create the appropriate service based on content type
                            if (isFictionFile && !string.IsNullOrEmpty(filePath))
                            {
                                // Create a FictionWritingBeta service for fiction content
                                Debug.WriteLine($"Creating FictionWritingBeta service for file: {filePath}");
                                
                                // Get library path - set to parent directory to allow for "../" references
                                string libraryPath = System.IO.Path.GetDirectoryName(filePath);
                                if (!string.IsNullOrEmpty(libraryPath))
                                {
                                    // Get parent directory to allow accessing files with "../" paths
                                    string parentLibraryPath = System.IO.Path.GetDirectoryName(libraryPath);
                                    if (!string.IsNullOrEmpty(parentLibraryPath))
                                    {
                                        libraryPath = parentLibraryPath;
                                        Debug.WriteLine($"Using parent directory as library path for references: {libraryPath}");
                                    }
                                }

                                if (string.IsNullOrEmpty(libraryPath))
                                {
                                    libraryPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                                }
                                
                                // Add debugging to verify paths
                                Debug.WriteLine($"Using library path for references: {libraryPath}");
                                Debug.WriteLine($"File path absolute: {System.IO.Path.GetFullPath(filePath)}");
                                
                                // Clear any existing instance to ensure we get a fresh one with the correct library path
                                FictionWritingBeta.ClearInstance(filePath);
                                FictionWritingBeta.ClearInstance("default"); // Also clear the default instance
                                
                                _currentService = await FictionWritingBeta.GetInstance(
                                    apiKey, 
                                    SelectedModel.Name, 
                                    SelectedModel.Provider, 
                                    null, // Don't pass file path here anymore (will set it separately) 
                                    libraryPath);
                                    
                                // Subscribe to retry event
                                var betaService = _currentService as FictionWritingBeta;
                                if (betaService != null)
                                {
                                    betaService.OnRetryingOverloadedRequest += RetryEventHandler;
                                    
                                    // Set the file path first
                                    Debug.WriteLine($"Setting file path in FictionWritingBeta service: {filePath}");
                                    betaService.SetCurrentFilePath(filePath);
                                    
                                    // Then set cursor position
                                    Debug.WriteLine($"Setting cursor position in FictionWritingBeta service: {cursorPosition}");
                                    betaService.UpdateCursorPosition(cursorPosition);
                                    
                                    // Initialize with content
                                    await betaService.UpdateContentAndInitialize(fileContent);
                                    Debug.WriteLine("Fiction service initialized with content");
                                }
                            }
                            else
                            {
                                // Create a general contextual service
                                Debug.WriteLine("Creating ContextualLangChainService for general content");
                                _currentService = new ContextualLangChainService(apiKey, SelectedModel.Name, SelectedModel.Provider, SelectedModel.IsThinkingMode);
                        await _currentService.UpdateContextAsync(fileContent);
                                Debug.WriteLine("Contextual service initialized with content");
                    }
                }
            }

            // Refresh context if needed
            if (needsContextRefresh && _currentService != null)
            {
                Debug.WriteLine("Refreshing context due to content changes");
                var freshContent = await GetCurrentContent();
                if (!string.IsNullOrEmpty(freshContent))
                        {
                            if (_currentService is FictionWritingBeta fictionService)
                            {
                                Debug.WriteLine("Refreshing context using FictionWritingBeta service");
                                
                                // Update cursor position before refreshing content
                                int cursorPosition = -1;
                                MarkdownTab currentMarkdownTab = null;
                                
                                // First try to get the cursor position directly from any markdown tab that might be open
                                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                                var selectedTabItem = mainWindow?.MainTabControl?.SelectedItem as System.Windows.Controls.TabItem;
                                
                                if (selectedTabItem?.Content is MarkdownTab tabMdTab)
                                {
                                    currentMarkdownTab = tabMdTab;
                                    cursorPosition = tabMdTab.LastKnownCursorPosition;
                                    Debug.WriteLine($"Getting cursor position from current tab's markdown editor for refresh: {cursorPosition}");
                                }
                                
                                // If we couldn't get it that way, try the editor approach
                                if (cursorPosition < 0 && _currentEditor is MarkdownTab refreshMdTab)
                                {
                                    currentMarkdownTab = refreshMdTab;
                                    cursorPosition = refreshMdTab.LastKnownCursorPosition;
                                    Debug.WriteLine($"Getting cursor position from current editor for refresh: {cursorPosition}");
                                }
                                
                                // Still not found? Check the associated tab specifically
                                if (cursorPosition < 0 && SelectedTab?.Tag != null)
                                {
                                    // Try to get from Tag dictionary
                                    if (SelectedTab.Tag is Dictionary<string, object> tagDict &&
                                        tagDict.TryGetValue("CursorPosition", out object posValue) &&
                                        posValue is int lastPos)
                                    {
                                        cursorPosition = lastPos;
                                        Debug.WriteLine($"Getting cursor position from tab tag dictionary for refresh: {cursorPosition}");
                                    }
                                    // Legacy tag handling (simple int)
                                    else if (SelectedTab.Tag is int legacyPos)
                                    {
                                        cursorPosition = legacyPos;
                                        Debug.WriteLine($"Getting cursor position from legacy tab tag for refresh: {cursorPosition}");
                                    }
                                }
                                
                                // Ensure cursor position is valid and non-zero
                                if (cursorPosition <= 0)
                                {
                                    // Try to get the cursor position more aggressively
                                    if (currentMarkdownTab != null)
                                    {
                                        var markdownEditor = currentMarkdownTab.Editor;
                                        if (markdownEditor != null)
                                        {
                                            try
                                            {
                                                // Access the text editor's cursor position directly
                                                cursorPosition = markdownEditor.CaretIndex;
                                                Debug.WriteLine($"Retrieved cursor position directly from editor: {cursorPosition}");
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine($"Error getting CaretIndex: {ex.Message}");
                                            }
                                        }
                                    }
                                    
                                    // If still invalid, set it to a reasonable default based on content length
                                    if (cursorPosition <= 0 && !string.IsNullOrEmpty(freshContent))
                                    {
                                        // Find the first chapter marker as a fallback
                                        int chapterIndex = freshContent.IndexOf("## Chapter");
                                        if (chapterIndex > 0)
                                        {
                                            cursorPosition = chapterIndex + 10; // Position after "## Chapter "
                                            Debug.WriteLine($"Set cursor position to first chapter marker: {cursorPosition}");
                                        }
                                        else
                                        {
                                            // Default to 20% into the content as a last resort
                                            cursorPosition = freshContent.Length / 5;
                                            Debug.WriteLine($"Set default cursor position to 20% into content: {cursorPosition}");
                                        }
                                    }
                                }
                                
                                // Set the cursor position in the service
                                Debug.WriteLine($"Setting cursor position for refresh: {cursorPosition}");
                                fictionService.UpdateCursorPosition(cursorPosition);
                                
                                // Refresh content
                                await fictionService.UpdateContentAndInitialize(freshContent);
                                Debug.WriteLine("Fiction context refreshed successfully");
                                
                                // Double-check that cursor position is still set correctly after refresh
                                Debug.WriteLine($"Re-verifying cursor position after refresh: {cursorPosition}");
                                fictionService.UpdateCursorPosition(cursorPosition);
                            }
                            else
                            {
                                await _currentService.UpdateContextAsync(freshContent);
                                Debug.WriteLine("Context refreshed successfully");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error refreshing context: {ex.Message}");
                    throw new InvalidOperationException($"Failed to process file context: {ex.Message}");
            }

            return _currentService;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetOrCreateService exception: {ex.Message}");
                throw; // Re-throw to be handled by the caller
            }
        }

        private void MarkdownTab_CursorPositionChanged(object sender, int newPosition)
        {
            Debug.WriteLine($"Markdown tab cursor position changed: {newPosition}");
            
            if (sender is MarkdownTab markdownTab)
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
            if (_currentService is FictionWritingBeta betaService)
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
                            Debug.WriteLine($"Cursor moved significantly: {lastPosition} -> {newPosition}");
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

                    var thinkingMessage = new Models.ChatMessage("assistant", "Thinking...")
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider,
                        IsThinking = true
                    };
                    // Add to the correct message collection based on context mode
                    if (originatingTab.IsContextMode)
                    {
                        originatingTab.Messages.Add(thinkingMessage);
                    }
                    else
                    {
                        originatingTab.ChatModeMessages.Add(thinkingMessage);
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
                        if (_currentEditor != null)
                        {
                            Debug.WriteLine($"Current editor file path: {_currentEditor.FilePath}");
                        }
                    }

                    try
                    {
                        var service = await GetOrCreateService();
                        if (service == null)
                        {
                            var activeMessages = originatingTab.IsContextMode ? 
                                originatingTab.Messages : 
                                originatingTab.ChatModeMessages;
                                
                            activeMessages.Remove(thinkingMessage);
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
                        Debug.WriteLine("Service created, processing request...");
                        
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
                    catch (Exception ex)
                    {
                        if (ex is OperationCanceledException)
                        {
                            Debug.WriteLine("Request was cancelled by user");
                            var cancelMessages = originatingTab.IsContextMode ? 
                                originatingTab.Messages : 
                                originatingTab.ChatModeMessages;
                                
                            cancelMessages.Remove(thinkingMessage);
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
                            
                        errorMessages.Remove(thinkingMessage);
                        errorMessages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                        {
                            LastUserMessage = userInput,
                            IsError = true
                        });
                        
                        // If this tab is the currently selected one, update the Messages property
                        if (originatingTab == SelectedTab)
                        {
                            Messages = errorMessages;
                        }
                        return;
                    }

                    var activeResponseMessages = originatingTab.IsContextMode ? 
                        originatingTab.Messages : 
                        originatingTab.ChatModeMessages;
                        
                    activeResponseMessages.Remove(thinkingMessage);
                    activeResponseMessages.Add(new Models.ChatMessage("assistant", response)
                    {
                        ModelName = originatingTab.SelectedModel.Name,
                        Provider = originatingTab.SelectedModel.Provider
                    });
                    
                    // If this tab is the currently selected one, update the Messages property
                    if (originatingTab == SelectedTab)
                    {
                        Messages = activeResponseMessages;
                    }
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
            
            if (selectedTab?.Content is MarkdownTab markdownTab)
            {
                Debug.WriteLine($"Found MarkdownTab: {markdownTab.FilePath}");
                
                // Get and save the current cursor position immediately
                int cursorPosition = markdownTab.LastKnownCursorPosition;
                Debug.WriteLine($"Current cursor position in markdown editor: {cursorPosition}");
                
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
                if (_currentService is FictionWritingBeta fictionService)
                {
                    fictionService.SetCurrentFilePath(markdownTab.FilePath);
                    
                    // Make sure to update the cursor position in the service
                    Debug.WriteLine($"Setting cursor position in service to: {cursorPosition}");
                    fictionService.UpdateCursorPosition(cursorPosition);
                }
                
                // Get the current text from the editor (for unsaved changes)
                string editorContent = markdownTab.Editor?.Text ?? string.Empty;
                
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
                    if (_currentService is FictionWritingBeta fictionService)
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
                if (_currentService != null)
                {
                    await _currentService.UpdateContextAsync(context);
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
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource = null;
            }

            // Remove the thinking message
            if (thinkingMessage != null && Messages.Contains(thinkingMessage))
            {
                Messages.Remove(thinkingMessage);
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
            else if (editorTabObj is MarkdownTab closedMdTab)
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

        private bool IsFictionFile(string content, string filePath)
        {
            Debug.WriteLine($"Checking if file is fiction: {filePath ?? "unknown"}");
            
            // Quick check for common fiction paths
            if (!string.IsNullOrEmpty(filePath))
            {
                var lowerPath = filePath.ToLowerInvariant();
                if (lowerPath.Contains("manuscript") ||
                    lowerPath.Contains("fiction") ||
                    lowerPath.Contains("novel"))
                {
                    Debug.WriteLine($"Fiction file detected based on path: {filePath}");
                    return true;
                }
            }
            
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                Debug.WriteLine("Content is empty, cannot determine file type");
                return false;
            }
            
            // Check for frontmatter
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                Debug.WriteLine("Frontmatter detected, checking for fiction type");
                
                // Find the closing delimiter
                int endIndex = -1;
                
                // Skip the first line (opening delimiter)
                int startIndex = content.IndexOf('\n') + 1;
                if (startIndex < content.Length)
                {
                    // Look for closing delimiter
                    endIndex = content.IndexOf("\n---", startIndex);
                    if (endIndex > startIndex)
                    {
                        // Extract frontmatter content
                        string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
                        Debug.WriteLine($"Extracted frontmatter, length: {frontmatterContent.Length}");
                        
                        // Convert to lowercase for case-insensitive matching
                        frontmatterContent = frontmatterContent.ToLowerInvariant();
                        
                        // Direct checks for fiction tags
                        if (frontmatterContent.Contains("fiction") || 
                            frontmatterContent.Contains("type: fiction") ||
                            frontmatterContent.Contains("type:fiction") ||
                            frontmatterContent.Contains("type: novel") ||
                            frontmatterContent.Contains("type:novel") ||
                            frontmatterContent.Contains("type: story") ||
                            frontmatterContent.Contains("type:story"))
                        {
                            Debug.WriteLine("Fiction file detected based on frontmatter content");
                            return true;
                        }
                        
                        // Parse frontmatter to check for specific keys
                        Dictionary<string, string> frontmatter = new Dictionary<string, string>();
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string line in lines)
                        {
                            // Look for key-value pairs (key: value)
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                string key = line.Substring(0, colonIndex).Trim();
                                string value = line.Substring(colonIndex + 1).Trim();
                                
                                // Store in dictionary - remove hashtag if present
                                if (key.StartsWith("#"))
                                {
                                    key = key.Substring(1);
                                }
                                
                                frontmatter[key] = value;
                                Debug.WriteLine($"Frontmatter key-value: {key}={value}");
                            }
                        }
                        
                        // Check for fiction tag or type: fiction
                        if (frontmatter.ContainsKey("fiction") || 
                            (frontmatter.TryGetValue("type", out string docType) && 
                             (docType.Equals("fiction", StringComparison.OrdinalIgnoreCase) ||
                              docType.Equals("novel", StringComparison.OrdinalIgnoreCase) ||
                              docType.Equals("story", StringComparison.OrdinalIgnoreCase))))
                        {
                            Debug.WriteLine("Fiction file detected based on parsed frontmatter");
                            return true;
                        }
                    }
                }
            }
            
            Debug.WriteLine("Not detected as a fiction file");
            return false;
        }

        private void SubscribeToMarkdownTab(MarkdownTab markdownTab)
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

        private void UnsubscribeFromMarkdownTab(MarkdownTab markdownTab)
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
            if (_currentEditor is MarkdownTab currentMarkdownTab && SelectedTab != null)
            {
                Debug.WriteLine($"Updating association: Markdown tab {currentMarkdownTab.FilePath} -> Chat tab {SelectedTab.Name}");
                _markdownTabAssociations[currentMarkdownTab] = SelectedTab;
            }
        }
    }
}

