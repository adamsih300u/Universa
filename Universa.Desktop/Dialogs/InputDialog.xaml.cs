using System;
using System.Windows;
using System.Windows.Controls;

namespace Universa.Desktop.Dialogs
{
    public partial class InputDialog : Window
    {
        private TextBox _inputTextBox;
        private string _inputText;

        public string InputText
        {
            get => _inputText;
            private set => _inputText = value;
        }

        private bool _required;

        public InputDialog(string title, string prompt)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            _required = true;
            _inputTextBox = InputTextBox;
            OkButton.IsEnabled = false;
            InputTextBox.Focus();
        }

        public InputDialog(string title, string prompt, bool required)
        {
            InitializeComponent();
            Title = title;
            PromptText.Text = prompt;
            _required = required;
            _inputTextBox = InputTextBox;
            OkButton.IsEnabled = !required;
            InputTextBox.Focus();
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (OkButton != null)
            {
                OkButton.IsEnabled = !_required || !string.IsNullOrWhiteSpace(InputTextBox.Text);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (_required && string.IsNullOrWhiteSpace(InputTextBox.Text))
            {
                return;
            }
            _inputText = InputTextBox.Text;
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