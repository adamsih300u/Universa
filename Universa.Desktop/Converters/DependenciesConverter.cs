using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using Universa.Desktop.Library;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class DependenciesConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var dependencies = values[0] as IEnumerable<Library.ProjectDependency>;
            var tasks = values[1] as IEnumerable<Models.ProjectTask>;
            var result = new List<string>();

            // Get the current project's file path for filtering internal task dependencies
            var currentProject = ProjectTracker.Instance.GetAllProjects()
                .FirstOrDefault(p => p.Dependencies == dependencies);
            if (currentProject == null) return result;

            // Add external dependencies
            if (dependencies != null)
            {
                // First add hard dependencies
                foreach (var dep in dependencies.Where(d => d.IsHardDependency))
                {
                    var displayName = GetDependencyDisplayName(dep);
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        result.Add($"{displayName} (Hard)");
                    }
                }

                // Then add soft dependencies
                foreach (var dep in dependencies.Where(d => !d.IsHardDependency))
                {
                    var displayName = GetDependencyDisplayName(dep);
                    if (!string.IsNullOrEmpty(displayName))
                    {
                        result.Add($"{displayName} (Soft)");
                    }
                }
            }

            // Add task dependencies that reference external items
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    if (task.Dependencies?.Any() == true)
                    {
                        foreach (var taskDep in task.Dependencies)
                        {
                            // Skip internal task references
                            if (!taskDep.StartsWith(currentProject.FilePath))
                            {
                                var depDisplayName = GetTaskDependencyDisplayName(taskDep);
                                if (!string.IsNullOrEmpty(depDisplayName))
                                {
                                    result.Add($"Task '{task.Title}' depends on: {depDisplayName}");
                                }
                            }
                        }
                    }
                }
            }

            return result;
        }

        private string GetDependencyDisplayName(Library.ProjectDependency dependency)
        {
            // Try to get project dependency
            var projectDep = ProjectTracker.Instance.GetAllProjects()
                .FirstOrDefault(p => p.FilePath == dependency.FilePath);
            if (projectDep != null)
            {
                return projectDep.Title;
            }

            // Try to get todo dependency
            var todoDep = ToDoTracker.Instance.GetAllTodos()
                .FirstOrDefault(t => t.FilePath == dependency.FilePath);
            if (todoDep != null)
            {
                return todoDep.Title;
            }

            return null;
        }

        private string GetTaskDependencyDisplayName(string dependencyPath)
        {
            // Check if it's a project task reference
            if (dependencyPath.Contains("#"))
            {
                var projectPath = dependencyPath.Substring(0, dependencyPath.IndexOf('#'));
                var project = ProjectTracker.Instance.GetAllProjects()
                    .FirstOrDefault(p => p.FilePath == projectPath);
                if (project != null)
                {
                    return project.Title;
                }
            }

            // Check if it's a project
            var projectDep = ProjectTracker.Instance.GetAllProjects()
                .FirstOrDefault(p => p.FilePath == dependencyPath);
            if (projectDep != null)
            {
                return projectDep.Title;
            }

            // Check if it's a todo
            var todoDep = ToDoTracker.Instance.GetAllTodos()
                .FirstOrDefault(t => t.FilePath == dependencyPath);
            if (todoDep != null)
            {
                return todoDep.Title;
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
} 