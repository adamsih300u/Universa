using System;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class AIService : IAIService
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private OpenAIService _openAIService;
        private AnthropicService _anthropicService;
        private XAIService _xaiService;

        public AIService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;

            InitializeServices();
        }

        private void InitializeServices()
        {
            if (_config.EnableOpenAI && !string.IsNullOrEmpty(_config.OpenAIApiKey))
            {
                _openAIService = new OpenAIService(_config.OpenAIApiKey);
            }
            else
            {
                _openAIService = null;
            }

            if (_config.EnableAnthropic && !string.IsNullOrEmpty(_config.AnthropicApiKey))
            {
                _anthropicService = new AnthropicService(_config.AnthropicApiKey);
            }
            else
            {
                _anthropicService = null;
            }

            if (_config.EnableXAI && !string.IsNullOrEmpty(_config.XAIApiKey))
            {
                _xaiService = new XAIService(_config.XAIApiKey);
            }
            else
            {
                _xaiService = null;
            }
        }

        public void RefreshConfiguration()
        {
            InitializeServices();
        }
    }
} 