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
        private Universa.Desktop.Models.Track _currentTrack;
        private DateTime _lastPlayPauseClickTime = DateTime.MinValue;
        private const int DEBOUNCE_INTERVAL_MS = 500; // Debounce interval in milliseconds

        public MediaControlBar()
        {
            InitializeComponent();
            // Hide the control bar by default
            this.Visibility = Visibility.Collapsed;
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

                // Hide the control bar by default
                this.Visibility = Visibility.Collapsed;
                NowPlayingText.Text = string.Empty;
                TimelineSlider.Value = 0;
                TimeInfo.Text = "00:00 / 00:00";

                // Only show if there's media playing or paused
                if (_mediaPlayerManager.IsPlaying || _mediaPlayerManager.IsPaused)
                {
                    this.Visibility = Visibility.Visible;
                    if (_mediaPlayerManager.CurrentTrack != null)
                    {
                        NowPlayingText.Text = $"{_mediaPlayerManager.CurrentTrack.Artist} - {_mediaPlayerManager.CurrentTrack.Title}";
                    }
                }
            }

            // Initialize volume
            VolumeSlider.Value = 1.0;
            UpdateVolumeIcon();
        }

        private void MediaPlayerManager_PlaybackStarted(object sender, EventArgs e)
        {
            // Only show the control bar if we have a track playing
            if (_mediaPlayerManager != null && _mediaPlayerManager.CurrentTrack != null)
            {
                this.Visibility = Visibility.Visible;
                UpdateNowPlayingText(_mediaPlayerManager.CurrentTrack);
                UpdatePlayPauseButtonState();
            }
            else
            {
                this.Visibility = Visibility.Collapsed;
                NowPlayingText.Text = string.Empty;
            }
        }

        private void MediaPlayerManager_PlaybackStopped(object sender, EventArgs e)
        {
            // Hide the control bar and reset all UI elements when playback stops
            this.Visibility = Visibility.Collapsed;
            NowPlayingText.Text = string.Empty;
            TimelineSlider.Value = 0;
            TimeInfo.Text = "00:00 / 00:00";
            UpdatePlayPauseButtonState();
        }

        private void MediaPlayerManager_TrackChanged(object sender, Universa.Desktop.Models.Track track)
        {
            if (track == null)
            {
                // Hide the control bar and clear the text when no track is playing
                this.Visibility = Visibility.Collapsed;
                NowPlayingText.Text = string.Empty;
                TimelineSlider.Value = 0;
                TimelineSlider.Maximum = 0; 
                TimeInfo.Text = "00:00 / 00:00";
                _currentTrack = null; // Ensure _currentTrack is also reset
                return;
            }

            // Store the current track
            _currentTrack = track;

            // Always show the control bar when we have a track (video or audio)
            this.Visibility = Visibility.Visible;
            UpdateNowPlayingText(track);

            TimeSpan durationToUse = track.Duration;
            if (durationToUse > TimeSpan.Zero)
            {
                TimelineSlider.Maximum = durationToUse.TotalSeconds;
                TimeInfo.Text = $"00:00 / {durationToUse:mm\\:ss}";
            }
            else
            {
                // Fallback if track.Duration is zero, try manager's duration
                var managerDuration = _mediaPlayerManager?.Duration ?? TimeSpan.Zero;
                if (managerDuration > TimeSpan.Zero) {
                     TimelineSlider.Maximum = managerDuration.TotalSeconds;
                     TimeInfo.Text = $"00:00 / {managerDuration:mm\\:ss}";
                } else {
                     TimelineSlider.Maximum = 0; 
                     TimeInfo.Text = "00:00 / --:--";
                }
            }
            TimelineSlider.Value = 0; // Reset position for new track
        }

        private void MediaPlayerManager_PositionChanged(object sender, TimeSpan currentPosition)
        {
            if (!_isDraggingTimelineSlider && _mediaPlayerManager != null)
            {
                var currentManagerDuration = _mediaPlayerManager.Duration;

                // Ensure slider maximum is up-to-date with the manager's duration
                if (TimelineSlider.Maximum != currentManagerDuration.TotalSeconds && currentManagerDuration > TimeSpan.Zero)
                {
                    TimelineSlider.Maximum = currentManagerDuration.TotalSeconds;
                }
                
                // Update slider value
                if (TimelineSlider.Maximum > 0)
                {
                     TimelineSlider.Value = currentPosition.TotalSeconds;
                }
                else
                {
                    TimelineSlider.Value = 0; 
                }

                UpdateTimeDisplay(currentPosition, currentManagerDuration); 
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
                this.Visibility = Visibility.Visible;
            }
        }

        public void StartPlayback()
        {
            if (_mediaPlayerManager != null)
            {
                _mediaPlayerManager.Play();
                PlayPauseButton.Content = "⏸";
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

        private void UpdateNowPlayingText(Universa.Desktop.Models.Track track)
        {
            if (track == null)
            {
                NowPlayingText.Text = string.Empty;
                return;
            }

            // Format differently for TV episodes vs music/movies
            if (track.IsVideo && !string.IsNullOrEmpty(track.Series))
            {
                // For TV episodes: "Series Name - Episode Title"
                var displayText = $"{track.Series} - {track.Title}";
                if (!string.IsNullOrEmpty(track.Season))
                {
                    displayText = $"{track.Series} ({track.Season}) - {track.Title}";
                }
                NowPlayingText.Text = displayText;
            }
            else if (!string.IsNullOrEmpty(track.Artist))
            {
                // For music: "Artist - Title"
                NowPlayingText.Text = $"{track.Artist} - {track.Title}";
            }
            else
            {
                // Fallback: just the title
                NowPlayingText.Text = track.Title;
            }
        }
    }
} 