namespace Universa.Desktop.Core.Configuration
{
    public static class ConfigurationKeys
    {
        // Sync Settings
        public static class Sync
        {
            public const string ServerUrl = "sync.serverUrl";
            public const string Username = "sync.username";
            public const string Password = "sync.password";
            public const string AutoSync = "sync.autoSync";
            public const string IntervalMinutes = "sync.intervalMinutes";
        }

        // WebDAV Sync Settings
        public static class WebDav
        {
            public const string ServerUrl = "webdav.serverUrl";
            public const string Username = "webdav.username";
            public const string Password = "webdav.password";
            public const string RemoteFolder = "webdav.remoteFolder";
            public const string AutoSync = "webdav.autoSync";
            public const string IntervalMinutes = "webdav.intervalMinutes";
        }

        // Weather Settings
        public static class Weather
        {
            public const string ApiKey = "weather.apiKey";
            public const string ZipCode = "weather.zipCode";
            public const string Enabled = "weather.enabled";
            public const string MoonPhaseEnabled = "weather.moonPhaseEnabled";
        }

        // AI Settings
        public static class AI
        {
            public const string OpenAIEnabled = "ai.openai.enabled";
            public const string OpenAIApiKey = "ai.openai.apiKey";
            public const string AnthropicEnabled = "ai.anthropic.enabled";
            public const string AnthropicApiKey = "ai.anthropic.apiKey";
            public const string XAIEnabled = "ai.xai.enabled";
            public const string XAIApiKey = "ai.xai.apiKey";
            public const string OllamaEnabled = "ai.ollama.enabled";
            public const string OllamaUrl = "ai.ollama.url";
            public const string OllamaModel = "ai.ollama.model";
            public const string OpenRouterEnabled = "ai.openrouter.enabled";
            public const string OpenRouterApiKey = "ai.openrouter.apiKey";
            public const string OpenRouterModels = "ai.openrouter.models";
            public const string LocalEmbeddingsEnabled = "ai.embeddings.enabled";
            public const string ChatEnabled = "ai.chat.enabled";
            public const string UseBetaChains = "ai.chat.useBetaChains";
            public const string DefaultChatPersona = "ai.chat.defaultPersona";
        }

        // Theme Settings
        public static class Theme
        {
            public const string Current = "theme.current";
            public const string DarkModePlayingColor = "theme.darkMode.playingColor";
            public const string LightModePlayingColor = "theme.lightMode.playingColor";
            public const string DarkModePausedColor = "theme.darkMode.pausedColor";
            public const string LightModePausedColor = "theme.lightMode.pausedColor";
            public const string DarkModeTextColor = "theme.darkMode.textColor";
            public const string LightModeTextColor = "theme.lightMode.textColor";
        }

        // Library Settings
        public static class Library
        {
            public const string Path = "Library:Path";
            public const string RecentFiles = "library.recentFiles";
            public const string OpenTabs = "library.openTabs";
            public const string ActiveTab = "library.activeTab";
            public const string LastWidth = "library.lastWidth";
            public const string IsExpanded = "library.isExpanded";
            public const string ExpandedPaths = "Library:ExpandedPaths";
        }

        // Media Settings
        public static class Media
        {
            public const string LastVolume = "Media:LastVolume";
            public const string OpenTabs = "media.openTabs";
        }

        // Service Settings
        public static class Services
        {
            public static class Subsonic
            {
                public const string Url = "Services:Subsonic:Url";
                public const string Username = "Services:Subsonic:Username";
                public const string Password = "Services:Subsonic:Password";
                public const string Name = "Services:Subsonic:Name";
            }

            public static class Jellyfin
            {
                public const string Url = "services.jellyfin.url";
                public const string Username = "services.jellyfin.username";
                public const string Password = "services.jellyfin.password";
                public const string Name = "services.jellyfin.name";
            }

            public static class Matrix
            {
                public const string Url = "services.matrix.url";
                public const string Username = "services.matrix.username";
                public const string Password = "services.matrix.password";
                public const string Name = "services.matrix.name";
            }

            public static class RSS
            {
                public const string Url = "services.rss.url";
            }

            public static class Audiobookshelf
            {
                public const string Url = "services.audiobookshelf.url";
                public const string Username = "services.audiobookshelf.username";
                public const string Password = "services.audiobookshelf.password";
                public const string Name = "services.audiobookshelf.name";
            }
        }

        // Chat Settings
        public static class Chat
        {
            public const string LastWidth = "Chat.LastWidth";
            public const string IsExpanded = "Chat.IsExpanded";
        }

        // TTS Settings
        public static class TTS
        {
            public const string Enabled = "tts.enabled";
            public const string ApiUrl = "tts.apiUrl";
            public const string Voice = "tts.voice";
            public const string DefaultVoice = "tts.defaultVoice";
            public const string AvailableVoices = "tts.availableVoices";
        }

        // Window Settings
        public static class Window
        {
            public const string Left = "window.left";
            public const string Top = "window.top";
            public const string Width = "window.width";
            public const string Height = "window.height";
            public const string State = "window.state";
        }

        // Editor Settings
        public static class Editor
        {
            public const string Font = "editor.font";
            public const string FontSize = "editor.fontSize";
        }

        // Org-Mode Settings
        public static class OrgMode
        {
            public const string AgendaFiles = "orgmode.agendaFiles";
            public const string AgendaDirectories = "orgmode.agendaDirectories";
            public const string TodoStates = "orgmode.todoStates";
            public const string DoneStates = "orgmode.doneStates";
            public const string NoActionStates = "orgmode.noActionStates";
            public const string CustomStates = "orgmode.customStates";
            public const string EnableGlobalAgenda = "orgmode.enableGlobalAgenda";
            public const string AgendaDaysAhead = "orgmode.agendaDaysAhead";
            public const string AgendaDaysBehind = "orgmode.agendaDaysBehind";
            public const string TodoTags = "orgmode.todoTags";
            public const string TagCyclingReplacesAll = "orgmode.tagCyclingReplacesAll";
            public const string StateColors = "orgmode.stateColors";
            public const string QuickRefileTargets = "orgmode.quickRefileTargets";
            public const string InboxFilePath = "orgmode.inboxFilePath";
            public const string AddTimestampToCapture = "orgmode.addTimestampToCapture";
        }
    }
} 