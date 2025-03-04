using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Diagnostics;
using Universa.Desktop.Windows;
using Universa.Desktop.Models;

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
            Debug.WriteLine("Initializing playback controls");
            
            // Set up play/pause button
            if (_playPauseButton != null)
            {
                Debug.WriteLine("Setting up play/pause button click handler");
                
                // Remove any existing handlers to avoid duplicates
                _playPauseButton.Click -= PlayPauseButton_Click;
                _playPauseButton.Click += PlayPauseButton_Click;
                
                Debug.WriteLine("Play/pause button click handler set up");
                
                // Set initial button state
                UpdatePlayPauseButton(_mediaPlayerManager.IsPlaying);
            }
            else
            {
                Debug.WriteLine("WARNING: _playPauseButton is null");
            }
            
            // Set up previous button
            if (_previousButton != null)
            {
                Debug.WriteLine("Setting up previous button click handler");
                
                // Remove any existing handlers to avoid duplicates
                _previousButton.Click -= PreviousButton_Click;
                _previousButton.Click += PreviousButton_Click;
                
                Debug.WriteLine("Previous button click handler set up");
            }
            else
            {
                Debug.WriteLine("WARNING: _previousButton is null");
            }
            
            // Set up next button
            if (_nextButton != null)
            {
                Debug.WriteLine("Setting up next button click handler");
                
                // Remove any existing handlers to avoid duplicates
                _nextButton.Click -= NextButton_Click;
                _nextButton.Click += NextButton_Click;
                
                Debug.WriteLine("Next button click handler set up");
            }
            else
            {
                Debug.WriteLine("WARNING: _nextButton is null");
            }
            
            // Set up shuffle button
            if (_shuffleButton != null)
            {
                Debug.WriteLine("Setting up shuffle button click handler");
                
                // Remove any existing handlers to avoid duplicates
                _shuffleButton.Click -= ShuffleButton_Click;
                _shuffleButton.Click += ShuffleButton_Click;
                
                Debug.WriteLine("Shuffle button click handler set up");
                
                // Set initial button state
                UpdateShuffleButton(_mediaPlayerManager.IsShuffleEnabled);
            }
            else
            {
                Debug.WriteLine("WARNING: _shuffleButton is null");
            }
            
            // Subscribe to media player events
            Debug.WriteLine("Subscribing to media player events");
            
            // Unsubscribe first to avoid duplicates
            _mediaPlayerManager.PlaybackStarted -= MediaPlayerManager_PlaybackStarted;
            _mediaPlayerManager.PlaybackPaused -= MediaPlayerManager_PlaybackPaused;
            _mediaPlayerManager.PlaybackStopped -= MediaPlayerManager_PlaybackStopped;
            _mediaPlayerManager.ShuffleChanged -= MediaPlayerManager_ShuffleChanged;
            _mediaPlayerManager.TrackChanged -= MediaPlayerManager_TrackChanged;
            
            // Subscribe to events
            _mediaPlayerManager.PlaybackStarted += MediaPlayerManager_PlaybackStarted;
            _mediaPlayerManager.PlaybackPaused += MediaPlayerManager_PlaybackPaused;
            _mediaPlayerManager.PlaybackStopped += MediaPlayerManager_PlaybackStopped;
            _mediaPlayerManager.ShuffleChanged += MediaPlayerManager_ShuffleChanged;
            _mediaPlayerManager.TrackChanged += MediaPlayerManager_TrackChanged;
            
            Debug.WriteLine("Media player event handlers set up");
        }
        
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine($"Play/Pause button clicked. Current state: {(_mediaPlayerManager.IsPlaying ? "Playing" : "Paused")}");
            if (_mediaPlayerManager.IsPlaying)
            {
                Debug.WriteLine("Calling Pause()");
                _mediaPlayerManager.Pause();
            }
            else
            {
                Debug.WriteLine("Calling Play()");
                _mediaPlayerManager.Play();
            }
        }
        
        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Previous button clicked");
            _mediaPlayerManager.Previous();
        }
        
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Next button clicked");
            _mediaPlayerManager.Next();
        }
        
        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Shuffle button clicked");
            _mediaPlayerManager.ToggleShuffle();
        }
        
        private void MediaPlayerManager_PlaybackStarted(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackStarted event received");
            UpdatePlayPauseButton(true);
            UpdateControlsVisibility(_mediaPlayerManager.IsPlayingVideo);
        }
        
        private void MediaPlayerManager_PlaybackPaused(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackPaused event received");
            UpdatePlayPauseButton(false);
        }
        
        private void MediaPlayerManager_PlaybackStopped(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackStopped event received");
            UpdatePlayPauseButton(false);
            UpdateControlsVisibility(false);
        }
        
        private void MediaPlayerManager_ShuffleChanged(object sender, bool isEnabled)
        {
            Debug.WriteLine($"ShuffleChanged event received: {isEnabled}");
            UpdateShuffleButton(isEnabled);
        }
        
        private void MediaPlayerManager_TrackChanged(object sender, Universa.Desktop.Models.Track track)
        {
            Debug.WriteLine($"TrackChanged event received: {track?.Title}");
            UpdateControlsVisibility(track?.IsVideo ?? false);
        }
        
        private void UpdateControlsVisibility(bool isPlayingVideo)
        {
            Debug.WriteLine($"UpdateControlsVisibility called with isPlayingVideo={isPlayingVideo}");
            
            Application.Current.Dispatcher.Invoke(() =>
            {
                // When playing a video, we still want to show the controls
                // but we might want to adjust some of them
                
                // Always show the play/pause button
                if (_playPauseButton != null)
                {
                    _playPauseButton.Visibility = Visibility.Visible;
                }
                
                // Show previous/next buttons for both audio and video
                if (_previousButton != null)
                {
                    _previousButton.Visibility = Visibility.Visible;
                }
                
                if (_nextButton != null)
                {
                    _nextButton.Visibility = Visibility.Visible;
                }
                
                // Show shuffle button only for audio
                if (_shuffleButton != null)
                {
                    _shuffleButton.Visibility = isPlayingVideo ? Visibility.Collapsed : Visibility.Visible;
                }
                
                // Update the timeline slider visibility
                if (_timelineSlider != null)
                {
                    _timelineSlider.Visibility = isPlayingVideo ? Visibility.Collapsed : Visibility.Visible;
                }
                
                // Update the time info visibility
                if (_timeInfo != null)
                {
                    _timeInfo.Visibility = isPlayingVideo ? Visibility.Collapsed : Visibility.Visible;
                }
                
                Debug.WriteLine("Updated controls visibility based on video playback state");
            });
        }
        
        public void ShowMediaControls()
        {
            Debug.WriteLine("\n========== SHOW MEDIA CONTROLS ==========");
            try
            {
                // Make sure we're on the UI thread for all UI operations
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Debug.WriteLine("ShowMediaControls: Running on UI thread");
                    
                    // Try multiple approaches to ensure the controls are visible
                    
                    // Approach 1: Use the controls panel if available
                    if (_controlsPanel != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Setting _controlsPanel visibility to Visible");
                        _controlsPanel.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Debug.WriteLine("ShowMediaControls: _controlsPanel is null");
                    }
                    
                    // Approach 2: Use the media controls grid if available
                    if (_mediaControlsGrid != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Setting _mediaControlsGrid visibility to Visible");
                        _mediaControlsGrid.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Debug.WriteLine("ShowMediaControls: _mediaControlsGrid is null");
                    }
                    
                    // Approach 3: Use the main window's media control bar if available
                    if (_mainWindow != null && _mainWindow.MediaControlBar != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Setting _mainWindow.MediaControlBar visibility to Visible");
                        _mainWindow.MediaControlBar.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        Debug.WriteLine("ShowMediaControls: _mainWindow or its MediaControlBar is null");
                    }
                    
                    // Approach 4: Try to get the main window as a last resort
                    var mainWindow = Application.Current.MainWindow as IMediaWindow;
                    if (mainWindow != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Found Application.Current.MainWindow");
                        
                        if (mainWindow.MediaControlBar != null)
                        {
                            Debug.WriteLine("ShowMediaControls: Setting mainWindow.MediaControlBar visibility to Visible");
                            mainWindow.MediaControlBar.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            Debug.WriteLine("ShowMediaControls: mainWindow.MediaControlBar is null");
                        }
                        
                        if (mainWindow.MediaControlsGrid != null)
                        {
                            Debug.WriteLine("ShowMediaControls: Setting mainWindow.MediaControlsGrid visibility to Visible");
                            mainWindow.MediaControlsGrid.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            Debug.WriteLine("ShowMediaControls: mainWindow.MediaControlsGrid is null");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("ShowMediaControls: Could not find Application.Current.MainWindow as IMediaWindow");
                    }
                    
                    // Approach 5: Try to find the media control bar by name in the main window
                    if (Application.Current.MainWindow != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Trying to find media control bar by name");
                        
                        try
                        {
                            var mediaControlBar = Application.Current.MainWindow.FindName("MediaControlBar") as UIElement;
                            if (mediaControlBar != null)
                            {
                                Debug.WriteLine("ShowMediaControls: Found MediaControlBar by name, setting visibility to Visible");
                                mediaControlBar.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                Debug.WriteLine("ShowMediaControls: Could not find MediaControlBar by name");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"ShowMediaControls: Error finding MediaControlBar by name: {ex.Message}");
                        }
                    }
                    
                    // Make sure the playback controls are visible
                    if (_playPauseButton != null) _playPauseButton.Visibility = Visibility.Visible;
                    if (_previousButton != null) _previousButton.Visibility = Visibility.Visible;
                    if (_nextButton != null) _nextButton.Visibility = Visibility.Visible;
                    if (_shuffleButton != null) _shuffleButton.Visibility = Visibility.Visible;
                    
                    // Update the visibility of playlist controls based on media type
                    bool isPlayingVideo = _mediaPlayerManager != null && _mediaPlayerManager.IsPlayingVideo;
                    UpdateControlsVisibility(isPlayingVideo);
                    
                    Debug.WriteLine("ShowMediaControls: Completed setting visibility");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShowMediaControls: {ex.Message}");
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

        public void UpdatePlayPauseButton(bool isPlaying)
        {
            Debug.WriteLine($"UpdatePlayPauseButton called with isPlaying={isPlaying}");
            
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_playPauseButton != null)
                    {
                        // When playing, show pause icon (⏸)
                        // When paused, show play icon (▶)
                        if (isPlaying)
                        {
                            Debug.WriteLine("Setting play/pause button to PAUSE icon");
                            _playPauseButton.Content = "⏸"; // Pause symbol
                            _playPauseButton.ToolTip = "Pause";
                        }
                        else
                        {
                            Debug.WriteLine("Setting play/pause button to PLAY icon");
                            _playPauseButton.Content = "▶"; // Play symbol
                            _playPauseButton.ToolTip = "Play";
                        }
                        
                        Debug.WriteLine($"Play/Pause button updated. Content: {_playPauseButton.Content}, ToolTip: {_playPauseButton.ToolTip}");
                    }
                    else
                    {
                        Debug.WriteLine("Play/Pause button is null, cannot update");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdatePlayPauseButton: {ex.Message}");
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
            Debug.WriteLine($"MediaControlsManager.UpdateNowPlaying called with title: {title}, artist: {artist}, series: {series}, season: {season}");
            
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (_nowPlayingText == null)
                    {
                        Debug.WriteLine("WARNING: _nowPlayingText is null");
                        
                        // Try to get the now playing text from the main window as a last resort
                        var mainWindow = Application.Current.MainWindow as IMediaWindow;
                        if (mainWindow != null && mainWindow.NowPlayingText != null)
                        {
                            Debug.WriteLine("Found main window's NowPlayingText, updating it directly");
                            
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
                            
                            mainWindow.NowPlayingText.Text = displayText;
                            Debug.WriteLine($"Updated main window's now playing text to: {displayText}");
                        }
                        else
                        {
                            Debug.WriteLine("Could not find main window or its NowPlayingText");
                        }
                        
                        return;
                    }

                    string nowPlayingText = title;
                    if (!string.IsNullOrEmpty(series))
                    {
                        nowPlayingText = $"{series} - {nowPlayingText}";
                        if (!string.IsNullOrEmpty(season))
                        {
                            nowPlayingText = $"{nowPlayingText} - {season}";
                        }
                    }
                    else if (!string.IsNullOrEmpty(artist))
                    {
                        nowPlayingText = $"{artist} - {nowPlayingText}";
                    }

                    // Only update if the text has actually changed
                    if (_nowPlayingText.Text != nowPlayingText)
                    {
                        _nowPlayingText.Text = nowPlayingText;
                        Debug.WriteLine($"Updated now playing text to: {nowPlayingText}");
                    }
                    else
                    {
                        Debug.WriteLine("Now playing text unchanged, skipping update");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateNowPlaying: {ex.Message}");
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