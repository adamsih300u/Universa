using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.ComponentModel;
using System.Diagnostics;

namespace Universa.Desktop.Models
{
    public class Configuration : INotifyPropertyChanged
    {
        private static Configuration _instance;
        private static readonly object _lock = new object();
        private const string CONFIG_FILE = "config.json";

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public static Configuration Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = Load();
                    }
                    return _instance;
                }
            }
        }

        // AI Service settings
        public bool EnableOpenAI { get; set; }
        public string OpenAIApiKey { get; set; }
        public bool EnableAnthropic { get; set; }
        public string AnthropicApiKey { get; set; }
        public bool EnableXAI { get; set; }
        public string XAIApiKey { get; set; }
        public bool EnableOllama { get; set; }
        public string OllamaUrl { get; set; }
        public string OllamaModel { get; set; }
        public bool EnableAIChat { get; set; }
        public bool UseBetaChains { get; set; }
        public AIProvider DefaultAIProvider { get; set; } = AIProvider.OpenAI;
        public string LastUsedModel { get; set; }  // Stores the name of the last used AI model

        // Theme settings
        public string CurrentTheme { get; set; } = "Default";
        private Dictionary<string, Dictionary<string, Color>> _themes = new Dictionary<string, Dictionary<string, Color>>();

        public Color GetThemeColor(string themeName, string colorName)
        {
            if (_themes.TryGetValue(themeName, out var theme) && theme.TryGetValue(colorName, out var color))
            {
                return color;
            }
            return Colors.White; // Default color
        }

        public void SetThemeColor(string themeName, string colorName, Color color)
        {
            if (!_themes.ContainsKey(themeName))
            {
                _themes[themeName] = new Dictionary<string, Color>();
            }
            _themes[themeName][colorName] = color;
            Save();
        }

        public IEnumerable<string> GetAvailableThemes()
        {
            return _themes.Keys.ToList();
        }

        public void DuplicateTheme(string sourceName, string newName)
        {
            if (!_themes.ContainsKey(sourceName))
            {
                throw new ArgumentException($"Theme '{sourceName}' does not exist.");
            }

            if (_themes.ContainsKey(newName))
            {
                throw new ArgumentException($"Theme '{newName}' already exists.");
            }

            _themes[newName] = new Dictionary<string, Color>(_themes[sourceName]);
            Save();
        }

        public void DeleteTheme(string themeName)
        {
            if (themeName == "Default" || themeName == "Dark")
            {
                throw new ArgumentException("Cannot delete built-in themes.");
            }

            if (!_themes.ContainsKey(themeName))
            {
                throw new ArgumentException($"Theme '{themeName}' does not exist.");
            }

            _themes.Remove(themeName);
            Save();
        }

        // Theme color settings
        public string DarkModePlayingColor { get; set; } = "#FF4CAF50";  // Default green
        public string LightModePlayingColor { get; set; } = "#FF4CAF50";
        public string DarkModePausedColor { get; set; } = "#FF9E9E9E";   // Default gray
        public string LightModePausedColor { get; set; } = "#FF9E9E9E";
        public string DarkModeTextColor { get; set; } = "#FFFFFFFF";      // Default white
        public string LightModeTextColor { get; set; } = "#FF000000";     // Default black
        public string DarkModeActiveTabColor { get; set; } = "#FF424242";   // Dark gray for active tabs
        public string DarkModeInactiveTabColor { get; set; } = "#FF303030"; // Darker gray for inactive tabs
        public string LightModeActiveTabColor { get; set; } = "#FFFFFFFF";   // White for active tabs
        public string LightModeInactiveTabColor { get; set; } = "#FFF0F0F0"; // Light gray for inactive tabs

        // Media player settings
        public double LastVolume { get; set; } = 1.0;

        // Subsonic settings
        public string SubsonicUrl { get; set; }
        public string SubsonicUsername { get; set; }
        public string SubsonicPassword { get; set; }
        public string SubsonicName { get; set; }

        // Weather settings
        public string WeatherApiKey { get; set; }
        public bool EnableWeather { get; set; }
        public string WeatherZipCode { get; set; }
        public bool EnableMoonPhase { get; set; }

        // Sync settings
        public string SyncServerUrl { get; set; }
        public string SyncUsername { get; set; }
        public string SyncPassword { get; set; }
        public bool AutoSync { get; set; }
        public int SyncIntervalMinutes { get; set; }

        // Library settings
        public string LibraryPath { get; set; }

        // Cache settings
        public Dictionary<string, CachedMusicItem> CachedArtists { get; set; } = new();
        public Dictionary<string, CachedMusicItem> CachedAlbums { get; set; } = new();
        public Dictionary<string, CachedMusicItem> CachedPlaylists { get; set; } = new();
        public DateTime LastCacheUpdate { get; set; }

        public class CachedMusicItem
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string Description { get; set; }
            public string ImageUrl { get; set; }
            public string ArtistId { get; set; }
            public string ArtistName { get; set; }
            public int Year { get; set; } // Changed from int? to int to match MusicItem
            public int SongCount { get; set; }
            public TimeSpan Duration { get; set; }
            public DateTime Created { get; set; }
            public DateTime LastModified { get; set; }

            // Conversion methods to help with type conversion errors
            public static CachedMusicItem FromArtist(Artist artist)
            {
                return new CachedMusicItem
                {
                    Id = artist.Id,
                    Name = artist.Name,
                    Description = artist.Description,
                    ImageUrl = artist.ImageUrl,
                    SongCount = artist.SongCount,
                    Created = artist.Created,
                    LastModified = artist.LastModified
                };
            }

            public static CachedMusicItem FromAlbum(Album album)
            {
                return new CachedMusicItem
                {
                    Id = album.Id,
                    Name = album.Title,
                    Description = album.Description,
                    ImageUrl = album.ImageUrl,
                    ArtistId = album.ArtistId,
                    ArtistName = album.ArtistName,
                    Year = album.Year,
                    SongCount = album.SongCount,
                    Duration = album.Duration,
                    Created = album.Created,
                    LastModified = album.LastModified
                };
            }

            public static CachedMusicItem FromPlaylist(Playlist playlist)
            {
                return new CachedMusicItem
                {
                    Id = playlist.Id,
                    Name = playlist.Name,
                    Description = playlist.Description,
                    ImageUrl = playlist.ImageUrl,
                    SongCount = playlist.SongCount,
                    Duration = playlist.Duration,
                    Created = playlist.Created,
                    LastModified = playlist.LastModified
                };
            }
        }

        // RSS settings
        public string RssServerUrl { get; set; }
        public string RssUsername { get; set; }
        public string RssPassword { get; set; }
        public bool RssAutoMarkAsRead { get; set; } = true;
        public RssReadingMode RssReadingMode { get; set; } = RssReadingMode.List;

        // TTS settings
        public bool EnableTTS { get; set; }
        public string TTSApiUrl { get; set; }
        public string TTSVoice { get; set; }
        public string TTSDefaultVoice { get; set; }
        public List<string> TTSAvailableVoices { get; set; } = new List<string>();

        // UI state settings
        public string OpenTabs { get; set; }
        public bool RssTabOpen { get; set; }
        public bool IsLibraryExpanded { get; set; }
        public double LastLibraryWidth { get; set; } = 250;
        public bool IsChatExpanded { get; set; }
        public double LastChatWidth { get; set; } = 300;
        public string[] RecentFiles { get; set; } = Array.Empty<string>();
        public HashSet<string> ExpandedPaths { get; set; } = new();

        // Theme color settings
        public string DarkModePlayingTrackColor { get; set; } = "#FF4CAF50";  // Default green
        public string LightModePlayingTrackColor { get; set; } = "#FF4CAF50";
        public string DarkModeMediaControlsColor { get; set; } = "#FFFFFFFF";  // Default white
        public string LightModeMediaControlsColor { get; set; } = "#FF000000"; // Default black

        // Jellyfin settings
        public string JellyfinUrl { get; set; }
        public string JellyfinUsername { get; set; }
        public string JellyfinPassword { get; set; }
        public string JellyfinName { get; set; }

        // Audiobookshelf settings
        public string AudiobookshelfUrl { get; set; }
        public string AudiobookshelfUsername { get; set; }
        public string AudiobookshelfPassword { get; set; }
        public string AudiobookshelfName { get; set; }

        // Matrix settings
        public string MatrixServerUrl { get; set; }
        public string MatrixUsername { get; set; }
        public string MatrixPassword { get; set; }
        public string MatrixServiceName { get; set; } = "Matrix Chat";
        public string MatrixAccessToken { get; set; }
        public string MatrixName { get; set; }

        public void Save()
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CONFIG_FILE, json);
        }

        public static Configuration Load()
        {
            Debug.WriteLine("Loading configuration...");
            try
            {
                var config = new Configuration();
                
                // First try to load from config.json
                if (File.Exists(CONFIG_FILE))
                {
                    Debug.WriteLine("Found config.json, loading settings...");
                    try
                    {
                        var json = File.ReadAllText(CONFIG_FILE);
                        config = JsonSerializer.Deserialize<Configuration>(json) ?? new Configuration();
                        Debug.WriteLine("Successfully loaded settings from config.json");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error loading from config.json: {ex.Message}");
                    }
                }
                else
                {
                    Debug.WriteLine("No config.json found, will create new one after loading settings");
                }

                // Then load from Properties.Settings.Default
                var settings = Properties.Settings.Default;
                
                // Load AI settings if not already set
                if (string.IsNullOrEmpty(config.OpenAIApiKey))
                {
                    config.OpenAIApiKey = settings.OpenAIApiKey;
                    config.EnableOpenAI = settings.EnableOpenAI;
                }
                Debug.WriteLine($"OpenAI settings - Enabled: {config.EnableOpenAI}, Key length: {config.OpenAIApiKey?.Length ?? 0}");

                if (string.IsNullOrEmpty(config.AnthropicApiKey))
                {
                    config.AnthropicApiKey = settings.AnthropicApiKey;
                    config.EnableAnthropic = settings.EnableAnthropic;
                }
                Debug.WriteLine($"Anthropic settings - Enabled: {config.EnableAnthropic}, Key length: {config.AnthropicApiKey?.Length ?? 0}");

                if (string.IsNullOrEmpty(config.XAIApiKey))
                {
                    config.XAIApiKey = settings.XAIApiKey;
                    config.EnableXAI = settings.EnableXAI;
                }
                Debug.WriteLine($"xAI settings - Enabled: {config.EnableXAI}, Key length: {config.XAIApiKey?.Length ?? 0}");

                if (string.IsNullOrEmpty(config.OllamaUrl))
                {
                    config.EnableOllama = settings.EnableOllama;
                    config.OllamaUrl = settings.OllamaUrl;
                    config.OllamaModel = settings.OllamaModel;
                }
                Debug.WriteLine($"Ollama settings - Enabled: {config.EnableOllama}, URL: {config.OllamaUrl}");

                // Load other settings if not already set
                if (string.IsNullOrEmpty(config.WeatherApiKey))
                {
                    config.WeatherApiKey = settings.WeatherApiKey;
                    config.EnableWeather = settings.EnableWeather;
                    config.WeatherZipCode = settings.WeatherZipCode;
                    config.EnableMoonPhase = settings.EnableMoonPhase;
                }
                Debug.WriteLine($"Weather settings - Enabled: {config.EnableWeather}, ZIP: {config.WeatherZipCode}, Key length: {config.WeatherApiKey?.Length ?? 0}");

                // Load sync settings if not already set
                if (string.IsNullOrEmpty(config.SyncServerUrl))
                {
                    config.SyncServerUrl = settings.SyncServerUrl;
                    config.SyncUsername = settings.SyncUsername;
                    config.SyncPassword = settings.SyncPassword;
                    config.AutoSync = settings.AutoSync;
                    config.SyncIntervalMinutes = settings.SyncIntervalMinutes;
                }
                Debug.WriteLine($"Sync settings - Server: {config.SyncServerUrl}, Username: {config.SyncUsername}, Password length: {config.SyncPassword?.Length ?? 0}, Auto sync: {config.AutoSync}");

                // Always save the merged configuration back to config.json
                config.Save();
                Debug.WriteLine("Saved merged configuration to config.json");

                return config;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading configuration: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return new Configuration();
            }
        }

        public void MigrateFromOldSettings()
        {
            try
            {
                var settings = Properties.Settings.Default;

                // Check if settings have already been migrated
                if (settings.SettingsMigrated)
                {
                    return;
                }

                // Migrate theme settings
                if (!string.IsNullOrEmpty(settings.CurrentTheme))
                {
                    CurrentTheme = settings.CurrentTheme;
                }

                // Migrate Subsonic settings
                if (!string.IsNullOrEmpty(settings.SubsonicUrl))
                {
                    SubsonicUrl = settings.SubsonicUrl;
                    SubsonicUsername = settings.SubsonicUsername;
                    SubsonicPassword = settings.SubsonicPassword;
                    SubsonicName = settings.SubsonicName;
                }

                // Migrate Jellyfin settings
                if (!string.IsNullOrEmpty(settings.JellyfinUrl))
                {
                    JellyfinUrl = settings.JellyfinUrl;
                    JellyfinUsername = settings.JellyfinUsername;
                    JellyfinPassword = settings.JellyfinPassword;
                    JellyfinName = settings.JellyfinName;
                }

                // Migrate Matrix settings
                if (!string.IsNullOrEmpty(settings.MatrixUrl))
                {
                    MatrixServerUrl = settings.MatrixUrl;
                    MatrixUsername = settings.MatrixUsername;
                    MatrixPassword = settings.MatrixPassword;
                    MatrixAccessToken = settings.MatrixAccessToken;
                    MatrixName = settings.MatrixName;
                }

                // Migrate UI state settings
                LastLibraryWidth = settings.LastLibraryWidth;
                LastChatWidth = settings.LastChatWidth;
                LibraryPath = settings.LibraryPath;

                // Save the migrated settings
                Save();

                // Mark settings as migrated
                settings.SettingsMigrated = true;
                settings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error migrating settings: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                // Don't throw - just log the error and continue
            }
        }

        public void MergeFrom(Configuration other)
        {
            if (other == null) return;

            // Merge settings that should be synced
            EnableOpenAI = other.EnableOpenAI;
            OpenAIApiKey = other.OpenAIApiKey;
            EnableAnthropic = other.EnableAnthropic;
            AnthropicApiKey = other.AnthropicApiKey;
            EnableXAI = other.EnableXAI;
            XAIApiKey = other.XAIApiKey;
            EnableOllama = other.EnableOllama;
            OllamaUrl = other.OllamaUrl;
            OllamaModel = other.OllamaModel;
            EnableAIChat = other.EnableAIChat;
            UseBetaChains = other.UseBetaChains;
            DefaultAIProvider = other.DefaultAIProvider;
            CurrentTheme = other.CurrentTheme;
            _themes = new Dictionary<string, Dictionary<string, Color>>(other._themes);
            
            // Theme colors
            DarkModePlayingColor = other.DarkModePlayingColor;
            LightModePlayingColor = other.LightModePlayingColor;
            DarkModePausedColor = other.DarkModePausedColor;
            LightModePausedColor = other.LightModePausedColor;
            DarkModeTextColor = other.DarkModeTextColor;
            LightModeTextColor = other.LightModeTextColor;
            DarkModeActiveTabColor = other.DarkModeActiveTabColor;
            DarkModeInactiveTabColor = other.DarkModeInactiveTabColor;
            LightModeActiveTabColor = other.LightModeActiveTabColor;
            LightModeInactiveTabColor = other.LightModeInactiveTabColor;
            DarkModePlayingTrackColor = other.DarkModePlayingTrackColor;
            LightModePlayingTrackColor = other.LightModePlayingTrackColor;
            DarkModeMediaControlsColor = other.DarkModeMediaControlsColor;
            LightModeMediaControlsColor = other.LightModeMediaControlsColor;

            // Media settings
            LastVolume = other.LastVolume;

            // Do not merge sensitive settings like API keys and passwords
            // Do not merge local paths and UI state
            
            Save();
        }
    }
} 