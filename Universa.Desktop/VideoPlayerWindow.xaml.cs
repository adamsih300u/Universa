using System;
using System.Windows;
using System.Windows.Threading;
using Universa.Desktop.Views;
using Universa.Desktop.Windows;

namespace Universa.Desktop
{
    public partial class VideoPlayerWindow : Window
    {
        private bool _isPlaying = true;
        private bool _isFullscreen = false;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private DispatcherTimer _timer;
        private bool _isDraggingSlider = false;

        public event Action PlaybackStopped;

        public VideoPlayerWindow(string mediaUrl, string title)
        {
            InitializeComponent();

            Title = title;
            System.Diagnostics.Debug.WriteLine($"Attempting to create Uri from URL: {mediaUrl}");
            try
            {
                VideoPlayer.Source = new Uri(mediaUrl, UriKind.Absolute);
                System.Diagnostics.Debug.WriteLine("Successfully created Uri");
                VideoPlayer.Play();
            }
            catch (UriFormatException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create Uri: {ex.Message}");
                throw;
            }

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += Timer_Tick;
            _timer.Start();

            // Stop music playback if it's playing
            if (Application.Current.MainWindow is IMediaWindow mediaWindow)
            {
                mediaWindow.MediaPlayerManager?.Stop();
            }
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            if (!_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Value = VideoPlayer.Position.TotalSeconds;
                UpdateTimeDisplay();
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                ProgressSlider.Maximum = VideoPlayer.NaturalDuration.TimeSpan.TotalSeconds;
                UpdateTimeDisplay();
            }
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Media failed to load: {e.ErrorException?.Message}");
            if (e.ErrorException != null)
            {
                System.Diagnostics.Debug.WriteLine($"Error details: {e.ErrorException}");
            }
            MessageBox.Show($"Failed to play video: {e.ErrorException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            PlaybackStopped?.Invoke();
            Close();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isPlaying)
            {
                VideoPlayer.Pause();
                PlayPauseButton.Content = "▶";
                // Sync with main window
                if (Application.Current.MainWindow is IMediaWindow mediaWindow)
                {
                    mediaWindow.MediaPlayerManager?.PauseMedia();
                }
            }
            else
            {
                VideoPlayer.Play();
                PlayPauseButton.Content = "⏸";
                // Sync with main window
                if (Application.Current.MainWindow is IMediaWindow mediaWindow)
                {
                    mediaWindow.MediaPlayerManager?.ResumeMedia();
                }
            }
            _isPlaying = !_isPlaying;
        }

        public void SyncPlaybackState(bool isPlaying)
        {
            if (_isPlaying != isPlaying)
            {
                _isPlaying = isPlaying;
                if (isPlaying)
                {
                    VideoPlayer.Play();
                    PlayPauseButton.Content = "⏸";
                }
                else
                {
                    VideoPlayer.Pause();
                    PlayPauseButton.Content = "▶";
                }
            }
        }

        private void ProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider && VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
                UpdateTimeDisplay();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            VideoPlayer.Volume = e.NewValue;
            // Sync with main window
            if (Application.Current.MainWindow is IMediaWindow mediaWindow)
            {
                mediaWindow.MediaPlayerManager?.SetVolume(e.NewValue);
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (!_isFullscreen)
            {
                _previousWindowState = WindowState;
                _previousWindowStyle = WindowStyle;
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                FullscreenButton.Content = "⛶";
            }
            else
            {
                WindowStyle = _previousWindowStyle;
                WindowState = _previousWindowState;
                FullscreenButton.Content = "⛶";
            }
            _isFullscreen = !_isFullscreen;
        }

        private void UpdateTimeDisplay()
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                TimeDisplay.Text = $"{VideoPlayer.Position:hh\\:mm\\:ss} / {VideoPlayer.NaturalDuration.TimeSpan:hh\\:mm\\:ss}";
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            VideoPlayer.Stop();
            VideoPlayer.Source = null;
            // Notify main window
            if (Application.Current.MainWindow is IMediaWindow mediaWindow)
            {
                mediaWindow.MediaPlayerManager?.StopMedia();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            PlaybackStopped?.Invoke();
        }
    }
} 