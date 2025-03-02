using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Universa.Desktop.Library
{
    public class ProjectDependency : INotifyPropertyChanged
    {
        private string _filePath;
        private bool _isHardDependency;

        public string FilePath
        {
            get => _filePath;
            set
            {
                if (_filePath != value)
                {
                    _filePath = value;
                    OnPropertyChanged();
                }
            }
        }

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

        public ProjectDependency()
        {
            _isHardDependency = true; // Default to hard dependency
        }

        public ProjectDependency(string filePath, bool isHardDependency = true)
        {
            _filePath = filePath;
            _isHardDependency = isHardDependency;
        }
    }
} 