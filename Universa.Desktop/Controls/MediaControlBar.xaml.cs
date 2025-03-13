using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Universa.Desktop.Models;
using Universa.Desktop.Managers;
using Universa.Desktop.Services;
using System.Diagnostics;

namespace Universa.Desktop.Controls
{
    public partial class MediaControlBar : UserControl
    {
        private MediaPlayerManager _mediaPlayerManager;
        private bool _isDraggingTimelineSlider;
        private DispatcherTimer _updateTimer;
        private Universa.Desktop.Models.Track _currentTrack;
        private DateTime _lastPlayPauseClickTime = DateTime.MinValue;
        private const int DEBOUNCE_INTERVAL_MS = 500; // Debounce interval in milliseconds

        public MediaControlBar()
        {
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            _updateTimer = new DispatcherTimer();
            _updateTimer.Interval = TimeSpan.FromMilliseconds(250);
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        public void Initialize(MediaPlayerManager mediaPlayerManager)
        {
            _mediaPlayerManager = mediaPlayerManager;
            
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.PlaybackStarted += MediaPlayerManager_PlaybackStarted;
                _mediaPlayerManager.PlaybackStopped += MediaPlayerManager_PlaybackStopped;
                _mediaPlayerManager.TrackChanged += MediaPlayerManager_TrackChanged;
                _mediaPlayerManager.PositionChanged += MediaPlayerManager_PositionChanged;
                
                // Initialize shuffle state
                UpdateShuffleButtonState();
                
                // Initialize play/pause button state
                UpdatePlayPauseButtonState();
            }

            // Initialize volume
            VolumeSlider.Value = 1.0;
            UpdateVolumeIcon();
        }

        private void MediaPlayerManager_PlaybackStarted(object sender, EventArgs e)
        {
            Debug.WriteLine("MediaPlayerManager_PlaybackStarted event received");
            UpdatePlayPauseButtonState();
            _updateTimer.Start();
            this.Visibility = Visibility.Visible;
        }

        private void MediaPlayerManager_PlaybackStopped(object sender, EventArgs e)
        {
            Debug.WriteLine("MediaPlayerManager_PlaybackStopped event received");
            UpdatePlayPauseButtonState();
            
            // Don't stop the timer when pausing, only when stopping completely
            if (_mediaPlayerManager != null && !_mediaPlayerManager.IsPaused)
            {
                _updateTimer.Stop();
            }
        }

        private void MediaPlayerManager_TrackChanged(object sender, Universa.Desktop.Models.Track e)
        {
            if (e != null)
            {
                // Update the now playing text
                NowPlayingText.Text = $"{e.Artist} - {e.Title}";
                
                // Update the timeline slider
                TimelineSlider.Maximum = e.Duration.TotalSeconds;
                TimelineSlider.Value = 0;
                
                // Update the time display
                UpdateTimeDisplay(TimeSpan.Zero, e.Duration);
                
                // Store the current track
                _currentTrack = e;
                
                // Start the update timer if it's not already running
                if (!_updateTimer.IsEnabled)
                {
                    _updateTimer.Start();
                }
                
                // Ensure visibility
                this.Visibility = Visibility.Visible;
            }
            else
            {
                NowPlayingText.Text = "No track playing";
                TimelineSlider.Value = 0;
                TimeInfo.Text = "00:00 / 00:00";
            }
        }

        private void MediaPlayerManager_PositionChanged(object sender, TimeSpan e)
        {
            if (!_isDraggingTimelineSlider && _mediaPlayerManager != null)
            {
                TimelineSlider.Value = e.TotalSeconds;
                UpdateTimeDisplay(e, _mediaPlayerManager.Duration);
            }
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaPlayerManager == null) return;

            try
            {
                var position = _mediaPlayerManager.CurrentPosition;
                var duration = _mediaPlayerManager.Duration;

                // Update time display
                TimeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";

                // Update timeline slider
                if (duration > TimeSpan.Zero && !_isDraggingTimelineSlider)
                {
                    TimelineSlider.Maximum = duration.TotalSeconds;
                    TimelineSlider.Value = position.TotalSeconds;
                }
                
                // Ensure play/pause button state is correct
                UpdatePlayPauseButtonState();
                
                // If we have a current track but the MediaPlayerManager doesn't, update the display
                if (_currentTrack != null && _mediaPlayerManager.CurrentTrack == null)
                {
                    _currentTrack = null;
                    NowPlayingText.Text = "No track playing";
                    TimelineSlider.Value = 0;
                    TimeInfo.Text = "00:00 / 00:00";
                }
                // If the current track has changed, update the display
                else if (_mediaPlayerManager.CurrentTrack != null && 
                        (_currentTrack == null || _currentTrack.Id != _mediaPlayerManager.CurrentTrack.Id))
                {
                    MediaPlayerManager_TrackChanged(this, _mediaPlayerManager.CurrentTrack);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateTimer_Tick: {ex.Message}");
            }
        }

        private void UpdateTimeDisplay(TimeSpan position, TimeSpan duration)
        {
            TimeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
        }

        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            // Mark the event as handled to prevent it from bubbling up to other handlers
            e.Handled = true;
            
            if (_mediaPlayerManager == null) return;

            // Implement debouncing to prevent rapid clicking
            DateTime now = DateTime.Now;
            if ((now - _lastPlayPauseClickTime).TotalMilliseconds < DEBOUNCE_INTERVAL_MS)
            {
                Debug.WriteLine("PlayPauseButton_Click: Ignoring click due to debounce");
                return;
            }
            _lastPlayPauseClickTime = now;

            Debug.WriteLine($"PlayPauseButton_Click: Current IsPlaying state: {_mediaPlayerManager.IsPlaying}");
            
            try
            {
                // Use the MediaPlayerManager's TogglePlayPause method which handles all the state changes
                _mediaPlayerManager.TogglePlayPause();
                
                // Update button state based on the current playing state
                UpdatePlayPauseButtonState();
                
                // If we're playing, make sure the timer is running
                if (_mediaPlayerManager.IsPlaying && !_updateTimer.IsEnabled)
                {
                    _updateTimer.Start();
                }
                
                Debug.WriteLine($"PlayPauseButton_Click: After toggle, IsPlaying state: {_mediaPlayerManager.IsPlaying}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayPauseButton_Click: {ex.Message}");
            }
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayerManager == null) return;

            _mediaPlayerManager.StopMedia();
            PlayPauseButton.Content = "▶";
            TimelineSlider.Value = 0;
            this.Visibility = Visibility.Collapsed;
        }

        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            // Mark the event as handled to prevent it from bubbling up to other handlers
            e.Handled = true;
            
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.Previous();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            // Mark the event as handled to prevent it from bubbling up to other handlers
            e.Handled = true;
            
            if (_mediaPlayerManager?.HasPlaylist == true)
            {
                _mediaPlayerManager.Next();
            }
        }

        private void TimelineSlider_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isDraggingTimelineSlider = true;
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.HandleTimelineSliderDragStarted();
            }
        }

        private void TimelineSlider_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isDraggingTimelineSlider = false;
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.HandleTimelineSliderDragCompleted();
            }
        }

        private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayerManager != null && _isDraggingTimelineSlider)
            {
                _mediaPlayerManager.HandleTimelineSliderValueChanged(e.NewValue);
            }
        }

        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.ToggleMute();
                UpdateVolumeIcon();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.SetVolume(e.NewValue);
                UpdateVolumeIcon();
            }
        }

        private void UpdateVolumeIcon()
        {
            if (_mediaPlayerManager == null) return;

            double volume = VolumeSlider.Value;
            bool isMuted = (MuteButton as ToggleButton)?.IsChecked ?? false;

            if (isMuted)
            {
                MuteButton.Content = "🔇";
            }
            else if (volume > 0.66)
            {
                MuteButton.Content = "🔊";
            }
            else if (volume > 0.33)
            {
                MuteButton.Content = "🔉";
            }
            else if (volume > 0)
            {
                MuteButton.Content = "🔈";
            }
            else
            {
                MuteButton.Content = "🔇";
            }
        }

        public void StartPlayback(Universa.Desktop.Models.Track track, bool shuffle = false)
        {
            if (_mediaPlayerManager != null && track != null)
            {
                _mediaPlayerManager.PlayMedia(track);
                PlayPauseButton.Content = "⏸";
                _updateTimer.Start();
                this.Visibility = Visibility.Visible;
            }
        }

        public void StartPlayback()
        {
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.Play();
                PlayPauseButton.Content = "⏸";
                _updateTimer.Start();
                this.Visibility = Visibility.Visible;
            }
        }

        public async void StartPlayback(AudiobookshelfService service, AudiobookItem audiobook)
        {
            if (_mediaPlayerManager != null && audiobook != null)
            {
                var streamUrl = await service.GetStreamUrlAsync(audiobook.Id);
                var track = new Universa.Desktop.Models.Track
                {
                    Id = audiobook.Id,
                    Title = audiobook.Title,
                    Artist = audiobook.Author,
                    Duration = TimeSpan.FromSeconds(audiobook.Duration),
                    StreamUrl = streamUrl,
                    Series = audiobook.Series,
                    Season = audiobook.SeriesSequence
                };

                _mediaPlayerManager.PlayMedia(track);
                PlayPauseButton.Content = "⏸";
                _updateTimer.Start();
                this.Visibility = Visibility.Visible;
            }
        }

        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            // Mark the event as handled to prevent it from bubbling up to other handlers
            e.Handled = true;
            
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.ToggleShuffle();
                UpdateShuffleButtonState();
            }
        }

        private void UpdateShuffleButtonState()
        {
            if (_mediaPlayerManager != null && ShuffleButton != null)
            {
                ShuffleButton.IsChecked = _mediaPlayerManager.IsShuffleEnabled;
                ShuffleButton.Opacity = _mediaPlayerManager.IsShuffleEnabled ? 1.0 : 0.5;
            }
        }

        private void UpdatePlayPauseButtonState()
        {
            if (_mediaPlayerManager != null)
            {
                try
                {
                    bool isPlaying = _mediaPlayerManager.IsPlaying;
                    
                    // Set the button content based on the current playing state
                    Application.Current.Dispatcher.Invoke(() => {
                        PlayPauseButton.Content = isPlaying ? "⏸" : "▶";
                        PlayPauseButton.ToolTip = isPlaying ? "Pause" : "Play";
                    });
                    
                    Debug.WriteLine($"UpdatePlayPauseButtonState: Set button to {PlayPauseButton.Content} based on IsPlaying={isPlaying}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in UpdatePlayPauseButtonState: {ex.Message}");
                }
            }
        }
    }
} 