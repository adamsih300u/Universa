using System;
using System.Collections.Generic;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Interface for centralized org-mode state configuration management.
    /// </summary>
    public interface IOrgStateConfigurationService : IDisposable
    {
        /// <summary>
        /// Event fired when the org state configuration changes.
        /// </summary>
        event EventHandler<OrgStateConfigurationChangedEventArgs> ConfigurationChanged;

        /// <summary>
        /// Gets the current org state configuration.
        /// </summary>
        OrgStateConfiguration GetConfiguration();

        /// <summary>
        /// Forces a refresh of the configuration from settings.
        /// </summary>
        void RefreshConfiguration();

        /// <summary>
        /// Gets the next state in the cycling sequence for the given current state.
        /// </summary>
        OrgStateInfo GetNextState(string currentState);

        /// <summary>
        /// Gets all configured state names for regex pattern building.
        /// </summary>
        IEnumerable<string> GetAllStateNames();

        /// <summary>
        /// Builds a regex pattern for all configured states.
        /// </summary>
        string GetStatePattern();
    }
} 