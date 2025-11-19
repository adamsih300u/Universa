using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Models;

namespace Universa.Desktop.Controls
{
    public partial class MessageContentControl : UserControl
    {
        private Models.ChatMessage _currentMessage;

        public MessageContentControl()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Unsubscribe from old message
            if (_currentMessage != null)
            {
                _currentMessage.PropertyChanged -= OnMessagePropertyChanged;
            }

            // Subscribe to new message
            _currentMessage = DataContext as Models.ChatMessage;
            if (_currentMessage != null)
            {
                _currentMessage.PropertyChanged += OnMessagePropertyChanged;
            }

            UpdateContentVisibility();
        }

        private void OnMessagePropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Update visibility when HasFictionText or Content changes
            if (e.PropertyName == nameof(Models.ChatMessage.HasFictionText) || 
                e.PropertyName == nameof(Models.ChatMessage.Content))
            {
                UpdateContentVisibility();
            }
        }

        private void UpdateContentVisibility()
        {
            if (_currentMessage != null)
            {
                // Show fiction control for AI messages with fiction content, otherwise show regular text
                bool showFictionControl = !_currentMessage.IsUserMessage && _currentMessage.HasFictionText;
                
                RegularTextBox.Visibility = showFictionControl ? Visibility.Collapsed : Visibility.Visible;
                FictionControl.Visibility = showFictionControl ? Visibility.Visible : Visibility.Collapsed;
                
                // Debug output removed to prevent UI performance issues during streaming
            }
            else
            {
                // Default to regular text if no message context
                RegularTextBox.Visibility = Visibility.Visible;
                FictionControl.Visibility = Visibility.Collapsed;
            }
        }
    }
} 