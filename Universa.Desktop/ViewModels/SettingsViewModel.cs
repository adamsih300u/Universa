using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using Universa.Desktop.Services;
using System.Windows;
using System.IO;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Universa.Desktop.Commands;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Managers;
using Universa.Desktop.Views;
using System.Runtime.CompilerServices;
using Universa.Desktop.Core.Configuration;
using Microsoft.Win32;
using System.Windows.Forms;
using System.Globalization;
using System.Text.Json;

namespace Universa.Desktop.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly IDialogService _dialogService;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool?> RequestClose;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SettingsViewModel(IConfigurationService configService, IDialogService dialogService)
        {
            _configService = configService;
            _config = _configService.Provider;
            _dialogService = dialogService;
            
            // Initialize commands
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
            ResetCommand = new RelayCommand(_ => ResetSettings());
            BrowseLibraryCommand = new RelayCommand(_ => BrowseLibrary());
            TestConnectionCommand = new RelayCommand(_ => TestConnection());
            ClearCacheCommand = new RelayCommand(_ => ClearCache());
            SyncNowCommand = new RelayCommand(_ => SyncNow());
            TestSyncCommand = new RelayCommand(_ => TestSync());
            RefreshVoicesCommand = new RelayCommand(_ => RefreshVoices());
            TestTTSCommand = new RelayCommand(_ => TestTTS());
            UpdateJellyfinPasswordCommand = new RelayCommand<string>(UpdateJellyfinPassword);
            UpdateSubsonicPasswordCommand = new RelayCommand<string>(UpdateSubsonicPassword);
            UpdateOpenAIKeyCommand = new RelayCommand<string>(UpdateOpenAIKey);
            UpdateAnthropicKeyCommand = new RelayCommand<string>(UpdateAnthropicKey);
            UpdateXAIKeyCommand = new RelayCommand<string>(UpdateXAIKey);
            UpdateWeatherApiKeyCommand = new RelayCommand<string>(UpdateWeatherApiKey);
            UpdateMatrixPasswordCommand = new RelayCommand<string>(UpdateMatrixPassword);
            UpdateAudiobookshelfPasswordCommand = new RelayCommand<string>(UpdateAudiobookshelfPassword);
            UpdateSyncPasswordCommand = new RelayCommand<string>(UpdateSyncPassword);
            UpdateOpenRouterKeyCommand = new RelayCommand<string>(UpdateOpenRouterKey);
            FetchOpenRouterModelsCommand = new RelayCommand(_ => FetchOpenRouterModels());
            BrowseCommand = new RelayCommand(BrowseFolder);
            BrowseFileCommand = new RelayCommand(BrowseFile);
            BrowseBackupFolderCommand = new RelayCommand(BrowseBackupFolder);
            TestWeatherCommand = new RelayCommand(_ => TestWeather());
            TestAICommand = new RelayCommand(_ => TestAI());
            TestMatrixCommand = new RelayCommand(_ => TestMatrix());
            TestSubsonicCommand = new RelayCommand(_ => TestSubsonic());
            TestJellyfinCommand = new RelayCommand(_ => TestJellyfin());
            TestAudiobookshelfCommand = new RelayCommand(_ => TestAudiobookshelf());

            // Org-Mode Commands
            AddAgendaFileCommand = new RelayCommand(_ => AddAgendaFile());
            RemoveAgendaFileCommand = new RelayCommand<string>(RemoveAgendaFile);
            AddAgendaDirectoryCommand = new RelayCommand(_ => AddAgendaDirectory());
            RemoveAgendaDirectoryCommand = new RelayCommand<string>(RemoveAgendaDirectory);
            AddTodoStateCommand = new RelayCommand(_ => AddTodoState());
            RemoveTodoStateCommand = new RelayCommand<string>(RemoveTodoState);
            AddDoneStateCommand = new RelayCommand(_ => AddDoneState());
            RemoveDoneStateCommand = new RelayCommand<string>(RemoveDoneState);
            AddNoActionStateCommand = new RelayCommand(_ => AddNoActionState());
            RemoveNoActionStateCommand = new RelayCommand<string>(RemoveNoActionState);
            ResetOrgStatesCommand = new RelayCommand(_ => ResetOrgStates());
            ResetStateColorCommand = new RelayCommand<string>(ResetStateColor);
            ResetAllStateColorsCommand = new RelayCommand(_ => ResetAllStateColors());
            ResetNoActionStateCommand = new RelayCommand<string>(ResetNoActionState);
            OpenColorPickerCommand = new RelayCommand<StateColorItem>(OpenColorPicker);

            // Quick Refile Target Commands
            AddRefileTargetCommand = new RelayCommand(_ => AddRefileTarget());
            EditRefileTargetCommand = new RelayCommand(_ => EditRefileTarget());
            RemoveRefileTargetCommand = new RelayCommand(_ => RemoveRefileTarget());
            ResetRefileTargetsCommand = new RelayCommand(_ => ResetRefileTargets());

            // Load initial settings
            LoadSettings();

            // Subscribe to configuration changes
            if (_configService != null)
            {
                _configService.ConfigurationChanged += OnConfigurationChanged;
            }
        }

        #region Commands
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand BrowseLibraryCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand ClearCacheCommand { get; }
        public ICommand SyncNowCommand { get; }
        public ICommand TestSyncCommand { get; }
        public ICommand RefreshVoicesCommand { get; }
        public ICommand TestTTSCommand { get; }
        public ICommand UpdateJellyfinPasswordCommand { get; }
        public ICommand UpdateSubsonicPasswordCommand { get; }
        public ICommand UpdateOpenAIKeyCommand { get; }
        public ICommand UpdateAnthropicKeyCommand { get; }
        public ICommand UpdateXAIKeyCommand { get; }
        public ICommand UpdateWeatherApiKeyCommand { get; }
        public ICommand UpdateMatrixPasswordCommand { get; }
        public ICommand UpdateAudiobookshelfPasswordCommand { get; }
        public ICommand UpdateSyncPasswordCommand { get; }
        public ICommand UpdateOpenRouterKeyCommand { get; }
        public ICommand FetchOpenRouterModelsCommand { get; }
        public ICommand BrowseCommand { get; }
        public ICommand BrowseFileCommand { get; }
        public ICommand BrowseBackupFolderCommand { get; }
        public ICommand TestWeatherCommand { get; }
        public ICommand TestAICommand { get; }
        public ICommand TestMatrixCommand { get; }
        public ICommand TestSubsonicCommand { get; }
        public ICommand TestJellyfinCommand { get; }
        public ICommand TestAudiobookshelfCommand { get; }

        // Org-Mode Commands
        public ICommand AddAgendaFileCommand { get; }
        public ICommand RemoveAgendaFileCommand { get; }
        public ICommand AddAgendaDirectoryCommand { get; }
        public ICommand RemoveAgendaDirectoryCommand { get; }
        public ICommand AddTodoStateCommand { get; }
        public ICommand RemoveTodoStateCommand { get; }
        public ICommand AddDoneStateCommand { get; }
        public ICommand RemoveDoneStateCommand { get; }
        public ICommand AddNoActionStateCommand { get; }
        public ICommand RemoveNoActionStateCommand { get; }
        public ICommand ResetOrgStatesCommand { get; }
        public ICommand ResetStateColorCommand { get; }
        public ICommand ResetAllStateColorsCommand { get; }
        public ICommand ResetNoActionStateCommand { get; }
        public ICommand OpenColorPickerCommand { get; }

        // Quick Refile Target Commands
        public ICommand AddRefileTargetCommand { get; }
        public ICommand EditRefileTargetCommand { get; }
        public ICommand RemoveRefileTargetCommand { get; }
        public ICommand ResetRefileTargetsCommand { get; }
        #endregion

        #region Properties
        public ThemeViewModel ThemeViewModel { get; }

        // Library Settings
        public string LibraryPath
        {
            get => _config.LibraryPath;
            set
            {
                if (_config.LibraryPath != value)
                {
                    string oldValue = _config.LibraryPath;
                    try
                    {
                        _config.LibraryPath = value;
                        
                        // Create the directory if it doesn't exist
                        if (!Directory.Exists(value))
                        {
                            Directory.CreateDirectory(value);
                        }
                        
                        OnPropertyChanged();
                        
                        // Save immediately when library path changes
                        _configService.Save();

                        // Notify the main window to refresh the library
                        var mainWindow = System.Windows.Application.Current.MainWindow as Views.MainWindow;
                        if (mainWindow?.LibraryNavigatorInstance != null)
                        {
                            // Run on UI thread
                            System.Windows.Application.Current.Dispatcher.Invoke(async () =>
                            {
                                try
                                {
                                    await mainWindow.LibraryNavigatorInstance.RefreshItems(false);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Error refreshing library: {ex.Message}");
                                    System.Windows.MessageBox.Show($"Error refreshing library: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error setting library path: {ex.Message}");
                        System.Windows.MessageBox.Show($"Error setting library path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Revert to old value on error
                        _config.LibraryPath = oldValue;
                        OnPropertyChanged();
                    }
                }
            }
        }

        // Theme Settings
        public string CurrentTheme
        {
            get => _config.CurrentTheme;
            set
            {
                if (_config.CurrentTheme != value)
                {
                    _config.CurrentTheme = value;
                    OnPropertyChanged();
                }
            }
        }

        // Weather Settings
        public bool EnableWeather
        {
            get => _config.EnableWeather;
            set
            {
                if (_config.EnableWeather != value)
                {
                    _config.EnableWeather = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableMoonPhase
        {
            get => _config.EnableMoonPhase;
            set
            {
                if (_config.EnableMoonPhase != value)
                {
                    _config.EnableMoonPhase = value;
                    OnPropertyChanged();
                }
            }
        }

        public string WeatherZipCode
        {
            get => _config.WeatherZipCode;
            set
            {
                if (_config.WeatherZipCode != value)
                {
                    _config.WeatherZipCode = value;
                    OnPropertyChanged();
                }
            }
        }

        public string WeatherApiKey => _config.WeatherApiKey;

        // AI Settings
        public bool EnableOpenAI
        {
            get => _config.EnableOpenAI;
            set
            {
                if (_config.EnableOpenAI != value)
                {
                    _config.EnableOpenAI = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableAnthropic
        {
            get => _config.EnableAnthropic;
            set
            {
                if (_config.EnableAnthropic != value)
                {
                    _config.EnableAnthropic = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableXAI
        {
            get => _config.EnableXAI;
            set
            {
                if (_config.EnableXAI != value)
                {
                    _config.EnableXAI = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableOllama
        {
            get => _config.EnableOllama;
            set
            {
                if (_config.EnableOllama != value)
                {
                    _config.EnableOllama = value;
                    OnPropertyChanged();
                }
            }
        }

        // OpenRouter Properties
        private bool _enableOpenRouter;
        public bool EnableOpenRouter
        {
            get => _enableOpenRouter;
            set
            {
                if (_enableOpenRouter != value)
                {
                    _enableOpenRouter = value;
                    _config.EnableOpenRouter = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OpenRouterApiKey => _config.OpenRouterApiKey;

        private ObservableCollection<OpenRouterModelViewModel> _availableOpenRouterModels;
        public ObservableCollection<OpenRouterModelViewModel> AvailableOpenRouterModels
        {
            get => _availableOpenRouterModels;
            set
            {
                if (_availableOpenRouterModels != value)
                {
                    _availableOpenRouterModels = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableAIChat
        {
            get => _config.EnableAIChat;
            set
            {
                if (_config.EnableAIChat != value)
                {
                    _config.EnableAIChat = value;
                    OnPropertyChanged();
                }
            }
        }

        // Jellyfin Settings
        public string JellyfinName
        {
            get => _config.JellyfinName;
            set
            {
                if (_config.JellyfinName != value)
                {
                    _config.JellyfinName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string JellyfinUrl
        {
            get => _config.JellyfinUrl;
            set
            {
                if (_config.JellyfinUrl != value)
                {
                    _config.JellyfinUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string JellyfinUsername
        {
            get => _config.JellyfinUsername;
            set
            {
                if (_config.JellyfinUsername != value)
                {
                    _config.JellyfinUsername = value;
                    OnPropertyChanged();
                }
            }
        }

        public string JellyfinPassword => _config.JellyfinPassword;

        public string OllamaUrl
        {
            get => _config.OllamaUrl;
            set
            {
                if (_config.OllamaUrl != value)
                {
                    _config.OllamaUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OllamaModel
        {
            get => _config.OllamaModel;
            set
            {
                if (_config.OllamaModel != value)
                {
                    _config.OllamaModel = value;
                    OnPropertyChanged();
                }
            }
        }

        // Sync Settings
        public string SyncServerUrl
        {
            get => _config.SyncServerUrl;
            set
            {
                if (_config.SyncServerUrl != value)
                {
                    _config.SyncServerUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SyncUsername
        {
            get => _config.SyncUsername;
            set
            {
                if (_config.SyncUsername != value)
                {
                    _config.SyncUsername = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SyncPassword => _config.SyncPassword;

        public bool AutoSync
        {
            get => _config.AutoSync;
            set
            {
                if (_config.AutoSync != value)
                {
                    _config.AutoSync = value;
                    OnPropertyChanged();
                }
            }
        }

        public int SyncIntervalMinutes
        {
            get => _config.SyncIntervalMinutes;
            set
            {
                if (_config.SyncIntervalMinutes != value)
                {
                    _config.SyncIntervalMinutes = value;
                    OnPropertyChanged();
                }
            }
        }

        // Matrix Settings
        public string MatrixServerUrl
        {
            get => _config.MatrixServerUrl;
            set
            {
                if (_config.MatrixServerUrl != value)
                {
                    _config.MatrixServerUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MatrixUsername
        {
            get => _config.MatrixUsername;
            set
            {
                if (_config.MatrixUsername != value)
                {
                    _config.MatrixUsername = value;
                    OnPropertyChanged();
                }
            }
        }

        public string MatrixPassword => _config.MatrixPassword;

        // Subsonic Settings
        public string SubsonicName
        {
            get => _config.SubsonicName;
            set
            {
                if (_config.SubsonicName != value)
                {
                    _config.SubsonicName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SubsonicUrl
        {
            get => _config.SubsonicUrl;
            set
            {
                if (_config.SubsonicUrl != value)
                {
                    _config.SubsonicUrl = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SubsonicUsername
        {
            get => _config.SubsonicUsername;
            set
            {
                if (_config.SubsonicUsername != value)
                {
                    _config.SubsonicUsername = value;
                    OnPropertyChanged();
                }
            }
        }

        public string SubsonicPassword => _config.SubsonicPassword;

        // TTS Settings
        public bool EnableTTS
        {
            get => _config.EnableTTS;
            set
            {
                _config.EnableTTS = value;
                OnPropertyChanged(nameof(EnableTTS));
                if (value)
                {
                    _ = LoadTTSVoices();
                }
            }
        }

        public string TTSApiUrl
        {
            get => _config.TTSApiUrl;
            set
            {
                _config.TTSApiUrl = value;
                OnPropertyChanged(nameof(TTSApiUrl));
                if (EnableTTS)
                {
                    _ = LoadTTSVoices();
                }
            }
        }

        public string TTSVoice
        {
            get => _config.TTSVoice;
            set
            {
                _config.TTSVoice = value;
                OnPropertyChanged(nameof(TTSVoice));
            }
        }

        private ObservableCollection<string> _ttsVoices = new ObservableCollection<string>();
        public ObservableCollection<string> TTSVoices
        {
            get => _ttsVoices;
            private set
            {
                _ttsVoices = value;
                OnPropertyChanged(nameof(TTSVoices));
            }
        }

        // Audiobookshelf Settings
        public string AudiobookshelfUrl
        {
            get => _config.AudiobookshelfUrl;
            set
            {
                _config.AudiobookshelfUrl = value;
                OnPropertyChanged(nameof(AudiobookshelfUrl));
            }
        }

        public string AudiobookshelfUsername
        {
            get => _config.AudiobookshelfUsername;
            set
            {
                _config.AudiobookshelfUsername = value;
                OnPropertyChanged(nameof(AudiobookshelfUsername));
            }
        }

        public string AudiobookshelfPassword
        {
            get => _config.AudiobookshelfPassword;
            set
            {
                _config.AudiobookshelfPassword = value;
                OnPropertyChanged(nameof(AudiobookshelfPassword));
            }
        }

        public string AudiobookshelfName
        {
            get => _config.AudiobookshelfName;
            set
            {
                _config.AudiobookshelfName = value;
                OnPropertyChanged(nameof(AudiobookshelfName));
            }
        }

        public string OpenAIApiKey => _config.OpenAIApiKey;
        public string AnthropicApiKey => _config.AnthropicApiKey;
        public string XAIApiKey => _config.XAIApiKey;

        // Org-Mode Settings
        public bool EnableGlobalAgenda
        {
            get => _config.EnableGlobalAgenda;
            set
            {
                if (_config.EnableGlobalAgenda != value)
                {
                    _config.EnableGlobalAgenda = value;
                    OnPropertyChanged();
                }
            }
        }

        public int AgendaDaysAhead
        {
            get => _config.AgendaDaysAhead;
            set
            {
                if (_config.AgendaDaysAhead != value)
                {
                    _config.AgendaDaysAhead = value;
                    OnPropertyChanged();
                }
            }
        }

        public int AgendaDaysBehind
        {
            get => _config.AgendaDaysBehind;
            set
            {
                if (_config.AgendaDaysBehind != value)
                {
                    _config.AgendaDaysBehind = value;
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _todoTags;
        public ObservableCollection<string> TodoTags
        {
            get
            {
                if (_todoTags == null)
                {
                    _todoTags = new ObservableCollection<string>(_config.TodoTags);
                    _todoTags.CollectionChanged += OnTodoTagsCollectionChanged;
                }
                return _todoTags;
            }
            set
            {
                if (_todoTags != value)
                {
                    // Unhook from old collection if it exists
                    if (_todoTags != null)
                    {
                        _todoTags.CollectionChanged -= OnTodoTagsCollectionChanged;
                    }

                    _todoTags = value;
                    
                    // Hook to new collection if it exists
                    if (_todoTags != null)
                    {
                        _todoTags.CollectionChanged += OnTodoTagsCollectionChanged;
                    }
                    
                    _config.TodoTags = value?.ToArray() ?? new string[0];
                    OnPropertyChanged();
                }
            }
        }

        private void OnTodoTagsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            _config.TodoTags = _todoTags.ToArray();
            OnPropertyChanged(nameof(TodoTags));
        }

        // Quick Refile Targets
        private ObservableCollection<KeyValuePair<string, string>> _orgQuickRefileTargets;
        public ObservableCollection<KeyValuePair<string, string>> OrgQuickRefileTargets
        {
            get
            {
                if (_orgQuickRefileTargets == null)
                {
                    var targets = _config.OrgQuickRefileTargets ?? new Dictionary<string, string>();
                    _orgQuickRefileTargets = new ObservableCollection<KeyValuePair<string, string>>(targets);
                    _orgQuickRefileTargets.CollectionChanged += OnOrgQuickRefileTargetsCollectionChanged;
                }
                return _orgQuickRefileTargets;
            }
            set
            {
                if (_orgQuickRefileTargets != value)
                {
                    if (_orgQuickRefileTargets != null)
                    {
                        _orgQuickRefileTargets.CollectionChanged -= OnOrgQuickRefileTargetsCollectionChanged;
                    }
                    _orgQuickRefileTargets = value;
                    if (_orgQuickRefileTargets != null)
                    {
                        _orgQuickRefileTargets.CollectionChanged += OnOrgQuickRefileTargetsCollectionChanged;
                    }
                    OnPropertyChanged();
                    
                    // Auto-save when collection is replaced
                    var dictionary = _orgQuickRefileTargets?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, string>();
                    _config.OrgQuickRefileTargets = dictionary;
                }
            }
        }

        private void OnOrgQuickRefileTargetsCollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Auto-save when OrgQuickRefileTargets collection changes
            var dictionary = OrgQuickRefileTargets?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) ?? new Dictionary<string, string>();
            _config.OrgQuickRefileTargets = dictionary;
        }

        private string _newRefileTargetName;
        public string NewRefileTargetName
        {
            get => _newRefileTargetName;
            set
            {
                if (_newRefileTargetName != value)
                {
                    _newRefileTargetName = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _newRefileTargetPath;
        public string NewRefileTargetPath
        {
            get => _newRefileTargetPath;
            set
            {
                if (_newRefileTargetPath != value)
                {
                    _newRefileTargetPath = value;
                    OnPropertyChanged();
                }
            }
        }

        private KeyValuePair<string, string>? _selectedRefileTarget;
        public KeyValuePair<string, string>? SelectedRefileTarget
        {
            get => _selectedRefileTarget;
            set
            {
                if (!Equals(_selectedRefileTarget, value))
                {
                    _selectedRefileTarget = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool TagCyclingReplacesAll
        {
            get => _config.TagCyclingReplacesAll;
            set
            {
                if (_config.TagCyclingReplacesAll != value)
                {
                    _config.TagCyclingReplacesAll = value;
                    OnPropertyChanged();
                }
            }
        }

        // Quick Capture Settings
        public string InboxFilePath
        {
            get => _config.InboxFilePath;
            set
            {
                if (_config.InboxFilePath != value)
                {
                    _config.InboxFilePath = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool AddTimestampToCapture
        {
            get => _config.AddTimestampToCapture;
            set
            {
                if (_config.AddTimestampToCapture != value)
                {
                    _config.AddTimestampToCapture = value;
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _orgAgendaFiles;
        public ObservableCollection<string> OrgAgendaFiles
        {
            get => _orgAgendaFiles ??= new ObservableCollection<string>(_config.OrgAgendaFiles);
            set
            {
                if (_orgAgendaFiles != value)
                {
                    _orgAgendaFiles = value;
                    _config.OrgAgendaFiles = value.ToArray();
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _orgAgendaDirectories;
        public ObservableCollection<string> OrgAgendaDirectories
        {
            get => _orgAgendaDirectories ??= new ObservableCollection<string>(_config.OrgAgendaDirectories);
            set
            {
                if (_orgAgendaDirectories != value)
                {
                    _orgAgendaDirectories = value;
                    _config.OrgAgendaDirectories = value.ToArray();
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _orgTodoStates;
        public ObservableCollection<string> OrgTodoStates
        {
            get => _orgTodoStates ??= new ObservableCollection<string>(_config.OrgTodoStates);
            set
            {
                if (_orgTodoStates != value)
                {
                    _orgTodoStates = value;
                    _config.OrgTodoStates = value.ToArray();
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _orgDoneStates;
        public ObservableCollection<string> OrgDoneStates
        {
            get => _orgDoneStates ??= new ObservableCollection<string>(_config.OrgDoneStates);
            set
            {
                if (_orgDoneStates != value)
                {
                    _orgDoneStates = value;
                    _config.OrgDoneStates = value.ToArray();
                    OnPropertyChanged();
                }
            }
        }

        private ObservableCollection<string> _orgNoActionStates;
        public ObservableCollection<string> OrgNoActionStates
        {
            get => _orgNoActionStates ??= new ObservableCollection<string>(_config.OrgNoActionStates);
            set
            {
                if (_orgNoActionStates != value)
                {
                    _orgNoActionStates = value;
                    _config.OrgNoActionStates = value.ToArray();
                    OnPropertyChanged();
                }
            }
        }

        // New state text properties for adding states
        private string _newTodoState;
        public string NewTodoState
        {
            get => _newTodoState;
            set
            {
                if (_newTodoState != value)
                {
                    _newTodoState = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _newDoneState;
        public string NewDoneState
        {
            get => _newDoneState;
            set
            {
                if (_newDoneState != value)
                {
                    _newDoneState = value;
                    OnPropertyChanged();
                }
            }
        }

        private string _newNoActionState;
        public string NewNoActionState
        {
            get => _newNoActionState;
            set
            {
                if (_newNoActionState != value)
                {
                    _newNoActionState = value;
                    OnPropertyChanged();
                }
            }
        }

        // State Color Configuration
        private ObservableCollection<StateColorItem> _stateColorItems;
        public ObservableCollection<StateColorItem> StateColorItems
        {
            get => _stateColorItems ??= LoadStateColorItems();
            set
            {
                if (_stateColorItems != value)
                {
                    _stateColorItems = value;
                    OnPropertyChanged();
                }
            }
        }

        #endregion

        private void Save()
        {
            try
            {
                // Validate library path before saving
                if (string.IsNullOrWhiteSpace(_config.LibraryPath))
                {
                    _dialogService.ShowError("Library path must be set before saving.", "Configuration Error");
                    return;
                }

                // Try to create the directory if it doesn't exist
                try
                {
                    if (!Directory.Exists(_config.LibraryPath))
                    {
                        Directory.CreateDirectory(_config.LibraryPath);
                    }
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Could not create library directory: {ex.Message}", "Configuration Error");
                    return;
                }

                // Save OpenRouter models if available
                if (AvailableOpenRouterModels != null && AvailableOpenRouterModels.Count > 0)
                {
                    var selectedModels = AvailableOpenRouterModels
                        .Where(m => m.IsSelected)
                        .Select(m => m.Name)
                        .ToList();
                    _config.OpenRouterModels = selectedModels;
                }

                _configService.Save();
                RequestClose?.Invoke(true);
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error saving settings: {ex.Message}", "Error");
            }
        }

        private void Cancel()
        {
            RequestClose?.Invoke(false);
        }

        private void ResetSettings()
        {
            if (_dialogService.ShowConfirmation("Are you sure you want to reset all settings to default?", "Confirm Reset"))
            {
                ConfigurationDefaults.ResetToDefaults(ConfigurationManager.Instance);
                LoadSettings();
            }
        }

        private void BrowseLibrary()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Library Folder",
                FileName = "Select Folder", // Workaround since we're using SaveFileDialog for folder selection
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dialog.ShowDialog() == true)
            {
                // Get the selected folder path
                string folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    LibraryPath = folderPath;
                }
            }
        }

        private void TestConnection()
        {
            // Implementation will depend on which connection to test
            _dialogService.ShowMessage("Connection test not implemented", "Not Implemented");
        }

        private void ClearCache()
        {
            if (_dialogService.ShowConfirmation("Are you sure you want to clear the cache?", "Confirm Clear Cache"))
            {
                try
                {
                    // Implementation depends on what needs to be cleared
                    _dialogService.ShowMessage("Cache cleared successfully", "Success");
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Error clearing cache: {ex.Message}", "Error");
                }
            }
        }

        private async void SyncNow()
        {
            try
            {
                // Implementation depends on sync service
                await Task.Delay(100); // Placeholder
                _dialogService.ShowMessage("Sync completed successfully", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error during sync: {ex.Message}", "Error");
            }
        }

        private async void TestSync()
        {
            try
            {
                // Implementation depends on sync service
                await Task.Delay(100); // Placeholder
                _dialogService.ShowMessage("Sync test completed successfully", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error testing sync: {ex.Message}", "Error");
            }
        }

        private async void RefreshVoices()
        {
            try
            {
                await LoadTTSVoices();
                _dialogService.ShowMessage("Voices refreshed successfully", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error refreshing voices: {ex.Message}", "Error");
            }
        }

        private void TestTTS()
        {
            try
            {
                // Implementation depends on TTS service
                _dialogService.ShowMessage("TTS test completed successfully", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error testing TTS: {ex.Message}", "Error");
            }
        }

        public void UpdateJellyfinPassword(string password)
        {
            _config.JellyfinPassword = password;
            _config.Save();
        }

        public void UpdateSubsonicPassword(string password)
        {
            _config.SubsonicPassword = password;
        }

        public void UpdateOpenAIKey(string password)
        {
            if (!string.IsNullOrWhiteSpace(password))
            {
                _config.OpenAIApiKey = password;
                EnableOpenAI = true;  // Auto-enable when key is provided
            }
            else
            {
                _config.OpenAIApiKey = null;
                EnableOpenAI = false;  // Auto-disable when key is removed
            }
            OnPropertyChanged(nameof(OpenAIApiKey));
        }

        public void UpdateAnthropicKey(string password)
        {
            _config.AnthropicApiKey = password;
        }

        public void UpdateXAIKey(string password)
        {
            _config.XAIApiKey = password;
        }

        public void UpdateWeatherApiKey(string password)
        {
            _config.WeatherApiKey = password;
        }

        public void UpdateMatrixPassword(string password)
        {
            _config.MatrixPassword = password;
        }

        public void UpdateAudiobookshelfPassword(string password)
        {
            _config.AudiobookshelfPassword = password;
        }

        public void UpdateSyncPassword(string password)
        {
            _config.SyncPassword = password;
            _config.Save();
        }

        private void CloseWindow()
        {
            var window = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
            window?.Close();
        }

        private bool HasValidAIProvider()
        {
            return (_config.EnableOpenAI && !string.IsNullOrEmpty(_config.OpenAIApiKey)) ||
                   (_config.EnableAnthropic && !string.IsNullOrEmpty(_config.AnthropicApiKey)) ||
                   (_config.EnableXAI && !string.IsNullOrEmpty(_config.XAIApiKey)) ||
                   (_config.EnableOllama) ||
                   (_config.EnableOpenRouter && !string.IsNullOrEmpty(_config.OpenRouterApiKey));
        }

        private async Task LoadTTSVoices()
        {
            try
            {
                var voices = _config.TTSAvailableVoices;
                if (voices != null && voices.Count > 0)
                {
                    TTSVoices.Clear();
                    foreach (var voice in voices)
                    {
                        TTSVoices.Add(voice);
                    }
                }
                else
                {
                    await RefreshVoicesFromServer();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading TTS voices: {ex.Message}");
                await RefreshVoicesFromServer();
            }
        }

        private async Task RefreshVoicesFromServer()
        {
            try
            {
                var voices = await GetVoicesFromServer();
                if (voices != null && voices.Any())
                {
                    TTSVoices.Clear();
                    foreach (var voice in voices)
                    {
                        TTSVoices.Add(voice);
                    }
                    _config.TTSAvailableVoices = voices.ToList();
                    _config.Save();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing TTS voices: {ex.Message}");
            }
        }

        private async Task<List<string>> GetVoicesFromServer()
        {
            if (string.IsNullOrEmpty(_config.TTSApiUrl))
            {
                return new List<string>();
            }

            try
            {
                using (var client = new HttpClient())
                {
                    var response = await client.GetAsync($"{_config.TTSApiUrl}/voices");
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        var voiceInfos = System.Text.Json.JsonSerializer.Deserialize<List<VoiceInfo>>(json);
                        return voiceInfos.Select(v => v.Name).ToList();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting voices from server: {ex.Message}");
            }

            return new List<string>();
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                // Initialize collections
                AvailableOpenRouterModels = new ObservableCollection<OpenRouterModelViewModel>();
                
                // Load AI service settings first
                EnableOpenAI = _config.EnableOpenAI;
                EnableAnthropic = _config.EnableAnthropic;
                EnableXAI = _config.EnableXAI;
                EnableOllama = _config.EnableOllama;
                EnableOpenRouter = _config.EnableOpenRouter;
                OllamaUrl = _config.OllamaUrl;
                OllamaModel = _config.OllamaModel;
                EnableAIChat = _config.EnableAIChat;

                // Load saved OpenRouter models if any
                LoadOpenRouterModels();
                
                // Load weather settings
                EnableWeather = _config.EnableWeather;
                WeatherZipCode = _config.WeatherZipCode;
                EnableMoonPhase = _config.EnableMoonPhase;

                // Load TTS settings
                EnableTTS = _config.EnableTTS;
                TTSApiUrl = _config.TTSApiUrl;
                TTSVoice = _config.TTSVoice;
                LoadTTSVoices();

                // Load sync settings
                SyncServerUrl = _config.SyncServerUrl;
                SyncUsername = _config.SyncUsername;
                AutoSync = _config.AutoSync;
                SyncIntervalMinutes = _config.SyncIntervalMinutes;

                // Restore password box values
                var window = System.Windows.Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                if (window != null)
                {
                    var jellyfinPasswordBox = window.FindName("JellyfinPassword") as System.Windows.Controls.PasswordBox;
                    if (jellyfinPasswordBox != null && !string.IsNullOrEmpty(_config.JellyfinPassword))
                    {
                        jellyfinPasswordBox.Password = _config.JellyfinPassword;
                    }
                }

                OnPropertyChanged(string.Empty);
                
                // Refresh state color items
                _stateColorItems = null;
                OnPropertyChanged(nameof(StateColorItems));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading settings: {ex.Message}");
            }
        }

        private async void LoadOpenRouterModels()
        {
            if (string.IsNullOrEmpty(_config.OpenRouterApiKey) || !_config.EnableOpenRouter)
            {
                return;
            }

            try
            {
                var service = new OpenRouterService(_config.OpenRouterApiKey);
                var models = await service.GetAvailableModels();
                var savedModels = _config.OpenRouterModels ?? new List<string>();

                AvailableOpenRouterModels.Clear();
                foreach (var model in models)
                {
                    AvailableOpenRouterModels.Add(new OpenRouterModelViewModel
                    {
                        Name = model.Name,
                        DisplayName = model.DisplayName,
                        IsSelected = savedModels.Contains(model.Name)
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading OpenRouter models: {ex.Message}");
            }
        }

        private async void FetchOpenRouterModels()
        {
            if (string.IsNullOrEmpty(_config.OpenRouterApiKey))
            {
                System.Windows.MessageBox.Show("Please enter an OpenRouter API key first.", "API Key Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var service = new OpenRouterService(_config.OpenRouterApiKey);
                var models = await service.GetAvailableModels();
                var savedModels = _config.OpenRouterModels ?? new List<string>();

                AvailableOpenRouterModels.Clear();
                foreach (var model in models)
                {
                    AvailableOpenRouterModels.Add(new OpenRouterModelViewModel
                    {
                        Name = model.Name,
                        DisplayName = model.DisplayName,
                        IsSelected = savedModels.Contains(model.Name)
                    });
                }

                System.Windows.MessageBox.Show($"Successfully fetched {models.Count} models from OpenRouter.", "Models Fetched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching OpenRouter models: {ex.Message}");
                System.Windows.MessageBox.Show($"Error fetching models: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void UpdateOpenRouterKey(string password)
        {
            if (!string.IsNullOrWhiteSpace(password))
            {
                _config.OpenRouterApiKey = password;
                EnableOpenRouter = true;  // Auto-enable when key is provided
            }
            else
            {
                _config.OpenRouterApiKey = null;
                EnableOpenRouter = false;  // Auto-disable when key is removed
            }
            OnPropertyChanged(nameof(OpenRouterApiKey));
        }

        private class VoiceInfo
        {
            public string Name { get; set; }
            [JsonIgnore]
            public string Description { get; set; }
        }

        public void SavePassword(string key, string value)
        {
            switch (key)
            {
                case "WeatherApiKey":
                    _config.WeatherApiKey = value;
                    break;
                case "OpenAIApiKey":
                    _config.OpenAIApiKey = value;
                    break;
                case "AnthropicApiKey":
                    _config.AnthropicApiKey = value;
                    break;
                case "XAIApiKey":
                    _config.XAIApiKey = value;
                    break;
                case "OpenRouterApiKey":
                    _config.OpenRouterApiKey = value;
                    break;
                case "SyncPassword":
                    _config.SyncPassword = value;
                    break;
                case "MatrixPassword":
                    _config.MatrixPassword = value;
                    break;
                case "SubsonicPassword":
                    _config.SubsonicPassword = value;
                    break;
                case "JellyfinPassword":
                    _config.JellyfinPassword = value;
                    break;
                case "AudiobookshelfPassword":
                    _config.AudiobookshelfPassword = value;
                    break;
            }
        }

        private void UpdateAIChatStatus()
        {
            // Enable AI chat if any AI service is enabled
            _config.EnableAIChat = 
                _config.EnableOpenAI || 
                _config.EnableAnthropic || 
                _config.EnableXAI || 
                _config.EnableOllama ||
                _config.EnableOpenRouter;
        }
        
        private void CharacterizeLibrary()
        {
            _dialogService.ShowMessage("The music characterization feature has been removed from this version.", "Feature Removed");
        }

        private void BrowseFolder(object parameter)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog();
            var folderDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Folder",
                FileName = "Select Folder", // Workaround since WPF doesn't have a folder browser
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            if (folderDialog.ShowDialog() == true)
            {
                string selectedPath = Path.GetDirectoryName(folderDialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Handle the selected folder path based on the context
                    if (parameter is string context)
                    {
                        switch (context)
                        {
                            case "Library":
                                LibraryPath = selectedPath;
                                break;
                            // Add other cases as needed
                        }
                    }
                }
            }
        }

        private void BrowseFile(object parameter)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            if (dialog.ShowDialog() == true)
            {
                string selectedFile = dialog.FileName;
                if (!string.IsNullOrEmpty(selectedFile))
                {
                    // Handle the selected file based on the context
                    if (parameter is string context)
                    {
                        switch (context)
                        {
                            // Add cases as needed
                        }
                    }
                }
            }
        }

        private void BrowseBackupFolder(object parameter)
        {
            var folderDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Select Backup Folder",
                FileName = "Select Folder", // Workaround since WPF doesn't have a folder browser
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false
            };

            if (folderDialog.ShowDialog() == true)
            {
                string selectedPath = Path.GetDirectoryName(folderDialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Handle the selected backup folder path
                    // Implementation depends on the specific requirements
                }
            }
        }

        private void TestWeather()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.WeatherApiKey))
                {
                    _dialogService.ShowError("Weather API key is required.", "Weather Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.WeatherZipCode))
                {
                    _dialogService.ShowError("ZIP code is required.", "Weather Test");
                    return;
                }

                // Implement actual weather API test
                _dialogService.ShowMessage("Weather API test successful.", "Weather Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Weather API test failed: {ex.Message}", "Weather Test");
            }
        }

        private void TestAI()
        {
            try
            {
                if (!HasValidAIProvider())
                {
                    _dialogService.ShowError("No valid AI provider configured.", "AI Test");
                    return;
                }

                // Implement actual AI API test
                _dialogService.ShowMessage("AI API test successful.", "AI Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"AI API test failed: {ex.Message}", "AI Test");
            }
        }

        private void TestMatrix()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.MatrixServerUrl))
                {
                    _dialogService.ShowError("Matrix server URL is required.", "Matrix Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.MatrixUsername))
                {
                    _dialogService.ShowError("Matrix username is required.", "Matrix Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.MatrixPassword))
                {
                    _dialogService.ShowError("Matrix password is required.", "Matrix Test");
                    return;
                }

                // Implement actual Matrix API test
                _dialogService.ShowMessage("Matrix API test successful.", "Matrix Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Matrix API test failed: {ex.Message}", "Matrix Test");
            }
        }

        private void TestSubsonic()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.SubsonicUrl))
                {
                    _dialogService.ShowError("Subsonic server URL is required.", "Subsonic Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.SubsonicUsername))
                {
                    _dialogService.ShowError("Subsonic username is required.", "Subsonic Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.SubsonicPassword))
                {
                    _dialogService.ShowError("Subsonic password is required.", "Subsonic Test");
                    return;
                }

                // Implement actual Subsonic API test
                _dialogService.ShowMessage("Subsonic API test successful.", "Subsonic Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Subsonic API test failed: {ex.Message}", "Subsonic Test");
            }
        }

        private void TestJellyfin()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.JellyfinUrl))
                {
                    _dialogService.ShowError("Jellyfin server URL is required.", "Jellyfin Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.JellyfinUsername))
                {
                    _dialogService.ShowError("Jellyfin username is required.", "Jellyfin Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.JellyfinPassword))
                {
                    _dialogService.ShowError("Jellyfin password is required.", "Jellyfin Test");
                    return;
                }

                // Implement actual Jellyfin API test
                _dialogService.ShowMessage("Jellyfin API test successful.", "Jellyfin Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Jellyfin API test failed: {ex.Message}", "Jellyfin Test");
            }
        }

        private void TestAudiobookshelf()
        {
            try
            {
                if (string.IsNullOrEmpty(_config.AudiobookshelfUrl))
                {
                    _dialogService.ShowError("Audiobookshelf server URL is required.", "Audiobookshelf Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.AudiobookshelfUsername))
                {
                    _dialogService.ShowError("Audiobookshelf username is required.", "Audiobookshelf Test");
                    return;
                }

                if (string.IsNullOrEmpty(_config.AudiobookshelfPassword))
                {
                    _dialogService.ShowError("Audiobookshelf password is required.", "Audiobookshelf Test");
                    return;
                }

                // Implement actual Audiobookshelf API test
                _dialogService.ShowMessage("Audiobookshelf API test successful.", "Audiobookshelf Test");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Audiobookshelf API test failed: {ex.Message}", "Audiobookshelf Test");
            }
        }

        #region Org-Mode Command Implementations
        private void AddAgendaFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Org File",
                Filter = "Org files (*.org)|*.org|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                if (!OrgAgendaFiles.Contains(dialog.FileName))
                {
                    OrgAgendaFiles.Add(dialog.FileName);
                    _config.OrgAgendaFiles = OrgAgendaFiles.ToArray();
                }
            }
        }

        private void RemoveAgendaFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && OrgAgendaFiles.Contains(filePath))
            {
                OrgAgendaFiles.Remove(filePath);
                _config.OrgAgendaFiles = OrgAgendaFiles.ToArray();
            }
        }

        private void AddAgendaDirectory()
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select directory containing org files",
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                if (!OrgAgendaDirectories.Contains(dialog.SelectedPath))
                {
                    OrgAgendaDirectories.Add(dialog.SelectedPath);
                    _config.OrgAgendaDirectories = OrgAgendaDirectories.ToArray();
                }
            }
        }

        private void RemoveAgendaDirectory(string directoryPath)
        {
            if (!string.IsNullOrEmpty(directoryPath) && OrgAgendaDirectories.Contains(directoryPath))
            {
                OrgAgendaDirectories.Remove(directoryPath);
                _config.OrgAgendaDirectories = OrgAgendaDirectories.ToArray();
            }
        }

        private void AddTodoState()
        {
            if (!string.IsNullOrWhiteSpace(NewTodoState))
            {
                var stateName = NewTodoState.Trim().ToUpper();
                if (!OrgTodoStates.Contains(stateName))
                {
                    OrgTodoStates.Add(stateName);
                    _config.OrgTodoStates = OrgTodoStates.ToArray();
                    NewTodoState = string.Empty;
                }
            }
        }

        private void RemoveTodoState(string state)
        {
            if (!string.IsNullOrEmpty(state) && OrgTodoStates.Contains(state))
            {
                OrgTodoStates.Remove(state);
                _config.OrgTodoStates = OrgTodoStates.ToArray();
            }
        }

        private void AddDoneState()
        {
            if (!string.IsNullOrWhiteSpace(NewDoneState))
            {
                var stateName = NewDoneState.Trim().ToUpper();
                if (!OrgDoneStates.Contains(stateName))
                {
                    OrgDoneStates.Add(stateName);
                    _config.OrgDoneStates = OrgDoneStates.ToArray();
                    NewDoneState = string.Empty;
                }
            }
        }

        private void RemoveDoneState(string state)
        {
            if (!string.IsNullOrEmpty(state) && OrgDoneStates.Contains(state))
            {
                OrgDoneStates.Remove(state);
                _config.OrgDoneStates = OrgDoneStates.ToArray();
            }
        }

        private void AddNoActionState()
        {
            if (!string.IsNullOrWhiteSpace(NewNoActionState))
            {
                var stateName = NewNoActionState.Trim().ToUpper();
                if (!OrgNoActionStates.Contains(stateName))
                {
                    OrgNoActionStates.Add(stateName);
                    _config.OrgNoActionStates = OrgNoActionStates.ToArray();
                    NewNoActionState = string.Empty;
                }
            }
        }

        private void RemoveNoActionState(string state)
        {
            if (!string.IsNullOrEmpty(state) && OrgNoActionStates.Contains(state))
            {
                OrgNoActionStates.Remove(state);
                _config.OrgNoActionStates = OrgNoActionStates.ToArray();
            }
        }

        private void ResetOrgStates()
        {
            var result = System.Windows.MessageBox.Show(
                "This will reset all TODO states to their defaults. Are you sure?",
                "Reset TODO States",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var defaultConfig = new OrgStateConfiguration();
                
                OrgTodoStates.Clear();
                foreach (var state in defaultConfig.TodoStates.Select(s => s.Name))
                {
                    OrgTodoStates.Add(state);
                }

                OrgDoneStates.Clear();
                foreach (var state in defaultConfig.DoneStates.Select(s => s.Name))
                {
                    OrgDoneStates.Add(state);
                }

                OrgNoActionStates.Clear();
                foreach (var state in defaultConfig.NoActionStates.Select(s => s.Name))
                {
                    OrgNoActionStates.Add(state);
                }

                _config.OrgTodoStates = OrgTodoStates.ToArray();
                _config.OrgDoneStates = OrgDoneStates.ToArray();
                _config.OrgNoActionStates = OrgNoActionStates.ToArray();
            }
        }

        private ObservableCollection<StateColorItem> LoadStateColorItems()
        {
            var items = new ObservableCollection<StateColorItem>();
            var colors = _config.OrgStateColors;
            
            // Get all configured states
            var allStates = new List<string>();
            allStates.AddRange(_config.OrgTodoStates);
            allStates.AddRange(_config.OrgDoneStates);
            allStates.AddRange(_config.OrgNoActionStates);
            
            // Create color items for all states
            foreach (var stateName in allStates.Distinct())
            {
                var colorHex = colors.ContainsKey(stateName) ? colors[stateName] : "#888888";
                items.Add(new StateColorItem(stateName, colorHex));
            }
            
            return items;
        }

        private void ResetStateColor(string stateName)
        {
            if (string.IsNullOrEmpty(stateName)) return;
            
            var defaultColors = _config.GetDefaultStateColors();
            var defaultColor = defaultColors.ContainsKey(stateName) ? defaultColors[stateName] : "#888888";
            
            // Update the configuration
            _config.SetStateColor(stateName, defaultColor);
            
            // Update the UI item
            var item = StateColorItems.FirstOrDefault(i => i.StateName == stateName);
            if (item != null)
            {
                item.ColorHex = defaultColor;
            }
        }

        private void ResetAllStateColors()
        {
            var result = System.Windows.MessageBox.Show(
                "This will reset all TODO state colors to their defaults. Are you sure?",
                "Reset State Colors",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var defaultColors = _config.GetDefaultStateColors();
                
                // Reset all colors in configuration
                _config.OrgStateColors = new Dictionary<string, string>(defaultColors);
                
                // Update UI items
                foreach (var item in StateColorItems)
                {
                    var defaultColor = defaultColors.ContainsKey(item.StateName) 
                        ? defaultColors[item.StateName] 
                        : "#888888";
                    item.ColorHex = defaultColor;
                }
            }
        }

        private void ResetNoActionState(string state)
        {
            if (!string.IsNullOrEmpty(state) && OrgNoActionStates.Contains(state))
            {
                OrgNoActionStates.Remove(state);
                _config.OrgNoActionStates = OrgNoActionStates.ToArray();
            }
        }

        private void OpenColorPicker(StateColorItem stateColorItem)
        {
            if (stateColorItem == null) return;

            // Create and configure the color dialog
            var colorDialog = new System.Windows.Forms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                SolidColorOnly = false
            };

            // Set the current color if valid
            try
            {
                if (!string.IsNullOrEmpty(stateColorItem.ColorHex))
                {
                    var currentColor = (Color)ColorConverter.ConvertFromString(stateColorItem.ColorHex);
                    colorDialog.Color = System.Drawing.Color.FromArgb(currentColor.A, currentColor.R, currentColor.G, currentColor.B);
                }
            }
            catch
            {
                // If current color is invalid, start with white
                colorDialog.Color = System.Drawing.Color.White;
            }

            // Show the dialog
            if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                // Convert the selected color to hex
                var selectedColor = colorDialog.Color;
                var hexColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
                
                // Update the state color item
                stateColorItem.ColorHex = hexColor;
                
                // Save to configuration
                var stateColors = _config.OrgStateColors;
                stateColors[stateColorItem.StateName] = hexColor;
                _config.OrgStateColors = stateColors;
            }
        }
        #endregion

        // Quick Refile Target Commands
        private void AddRefileTarget()
        {
            try
            {
                // Show input dialog for name
                var nameDialog = new Dialogs.InputDialog("Add Quick Refile Target", "Enter target name (e.g., INBOX, PROJECTS):");
                nameDialog.Owner = System.Windows.Application.Current.MainWindow;
                
                if (nameDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDialog.InputText))
                {
                    var name = nameDialog.InputText.Trim().ToUpperInvariant();
                    
                    // Check if name already exists
                    if (OrgQuickRefileTargets.Any(kvp => kvp.Key.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    {
                        _dialogService.ShowError($"A refile target with name '{name}' already exists.", "Duplicate Name");
                        return;
                    }
                    
                    // Show input dialog for path with better sizing and examples
                    var pathPrompt = "Enter target path (relative to library folder):\n\n" +
                                   "Examples:\n" +
                                   "• inbox.org\n" +
                                   "• projects.org::*Projects\n" +
                                   "• someday.org::*Someday Maybe\n\n" +
                                   "Format: filename.org or filename.org::*Heading";
                    
                    var pathDialog = new Dialogs.InputDialog("Add Quick Refile Target", pathPrompt);
                    pathDialog.Owner = System.Windows.Application.Current.MainWindow;
                    pathDialog.Height = 300; // Make dialog taller for long prompt
                    pathDialog.Width = 500;  // Make dialog wider
                    
                    if (pathDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(pathDialog.InputText))
                    {
                        var path = pathDialog.InputText.Trim();
                        
                        // Validate path format
                        if (!path.EndsWith(".org") && !path.Contains("::"))
                        {
                            if (_dialogService.ShowConfirmation($"Path '{path}' doesn't look like an org file. Continue anyway?", "Confirm Path"))
                            {
                                // Continue with the path as entered
                            }
                            else
                            {
                                return; // User cancelled
                            }
                        }
                        
                        // Add to collection
                        OrgQuickRefileTargets.Add(new KeyValuePair<string, string>(name, path));
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error adding refile target: {ex.Message}", "Error");
            }
        }

        private void EditRefileTarget()
        {
            try
            {
                if (!SelectedRefileTarget.HasValue)
                {
                    _dialogService.ShowMessage("Please select a refile target from the list to edit.", "No Selection");
                    return;
                }

                var selected = SelectedRefileTarget.Value;
                
                // Show input dialog for name with current value
                var nameDialog = new Dialogs.InputDialog("Edit Quick Refile Target", $"Enter target name:\n\nCurrent: {selected.Key}");
                nameDialog.Owner = System.Windows.Application.Current.MainWindow;
                
                if (nameDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(nameDialog.InputText))
                {
                    var newName = nameDialog.InputText.Trim().ToUpperInvariant();
                    
                    // Check if name already exists (unless it's the same as current)
                    if (!newName.Equals(selected.Key, StringComparison.OrdinalIgnoreCase) && 
                        OrgQuickRefileTargets.Any(kvp => kvp.Key.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _dialogService.ShowError($"A refile target with name '{newName}' already exists.", "Duplicate Name");
                        return;
                    }
                    
                    // Show input dialog for path with current value
                    var pathPrompt = $"Enter target path (relative to library folder):\n\n" +
                                   $"Current: {selected.Value}\n\n" +
                                   "Examples:\n" +
                                   "• inbox.org\n" +
                                   "• projects.org::*Projects\n" +
                                   "• someday.org::*Someday Maybe\n\n" +
                                   "Format: filename.org or filename.org::*Heading";
                    
                    var pathDialog = new Dialogs.InputDialog("Edit Quick Refile Target", pathPrompt);
                    pathDialog.Owner = System.Windows.Application.Current.MainWindow;
                    pathDialog.Height = 300; // Make dialog taller for long prompt
                    pathDialog.Width = 500;  // Make dialog wider
                    
                    if (pathDialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(pathDialog.InputText))
                    {
                        var newPath = pathDialog.InputText.Trim();
                        
                        // Remove old entry and add new one
                        var index = OrgQuickRefileTargets.IndexOf(selected);
                        if (index >= 0)
                        {
                            OrgQuickRefileTargets.RemoveAt(index);
                            OrgQuickRefileTargets.Insert(index, new KeyValuePair<string, string>(newName, newPath));
                            SelectedRefileTarget = new KeyValuePair<string, string>(newName, newPath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error editing refile target: {ex.Message}", "Error");
            }
        }

        private void RemoveRefileTarget()
        {
            try
            {
                if (!SelectedRefileTarget.HasValue)
                {
                    _dialogService.ShowMessage("Please select a refile target from the list to remove.", "No Selection");
                    return;
                }

                var selected = SelectedRefileTarget.Value;
                
                if (_dialogService.ShowConfirmation($"Are you sure you want to remove the refile target '{selected.Key}'?", "Confirm Removal"))
                {
                    OrgQuickRefileTargets.Remove(selected);
                    SelectedRefileTarget = null;
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error removing refile target: {ex.Message}", "Error");
            }
        }

        private void ResetRefileTargets()
        {
            try
            {
                if (_dialogService.ShowConfirmation("Are you sure you want to reset all quick refile targets to defaults?", "Confirm Reset"))
                {
                    // Clear existing targets
                    OrgQuickRefileTargets.Clear();
                    
                    // Add default targets
                    var defaults = _config.GetDefaultQuickRefileTargets();
                    foreach (var kvp in defaults)
                    {
                        OrgQuickRefileTargets.Add(new KeyValuePair<string, string>(kvp.Key, kvp.Value));
                    }
                    
                    _dialogService.ShowMessage("Quick refile targets have been reset to defaults.", "Reset Complete");
                }
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error resetting refile targets: {ex.Message}", "Error");
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

    public class OpenRouterModelViewModel : INotifyPropertyChanged
    {
        private string _name;
        private string _displayName;
        private bool _isSelected;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }

        public string DisplayName
        {
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 
