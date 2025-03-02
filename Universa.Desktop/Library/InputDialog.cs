using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Universa.Desktop.Library
{
    public class InputDialog : Window
    {
        private TextBox _inputTextBox;
        private string _inputText;

        public string InputText
        {
            get => _inputText;
            private set
            {
                _inputText = value;
            }
        }

        public InputDialog(string title, string prompt)
        {
            Title = title;
            Width = 300;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Owner = Application.Current.MainWindow;
            Background = Application.Current.Resources["WindowBackgroundBrush"] as Brush;
            Foreground = Application.Current.Resources["TextBrush"] as Brush;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var promptLabel = new TextBlock
            {
                Text = prompt,
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Foreground
            };
            Grid.SetRow(promptLabel, 0);
            grid.Children.Add(promptLabel);

            _inputTextBox = new TextBox
            {
                Margin = new Thickness(10),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Background = Application.Current.Resources["WindowBackgroundBrush"] as Brush,
                Foreground = Application.Current.Resources["TextBrush"] as Brush,
                BorderBrush = Application.Current.Resources["BorderBrush"] as Brush
            };
            Grid.SetRow(_inputTextBox, 1);
            grid.Children.Add(_inputTextBox);

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(10)
            };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button
            {
                Content = "OK",
                Width = 75,
                Height = 23,
                Margin = new Thickness(0, 0, 10, 0),
                IsDefault = true,
                Background = Application.Current.Resources["ButtonBackgroundBrush"] as Brush,
                Foreground = Application.Current.Resources["TextBrush"] as Brush,
                BorderBrush = Application.Current.Resources["BorderBrush"] as Brush
            };
            okButton.Click += (s, e) =>
            {
                DialogResult = true;
                InputText = _inputTextBox.Text;
                Close();
            };

            var cancelButton = new Button
            {
                Content = "Cancel",
                Width = 75,
                Height = 23,
                IsCancel = true,
                Background = Application.Current.Resources["ButtonBackgroundBrush"] as Brush,
                Foreground = Application.Current.Resources["TextBrush"] as Brush,
                BorderBrush = Application.Current.Resources["BorderBrush"] as Brush
            };
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);

            Content = grid;

            // Set focus to the input box
            Loaded += (s, e) => _inputTextBox.Focus();
        }
    }
} 