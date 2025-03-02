using Universa.Desktop.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Universa.Desktop.Models
{
    public class DependencyItem
    {
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public DependencyType Type { get; set; }
        public ProjectTask Task { get; set; }
    }

    public class DependencyDisplayItem : INotifyPropertyChanged
    {
        private bool _isHardDependency;
            
        public string FilePath { get; set; }
        public string DisplayName { get; set; }
        public bool IsHardDependency 
        { 
            get => _isHardDependency;
            set
            {
                if (_isHardDependency != value)
                {
                    _isHardDependency = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public enum DependencyType
    {
        Project,
        ToDo,
        Task
    }
} 