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
        private EditorTab _currentEditor;
        private bool _useBetaChains;
        private MusicTab _currentMusicTab;
        private bool _isInitializing = true;
        private string _lastUserMessage;

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
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
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
                    (SendCommand as Commands.RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsContextMode
        {
            get => _isContextMode;
            set
            {
                Debug.WriteLine($"IsContextMode changing from {_isContextMode} to {value}");
                if (_isContextMode != value)
                {
                    _isContextMode = value;
                    OnPropertyChanged();
                    Debug.WriteLine("Calling UpdateMode()");
                    UpdateMode();
                }
            }
        }

        public bool UseBetaChains
        {
            get => _config.UseBetaChains;
            set
            {
                if (_config.UseBetaChains != value)
                {
                    _config.UseBetaChains = value;
                    OnPropertyChanged();
                    // Reset current service to force recreation with new setting
                    DisposeCurrentService();
                }
            }
        }

        public ICommand SendCommand { get; private set; }
        public ICommand ToggleModeCommand { get; private set; }
        public ICommand VerifyDeviceCommand { get; private set; }
        public ICommand RetryCommand { get; private set; }

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
            get => MatrixClient.Instance.IsDeviceVerified;
        }

        public ChatSidebarViewModel()
        {
            _isInitializing = true;
            
            // Initialize collections
            _messages = new ObservableCollection<Models.ChatMessage>();
            _chatModeMessages = new ObservableCollection<Models.ChatMessage>();
            _availableModels = new ObservableCollection<AIModelInfo>();
            
            // Initialize services
            _configService = ServiceLocator.Instance.GetRequiredService<IConfigurationService>();
            _config = _configService.Provider;
            _modelProvider = new ModelProvider(_configService);
            _modelProvider.ModelsChanged += OnModelsChanged;
            
            // Load beta chains setting from configuration
            _useBetaChains = _config.UseBetaChains;
            
            // Initialize commands
            SendCommand = new RelayCommand(async _ => await SendMessageAsync());
            ToggleModeCommand = new RelayCommand(_ => UpdateMode());
            VerifyDeviceCommand = new RelayCommand(async _ => await VerifyDeviceAsync());
            RetryCommand = new RelayCommand(async param => await RetryMessageAsync(param as Models.ChatMessage));

            // Subscribe to messages collection changes
            _messages.CollectionChanged += Messages_CollectionChanged;

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
                AvailableModels.Clear();
                foreach (var model in models)
                {
                    AvailableModels.Add(model);
                }

                // Restore last used model if available
                if (!string.IsNullOrEmpty(Configuration.Instance.LastUsedModel))
                {
                    var lastModel = models.FirstOrDefault(m => m.Name == Configuration.Instance.LastUsedModel);
                    if (lastModel != null)
                    {
                        SelectedModel = lastModel;
                    }
                }

                if (SelectedModel == null && models.Count > 0)
                {
                    SelectedModel = models[0];
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
                        _currentEditor = null;
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
            if (Messages != null && Messages.Any())
            {
                // Find the ScrollViewer in the visual tree
                var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                var chatSidebar = mainWindow?.FindName("ChatSidebar") as Views.ChatSidebar;
                var scrollViewer = chatSidebar?.FindName("MessagesScrollViewer") as ScrollViewer;
                
                if (scrollViewer != null)
                {
                    // Use Dispatcher to ensure we scroll after layout is updated
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        scrollViewer.ScrollToBottom();
                    }), DispatcherPriority.ContextIdle);
                }
            }
        }

        private void UpdateMode()
        {
            try
            {
                Debug.WriteLine($"UpdateMode - Current mode: {(IsContextMode ? "Context" : "Chat")}");
                if (IsContextMode)
                {
                    Debug.WriteLine("Switching to context mode");
                    _currentService = null;
                    Messages = _messages ?? new ObservableCollection<Models.ChatMessage>();
                }
                else
                {
                    Debug.WriteLine("Switching to chat mode");
                    Messages = _chatModeMessages ?? new ObservableCollection<Models.ChatMessage>();
                    if (_generalChatService == null)
                    {
                        var apiKey = GetApiKey(SelectedModel?.Provider ?? AIProvider.Anthropic);
                        Debug.WriteLine($"Creating new GeneralChatService with provider: {SelectedModel?.Provider ?? AIProvider.Anthropic}");
                        _generalChatService = GeneralChatService.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, SelectedModel.IsThinkingMode);
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
                betaService.OnRetryingOverloadedRequest -= RetryEventHandler;
            }
            else if (_currentService is FictionWritingChain chainService)
            {
                chainService.OnRetryingOverloadedRequest -= RetryEventHandler;
            }
            
            _currentService = null;
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
            if (_isInitializing) return null;

            var apiKey = _modelProvider.GetApiKey(SelectedModel.Provider);
            if (string.IsNullOrEmpty(apiKey))
            {
                throw new InvalidOperationException($"No API key configured for {SelectedModel.Provider}");
            }

            // If we're in chat mode, use the general chat service
            if (!IsContextMode)
            {
                if (_generalChatService == null)
                {
                    _generalChatService = GeneralChatService.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, SelectedModel.IsThinkingMode);
                }
                return _generalChatService;
            }

            // Get the selected tab directly from MainWindow
            var mainWindow = Application.Current.MainWindow as Views.MainWindow;
            var selectedTab = mainWindow?.MainTabControl?.SelectedItem as TabItem;
            var selectedContent = selectedTab?.Content;

            if (selectedContent == null)
            {
                Debug.WriteLine("No tab content found");
                return null;
            }

            // If it's an overview tab, use the overview chain
            if (selectedContent is OverviewTab overviewTab)
            {
                Debug.WriteLine("Selected tab is OverviewTab");
                try
                {
                    // Get all projects with their full hierarchy
                    var projects = ProjectTracker.Instance.GetAllProjects();
                    var todos = ToDoTracker.Instance.GetAllTodos();

                    Debug.WriteLine($"Creating OverviewChain with {projects.Count} projects and {todos.Count} todos");
                    return OverviewChain.GetInstance(
                        apiKey, 
                        SelectedModel.Name, 
                        SelectedModel.Provider,
                        projects,
                        todos
                    );
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating OverviewChain: {ex.Message}");
                    throw;
                }
            }

            // Get the file path and content
            string filePath = null;
            string fileContent = null;
            bool isMarkdown = false;
            bool isReference = false;
            bool isScreenplay = false;
            bool isFiction = false;

            if (selectedContent is EditorTab editorTab)
            {
                Debug.WriteLine($"Selected tab is EditorTab: {editorTab.FilePath}");
                _currentEditor = editorTab;
                filePath = editorTab.FilePath;
            }
            else if (selectedContent is MarkdownTab markdownTab)
            {
                Debug.WriteLine($"Selected tab is MarkdownTab: {markdownTab.FilePath}");
                filePath = markdownTab.FilePath;
            }
            else if (selectedContent is ProjectTab projectTab)
            {
                Debug.WriteLine($"Selected tab is ProjectTab: {projectTab.FilePath}");
                filePath = projectTab.FilePath;
                
                // Create ProjectChain for project tabs
                Debug.WriteLine("Creating ProjectChain service");
                _currentService = await ProjectChain.GetInstanceAsync(apiKey, SelectedModel.Name, SelectedModel.Provider, projectTab.Project);
                return _currentService;
            }
            else if (_currentMusicTab != null)
            {
                var selectedItems = _currentMusicTab.ContentListView_Control?.SelectedItems;
                if (selectedItems != null && selectedItems.Count > 0)
                {
                    var selectedTracks = selectedItems.Cast<MusicItem>()
                        .Select(item => $"{item.Name} by {item.ArtistName} from {item.Album}")
                        .ToList();
                    _currentService = MusicChain.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, string.Join(", ", selectedTracks));
                    return _currentService;
                }
            }
            else if (selectedContent is ToDoTab todoTab)
            {
                Debug.WriteLine($"Selected tab is ToDoTab: {todoTab.FilePath}");
                filePath = todoTab.FilePath;
                
                // Create ToDoChain for todo tabs
                Debug.WriteLine("Creating ToDoChain service");
                _currentService = ToDoChain.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, todoTab.Todos.ToList());
                return _currentService;
            }

            if (!string.IsNullOrEmpty(filePath))
            {
                // Get file content
                try {
                    fileContent = await File.ReadAllTextAsync(filePath);
                } catch (Exception ex) {
                    Debug.WriteLine($"Error reading file content: {ex.Message}");
                    fileContent = string.Empty;
                }

                // Detect file types
                if (!string.IsNullOrEmpty(fileContent))
                {
                    var normalizedContent = fileContent.Replace("\r\n", "\n");
                    var contentLines = normalizedContent.Split('\n')
                        .Select(line => line.Trim())
                        .Where(line => !string.IsNullOrEmpty(line))
                        .ToList();
                    
                    if (contentLines.Any())
                    {
                        var firstNonEmptyLine = contentLines[0];
                        Debug.WriteLine($"First non-empty line: '{firstNonEmptyLine}'");

                        // Check first few lines for tags
                        var firstFewLines = contentLines.Take(5).ToList();
                        
                        // Check for frontmatter if this is a MarkdownTab
                        if (selectedTab?.Content is MarkdownTab markdownTab && markdownTab.HasFrontmatter())
                        {
                            // Check for type field in frontmatter
                            string documentType = markdownTab.GetFrontmatterValue("type");
                            if (!string.IsNullOrEmpty(documentType))
                            {
                                // Set flags based on type value
                                switch (documentType.ToLowerInvariant())
                                {
                                    case "fiction":
                                        isFiction = true;
                                        break;
                                    case "reference":
                                    case "ref":
                                        isReference = true;
                                        break;
                                    case "screenplay":
                                        isScreenplay = true;
                                        break;
                                }
                                Debug.WriteLine($"Document type from frontmatter: {documentType}");
                            }
                            else
                            {
                                // Maintain backward compatibility with existing boolean flags
                                // Check if the frontmatter contains the fiction tag
                                isFiction = isFiction || 
                                           markdownTab.GetFrontmatterValue("fiction") != null;
                                           
                                // Check if the frontmatter contains reference tags
                                isReference = isReference || 
                                             markdownTab.GetFrontmatterValue("reference") != null ||
                                             markdownTab.GetFrontmatterValue("ref") != null ||
                                             markdownTab.GetFrontmatterKeys().Any(k => k.StartsWith("ref "));
                                             
                                // Check if the frontmatter contains screenplay tag
                                isScreenplay = isScreenplay || 
                                              markdownTab.GetFrontmatterValue("screenplay") != null;
                            }
                                          
                            Debug.WriteLine("Checked frontmatter for tags");
                        }
                        
                        // Detect various file types from content
                        isFiction = isFiction || 
                                   filePath?.EndsWith(".fiction", StringComparison.OrdinalIgnoreCase) == true ||
                                   firstFewLines.Any(line => line.Equals("#fiction", StringComparison.OrdinalIgnoreCase));

                        isReference = isReference || 
                                     filePath?.EndsWith(".reference", StringComparison.OrdinalIgnoreCase) == true ||
                                     firstFewLines.Any(line => line.Equals("#reference", StringComparison.OrdinalIgnoreCase) ||
                                                             line.Equals("#ref", StringComparison.OrdinalIgnoreCase) ||
                                                             line.StartsWith("#ref data:", StringComparison.OrdinalIgnoreCase));

                        isScreenplay = isScreenplay || 
                                      filePath?.EndsWith(".screenplay", StringComparison.OrdinalIgnoreCase) == true ||
                                      firstFewLines.Any(line => line.Equals("#screenplay", StringComparison.OrdinalIgnoreCase));

                        isMarkdown = Path.GetExtension(filePath)?.ToLower() == ".md";
                        // Note: Fiction files can be markdown files, so we don't need to exclude them

                        Debug.WriteLine($"File type detection: isFiction={isFiction}, isReference={isReference}, isScreenplay={isScreenplay}, isMarkdown={isMarkdown}");
                    }
                }
            }

            var libraryPath = _config.LibraryPath;
            Debug.WriteLine($"Library path from config: {libraryPath}");

            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new InvalidOperationException("Library path is not configured. Please set it in the settings.");
            }

            if (isMarkdown)
            {
                Debug.WriteLine("Processing markdown file");
                if (isReference)
                {
                    Debug.WriteLine("Creating ReferenceChain service");
                    _currentService = await ReferenceChain.GetInstanceAsync(apiKey, SelectedModel.Name, SelectedModel.Provider, fileContent ?? string.Empty);
                    return _currentService;
                }
                else if (isScreenplay)
                {
                    Debug.WriteLine("Creating ScreenplayChain service");
                    _currentService = ScreenplayChain.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, fileContent ?? string.Empty);
                }
                else if (isFiction)
                {
                    if (UseBetaChains)
                    {
                        Debug.WriteLine("Creating FictionWritingBeta service");
                        Debug.WriteLine($"File path: {filePath}");
                        Debug.WriteLine($"File content length: {fileContent?.Length ?? 0}");
                        
                        _currentService = await FictionWritingBeta.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, filePath, libraryPath);
                        if (_currentService is FictionWritingBeta fictionBeta && !string.IsNullOrEmpty(fileContent))
                        {
                            Debug.WriteLine($"Initializing FictionWritingBeta with content length: {fileContent.Length}");
                            await fictionBeta.UpdateContentAndInitialize(fileContent);
                        }
                        else
                        {
                            Debug.WriteLine($"Could not initialize FictionWritingBeta. _currentService is FictionWritingBeta: {_currentService is FictionWritingBeta}, fileContent is null or empty: {string.IsNullOrEmpty(fileContent)}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("Creating FictionWritingChain service");
                        _currentService = await FictionWritingChain.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, fileContent ?? string.Empty);
                    }
                }

                // Subscribe to cursor position changes if it's a MarkdownTab
                if (selectedContent is MarkdownTab markdownTab)
                {
                    Debug.WriteLine("Subscribing to cursor position changes");
                    markdownTab.CursorPositionChanged -= MarkdownTab_CursorPositionChanged;
                    markdownTab.CursorPositionChanged += MarkdownTab_CursorPositionChanged;
                    
                    if (_currentService is FictionWritingBeta fictionBetaService)
                    {
                        Debug.WriteLine($"Updating initial cursor position: {markdownTab.LastKnownCursorPosition}");
                        fictionBetaService.UpdateCursorPosition(markdownTab.LastKnownCursorPosition);
                    }
                }
                
                // Return if we've created a service
                if (_currentService != null)
                {
                    return _currentService;
                }
            }
            else if (isFiction)
            {
                Debug.WriteLine("Creating FictionWritingBeta service");
                Debug.WriteLine($"File path: {filePath}");
                Debug.WriteLine($"File content length: {fileContent?.Length ?? 0}");
                
                _currentService = await FictionWritingBeta.GetInstance(apiKey, SelectedModel.Name, SelectedModel.Provider, filePath, libraryPath);
                if (_currentService is FictionWritingBeta fictionBeta && !string.IsNullOrEmpty(fileContent))
                {
                    Debug.WriteLine($"Initializing FictionWritingBeta with content length: {fileContent.Length}");
                    await fictionBeta.UpdateContentAndInitialize(fileContent);
                }
                else
                {
                    Debug.WriteLine($"Could not initialize FictionWritingBeta. _currentService is FictionWritingBeta: {_currentService is FictionWritingBeta}, fileContent is null or empty: {string.IsNullOrEmpty(fileContent)}");
                }
            }

            if (!string.IsNullOrEmpty(fileContent) && _currentService != null)
            {
                await _currentService.UpdateContextAsync(fileContent);
            }
            return _currentService;
        }

        private void MarkdownTab_CursorPositionChanged(object sender, int newPosition)
        {
            if (_currentService is FictionWritingBeta betaService)
            {
                Debug.WriteLine($"Updating cursor position in FictionWritingBeta: {newPosition}");
                betaService.UpdateCursorPosition(newPosition);
            }
        }

        private async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText)) return;

            var userInput = InputText;
            InputText = string.Empty;
            _lastUserMessage = userInput;

            try
            {
                var apiKey = GetApiKey(SelectedModel.Provider);
                if (string.IsNullOrEmpty(apiKey) && SelectedModel.Provider != AIProvider.Ollama)
                {
                    MessageBox.Show($"Please configure your {SelectedModel.Provider} API key in settings.", "Configuration Required", MessageBoxButton.OK, MessageBoxImage.Information);
                    InputText = userInput;  // Restore the input text
                    return;
                }

                // Add user message after API key check
                var userMessage = new Models.ChatMessage("user", userInput)
                {
                    ModelName = SelectedModel.Name,
                    Provider = SelectedModel.Provider
                };
                Messages.Add(userMessage);

                var thinkingMessage = new Models.ChatMessage("assistant", "Thinking...")
                {
                    ModelName = SelectedModel.Name,
                    Provider = SelectedModel.Provider
                };
                Messages.Add(thinkingMessage);

                string content = null;
                string response = null;

                if (IsContextMode)
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
                        Messages.Remove(thinkingMessage);
                        Messages.Add(new Models.ChatMessage("system", "Failed to create language service. Please check your settings and try again.", true)
                        {
                            LastUserMessage = userInput
                        });
                        return;
                    }
                    Debug.WriteLine("Service created, processing request...");
                    response = await service.ProcessRequest(content, userInput);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing request: {ex}");
                    Messages.Remove(thinkingMessage);
                    Messages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                    {
                        LastUserMessage = userInput
                    });
                    return;
                }

                Messages.Remove(thinkingMessage);
                Messages.Add(new Models.ChatMessage("assistant", response)
                {
                    ModelName = SelectedModel.Name,
                    Provider = SelectedModel.Provider
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in SendMessageAsync: {ex}");
                Messages.Add(new Models.ChatMessage("system", $"Error: {ex.Message}", true)
                {
                    LastUserMessage = userInput
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
            return provider switch
            {
                AIProvider.OpenAI => _config.OpenAIApiKey,
                AIProvider.Anthropic => _config.AnthropicApiKey,
                AIProvider.XAI => _config.XAIApiKey,
                AIProvider.Ollama => string.Empty,  // Ollama doesn't need an API key
                _ => throw new ArgumentException($"Unsupported provider: {provider}")
            };
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
            var mainWindow = Application.Current.MainWindow as Views.MainWindow;
            var selectedTab = mainWindow?.MainTabControl?.SelectedItem as System.Windows.Controls.TabItem;

            if (selectedTab?.Content is OverviewTab overviewTab)
            {
                Debug.WriteLine("OverviewTab detected - gathering project and todo data");
                var projects = ProjectTracker.Instance.GetAllProjects();
                var todos = ToDoTracker.Instance.GetAllTodos();
                
                var summary = new StringBuilder();
                summary.AppendLine("# Overview");
                summary.AppendLine();
                
                summary.AppendLine("## Projects");
                foreach (var project in projects)
                {
                    summary.AppendLine($"- {project.Title} ({project.Status})");
                    if (!string.IsNullOrEmpty(project.Goal))
                        summary.AppendLine($"  Goal: {project.Goal}");
                    if (project.StartDate.HasValue)
                        summary.AppendLine($"  Started: {project.StartDate:d}");
                    if (project.DueDate.HasValue)
                        summary.AppendLine($"  Due: {project.DueDate:d}");
                    if (project.CompletedDate.HasValue)
                        summary.AppendLine($"  Completed: {project.CompletedDate:d}");
                    
                    if (project.LogEntries?.Any() == true)
                    {
                        summary.AppendLine("  Recent Log Entries:");
                        foreach (var entry in project.LogEntries.OrderByDescending(e => e.Timestamp).Take(3))
                        {
                            summary.AppendLine($"  - [{entry.Timestamp:g}] {entry.Content}");
                        }
                    }
                    
                    if (project.Dependencies?.Any() == true)
                    {
                        summary.AppendLine("  Dependencies:");
                        foreach (var dep in project.Dependencies)
                        {
                            // Try to find the dependent project to get its title
                            var dependentProject = projects.FirstOrDefault(p => p.FilePath == dep.FilePath);
                            var depTitle = dependentProject?.Title ?? Path.GetFileNameWithoutExtension(dep.FilePath);
                            summary.AppendLine($"  - {depTitle} ({(dep.IsHardDependency ? "Hard" : "Soft")})");
                        }
                    }
                    
                    if (project.Tasks?.Any() == true)
                    {
                        summary.AppendLine("  Tasks:");
                        foreach (var task in project.Tasks)
                        {
                            summary.AppendLine($"  - [{(task.IsCompleted ? "x" : " ")}] {task.Title}");
                        }
                    }
                    summary.AppendLine();
                }
                
                summary.AppendLine("## ToDos");
                // Group todos by their file name (category)
                var todosByFile = todos.GroupBy(t => Path.GetFileNameWithoutExtension(t.FilePath ?? "Uncategorized"));
                foreach (var group in todosByFile)
                {
                    summary.AppendLine($"\n### {group.Key}");
                    foreach (var todo in group)
                    {
                        summary.AppendLine($"- [{(todo.IsCompleted ? "x" : " ")}] {todo.Title}");
                        if (!string.IsNullOrEmpty(todo.Description))
                            summary.AppendLine($"  {todo.Description}");
                        if (todo.StartDate.HasValue)
                            summary.AppendLine($"  Started: {todo.StartDate:d}");
                        if (todo.DueDate.HasValue)
                            summary.AppendLine($"  Due: {todo.DueDate:d}");
                        if (todo.CompletedDate.HasValue)
                            summary.AppendLine($"  Completed: {todo.CompletedDate:d}");
                        if (todo.Tags?.Any() == true)
                            summary.AppendLine($"  Tags: {string.Join(", ", todo.Tags)}");
                        
                        if (todo.SubTasks?.Any() == true)
                        {
                            foreach (var subtask in todo.SubTasks)
                            {
                                summary.AppendLine($"  - [{(subtask.IsCompleted ? "x" : " ")}] {subtask.Title}");
                            }
                        }
                        summary.AppendLine();
                    }
                }

                Debug.WriteLine("Overview data gathered successfully");
                return summary.ToString();
            }
            else if (selectedTab?.Content is MarkdownTab markdownTab)
            {
                var content = markdownTab.Editor?.Text;
                if (!string.IsNullOrEmpty(content))
                {
                    Debug.WriteLine($"Retrieved content from MarkdownTab: {markdownTab.FilePath}");
                    return $"#file:{markdownTab.FilePath}\n{content}";
                }
                Debug.WriteLine("Warning: MarkdownTab editor content is empty");
                return string.Empty;
            }
            else if (selectedTab?.Content is EditorTab editorTab)
            {
                var content = editorTab.GetContent();
                if (!string.IsNullOrEmpty(content))
                {
                    Debug.WriteLine($"Retrieved content from EditorTab: {editorTab.FilePath}");
                    return $"#file:{editorTab.FilePath}\n{content}";
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
    }
} 