using System;
using System.Windows;
using System.Windows.Controls;

namespace Universa.Desktop.Controls
{
    public partial class TTSButtonsControl : UserControl
    {
        public static readonly DependencyProperty IsPlayingProperty = DependencyProperty.Register(
            nameof(IsPlaying), 
            typeof(bool), 
            typeof(TTSButtonsControl), 
            new PropertyMetadata(false));

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        public event EventHandler PlaybackToggled;

        public TTSButtonsControl()
        {
            InitializeComponent();
        }

        private void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            PlaybackToggled?.Invoke(this, EventArgs.Empty);
        }
    }
} 