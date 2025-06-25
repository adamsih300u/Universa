using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class JellyfinService
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly HttpClient _httpClient;
        private JellyfinAuthService _authService;
        private JellyfinLibraryService _libraryService;
        private JellyfinStreamService _streamService;
        private JellyfinCacheService _cacheService;

        public JellyfinService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            
            // Initialize HttpClient with proper configuration
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            
            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;
            
            // Initialize services
            InitializeServices();
            
            // Log current configuration
            System.Diagnostics.Debug.WriteLine($"JellyfinService: Current configuration - URL: {_config.JellyfinUrl}, Username: {_config.JellyfinUsername}");
        }

        private void InitializeServices()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("JellyfinService: Initializing services...");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: URL: {_config.JellyfinUrl}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Username: {_config.JellyfinUsername}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Password length: {(_config.JellyfinPassword?.Length ?? 0)}");

                if (!string.IsNullOrEmpty(_config.JellyfinUrl) &&
                    !string.IsNullOrEmpty(_config.JellyfinUsername) &&
                    !string.IsNullOrEmpty(_config.JellyfinPassword))
                {
                    // Initialize cache service first
                    _cacheService = new JellyfinCacheService();
                    
                    // Initialize auth service
                    _authService = new JellyfinAuthService(
                        _httpClient,
                        _config.JellyfinUrl,
                        _config.JellyfinUsername,
                        _config.JellyfinPassword
                    );
                    
                    // Initialize dependent services
                    _libraryService = new JellyfinLibraryService(_httpClient, _authService, _cacheService);
                    _streamService = new JellyfinStreamService(_httpClient, _authService);
                    
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: Services initialized with URL {_config.JellyfinUrl}");
                }
                else
                {
                    _authService = null;
                    _libraryService = null;
                    _streamService = null;
                    _cacheService = null;
                    
                    System.Diagnostics.Debug.WriteLine("JellyfinService: Services not initialized due to missing configuration");
                    if (string.IsNullOrEmpty(_config.JellyfinUrl)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing URL");
                    if (string.IsNullOrEmpty(_config.JellyfinUsername)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing Username");
                    if (string.IsNullOrEmpty(_config.JellyfinPassword)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing Password");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Error initializing services - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Stack trace - {ex.StackTrace}");
                _authService = null;
                _libraryService = null;
                _streamService = null;
                _cacheService = null;
            }
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            try
            {
                // Reinitialize services if Jellyfin settings change
                if (e.Key.StartsWith("services.jellyfin"))
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: Configuration changed - {e.Key}");
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: New value - {e.NewValue}");
                    InitializeServices();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Error handling configuration change - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Stack trace - {ex.StackTrace}");
            }
        }

        public async Task<bool> Authenticate()
        {
            if (_authService == null)
            {
                System.Diagnostics.Debug.WriteLine("JellyfinService: Authenticate called but auth service is null, attempting to initialize");
                InitializeServices();
                if (_authService == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinService: Failed to initialize auth service for authentication");
                    return false;
                }
            }
            return await _authService.AuthenticateAsync();
        }

        public async Task<List<MediaItem>> GetLibraries()
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetLibrariesAsync();
        }

        public async Task<List<MediaItem>> GetLibraryItems(string libraryId)
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetLibraryItemsAsync(libraryId);
        }

        public async Task<List<MediaItem>> GetItems(string itemParentId)
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetItemsAsync(itemParentId);
        }

        public async Task<List<MediaItem>> GetMediaLibraryAsync()
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetLibrariesAsync();
        }

        public string GetStreamUrl(string itemId)
        {
            if (_streamService == null)
            {
                throw new InvalidOperationException("Jellyfin stream service is not initialized. Please check your configuration.");
            }
            return _streamService.GetStreamUrl(itemId);
        }

        public async Task<bool> MarkAsWatchedAsync(string itemId, bool watched)
        {
            if (_streamService == null)
            {
                throw new InvalidOperationException("Jellyfin stream service is not initialized. Please check your configuration.");
            }
            return await _streamService.MarkAsWatchedAsync(itemId, watched);
        }

        public async Task<List<MediaItem>> GetContinueWatchingAsync(bool isTV)
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetContinueWatchingAsync(isTV);
        }

        public async Task<List<MediaItem>> GetRecentlyAddedAsync(bool isTV)
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetRecentlyAddedAsync(isTV);
        }

        public async Task<List<MediaItem>> GetNextUpAsync()
        {
            if (_libraryService == null)
            {
                throw new InvalidOperationException("Jellyfin library service is not initialized. Please check your configuration.");
            }
            return await _libraryService.GetNextUpAsync();
        }

        public async Task<string> DiagnoseContinueWatchingAsync()
        {
            if (_libraryService == null)
            {
                return "Jellyfin library service is not initialized. Please check your configuration.";
            }
            return await _libraryService.DiagnoseContinueWatchingAsync();
        }

        public async Task ClearCacheAsync()
        {
            if (_cacheService != null)
            {
                await _cacheService.ClearCacheAsync();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
} 