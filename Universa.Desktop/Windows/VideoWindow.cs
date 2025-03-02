using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop.Windows
{
    public interface IVideoHost
    {
        void SyncPlaybackState(bool isPlaying);
        void UpdateVolumeControls(double volume, bool isMuted);
    }

    public partial class VideoWindow : Window, IVideoHost
    {
        private readonly IMediaWindow _parentWindow;
        private bool _isPlaying;

        public VideoWindow(IMediaWindow parentWindow)
        {
            _parentWindow = parentWindow;
            InitializeComponent();
            Closing += VideoWindow_Closing;
        }

        public void PlayVideo(string path, string title)
        {
            Title = title;
            VideoPlayer.Source = new Uri(path);
            VideoPlayer.Play();
            _isPlaying = true;
        }

        public void SyncPlaybackState(bool isPlaying)
        {
            if (_isPlaying != isPlaying)
            {
                _isPlaying = isPlaying;
                if (isPlaying)
                    VideoPlayer.Play();
                else
                    VideoPlayer.Pause();
            }
        }

        public void UpdateVolumeControls(double volume, bool isMuted)
        {
            VideoPlayer.Volume = isMuted ? 0 : volume;
        }

        private void VideoWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            VideoPlayer.Stop();
            VideoPlayer.Source = null;
        }
    }
} 