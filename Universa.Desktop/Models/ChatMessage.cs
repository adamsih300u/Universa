using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Represents a chat message with model and provider information
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        private string _role;
        private string _content;
        private string _modelName;
        private AIProvider _provider;
        private DateTime _timestamp;
        private bool _isError;
        private string _sender;
        private bool _isUserMessage;
        private bool _canRetry;
        private string _lastUserMessage;
        private bool _isThinking;

        public string Role
        {
            get => _role;
            set
            {
                if (_role != value)
                {
                    _role = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ModelName
        {
            get => _modelName;
            set
            {
                if (_modelName != value)
                {
                    _modelName = value;
                    OnPropertyChanged();
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
                    OnPropertyChanged();
                }
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            set
            {
                if (_timestamp != value)
                {
                    _timestamp = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsError
        {
            get => _isError;
            set
            {
                if (_isError != value)
                {
                    _isError = value;
                    OnPropertyChanged();
                    // Update CanRetry when IsError changes
                    if (value)
                    {
                        CanRetry = true;
                    }
                }
            }
        }

        public string Sender
        {
            get => _sender;
            set
            {
                if (_sender != value)
                {
                    _sender = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsUserMessage
        {
            get => _isUserMessage;
            set
            {
                if (_isUserMessage != value)
                {
                    _isUserMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool CanRetry
        {
            get => _canRetry;
            set
            {
                if (_canRetry != value)
                {
                    _canRetry = value;
                    OnPropertyChanged();
                }
            }
        }

        public string LastUserMessage
        {
            get => _lastUserMessage;
            set
            {
                if (_lastUserMessage != value)
                {
                    _lastUserMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (_isThinking != value)
                {
                    _isThinking = value;
                    OnPropertyChanged();
                }
            }
        }

        public ChatMessage()
        {
            Timestamp = DateTime.UtcNow;
            Provider = AIProvider.OpenAI; // Default provider
        }

        public ChatMessage(string role, string content, bool isError = false) : this()
        {
            Role = role;
            Content = content;
            IsError = isError;
            IsUserMessage = role == "user";
            Sender = role == "user" ? "You" : "Assistant";
            CanRetry = isError;
            IsThinking = role == "assistant" && content != null && content.StartsWith("Thinking");
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 