using System.Windows;

namespace Universa.Desktop.Dialogs
{
    public partial class ProgressDialog : Window
    {
        public new string Title
        {
            get => base.Title;
            set => base.Title = value;
        }

        public string Message
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        public new Window Owner
        {
            get => base.Owner;
            set => base.Owner = value;
        }

        public ProgressDialog()
        {
            InitializeComponent();
        }

        public new void Close()
        {
            base.Close();
        }
    }
} 