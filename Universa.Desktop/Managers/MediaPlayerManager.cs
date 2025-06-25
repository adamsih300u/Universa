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
using Windows.Media;
using Windows.Storage.Streams;
using System.Threading.Tasks;

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
        private DateTime? _lastIsPlayingLogTime;
        private DateTime? _lastHasPlaylistLogTime;
        private bool _isAutoAdvancing = false;
        private bool _wasPlayingBeforeSeek = false;
        private SystemMediaTransportControls _smtc;

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
                if (!_lastIsPlayingLogTime.HasValue || (DateTime.Now - _lastIsPlayingLogTime.Value).TotalMilliseconds > 1000)
                {
                    Debug.WriteLine($"IsPlaying getter called, returning: {_isPlaying}");
                    _lastIsPlayingLogTime = DateTime.Now;
                }
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
                try
                {
                    var playlist = _playlist;
                    bool hasPlaylist = playlist != null && playlist.Count > 0;
                    
                    if (!_lastHasPlaylistLogTime.HasValue || (DateTime.Now - _lastHasPlaylistLogTime.Value).TotalMilliseconds > 1000)
                    {
                        Debug.WriteLine($"HasPlaylist getter called, returning: {hasPlaylist}, playlist count: {playlist?.Count ?? 0}, current index: {_currentTrackIndex}");
                        _lastHasPlaylistLogTime = DateTime.Now;
                    }
                    
                    return hasPlaylist;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in HasPlaylist getter: {ex.Message}");
                    return false;
                }
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
        public bool IsShuffleEnabled 
        { 
            get => _isShuffleEnabled;
            set
            {
                if (_isShuffleEnabled != value)
                {
                    Debug.WriteLine($"IsShuffleEnabled changing from {_isShuffleEnabled} to {value}");
                    _isShuffleEnabled = value;
                    
                    // Update controls manager
                    if (_controlsManager != null)
                    {
                        Debug.WriteLine($"Notifying controls manager of shuffle state change: {_isShuffleEnabled}");
                        _controlsManager.UpdateShuffleButton(_isShuffleEnabled);
                    }
                    
                    // Notify listeners
                    ShuffleChanged?.Invoke(this, _isShuffleEnabled);
                    
                    // Trigger property changed notification
                    OnPropertyChanged(nameof(IsShuffleEnabled));
                }
            }
        }
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
            
            // Initialize VideoWindowManager from dependency injection
            try
            {
                _videoWindowManager = Services.ServiceLocator.Instance.GetRequiredService<VideoWindowManager>();
                Debug.WriteLine("MediaPlayerManager: VideoWindowManager initialized from DI");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MediaPlayerManager: Failed to initialize VideoWindowManager from DI: {ex.Message}");
                // Fallback to creating new instance
                _videoWindowManager = new VideoWindowManager();
                Debug.WriteLine("MediaPlayerManager: Created fallback VideoWindowManager instance");
            }
            
            // Initialize playlist
            _playlist = new List<Universa.Desktop.Models.Track>();
            
            Debug.WriteLine("MediaPlayerManager constructor completed");
            InitializeSmtc();
        }
        
        private void InitializeSmtc()
        {
            try
            {
                _smtc = SystemMediaTransportControls.GetForCurrentView();

                if (_smtc != null)
                {
                    _smtc.IsPlayEnabled = true;
                    _smtc.IsPauseEnabled = true;
                    _smtc.IsNextEnabled = true;
                    _smtc.IsPreviousEnabled = true;
                    // Add other buttons as needed e.g. _smtc.IsStopEnabled = true;

                    _smtc.ButtonPressed += Smtc_ButtonPressed;
                    _smtc.PlaybackStatus = MediaPlaybackStatus.Closed; // Initial status
                    Debug.WriteLine("SMTC Initialized successfully.");
                }
                else
                {
                    Debug.WriteLine("Failed to get SMTC instance.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing SMTC: {ex.Message}");
                // Log or handle the exception appropriately. This can happen if the app doesn't have a window yet
                // or if running in a context where SMTC is not available (e.g. some test runners).
            }
        }

        private async void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            // Ensure execution on the UI thread if necessary for media operations
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Debug.WriteLine($"SMTC Button Pressed: {args.Button}");
                switch (args.Button)
                {
                    case SystemMediaTransportControlsButton.Play:
                        Play();
                        break;
                    case SystemMediaTransportControlsButton.Pause:
                        Pause();
                        break;
                    case SystemMediaTransportControlsButton.Next:
                        Next();
                        break;
                    case SystemMediaTransportControlsButton.Previous:
                        Previous();
                        break;
                    case SystemMediaTransportControlsButton.Stop:
                        Stop();
                        break;
                        // Handle other buttons if enabled
                }
            });
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
            
            // Detach event handlers first to avoid duplicates
            if (_mediaElement != null)
            {
                // Remove event handlers before adding them
                Debug.WriteLine("InitializeWithWindow: Detaching existing media element event handlers");
                _mediaElement.MediaEnded -= OnMediaEnded;
                _mediaElement.MediaOpened -= OnMediaOpened;
                _mediaElement.MediaFailed -= OnMediaFailed;
                
                // Add event handlers
                Debug.WriteLine("InitializeWithWindow: Attaching media element event handlers");
                _mediaElement.MediaEnded += OnMediaEnded;
                _mediaElement.MediaOpened += OnMediaOpened;
                _mediaElement.MediaFailed += OnMediaFailed;
            }

            // If we already have a controls manager, dispose of it
            if (_controlsManager != null)
            {
                Debug.WriteLine("InitializeWithWindow: Clearing existing controls manager");
                // We don't need to do anything specific since it's not disposable
                _controlsManager = null;
            }
            
            // Create the controls manager
            Debug.WriteLine("InitializeWithWindow: Creating new controls manager");
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

            // Stop and restart position timer to ensure it's clean
            if (_positionTimer != null)
            {
                Debug.WriteLine("InitializeWithWindow: Stopping existing position timer");
                _positionTimer.Stop();
                _positionTimer.Tick -= PositionTimer_Tick;
            }
            
            // Initialize position timer
            Debug.WriteLine("InitializeWithWindow: Creating new position timer");
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
            try
            {
                // Handle video playback in video window
                if (_isPlayingVideo && _currentVideoWindow != null)
                {
                    // For video playing in video window, the position updates come from the video window itself
                    // via UpdateVideoPlaybackState method, so we don't need to do anything here
                    // The video window calls UpdateVideoPlaybackState which triggers PositionChanged events
                    return;
                }
                
                // Handle regular audio playback in main MediaElement
                if (_mediaElement != null && _mediaElement.Source != null && !_isDraggingTimelineSlider)
                {
                    // Always update position for smoother UI
                    var currentPosition = _mediaElement.Position;
                    var currentDuration = _mediaElement.NaturalDuration.HasTimeSpan ? _mediaElement.NaturalDuration.TimeSpan : Duration;
                    
                    // Store current position and raise event
                    _lastReportedPosition = currentPosition;
                    PositionChanged?.Invoke(this, currentPosition);
                    
                    // Update time display through controls manager
                    if (_controlsManager != null)
                    {
                        UpdateTimeDisplay(currentPosition, currentDuration);
                    }
                    UpdateSmtcTimelineProperties(currentPosition, currentDuration);
                    
                    // Debug.WriteLine($"Position: {currentPosition.TotalSeconds:F1}s / {currentDuration.TotalSeconds:F1}s");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PositionTimer_Tick: {ex.Message}");
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

        public void PlayCurrentTrack()
        {
            try
            {
                Debug.WriteLine("PlayCurrentTrack called");
                
                if (!HasPlaylist)
                {
                    Debug.WriteLine("PlayCurrentTrack: No playlist available");
                    return;
                }

                if (_playlist == null || _playlist.Count == 0)
                {
                    Debug.WriteLine("PlayCurrentTrack: Playlist is null or empty");
                    return;
                }

                if (_currentTrackIndex < 0 || _currentTrackIndex >= _playlist.Count)
                {
                    Debug.WriteLine($"PlayCurrentTrack: Invalid track index {_currentTrackIndex}");
                    return;
                }

                // Set state before starting playback to ensure UI reflects the correct state
                _isPlaying = true;
                IsPaused = false;
                
                // Notify immediately that IsPlaying changed
                OnPropertyChanged(nameof(IsPlaying));
                
                // Update UI before starting playback
                var currentTrack = _playlist[_currentTrackIndex];
                Debug.WriteLine($"PlayCurrentTrack: Preparing to play track: {currentTrack.Title}");
                
                // Set duration from track metadata if available
                if (currentTrack.Duration.TotalSeconds > 0)
                {
                    _duration = currentTrack.Duration;
                    Debug.WriteLine($"PlayCurrentTrack: Setting duration from track metadata: {_duration}");
                    OnPropertyChanged(nameof(Duration));
                    
                    // Initialize the timeline slider with the track's duration
                    if (_controlsManager != null)
                    {
                        _controlsManager.UpdateTimelineMaximum(_duration.TotalMilliseconds);
                        _controlsManager.UpdateTimeDisplay(TimeSpan.Zero, _duration);
                    }
                }
                
                // Update the UI for current track
                UpdateNowPlaying(currentTrack.Title, currentTrack.Artist, currentTrack.Series, currentTrack.Season);
                
                // Update play/pause button before playback
                _controlsManager?.UpdatePlayPauseButton(true);
                UpdateSmtcMetadataAsync(currentTrack);
                
                // Ensure controls are visible
                _controlsManager?.ShowMediaControls();
                
                // Play the track
                try
                {
                    PlayTrack(currentTrack);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in PlayTrack call: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    
                    // Try to recover - directly set the source and play
                    var mediaElement = GetMediaElement();
                    if (mediaElement != null && !string.IsNullOrEmpty(currentTrack.StreamUrl))
                    {
                        Debug.WriteLine("Attempting direct playback recovery");
                        mediaElement.Source = new Uri(currentTrack.StreamUrl);
                        mediaElement.Play();
                    }
                }

                Debug.WriteLine($"PlayCurrentTrack: Started playing track: {currentTrack.Title}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayCurrentTrack: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void AttachMediaElementEvents(MediaElement mediaElement)
        {
            if (mediaElement == null) return;
            
            // Remove existing handlers to avoid duplicates
            mediaElement.MediaOpened -= MediaElement_MediaOpened;
            mediaElement.MediaEnded -= OnMediaEnded;
            mediaElement.MediaFailed -= MediaElement_MediaFailed;
            
            // Add handlers
            mediaElement.MediaOpened += MediaElement_MediaOpened;
            mediaElement.MediaEnded += OnMediaEnded;
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
                    // Check if we're already playing to avoid redundant calls
                    if (IsPlaying && !IsPaused)
                    {
                        Debug.WriteLine("Already playing, no need to call Play again");
                        return;
                    }
                    
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
                    
                    // Set the playing and paused states
                    IsPlaying = true;
                    IsPaused = false;
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Playing;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(true);
                    
                    Debug.WriteLine($"Media element playback started. IsPlaying={IsPlaying}, IsPaused={IsPaused}");
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
                    
                    // Set the playing and paused states
                    IsPlaying = false;
                    IsPaused = true;
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Paused;
                    
                    // Ensure the button state is updated
                    _controlsManager?.UpdatePlayPauseButton(false);
                    
                    // Raise the PlaybackPaused event instead of stopped
                    // This is important as it won't hide the controls
                    PlaybackPaused?.Invoke(this, EventArgs.Empty);
                    
                    Debug.WriteLine($"Media element paused. IsPlaying={IsPlaying}, IsPaused={IsPaused}");
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
                // Add a small delay to prevent rapid toggling
                System.Threading.Thread.Sleep(100);
                
                // Handle video playback in video window
                if (_isPlayingVideo && _currentVideoWindow != null)
                {
                    Debug.WriteLine("Controlling video playback in video window");
                    if (IsPlaying)
                    {
                        _currentVideoWindow.SyncPlaybackState(false);
                        _isPlaying = false;
                        IsPaused = true;
                        PlaybackPaused?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        _currentVideoWindow.SyncPlaybackState(true);
                        _isPlaying = true;
                        IsPaused = false;
                        PlaybackStarted?.Invoke(this, EventArgs.Empty);
                    }
                    
                    _controlsManager?.UpdatePlayPauseButton(IsPlaying);
                    ShowMediaControls();
                    return;
                }
                
                // Handle regular audio playback
                if (IsPlaying)
                {
                    Debug.WriteLine("Currently playing, will pause");
                    
                    // Set IsPaused before calling Pause to ensure proper state tracking
                    IsPaused = true;
                    
                    // Call Pause which will set IsPlaying to false
                    Pause();
                    
                    // Ensure the controls remain visible
                    ShowMediaControls();
                    
                    Debug.WriteLine($"After pause, IsPlaying={IsPlaying}, IsPaused={IsPaused}");
                }
                else
                {
                    Debug.WriteLine("Currently paused or stopped, will play");
                    
                    // Reset IsPaused state
                    IsPaused = false;
                    
                    // If we have a current track but no media element source, set it
                    var mediaElement = GetMediaElement();
                    if (mediaElement != null && mediaElement.Source == null && HasPlaylist)
                    {
                        var currentTrack = _playlist[_currentTrackIndex];
                        if (currentTrack != null && !string.IsNullOrEmpty(currentTrack.StreamUrl))
                        {
                            mediaElement.Source = new Uri(currentTrack.StreamUrl);
                        }
                    }
                    
                    // Call Play which will set IsPlaying to true
                    Play();
                    
                    // Also raise the TrackChanged event to update the UI
                    if (HasPlaylist && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                    {
                        TrackChanged?.Invoke(this, _playlist[_currentTrackIndex]);
                    }
                    
                    // Ensure controls are visible
                    ShowMediaControls();
                    
                    Debug.WriteLine($"After play, IsPlaying={IsPlaying}, IsPaused={IsPaused}");
                }
                
                // Ensure the button state is updated
                _controlsManager?.UpdatePlayPauseButton(IsPlaying);
                
                Debug.WriteLine($"After toggle, IsPlaying state: {IsPlaying}, IsPaused state: {IsPaused}");
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
                if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
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
                if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                Debug.WriteLine("Media playback stopped");
            }
            else
            {
                Debug.WriteLine("WARNING: mediaElement is null");
            }
        }

        public void Next()
        {
            // Add detailed timestamp to track when this method is called
            Debug.WriteLine($"Next method called at {DateTime.Now.ToString("HH:mm:ss.fff")} (auto-advancing: {_isAutoAdvancing})");
            
            // If we're already auto-advancing, don't do it again
            if (_isAutoAdvancing)
            {
                Debug.WriteLine("Skipping Next() call because we're already auto-advancing");
                return;
            }
            
            // Lock to prevent concurrent execution
            lock (this)
            {
                // Log playlist state before any changes
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
                    
                    // First, stop any current playback
                    var mediaElement = GetMediaElement();
                    if (mediaElement != null)
                    {
                        // Stop before changing track
                        Debug.WriteLine("Stopping media element before advancing to next track");
                        mediaElement.Stop();
                    }
                    
                    // Get the current track for logging
                    string currentTrackTitle = "unknown";
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                    {
                        currentTrackTitle = _playlist[_currentTrackIndex].Title;
                    }
                    
                    // Move to next track
                    int previousIndex = _currentTrackIndex;
                    _currentTrackIndex++;
                    
                    // Loop back to beginning if we've reached the end
                    if (_currentTrackIndex >= _playlist.Count)
                    {
                        Debug.WriteLine("Reached end of playlist, looping back to beginning");
                        _currentTrackIndex = 0;
                    }
                    
                    Debug.WriteLine($"Moving from track {previousIndex} ({currentTrackTitle}) to index: {_currentTrackIndex}");
                    
                    // Verify media element
                    if (mediaElement == null)
                    {
                        Debug.WriteLine("Media element is null, cannot play next track");
                        return;
                    }
                    
                    // Explicitly raise the TrackChanged event before playing the track
                    var nextTrack = _playlist[_currentTrackIndex];
                    if (TrackChanged != null && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                    {
                        Debug.WriteLine($"Explicitly raising TrackChanged event for track: {nextTrack.Title}");
                        TrackChanged(this, nextTrack);
                    }
                    
                    // Set flags for playback
                    _isPlaying = true;
                    IsPaused = false;
                    
                    // Play the next track
                    Debug.WriteLine($"Starting playback of track: {nextTrack.Title}");
                    PlayCurrentTrack();
                    
                    // Ensure media controls are visible
                    _controlsManager?.ShowMediaControls();
                    
                    // Update play/pause button state
                    _controlsManager?.UpdatePlayPauseButton(true);
                    
                    Debug.WriteLine($"Next method completed successfully, now playing track: {nextTrack.Title}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in Next method: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                
                // Log playlist state after changes
                LogPlaylistState("Next - After");
            }
            
            Debug.WriteLine($"Next method completed at {DateTime.Now.ToString("HH:mm:ss.fff")}");
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
                
                // Loop back to end if we've reached the beginning
                if (_currentTrackIndex < 0)
                {
                    Debug.WriteLine("Reached beginning of playlist, looping back to end");
                    _currentTrackIndex = _playlist.Count - 1;
                }

                Debug.WriteLine($"Moving to previous track at index: {_currentTrackIndex}");
                
                // Get the media element
                var mediaElement = GetMediaElement();
                if (mediaElement == null)
                {
                    Debug.WriteLine("Media element is null, cannot play previous track");
                    return;
                }
                
                // Explicitly raise the TrackChanged event before playing the track
                if (TrackChanged != null && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                {
                    var track = _playlist[_currentTrackIndex];
                    Debug.WriteLine($"Explicitly raising TrackChanged event for track: {track.Title}");
                    TrackChanged(this, track);
                }
                
                // Stop any currently playing media first
                mediaElement.Stop();
                
                // Play the selected track
                PlayCurrentTrack();
                
                // Ensure media controls are visible
                _controlsManager?.ShowMediaControls();
                
                // Update play/pause button state
                _controlsManager?.UpdatePlayPauseButton(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in Previous method: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
            // Add precise timestamp for diagnostics
            Debug.WriteLine($"OnMediaEnded called at {DateTime.Now.ToString("HH:mm:ss.fff")}");
            
            try
            {
                // Set the auto-advancing flag to prevent Next() method from being called during auto-advance
                _isAutoAdvancing = true;
                
                // First check if we have a playlist
                if (!HasPlaylist)
                {
                    Debug.WriteLine("No playlist available for OnMediaEnded");
                    // Even if we don't have a playlist, we should update the UI to reflect that playback has ended
                    IsPlaying = false;
                    UpdatePlaybackState(false);
                    _isAutoAdvancing = false;
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    return;
                }
                
                Debug.WriteLine($"Current playlist has {_playlist.Count} tracks, current index: {_currentTrackIndex}");
                
                // Safety check for playlist and index bounds
                if (_playlist == null || _playlist.Count == 0)
                {
                    Debug.WriteLine("OnMediaEnded: playlist is null or empty");
                    _isAutoAdvancing = false;
                    if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                    return;
                }
                
                if (_currentTrackIndex < 0 || _currentTrackIndex >= _playlist.Count)
                {
                    Debug.WriteLine($"OnMediaEnded: Invalid track index {_currentTrackIndex}, resetting to 0");
                    _currentTrackIndex = 0;
                }
                
                // Get current track for logging
                var currentTrack = _playlist[_currentTrackIndex];
                Debug.WriteLine($"OnMediaEnded: Finished playing track: {currentTrack.Title}");
                
                // Log playlist state before advancing
                LogPlaylistState("OnMediaEnded - Before Next");
                
                try
                {
                    lock (this) // Lock to prevent concurrent modifications to the track index
                    {
                        // Move to the next track
                        Debug.WriteLine($"OnMediaEnded: Moving to next track from index {_currentTrackIndex}");
                        int oldIndex = _currentTrackIndex;
                        
                        // Manually increment track index instead of calling Next()
                        _currentTrackIndex++;
                        
                        // Loop back to beginning if needed
                        if (_currentTrackIndex >= _playlist.Count)
                        {
                            Debug.WriteLine("OnMediaEnded: Reached end of playlist, looping back to beginning");
                            _currentTrackIndex = 0;
                        }
                        
                        Debug.WriteLine($"OnMediaEnded: Track index changed from {oldIndex} to {_currentTrackIndex}");
                    }
                    
                    // Use the dispatcher to start playing the next track on the UI thread
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => 
                    {
                        try 
                        {
                            Debug.WriteLine($"OnMediaEnded: Dispatcher callback executing at {DateTime.Now.ToString("HH:mm:ss.fff")}");
                            
                            // Get a fresh media element
                            var mediaElement = GetMediaElement();
                            if (mediaElement == null)
                            {
                                Debug.WriteLine("OnMediaEnded: Media element is null");
                                _isAutoAdvancing = false;
                                if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
                                return;
                            }
                            
                            // Stop any existing media (this helps prevent issues)
                            mediaElement.Stop();
                            
                            // Explicitly raise the TrackChanged event
                            if (TrackChanged != null && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                            {
                                var nextTrack = _playlist[_currentTrackIndex];
                                Debug.WriteLine($"OnMediaEnded: Raising TrackChanged event for track: {nextTrack.Title}");
                                TrackChanged(this, nextTrack);
                            }
                            
                            // Set state to playing
                            _isPlaying = true;
                            IsPaused = false;
                            OnPropertyChanged(nameof(IsPlaying));
                            
                            // Play the track directly using a local variable to track
                            if (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                            {
                                var nextTrack = _playlist[_currentTrackIndex];
                                Debug.WriteLine($"OnMediaEnded: Starting playback of track: {nextTrack.Title}");
                                
                                // Play the track
                                PlayCurrentTrack();
                            }
                            else
                            {
                                Debug.WriteLine($"OnMediaEnded: Invalid track index {_currentTrackIndex}");
                            }
                            
                            // Ensure media controls are visible
                            _controlsManager?.ShowMediaControls();
                            
                            // Update play/pause button state
                            _controlsManager?.UpdatePlayPauseButton(true);
                            
                            Debug.WriteLine($"OnMediaEnded: Auto-advancement complete at {DateTime.Now.ToString("HH:mm:ss.fff")}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error in OnMediaEnded dispatcher callback: {ex.Message}");
                            Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                        }
                        finally
                        {
                            // Always reset the auto-advancing flag
                            _isAutoAdvancing = false;
                            Debug.WriteLine("OnMediaEnded: Reset _isAutoAdvancing to false");
                        }
                    }), DispatcherPriority.Background, null);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in OnMediaEnded before dispatcher: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    _isAutoAdvancing = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMediaEnded: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                _isAutoAdvancing = false;
            }
            
            Debug.WriteLine($"OnMediaEnded method completed at {DateTime.Now.ToString("HH:mm:ss.fff")}");
        }

        private void OnMediaOpened(object sender, EventArgs e)
        {
            Debug.WriteLine("OnMediaOpened event fired");
            
            try
            {
                if (_mediaElement != null && _mediaElement.NaturalDuration.HasTimeSpan)
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

                    // Get the duration from the media element
                    TimeSpan mediaDuration = _mediaElement.NaturalDuration.TimeSpan;
                    
                    // If media element reports very short duration but we have track metadata with duration
                    if (mediaDuration.TotalSeconds < 1 && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist?.Count)
                    {
                        var currentTrack = _playlist[_currentTrackIndex];
                        if (currentTrack != null && currentTrack.Duration.TotalSeconds > 0)
                        {
                            mediaDuration = currentTrack.Duration;
                            Debug.WriteLine($"OnMediaOpened: Using duration from track metadata: {mediaDuration}");
                        }
                    }
                    
                    // Update the time display
                    UpdateTimeDisplay(TimeSpan.Zero, mediaDuration);
                    
                    // Store the duration
                    _duration = mediaDuration;
                    OnPropertyChanged(nameof(Duration));
                    Debug.WriteLine($"Media duration set to: {_duration}");
                }
                else
                {
                    Debug.WriteLine("OnMediaOpened: MediaElement has no timespan duration");
                    
                    // Try to get duration from track metadata if available
                    if (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist?.Count)
                    {
                        var currentTrack = _playlist[_currentTrackIndex];
                        if (currentTrack != null && currentTrack.Duration.TotalSeconds > 0)
                        {
                            _duration = currentTrack.Duration;
                            OnPropertyChanged(nameof(Duration));
                            Debug.WriteLine($"OnMediaOpened: Set duration from track metadata: {_duration}");
                            
                            // Update the time display
                            UpdateTimeDisplay(TimeSpan.Zero, _duration);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in OnMediaOpened: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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
                    
                    // Get the duration from the media element
                    TimeSpan mediaDuration = mediaElement.NaturalDuration.HasTimeSpan 
                        ? mediaElement.NaturalDuration.TimeSpan 
                        : TimeSpan.Zero;
                        
                    Debug.WriteLine($"MediaElement_MediaOpened: Media duration from MediaElement: {mediaDuration}");
                    
                    // If media element reports zero duration but we have track metadata with duration
                    if ((mediaDuration == TimeSpan.Zero || mediaDuration.TotalSeconds < 1) && 
                        _currentTrackIndex >= 0 && _currentTrackIndex < _playlist?.Count)
                    {
                        var currentTrack = _playlist[_currentTrackIndex];
                        if (currentTrack != null && currentTrack.Duration.TotalSeconds > 0)
                        {
                            mediaDuration = currentTrack.Duration;
                            Debug.WriteLine($"MediaElement_MediaOpened: Using duration from track metadata: {mediaDuration}");
                        }
                    }
                    
                    // Update duration
                    _duration = mediaDuration;
                    OnPropertyChanged(nameof(Duration));
                    Debug.WriteLine($"Media duration set to: {_duration}");
                    
                    // Update the timeline and time display
                    if (_controlsManager != null)
                    {
                        _controlsManager.UpdateTimelineMaximum(_duration.TotalMilliseconds);
                        _controlsManager.UpdateTimeDisplay(TimeSpan.Zero, _duration);
                    }
                    
                    // Show media controls
                    _controlsManager?.ShowMediaControls();
                    
                    // Notify that playback has started
                    PlaybackStarted?.Invoke(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MediaElement_MediaOpened: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void MediaElement_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            MessageBox.Show($"Media playback failed: {e.ErrorException.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _isPlaying = false;
            UpdatePlaybackState(false);
            if (_smtc != null) _smtc.PlaybackStatus = MediaPlaybackStatus.Stopped;
        }

        private void UpdateTimeDisplay(TimeSpan position, TimeSpan duration)
        {
            if (_window == null) return;
            
            try
            {
                // Prefer using the controls manager if available
                if (_controlsManager != null)
                {
                    // Let the controls manager handle the update
                    _controlsManager.UpdateTimeDisplay();
                    return;
                }
                
                // Fall back to direct UI manipulation if controls manager is not available
                var timeInfo = (_window as IMediaWindow).TimeInfo;
                var timelineSlider = (_window as IMediaWindow).TimelineSlider;
                
                if (timeInfo != null)
                {
                    Application.Current.Dispatcher.InvokeAsync(() => 
                    {
                        timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
                    });
                }
                
                if (timelineSlider != null && !_isDraggingTimelineSlider)
                {
                    Application.Current.Dispatcher.InvokeAsync(() => 
                    {
                        timelineSlider.Maximum = duration.TotalSeconds;
                        timelineSlider.Value = position.TotalSeconds;
                    });
                }
                
                // Report position change
                if (_lastReportedPosition == null || Math.Abs((position - _lastReportedPosition.Value).TotalSeconds) >= 1)
                {
                    PositionChanged?.Invoke(this, position);
                    _lastReportedPosition = position;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateTimeDisplay: {ex.Message}");
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
            _wasPlayingBeforeSeek = this.IsPlaying;
            if (_wasPlayingBeforeSeek)
            {
                this.Pause();
            }
        }

        public void HandleTimelineSliderDragCompleted()
        {
            if (_window == null) return;
            
            if (_mediaElement != null && (_window as IMediaWindow).TimelineSlider != null)
            {
                _mediaElement.Position = TimeSpan.FromSeconds((_window as IMediaWindow).TimelineSlider.Value);
            }
            _isDraggingTimelineSlider = false;

            if (_wasPlayingBeforeSeek)
            {
                this.Play();
            }
            _wasPlayingBeforeSeek = false;
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
            Debug.WriteLine("SetControlsManager called");
            
            if (controlsManager == null)
            {
                Debug.WriteLine("SetControlsManager: controlsManager is null");
                return;
            }
            
            _controlsManager = controlsManager;
            Debug.WriteLine("SetControlsManager: Controls manager set successfully");
            
            // Sync the initial state
            if (_controlsManager != null)
            {
                // Hide controls by default
                _controlsManager.HideMediaControls();
                
                // Only update UI state if we have media playing or paused
                if (IsPlaying || IsPaused)
                {
                    _controlsManager.UpdatePlayPauseButton(IsPlaying);
                    _controlsManager.UpdateVolumeControls(_currentVolume, _isMuted);
                    _controlsManager.UpdateShuffleButton(_isShuffleEnabled);
                    
                    if (HasPlaylist && _currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count)
                    {
                        var currentTrack = _playlist[_currentTrackIndex];
                        _controlsManager.UpdateNowPlaying(currentTrack.Title, currentTrack.Artist, currentTrack.Series, currentTrack.Season);
                    }
                    
                    _controlsManager.ShowMediaControls();
                }
            }
        }

        public MediaControlsManager GetControlsManager()
        {
            return _controlsManager;
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
            Debug.WriteLine("\n========== SHOW MEDIA CONTROLS ==========");
            Debug.WriteLine("ShowMediaControls: Running on UI thread");

            // Only show controls if we have media playing or paused
            if (!IsPlaying && !IsPaused)
            {
                Debug.WriteLine("ShowMediaControls: No media playing or paused, hiding controls");
                HideMediaControls();
                return;
            }

            // Use the controls manager to show controls
            if (_controlsManager != null)
            {
                Debug.WriteLine("ShowMediaControls: Using controls manager to show controls");
                _controlsManager.ShowMediaControls();
            }
            else
            {
                Debug.WriteLine("ShowMediaControls: No controls manager available");
            }

            Debug.WriteLine("ShowMediaControls: Completed setting visibility");
            Debug.WriteLine("========== SHOW MEDIA CONTROLS END ==========\n");
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
                        _mediaElement.MediaEnded -= OnMediaEnded;
                        _mediaElement.MediaFailed -= MediaElement_MediaFailed;
                    }

                    // Clear playlist
                    _playlist.Clear();
                    _playlist = null;

                    if (_smtc != null)
                    {
                        _smtc.ButtonPressed -= Smtc_ButtonPressed;
                        // Clear display and update
                        if (_smtc.DisplayUpdater != null)
                        {
                            _smtc.DisplayUpdater.ClearAll();
                            _smtc.DisplayUpdater.Update();
                        }
                         _smtc.PlaybackStatus = MediaPlaybackStatus.Closed;
                    }
                }

                _disposed = true;
                Debug.WriteLine("MediaPlayerManager disposed.");
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
            
            Debug.WriteLine($"PlayMedia called for track: {item.Title}");

            try
            {
                // If we don't have a playlist yet, create one
                if (_playlist == null)
                {
                    _playlist = new List<Universa.Desktop.Models.Track>();
                }

                // If the track is already in the playlist, just play from that position
                var existingIndex = _playlist.FindIndex(t => t.Id == item.Id);
                if (existingIndex >= 0)
                {
                    Debug.WriteLine($"Track already in playlist at index {existingIndex}, playing from there");
                    _currentTrackIndex = existingIndex;
                    PlayCurrentTrack();
                    return;
                }

                // If we get here, it's a new track not in our current playlist
                Debug.WriteLine("Adding new track to playlist");
                _playlist.Clear();
                _playlist.Add(item);
                _currentTrackIndex = 0;

                // Show media controls
                ShowMediaControls();

                // Start playback
                PlayCurrentTrack();  // Use PlayCurrentTrack instead of Play for consistency

                // Update UI
                UpdateNowPlaying(item.Title, item.Artist, item.Series, item.Season);
                
                // Ensure the TrackChanged event is raised
                TrackChanged?.Invoke(this, item);
                
                // Ensure the PlaybackStarted event is raised
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                
                Debug.WriteLine("PlayMedia completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayMedia: {ex.Message}");
            }
        }

        public void PlayTracksWithShuffle(IEnumerable<MusicItem> musicItems, bool shuffle = false)
        {
            try
            {
                Debug.WriteLine($"PlayTracksWithShuffle called with {musicItems?.Count() ?? 0} tracks, shuffle={shuffle}");
                
                if (musicItems == null || !musicItems.Any())
                {
                    Debug.WriteLine("PlayTracksWithShuffle: No tracks provided");
                    return;
                }

                // Convert MusicItems to Tracks - Note: MusicItem uses 'Name' not 'Title'
                var tracks = musicItems.Select(item => new Models.Track
                {
                    Id = item.Id,
                    Title = item.Name, // Use Name property since MusicItem doesn't have Title
                    Artist = item.Artist,
                    Album = item.Album,
                    StreamUrl = item.StreamUrl, // Use StreamUrl instead of FilePath
                    Duration = item.Duration
                }).ToList();

                // Create a defensive copy to prevent reference issues
                var tracksCopy = new List<Models.Track>(tracks);
                
                // Set shuffle state using the property setter for proper notification
                IsShuffleEnabled = shuffle;
                
                if (shuffle)
                {
                    Debug.WriteLine("PlayTracksWithShuffle: Creating shuffled playlist");
                    
                    // Save original playlist order for potential later use
                    _originalPlaylist = new List<Models.Track>(tracksCopy);
                    
                    // Create a shuffled copy
                    var random = new Random();
                    _playlist = tracksCopy.OrderBy(x => random.Next()).ToList();
                    
                    // Set current track index to 0 to start with the first (random) track
                    _currentTrackIndex = 0;
                }
                else
                {
                    Debug.WriteLine("PlayTracksWithShuffle: Using ordered playlist");
                    
                    // Use the original order
                    _playlist = tracksCopy;
                    _currentTrackIndex = 0;
                    _originalPlaylist = null; // Clear original playlist since we're not shuffling
                }
                
                Debug.WriteLine($"PlayTracksWithShuffle: Playlist created with {_playlist.Count} tracks");
                OnPropertyChanged(nameof(HasPlaylist));
                
                // Start playing the first track in the playlist
                if (_playlist.Count > 0)
                {
                    PlayCurrentTrack();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayTracksWithShuffle: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }
        
        // Fix PlayTracks to utilize the new PlayTracksWithShuffle method with shuffle disabled
        public void PlayTracks(IEnumerable<MusicItem> tracks)
        {
            PlayTracksWithShuffle(tracks, false);
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
                // Toggle the shuffle state using the property setter
                IsShuffleEnabled = !IsShuffleEnabled;
                
                // Save the current track to maintain position in playlist
                var currentTrack = CurrentTrack;
                
                if (IsShuffleEnabled)
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
                        int originalIndex = _originalPlaylist.FindIndex(t => t.Id == currentTrack?.Id);
                        
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
            
            Debug.WriteLine("PauseMedia called");
            
            _mediaElement.Pause();
            _isPlaying = false;
            IsPaused = true;
            
            // Update the play/pause button
            _controlsManager?.UpdatePlayPauseButton(false);
            
            // Raise the PlaybackPaused event instead of stopped
            PlaybackPaused?.Invoke(this, EventArgs.Empty);
            
            Debug.WriteLine("Media paused");
        }

        public void ResumeMedia()
        {
            if (_mediaElement == null) return;
            
            Debug.WriteLine("ResumeMedia called");
            
            _mediaElement.Play();
            _isPlaying = true;
            IsPaused = false;
            
            // Update the play/pause button
            _controlsManager?.UpdatePlayPauseButton(true);
            
            // Raise the PlaybackStarted event
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
            
            Debug.WriteLine("Media resumed");
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
                            mediaElement.MediaEnded += OnMediaEnded;
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
                            mediaElement.MediaEnded += OnMediaEnded;
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
                    
                    // Make sure events are attached
                    AttachMediaElementEvents(_mediaElement);
                    
                    return _mediaElement;
                }
                
                // If that's null, try to get it from the window
                if (_window != null && _window.MediaPlayer != null)
                {
                    Debug.WriteLine("GetMediaElement: Using _window.MediaPlayer");
                    
                    // Make sure events are attached before returning
                    var mediaElement = _window.MediaPlayer;
                    AttachMediaElementEvents(mediaElement);
                    
                    // Try to set the _mediaElement field using reflection
                    try
                    {
                        var field = this.GetType().GetField("_mediaElement", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (field != null)
                        {
                            field.SetValue(this, mediaElement);
                            Debug.WriteLine("GetMediaElement: Successfully set _mediaElement using reflection");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"GetMediaElement: Error setting _mediaElement field: {ex.Message}");
                    }
                    
                    return mediaElement;
                }
                
                // As a last resort, try to get it from the main window
                var mainWindow = Application.Current.MainWindow as IMediaWindow;
                if (mainWindow != null && mainWindow.MediaPlayer != null)
                {
                    Debug.WriteLine("GetMediaElement: Using Application.Current.MainWindow.MediaPlayer");
                    
                    var mediaElement = mainWindow.MediaPlayer;
                    
                    // Make sure events are attached
                    AttachMediaElementEvents(mediaElement);
                    
                    return mediaElement;
                }
                
                Debug.WriteLine("GetMediaElement: Could not find a media element");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetMediaElement: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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

        private void PlayVideo(Universa.Desktop.Models.Track track)
        {
            Debug.WriteLine($"PlayVideo called for track: {track.Title}");
            
            try
            {
                // Ensure the position timer is initialized for video playback tracking
                if (_positionTimer == null)
                {
                    _positionTimer = new DispatcherTimer();
                    _positionTimer.Interval = TimeSpan.FromMilliseconds(500);
                    _positionTimer.Tick += PositionTimer_Tick;
                    Debug.WriteLine("MediaPlayerManager.PlayVideo: _positionTimer was null, created and configured.");
                }

                if (!_positionTimer.IsEnabled)
                {
                    _positionTimer.Start();
                    Debug.WriteLine("MediaPlayerManager.PlayVideo: Position timer started.");
                }
                
                // Set video playing flags
                _isPlayingVideo = true;
                IsVideoPlaying = true;
                _isPlaying = true;
                IsPaused = false;
                
                // Update the now playing information
                UpdateNowPlaying(track.Title, track.Artist, track.Series, track.Season);
                
                // Show the video window ONLY - don't play in main MediaElement to avoid dual playback
                if (_videoWindowManager != null)
                {
                    Debug.WriteLine("Showing video window (avoiding dual playback with main MediaElement)");
                    
                    // Create a better title for TV episodes
                    string windowTitle = track.Title;
                    if (track.IsVideo && !string.IsNullOrEmpty(track.Series))
                    {
                        if (!string.IsNullOrEmpty(track.Season))
                        {
                            windowTitle = $"{track.Series} ({track.Season}) - {track.Title}";
                        }
                        else
                        {
                            windowTitle = $"{track.Series} - {track.Title}";
                        }
                    }
                    
                    _videoWindowManager.ShowVideoWindow(new Uri(track.StreamUrl), windowTitle);
                    
                    // Store reference to the current video window for control
                    _currentVideoWindow = _videoWindowManager.CurrentVideoWindow;
                    
                    // Set duration from track if available
                    if (track.Duration.TotalSeconds > 0)
                    {
                        _duration = track.Duration;
                        OnPropertyChanged(nameof(Duration));
                    }
                }
                else
                {
                    Debug.WriteLine("Video window manager is null, falling back to media element");
                    // Fallback: play in main MediaElement if no video window manager
                    var mediaElement = GetMediaElement();
                    if (mediaElement != null)
                    {
                        mediaElement.Source = new Uri(track.StreamUrl);
                        mediaElement.Play();
                    }
                }
                
                // Notify listeners that playback has started
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                
                // Notify listeners that the track has changed
                TrackChanged?.Invoke(this, track);
                
                Debug.WriteLine($"PlayVideo completed for track: {track.Title}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayVideo: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void PlayTrack(Models.Track track)
        {
            try
            {
                Debug.WriteLine($"PlayTrack called for track: {track.Title}");
                
                if (track == null)
                {
                    Debug.WriteLine("PlayTrack: Track is null");
                    return;
                }
                
                // Show media controls immediately
                _controlsManager?.ShowMediaControls();
                
                // Initialize media element if needed
                var mediaElement = GetMediaElement();
                if (mediaElement == null)
                {
                    Debug.WriteLine("PlayTrack: Media element is null, cannot play track");
                    return;
                }
                
                // Handle video tracks
                if (track.IsVideo)
                {
                    _isPlayingVideo = true;
                    PlayVideo(track);
                    return;
                }
                else
                {
                    _isPlayingVideo = false;
                }
                
                // Ensure the position timer is initialized and running
                if (_positionTimer == null)
                {
                    _positionTimer = new DispatcherTimer();
                    _positionTimer.Interval = TimeSpan.FromMilliseconds(500); // Or your desired interval
                    _positionTimer.Tick += PositionTimer_Tick;
                    Debug.WriteLine("MediaPlayerManager.PlayTrack: _positionTimer was null, created and configured.");
                }

                if (!_positionTimer.IsEnabled)
                {
                    _positionTimer.Start();
                    Debug.WriteLine("MediaPlayerManager.PlayTrack: Position timer started.");
                }
                
                // Set the track source
                try
                {
                    // Set the source and start playing
                    if (!string.IsNullOrEmpty(track.StreamUrl))
                    {
                        mediaElement.Source = new Uri(track.StreamUrl);
                        Debug.WriteLine($"PlayTrack: Set source to StreamUrl: {track.StreamUrl}");
                    }
                    else
                    {
                        Debug.WriteLine("PlayTrack: Track has no valid source URL");
                        return;
                    }
                    
                    // Start playback
                    mediaElement.Play();
                    
                    // Notify listeners that playback has started
                    PlaybackStarted?.Invoke(this, EventArgs.Empty);
                    
                    // Notify listeners that the track has changed
                    TrackChanged?.Invoke(this, track);
                    
                    Debug.WriteLine($"PlayTrack: Playback started for track: {track.Title}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting track source: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    _isPlaying = false;
                    IsPlaying = false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in PlayTrack: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private async Task UpdateSmtcMetadataAsync(Universa.Desktop.Models.Track track)
        {
            if (_smtc == null || track == null)
            {
                Debug.WriteLine("SMTC or Track is null, cannot update metadata.");
                return;
            }

            try
            {
                var updater = _smtc.DisplayUpdater;
                updater.Type = MediaPlaybackType.Music; // Assuming music, adjust if video support needed here
                
                updater.MusicProperties.Title = track.Title ?? string.Empty;
                updater.MusicProperties.Artist = track.Artist ?? string.Empty;
                updater.MusicProperties.AlbumTitle = track.Album ?? string.Empty;
                // updater.MusicProperties.AlbumArtist = track.AlbumArtist ?? string.Empty; // If you have AlbumArtist
                // updater.MusicProperties.TrackNumber = (uint)(track.TrackNumber > 0 ? track.TrackNumber : 0); // If you have TrackNumber

                // Placeholder for album art - this needs a valid URI or stream
                // For example, if track.AlbumArtUri is a valid URI string to an image:
                // if (!string.IsNullOrEmpty(track.AlbumArtUri))
                // {
                //     try
                //     {
                //         updater.Thumbnail = RandomAccessStreamReference.CreateFromUri(new Uri(track.AlbumArtUri));
                //     }
                //     catch (Exception ex)
                //     {
                //         Debug.WriteLine($"Error setting SMTC thumbnail from URI: {track.AlbumArtUri} - {ex.Message}");
                //     }
                // }
                // else
                // {
                //      updater.Thumbnail = null; // Or a default image
                // }


                updater.Update(); // Corrected: Removed await and changed to Update()
                Debug.WriteLine($"SMTC Metadata Updated for: {track.Title}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating SMTC metadata: {ex.Message}");
            }
        }

        private void UpdateSmtcTimelineProperties(TimeSpan position, TimeSpan duration)
        {
            if (_smtc == null) return;

            try
            {
                var timelineProperties = new SystemMediaTransportControlsTimelineProperties();
                timelineProperties.StartTime = TimeSpan.Zero;
                timelineProperties.EndTime = duration;
                timelineProperties.Position = position;
                timelineProperties.MinSeekTime = TimeSpan.Zero; // Or actual min seek time if applicable
                timelineProperties.MaxSeekTime = duration;   // Or actual max seek time if applicable

                _smtc.UpdateTimelineProperties(timelineProperties);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating SMTC timeline properties: {ex.Message}");
            }
        }

        public void UpdateVideoPlaybackState(TimeSpan position, TimeSpan duration)
        {
            // Update internal state for video playback
            _duration = duration;
            
            // Update the controls manager with video position/duration
            if (_controlsManager != null)
            {
                _controlsManager.UpdateTimeDisplay(position, duration);
                _controlsManager.UpdateTimelineSlider(position, duration);
            }
            
            // Raise position changed event for any listeners
            PositionChanged?.Invoke(this, position);
            
            Debug.WriteLine($"UpdateVideoPlaybackState: Position={position:mm\\:ss}, Duration={duration:mm\\:ss}");
        }
    }
} 