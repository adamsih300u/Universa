using System.Windows;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.Views
{
    public partial class PlaybackWindow : Window
    {
        private readonly AudiobookshelfService _service;
        private readonly AudiobookItem _item;
        private string _status;

        public PlaybackWindow(AudiobookshelfService service, AudiobookItem item)
        {
            InitializeComponent();
            _service = service;
            _item = item;
            DataContext = _item;
            Status = "Ready to play";
        }

        public string Status
        {
            get => _status;
            set
            {
                _status = value;
                // TODO: Implement proper property change notification
            }
        }

        // TODO: Implement playback controls and audio streaming
    }
} 