using System;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class MatrixService : IMatrixService
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private MatrixClient _matrixClient;

        public MatrixService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;

            InitializeClient();
        }

        private void InitializeClient()
        {
            if (!string.IsNullOrEmpty(_config.MatrixServerUrl) &&
                !string.IsNullOrEmpty(_config.MatrixUsername) &&
                !string.IsNullOrEmpty(_config.MatrixPassword))
            {
                _matrixClient?.Dispose();
                _matrixClient = new MatrixClient(_config.MatrixServerUrl);
                // Login after initialization
                _ = _matrixClient.Login(_config.MatrixUsername, _config.MatrixPassword);
            }
        }

        public void RefreshConfiguration()
        {
            InitializeClient();
        }
    }
} 