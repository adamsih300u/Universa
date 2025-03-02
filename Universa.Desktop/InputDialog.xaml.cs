using System.Windows;

namespace Universa.Desktop
{
    public partial class InputDialog : Window
    {
        public string ResponseText => ResponseTextBox.Text;
        public string Prompt { get; }

        public InputDialog(string title, string prompt)
        {
            InitializeComponent();
            Title = title;
            Prompt = prompt;
            DataContext = this;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 