using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using System.Text;

namespace Universa.Desktop.Services
{
    public class ToDoChain : BaseLangChainService
    {
        private List<ToDo> _todoItems;
        private static ToDoChain _instance;
        private static readonly object _lock = new object();

        private ToDoChain(string apiKey, string model, Models.AIProvider provider, List<ToDo> todoItems)
            : base(apiKey, model, provider)
        {
            _todoItems = todoItems;
            InitializeSystemMessage();
        }

        public static ToDoChain GetInstance(string apiKey, string model, Models.AIProvider provider, List<ToDo> todoItems)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new ToDoChain(apiKey, model, provider, todoItems);
                }
                else
                {
                    // Update todo items and reinitialize system message
                    _instance._todoItems = todoItems;
                    _instance.InitializeSystemMessage();
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var todoList = new StringBuilder();
            foreach (var item in _todoItems)
            {
                // Main title with completion status
                todoList.AppendLine($"- [{(item.IsCompleted ? "x" : " ")}] {item.Title}");
                
                // Description with better formatting
                if (!string.IsNullOrEmpty(item.Description))
                {
                    var descriptionLines = item.Description.Split('\n');
                    foreach (var line in descriptionLines)
                    {
                        todoList.AppendLine($"    │ {line.Trim()}");
                    }
                }

                // Metadata section
                var metadata = new List<string>();
                if (item.StartDate.HasValue)
                {
                    metadata.Add($"Start: {item.StartDate:d}");
                }
                if (item.DueDate.HasValue)
                {
                    metadata.Add($"Due: {item.DueDate:d}");
                }
                if (item.CompletedDate.HasValue)
                {
                    metadata.Add($"Completed: {item.CompletedDate:d}");
                }
                if (item.IsRecurring)
                {
                    metadata.Add($"Recurs: every {item.RecurrenceInterval} {item.RecurrenceUnit}(s)");
                }
                if (item.Tags?.Any() == true)
                {
                    metadata.Add($"Tags: {string.Join(", ", item.Tags)}");
                }
                
                // Add metadata if any exists
                if (metadata.Any())
                {
                    todoList.AppendLine($"    └─ {string.Join(" | ", metadata)}");
                }

                // Subtasks section
                if (item.SubTasks?.Any() == true)
                {
                    todoList.AppendLine("    └─ Subtasks:");
                    foreach (var subtask in item.SubTasks)
                    {
                        todoList.AppendLine($"        • [{(subtask.IsCompleted ? "x" : " ")}] {subtask.Title}");
                        if (!string.IsNullOrEmpty(subtask.Description))
                        {
                            foreach (var line in subtask.Description.Split('\n'))
                            {
                                todoList.AppendLine($"          {line.Trim()}");
                            }
                        }
                    }
                }
                todoList.AppendLine();
            }

            var systemPrompt = new StringBuilder();
            systemPrompt.AppendLine("You are a task management assistant. Help organize and manage todo items.");
            systemPrompt.AppendLine("You can see the full details of each task, including descriptions, dates, and subtasks.");
            systemPrompt.AppendLine("When referring to tasks, you can mention their titles and relevant details to be specific.");
            systemPrompt.AppendLine();
            systemPrompt.AppendLine($"Current Date: {DateTime.Now:d}");
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("Todo Items:");
            systemPrompt.AppendLine(todoList.ToString());

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
                // Only process the request if one was provided
                if (!string.IsNullOrEmpty(request))
                {
                    // Add the user request to memory
                    AddUserMessage(request);
                    
                    // Get response from AI using the memory context
                    var response = await ExecutePrompt(string.Empty);
                    
                    // Add the response to memory
                    AddAssistantMessage(response);

                    return response;
                }
                
                return string.Empty;  // No request to process
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== TODO CHAIN ERROR ===\n{ex}");
                throw;
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            // This method is no longer used since we're handling the system message and conversation history separately
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