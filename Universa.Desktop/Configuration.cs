using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Properties;
using System.Windows.Media;

namespace Universa.Desktop
{
    public class Configuration : INotifyPropertyChanged
    {
        private static Configuration _instance;
        private static readonly object _lock = new object();
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReferenceHandler = ReferenceHandler.Preserve,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private bool _isLibraryExpanded = true;

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static Configuration Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = Load();
                        }
                    }
                }
                return _instance;
            }
        }

        // Library and file settings
        public string LibraryPath { get; set; }
        public string[] RecentFiles { get; set; } = Array.Empty<string>();
        public string OpenTabs { get; set; } = string.Empty;

        // AI settings
        public string OpenAIApiKey { get; set; }
        public string AnthropicApiKey { get; set; }
        public string XAIApiKey { get; set; }
        public AIProvider DefaultAIProvider { get; set; } = AIProvider.OpenAI;
        public bool EnableOpenAI { get; set; } = true;
        public bool EnableAnthropic { get; set; } = true;
        public bool EnableOllama { get; set; } = true;
        public bool EnableXAI { get; set; } = true;
        public bool UseBetaChains { get; set; }
        public string OllamaUrl { get; set; } = "http://localhost:11434/api/";
        public string OllamaModel { get; set; } = "llama2";
        public string LastUsedModel { get; set; }
        public bool EnableAICharacterization { get; set; } = true;
        public bool EnableAIChat { get; set; } = true;

        // Theme settings
        public string CurrentTheme { get; set; } = "Dark";
        public string DarkModePlayingTrackColor { get; set; } = "#2D2D30";
        public string LightModePlayingTrackColor { get; set; } = "#E6E6E6";
        public string DarkModeActiveTabColor { get; set; } = "#252526";
        public string LightModeActiveTabColor { get; set; } = "#FFFFFF";
        public string DarkModeInactiveTabColor { get; set; } = "#2D2D30";
        public string LightModeInactiveTabColor { get; set; } = "#F0F0F0";
        public string DarkModeMediaControlsColor { get; set; } = "#2D2D30";
        public string LightModeMediaControlsColor { get; set; } = "#F0F0F0";
        public string DarkModeTextColor { get; set; } = "#FFFFFF";
        public string LightModeTextColor { get; set; } = "#000000";
        public string DarkModePausedColor { get; set; } = "#404040";
        public string DarkModePlayingColor { get; set; } = "#2D2D30";
        public string LightModePausedColor { get; set; } = "#E0E0E0";
        public string LightModePlayingColor { get; set; } = "#FFFFFF";

        // UI state
        public double LastLibraryWidth { get; set; } = 250;
        public double LastChatWidth { get; set; } = 300;
        public double LastVolume { get; set; } = 1.0;
        public bool RssTabOpen { get; set; }

        // Music service settings
        public string SubsonicName { get; set; }
        public string SubsonicUrl { get; set; }
        public string SubsonicUsername { get; set; }
        public string SubsonicPassword { get; set; }

        // Jellyfin settings
        public string JellyfinName { get; set; }
        public string JellyfinUrl { get; set; }
        public string JellyfinUsername { get; set; }
        public string JellyfinPassword { get; set; }

        // Matrix settings
        public string MatrixName { get; set; }
        public string MatrixUrl { get; set; }
        public string MatrixUsername { get; set; }
        public string MatrixPassword { get; set; }
        private string _matrixServerUrl;
        public string MatrixServerUrl
        {
            get => _matrixServerUrl;
            set
            {
                if (_matrixServerUrl != value)
                {
                    _matrixServerUrl = value;
                    OnPropertyChanged(nameof(MatrixServerUrl));
                }
            }
        }
        private string _matrixServiceName;
        public string MatrixServiceName
        {
            get => _matrixServiceName;
            set
            {
                if (_matrixServiceName != value)
                {
                    _matrixServiceName = value;
                    OnPropertyChanged(nameof(MatrixServiceName));
                }
            }
        }

        // Audiobookshelf settings
        public string AudiobookshelfName { get; set; }
        public string AudiobookshelfUrl { get; set; }
        public string AudiobookshelfUsername { get; set; }
        public string AudiobookshelfPassword { get; set; }

        // RSS settings
        public string RssServerUrl { get; set; }
        public string RssUsername { get; set; }
        public string RssPassword { get; set; }
        public RssReadingMode RssReadingMode { get; set; } = RssReadingMode.List;
        public bool RssAutoMarkAsRead { get; set; }
        public int RssRefreshInterval { get; set; } = 30; // minutes
        public bool RssShowNotifications { get; set; } = true;
        public bool RssDownloadImages { get; set; } = true;
        public int RssMaxArticles { get; set; } = 100;
        public bool RssKeepUnread { get; set; }
        
        [JsonPropertyName("RssLastRead")]
        public Dictionary<string, DateTime> RssLastRead { get; set; } = new Dictionary<string, DateTime>();
        
        [JsonPropertyName("RssFavorites")]
        public List<string> RssFavorites { get; set; } = new List<string>();

        public List<string> ExpandedPaths { get; set; } = new List<string>();

        // TTS Settings
        public bool EnableTTS { get; set; }
        public string TTSApiUrl { get; set; } = "http://localhost:8000";
        public string TTSVoice { get; set; } = "af";
        public List<string> TTSAvailableVoices { get; set; } = new List<string>();
        public string TTSDefaultVoice { get; set; } = "af";

        // Weather Settings
        private bool _enableWeather;
        public bool EnableWeather
        {
            get => _enableWeather;
            set
            {
                if (_enableWeather != value)
                {
                    _enableWeather = value;
                    OnPropertyChanged(nameof(EnableWeather));
                }
            }
        }

        private string _weatherZipCode;
        public string WeatherZipCode
        {
            get => _weatherZipCode;
            set
            {
                if (_weatherZipCode != value)
                {
                    _weatherZipCode = value;
                    OnPropertyChanged(nameof(WeatherZipCode));
                }
            }
        }

        private bool _enableMoonPhase;
        public bool EnableMoonPhase
        {
            get => _enableMoonPhase;
            set
            {
                if (_enableMoonPhase != value)
                {
                    _enableMoonPhase = value;
                    OnPropertyChanged(nameof(EnableMoonPhase));
                }
            }
        }

        public string WeatherApiKey
        {
            get => Settings.Default.WeatherApiKey;
            set { Settings.Default.WeatherApiKey = value; Settings.Default.Save(); }
        }

        // Web Sync Settings
        public string UniversaWebUrl 
        { 
            get => Settings.Default.UniversaWebUrl;
            set { Settings.Default.UniversaWebUrl = value; Settings.Default.Save(); OnPropertyChanged(nameof(UniversaWebUrl)); }
        }
        
        public string UniversaWebUsername 
        { 
            get => Settings.Default.UniversaWebUsername;
            set { Settings.Default.UniversaWebUsername = value; Settings.Default.Save(); OnPropertyChanged(nameof(UniversaWebUsername)); }
        }
        
        public string UniversaWebPassword 
        { 
            get => Settings.Default.UniversaWebPassword;
            set { Settings.Default.UniversaWebPassword = value; Settings.Default.Save(); OnPropertyChanged(nameof(UniversaWebPassword)); }
        }
        
        public bool EnableWebSync 
        { 
            get => Settings.Default.EnableWebSync;
            set { Settings.Default.EnableWebSync = value; Settings.Default.Save(); OnPropertyChanged(nameof(EnableWebSync)); }
        }

        public bool AutoSync
        {
            get => Settings.Default.AutoSync;
            set { Settings.Default.AutoSync = value; Settings.Default.Save(); OnPropertyChanged(nameof(AutoSync)); }
        }

        public string SyncServerUrl
        {
            get => Settings.Default.SyncServerUrl;
            set { Settings.Default.SyncServerUrl = value; Settings.Default.Save(); OnPropertyChanged(nameof(SyncServerUrl)); }
        }

        public string SyncUsername
        {
            get => Settings.Default.SyncUsername;
            set { Settings.Default.SyncUsername = value; Settings.Default.Save(); OnPropertyChanged(nameof(SyncUsername)); }
        }

        public string SyncPassword
        {
            get => Settings.Default.SyncPassword;
            set { Settings.Default.SyncPassword = value; Settings.Default.Save(); OnPropertyChanged(nameof(SyncPassword)); }
        }

        public int SyncIntervalMinutes
        {
            get => Settings.Default.SyncIntervalMinutes;
            set { Settings.Default.SyncIntervalMinutes = value; Settings.Default.Save(); OnPropertyChanged(nameof(SyncIntervalMinutes)); }
        }
        
        private DateTime? _lastWebSyncTime;
        public DateTime? LastWebSyncTime 
        { 
            get 
            {
                var storedTime = Settings.Default.LastWebSyncTime;
                return storedTime == DateTime.MinValue ? null : storedTime;
            }
            set 
            {
                _lastWebSyncTime = value;
                Settings.Default.LastWebSyncTime = value.GetValueOrDefault(DateTime.MinValue);
                Settings.Default.Save();
                OnPropertyChanged(nameof(LastWebSyncTime));
            }
        }

        // Theme Management
        private Dictionary<string, Dictionary<string, string>> _themes = new Dictionary<string, Dictionary<string, string>>();

        public Color GetThemeColor(string themeName, string colorName)
        {
            if (_themes.TryGetValue(themeName, out var theme) && theme.TryGetValue(colorName, out var colorValue))
            {
                return (Color)ColorConverter.ConvertFromString(colorValue);
            }
            return Colors.Transparent;
        }

        public void SetThemeColor(string themeName, string colorName, Color color)
        {
            if (!_themes.ContainsKey(themeName))
            {
                _themes[themeName] = new Dictionary<string, string>();
            }
            _themes[themeName][colorName] = color.ToString();
            Save();
        }

        public IEnumerable<string> GetAvailableThemes()
        {
            return _themes.Keys;
        }

        public void DuplicateTheme(string sourceName, string newName)
        {
            if (_themes.TryGetValue(sourceName, out var sourceTheme))
            {
                _themes[newName] = new Dictionary<string, string>(sourceTheme);
                Save();
            }
        }

        public void DeleteTheme(string themeName)
        {
            if (themeName != "Light" && themeName != "Dark" && _themes.Remove(themeName))
            {
                Save();
            }
        }

        // AI and ML Settings
        public bool EnableLocalEmbeddings { get; set; } = false;
        public string MusicCharacterizationMethod { get; set; } = "Default";

        // Configuration Merging
        public void MergeFrom(Configuration other)
        {
            if (other == null) return;

            // Merge theme settings
            foreach (var theme in other._themes)
            {
                if (!_themes.ContainsKey(theme.Key))
                {
                    _themes[theme.Key] = new Dictionary<string, string>(theme.Value);
                }
            }

            // Merge other settings that should be synchronized
            if (!string.IsNullOrEmpty(other.WeatherZipCode))
                WeatherZipCode = other.WeatherZipCode;
            if (!string.IsNullOrEmpty(other.WeatherApiKey))
                WeatherApiKey = other.WeatherApiKey;
            EnableWeather = other.EnableWeather;
            EnableMoonPhase = other.EnableMoonPhase;

            // Merge AI settings
            EnableLocalEmbeddings = other.EnableLocalEmbeddings;
            MusicCharacterizationMethod = other.MusicCharacterizationMethod;
            EnableAICharacterization = other.EnableAICharacterization;
            EnableAIChat = other.EnableAIChat;

            Save();
        }

        [JsonConstructor]
        public Configuration()
        {
            // Initialize collections
            RssLastRead = new Dictionary<string, DateTime>();
            RssFavorites = new List<string>();
            RecentFiles = Array.Empty<string>();

            // Initialize default values
            UniversaWebUrl = "";
            UniversaWebUsername = "";
            UniversaWebPassword = "";
            EnableWebSync = false;
            AutoSync = false;
            SyncServerUrl = "";
            SyncUsername = "";
            SyncPassword = "";
            SyncIntervalMinutes = 5;  // Default to 5 minutes
            LastWebSyncTime = null;
        }

        private static string GetConfigPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var basePath = Path.Combine(appDataPath, "Universa");

#if DEBUG
            // Use a different directory for debug builds
            basePath = Path.Combine(basePath, "Debug");
#endif

            return Path.Combine(basePath, "config.json");
        }

        private static Configuration Load()
        {
            try
            {
                var configPath = GetConfigPath();
                var configDir = Path.GetDirectoryName(configPath);
                
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                Configuration config;

                if (File.Exists(configPath))
                {
                    var json = File.ReadAllText(configPath);
                    config = JsonSerializer.Deserialize<Configuration>(json, _jsonOptions) ?? new Configuration();
                }
                else
                {
                    config = new Configuration();
                }

                // Initialize collections if they're null
                config.RssLastRead ??= new Dictionary<string, DateTime>();
                config.RssFavorites ??= new List<string>();
                config.RecentFiles ??= Array.Empty<string>();

                // Initialize weather settings from old settings if needed
                if (string.IsNullOrEmpty(config.WeatherZipCode))
                {
                    config.WeatherZipCode = Settings.Default.WeatherZipCode;
                    config.EnableWeather = Settings.Default.EnableWeather;
                    config.EnableMoonPhase = Settings.Default.EnableMoonPhase;
                    config.WeatherApiKey = Settings.Default.WeatherApiKey;
                }

                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading configuration: {ex}");
                return new Configuration();
            }
        }

        public void Save()
        {
            try
            {
                var configPath = GetConfigPath();
                var configDir = Path.GetDirectoryName(configPath);
                
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                var json = JsonSerializer.Serialize(this, _jsonOptions);
                File.WriteAllText(configPath, json);

                // Also save to the old settings system for compatibility
                var settings = Properties.Settings.Default;
                settings.SubsonicUrl = SubsonicUrl;
                settings.SubsonicUsername = SubsonicUsername;
                settings.SubsonicPassword = SubsonicPassword;
                settings.SubsonicName = SubsonicName;
                settings.JellyfinUrl = JellyfinUrl;
                settings.JellyfinUsername = JellyfinUsername;
                settings.JellyfinPassword = JellyfinPassword;
                settings.JellyfinName = JellyfinName;
                settings.MatrixUrl = MatrixUrl;
                settings.MatrixUsername = MatrixUsername;
                settings.MatrixPassword = MatrixPassword;
                settings.MatrixName = MatrixName;
                settings.CurrentTheme = CurrentTheme;
                settings.LibraryPath = LibraryPath;
                settings.LastLibraryWidth = LastLibraryWidth;
                settings.LastChatWidth = LastChatWidth;
                settings.LastVolume = LastVolume;
                settings.RssTabOpen = RssTabOpen;

                // Save AI service settings
                settings.EnableOpenAI = EnableOpenAI;
                settings.OpenAIApiKey = OpenAIApiKey;
                settings.EnableAnthropic = EnableAnthropic;
                settings.AnthropicApiKey = AnthropicApiKey;
                settings.EnableOllama = EnableOllama;
                settings.EnableXAI = EnableXAI;
                settings.XAIApiKey = XAIApiKey;
                settings.OllamaUrl = OllamaUrl;
                settings.OllamaModel = OllamaModel;
                settings.EnableAICharacterization = EnableAICharacterization;
                settings.EnableAIChat = EnableAIChat;

                // Save weather settings
                settings.WeatherZipCode = WeatherZipCode;
                settings.EnableWeather = EnableWeather;
                settings.EnableMoonPhase = EnableMoonPhase;

                // Save web sync settings
                settings.UniversaWebUrl = UniversaWebUrl;
                settings.UniversaWebUsername = UniversaWebUsername;
                settings.UniversaWebPassword = UniversaWebPassword;
                settings.EnableWebSync = EnableWebSync;
                settings.AutoSync = AutoSync;
                settings.SyncServerUrl = SyncServerUrl;
                settings.SyncUsername = SyncUsername;
                settings.SyncPassword = SyncPassword;
                settings.SyncIntervalMinutes = SyncIntervalMinutes;
                settings.LastWebSyncTime = LastWebSyncTime.GetValueOrDefault(DateTime.MinValue);

                settings.Save();

                System.Diagnostics.Debug.WriteLine($"Configuration saved to: {configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving configuration: {ex}");
                throw;
            }
        }

        public void MigrateFromOldSettings()
        {
            var settings = Properties.Settings.Default;
            
            // Migrate existing settings
            SubsonicUrl = settings.SubsonicUrl;
            SubsonicUsername = settings.SubsonicUsername;
            SubsonicPassword = settings.SubsonicPassword;
            SubsonicName = settings.SubsonicName;
            
            JellyfinUrl = settings.JellyfinUrl;
            JellyfinUsername = settings.JellyfinUsername;
            JellyfinPassword = settings.JellyfinPassword;
            JellyfinName = settings.JellyfinName;
            
            MatrixUrl = settings.MatrixUrl;
            MatrixUsername = settings.MatrixUsername;
            MatrixPassword = settings.MatrixPassword;
            MatrixName = settings.MatrixName;
            
            CurrentTheme = settings.CurrentTheme;
            LibraryPath = settings.LibraryPath;
            LastLibraryWidth = settings.LastLibraryWidth;
            LastChatWidth = settings.LastChatWidth;
            LastVolume = settings.LastVolume;
            RssTabOpen = settings.RssTabOpen;
            
            // Migrate AI service settings
            EnableOpenAI = settings.EnableOpenAI;
            OpenAIApiKey = settings.OpenAIApiKey;
            EnableAnthropic = settings.EnableAnthropic;
            AnthropicApiKey = settings.AnthropicApiKey;
            EnableOllama = settings.EnableOllama;
            EnableXAI = settings.EnableXAI;
            XAIApiKey = settings.XAIApiKey;
            OllamaUrl = settings.OllamaUrl;
            OllamaModel = settings.OllamaModel;
            EnableAICharacterization = settings.EnableAICharacterization;
            EnableAIChat = settings.EnableAIChat;
            
            // Migrate weather settings
            WeatherZipCode = settings.WeatherZipCode;
            EnableWeather = settings.EnableWeather;
            EnableMoonPhase = settings.EnableMoonPhase;

            // Migrate web sync settings
            UniversaWebUrl = settings.UniversaWebUrl;
            UniversaWebUsername = settings.UniversaWebUsername;
            UniversaWebPassword = settings.UniversaWebPassword;
            EnableWebSync = settings.EnableWebSync;
            AutoSync = settings.AutoSync;
            SyncServerUrl = settings.SyncServerUrl;
            SyncUsername = settings.SyncUsername;
            SyncPassword = settings.SyncPassword;
            SyncIntervalMinutes = settings.SyncIntervalMinutes;
            LastWebSyncTime = settings.LastWebSyncTime == DateTime.MinValue ? null : settings.LastWebSyncTime;
        }

        public void LoadFrom(Configuration settings)
        {
            if (settings != null)
            {
                // ... existing code ...

                // Jellyfin settings
                if (!string.IsNullOrEmpty(settings.JellyfinUrl))
                {
                    JellyfinName = settings.JellyfinName;
                    JellyfinUrl = settings.JellyfinUrl;
                    JellyfinUsername = settings.JellyfinUsername;
                    JellyfinPassword = settings.JellyfinPassword;
                }

                // Subsonic settings
                if (!string.IsNullOrEmpty(settings.SubsonicUrl))
                {
                    SubsonicName = settings.SubsonicName;
                    SubsonicUrl = settings.SubsonicUrl;
                    SubsonicUsername = settings.SubsonicUsername;
                    SubsonicPassword = settings.SubsonicPassword;
                }

                // Matrix settings
                if (!string.IsNullOrEmpty(settings.MatrixUrl))
                {
                    MatrixName = settings.MatrixName;
                    MatrixUrl = settings.MatrixUrl;
                    MatrixUsername = settings.MatrixUsername;
                    MatrixPassword = settings.MatrixPassword;
                }
                // ... existing code ...
            }
        }

        public bool IsLibraryExpanded
        {
            get => _isLibraryExpanded;
            set
            {
                if (_isLibraryExpanded != value)
                {
                    _isLibraryExpanded = value;
                    OnPropertyChanged(nameof(IsLibraryExpanded));
                }
            }
        }
    }
} 