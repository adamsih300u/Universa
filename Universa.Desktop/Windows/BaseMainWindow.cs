using System;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Services;
using Universa.Desktop.Managers;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Views;
using Universa.Desktop.Core.TTS;
using Universa.Desktop.Controls;
using Universa.Desktop.Library;

namespace Universa.Desktop.Windows
{
    public interface IMediaWindow
    {
        MediaElement MediaPlayer { get; }
        TextBlock NowPlayingText { get; }
        Slider TimelineSlider { get; }
        TextBlock TimeInfo { get; }
        Button VolumeButton { get; }
        Slider VolumeSlider { get; }
        Grid MediaControlsGrid { get; }
        Button PlayPauseButton { get; }
        Button PreviousButton { get; }
        Button NextButton { get; }
        Button ShuffleButton { get; }
        Button MuteButton { get; }
        Button StopButton { get; }
        MediaControlBar MediaControlBar { get; }
        MediaPlayerManager MediaPlayerManager { get; }
    }

    public class BaseMainWindow : Window, IAISupport, INotifyPropertyChanged, IMediaWindow
    {
        protected readonly IConfigurationService _configService;
        protected internal TabControl MainTabControl;
        protected internal ColumnDefinition NavigationColumn;
        protected internal MediaElement MediaPlayer;
        protected internal MediaPlayerManager _mediaPlayerManager;
        protected internal MediaControlsManager _mediaControlsManager;
        protected internal Grid MediaControlsGrid;
        protected internal TextBlock NowPlayingText;
        protected internal Slider TimelineSlider;
        protected internal TextBlock TimeInfo;
        protected internal Button VolumeButton;
        protected internal Slider VolumeSlider;
        protected internal Button PlayPauseButton;
        protected internal Button PreviousButton;
        protected internal Button NextButton;
        protected internal Button ShuffleButton;
        protected internal Button MuteButton;
        protected internal Button StopButton;
        protected internal MediaControlBar MediaControlBar;
        protected internal TTSManager _ttsManager;
        protected internal LibraryNavigator LibraryNavigator;

        // Public properties
        public TTSClient TTSClient => _ttsManager?.TTSClient;
        public virtual TabControl TabControlInstance => MainTabControl;
        public virtual MediaControlBar MediaControlBarControl => MediaControlBar;
        public virtual MediaPlayerManager MediaPlayerManagerInstance => _mediaPlayerManager;
        public virtual LibraryNavigator LibraryNavigatorInstance => LibraryNavigator;

        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<string> CurrentTrackChanged;
        public event Action<TimeSpan> PositionChanged;

        // Implement IMediaWindow interface with public properties
        MediaElement IMediaWindow.MediaPlayer => MediaPlayer;
        TextBlock IMediaWindow.NowPlayingText => NowPlayingText;
        Slider IMediaWindow.TimelineSlider => TimelineSlider;
        TextBlock IMediaWindow.TimeInfo => TimeInfo;
        Button IMediaWindow.VolumeButton => VolumeButton;
        Slider IMediaWindow.VolumeSlider => VolumeSlider;
        Grid IMediaWindow.MediaControlsGrid => MediaControlsGrid;
        Button IMediaWindow.PlayPauseButton => PlayPauseButton;
        Button IMediaWindow.PreviousButton => PreviousButton;
        Button IMediaWindow.NextButton => NextButton;
        Button IMediaWindow.ShuffleButton => ShuffleButton;
        Button IMediaWindow.MuteButton => MuteButton;
        Button IMediaWindow.StopButton => StopButton;
        MediaControlBar IMediaWindow.MediaControlBar => MediaControlBar;
        MediaPlayerManager IMediaWindow.MediaPlayerManager => _mediaPlayerManager;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public BaseMainWindow(IConfigurationService configService)
        {
            _configService = configService;
            Loaded += BaseMainWindow_Loaded;
        }

        private void BaseMainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeMediaPlayer();
        }

        protected virtual void InitializeMediaPlayer()
        {
            if (MediaPlayer != null)
            {
                // Get the MediaPlayerManager from the service provider
                _mediaPlayerManager = ServiceLocator.Instance.GetRequiredService<MediaPlayerManager>();
                
                // Initialize it with this window
                _mediaPlayerManager.InitializeWithWindow(this as IMediaWindow);
                
                // Create the media controls manager
                _mediaControlsManager = new MediaControlsManager(
                    this as IMediaWindow,
                    _mediaPlayerManager,
                    MediaControlsGrid,
                    PlayPauseButton,
                    PreviousButton,
                    NextButton,
                    ShuffleButton,
                    VolumeButton,
                    VolumeSlider,
                    TimeInfo,
                    TimelineSlider,
                    NowPlayingText,
                    MediaControlsGrid
                );
                
                // Set the controls manager on the media player manager
                _mediaPlayerManager.SetControlsManager(_mediaControlsManager);
            }
        }

        protected virtual void InitializeServices()
        {
            // Base initialization logic
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
        }

        protected virtual void SaveWindowState()
        {
            // Save window state logic
            if (NavigationColumn != null)
            {
                _configService.Provider.LastLibraryWidth = NavigationColumn.Width.Value;
                _configService.Provider.IsLibraryExpanded = NavigationColumn.Width.Value > 0;
                _configService.Save();
            }
        }

        protected virtual void RestoreWindowState()
        {
            // Restore window state logic
            if (NavigationColumn != null)
            {
                if (_configService.Provider.IsLibraryExpanded)
                {
                    NavigationColumn.Width = new GridLength(_configService.Provider.LastLibraryWidth);
                }
                else
                {
                    NavigationColumn.Width = new GridLength(0);
                }
            }
        }

        public virtual void OnAISettingsChanged()
        {
            // Base AI settings changed logic
        }

        public virtual void OpenFileInEditor(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return;

            if (!System.IO.File.Exists(filePath))
            {
                MessageBox.Show($"File not found: {filePath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var extension = System.IO.Path.GetExtension(filePath).ToLower();
            var title = System.IO.Path.GetFileName(filePath);

            // Create appropriate editor based on file type
            UserControl editor;
            switch (extension)
            {
                case ".md":
                case ".todo":
                    editor = new TextEditorControl(System.IO.File.ReadAllText(filePath));
                    break;
                default:
                    editor = new TextEditorControl(System.IO.File.ReadAllText(filePath));
                    break;
            }

            // Add the new tab
            var tab = new TabItem
            {
                Header = title,
                Content = editor,
                Tag = filePath
            };

            if (MainTabControl != null)
            {
                MainTabControl.Items.Add(tab);
                MainTabControl.SelectedItem = tab;
            }
        }
    }
} 