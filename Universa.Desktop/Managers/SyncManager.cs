using System;
using System.Threading.Tasks;
using System.Timers;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;
using Universa.Desktop.Models;

namespace Universa.Desktop.Managers
{
    public class SyncManager : IDisposable
    {
        private static SyncManager _instance;
        private static readonly object _lock = new object();
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly Timer _syncTimer;
        private bool _isDisposed;
        private bool _hasPendingChanges;

        public event EventHandler<SyncStatusEventArgs> SyncStatusChanged;

        public bool HasPendingChanges => _hasPendingChanges;

        private SyncManager(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;

            // Initialize sync timer
            _syncTimer = new Timer();
            _syncTimer.Elapsed += OnSyncTimerElapsed;

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;

            // Initialize sync settings
            UpdateSyncTimer();
        }

        public static SyncManager GetInstance()
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
                        _instance = new SyncManager(configService);
                    }
                }
            }
            return _instance;
        }

        public void UpdateSyncTimer()
        {
            if (_config.AutoSync)
            {
                _syncTimer.Interval = TimeSpan.FromMinutes(_config.SyncIntervalMinutes).TotalMilliseconds;
                _syncTimer.Start();
            }
            else
            {
                _syncTimer.Stop();
            }
        }

        public void StartAutoSync(int intervalMinutes)
        {
            _syncTimer.Interval = TimeSpan.FromMinutes(intervalMinutes).TotalMilliseconds;
            _syncTimer.Start();
        }

        public void StopAutoSync()
        {
            _syncTimer.Stop();
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            switch (e.Key)
            {
                case nameof(ConfigurationProvider.AutoSync):
                case nameof(ConfigurationProvider.SyncIntervalMinutes):
                    UpdateSyncTimer();
                    break;
            }
        }

        private async void OnSyncTimerElapsed(object sender, ElapsedEventArgs e)
        {
            await SynchronizeAsync();
        }

        public async Task SynchronizeAsync()
        {
            try
            {
                OnSyncStatusChanged(SyncStatus.Syncing);

                var serverUrl = _config.SyncServerUrl;
                var username = _config.SyncUsername;
                var password = _config.SyncPassword;

                // Perform sync operation
                // TODO: Implement actual sync logic

                _hasPendingChanges = false;
                OnSyncStatusChanged(SyncStatus.Success);
            }
            catch (Exception ex)
            {
                OnSyncStatusChanged(SyncStatus.Error, ex.Message);
            }
        }

        public async Task HandleLocalFileChangeAsync(string relativePath)
        {
            try
            {
                _hasPendingChanges = true;
                OnSyncStatusChanged(SyncStatus.Conflicted);
                await SynchronizeAsync(); // Trigger immediate sync
            }
            catch (Exception ex)
            {
                OnSyncStatusChanged(SyncStatus.Error, ex.Message);
            }
        }

        public async Task CheckForServerChangesAsync()
        {
            try
            {
                OnSyncStatusChanged(SyncStatus.Syncing);
                // TODO: Add server changes check implementation
                await SynchronizeAsync();
            }
            catch (Exception ex)
            {
                OnSyncStatusChanged(SyncStatus.Error, ex.Message);
            }
        }

        public async Task ShowPendingChangesDialog()
        {
            // TODO: Implement pending changes dialog
            await Task.CompletedTask;
        }

        public void UpdateCredentials()
        {
            // TODO: Implement credentials update
        }

        private void OnSyncStatusChanged(SyncStatus status, string message = null)
        {
            SyncStatusChanged?.Invoke(this, new SyncStatusEventArgs(status, message));
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _syncTimer.Dispose();
                _isDisposed = true;
            }
        }
    }
}