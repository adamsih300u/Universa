using System;
using System.Collections.Generic;
using System.Linq;

namespace Universa.Desktop.Models
{
    public class OrgStateConfiguration
    {
        public List<OrgStateInfo> TodoStates { get; set; }
        public List<OrgStateInfo> DoneStates { get; set; }
        public List<OrgStateInfo> NoActionStates { get; set; }

        public OrgStateConfiguration()
        {
            // Default states
            TodoStates = new List<OrgStateInfo>
            {
                new OrgStateInfo { Name = "TODO", Color = "#FF8C00", RequiresAction = true },
                new OrgStateInfo { Name = "NEXT", Color = "#1E90FF", RequiresAction = true },
                new OrgStateInfo { Name = "STARTED", Color = "#DAA520", RequiresAction = true },
                new OrgStateInfo { Name = "PROJECT", Color = "#9932CC", RequiresAction = true }
            };

            DoneStates = new List<OrgStateInfo>
            {
                new OrgStateInfo { Name = "DONE", Color = "#228B22", RequiresAction = false },
                new OrgStateInfo { Name = "CANCELLED", Color = "#696969", RequiresAction = false }
            };

            NoActionStates = new List<OrgStateInfo>
            {
                new OrgStateInfo { Name = "DELEGATED", Color = "#9370DB", RequiresAction = false },
                new OrgStateInfo { Name = "SOMEDAY", Color = "#708090", RequiresAction = false },
                new OrgStateInfo { Name = "WAITING", Color = "#DC143C", RequiresAction = false },
                new OrgStateInfo { Name = "DEFERRED", Color = "#9370DB", RequiresAction = false }
            };
        }

        public List<OrgStateInfo> GetAllStates()
        {
            var allStates = new List<OrgStateInfo>();
            allStates.AddRange(TodoStates);
            allStates.AddRange(NoActionStates);
            allStates.AddRange(DoneStates);
            return allStates;
        }

        public List<OrgStateInfo> GetActionRequiredStates()
        {
            return GetAllStates().Where(s => s.RequiresAction).ToList();
        }

        public List<OrgStateInfo> GetNoActionStates()
        {
            return GetAllStates().Where(s => !s.RequiresAction).ToList();
        }

        public OrgStateInfo GetNextState(string currentState)
        {
            var allStates = GetAllStates();
            
            // Handle special "None" state - cycle to first state
            if (currentState == "None" || string.IsNullOrEmpty(currentState))
            {
                return allStates.FirstOrDefault();
            }
            
            var currentIndex = allStates.FindIndex(s => s.Name == currentState);
            
            if (currentIndex == -1)
            {
                // If no current state found, start with first state
                return allStates.FirstOrDefault();
            }

            // Cycle to next state, but at the end cycle back to None (null)
            var nextIndex = currentIndex + 1;
            if (nextIndex >= allStates.Count)
            {
                // At the end of all states, cycle back to None
                return null;
            }
            
            return allStates[nextIndex];
        }

        public bool IsCompleted(string stateName)
        {
            return DoneStates.Any(s => s.Name == stateName);
        }

        public bool RequiresAction(string stateName)
        {
            var state = GetAllStates().FirstOrDefault(s => s.Name == stateName);
            return state?.RequiresAction ?? true;
        }
    }

    public class OrgStateInfo
    {
        public string Name { get; set; }
        public string Color { get; set; }
        public bool RequiresAction { get; set; }
        public string Description { get; set; }

        public OrgStateInfo()
        {
        }

        public OrgStateInfo(string name, string color, bool requiresAction, string description = null)
        {
            Name = name;
            Color = color;
            RequiresAction = requiresAction;
            Description = description ?? name;
        }
    }
} 