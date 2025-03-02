using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using Universa.Desktop.ViewModels;

namespace Universa.Desktop.Views
{
    public partial class ChatSidebar : UserControl
    {
        public ChatSidebar()
        {
            InitializeComponent();
            DataContext = new ChatSidebarViewModel();
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            Debug.WriteLine($"Key pressed: {e.Key}, Modifiers: {Keyboard.Modifiers}");
            
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    Debug.WriteLine("Shift+Enter detected");
                    var textBox = sender as TextBox;
                    if (textBox != null)
                    {
                        try
                        {
                            Debug.WriteLine($"Current text length: {textBox.Text?.Length ?? 0}");
                            Debug.WriteLine($"Caret index: {textBox.CaretIndex}");
                            Debug.WriteLine($"AcceptsReturn: {textBox.AcceptsReturn}");
                            Debug.WriteLine($"IsReadOnly: {textBox.IsReadOnly}");
                            Debug.WriteLine($"Current text: '{textBox.Text}'");
                            
                            // Direct approach to inserting newline
                            int caretIndex = textBox.CaretIndex;
                            textBox.SelectedText = Environment.NewLine;
                            textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                            e.Handled = true;
                            
                            Debug.WriteLine("Newline inserted successfully");
                            Debug.WriteLine($"New text length: {textBox.Text?.Length ?? 0}");
                            Debug.WriteLine($"New caret index: {textBox.CaretIndex}");
                            Debug.WriteLine($"New text: '{textBox.Text}'");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error handling Shift+Enter: {ex}");
                        }
                    }
                }
                else if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    Debug.WriteLine("Regular Enter detected - sending message");
                    e.Handled = true;
                    var viewModel = DataContext as ChatSidebarViewModel;
                    if (viewModel?.SendCommand?.CanExecute(null) == true)
                    {
                        viewModel.SendCommand.Execute(null);
                    }
                }
            }
        }
    }
} 