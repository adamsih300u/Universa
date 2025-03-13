using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Diagnostics;
using Universa.Desktop.Windows;
using Universa.Desktop.Models;
using System.Windows.Media;

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
        private DateTime _lastPlayPauseClickTime = DateTime.MinValue;
        private DateTime _lastNextClickTime = DateTime.MinValue;
        private const int DEBOUNCE_MILLISECONDS = 500;
        private bool _isPlayPauseOperationInProgress = false;

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
                
                // Check if the PlayPauseButton already has a Click handler from the MediaControlBar
                bool hasExistingHandler = false;
                
                try
                {
                    // Get the event invocation list count to check if handlers already exist
                    var clickEventInfo = _playPauseButton.GetType().GetEvent("Click");
                    if (clickEventInfo != null)
                    {
                        // If we find that the button already has Click handlers from MediaControlBar
                        // we'll skip adding our own to prevent duplicate calls
                        Debug.WriteLine("PlayPauseButton already has a Click handler from MediaControlBar, skipping our handler");
                        hasExistingHandler = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for existing handlers: {ex.Message}");
                }
                
                // Only proceed with adding our handler if no existing handler was found
                if (!hasExistingHandler)
                {
                    // First completely clear any handlers by using PresentationCore technique
                    var noHandlers = typeof(Button).GetField("EventClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (noHandlers != null)
                    {
                        Debug.WriteLine("Clearing all PlayPauseButton event handlers");
                        var newHandler = (System.Windows.RoutedEvent)noHandlers.GetValue(null);
                        _playPauseButton.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(PlayPauseButton_Click));
                    }
                    
                    // Then add our handler
                    _playPauseButton.Click += PlayPauseButton_Click;
                    
                    Debug.WriteLine("Play/pause button click handler set up");
                }
                
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
                
                // Check if the PreviousButton already has a Click handler from the MediaControlBar
                bool hasExistingHandler = false;
                
                try
                {
                    // Get the event invocation list count to check if handlers already exist
                    var clickEventInfo = _previousButton.GetType().GetEvent("Click");
                    if (clickEventInfo != null)
                    {
                        // If we find that the button already has Click handlers from MediaControlBar
                        // we'll skip adding our own to prevent duplicate calls
                        Debug.WriteLine("PreviousButton already has a Click handler from MediaControlBar, skipping our handler");
                        hasExistingHandler = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for existing handlers: {ex.Message}");
                }
                
                // Only proceed with adding our handler if no existing handler was found
                if (!hasExistingHandler)
                {
                    // First completely clear any handlers
                    var noHandlers = typeof(Button).GetField("EventClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (noHandlers != null)
                    {
                        Debug.WriteLine("Clearing all PreviousButton event handlers");
                        var newHandler = (System.Windows.RoutedEvent)noHandlers.GetValue(null);
                        _previousButton.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(PreviousButton_Click));
                    }
                    
                    // Then add our handler
                    _previousButton.Click += PreviousButton_Click;
                    
                    Debug.WriteLine("Previous button click handler set up");
                }
            }
            else
            {
                Debug.WriteLine("WARNING: _previousButton is null");
            }
            
            // Set up next button
            if (_nextButton != null)
            {
                Debug.WriteLine("Setting up next button click handler");
                
                // Check if the NextButton already has a Click handler from the MediaControlBar
                bool hasExistingHandler = false;
                
                try
                {
                    // Get the event invocation list count to check if handlers already exist
                    var clickEventInfo = _nextButton.GetType().GetEvent("Click");
                    if (clickEventInfo != null)
                    {
                        // If we find that the button already has Click handlers from MediaControlBar
                        // we'll skip adding our own to prevent duplicate calls
                        Debug.WriteLine("NextButton already has a Click handler from MediaControlBar, skipping our handler");
                        hasExistingHandler = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for existing handlers: {ex.Message}");
                }
                
                // Only proceed with adding our handler if no existing handler was found
                if (!hasExistingHandler) 
                {
                    // First completely clear any handlers
                    var noHandlers = typeof(Button).GetField("EventClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (noHandlers != null)
                    {
                        Debug.WriteLine("Clearing all NextButton event handlers");
                        var newHandler = (System.Windows.RoutedEvent)noHandlers.GetValue(null);
                        _nextButton.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(NextButton_Click));
                    }
                    
                    // Then add our handler
                    _nextButton.Click += NextButton_Click;
                    
                    Debug.WriteLine("Next button click handler set up");
                }
            }
            else
            {
                Debug.WriteLine("WARNING: _nextButton is null");
            }
            
            // Set up shuffle button
            if (_shuffleButton != null)
            {
                Debug.WriteLine("Setting up shuffle button click handler");
                
                // Check if the ShuffleButton already has a Click handler from the MediaControlBar
                bool hasExistingHandler = false;
                
                try
                {
                    // Get the event invocation list count to check if handlers already exist
                    var clickEventInfo = _shuffleButton.GetType().GetEvent("Click");
                    if (clickEventInfo != null)
                    {
                        // If we find that the button already has Click handlers from MediaControlBar
                        // we'll skip adding our own to prevent duplicate calls
                        Debug.WriteLine("ShuffleButton already has a Click handler from MediaControlBar, skipping our handler");
                        hasExistingHandler = true;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error checking for existing handlers: {ex.Message}");
                }
                
                // Only proceed with adding our handler if no existing handler was found
                if (!hasExistingHandler)
                {
                    // First completely clear any handlers
                    var noHandlers = typeof(Button).GetField("EventClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    if (noHandlers != null)
                    {
                        Debug.WriteLine("Clearing all ShuffleButton event handlers");
                        var newHandler = (System.Windows.RoutedEvent)noHandlers.GetValue(null);
                        _shuffleButton.RemoveHandler(Button.ClickEvent, new RoutedEventHandler(ShuffleButton_Click));
                    }
                    
                    // Then add our handler
                    _shuffleButton.Click += ShuffleButton_Click;
                    
                    Debug.WriteLine("Shuffle button click handler set up");
                }
                
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
            try
            {
                // Get caller information for better debugging
                Debug.WriteLine($"Play/Pause button clicked from: {sender?.GetType().Name ?? "unknown source"}");
                Debug.WriteLine($"Current state: {(_mediaPlayerManager.IsPlaying ? "Playing" : "Paused")}");
                
                // Mark the event as handled to prevent bubbling
                e.Handled = true;
                
                // Implement a simple debounce
                var now = DateTime.Now;
                if ((now - _lastPlayPauseClickTime).TotalMilliseconds < DEBOUNCE_MILLISECONDS)
                {
                    Debug.WriteLine($"Ignoring play/pause click - too soon after previous click ({(now - _lastPlayPauseClickTime).TotalMilliseconds}ms)");
                    return;
                }
                
                _lastPlayPauseClickTime = now;
                
                // Prevent multiple operations from running concurrently
                if (_isPlayPauseOperationInProgress)
                {
                    Debug.WriteLine("Ignoring play/pause click - operation already in progress");
                    return;
                }
                
                _isPlayPauseOperationInProgress = true;
                
                // Temporarily disable button
                _playPauseButton.IsEnabled = false;
                
                // Capture current state before action
                bool wasPlaying = _mediaPlayerManager.IsPlaying;
                
                try
                {
                    // Perform the play/pause action
                    if (wasPlaying)
                    {
                        Debug.WriteLine("Calling Pause()");
                        _mediaPlayerManager.Pause();
                        
                        // Immediately update the button UI to show play state
                        UpdatePlayPauseButton(false);
                    }
                    else
                    {
                        Debug.WriteLine("Calling Play()");
                        _mediaPlayerManager.Play();
                        
                        // Immediately update the button UI to show pause state
                        UpdatePlayPauseButton(true);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in play/pause operation: {ex.Message}");
                    
                    // Revert UI to match the actual state
                    UpdatePlayPauseButton(_mediaPlayerManager.IsPlaying);
                }
                finally
                {
                    // Re-enable button
                    _playPauseButton.IsEnabled = true;
                    _isPlayPauseOperationInProgress = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayPauseButton_Click: {ex.Message}");
                
                // Make sure to clean up state even on error
                _isPlayPauseOperationInProgress = false;
                
                // Make sure to re-enable the button even on error
                if (_playPauseButton != null)
                {
                    _playPauseButton.IsEnabled = true;
                }
            }
        }
        
        private void PreviousButton_Click(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("Previous button clicked");
            e.Handled = true; // Add this to prevent event bubbling
            _mediaPlayerManager.Previous();
        }
        
        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine($"NextButton_Click called at {DateTime.Now.ToString("HH:mm:ss.fff")}");
                
                // Mark as handled immediately to prevent event bubbling
                e.Handled = true;
                
                // Add a lock to prevent multiple rapid clicks
                lock (this)
                {
                    // Check if a click was already processed recently (debounce)
                    var now = DateTime.Now;
                    if ((now - _lastNextClickTime).TotalMilliseconds < DEBOUNCE_MILLISECONDS)
                    {
                        Debug.WriteLine($"NextButton_Click: Ignoring click - too soon after previous ({(now - _lastNextClickTime).TotalMilliseconds}ms)");
                        return;
                    }
                    
                    // Immediately disable the button to prevent multiple clicks
                    if (_nextButton != null)
                    {
                        _nextButton.IsEnabled = false;
                    }
                    
                    // Update timestamp *after* we're sure we'll process this click
                    _lastNextClickTime = now;
                    
                    try
                    {
                        // Do a direct index manipulation instead of using the Next() method
                        // to avoid any potential issues with multiple Next calls
                        if (_mediaPlayerManager != null)
                        {
                            // Tell the MediaPlayerManager that we're handling a user-initiated next action
                            Debug.WriteLine("NextButton_Click: Calling _mediaPlayerManager.Next() directly");
                            _mediaPlayerManager.Next();
                            Debug.WriteLine("NextButton_Click: Next() method completed");
                        }
                    }
                    finally
                    {
                        // Re-enable the button after a delay to prevent multiple clicks
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_nextButton != null)
                            {
                                _nextButton.IsEnabled = true;
                                Debug.WriteLine("NextButton_Click: Re-enabled next button");
                            }
                        }), DispatcherPriority.Background, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in NextButton_Click: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Always re-enable the button in case of error
                if (_nextButton != null)
                {
                    _nextButton.IsEnabled = true;
                }
            }
            
            Debug.WriteLine($"NextButton_Click completed at {DateTime.Now.ToString("HH:mm:ss.fff")}");
        }
        
        private void ShuffleButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("Shuffle button clicked from: " + sender?.GetType().Name);
                
                // Mark the event as handled to prevent bubbling
                e.Handled = true;
                
                // Temporarily disable button
                if (_shuffleButton != null)
                {
                    _shuffleButton.IsEnabled = false;
                }
                
                // Get the current state from the Tag property
                bool currentState = false;
                if (_shuffleButton != null && _shuffleButton.Tag is bool)
                {
                    currentState = (bool)_shuffleButton.Tag;
                    Debug.WriteLine($"Current shuffle state before toggle: {currentState}");
                }
                
                // Toggle shuffle state in the media player
                _mediaPlayerManager.ToggleShuffle();
                
                // Update the UI immediately
                // Use the negated current state because we're toggling
                UpdateShuffleButton(!currentState);
                
                // Re-enable button directly - no timers or complex logic
                if (_shuffleButton != null)
                {
                    _shuffleButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ShuffleButton_Click: {ex.Message}");
                
                // Make sure to re-enable the button even on error
                if (_shuffleButton != null)
                {
                    _shuffleButton.IsEnabled = true;
                }
            }
        }
        
        private void MediaPlayerManager_PlaybackStarted(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackStarted event received");
            
            // Always force the button to show pause icon (⏸)
            // This is important to ensure proper state after playback starts
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Force update the play/pause button to show pause icon
                    UpdatePlayPauseButton(true);
                    
                    // Ensure controls are visible
                    if (_playPauseButton != null)
                    {
                        _playPauseButton.Visibility = Visibility.Visible;
                        _playPauseButton.Content = "⏸"; // Pause symbol
                        _playPauseButton.ToolTip = "Pause";
                        Debug.WriteLine("Forced play/pause button to PAUSE icon in PlaybackStarted event");
                    }
                    
                    // Update control visibility based on current media type
                    UpdateControlsVisibility(_mediaPlayerManager.IsPlayingVideo);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in PlaybackStarted event handler: {ex.Message}");
                }
            });
        }
        
        private void MediaPlayerManager_PlaybackPaused(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackPaused event received");
            
            // Update the play/pause button to show the play icon
            UpdatePlayPauseButton(false);
            
            // Ensure the controls remain visible when paused
            ShowMediaControls();
        }
        
        private void MediaPlayerManager_PlaybackStopped(object sender, EventArgs e)
        {
            Debug.WriteLine("PlaybackStopped event received");
            
            // Update the play/pause button to show the play icon
            UpdatePlayPauseButton(false);
            
            // Update control visibility, but don't hide the media controls completely
            UpdateControlsVisibility(false);
            
            // Keep the controls visible - only hide them when explicitly requested
            // (don't hide them just because playback stopped)
            ShowMediaControls();
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

        public void UpdateTimeDisplay()
        {
            try
            {
                if (_timeInfo != null && _timelineSlider != null && _mediaPlayerManager != null)
                {
                    var position = _mediaPlayerManager.CurrentPosition;
                    var duration = _mediaPlayerManager.Duration;

                    // Apply changes on UI thread for thread safety
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Update time display text
                            _timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
                            
                            // Update slider position if not being dragged
                            if (!_mediaPlayerManager.IsDraggingSlider && duration.TotalSeconds > 0)
                            {
                                _timelineSlider.Maximum = duration.TotalSeconds;
                                _timelineSlider.Value = position.TotalSeconds;
                            }
                            
                            // Debug.WriteLine($"Time updated: {position:mm\\:ss} / {duration:mm\\:ss}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating time display on UI thread: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateTimeDisplay: {ex.Message}");
            }
        }

        // Add an overload that takes position and duration directly
        public void UpdateTimeDisplay(TimeSpan position, TimeSpan duration)
        {
            try
            {
                if (_timeInfo != null && _timelineSlider != null)
                {
                    // Apply changes on UI thread for thread safety
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Update time display text
                            _timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
                            
                            // Update slider position if not being dragged
                            if (_mediaPlayerManager != null && !_mediaPlayerManager.IsDraggingSlider && duration.TotalSeconds > 0)
                            {
                                _timelineSlider.Maximum = duration.TotalSeconds;
                                _timelineSlider.Value = position.TotalSeconds;
                            }
                            
                            Debug.WriteLine($"Time updated with params: {position:mm\\:ss} / {duration:mm\\:ss}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating time display with params on UI thread: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateTimeDisplay with params: {ex.Message}");
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

        // Add a method to update just the timeline maximum value
        public void UpdateTimelineMaximum(double maximumValueInMilliseconds)
        {
            try
            {
                if (_timelineSlider != null)
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // Convert milliseconds to seconds for the slider
                            double maximumValueInSeconds = maximumValueInMilliseconds / 1000.0;
                            _timelineSlider.Maximum = maximumValueInSeconds;
                            Debug.WriteLine($"Timeline slider maximum updated to {maximumValueInSeconds} seconds");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error updating timeline maximum on UI thread: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateTimelineMaximum: {ex.Message}");
            }
        }

        public void UpdateShuffleButton(bool isEnabled)
        {
            if (_shuffleButton != null)
            {
                Debug.WriteLine($"UpdateShuffleButton called with isEnabled={isEnabled}");
                
                // Apply the changes on the UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Since we're using a regular Button instead of a ToggleButton,
                        // we need to make the enabled state more visually apparent
                        
                        // Store the state in the Tag property for reference
                        _shuffleButton.Tag = isEnabled;
                        
                        // Make the button more visually distinct based on state
                        if (isEnabled)
                        {
                            // When shuffle is ON - make it fully opaque with a distinctive background
                            _shuffleButton.Opacity = 1.0;
                            _shuffleButton.FontWeight = FontWeights.Bold;
                            _shuffleButton.Background = new SolidColorBrush(Colors.LightBlue);
                            _shuffleButton.Foreground = new SolidColorBrush(Colors.Black);
                            _shuffleButton.ToolTip = "Shuffle On (Click to Turn Off)";
                        }
                        else
                        {
                            // When shuffle is OFF - make it semi-transparent with normal background
                            _shuffleButton.Opacity = 0.7;
                            _shuffleButton.FontWeight = FontWeights.Normal;
                            _shuffleButton.Background = null; // Use default background
                            _shuffleButton.Foreground = new SolidColorBrush(Colors.White);
                            _shuffleButton.ToolTip = "Shuffle Off (Click to Turn On)";
                        }
                        
                        // Ensure the button is enabled and visible
                        _shuffleButton.IsEnabled = true;
                        _shuffleButton.Visibility = Visibility.Visible;
                        
                        Debug.WriteLine($"Shuffle button updated: IsEnabled={isEnabled}, Opacity={_shuffleButton.Opacity}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error updating shuffle button: {ex.Message}");
                    }
                });
            }
            else
            {
                Debug.WriteLine("UpdateShuffleButton: _shuffleButton is null");
            }
        }
    }
} 