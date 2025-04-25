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
    public class ContextualLangChainService : BaseLangChainService
    {
        private string _currentContext;

        public ContextualLangChainService(string apiKey, string model = "gpt-4", Models.AIProvider provider = Models.AIProvider.OpenAI, bool isThinkingMode = false)
            : base(apiKey, model, provider, isThinkingMode)
        {
            _currentContext = string.Empty;
            InitializeSystemMessage();
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = new StringBuilder();
            systemPrompt.AppendLine("You are a helpful AI assistant specialized in analyzing and responding to queries about the provided context.");
            systemPrompt.AppendLine("Provide detailed and knowledgeable responses based on the specific content shared with you.");
            
            if (_isThinkingMode)
            {
                systemPrompt.AppendLine("You are running in Thinking mode, which means you'll show your reasoning process step by step.");
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
                // Update context if changed
                if (content != null && _currentContext != content)
                {
                    await UpdateContextAsync(content);
                }

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Get response from AI using the memory context
                var response = await ExecutePrompt("");
                
                // Add the response to memory
                _memory.Add(new MemoryMessage("assistant", response, _model));

                // Trim memory if needed
                TrimMemory();

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ContextualLangChainService.ProcessRequest: {ex.Message}");
                throw;
            }
        }

        public override async Task<string> ProcessRequest(string content, string request, CancellationToken cancellationToken)
        {
            try
            {
                // Update context if changed
                if (content != null && _currentContext != content)
                {
                    await UpdateContextAsync(content);
                }

                // Add the user request to memory
                _memory.Add(new MemoryMessage("user", request, _model));
                
                // Get response from AI using the memory context with cancellation support
                var response = await ExecutePrompt("", cancellationToken);
                
                // Add the response to memory
                _memory.Add(new MemoryMessage("assistant", response, _model));

                // Trim memory if needed
                TrimMemory();

                return response;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ContextualLangChainService.ProcessRequest with cancellation: {ex.Message}");
                throw;
            }
        }

        public override async Task UpdateContextAsync(string context)
        {
            try
            {
                _currentContext = context;
                
                // Create or update context message
                var contextMessage = _memory.FirstOrDefault(m => m.Role.Equals("user", StringComparison.OrdinalIgnoreCase) && m.Content.StartsWith("Context:"));
                
                string formattedContext = string.IsNullOrEmpty(context) ? string.Empty : $"Context:\n{context}";
                
                if (contextMessage != null)
                {
                    // Update existing context message
                    contextMessage.Content = formattedContext;
                }
                else if (!string.IsNullOrEmpty(formattedContext))
                {
                    // Insert context as first user message after system message
                    int insertIndex = _memory.FindIndex(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                    if (insertIndex >= 0)
                    {
                        _memory.Insert(insertIndex + 1, new MemoryMessage("user", formattedContext, _model));
                    }
                    else
                    {
                        // No system message found, add context as first message
                        _memory.Insert(0, new MemoryMessage("user", formattedContext, _model));
                    }
                }
                
                // Add synthetic assistant response to acknowledge the context
                if (!string.IsNullOrEmpty(context))
                {
                    var assistantResponse = "I've received the updated context and will take it into account for our conversation.";
                    
                    // Check if we need to add the assistant response
                    var lastMessage = _memory.LastOrDefault();
                    if (lastMessage == null || lastMessage.Role != "assistant" || !lastMessage.Content.Contains("received the updated context"))
                    {
                        _memory.Add(new MemoryMessage("assistant", assistantResponse, _model));
                    }
                }
                
                // Trim memory to avoid exceeding token limits
                TrimMemory();
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating context: {ex.Message}");
                throw;
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            // Not used since we're handling messages directly in memory
            return string.Empty;
        }

        public override void Dispose()
        {
            // Clean up resources
            base.Dispose();
        }
    }
} 