using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Universa.Desktop.Models;
using Universa.Desktop.Properties;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using Universa.Desktop.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Universa.Desktop.ViewModels;

namespace Universa.Desktop
{
    public partial class ChatSidebar : UserControl
    {
        private bool _autoScroll = true;

        public ChatSidebar()
        {
            InitializeComponent();
            
            if (DesignerProperties.GetIsInDesignMode(this))
            {
                // Only create a new ViewModel in design mode
                DataContext = new ChatSidebarViewModel();
            }
        }

        public ChatSidebar(ChatSidebarViewModel viewModel) : this()
        {
            DataContext = viewModel;
        }

        private void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!(sender is TextBox textBox)) return;

            if (e.Key == Key.Enter)
            {
                if (Keyboard.Modifiers == ModifierKeys.Shift)
                {
                    // Insert a newline at the current caret position
                    int caretIndex = textBox.CaretIndex;
                    string text = textBox.Text ?? string.Empty;
                    string newText = text.Insert(caretIndex, Environment.NewLine);
                    textBox.Text = newText;
                    textBox.CaretIndex = caretIndex + Environment.NewLine.Length;
                    e.Handled = true;
                }
                else if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    // Send the message
                    if (DataContext is ChatSidebarViewModel viewModel && 
                        viewModel.SendCommand?.CanExecute(null) == true)
                    {
                        viewModel.SendCommand.Execute(null);
                        e.Handled = true;
                    }
                }
            }
        }

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (_autoScroll && e.Action == NotifyCollectionChangedAction.Add)
            {
                MessageScrollViewer.ScrollToBottom();
            }
        }

        private void MessageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            // User scroll up/down
            if (e.ExtentHeightChange == 0)
            {
                // If close to bottom, enable auto-scroll
                _autoScroll = MessageScrollViewer.VerticalOffset >= MessageScrollViewer.ScrollableHeight - 10;
            }

            // Content added/removed
            if (_autoScroll && e.ExtentHeightChange != 0)
            {
                MessageScrollViewer.ScrollToBottom();
            }
        }
    }
} 