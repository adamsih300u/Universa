using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Media;
using System.Collections.Generic;
using System.Linq;
using System.Configuration;
using Universa.Desktop.Core.Theme;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Universa.Desktop.Core.Configuration
{
    public class ConfigurationProvider : INotifyPropertyChanged
    {
        private static ConfigurationProvider _instance;
        private static readonly object _lock = new();
        private readonly ConfigurationManager _configManager;
        private readonly IConfigurationStore _store;
        private bool _isAccessingProperty;
        private Configuration _configuration;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<ConfigurationChangedEventArgs> ConfigurationChanged;

        public ConfigurationProvider(IConfigurationStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _configManager = ConfigurationManager.Instance;
            _configManager.ConfigurationChanged += OnConfigurationManagerChanged;
            _configuration = new Configuration();
        }

        public static ConfigurationProvider Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ConfigurationProvider(ConfigurationManager.Instance);
                    }
                }
                return _instance;
            }
        }

        #region Sync Settings
        public string SyncServerUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Sync.ServerUrl);
            set
            {
                _configManager.Set(ConfigurationKeys.Sync.ServerUrl, value);
                OnPropertyChanged();
            }
        }

        public string SyncUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.Sync.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.Sync.Username, value);
                OnPropertyChanged();
            }
        }

        public string SyncPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.Sync.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.Sync.Password, value);
                OnPropertyChanged();
            }
        }

        public bool AutoSync
        {
            get => _configManager.Get<bool>(ConfigurationKeys.Sync.AutoSync);
            set
            {
                _configManager.Set(ConfigurationKeys.Sync.AutoSync, value);
                OnPropertyChanged();
            }
        }

        public int SyncIntervalMinutes
        {
            get => _configManager.Get<int>(ConfigurationKeys.Sync.IntervalMinutes, 30);
            set
            {
                _configManager.Set(ConfigurationKeys.Sync.IntervalMinutes, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region WebDAV Sync Settings
        public string WebDavServerUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.WebDav.ServerUrl);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.ServerUrl, value);
                OnPropertyChanged();
            }
        }

        public string WebDavUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.WebDav.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.Username, value);
                OnPropertyChanged();
            }
        }

        public string WebDavPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.WebDav.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.Password, value);
                OnPropertyChanged();
            }
        }

        public string WebDavRemoteFolder
        {
            get => _configManager.Get<string>(ConfigurationKeys.WebDav.RemoteFolder);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.RemoteFolder, value);
                OnPropertyChanged();
            }
        }

        public bool WebDavAutoSync
        {
            get => _configManager.Get<bool>(ConfigurationKeys.WebDav.AutoSync);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.AutoSync, value);
                OnPropertyChanged();
            }
        }

        public int WebDavSyncIntervalMinutes
        {
            get => _configManager.Get<int>(ConfigurationKeys.WebDav.IntervalMinutes, 15);
            set
            {
                _configManager.Set(ConfigurationKeys.WebDav.IntervalMinutes, value);
                OnPropertyChanged();
            }
        }

        public DateTime? LastWebDavSyncTime { get; set; }
        #endregion

        #region Weather Settings
        public string WeatherApiKey
        {
            get => _configManager.Get<string>(ConfigurationKeys.Weather.ApiKey);
            set
            {
                _configManager.Set(ConfigurationKeys.Weather.ApiKey, value);
                OnPropertyChanged();
            }
        }

        public string WeatherZipCode
        {
            get => _configManager.Get<string>(ConfigurationKeys.Weather.ZipCode);
            set
            {
                _configManager.Set(ConfigurationKeys.Weather.ZipCode, value);
                OnPropertyChanged();
            }
        }

        public bool EnableWeather
        {
            get => _configManager.Get<bool>(ConfigurationKeys.Weather.Enabled);
            set
            {
                _configManager.Set(ConfigurationKeys.Weather.Enabled, value);
                OnPropertyChanged();
            }
        }

        public bool EnableMoonPhase
        {
            get => _configManager.Get<bool>(ConfigurationKeys.Weather.MoonPhaseEnabled);
            set
            {
                _configManager.Set(ConfigurationKeys.Weather.MoonPhaseEnabled, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region AI Settings
        public bool EnableOpenAI
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.OpenAIEnabled, false);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OpenAIEnabled, value);
                OnPropertyChanged();
                OnConfigurationChanged(ConfigurationKeys.AI.OpenAIEnabled, !value, value);
            }
        }

        public string OpenAIApiKey
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.OpenAIApiKey);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OpenAIApiKey, value);
                OnPropertyChanged();
            }
        }

        public bool EnableAnthropic
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.AnthropicEnabled, false);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.AnthropicEnabled, value);
                OnPropertyChanged();
                OnConfigurationChanged(ConfigurationKeys.AI.AnthropicEnabled, !value, value);
            }
        }

        public string AnthropicApiKey
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.AnthropicApiKey);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.AnthropicApiKey, value);
                OnPropertyChanged();
            }
        }

        public bool EnableOpenRouter
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.OpenRouterEnabled, false);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OpenRouterEnabled, value);
                OnPropertyChanged();
                OnConfigurationChanged(ConfigurationKeys.AI.OpenRouterEnabled, !value, value);
            }
        }

        public string OpenRouterApiKey
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.OpenRouterApiKey);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OpenRouterApiKey, value);
                OnPropertyChanged();
            }
        }

        public List<string> OpenRouterModels
        {
            get => _configManager.Get<List<string>>(ConfigurationKeys.AI.OpenRouterModels, new List<string>());
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OpenRouterModels, value);
                OnPropertyChanged();
                OnConfigurationChanged(ConfigurationKeys.AI.OpenRouterModels, null, value);
            }
        }

        public bool UseBetaChains
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.UseBetaChains);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.UseBetaChains, value);
                OnPropertyChanged();
            }
        }

        public bool EnableLocalEmbeddings
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.LocalEmbeddingsEnabled);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.LocalEmbeddingsEnabled, value);
                OnPropertyChanged();
            }
        }

        public bool EnableXAI
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.XAIEnabled);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.XAIEnabled, value);
                OnPropertyChanged();
            }
        }

        public string XAIApiKey
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.XAIApiKey);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.XAIApiKey, value);
                OnPropertyChanged();
            }
        }

        public bool EnableOllama
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.OllamaEnabled);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OllamaEnabled, value);
                OnPropertyChanged();
            }
        }

        public string OllamaUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.OllamaUrl);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OllamaUrl, value);
                OnPropertyChanged();
            }
        }

        public string OllamaModel
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.OllamaModel);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.OllamaModel, value);
                OnPropertyChanged();
            }
        }

        public bool EnableAIChat
        {
            get => _configManager.Get<bool>(ConfigurationKeys.AI.ChatEnabled);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.ChatEnabled, value);
                OnPropertyChanged();
            }
        }

        public string DefaultChatPersona
        {
            get => _configManager.Get<string>(ConfigurationKeys.AI.DefaultChatPersona);
            set
            {
                _configManager.Set(ConfigurationKeys.AI.DefaultChatPersona, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region Theme Settings
        public string CurrentTheme
        {
            get => _configManager.Get<string>(ConfigurationKeys.Theme.Current);
            set
            {
                _configManager.Set(ConfigurationKeys.Theme.Current, value);
                OnPropertyChanged();
            }
        }

        public Theme.Theme GetTheme(string themeName)
        {
            if (string.IsNullOrEmpty(themeName))
                return null;

            var theme = new Theme.Theme(themeName);

            // Try to load custom colors from configuration if they exist
            var prefix = $"theme.{themeName.ToLower()}.";

            if (_configManager.HasKey($"{prefix}windowBackground"))
                theme.WindowBackground = ColorFromString(_configManager.Get<string>($"{prefix}windowBackground"));
            if (_configManager.HasKey($"{prefix}menuBackground"))
                theme.MenuBackground = ColorFromString(_configManager.Get<string>($"{prefix}menuBackground"));
            if (_configManager.HasKey($"{prefix}menuForeground"))
                theme.MenuForeground = ColorFromString(_configManager.Get<string>($"{prefix}menuForeground"));
            if (_configManager.HasKey($"{prefix}tabBackground"))
                theme.TabBackground = ColorFromString(_configManager.Get<string>($"{prefix}tabBackground"));
            if (_configManager.HasKey($"{prefix}tabForeground"))
                theme.TabForeground = ColorFromString(_configManager.Get<string>($"{prefix}tabForeground"));
            if (_configManager.HasKey($"{prefix}activeTabBackground"))
                theme.ActiveTabBackground = ColorFromString(_configManager.Get<string>($"{prefix}activeTabBackground"));
            if (_configManager.HasKey($"{prefix}activeTabForeground"))
                theme.ActiveTabForeground = ColorFromString(_configManager.Get<string>($"{prefix}activeTabForeground"));
            if (_configManager.HasKey($"{prefix}contentBackground"))
                theme.ContentBackground = ColorFromString(_configManager.Get<string>($"{prefix}contentBackground"));
            if (_configManager.HasKey($"{prefix}contentForeground"))
                theme.ContentForeground = ColorFromString(_configManager.Get<string>($"{prefix}contentForeground"));
            if (_configManager.HasKey($"{prefix}accentColor"))
                theme.AccentColor = ColorFromString(_configManager.Get<string>($"{prefix}accentColor"));

            return theme;
        }

        private Color ColorFromString(string colorString)
        {
            try
            {
                if (colorString.StartsWith("#"))
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorString);
                    return color;
                }
                else
                {
                    // Handle RGB format "R,G,B"
                    var parts = colorString.Split(',');
                    if (parts.Length == 3 &&
                        byte.TryParse(parts[0], out byte r) &&
                        byte.TryParse(parts[1], out byte g) &&
                        byte.TryParse(parts[2], out byte b))
                    {
                        return Color.FromRgb(r, g, b);
                    }
                }
            }
            catch (Exception)
            {
                // Return a default color if parsing fails
                return Colors.White;
            }

            return Colors.White;
        }

        public void SetThemeColor(string themeName, string colorName, Color color)
        {
            var key = $"theme.{themeName.ToLower()}.{colorName.ToLower()}";
            var colorStr = color.ToString();
            _configManager.Set(key, colorStr);
            OnPropertyChanged($"Theme_{themeName}_{colorName}");
        }

        public Color GetThemeColor(string themeName, string colorName)
        {
            var key = $"theme.{themeName.ToLower()}.{colorName.ToLower()}";
            var colorStr = _configManager.Get<string>(key);
            return ColorFromString(colorStr);
        }

        public IEnumerable<string> GetAvailableThemes()
        {
            var themes = new List<string> { "Light", "Dark" };
            // Add custom themes here if needed
            return themes;
        }

        public void DuplicateTheme(string sourceName, string newName)
        {
            var sourceTheme = GetTheme(sourceName);
            if (sourceTheme == null)
                return;

            var sourcePrefix = $"theme.{sourceName.ToLower()}.";
            var newPrefix = $"theme.{newName.ToLower()}.";

            foreach (var key in _configManager.GetAllKeys())
            {
                if (key.StartsWith(sourcePrefix))
                {
                    var colorName = key.Substring(sourcePrefix.Length);
                    var newKey = newPrefix + colorName;
                    var value = _configManager.Get<string>(key);
                    _configManager.Set(newKey, value);
                }
            }

            _configManager.Save();
        }

        public void DeleteTheme(string themeName)
        {
            if (themeName.Equals("Light", StringComparison.OrdinalIgnoreCase) ||
                themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            {
                return; // Don't allow deletion of built-in themes
            }

            var prefix = $"theme.{themeName.ToLower()}.";
            var keysToRemove = new List<string>();

            foreach (var key in _configManager.GetAllKeys())
            {
                if (key.StartsWith(prefix))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _configManager.RemoveKey(key);
            }

            _configManager.Save();
        }

        public string DarkModePlayingColor
        {
            get => _configManager.Get<string>(ConfigurationKeys.Theme.DarkModePlayingColor);
            set
            {
                _configManager.Set(ConfigurationKeys.Theme.DarkModePlayingColor, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region Library Settings
        public string LibraryPath
        {
            get
            {
                Debug.WriteLine("Getting LibraryPath");
                var path = _configManager.Get<string>(ConfigurationKeys.Library.Path);
                Debug.WriteLine($"LibraryPath value: {path}");
                return path;
            }
            set
            {
                Debug.WriteLine($"Setting LibraryPath to: {value}");
                _configManager.Set(ConfigurationKeys.Library.Path, value);
                OnPropertyChanged();
            }
        }

        public string[] ExpandedPaths
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.Library.ExpandedPaths) ?? Array.Empty<string>();
            set
            {
                _configManager.Set(ConfigurationKeys.Library.ExpandedPaths, value);
                OnPropertyChanged();
            }
        }

        public string[] RecentFiles
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.Library.RecentFiles) ?? Array.Empty<string>();
            set => _configManager.Set(ConfigurationKeys.Library.RecentFiles, value);
        }

        public string OpenTabs
        {
            get => _configManager.Get<string>(ConfigurationKeys.Library.OpenTabs) ?? string.Empty;
            set => _configManager.Set(ConfigurationKeys.Library.OpenTabs, value);
        }

        public double LastLibraryWidth
        {
            get => _configManager.Get<double>(ConfigurationKeys.Library.LastWidth);
            set => _configManager.Set(ConfigurationKeys.Library.LastWidth, value);
        }

        public bool IsLibraryExpanded
        {
            get => _configManager.Get<bool>(ConfigurationKeys.Library.IsExpanded);
            set => _configManager.Set(ConfigurationKeys.Library.IsExpanded, value);
        }
        #endregion

        #region Chat Settings
        public double LastChatWidth
        {
            get => _configManager.Get<double>(ConfigurationKeys.Chat.LastWidth);
            set => _configManager.Set(ConfigurationKeys.Chat.LastWidth, value);
        }

        public bool IsChatExpanded
        {
            get => _configManager.Get<bool>(ConfigurationKeys.Chat.IsExpanded, true);
            set => _configManager.Set(ConfigurationKeys.Chat.IsExpanded, value);
        }
        #endregion

        #region Service Settings
        public string RssServerUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.RSS.Url);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.RSS.Url, value);
                OnPropertyChanged();
            }
        }

        public string SubsonicUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Subsonic.Url);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Subsonic.Url, value);
                OnPropertyChanged();
            }
        }

        public string SubsonicUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Subsonic.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Subsonic.Username, value);
                OnPropertyChanged();
            }
        }

        public string SubsonicPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Subsonic.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Subsonic.Password, value);
                OnPropertyChanged();
            }
        }

        public string SubsonicName
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Subsonic.Name);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Subsonic.Name, value);
                OnPropertyChanged();
            }
        }

        public string JellyfinUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Jellyfin.Url);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Jellyfin.Url, value);
                OnPropertyChanged();
            }
        }

        public string JellyfinUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Jellyfin.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Jellyfin.Username, value);
                OnPropertyChanged();
            }
        }

        public string JellyfinPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Jellyfin.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Jellyfin.Password, value);
                OnPropertyChanged();
            }
        }

        public string JellyfinName
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Jellyfin.Name);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Jellyfin.Name, value);
                OnPropertyChanged();
            }
        }

        // Audiobookshelf settings
        public string AudiobookshelfUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Audiobookshelf.Url);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Audiobookshelf.Url, value);
                OnPropertyChanged();
            }
        }

        public string AudiobookshelfUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Audiobookshelf.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Audiobookshelf.Username, value);
                OnPropertyChanged();
            }
        }

        public string AudiobookshelfPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Audiobookshelf.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Audiobookshelf.Password, value);
                OnPropertyChanged();
            }
        }

        public string AudiobookshelfName
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Audiobookshelf.Name);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Audiobookshelf.Name, value);
                OnPropertyChanged();
            }
        }

        // Matrix settings
        public string MatrixServerUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Matrix.Url);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Matrix.Url, value);
                OnPropertyChanged();
            }
        }

        public string MatrixUsername
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Matrix.Username);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Matrix.Username, value);
                OnPropertyChanged();
            }
        }

        public string MatrixPassword
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Matrix.Password);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Matrix.Password, value);
                OnPropertyChanged();
            }
        }

        public string MatrixName
        {
            get => _configManager.Get<string>(ConfigurationKeys.Services.Matrix.Name);
            set
            {
                _configManager.Set(ConfigurationKeys.Services.Matrix.Name, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region TTS Settings
        public bool EnableTTS
        {
            get => _configManager.Get<bool>(ConfigurationKeys.TTS.Enabled);
            set
            {
                _configManager.Set(ConfigurationKeys.TTS.Enabled, value);
                OnPropertyChanged();
            }
        }

        public string TTSApiUrl
        {
            get => _configManager.Get<string>(ConfigurationKeys.TTS.ApiUrl);
            set
            {
                _configManager.Set(ConfigurationKeys.TTS.ApiUrl, value);
                OnPropertyChanged();
            }
        }

        public string TTSVoice
        {
            get => _configManager.Get<string>(ConfigurationKeys.TTS.Voice);
            set
            {
                _configManager.Set(ConfigurationKeys.TTS.Voice, value);
                OnPropertyChanged();
            }
        }

        public string TTSDefaultVoice
        {
            get => _configManager.Get<string>(ConfigurationKeys.TTS.DefaultVoice);
            set
            {
                _configManager.Set(ConfigurationKeys.TTS.DefaultVoice, value);
                OnPropertyChanged();
            }
        }

        public List<string> TTSAvailableVoices
        {
            get => _configManager.Get<List<string>>(ConfigurationKeys.TTS.AvailableVoices) ?? new List<string>();
            set
            {
                _configManager.Set(ConfigurationKeys.TTS.AvailableVoices, value);
                OnPropertyChanged();
            }
        }
        #endregion

        #region Org-Mode Settings
        public bool EnableGlobalAgenda
        {
            get => _configManager.Get<bool>(ConfigurationKeys.OrgMode.EnableGlobalAgenda, false);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.EnableGlobalAgenda, value);
                OnPropertyChanged();
            }
        }

        public int AgendaDaysAhead
        {
            get => _configManager.Get<int>(ConfigurationKeys.OrgMode.AgendaDaysAhead, 7);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.AgendaDaysAhead, value);
                OnPropertyChanged();
            }
        }

        public int AgendaDaysBehind
        {
            get => _configManager.Get<int>(ConfigurationKeys.OrgMode.AgendaDaysBehind, 30);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.AgendaDaysBehind, value);
                OnPropertyChanged();
            }
        }

        public string[] TodoTags
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.TodoTags) ?? GetDefaultTodoTags();
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.TodoTags, value);
                OnPropertyChanged();
            }
        }

        private string[] GetDefaultTodoTags()
        {
            return new[] { "work", "personal", "urgent", "project", "meeting", "home" };
        }

        public bool TagCyclingReplacesAll
        {
            get => _configManager.Get<bool>(ConfigurationKeys.OrgMode.TagCyclingReplacesAll, false);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.TagCyclingReplacesAll, value);
                OnPropertyChanged();
            }
        }

        public string[] OrgAgendaFiles
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.AgendaFiles) ?? Array.Empty<string>();
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.AgendaFiles, value);
                OnPropertyChanged();
            }
        }

        public string[] OrgAgendaDirectories
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.AgendaDirectories) ?? Array.Empty<string>();
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.AgendaDirectories, value);
                OnPropertyChanged();
            }
        }

        public string[] OrgTodoStates
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.TodoStates) ?? new[] { "TODO", "NEXT", "STARTED", "PROJECT" };
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.TodoStates, value);
                OnPropertyChanged();
            }
        }

        public string[] OrgDoneStates
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.DoneStates) ?? new[] { "DONE", "CANCELLED" };
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.DoneStates, value);
                OnPropertyChanged();
            }
        }

        public string[] OrgNoActionStates
        {
            get => _configManager.Get<string[]>(ConfigurationKeys.OrgMode.NoActionStates) ?? new[] { "DELEGATED", "SOMEDAY", "WAITING", "DEFERRED" };
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.NoActionStates, value);
                OnPropertyChanged();
            }
        }

        // TODO State Colors
        public Dictionary<string, string> OrgStateColors
        {
            get => _configManager.Get<Dictionary<string, string>>(ConfigurationKeys.OrgMode.StateColors) ?? GetDefaultStateColors();
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.StateColors, value);
                OnPropertyChanged();
            }
        }

        public Dictionary<string, string> GetDefaultStateColors()
        {
            return new Dictionary<string, string>
            {
                // TODO States
                { "TODO", "#FF8C00" },       // Dark Orange
                { "NEXT", "#1E90FF" },       // Dodger Blue  
                { "STARTED", "#DAA520" },    // Goldenrod
                { "PROJECT", "#9932CC" },    // Dark Violet
                
                // Done States
                { "DONE", "#228B22" },       // Forest Green
                { "CANCELLED", "#696969" },  // Dim Gray
                
                // No Action States
                { "DELEGATED", "#9370DB" },  // Medium Purple
                { "SOMEDAY", "#708090" },    // Slate Gray
                { "WAITING", "#DC143C" },    // Crimson
                { "DEFERRED", "#9370DB" },   // Medium Purple
            };
        }

        public string GetStateColor(string stateName)
        {
            var colors = OrgStateColors;
            return colors.ContainsKey(stateName) ? colors[stateName] : "#888888"; // Default gray
        }

        public void SetStateColor(string stateName, string color)
        {
            var colors = OrgStateColors;
            colors[stateName] = color;
            OrgStateColors = colors;
        }

        public Dictionary<string, string> OrgQuickRefileTargets
        {
            get => _configManager.Get<Dictionary<string, string>>(ConfigurationKeys.OrgMode.QuickRefileTargets) ?? GetDefaultQuickRefileTargets();
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.QuickRefileTargets, value);
                OnPropertyChanged();
            }
        }

        public Dictionary<string, string> GetDefaultQuickRefileTargets()
        {
            return new Dictionary<string, string>
            {
                { "INBOX", "inbox.org" },
                { "TASKS", "tasks.org" },
                { "PROJECTS", "projects.org::*Projects" },
                { "SOMEDAY", "someday.org" },
                { "REFERENCE", "reference.org" }
            };
        }

        public string InboxFilePath
        {
            get => _configManager.Get<string>(ConfigurationKeys.OrgMode.InboxFilePath);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.InboxFilePath, value);
                OnPropertyChanged();
            }
        }

        public bool AddTimestampToCapture
        {
            get => _configManager.Get<bool>(ConfigurationKeys.OrgMode.AddTimestampToCapture, true);
            set
            {
                _configManager.Set(ConfigurationKeys.OrgMode.AddTimestampToCapture, value);
                OnPropertyChanged();
            }
        }
        #endregion

        public double LastVolume
        {
            get => _configManager.Get<double>(ConfigurationKeys.Media.LastVolume, 1.0);
            set
            {
                _configManager.Set(ConfigurationKeys.Media.LastVolume, value);
                OnPropertyChanged();
            }
        }

        private void OnConfigurationManagerChanged(object sender, ConfigurationChangedEventArgs e)
        {
            OnConfigurationChanged(e.Key, e.OldValue, e.NewValue);
        }

        protected virtual void OnConfigurationChanged(string key, object oldValue, object newValue)
        {
            ConfigurationChanged?.Invoke(this, new ConfigurationChangedEventArgs(key, oldValue, newValue));
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Save()
        {
            _configManager.Save();
        }

        public async Task LoadAsync()
        {
            Debug.WriteLine("ConfigurationProvider.LoadAsync starting");
            try
            {
                _configuration = await _store.LoadAsync();
                if (_configuration == null)
                {
                    Debug.WriteLine("Loaded configuration was null, creating new instance");
                    _configuration = new Configuration();
                }
                
                if (_configuration.Values == null)
                {
                    Debug.WriteLine("Configuration Values dictionary was null, initializing");
                    _configuration.Values = new Dictionary<string, object>();
                }

                Debug.WriteLine($"Loaded configuration with {_configuration.Values.Count} values");
                foreach (var kvp in _configuration.Values)
                {
                    Debug.WriteLine($"Loaded key: {kvp.Key}, value: {kvp.Value}");
                }

                OnConfigurationChanged("Configuration", null, _configuration);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ConfigurationProvider.LoadAsync: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task SaveAsync()
        {
            await _store.SaveAsync(_configuration);
        }

        public T GetValue<T>(string key)
        {
            return _configManager.Get<T>(key);
        }

        public void SetValue<T>(string key, T value)
        {
            _configManager.Set(key, value);
            OnConfigurationChanged(key, null, value);
        }

        public void SetIfNotExists<T>(string key, T value)
        {
            if (!_configManager.HasKey(key))
            {
                SetValue(key, value);
            }
        }

        public void ResetToDefaults(IDictionary<string, object> defaultValues)
        {
            _configManager.ResetToDefaults(defaultValues);
            OnConfigurationChanged("Configuration", null, defaultValues);
        }
    }
} 