using System.Windows;

namespace Universa.Desktop.Controls
{
    public partial class LoadingDialog : Window
    {
        public LoadingDialog(string title, string message)
        {
            InitializeComponent();
            Title = title;
            MessageText.Text = message;
            Owner = Application.Current.MainWindow;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        public void UpdateMessage(string message)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateMessage(message));
                return;
            }
            MessageText.Text = message;
        }
    }
} 