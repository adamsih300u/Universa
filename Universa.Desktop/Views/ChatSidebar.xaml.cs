using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Diagnostics;
using System.ComponentModel;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Converters;
using System.Windows.Documents;
using System.Windows.Threading;

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
                
                // Add loaded event handler to ensure ScrollViewer is properly initialized
                this.Loaded += ChatSidebar_Loaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing ChatSidebar: {ex.Message}");
                MessageBox.Show($"Error initializing chat: {ex.Message}", "Initialization Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ChatSidebar_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Set the ScrollViewer in the ViewModel after the control is fully loaded
                if (ViewModel != null && MessagesScrollViewer != null)
                {
                    ViewModel.ChatScrollViewer = MessagesScrollViewer;
                    System.Diagnostics.Debug.WriteLine("ChatSidebar loaded and ScrollViewer assigned to ViewModel");
                    
                    // Trigger a delayed scroll restoration in case tabs were loaded before the UI was ready
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
                    {
                        // This ensures scroll position is restored even if it was attempted before UI was ready
                        if (ViewModel.SelectedTab != null && ViewModel.SelectedTab.CurrentScrollPosition > 0)
                        {
                            System.Diagnostics.Debug.WriteLine("Attempting scroll restoration after ChatSidebar loaded");
                            ViewModel.ChatScrollViewer?.ScrollToVerticalOffset(ViewModel.SelectedTab.CurrentScrollPosition);
                        }
                    }), System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ChatSidebar_Loaded: {ex.Message}");
            }
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    // Insert newline for Shift+Enter
                    var textBox = sender as TextBox;
                    if (textBox != null)
                    {
                        try
                        {
                            int caretIndex = textBox.CaretIndex;
                            textBox.SelectedText = Environment.NewLine;
                            textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                            e.Handled = true;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error handling Shift+Enter: {ex}");
                        }
                    }
                }
                else if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    // Send message for regular Enter
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