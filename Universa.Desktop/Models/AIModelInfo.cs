using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Universa.Desktop.Models
{
    public class AIModelInfo : INotifyPropertyChanged
    {
        private string _name;
        private string _displayName;
        private AIProvider _provider;
        private bool _isEnabled = true;
        private bool _isThinkingMode = false;

        public string Name 
        { 
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public string DisplayName 
        { 
            get => _displayName;
            set
            {
                if (_displayName != value)
                {
                    _displayName = value;
                    OnPropertyChanged(nameof(DisplayName));
                }
            }
        }

        public AIProvider Provider 
        { 
            get => _provider;
            set
            {
                if (_provider != value)
                {
                    _provider = value;
                    OnPropertyChanged(nameof(Provider));
                }
            }
        }

        public bool IsEnabled 
        { 
            get => _isEnabled;
            set
            {
                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public bool IsThinkingMode
        {
            get => _isThinkingMode;
            set
            {
                if (_isThinkingMode != value)
                {
                    _isThinkingMode = value;
                    OnPropertyChanged(nameof(IsThinkingMode));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 