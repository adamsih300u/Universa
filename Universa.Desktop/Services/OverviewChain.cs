using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using Universa.Desktop.Models;
using Universa.Desktop.Library;
using System.IO;

namespace Universa.Desktop.Services
{
    public class OverviewChain : BaseLangChainService
    {
        private List<Project> _projects;
        private List<ToDo> _todos;
        private static OverviewChain _instance;
        private static readonly object _lock = new object();

        private OverviewChain(string apiKey, string model, AIProvider provider, List<Project> projects, List<ToDo> todos)
            : base(apiKey, model, provider)
        {
            _projects = projects;
            _todos = todos;
            InitializeSystemMessage();
        }

        public static OverviewChain GetInstance(string apiKey, string model, AIProvider provider, List<Project> projects, List<ToDo> todos)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new OverviewChain(apiKey, model, provider, projects, todos);
                }
                else
                {
                    // Update data and reinitialize system message
                    _instance._projects = projects;
                    _instance._todos = todos;
                    _instance.InitializeSystemMessage();
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var systemMessage = @"You are an AI assistant specialized in analyzing and providing insights about projects and todos.
You have access to the following information:
- A list of projects with their titles, goals, status, due dates, tasks, and dependencies
- A list of todos with their titles, descriptions, completion status, start dates, due dates, and tags

Please help analyze this information and provide insights about:
- Project status and progress
- Todo priorities and deadlines
- Dependencies and relationships between items
- Scheduling and time management suggestions
- Potential bottlenecks or risks
- Organization and productivity recommendations

When analyzing, consider:
1. Project dependencies and their impact on timelines
2. Resource allocation and task distribution
3. Critical path and milestone tracking
4. Workload balancing and deadline management
5. Task prioritization and scheduling optimization

Provide clear, actionable insights and recommendations based on the available data.";

            _memory.Clear();
            _memory.Add(new MemoryMessage("system", systemMessage));
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                // Add the content to memory if it's not empty
                if (!string.IsNullOrEmpty(content))
                {
                    AddUserMessage(content);
                }
                
                // Add the user request to memory
                if (!string.IsNullOrEmpty(request))
                {
                    AddUserMessage(request);
                }
                
                // Get response from AI using the memory context
                var response = await ExecutePrompt(string.Empty);
                
                // Add the response to memory
                AddAssistantMessage(response);

                return response;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== OVERVIEW CHAIN ERROR ===\n{ex}");
                throw;
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            var contextBuilder = new StringBuilder();
            
            // Group projects by status
            var projectsByStatus = _projects.GroupBy(p => p.Status).OrderBy(g => g.Key);
            
            // Add projects information
            contextBuilder.AppendLine("# Projects Overview");
            contextBuilder.AppendLine();
            
            foreach (var statusGroup in projectsByStatus)
            {
                // Add status header with visual separator
                contextBuilder.AppendLine($"## {statusGroup.Key} Projects");
                contextBuilder.AppendLine("-------------------");
                
                foreach (var project in statusGroup)
                {
                    contextBuilder.AppendLine($"### {project.Title}");
                    if (!string.IsNullOrEmpty(project.Goal))
                        contextBuilder.AppendLine($"Goal: {project.Goal}");
                    if (project.StartDate.HasValue)
                        contextBuilder.AppendLine($"Started: {project.StartDate:d}");
                    if (project.DueDate.HasValue)
                        contextBuilder.AppendLine($"Due: {project.DueDate:d}");
                    if (project.CompletedDate.HasValue)
                        contextBuilder.AppendLine($"Completed: {project.CompletedDate:d}");
                    
                    if (project.Dependencies?.Any() == true)
                    {
                        contextBuilder.AppendLine("\nDependencies:");
                        foreach (var dep in project.Dependencies)
                        {
                            // Try to find the dependent project to get its title
                            var dependentProject = _projects.FirstOrDefault(p => p.FilePath == dep.FilePath);
                            var depTitle = dependentProject?.Title ?? Path.GetFileNameWithoutExtension(dep.FilePath);
                            contextBuilder.AppendLine($"- {depTitle} ({(dep.IsHardDependency ? "Hard" : "Soft")})");
                        }
                    }
                    
                    if (project.Tasks?.Any() == true)
                    {
                        contextBuilder.AppendLine("\nTasks:");
                        foreach (var task in project.Tasks)
                        {
                            contextBuilder.AppendLine($"- [{(task.IsCompleted ? "x" : " ")}] {task.Title}");
                            if (!string.IsNullOrEmpty(task.Description))
                                contextBuilder.AppendLine($"  Description: {task.Description}");
                            if (task.StartDate.HasValue)
                                contextBuilder.AppendLine($"  Start Date: {task.StartDate:d}");
                            if (task.DueDate.HasValue)
                                contextBuilder.AppendLine($"  Due Date: {task.DueDate:d}");
                        }
                    }
                    contextBuilder.AppendLine("\n-------------------");
                }
                contextBuilder.AppendLine();
            }
            
            // Add todos information with clear section break
            contextBuilder.AppendLine("\n========================================");
            contextBuilder.AppendLine("# ToDos Overview");
            contextBuilder.AppendLine("========================================\n");
            
            // Group todos by their file name (category)
            var todosByFile = _todos.GroupBy(t => Path.GetFileNameWithoutExtension(t.FilePath ?? "Uncategorized"));
            foreach (var group in todosByFile)
            {
                contextBuilder.AppendLine($"## {group.Key}");
                contextBuilder.AppendLine("-------------------");
                foreach (var todo in group)
                {
                    contextBuilder.AppendLine($"- [{(todo.IsCompleted ? "x" : " ")}] {todo.Title}");
                    if (!string.IsNullOrEmpty(todo.Description))
                        contextBuilder.AppendLine($"  Description: {todo.Description}");
                    if (todo.StartDate.HasValue)
                        contextBuilder.AppendLine($"  Started: {todo.StartDate:d}");
                    if (todo.DueDate.HasValue)
                        contextBuilder.AppendLine($"  Due: {todo.DueDate:d}");
                    if (todo.CompletedDate.HasValue)
                        contextBuilder.AppendLine($"  Completed: {todo.CompletedDate:d}");
                    if (todo.Tags?.Any() == true)
                        contextBuilder.AppendLine($"  Tags: {string.Join(", ", todo.Tags)}");
                    
                    if (todo.SubTasks?.Any() == true)
                    {
                        contextBuilder.AppendLine("  Subtasks:");
                        foreach (var subtask in todo.SubTasks)
                        {
                            contextBuilder.AppendLine($"  - [{(subtask.IsCompleted ? "x" : " ")}] {subtask.Title}");
                        }
                    }
                    contextBuilder.AppendLine();
                }
            }

            return contextBuilder.ToString();
        }
    }
} 