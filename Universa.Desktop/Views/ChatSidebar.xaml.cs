using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Converters;
using System.Windows.Documents;

namespace Universa.Desktop.Views
{
    public partial class ChatSidebar : UserControl
    {
        public ChatSidebarViewModel ViewModel => DataContext as ChatSidebarViewModel;
        
        public ChatSidebar()
        {
            try
            {
                InitializeComponent();
                
                // Initialize DataContext after component initialization
                this.DataContext = new ChatSidebarViewModel();
                
                // Set the ScrollViewer in the ViewModel
                if (ViewModel != null)
                {
                    ViewModel.ChatScrollViewer = MessagesScrollViewer;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing ChatSidebar: {ex.Message}");
                MessageBox.Show($"Error initializing chat: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
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

        private void MessageContent_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is RichTextBox richTextBox)
                {
                    string content = richTextBox.Tag as string;
                    if (content != null)
                    {
                        FlowDocument document = new FlowDocument();
                        
                        try
                        {
                            CodeBlockFormatter.FormatContent(content, document);
                        }
                        catch (Exception ex)
                        {
                            // Fallback to simple text if formatting fails
                            document.Blocks.Clear();
                            document.Blocks.Add(new Paragraph(new Run(content)));
                            Debug.WriteLine($"Error formatting content: {ex.Message}");
                        }
                        
                        richTextBox.Document = document;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in MessageContent_Loaded: {ex.Message}");
                // Don't rethrow to prevent application crash
            }
        }

        // Add a method to handle text changed event
        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var viewModel = DataContext as ViewModels.ChatSidebarViewModel;
                if (viewModel == null) return;

                // Try to call the OnInputTextChanged method via reflection since it's private
                try
                {
                    var method = viewModel.GetType().GetMethod("OnInputTextChanged", 
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    
                    if (method != null)
                    {
                        method.Invoke(viewModel, null);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error invoking OnInputTextChanged: {ex.Message}");
                    // Don't rethrow to prevent application crash
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in InputTextBox_TextChanged: {ex.Message}");
                // Don't rethrow to prevent application crash
            }
        }
    }
} 