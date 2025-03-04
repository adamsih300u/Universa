using System;
using System.Diagnostics;
using Universa.Desktop;

namespace Universa.Desktop.Managers
{
    /// <summary>
    /// Manages video playback windows for the application.
    /// </summary>
    public class VideoWindowManager
    {
        private VideoPlayerWindow _currentVideoWindow;
        
        /// <summary>
        /// Gets a value indicating whether a video is currently playing.
        /// </summary>
        public bool IsVideoPlaying => _currentVideoWindow != null;
        
        /// <summary>
        /// Opens a video in a new window.
        /// </summary>
        /// <param name="videoUrl">The URL of the video to play.</param>
        /// <param name="title">The title to display in the window.</param>
        public void OpenVideo(string videoUrl, string title)
        {
            try
            {
                // Close any existing video window
                CloseCurrentVideo();
                
                // Create and show a new video window
                _currentVideoWindow = new VideoPlayerWindow(videoUrl, title);
                _currentVideoWindow.PlaybackStopped += OnVideoPlaybackStopped;
                _currentVideoWindow.Show();
                
                Debug.WriteLine($"Opened video window for: {title}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error opening video window: {ex.Message}");
                _currentVideoWindow = null;
            }
        }
        
        /// <summary>
        /// Closes the current video window if one is open.
        /// </summary>
        public void CloseCurrentVideo()
        {
            if (_currentVideoWindow != null)
            {
                try
                {
                    _currentVideoWindow.PlaybackStopped -= OnVideoPlaybackStopped;
                    _currentVideoWindow.Close();
                    Debug.WriteLine("Closed current video window");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing video window: {ex.Message}");
                }
                finally
                {
                    _currentVideoWindow = null;
                }
            }
        }
        
        /// <summary>
        /// Pauses the current video if one is playing.
        /// </summary>
        public void PauseVideo()
        {
            if (_currentVideoWindow != null)
            {
                try
                {
                    _currentVideoWindow.SyncPlaybackState(false);
                    Debug.WriteLine("Paused video playback");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error pausing video: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Resumes the current video if one is paused.
        /// </summary>
        public void ResumeVideo()
        {
            if (_currentVideoWindow != null)
            {
                try
                {
                    _currentVideoWindow.SyncPlaybackState(true);
                    Debug.WriteLine("Resumed video playback");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error resuming video: {ex.Message}");
                }
            }
        }
        
        private void OnVideoPlaybackStopped()
        {
            Debug.WriteLine("Video playback stopped");
            _currentVideoWindow = null;
        }
    }
} 