using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Diagnostics;
using Universa.Desktop.Windows;

namespace Universa.Desktop.Managers
{
    public class MediaControlsManager
    {
        private readonly IMediaWindow _mainWindow;
        private readonly MediaPlayerManager _mediaPlayerManager;
        private readonly UIElement _controlsPanel;
        private readonly Button _playPauseButton;
        private readonly Button _previousButton;
        private readonly Button _nextButton;
        private readonly Button _shuffleButton;
        private readonly ButtonBase _volumeButton;
        private readonly Slider _volumeSlider;
        private readonly TextBlock _timeInfo;
        private readonly Slider _timelineSlider;
        private readonly TextBlock _nowPlayingText;
        private readonly Grid _mediaControlsGrid;

        public MediaControlsManager(
            IMediaWindow mainWindow,
            MediaPlayerManager mediaPlayerManager,
            UIElement controlsPanel,
            Button playPauseButton,
            Button previousButton,
            Button nextButton,
            Button shuffleButton,
            ButtonBase volumeButton,
            Slider volumeSlider,
            TextBlock timeInfo,
            Slider timelineSlider,
            TextBlock nowPlayingText,
            Grid mediaControlsGrid)
        {
            _mainWindow = mainWindow;
            _mediaPlayerManager = mediaPlayerManager;
            _controlsPanel = controlsPanel;
            _playPauseButton = playPauseButton;
            _previousButton = previousButton;
            _nextButton = nextButton;
            _shuffleButton = shuffleButton;
            _volumeButton = volumeButton;
            _volumeSlider = volumeSlider;
            _timeInfo = timeInfo;
            _timelineSlider = timelineSlider;
            _nowPlayingText = nowPlayingText;
            _mediaControlsGrid = mediaControlsGrid;

            InitializePlaybackControls();
        }

        private void InitializePlaybackControls()
        {
            Debug.WriteLine("========== INITIALIZE PLAYBACK CONTROLS START ==========");
            try
            {
                if (_timeInfo != null)
                {
                    _timeInfo.Visibility = Visibility.Visible;
                    _timeInfo.Text = "00:00 / 00:00";
                }

                // Remove the duplicate timer since MediaPlayerManager already handles updates
                Debug.WriteLine("Playback controls initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR initializing playback controls: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            Debug.WriteLine("========== INITIALIZE PLAYBACK CONTROLS END ==========\n");
        }

        public void ShowMediaControls()
        {
            Debug.WriteLine("\n========== SHOW MEDIA CONTROLS ==========");
            try
            {
                if (_controlsPanel == null)
                {
                    Debug.WriteLine("ERROR: _controlsPanel is null");
                    return;
                }

                // Make sure we're on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Debug.WriteLine("Setting controls panel visibility...");
                    
                    // Only update visibility if it's not already visible
                    if (_controlsPanel.Visibility != Visibility.Visible)
                    {
                        _controlsPanel.Visibility = Visibility.Visible;
                        Debug.WriteLine("Media controls panel made visible");
                    }
                    else
                    {
                        Debug.WriteLine("Media controls panel already visible, skipping update");
                    }
                    
                    if (_mediaControlsGrid != null && _mediaControlsGrid.Visibility != Visibility.Visible)
                    {
                        _mediaControlsGrid.Visibility = Visibility.Visible;
                    }

                    // Show/hide playlist controls based on media type
                    bool isPlayingVideo = Application.Current.Windows.OfType<VideoWindow>().Any();
                    if (_previousButton != null) _previousButton.Visibility = isPlayingVideo ? Visibility.Collapsed : Visibility.Visible;
                    if (_nextButton != null) _nextButton.Visibility = isPlayingVideo ? Visibility.Collapsed : Visibility.Visible;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR showing media controls: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            Debug.WriteLine("========== SHOW MEDIA CONTROLS END ==========\n");
        }

        public void HideMediaControls()
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_controlsPanel != null)
                    {
                        _controlsPanel.Visibility = Visibility.Collapsed;
                    }

                    if (_mediaControlsGrid != null)
                    {
                        _mediaControlsGrid.Visibility = Visibility.Collapsed;
                    }

                    if (_nowPlayingText != null)
                    {
                        _nowPlayingText.Text = string.Empty;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error hiding media controls: {ex.Message}");
            }
        }

        public void UpdatePlaybackButton(bool isPlaying)
        {
            if (_playPauseButton != null)
            {
                _playPauseButton.Content = isPlaying ? "⏸" : "▶";
            }
        }

        public void UpdateVolumeControls(double volume, bool isMuted)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_volumeSlider != null)
                    {
                        _volumeSlider.Value = volume;
                    }

                    if (_volumeButton != null)
                    {
                        _volumeButton.Content = isMuted ? "🔇" : "🔊";
                    }

                    Debug.WriteLine($"Volume controls updated - Volume: {volume}, Muted: {isMuted}");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating volume controls: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void UpdateNowPlaying(string title, string artist = null, string series = null, string season = null)
        {
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_nowPlayingText == null)
                    {
                        Debug.WriteLine("WARNING: _nowPlayingText is null");
                        return;
                    }

                    string displayText = title;
                    if (!string.IsNullOrEmpty(series))
                    {
                        displayText = $"{series} - {displayText}";
                        if (!string.IsNullOrEmpty(season))
                        {
                            displayText = $"{displayText} - {season}";
                        }
                    }
                    else if (!string.IsNullOrEmpty(artist))
                    {
                        displayText = $"{artist} - {displayText}";
                    }

                    // Only update if the text has actually changed
                    if (_nowPlayingText.Text != displayText)
                    {
                        _nowPlayingText.Text = displayText;
                        Debug.WriteLine($"Updated now playing text to: {displayText}");
                    }
                    else
                    {
                        Debug.WriteLine("Now playing text unchanged, skipping update");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating now playing display: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void UpdateTimeDisplay()
        {
            if (_timeInfo != null && _timelineSlider != null)
            {
                var position = _mediaPlayerManager.CurrentPosition;
                var duration = _mediaPlayerManager.Duration;

                // Update time display text
                _timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";

                // Update slider position if not being dragged
                if (!_mediaPlayerManager.IsDraggingSlider && duration.TotalSeconds > 0)
                {
                    _timelineSlider.Maximum = duration.TotalSeconds;
                    _timelineSlider.Value = position.TotalSeconds;
                }
            }
        }

        public void InitializeTimelineSlider(TimeSpan duration)
        {
            if (_timelineSlider != null)
            {
                _timelineSlider.Minimum = 0;
                _timelineSlider.Maximum = duration.TotalSeconds;
                _timelineSlider.SmallChange = 1;
                _timelineSlider.LargeChange = Math.Max(10, duration.TotalSeconds / 10);
                _timelineSlider.Value = 0;

                // Initial time display update
                UpdateTimeDisplay();
            }
        }

        public void UpdateShuffleButton(bool isEnabled)
        {
            if (_shuffleButton != null)
            {
                _shuffleButton.Opacity = isEnabled ? 1.0 : 0.5;
            }
        }
    }
} 