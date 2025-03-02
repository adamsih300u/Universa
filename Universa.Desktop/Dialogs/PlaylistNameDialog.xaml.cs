using System.Windows;

namespace Universa.Desktop.Dialogs
{
    public partial class PlaylistNameDialog : Window
    {
        public string PlaylistName { get; private set; }

        public PlaylistNameDialog()
        {
            InitializeComponent();
            PlaylistNameTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(PlaylistNameTextBox.Text))
            {
                PlaylistName = PlaylistNameTextBox.Text;
                DialogResult = true;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
} 