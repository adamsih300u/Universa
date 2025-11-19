using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Universa.Desktop.Services;
using Universa.Desktop.Interfaces;
using Universa.Desktop.ViewModels;

namespace Universa.Desktop.Views
{
    public partial class AudiobookTab : UserControl
    {
        private readonly AudiobookTabViewModel _viewModel;

        public AudiobookTab(IAudiobookshelfService audiobookshelfService)
        {
            InitializeComponent();
            _viewModel = new AudiobookTabViewModel(audiobookshelfService);
            DataContext = _viewModel;
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.SelectedItem != null)
            {
                _viewModel.PlayCommand.Execute(null);
            }
        }
    }
} 