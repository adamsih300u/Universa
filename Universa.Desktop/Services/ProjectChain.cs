using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text;
using Universa.Desktop.Models;
using Universa.Desktop.Library;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Universa.Desktop.Services
{
    public class ProjectChain : BaseLangChainService
    {
        private Project _currentProject;
        private static ProjectChain _instance;
        private static readonly object _lock = new object();
        private readonly ProjectTracker _projectTracker;

        private ProjectChain(string apiKey, string model, AIProvider provider, Project project)
            : base(apiKey, model, provider)
        {
            _currentProject = project;
            _projectTracker = ProjectTracker.Instance;
            InitializeSystemMessage();
        }

        public static async Task<ProjectChain> GetInstanceAsync(string apiKey, string model, AIProvider provider, Project project)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new ProjectChain(apiKey, model, provider, project);
                }
                else if (_instance._currentProject != project)
                {
                    // Update project and reinitialize
                    _instance._currentProject = project;
                    Task.Run(() => _instance.InitializeSystemMessage()).Wait();
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var systemMessage = new MemoryMessage("system", 
                "You are a project management assistant. Help users manage their projects, tasks, and dependencies. " +
                "Provide insights about project status, dependencies, and suggest improvements. " +
                "Consider both direct project content and related dependencies when providing assistance.");
            _memory.Clear();
            _memory.Add(systemMessage);
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            // Gather the project context directly
            var projectContext = await GatherProjectContext();
            Debug.WriteLine($"Gathered project context: {projectContext.Length} characters");
            
            // Build and execute the prompt using our gathered context
            var prompt = BuildBasePrompt(projectContext, request);
            return await ExecutePrompt(prompt);
        }

        private async Task<string> GatherProjectContext()
        {
            var sb = new StringBuilder();

            if (_currentProject == null)
            {
                return "No active project.";
            }

            // Main project information
            sb.AppendLine("=== CURRENT PROJECT ===");
            sb.AppendLine($"Title: {_currentProject.Title}");
            sb.AppendLine($"Goal: {_currentProject.Goal}");
            sb.AppendLine($"Status: {_currentProject.Status}");
            sb.AppendLine($"Created: {_currentProject.CreatedDate:d}");
            if (_currentProject.DueDate.HasValue)
            {
                sb.AppendLine($"Due: {_currentProject.DueDate:d}");
            }

            // Tasks and subtasks
            if (_currentProject.Tasks?.Any() == true)
            {
                sb.AppendLine("=== CURRENT PROJECT TASKS ===");
                foreach (var task in _currentProject.Tasks)
                {
                    AppendTaskInfo(sb, task, 0);
                }
            }

            // Log entries
            if (_currentProject.LogEntries?.Any() == true)
            {
                sb.AppendLine("=== RECENT PROJECT LOG ENTRIES ===");
                foreach (var entry in _currentProject.LogEntries.OrderByDescending(e => e.Timestamp).Take(5))
                {
                    sb.AppendLine($"[{entry.Timestamp:g}] {entry.Content}");
                }
            }

            // Dependencies
            await AppendDependenciesInfo(sb);

            // Reference files
            if (_currentProject.RelativeReferences?.Any() == true)
            {
                sb.AppendLine("=== PROJECT REFERENCE FILES ===");
                string projectDir = Path.GetDirectoryName(_currentProject.FilePath);
                
                foreach (var reference in _currentProject.RelativeReferences)
                {
                    sb.AppendLine($"--- Reference: {reference.RelativePath} ---");
                    if (!string.IsNullOrEmpty(reference.Description))
                    {
                        sb.AppendLine($"Description: {reference.Description}");
                    }

                    try
                    {
                        string fullPath = Path.GetFullPath(Path.Combine(projectDir, reference.RelativePath));
                        if (File.Exists(fullPath))
                        {
                            // Only include content for text-based files
                            string extension = Path.GetExtension(fullPath).ToLower();
                            if (extension == ".md" || extension == ".txt" || extension == ".todo" || 
                                extension == ".project" || extension == ".reference")
                            {
                                sb.AppendLine("Content:");
                                string content = await File.ReadAllTextAsync(fullPath);
                                // Indent the content for better readability
                                foreach (var line in content.Split('\n'))
                                {
                                    sb.AppendLine($"    {line.TrimEnd()}");
                                }
                            }
                            else
                            {
                                sb.AppendLine("(Binary or unsupported file type)");
                            }
                        }
                        else
                        {
                            sb.AppendLine("(File not found)");
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.AppendLine($"(Error reading file: {ex.Message})");
                    }
                }
            }

            return sb.ToString();
        }

        private void AppendTaskInfo(StringBuilder sb, ProjectTask task, int level)
        {
            var indent = new string(' ', level * 2);
            sb.AppendLine($"{indent}- [{(task.IsCompleted ? "x" : " ")}] {task.Title}");
            
            if (!string.IsNullOrEmpty(task.Description))
            {
                sb.AppendLine($"{indent}  Description: {task.Description}");
            }

            if (task.StartDate.HasValue)
            {
                sb.AppendLine($"{indent}  Start: {task.StartDate:d}");
            }

            if (task.DueDate.HasValue)
            {
                sb.AppendLine($"{indent}  Due: {task.DueDate:d}");
            }

            // Append task dependencies if any
            if (task.Dependencies?.Any() == true)
            {
                sb.AppendLine($"{indent}  Dependencies:");
                foreach (var dep in task.Dependencies)
                {
                    sb.AppendLine($"{indent}    - {dep}");
                }
            }

            // Recursively append subtasks
            if (task.Subtasks?.Any() == true)
            {
                foreach (var subtask in task.Subtasks)
                {
                    AppendTaskInfo(sb, subtask, level + 1);
                }
            }
        }

        private async Task AppendDependenciesInfo(StringBuilder sb)
        {
            if (_currentProject.Dependencies?.Any() != true)
            {
                return;
            }

            sb.AppendLine("=== PROJECT DEPENDENCIES ===");
            await AppendDependencyLevel(_currentProject.Dependencies, sb, 0);
        }

        private async Task AppendDependencyLevel(IEnumerable<ProjectDependency> dependencies, StringBuilder sb, int level)
        {
            if (level >= 2 || dependencies == null || !dependencies.Any()) return;

            // If this is a nested level, add a header explaining the context
            if (level > 0)
            {
                string indent = new string(' ', level * 2);
                sb.AppendLine($"{indent}=== DEPENDENCIES OF ABOVE PROJECT ===");
                sb.AppendLine($"{indent}Note: These dependencies are required by the parent project, not directly by the root project.");
            }

            foreach (var dep in dependencies)
            {
                // Try to get project dependency
                var projectDep = _projectTracker.GetAllProjects()
                    .FirstOrDefault(p => p.FilePath == dep.FilePath);

                if (projectDep != null)
                {
                    string indent = new string(' ', level * 2);
                    sb.AppendLine($"{indent}--- Dependent Project: {projectDep.Title} ---");
                    sb.AppendLine($"{indent}Dependency Type: {(dep.IsHardDependency ? "Hard (Blocking)" : "Soft (Non-blocking)")}");
                    if (level > 0)
                    {
                        sb.AppendLine($"{indent}Relationship: Required by the project above, not directly by the root project");
                    }
                    sb.AppendLine($"{indent}Status: {projectDep.Status}");
                    sb.AppendLine($"{indent}Goal: {projectDep.Goal}");

                    // Add tasks from dependency
                    if (projectDep.Tasks?.Any() == true)
                    {
                        sb.AppendLine($"{indent}Tasks:");
                        foreach (var task in projectDep.Tasks)
                        {
                            AppendTaskInfo(sb, task, level + 1);
                        }
                    }

                    // Add reference files from dependency
                    if (projectDep.RelativeReferences?.Any() == true)
                    {
                        sb.AppendLine($"{indent}Reference Files:");
                        string projectDir = Path.GetDirectoryName(projectDep.FilePath);
                        foreach (var reference in projectDep.RelativeReferences)
                        {
                            sb.AppendLine($"{indent}  • {reference.RelativePath}");
                            if (!string.IsNullOrEmpty(reference.Description))
                            {
                                sb.AppendLine($"{indent}    Description: {reference.Description}");
                            }

                            try
                            {
                                string fullPath = Path.GetFullPath(Path.Combine(projectDir, reference.RelativePath));
                                if (File.Exists(fullPath))
                                {
                                    string extension = Path.GetExtension(fullPath).ToLower();
                                    if (extension == ".md" || extension == ".txt" || extension == ".todo" || 
                                        extension == ".project" || extension == ".reference")
                                    {
                                        sb.AppendLine($"{indent}    Content:");
                                        string content = await File.ReadAllTextAsync(fullPath);
                                        foreach (var line in content.Split('\n'))
                                        {
                                            sb.AppendLine($"{indent}      {line.TrimEnd()}");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                sb.AppendLine($"{indent}    (Error reading file: {ex.Message})");
                            }
                        }
                    }

                    // Recursively append next level of dependencies
                    if (projectDep.Dependencies?.Any() == true)
                    {
                        await AppendDependencyLevel(projectDep.Dependencies, sb, level + 1);
                    }
                }
                else
                {
                    // Try to get todo dependency
                    var todoDep = Library.ToDoTracker.Instance.GetAllTodos()
                        .FirstOrDefault(t => t.FilePath == dep.FilePath);

                    if (todoDep != null)
                    {
                        string indent = new string(' ', level * 2);
                        sb.AppendLine($"{indent}--- Dependent ToDo: {todoDep.Title} ---");
                        sb.AppendLine($"{indent}Dependency Type: {(dep.IsHardDependency ? "Hard (Blocking)" : "Soft (Non-blocking)")}");
                        if (level > 0)
                        {
                            sb.AppendLine($"{indent}Relationship: Required by the project above, not directly by the root project");
                        }
                        sb.AppendLine($"{indent}Status: {(todoDep.IsCompleted ? "Completed" : "Pending")}");
                    }
                    else
                    {
                        string indent = new string(' ', level * 2);
                        sb.AppendLine($"{indent}--- Unknown Dependency ---");
                        sb.AppendLine($"{indent}Dependency Type: {(dep.IsHardDependency ? "Hard (Blocking)" : "Soft (Non-blocking)")}");
                        if (level > 0)
                        {
                            sb.AppendLine($"{indent}Relationship: Required by the project above, not directly by the root project");
                        }
                        sb.AppendLine($"{indent}Path: {dep.FilePath}");
                    }
                }
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            var historyText = string.Join("\n", _memory.Select(m => $"{m.Role}: {m.Content}"));
            return $@"You are a project management assistant specialized in helping users with their projects.

Previous conversation:
{historyText}

Current project context:
{content}

User request:
{request}

Please provide specific and helpful suggestions based on the project context and its dependencies.";
        }
    }
} 