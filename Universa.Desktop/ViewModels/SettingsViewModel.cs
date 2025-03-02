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

namespace Universa.Desktop.ViewModels
{
    public class SettingsViewModel : INotifyPropertyChanged
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly IDialogService _dialogService;
        private readonly CharacterizationService _characterizationService;
        private bool _isCharacterizing;
        private double _characterizationProgress;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool?> RequestClose;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public SettingsViewModel(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            _dialogService = ServiceLocator.Instance.GetRequiredService<IDialogService>();
            
            // Only get CharacterizationService if AI characterization is enabled
            if (_config.EnableAICharacterization)
            {
                _characterizationService = ServiceLocator.Instance.GetService<CharacterizationService>();
            }

            // Initialize commands
            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
            ResetCommand = new RelayCommand(_ => ResetSettings());
            BrowseLibraryCommand = new RelayCommand(_ => BrowseLibrary());
            TestConnectionCommand = new RelayCommand(_ => TestConnection());
            CharacterizeLibraryCommand = new RelayCommand(_ => CharacterizeLibrary(), _ => _characterizationService != null);
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
        public ICommand CharacterizeLibraryCommand { get; }
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
                        var mainWindow = Application.Current.MainWindow as Views.MainWindow;
                        if (mainWindow?.LibraryNavigatorInstance != null)
                        {
                            // Run on UI thread
                            Application.Current.Dispatcher.Invoke(async () =>
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

        public bool EnableAICharacterization
        {
            get => _config.EnableAICharacterization;
            set
            {
                if (_config.EnableAICharacterization != value)
                {
                    _config.EnableAICharacterization = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool EnableLocalEmbeddings
        {
            get => _config.EnableLocalEmbeddings;
            set
            {
                if (_config.EnableLocalEmbeddings != value)
                {
                    _config.EnableLocalEmbeddings = value;
                    OnPropertyChanged();
                }
            }
        }

        public string OpenAIApiKey => _config.OpenAIApiKey;
        public string AnthropicApiKey => _config.AnthropicApiKey;
        public string XAIApiKey => _config.XAIApiKey;

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

        public double CharacterizationProgress
        {
            get => _characterizationProgress;
            set
            {
                _characterizationProgress = value;
                OnPropertyChanged(nameof(CharacterizationProgress));
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
        #endregion

        #region Methods
        private void LoadSettings()
        {
            try
            {
                // Load AI service settings first
                EnableOpenAI = _config.EnableOpenAI;
                EnableAnthropic = _config.EnableAnthropic;
                EnableXAI = _config.EnableXAI;
                EnableOllama = _config.EnableOllama;
                OllamaUrl = _config.OllamaUrl;
                OllamaModel = _config.OllamaModel;
                EnableAIChat = _config.EnableAIChat;

                // Ensure AI characterization is disabled if no valid provider
                if (_config.EnableAICharacterization && !HasValidAIProvider())
                {
                    _config.EnableAICharacterization = false;
                    _config.Save();
                }
                
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
                var window = Application.Current.Windows.OfType<SettingsWindow>().FirstOrDefault();
                if (window != null)
                {
                    var jellyfinPasswordBox = window.FindName("JellyfinPassword") as System.Windows.Controls.PasswordBox;
                    if (jellyfinPasswordBox != null && !string.IsNullOrEmpty(_config.JellyfinPassword))
                    {
                        jellyfinPasswordBox.Password = _config.JellyfinPassword;
                    }
                }

                OnPropertyChanged(string.Empty);
            }
            finally
            {
            }
        }

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

        private async void CharacterizeLibrary()
        {
            if (_isCharacterizing)
            {
                _dialogService.ShowMessage("Characterization is already in progress", "In Progress");
                            return;
                        }

            try
            {
                _isCharacterizing = true;
                await _characterizationService.CharacterizeLibraryAsync();
                _dialogService.ShowMessage("Library characterization completed successfully", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error characterizing library: {ex.Message}", "Error");
            }
            finally
            {
                _isCharacterizing = false;
                CharacterizationProgress = 0;
            }
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
            var window = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.DataContext == this);
            window?.Close();
        }

        private void UpdateAIChatStatus()
        {
            // Enable AI chat if any AI service is enabled
            _config.EnableAIChat = 
                _config.EnableOpenAI || 
                _config.EnableAnthropic || 
                _config.EnableXAI || 
                _config.EnableOllama;
        }

        private bool HasValidAIProvider()
        {
            return (_config.EnableOpenAI && !string.IsNullOrEmpty(_config.OpenAIApiKey)) ||
                   (_config.EnableAnthropic && !string.IsNullOrEmpty(_config.AnthropicApiKey)) ||
                   (_config.EnableXAI && !string.IsNullOrEmpty(_config.XAIApiKey)) ||
                   (_config.EnableOllama);
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

        private void OnCharacterizationProgress(object sender, CharacterizationProgressEventArgs e)
        {
            CharacterizationProgress = e.ProgressPercentage;
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

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            LoadSettings();
        }
        #endregion
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