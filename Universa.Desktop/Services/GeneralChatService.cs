using System;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Universa.Desktop.Services
{
    public class GeneralChatService : BaseLangChainService
    {
        private string _currentContent;
        private static GeneralChatService _instance;
        private static readonly object _lock = new object();

        private GeneralChatService(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false)
            : base(apiKey, model, provider, isThinkingMode)
        {
            _currentContent = string.Empty;
            InitializeSystemMessage();
        }

        public static GeneralChatService GetInstance(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new GeneralChatService(apiKey, model, provider, isThinkingMode);
                }
                else if (_instance._apiKey != apiKey || _instance._model != model || _instance._provider != provider || _instance._isThinkingMode != isThinkingMode)
                {
                    // If API key, model, provider, or thinking mode changed, create new instance
                    _instance = new GeneralChatService(apiKey, model, provider, isThinkingMode);
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = new StringBuilder();
            systemPrompt.AppendLine("You are an AI assistant specialized in helping users with their tasks.");
            
            if (_isThinkingMode)
            {
                systemPrompt.AppendLine("You are running in Thinking mode, which means you'll show your reasoning process step by step.");
            }
            
            if (!string.IsNullOrEmpty(_currentContent))
            {
                systemPrompt.AppendLine("\nCurrent content:");
                systemPrompt.AppendLine(_currentContent);
            }

            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                systemMessage.Content = systemPrompt.ToString();
            }
            else
            {
                _memory.Insert(0, new MemoryMessage("system", systemPrompt.ToString(), _model));
            }
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                // Update content if changed
                if (_currentContent != content)
                {
                    _currentContent = content;
                    InitializeSystemMessage();
                }

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Get response from AI using the memory context
                var response = await ExecutePrompt("");
                
                // Add the response to memory
                _memory.Add(new MemoryMessage("assistant", response, _model));

                // Trim memory if needed
                while (_memory.Count > MAX_HISTORY_ITEMS)
                {
                    // Remove the oldest non-system message
                    var oldestNonSystem = _memory.Skip(1).FirstOrDefault();
                    if (oldestNonSystem != null)
                    {
                        _memory.Remove(oldestNonSystem);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== GENERAL CHAT ERROR ===\n{ex}");
                throw;
            }
        }

        // Override the cancellable version
        public override async Task<string> ProcessRequest(string content, string request, CancellationToken cancellationToken)
        {
            try
            {
                // Update content if changed
                if (_currentContent != content)
                {
                    _currentContent = content;
                    InitializeSystemMessage();
                }

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Get response from AI using the memory context with cancellation support
                var response = await ExecutePrompt("", cancellationToken);
                
                // Add the response to memory
                _memory.Add(new MemoryMessage("assistant", response, _model));

                // Trim memory if needed
                while (_memory.Count > MAX_HISTORY_ITEMS)
                {
                    // Remove the oldest non-system message
                    var oldestNonSystem = _memory.Skip(1).FirstOrDefault();
                    if (oldestNonSystem != null)
                    {
                        _memory.Remove(oldestNonSystem);
                    }
                }

                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== GENERAL CHAT ERROR ===\n{ex}");
                throw;
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            // Not used since we're handling messages directly in memory
            return string.Empty;
        }

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }
    }
} 