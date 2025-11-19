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
        // BULLY FIX: Removed _currentContent field - this service is now completely file-independent
        private static GeneralChatService _instance;
        private static readonly object _lock = new object();
        private readonly string _persona;

        public GeneralChatService(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false, string persona = null)
            : base(apiKey, model, provider, isThinkingMode)
        {
            _persona = persona;
            InitializeSystemMessage();
        }

        public static GeneralChatService GetInstance(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false, string persona = null)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new GeneralChatService(apiKey, model, provider, isThinkingMode, persona);
                }
                else if (_instance._apiKey != apiKey || _instance._model != model || _instance._provider != provider || _instance._isThinkingMode != isThinkingMode || _instance._persona != persona)
                {
                    // If API key, model, provider, thinking mode, or persona changed, create new instance
                    _instance = new GeneralChatService(apiKey, model, provider, isThinkingMode, persona);
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = new StringBuilder();
            
            // SPLENDID FIX: Add persona-based system prompt if specified
            if (!string.IsNullOrWhiteSpace(_persona))
            {
                systemPrompt.AppendLine($"You are {_persona}. Respond and act as that person would during this interaction.");
                systemPrompt.AppendLine("Embody their personality, speaking style, knowledge, and perspective while being helpful to the user.");
            }
            else
            {
                systemPrompt.AppendLine("You are an AI assistant specialized in helping users with their tasks.");
            }
            
            // Add current date and time context for temporal awareness
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("=== CURRENT DATE AND TIME ===");
            systemPrompt.AppendLine($"Current Date/Time: {DateTime.Now:F}");
            systemPrompt.AppendLine($"Local Time Zone: {TimeZoneInfo.Local.DisplayName}");
            systemPrompt.AppendLine("");
            
            if (_isThinkingMode)
            {
                systemPrompt.AppendLine("You are running in Thinking mode, which means you'll show your reasoning process step by step.");
            }
            
            // SPLENDID: GeneralChatService is now completely independent - no file content included
            // This ensures "Chat" chain is truly general and not tied to any specific file

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
                // BULLY FIX: Ignore content parameter - GeneralChatService is file-independent
                // content parameter is ignored to keep this service truly general

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Add placeholder assistant message that will be updated with reasoning
                var assistantMessage = new MemoryMessage("assistant", "", _model);
                _memory.Add(assistantMessage);
                
                // Get response from AI using the memory context
                var response = await ExecutePrompt("");
                
                // Update the assistant message with response content
                // (Reasoning was already stored by ExecuteOpenRouterPrompt if present)
                assistantMessage.Content = response;

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
                // BULLY FIX: Ignore content parameter - GeneralChatService is file-independent
                // content parameter is ignored to keep this service truly general

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Add placeholder assistant message that will be updated with reasoning
                var assistantMessage = new MemoryMessage("assistant", "", _model);
                _memory.Add(assistantMessage);
                
                // Get response from AI using the memory context with cancellation support
                var response = await ExecutePrompt("", cancellationToken);
                
                // Update the assistant message with response content
                // (Reasoning was already stored by ExecuteOpenRouterPrompt if present)
                assistantMessage.Content = response;

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