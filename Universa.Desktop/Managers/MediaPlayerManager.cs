using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Windows.Controls.Primitives;
using Universa.Desktop.Models;
using Universa.Desktop.Windows;
using Universa.Desktop.Views;
using Universa.Desktop.Core.Configuration;

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

    public class MediaPlayerManager : IDisposable
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

        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackStopped;
        public event EventHandler<Universa.Desktop.Models.Track> TrackChanged;
        public event EventHandler<TimeSpan> PositionChanged;

        public bool IsPlaying => _isPlaying;
        public bool HasPlaylist => _playlist != null && _playlist.Any();
        public Universa.Desktop.Models.Track CurrentTrack => HasPlaylist ? _playlist[_currentTrackIndex] : null;
        public bool IsPaused 
        { 
            get => _isPaused;
            private set => _isPaused = value;
        }
        public TimeSpan CurrentPosition => _mediaElement?.Position ?? TimeSpan.Zero;
        public TimeSpan Duration => _mediaElement?.NaturalDuration.HasTimeSpan == true ? 
            _mediaElement.NaturalDuration.TimeSpan : TimeSpan.Zero;
        public bool IsShuffleEnabled => _isShuffle;

        public MediaPlayerManager(IMediaWindow window, MediaElement mediaElement, IConfigurationService configService)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _mediaElement = mediaElement ?? throw new ArgumentNullException(nameof(mediaElement));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
            
            _mediaElement.MediaEnded += OnMediaEnded;
            _mediaElement.MediaOpened += OnMediaOpened;
            _mediaElement.MediaFailed += OnMediaFailed;

            _controlsManager = new MediaControlsManager(
                window as BaseMainWindow,
                this,
                (_window as IMediaWindow).MediaControlsGrid,
                (_window as IMediaWindow).PlayPauseButton,
                (_window as IMediaWindow).StopButton,
                (_window as IMediaWindow).PreviousButton,
                (_window as IMediaWindow).NextButton,
                (_window as IMediaWindow).MuteButton,
                (_window as IMediaWindow).VolumeSlider,
                (_window as IMediaWindow).TimeInfo,
                (_window as IMediaWindow).TimelineSlider,
                (_window as IMediaWindow).NowPlayingText,
                (_window as IMediaWindow).MediaControlsGrid
            );

            InitializeMediaPlayer();
        }

        private void InitializeMediaPlayer()
        {
            _mediaElement.LoadedBehavior = MediaState.Manual;
            _mediaElement.UnloadedBehavior = MediaState.Manual;
            _mediaElement.MediaOpened += MediaPlayer_MediaOpened;
            _mediaElement.MediaEnded += MediaPlayer_MediaEnded;
            _mediaElement.MediaFailed += MediaPlayer_MediaFailed;

            InitializePositionTimer();
            LoadVolumeState();
        }

        private void InitializePositionTimer()
        {
            _positionTimer = new DispatcherTimer();
            _positionTimer.Interval = TimeSpan.FromMilliseconds(250);
            _positionTimer.Tick += (s, e) =>
            {
                if (_mediaElement.Source != null && !_isDraggingTimelineSlider)
                {
                    TimeSpan position = _mediaElement.Position;
                    
                    if (_lastReportedPosition == null || Math.Abs((position - _lastReportedPosition.Value).TotalSeconds) > 0.1)
                    {
                        PositionChanged?.Invoke(this, position);
                        _lastReportedPosition = position;
                    }
                    
                    if (_mediaElement.NaturalDuration.HasTimeSpan)
                    {
                        UpdateTimeDisplay(position, _mediaElement.NaturalDuration.TimeSpan);
                    }
                    else
                    {
                        UpdateTimeDisplay(position, TimeSpan.Zero);
                    }
                }
            };
        }

        public void SetPlaylist(IEnumerable<Universa.Desktop.Models.Track> tracks, bool shuffle = false)
        {
            if (tracks == null) throw new ArgumentNullException(nameof(tracks));
            
            _playlist = tracks.ToList();
            _currentTrackIndex = 0;
            _isShuffle = shuffle;

            if (_isShuffle)
            {
                ShufflePlaylist();
            }

            if (_playlist.Any())
            {
                PlayCurrentTrack();
            }
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
            if (!HasPlaylist) return;

            var track = CurrentTrack;
            if (track != null)
            {
                _mediaElement.Source = new Uri(track.StreamUrl);
                _mediaElement.Play();
                _isPlaying = true;
                PlaybackStarted?.Invoke(this, EventArgs.Empty);
                TrackChanged?.Invoke(this, track);
                UpdateNowPlaying(track.Title, track.Artist, track.Series, track.Season);

                // For video content, open the video window
                if (track.IsVideo)
                {
                    var videoWindow = new VideoPlayerWindow(track.StreamUrl, track.Title);
                    videoWindow.Owner = Application.Current.MainWindow;
                    videoWindow.Show();
                }
            }
        }

        public void Play()
        {
            if (!HasPlaylist) return;

            var track = _playlist[_currentTrackIndex];
            _mediaElement.Source = new Uri(track.StreamUrl);
            _mediaElement.Play();
            _isPlaying = true;
            
            PlaybackStarted?.Invoke(this, EventArgs.Empty);
            TrackChanged?.Invoke(this, track);
        }

        public void Pause()
        {
            _mediaElement.Pause();
            _isPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            _mediaElement.Stop();
            _isPlaying = false;
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        public void Next()
        {
            if (!HasPlaylist) return;

            if (_currentTrackIndex < _playlist.Count - 1)
            {
                _currentTrackIndex++;
                PlayCurrentTrack();
            }
            else if (_isShuffle)
            {
                ShufflePlaylist();
                _currentTrackIndex = 0;
                PlayCurrentTrack();
            }
            else
            {
                _currentTrackIndex = 0;
                PlayCurrentTrack();
            }
        }

        public void Previous()
        {
            if (!HasPlaylist) return;

            if (_currentTrackIndex > 0)
            {
                _currentTrackIndex--;
                PlayCurrentTrack();
            }
            else
            {
                _currentTrackIndex = _playlist.Count - 1;
                PlayCurrentTrack();
            }
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
            // Handle media opened event if needed
        }

        private void OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            Stop();
            System.Windows.MessageBox.Show($"Media playback failed: {e.ErrorException.Message}", "Playback Error",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }

        private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            try
            {
                _isPlaying = true;
                UpdatePlaybackState(true);

                // Start position timer
                if (_positionTimer != null)
                {
                    _positionTimer.Stop();
                }
                
                InitializePositionTimer();
                _positionTimer.Start();

                // Initialize timeline slider
                if (_mediaElement.NaturalDuration.HasTimeSpan)
                {
                    InitializeTimelineSlider();
                    UpdateTimeDisplay(_mediaElement.Position, _mediaElement.NaturalDuration.TimeSpan);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MediaPlayer_MediaOpened: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _positionTimer?.Stop();
            if (_currentTrackIndex < _playlist.Count - 1)
            {
                _currentTrackIndex++;
                Play();
            }
            else
            {
                _isPlaying = false;
                UpdatePlaybackState(false);
            }
        }

        private void MediaPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
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
            if (timeInfo != null && timelineSlider != null)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    timeInfo.Text = $"{position:mm\\:ss} / {duration:mm\\:ss}";
                    if (!_isDraggingTimelineSlider)
                    {
                        timelineSlider.Value = position.TotalSeconds;
                    }
                });
            }
        }

        private void InitializeTimelineSlider()
        {
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

        public void HandleTimelineSliderDragStarted()
        {
            _isDraggingTimelineSlider = true;
        }

        public void HandleTimelineSliderDragCompleted()
        {
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

        public bool HasMedia => _mediaElement.Source != null;
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
            if (_controlsManager != null)
            {
                _controlsManager.ShowMediaControls();
            }
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
                _controlsManager.UpdatePlaybackButton(isPlaying);
                _controlsManager.UpdateVolumeControls(volume, isMuted);
            }
        }

        public void UpdateNowPlaying(string title, string artist = null, string series = null, string season = null)
        {
            if (_controlsManager != null)
            {
                _controlsManager.UpdateNowPlaying(title, artist, series, season);
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
                        _mediaElement.MediaOpened -= MediaPlayer_MediaOpened;
                        _mediaElement.MediaEnded -= MediaPlayer_MediaEnded;
                        _mediaElement.MediaFailed -= MediaPlayer_MediaFailed;
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
            if (tracks == null || !tracks.Any())
            {
                Debug.WriteLine("No tracks provided to play");
                return;
            }

            var firstTrack = tracks.First();
            Debug.WriteLine($"Playing {tracks.Count()} tracks. First track: {firstTrack.Name} with URL: {firstTrack.StreamUrl}");

            // Check if we're already playing these exact tracks
            if (_playlist != null && _playlist.Any() && 
                _playlist.Count == tracks.Count() && 
                _playlist[0].Id == firstTrack.Id)
            {
                Debug.WriteLine("Already playing these tracks, skipping duplicate playback");
                return;
            }

            // Convert MusicItems to Tracks
            _playlist = tracks.Select(t => new Universa.Desktop.Models.Track
            {
                Id = t.Id,
                Title = t.Name,
                Artist = t.Artist ?? t.ArtistName,
                Album = t.Album,
                StreamUrl = t.StreamUrl,
                Duration = t.Duration,
                TrackNumber = t.TrackNumber
            }).ToList();

            _currentTrackIndex = 0;

            // Show media controls only if they're not already visible
            var mediaControlsGrid = (_window as IMediaWindow).MediaControlsGrid;
            if (mediaControlsGrid == null || mediaControlsGrid.Visibility != Visibility.Visible)
            {
                ShowMediaControls();
            }

            // Start playback of the first track
            PlayCurrentTrack();
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
                _controlsManager.UpdatePlaybackButton(isPlaying);
                
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
            if (!HasPlaylist) return;
            
            _isShuffle = !_isShuffle;
            if (_isShuffle)
            {
                ShufflePlaylist();
            }
            else
            {
                var currentTrack = _playlist[_currentTrackIndex];
                _playlist = _playlist.OrderBy(x => x.TrackNumber).ToList();
                _currentTrackIndex = _playlist.IndexOf(currentTrack);
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
    }
} 