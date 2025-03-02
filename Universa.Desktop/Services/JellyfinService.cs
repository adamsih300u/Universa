using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class JellyfinService
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private JellyfinClient _client;

        public JellyfinService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            
            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;
            
            // Initialize client
            InitializeClient();
            
            // Log current configuration
            System.Diagnostics.Debug.WriteLine($"JellyfinService: Current configuration - URL: {_config.JellyfinUrl}, Username: {_config.JellyfinUsername}");
        }

        private void InitializeClient()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("JellyfinService: Initializing client...");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: URL: {_config.JellyfinUrl}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Username: {_config.JellyfinUsername}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Password length: {(_config.JellyfinPassword?.Length ?? 0)}");

                if (!string.IsNullOrEmpty(_config.JellyfinUrl) &&
                    !string.IsNullOrEmpty(_config.JellyfinUsername) &&
                    !string.IsNullOrEmpty(_config.JellyfinPassword))
                {
                    _client = new JellyfinClient(
                        _config.JellyfinUrl,
                        _config.JellyfinUsername,
                        _config.JellyfinPassword
                    );
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: Client initialized with URL {_config.JellyfinUrl}");
                }
                else
                {
                    _client = null;
                    System.Diagnostics.Debug.WriteLine("JellyfinService: Client not initialized due to missing configuration");
                    if (string.IsNullOrEmpty(_config.JellyfinUrl)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing URL");
                    if (string.IsNullOrEmpty(_config.JellyfinUsername)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing Username");
                    if (string.IsNullOrEmpty(_config.JellyfinPassword)) System.Diagnostics.Debug.WriteLine("JellyfinService: Missing Password");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Error initializing client - {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"JellyfinService: Stack trace - {ex.StackTrace}");
                _client = null;
            }
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            try
            {
                // Reinitialize client if Jellyfin settings change
                if (e.Key.StartsWith("services.jellyfin"))
                {
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: Configuration changed - {e.Key}");
                    System.Diagnostics.Debug.WriteLine($"JellyfinService: New value - {e.NewValue}");
                    InitializeClient();
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
            if (_client == null)
            {
                System.Diagnostics.Debug.WriteLine("JellyfinService: Authenticate called but client is null, attempting to initialize");
                InitializeClient();
                if (_client == null)
                {
                    System.Diagnostics.Debug.WriteLine("JellyfinService: Failed to initialize client for authentication");
                    return false;
                }
            }
            return await _client.Authenticate();
        }

        public async Task<List<MediaItem>> GetLibraries()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Jellyfin client is not initialized. Please check your configuration.");
            }
            return await _client.GetLibraries();
        }

        public async Task<List<MediaItem>> GetLibraryItems(string libraryId)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Jellyfin client is not initialized. Please check your configuration.");
            }
            return await _client.GetLibraryItems(libraryId);
        }

        public async Task<List<MediaItem>> GetItems(string itemParentId)
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Jellyfin client is not initialized. Please check your configuration.");
            }
            return await _client.GetItems(itemParentId);
        }

        public async Task<List<MediaItem>> GetMediaLibraryAsync()
        {
            if (_client == null)
            {
                throw new InvalidOperationException("Jellyfin client is not initialized. Please check your configuration.");
            }
            return await _client.GetMediaLibraryAsync();
        }
    }
} 