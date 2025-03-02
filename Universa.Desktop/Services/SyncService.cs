using System;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class SyncService : ISyncService
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly Managers.SyncManager _syncManager;

        public SyncService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            _syncManager = Managers.SyncManager.GetInstance();
        }

        public void RefreshConfiguration()
        {
            // Update sync manager with new configuration
            _syncManager.UpdateCredentials();

            // If auto sync is enabled, restart sync timer
            if (_config.AutoSync)
            {
                _syncManager.StartAutoSync(_config.SyncIntervalMinutes);
            }
            else
            {
                _syncManager.StopAutoSync();
            }
        }
    }
} 