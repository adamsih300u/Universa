using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using Universa.Desktop.Models;
using Universa.Desktop.Windows;
using Universa.Desktop.Views;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;
using System.Reflection;
using System.ComponentModel;
using Universa.Desktop.Managers;

namespace Universa.Desktop.Managers
{
    public enum MediaType
    {
        Audio,
        Video
    }

    public class MediaItem
    {
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string StreamUrl { get; set; }
        public MediaType Type { get; set; }
        public string Series { get; set; }
        public string Season { get; set; }
    }

    public class MediaPlayerManager : IDisposable, INotifyPropertyChanged
    {
        private readonly IMediaWindow _window;
        private readonly MediaElement _mediaElement;
        private List<Universa.Desktop.Models.Track> _playlist;
        private int _currentTrackIndex;
        private bool _isPlaying;
        private bool _isShuffle;
        private bool _isMuted = false;
        private double _lastVolume = 1.0;
        private double _currentVolume = 1.0;
        private DispatcherTimer _positionTimer;
        private bool _isDraggingTimelineSlider;
        private bool _disposed;
        private TimeSpan? _lastReportedPosition;
        private bool _isPaused;
        private MediaControlsManager _controlsManager;
        private readonly IConfigurationService _configService;
        private bool _isShuffleEnabled;
        private List<Universa.Desktop.Models.Track> _originalPlaylist;
        private bool _isPlayingVideo = false;
        private VideoPlayerWindow _currentVideoWindow = null;
        private TimeSpan _duration;
        private VideoWindowManager _videoWindowManager;
        private bool _isVideoPlaying;

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackPaused;
        public event EventHandler PlaybackStopped;
        public event EventHandler<Universa.Desktop.Models.Track> TrackChanged;
        public event EventHandler<TimeSpan> PositionChanged;
        public event EventHandler<bool> ShuffleChanged;
        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsPlaying
        {
            get
            {
                Debug.WriteLine($"IsPlaying getter called, returning: {_isPlaying}");
                return _isPlaying;
            }
            private set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    Debug.WriteLine($"IsPlaying changed to: {_isPlaying}");
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPlaying)));
                    
                    // Ensure media controls reflect the current state
                    _controlsManager?.UpdatePlayPauseButton(_isPlaying);
                }
            }
        }
        public bool HasPlaylist
        {
            get
            {
                bool hasPlaylist = _playlist != null && _playlist.Count > 0;
                Debug.WriteLine($"HasPlaylist getter called, returning: {hasPlaylist}");
                return hasPlaylist;
            }
        }
        public Universa.Desktop.Models.Track CurrentTrack => HasPlaylist ? _playlist[_currentTrackIndex] : null;
        public bool IsPaused 
        { 
            get => _isPaused;
            private set => _isPaused = value;
        }
        public TimeSpan CurrentPosition => _mediaElement?.Position ?? TimeSpan.Zero;
        public TimeSpan Duration => _duration;
        public bool IsShuffleEnabled => _isShuffleEnabled;
        public bool HasMedia => _mediaElement?.Source != null;
        public bool IsPlayingVideo => _isPlayingVideo;
        public bool IsVideoPlaying 
        { 
            get => _isVideoPlaying;
            set
            {
                if (_isVideoPlaying != value)
                {
                    _isVideoPlaying = value;
                    OnPropertyChanged(nameof(IsVideoPlaying));
                }
            }
        }

        public MediaPlayerManager(IMediaWindow window)
        {
            _window = window;
            
            if (window != null)
            {
                _mediaElement = window.MediaPlayer;
                if (_mediaElement == null)
                {
                    Debug.WriteLine("MediaPlayerManager constructor: window.MediaPlayer is null");
                }
                else
                {
                    Debug.WriteLine("MediaPlayerManager constructor: Successfully initialized _mediaElement from window");
                }
            }
            else
            {
                Debug.WriteLine("MediaPlayerManager constructor: window is null");
            }
            
            _configService = Services.ServiceLocator.Instance.GetRequiredService<IConfigurationService>();
            
            // Initialize playlist
            _playlist = new List<Universa.Desktop.Models.Track>();
            
            Debug.WriteLine("MediaPlayerManager constructor completed");
        }
        
        public void InitializeWithWindow(IMediaWindow window)
        {
            Debug.WriteLine("InitializeWithWindow called");
            
            if (window == null)
            {
                Debug.WriteLine("InitializeWithWindow: window is null");
                return;
            }
                
            // If _window is null, we can't do much
            if (_window == null)
            {
                Debug.WriteLine("InitializeWithWindow: _window is null, cannot initialize");
                return;
            }
            
            // If _mediaElement is null, try to get it from the window
            if (_mediaElement == null)
            {
                Debug.WriteLine("InitializeWithWindow: _mediaElement is null, trying to get it from window");
                var mediaElement = window.MediaPlayer;
                if (mediaElement != null)
                {
                    // Use reflection to set the readonly field
                    var field = this.GetType().GetField("_mediaElement", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        field.SetValue(this, mediaElement);
                        Debug.WriteLine("InitializeWithWindow: Successfully set _mediaElement using reflection");
                    }
                    else
                    {
                        Debug.WriteLine("InitializeWithWindow: Could not find _mediaElement field using reflection");
                        return;
                    }
                }
                else
                {
                    Debug.WriteLine("InitializeWithWindow: window.MediaPlayer is null");
                    return;
                }
            }
            
            // Initialize the media element events
            _mediaElement.MediaEnded += OnMediaEnded;
            _mediaElement.MediaOpened += OnMediaOpened;
            _mediaElement.MediaFailed += OnMediaFailed;

            // Create the controls manager
            _controlsManager = new MediaControlsManager(
                _window,
                this,
                _window.MediaControlsGrid,
                _window.PlayPauseButton,
                _window.PreviousButton,
                _window.NextButton,
                _window.ShuffleButton,
                _window.VolumeButton,
                _window.VolumeSlider,
                _window.TimeInfo,
                _window.TimelineSlider,
                _window.NowPlayingText,
                _window.MediaControlsGrid);

            // Initialize position timer
            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
            _positionTimer.Tick += PositionTimer_Tick;

            // Load volume state from config
            LoadVolumeState();
            
            // Set initial volume
            if (_mediaElement != null)
            {
                _mediaElement.Volume = _currentVolume;
                if (_window.VolumeSlider != null)
                {
                    _window.VolumeSlider.Value = _currentVolume * 100;
                }
            }
            
            Debug.WriteLine("InitializeWithWindow completed successfully");
        }

        private void PositionTimer_Tick(object sender, EventArgs e)
        {
            if (_mediaElement != null && _mediaElement.Source != null && !_isDraggingTimelineSlider)
            {
                // Only update if the position has changed
                if (_lastReportedPosition != _mediaElement.Position)
                {
                    _lastReportedPosition = _mediaElement.Position;
                    PositionChanged?.Invoke(this, _mediaElement.Position);
                    
                    if (_controlsManager != null)
                    {
                        UpdateTimeDisplay(_mediaElement.Position, Duration);
                    }
                }
            }
        }

        public void SetPlaylist(IEnumerable<Universa.Desktop.Models.Track> tracks, bool shuffle = false)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            
            // Save the original playlist
            _originalPlaylist = tracks.ToList();
            
            // Set the shuffle state
            _isShuffleEnabled = shuffle;
            
            if (shuffle)
            {
                // Create a shuffled playlist
                var random = new Random();
                _playlist = _originalPlaylist.OrderBy(x => random.Next()).ToList();
            }
            else
            {
                // Use the original playlist
                _playlist = new List<Universa.Desktop.Models.Track>(_originalPlaylist);
            }
            
            _currentTrackIndex = 0;
            
            // Notify listeners that shuffle state has changed
            ShuffleChanged?.Invoke(this, _isShuffleEnabled);
            
            // Start playing the first track
            PlayCurrentTrack();
        }

        private void ShufflePlaylist()
        {
            if (_playlist == null || !_playlist.Any()) return;

            var currentTrack = _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count 
                ? _playlist[_currentTrackIndex] 
                : null;

            var rng = new Random();
            _playlist = _playlist.OrderBy(x => rng.Next()).ToList();

            if (currentTrack != null)
            {
                _currentTrackIndex = _playlist.IndexOf(currentTrack);
            }
        }

        private void PlayCurrentTrack()
        {
            Debug.WriteLine("PlayCurrentTrack called");
            LogPlaylistState("PlayCurrentTrack - Before");
            
            // Show media controls immediately
            _controlsManager?.ShowMediaControls();
            
            try
            {
                if (!HasPlaylist)
                {
                    Debug.WriteLine("No playlist available for PlayCurrentTrack");
                    return;
                }

                // Ensure we have a valid playlist and index
                if (_playlist == null || _playlist.Count == 0)
                {
                    Debug.WriteLine("Playlist is null or empty in PlayCurrentTrack");
                    return;
                }

                // Validate current track index
                if (_currentTrackIndex < 0 || _currentTrackIndex >= _playlist.Count)
                {
                    Debug.WriteLine($"Invalid track index: {_currentTrackIndex}, resetting to 0");
                    _currentTrackIndex = 0;
                }

                var track = _playlist[_currentTrackIndex];
                if (track == null)
                {
                    Debug.WriteLine("Current track is null");
                    return;
                }

                Debug.WriteLine($"Playing track: {track.Title}, StreamUrl: {track.StreamUrl}");

                // Initialize media element if needed
                var mediaElement = GetMediaElement();
                if (mediaElement == null)
                {
                    Debug.WriteLine("Media element is null, cannot play track");
                    return;
                }

                // Handle video tracks
                bool isVideo = track.StreamUrl?.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) == true ||
                               track.StreamUrl?.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) == true ||
                               track.StreamUrl?.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) == true;

                // Handle video functionality safely
                if (isVideo && _videoWindowManager != null)
                {
                    Debug.WriteLine("Track is a video, but video window manager handling is disabled");
                    // We'll set the flag but not call methods on _videoWindowManager since we don't know its type
                    IsVideoPlaying = true;
                }
                else
                {
                    Debug.WriteLine("Track is audio or video window manager is null");
                    IsVideoPlaying = false;
                }

                // Set media source and start playback
                if (!string.IsNullOrEmpty(track.StreamUrl))
                {
                    Debug.WriteLine($"Setting media source to: {track.StreamUrl}");
                    mediaElement.Source = new Uri(track.StreamUrl);
                    
                    // Ensure event handlers are attached
                    AttachMediaElementEvents(mediaElement);
                    
                    // Start playback
                    Debug.WriteLine("Starting playback");
                    mediaElement.Play();
                    IsPlaying = true;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(true);
                    
                    // Update now playing info
                    if (PlaybackStarted != null)
                    {
                        Debug.WriteLine("Raising PlaybackStarted event");
                        PlaybackStarted(this, EventArgs.Empty);
                    }
                    
                    if (TrackChanged != null && track != null)
                    {
                        Debug.WriteLine($"Raising TrackChanged event for track: {track.Title}");
                        TrackChanged(this, track);
                    }
                    
                    // Ensure media controls are visible
                    _controlsManager?.ShowMediaControls();
                    
                    Debug.WriteLine("Playback started successfully");
                }
                else
                {
                    Debug.WriteLine("Track StreamUrl is null or empty");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayCurrentTrack: {ex.Message}");
            }
            
            LogPlaylistState("PlayCurrentTrack - After");
            Debug.WriteLine("PlayCurrentTrack completed");
        }

        private void AttachMediaElementEvents(MediaElement mediaElement)
        {
            if (mediaElement == null) return;
            
            // Remove existing handlers to avoid duplicates
            mediaElement.MediaOpened -= MediaElement_MediaOpened;
            mediaElement.MediaEnded -= MediaElement_MediaEnded;
            mediaElement.MediaFailed -= MediaElement_MediaFailed;
            
            // Add handlers
            mediaElement.MediaOpened += MediaElement_MediaOpened;
            mediaElement.MediaEnded += MediaElement_MediaEnded;
            mediaElement.MediaFailed += MediaElement_MediaFailed;
            
            Debug.WriteLine("Media element events attached");
        }

        public void Play()
        {
            Debug.WriteLine("Play method called");
            LogPlaylistState("Play - Before");

            try
            {
                // Handle video functionality safely
                if (IsVideoPlaying && _videoWindowManager != null)
                {
                    Debug.WriteLine("Video is playing, but video window manager handling is disabled");
                    // We won't call methods on _videoWindowManager since we don't know its type
                }

                var mediaElement = GetMediaElement();
                if (mediaElement != null)
                {
                    if (mediaElement.Source == null && HasPlaylist)
                    {
                        Debug.WriteLine("Media element source is null, attempting to set from current track");
                        var currentTrack = _playlist[_currentTrackIndex];
                        if (currentTrack != null && !string.IsNullOrEmpty(currentTrack.StreamUrl))
                        {
                            mediaElement.Source = new Uri(currentTrack.StreamUrl);
                        }
                    }

                    Debug.WriteLine("Playing media element");
                    mediaElement.Play();
                    IsPlaying = true;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(true);
                    
                    Debug.WriteLine("Media element playback started");
                }
                else
                {
                    Debug.WriteLine("Media element is null, attempting to play current track");
                    if (HasPlaylist)
                    {
                        PlayCurrentTrack();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Play method: {ex.Message}");
            }

            LogPlaylistState("Play - After");
        }

        public void Pause()
        {
            Debug.WriteLine("Pause method called");

            try
            {
                var mediaElement = GetMediaElement();
                if (mediaElement != null)
                {
                    Debug.WriteLine("Pausing media element");
                    mediaElement.Pause();
                    IsPlaying = false;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(false);
                    
                    Debug.WriteLine("Media element paused");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Pause method: {ex.Message}");
            }
        }

        public void TogglePlayPause()
        {
            Debug.WriteLine($"TogglePlayPause called. Current IsPlaying state: {IsPlaying}");
            
            try
            {
                if (IsPlaying)
                {
                    Debug.WriteLine("Currently playing, will pause");
                    Pause();
                }
                else
                {
                    Debug.WriteLine("Currently paused, will play");
                    Play();
                }
                
                // Ensure the button state is updated
                _controlsManager?.UpdatePlayPauseButton(IsPlaying);
                
                Debug.WriteLine($"After toggle, IsPlaying state: {IsPlaying}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in TogglePlayPause: {ex.Message}");
            }
        }

        public void Stop()
        {
            Debug.WriteLine("MediaPlayerManager.Stop() called");
            
            // If we're playing a video, control the video window
            if (_isPlayingVideo && _currentVideoWindow != null)
            {
                Debug.WriteLine("Stopping video in video window");
                _currentVideoWindow.Close();
                _currentVideoWindow = null;
                _isPlayingVideo = false;
                _isPlaying = false;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            // Get the media element
            var mediaElement = GetMediaElement();
            
            if (mediaElement != null)
            {
                Debug.WriteLine("Stopping media element");
                mediaElement.Stop();
                _isPlaying = false;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
                Debug.WriteLine("Media playback stopped");
            }
            else
            {
                Debug.WriteLine("WARNING: mediaElement is null");
            }
        }

        public void Next()
        {
            Debug.WriteLine("Next method called");
            LogPlaylistState("Next - Before");

            try
            {
                if (!HasPlaylist)
                {
                    Debug.WriteLine("No playlist available for Next operation");
                    return;
                }

                // Ensure we have a valid playlist
                if (_playlist == null || _playlist.Count == 0)
                {
                    Debug.WriteLine("Playlist is null or empty in Next method");
                    return;
                }

                // Move to next track
                _currentTrackIndex++;
                
                // Loop back to beginning if we've reached the end
                if (_currentTrackIndex >= _playlist.Count)
                {
                    Debug.WriteLine("Reached end of playlist, looping back to beginning");
                    _currentTrackIndex = 0;
                }

                Debug.WriteLine($"Moving to next track at index: {_currentTrackIndex}");
                PlayCurrentTrack();
                
                // Ensure media controls are visible
                _controlsManager?.ShowMediaControls();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Next method: {ex.Message}");
            }

            LogPlaylistState("Next - After");
        }

        public void Previous()
        {
            Debug.WriteLine("Previous method called");
            LogPlaylistState("Previous - Before");

            try
            {
                if (!HasPlaylist)
                {
                    Debug.WriteLine("No playlist available for Previous operation");
                    return;
                }

                // Ensure we have a valid playlist
                if (_playlist == null || _playlist.Count == 0)
                {
                    Debug.WriteLine("Playlist is null or empty in Previous method");
                    return;
                }

                // Move to previous track
                _currentTrackIndex--;
                
                // Loop to end if we've gone before the beginning
                if (_currentTrackIndex < 0)
                {
                    Debug.WriteLine("Reached beginning of playlist, looping to end");
                    _currentTrackIndex = _playlist.Count - 1;
                }

                Debug.WriteLine($"Moving to previous track at index: {_currentTrackIndex}");
                PlayCurrentTrack();
                
                // Ensure media controls are visible
                _controlsManager?.ShowMediaControls();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Previous method: {ex.Message}");
            }

            LogPlaylistState("Previous - After");
        }

        public void SetPosition(TimeSpan position)
        {
            _mediaElement.Position = position;
            PositionChanged?.Invoke(this, position);
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            Next();
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            if (_mediaElement.NaturalDuration.HasTimeSpan)
            {
                // Update the timeline slider maximum
                if (_controlsManager != null)
                {
                    _controlsManager.InitializeTimelineSlider(_mediaElement.NaturalDuration.TimeSpan);
                }

                // Start the position timer
                if (_positionTimer != null)
                {
                    _positionTimer.Start();
                }
                else
                {
                    // Create a new position timer if it doesn't exist
                    _positionTimer = new DispatcherTimer();
                    _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _positionTimer.Tick += PositionTimer_Tick;
                }
                
                _positionTimer.Start();

                // Update the time display
                UpdateTimeDisplay(TimeSpan.Zero, _mediaElement.NaturalDuration.TimeSpan);
            }
        }

        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Stop();
            System.Windows.MessageBox.Show($"Media playback failed: {e.ErrorException.Message}", "Playback Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        private void MediaElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("MediaElement_MediaOpened event fired");
            
            try
            {
                var mediaElement = sender as MediaElement;
                if (mediaElement != null)
                {
                    // Start playback and update state
                    mediaElement.Play();
                    IsPlaying = true;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(true);
                    
                    // Update duration
                    if (mediaElement.NaturalDuration.HasTimeSpan)
                    {
                        // Check if we have a setter for Duration or use a different approach
                        _duration = mediaElement.NaturalDuration.TimeSpan;
                        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
                        Debug.WriteLine($"Media duration set to: {_duration}");
                    }
                    
                    // Show media controls
                    _controlsManager?.ShowMediaControls();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MediaElement_MediaOpened: {ex.Message}");
            }
        }

        private void MediaElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            Debug.WriteLine("MediaElement_MediaEnded event fired");
            
            try
            {
                IsPlaying = false;
                
                // Ensure the button state is updated
                _controlsManager?.UpdatePlayPauseButton(false);
                
                if (HasPlaylist)
                {
                    Debug.WriteLine("Media ended, attempting to play next track");
                    Next();
                }
                else
                {
                    Debug.WriteLine("Media ended, no playlist available");
                    var mediaElement = sender as MediaElement;
                    if (mediaElement != null)
                    {
                        mediaElement.Stop();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MediaElement_MediaEnded: {ex.Message}");
            }
        }

        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MessageBox.Show($"Media playback failed: {e.ErrorException.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isPlaying = false;
            UpdatePlaybackState(false);
        }

        private void UpdateTimeDisplay(TimeSpan position, TimeSpan duration)
        {
            if (_window == null) return;
            
            var timeInfo = (_window as IMediaWindow).TimeInfo;
            var timelineSlider = (_window as IMediaWindow).TimelineSlider;
            
            if (timeInfo != null)
            {
                timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
            }
            
            if (timelineSlider != null && !_isDraggingTimelineSlider)
            {
                timelineSlider.Maximum = duration.TotalSeconds;
                timelineSlider.Value = position.TotalSeconds;
            }
            
            // Report position change
            if (_lastReportedPosition == null || Math.Abs((position - _lastReportedPosition.Value).TotalSeconds) >= 1)
            {
                PositionChanged?.Invoke(this, position);
                _lastReportedPosition = position;
            }
        }

        private void InitializeTimelineSlider()
        {
            if (_window == null) return;
            
            var timelineSlider = (_window as IMediaWindow).TimelineSlider;
            if (timelineSlider != null)
            {
                timelineSlider.Minimum = 0;
                timelineSlider.Maximum = _mediaElement.NaturalDuration.TimeSpan.TotalSeconds;
                timelineSlider.Value = 0;
            }
        }

        public void StopPlaybackAndCleanup()
        {
            if (_mediaElement != null)
            {
                _mediaElement.Stop();
                _mediaElement.Source = null;
            }

            if (_window != null)
            {
                var mediaControlsGrid = (_window as IMediaWindow).MediaControlsGrid;
                if (mediaControlsGrid != null)
                {
                    mediaControlsGrid.Visibility = Visibility.Collapsed;
                }

                var nowPlayingText = (_window as IMediaWindow).NowPlayingText;
                if (nowPlayingText != null)
                {
                    nowPlayingText.Text = string.Empty;
                }
            }
            
            // Also hide controls through the controls manager if available
            if (_controlsManager != null)
            {
                _controlsManager.HideMediaControls();
            }
        }

        public void HandleTimelineSliderDragStarted()
        {
            _isDraggingTimelineSlider = true;
        }

        public void HandleTimelineSliderDragCompleted()
        {
            if (_window == null) return;
            
            if (_mediaElement != null && (_window as IMediaWindow).TimelineSlider != null)
            {
                _mediaElement.Position = TimeSpan.FromSeconds((_window as IMediaWindow).TimelineSlider.Value);
            }
            _isDraggingTimelineSlider = false;
        }

        public void HandleTimelineSliderValueChanged(double newValue)
        {
            try
            {
                if (_window == null) return;
                
                if (_mediaElement != null && _mediaElement.Source != null && _isDraggingTimelineSlider)
                {
                    // Update time display while dragging
                    TimeSpan newPosition = TimeSpan.FromSeconds(newValue);
                    TimeSpan duration = _mediaElement.NaturalDuration.TimeSpan;
                    
                    // Pause playback while dragging to make seeking smoother
                    if (_isPlaying)
                    {
                        _mediaElement.Pause();
                    }
                    
                    // Update the time display immediately while dragging
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        (_window as IMediaWindow).TimeInfo.Text = $"{newPosition:mm\\:ss} / {duration:mm\\:ss}";
                    });
                    
                    Debug.WriteLine($"Seeking to position: {newPosition.TotalSeconds:F1}s");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HandleTimelineSliderValueChanged: {ex.Message}");
            }
        }

        public bool IsDraggingSlider => _isDraggingTimelineSlider;

        public void SetControlsManager(MediaControlsManager controlsManager)
        {
            _controlsManager = controlsManager;
        }

        public void UpdateMetadata(string title, string series = null, string season = null, string episodeNumber = null)
        {
            if (_controlsManager != null)
            {
                _controlsManager.UpdateNowPlaying(title, null, series, season);
            }
        }

        public void ShowMediaControls()
        {
            Debug.WriteLine("ShowMediaControls called");
            
            // First try to show controls through the controls manager
            if (_controlsManager != null)
            {
                Debug.WriteLine("ShowMediaControls: Using _controlsManager to show controls");
                _controlsManager.ShowMediaControls();
            }
            
            // Also try to show controls directly through the window
            if (_window != null)
            {
                Debug.WriteLine("ShowMediaControls: Showing controls through _window");
                var mediaControlsGrid = (_window as IMediaWindow).MediaControlsGrid;
                if (mediaControlsGrid != null)
                {
                    Debug.WriteLine("ShowMediaControls: Setting MediaControlsGrid visibility to Visible");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        mediaControlsGrid.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    Debug.WriteLine("ShowMediaControls: MediaControlsGrid is null");
                }
                
                var mediaControlBar = (_window as IMediaWindow).MediaControlBar;
                if (mediaControlBar != null)
                {
                    Debug.WriteLine("ShowMediaControls: Setting MediaControlBar visibility to Visible");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        mediaControlBar.Visibility = Visibility.Visible;
                    });
                }
                else
                {
                    Debug.WriteLine("ShowMediaControls: MediaControlBar is null");
                }
            }
            else
            {
                Debug.WriteLine("ShowMediaControls: _window is null, trying to find main window");
                
                // Try to get the main window as a last resort
                var mainWindow = Application.Current.MainWindow as IMediaWindow;
                if (mainWindow != null)
                {
                    Debug.WriteLine("ShowMediaControls: Found main window");
                    
                    var mediaControlsGrid = mainWindow.MediaControlsGrid;
                    if (mediaControlsGrid != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Setting main window's MediaControlsGrid visibility to Visible");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            mediaControlsGrid.Visibility = Visibility.Visible;
                        });
                    }
                    
                    var mediaControlBar = mainWindow.MediaControlBar;
                    if (mediaControlBar != null)
                    {
                        Debug.WriteLine("ShowMediaControls: Setting main window's MediaControlBar visibility to Visible");
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            mediaControlBar.Visibility = Visibility.Visible;
                        });
                    }
                }
                else
                {
                    Debug.WriteLine("ShowMediaControls: Could not find main window");
                }
            }
            
            // Also try to directly invoke the PlaybackStarted event to trigger the visibility change
            Debug.WriteLine("ShowMediaControls: Invoking PlaybackStarted event");
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void HideMediaControls()
        {
            if (_controlsManager != null)
            {
                _controlsManager.HideMediaControls();
            }
        }

        public void UpdateVolumeControls(double volume, bool isMuted)
        {
            if (_controlsManager != null)
            {
                _controlsManager.UpdateVolumeControls(volume, isMuted);
            }
        }

        public void SyncPlaybackState(bool isPlaying, double volume, bool isMuted)
        {
            _isPlaying = isPlaying;
            _mediaElement.Volume = volume;
            _isMuted = isMuted;
            
            if (_controlsManager != null)
            {
                _controlsManager.UpdatePlayPauseButton(isPlaying);
                _controlsManager.UpdateVolumeControls(volume, isMuted);
            }
        }

        public void UpdateNowPlaying(string title, string artist = null, string series = null, string season = null)
        {
            Debug.WriteLine($"UpdateNowPlaying called with title: {title}, artist: {artist}, series: {series}, season: {season}");
            
            if (_controlsManager != null)
            {
                Debug.WriteLine("UpdateNowPlaying: Using _controlsManager to update now playing info");
                _controlsManager.UpdateNowPlaying(title, artist, series, season);
            }
            else
            {
                Debug.WriteLine("UpdateNowPlaying: _controlsManager is null");
                
                // Try to update the now playing text directly through the window
                if (_window != null && _window.NowPlayingText != null)
                {
                    Debug.WriteLine("UpdateNowPlaying: Updating now playing text directly through _window");
                    
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
                    
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        _window.NowPlayingText.Text = displayText;
                        Debug.WriteLine($"UpdateNowPlaying: Updated now playing text to: {displayText}");
                    });
                }
                else
                {
                    Debug.WriteLine("UpdateNowPlaying: _window or NowPlayingText is null");
                    
                    // Try to get the main window as a last resort
                    var mainWindow = Application.Current.MainWindow as IMediaWindow;
                    if (mainWindow != null && mainWindow.NowPlayingText != null)
                    {
                        Debug.WriteLine("UpdateNowPlaying: Found main window, updating now playing text");
                        
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
                        
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            mainWindow.NowPlayingText.Text = displayText;
                            Debug.WriteLine($"UpdateNowPlaying: Updated main window's now playing text to: {displayText}");
                        });
                    }
                    else
                    {
                        Debug.WriteLine("UpdateNowPlaying: Could not find main window or its NowPlayingText");
                    }
                }
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop any ongoing playback and cleanup
                    StopPlaybackAndCleanup();

                    // Dispose of timer
                    if (_positionTimer != null)
                    {
                        _positionTimer.Stop();
                        _positionTimer = null;
                    }

                    // Clear event handlers
                    if (_mediaElement != null)
                    {
                        _mediaElement.MediaOpened -= MediaElement_MediaOpened;
                        _mediaElement.MediaEnded -= MediaElement_MediaEnded;
                        _mediaElement.MediaFailed -= MediaElement_MediaFailed;
                    }

                    // Clear playlist
                    _playlist.Clear();
                    _playlist = null;
                }

                _disposed = true;
            }
        }

        ~MediaPlayerManager()
        {
            Dispose(false);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MediaPlayerManager));
            }
        }

        public void PlayMedia(Universa.Desktop.Models.Track item)
        {
            if (item == null) return;

            // If we don't have a playlist yet, create one
            if (_playlist == null)
            {
                _playlist = new List<Universa.Desktop.Models.Track>();
            }

            // If the track is already in the playlist, just play from that position
            var existingIndex = _playlist.FindIndex(t => t.Id == item.Id);
            if (existingIndex >= 0)
            {
                _currentTrackIndex = existingIndex;
                PlayCurrentTrack();
                return;
            }

            // If we get here, it's a new track not in our current playlist
            _playlist.Clear();
            _playlist.Add(item);
            _currentTrackIndex = 0;

            // Show media controls
            ShowMediaControls();

            // Start playback
            PlayCurrentTrack();  // Use PlayCurrentTrack instead of Play for consistency

            // Update UI
            UpdateNowPlaying(item.Title, item.Artist, item.Series, item.Season);
        }

        public void PlayTracks(IEnumerable<MusicItem> tracks)
        {
            Debug.WriteLine("PlayTracks called");
            
            if (tracks == null || !tracks.Any())
            {
                Debug.WriteLine("PlayTracks: No tracks provided");
                return;
            }
            
            try
            {
                var tracksList = tracks.ToList();
                var firstTrack = tracksList.First();
                
                Debug.WriteLine($"Playing {tracksList.Count} tracks. First track: {firstTrack.Name} with URL: {firstTrack.StreamUrl}");
                
                // Convert MusicItems to Tracks
                var convertedTracks = tracksList.Select(t => new Universa.Desktop.Models.Track
                {
                    Id = t.Id,
                    Title = t.Name,
                    Artist = t.Artist,
                    Album = t.Album,
                    StreamUrl = t.StreamUrl,
                    CoverArtUrl = t.ImageUrl,
                    Duration = t.Duration,
                    IsVideo = t.StreamUrl != null && (
                        t.StreamUrl.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".avi", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".mov", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".flv", StringComparison.OrdinalIgnoreCase) ||
                        t.StreamUrl.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
                    )
                }).ToList();
                
                // Set the playlist
                Debug.WriteLine($"Setting playlist with {convertedTracks.Count} tracks");
                _playlist = convertedTracks;
                _currentTrackIndex = 0;
                
                // Log playlist state
                LogPlaylistState("PlayTracks - After setting playlist");
                
                // Show media controls
                Debug.WriteLine("PlayTracks: Showing media controls");
                ShowMediaControls();
                
                // Update the now playing information for the first track
                var currentTrack = convertedTracks.First();
                Debug.WriteLine($"Updating now playing info for first track: {currentTrack.Title}");
                UpdateNowPlaying(currentTrack.Title, currentTrack.Artist, currentTrack.Series, currentTrack.Season);
                
                // Start playback
                if (!_isPlaying)
                {
                    Debug.WriteLine("Starting playback");
                    Play();
                }
                else
                {
                    // If already playing, just play the current track
                    Debug.WriteLine("Already playing, playing current track");
                    PlayCurrentTrack();
                }
                
                // Ensure the media control bar is visible by invoking the PlaybackStarted event
                Debug.WriteLine("PlayTracks: Invoking PlaybackStarted event to ensure media control bar is visible");
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                
                // Show media controls again after playback has started
                Debug.WriteLine("PlayTracks: Showing media controls again after playback has started");
                ShowMediaControls();
                
                // Log playlist state again
                LogPlaylistState("PlayTracks - After starting playback");
                
                Debug.WriteLine("PlayTracks completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayTracks: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public void SetMute(bool isMuted)
        {
            if (_isMuted != isMuted)
            {
                if (isMuted)
                {
                    _lastVolume = _mediaElement.Volume;
                    _mediaElement.Volume = 0;
                }
                else
                {
                    _mediaElement.Volume = _lastVolume;
                }
                _isMuted = isMuted;

                if (_controlsManager != null)
                {
                    _controlsManager.UpdateVolumeControls(_mediaElement.Volume, _isMuted);
                }
            }
        }

        public void UpdateNowPlayingLabel(string text)
        {
            if (_window == null) return;
            
            var nowPlayingText = (_window as IMediaWindow).NowPlayingText;
            if (nowPlayingText != null)
            {
                nowPlayingText.Text = text;
                (_window as IMediaWindow).MediaControlsGrid.Visibility = Visibility.Visible;
            }
        }

        public string GetCurrentTrackUrl()
        {
            ThrowIfDisposed();
            if (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
            {
                return _playlist[_currentTrackIndex].StreamUrl;
            }
            return null;
        }

        public void UpdatePlaybackState(bool isPlaying)
        {
            _isPlaying = isPlaying;
            
            if (_controlsManager != null)
            {
                _controlsManager.UpdatePlayPauseButton(isPlaying);
                
                if (_mediaElement.Source == null)
                {
                    _controlsManager.HideMediaControls();
                    TrackChanged?.Invoke(this, null);
                }
                else if (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                {
                    var currentTrack = _playlist[_currentTrackIndex];
                    _controlsManager.UpdateNowPlaying(currentTrack.Title, currentTrack.Artist, currentTrack.Series, currentTrack.Season);
                    _controlsManager.ShowMediaControls();
                    
                    TrackChanged?.Invoke(this, currentTrack);
                }
            }

            // Notify video window if it exists
            if (Application.Current.Windows.OfType<Windows.VideoWindow>().FirstOrDefault() is Windows.VideoWindow videoWindow)
            {
                videoWindow.SyncPlaybackState(isPlaying);
                videoWindow.UpdateVolumeControls(_mediaElement.Volume, _isMuted);
            }
        }

        public void PlayPause()
        {
            if (_isPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void ToggleMute()
        {
            if (_mediaElement == null) return;
            
            _isMuted = !_isMuted;
            if (_isMuted)
            {
                _lastVolume = _mediaElement.Volume;
                _mediaElement.Volume = 0;
            }
            else
            {
                _mediaElement.Volume = _lastVolume;
            }
            
            if (_controlsManager != null)
            {
                _controlsManager.UpdateVolumeControls(_mediaElement.Volume, _isMuted);
            }
        }

        public void SetVolume(double volume)
        {
            if (_mediaElement == null) return;
            
            volume = Math.Max(0, Math.Min(1, volume));
            _mediaElement.Volume = volume;
            _currentVolume = volume;
            
            if (!_isMuted)
            {
                _lastVolume = volume;
            }
            
            SaveVolumeState();
            
            if (_controlsManager != null)
            {
                _controlsManager.UpdateVolumeControls(volume, _isMuted);
            }
        }

        public void ToggleShuffle()
        {
            Debug.WriteLine("ToggleShuffle called");
            
            if (!HasPlaylist)
            {
                Debug.WriteLine("ToggleShuffle: No playlist available");
                return;
            }
            
            try
            {
                _isShuffleEnabled = !_isShuffleEnabled;
                Debug.WriteLine($"ToggleShuffle: Shuffle is now {(_isShuffleEnabled ? "enabled" : "disabled")}");
                
                // Save the current track
                var currentTrack = CurrentTrack;
                
                if (_isShuffleEnabled)
                {
                    // If enabling shuffle, save the original playlist and create a shuffled version
                    if (_originalPlaylist == null)
                    {
                        _originalPlaylist = new List<Universa.Desktop.Models.Track>(_playlist);
                        Debug.WriteLine($"ToggleShuffle: Saved original playlist with {_originalPlaylist.Count} tracks");
                    }
                    
                    // Get all tracks after the current one
                    var remainingTracks = _playlist.Skip(_currentTrackIndex + 1).ToList();
                    Debug.WriteLine($"ToggleShuffle: Found {remainingTracks.Count} remaining tracks to shuffle");
                    
                    // Shuffle the remaining tracks
                    var random = new Random();
                    var shuffledRemainingTracks = remainingTracks.OrderBy(x => random.Next()).ToList();
                    
                    // Create a new playlist with the current track followed by the shuffled remaining tracks
                    var newPlaylist = new List<Universa.Desktop.Models.Track>();
                    
                    // Add all tracks up to and including the current one
                    for (int i = 0; i <= _currentTrackIndex; i++)
                    {
                        newPlaylist.Add(_playlist[i]);
                    }
                    
                    // Add the shuffled remaining tracks
                    newPlaylist.AddRange(shuffledRemainingTracks);
                    
                    // Update the playlist
                    _playlist = newPlaylist;
                    Debug.WriteLine($"ToggleShuffle: Created new shuffled playlist with {_playlist.Count} tracks");
                }
                else
                {
                    // If disabling shuffle, restore the original playlist but keep the current position
                    if (_originalPlaylist != null)
                    {
                        Debug.WriteLine("ToggleShuffle: Restoring original playlist order");
                        
                        // Find the current track in the original playlist
                        int originalIndex = _originalPlaylist.FindIndex(t => t.Id == currentTrack.Id);
                        
                        if (originalIndex >= 0)
                        {
                            Debug.WriteLine($"ToggleShuffle: Found current track at index {originalIndex} in original playlist");
                            _playlist = new List<Universa.Desktop.Models.Track>(_originalPlaylist);
                            _currentTrackIndex = originalIndex;
                        }
                        else
                        {
                            Debug.WriteLine("ToggleShuffle: Could not find current track in original playlist");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("ToggleShuffle: No original playlist available");
                    }
                }
                
                // Notify listeners that shuffle state has changed
                Debug.WriteLine("ToggleShuffle: Invoking ShuffleChanged event");
                ShuffleChanged?.Invoke(this, _isShuffleEnabled);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ToggleShuffle: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        public MediaItem GetCurrentMediaItem()
        {
            if (CurrentTrack == null) return null;
            
            return new MediaItem
            {
                Title = CurrentTrack.Title,
                Artist = CurrentTrack.Artist,
                Album = CurrentTrack.Album,
                Duration = CurrentTrack.Duration,
                StreamUrl = CurrentTrack.StreamUrl,
                Type = MediaType.Audio,
                Series = CurrentTrack.Series,
                Season = CurrentTrack.Season
            };
        }

        public void PauseMedia()
        {
            if (_mediaElement == null) return;
            
            _mediaElement.Pause();
            _isPlaying = false;
            IsPaused = true;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void ResumeMedia()
        {
            if (_mediaElement == null) return;
            
            _mediaElement.Play();
            _isPlaying = true;
            IsPaused = false;
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
        }

        public void StopMedia()
        {
            if (_mediaElement == null) return;
            
            _mediaElement.Stop();
            _mediaElement.Position = TimeSpan.Zero;
            _isPlaying = false;
            IsPaused = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void LoadVolumeState()
        {
            try
            {
                if (_configService?.Provider != null)
                {
                    _currentVolume = _configService.Provider.LastVolume;
                    _lastVolume = _currentVolume;
                    _mediaElement.Volume = _currentVolume;
                    
                    Debug.WriteLine($"Loaded volume state: {_currentVolume}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading volume state: {ex.Message}");
                // Use defaults if loading fails
                _currentVolume = 1.0;
                _lastVolume = 1.0;
                _mediaElement.Volume = 1.0;
            }
        }

        private void SaveVolumeState()
        {
            try
            {
                if (_configService?.Provider != null)
                {
                    _configService.Provider.LastVolume = _currentVolume;
                    Debug.WriteLine($"Saved volume state: {_currentVolume}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving volume state: {ex.Message}");
            }
        }

        private bool EnsureMediaElementInitialized()
        {
            Debug.WriteLine("EnsureMediaElementInitialized called");
            
            try
            {
                // First check if we already have a media element
                if (_mediaElement != null)
                {
                    Debug.WriteLine("EnsureMediaElementInitialized: _mediaElement is already initialized");
                    return true;
                }
                
                // Try to get the media element from the window
                if (_window != null)
                {
                    Debug.WriteLine("EnsureMediaElementInitialized: _window is not null, trying to get MediaPlayer");
                    var mediaElement = _window.MediaPlayer;
                    
                    if (mediaElement != null)
                    {
                        Debug.WriteLine("EnsureMediaElementInitialized: Found MediaPlayer in _window");
                        
                        // We can't assign to the readonly field, but we can use reflection to set it
                        var field = this.GetType().GetField("_mediaElement", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(this, mediaElement);
                            Debug.WriteLine("EnsureMediaElementInitialized: Successfully set _mediaElement using reflection");
                            
                            // Set up event handlers
                            mediaElement.MediaEnded += MediaElement_MediaEnded;
                            mediaElement.MediaOpened += MediaElement_MediaOpened;
                            mediaElement.MediaFailed += MediaElement_MediaFailed;
                            
                            return true;
                        }
                        else
                        {
                            Debug.WriteLine("EnsureMediaElementInitialized: Could not find _mediaElement field using reflection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("EnsureMediaElementInitialized: Window's MediaPlayer is null");
                    }
                }
                
                // If we get here, we couldn't get the media element from the window
                // Try to get it from the main window
                var mainWindow = Application.Current.MainWindow as IMediaWindow;
                if (mainWindow != null)
                {
                    Debug.WriteLine("EnsureMediaElementInitialized: Found main window, trying to get MediaPlayer");
                    var mediaElement = mainWindow.MediaPlayer;
                    
                    if (mediaElement != null)
                    {
                        Debug.WriteLine("EnsureMediaElementInitialized: Found MediaPlayer in main window");
                        
                        // We can't assign to the readonly field, but we can use reflection to set it
                        var field = this.GetType().GetField("_mediaElement", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(this, mediaElement);
                            Debug.WriteLine("EnsureMediaElementInitialized: Successfully set _mediaElement using reflection");
                            
                            // Set up event handlers
                            mediaElement.MediaEnded += MediaElement_MediaEnded;
                            mediaElement.MediaOpened += MediaElement_MediaOpened;
                            mediaElement.MediaFailed += MediaElement_MediaFailed;
                            
                            return true;
                        }
                        else
                        {
                            Debug.WriteLine("EnsureMediaElementInitialized: Could not find _mediaElement field using reflection");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("EnsureMediaElementInitialized: Main window's MediaPlayer is null");
                    }
                }
                else
                {
                    Debug.WriteLine("EnsureMediaElementInitialized: Could not find main window");
                }
                
                // If we get here, we couldn't initialize the media element
                Debug.WriteLine("EnsureMediaElementInitialized: Failed to initialize media element");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in EnsureMediaElementInitialized: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        // Helper method to get the media element
        private MediaElement GetMediaElement()
        {
            try
            {
                Debug.WriteLine("GetMediaElement called");
                
                // First try to use the readonly _mediaElement field
                if (_mediaElement != null)
                {
                    Debug.WriteLine("GetMediaElement: Using existing _mediaElement");
                    return _mediaElement;
                }
                
                // If that's null, try to get it from the window
                if (_window != null && _window.MediaPlayer != null)
                {
                    Debug.WriteLine("GetMediaElement: Using _window.MediaPlayer");
                    
                    // Try to set the _mediaElement field using reflection
                    try
                    {
                        var field = this.GetType().GetField("_mediaElement", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(this, _window.MediaPlayer);
                            Debug.WriteLine("GetMediaElement: Successfully set _mediaElement field using reflection");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GetMediaElement: Error setting _mediaElement field: {ex.Message}");
                    }
                    
                    return _window.MediaPlayer;
                }
                
                // If that's null, try to get it from the main window
                var mainWindow = Application.Current.MainWindow as IMediaWindow;
                if (mainWindow != null && mainWindow.MediaPlayer != null)
                {
                    Debug.WriteLine("GetMediaElement: Using mainWindow.MediaPlayer");
                    
                    // Try to set the _mediaElement field using reflection
                    try
                    {
                        var field = this.GetType().GetField("_mediaElement", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(this, mainWindow.MediaPlayer);
                            Debug.WriteLine("GetMediaElement: Successfully set _mediaElement field using reflection");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GetMediaElement: Error setting _mediaElement field: {ex.Message}");
                    }
                    
                    return mainWindow.MediaPlayer;
                }
                
                // If all else fails, return null
                Debug.WriteLine("GetMediaElement: Could not find a media element");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetMediaElement: {ex.Message}");
                return null;
            }
        }

        // Add a debug method to log playlist state
        private void LogPlaylistState(string context)
        {
            if (_playlist == null)
            {
                Debug.WriteLine($"{context}: _playlist is null");
            }
            else
            {
                Debug.WriteLine($"{context}: _playlist has {_playlist.Count} tracks, current index: {_currentTrackIndex}");
                if (_playlist.Count > 0 && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                {
                    var track = _playlist[_currentTrackIndex];
                    Debug.WriteLine($"{context}: Current track: {track.Title} by {track.Artist}");
                }
                else if (_playlist.Count > 0)
                {
                    Debug.WriteLine($"{context}: Current index {_currentTrackIndex} is out of range (0-{_playlist.Count-1})");
                }
            }
        }

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
} 