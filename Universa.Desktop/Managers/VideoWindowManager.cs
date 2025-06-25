using System;
using System.Windows;

namespace Universa.Desktop.Managers
{
    public class VideoWindowManager
    {
        private VideoPlayerWindow _currentVideoWindow;

        public VideoWindowManager()
        {
        }

        public VideoPlayerWindow CurrentVideoWindow => _currentVideoWindow;

        public void ShowVideoWindow(Uri videoUri, string title)
        {
            if (_currentVideoWindow != null)
            {
                CloseVideoWindow();
            }

            try
            {
                _currentVideoWindow = new VideoPlayerWindow(videoUri.ToString(), title);
                _currentVideoWindow.PlaybackStopped += () => _currentVideoWindow = null;
                _currentVideoWindow.Show();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error showing video window: {ex.Message}");
                MessageBox.Show($"Error showing video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void CloseVideoWindow()
        {
            if (_currentVideoWindow != null)
            {
                _currentVideoWindow.Close();
                _currentVideoWindow = null;
            }
        }

        public bool IsVideoWindowOpen => _currentVideoWindow != null;
    }
} 