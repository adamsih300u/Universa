using System.Windows.Controls;
using Universa.Desktop.ViewModels;

namespace Universa.Desktop.Views
{
    public partial class MatrixChatView : Page
    {
        private MatrixChatViewModel _viewModel;

        public MatrixChatView()
        {
            InitializeComponent();
            _viewModel = new MatrixChatViewModel();
            DataContext = _viewModel;
            Loaded += MatrixChatView_Loaded;
        }

        private async void MatrixChatView_Loaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Connect to Matrix server when the view is loaded
            await _viewModel.Connect();
        }

        private void ScrollToBottom()
        {
            if (MessagesScroller != null)
            {
                MessagesScroller.ScrollToBottom();
            }
        }

        private void MessagesScroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // If we're adding new items and the scroll bar was at the bottom before
            if (e.ExtentHeightChange > 0 && e.ExtentHeight - e.ViewportHeight - e.VerticalOffset < e.ExtentHeightChange + 1)
            {
                ScrollToBottom();
            }
        }
    }
} 