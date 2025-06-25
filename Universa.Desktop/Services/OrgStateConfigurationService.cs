using System;
using System.Collections.Generic;
using System.Linq;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Centralized service for managing org-mode state configuration across all tabs and components.
    /// Provides consistent state cycling behavior and automatic updates when configuration changes.
    /// </summary>
    public class OrgStateConfigurationService : IOrgStateConfigurationService
    {
        private static readonly Lazy<OrgStateConfigurationService> _instance = 
            new Lazy<OrgStateConfigurationService>(() => new OrgStateConfigurationService());
        
        public static OrgStateConfigurationService Instance => _instance.Value;

        private readonly IConfigurationService _configService;
        private OrgStateConfiguration _cachedConfiguration;
        private DateTime _lastConfigUpdate = DateTime.MinValue;
        
        public event EventHandler<OrgStateConfigurationChangedEventArgs> ConfigurationChanged;

        private OrgStateConfigurationService()
        {
            _configService = ServiceLocator.Instance?.GetService<IConfigurationService>();
            
            // Subscribe to configuration changes
            if (_configService?.Provider != null)
            {
                _configService.Provider.PropertyChanged += OnConfigurationPropertyChanged;
            }
            
            // Load initial configuration
            RefreshConfiguration();
        }

        /// <summary>
        /// Gets the current org state configuration. Returns cached version if available and recent.
        /// </summary>
        public OrgStateConfiguration GetConfiguration()
        {
            // Refresh if cache is old or empty
            if (_cachedConfiguration == null || DateTime.Now - _lastConfigUpdate > TimeSpan.FromSeconds(1))
            {
                RefreshConfiguration();
            }
            
            return _cachedConfiguration;
        }

        /// <summary>
        /// Forces a refresh of the configuration from settings.
        /// </summary>
        public void RefreshConfiguration()
        {
            try
            {
                var config = new OrgStateConfiguration();
                
                if (_configService?.Provider != null)
                {
                    // Load configured states from settings
                    var todoStates = _configService.Provider.OrgTodoStates ?? new string[0];
                    var doneStates = _configService.Provider.OrgDoneStates ?? new string[0];
                    var noActionStates = _configService.Provider.OrgNoActionStates ?? new string[0];
                    
                    System.Diagnostics.Debug.WriteLine($"OrgStateConfigurationService: Refreshing configuration");
                    System.Diagnostics.Debug.WriteLine($"  TODO States: [{string.Join(", ", todoStates)}]");
                    System.Diagnostics.Debug.WriteLine($"  Done States: [{string.Join(", ", doneStates)}]");  
                    System.Diagnostics.Debug.WriteLine($"  NoAction States: [{string.Join(", ", noActionStates)}]");
                    
                    if (todoStates.Any() || doneStates.Any() || noActionStates.Any())
                    {
                        config.TodoStates.Clear();
                        config.DoneStates.Clear();
                        config.NoActionStates.Clear();
                        
                        // Add TODO states
                        foreach (var state in todoStates)
                        {
                            config.TodoStates.Add(new OrgStateInfo 
                            { 
                                Name = state, 
                                RequiresAction = true, 
                                Color = _configService.Provider.GetStateColor(state) 
                            });
                        }
                        
                        // Add No-Action states  
                        foreach (var state in noActionStates)
                        {
                            config.NoActionStates.Add(new OrgStateInfo 
                            { 
                                Name = state, 
                                RequiresAction = false, 
                                Color = _configService.Provider.GetStateColor(state) 
                            });
                        }
                        
                        // Add Done states
                        foreach (var state in doneStates)
                        {
                            config.DoneStates.Add(new OrgStateInfo 
                            { 
                                Name = state, 
                                RequiresAction = false, 
                                Color = _configService.Provider.GetStateColor(state) 
                            });
                        }
                    }
                }
                
                _cachedConfiguration = config;
                _lastConfigUpdate = DateTime.Now;
                
                var allStates = config.GetAllStates();
                System.Diagnostics.Debug.WriteLine($"OrgStateConfigurationService: Configuration updated. Cycling sequence: None -> {string.Join(" -> ", allStates.Select(s => s.Name))} -> None");
                
                // Notify subscribers of configuration change
                ConfigurationChanged?.Invoke(this, new OrgStateConfigurationChangedEventArgs(config));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OrgStateConfigurationService: Error refreshing configuration: {ex.Message}");
                
                // Fall back to defaults if configuration fails
                _cachedConfiguration = new OrgStateConfiguration();
                _lastConfigUpdate = DateTime.Now;
            }
        }

        /// <summary>
        /// Gets the next state in the cycling sequence for the given current state.
        /// </summary>
        public OrgStateInfo GetNextState(string currentState)
        {
            return GetConfiguration().GetNextState(currentState);
        }

        /// <summary>
        /// Gets all configured state names for regex pattern building.
        /// </summary>
        public IEnumerable<string> GetAllStateNames()
        {
            return GetConfiguration().GetAllStates().Select(s => s.Name).Where(s => !string.IsNullOrEmpty(s));
        }

        /// <summary>
        /// Builds a regex pattern for all configured states.
        /// </summary>
        public string GetStatePattern()
        {
            return string.Join("|", GetAllStateNames());
        }

        private void OnConfigurationPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Check if the property change is related to org-mode states
            if (e.PropertyName != null && (
                e.PropertyName.Contains("OrgTodoStates") ||
                e.PropertyName.Contains("OrgDoneStates") ||
                e.PropertyName.Contains("OrgNoActionStates") ||
                e.PropertyName.Contains("OrgStateColors")))
            {
                System.Diagnostics.Debug.WriteLine($"OrgStateConfigurationService: Configuration property '{e.PropertyName}' changed, refreshing");
                RefreshConfiguration();
            }
        }

        public void Dispose()
        {
            if (_configService?.Provider != null)
            {
                _configService.Provider.PropertyChanged -= OnConfigurationPropertyChanged;
            }
        }
    }

    /// <summary>
    /// Event arguments for org state configuration changes.
    /// </summary>
    public class OrgStateConfigurationChangedEventArgs : EventArgs
    {
        public OrgStateConfiguration NewConfiguration { get; }

        public OrgStateConfigurationChangedEventArgs(OrgStateConfiguration newConfiguration)
        {
            NewConfiguration = newConfiguration;
        }
    }
} 