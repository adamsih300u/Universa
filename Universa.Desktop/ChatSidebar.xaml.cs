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
        // SPLENDID: Enhanced scroll state tracking for solid virtualized scrolling
        private bool _isUserScrolling = false;
        private bool _isAtBottom = true;
        private double _lastScrollPosition = 0;
        private DateTime _lastUserScrollTime = DateTime.MinValue;
        private const double SCROLL_THRESHOLD = 150.0; // Pixels from bottom to consider "at bottom"
        private const int USER_SCROLL_TIMEOUT_MS = 2000; // How long to wait after user scroll before allowing auto-scroll

        public ChatSidebar()
        {
            InitializeComponent();
            
            // SPLENDID: Always create a ViewModel, both in design mode and runtime
            var viewModel = new ChatSidebarViewModel();
            DataContext = viewModel;
            viewModel.ScrollToBottomAction = ScrollToBottomConditional;
            
            // Wire up scroll delegate when DataContext changes
            DataContextChanged += (s, e) =>
            {
                if (e.NewValue is ChatSidebarViewModel vm)
                {
                    vm.ScrollToBottomAction = ScrollToBottomConditional;
                }
            };
        }

        public ChatSidebar(ChatSidebarViewModel viewModel) : this()
        {
            DataContext = viewModel;
            
            // Wire up the scroll delegate
            if (viewModel != null)
            {
                viewModel.ScrollToBottomAction = ScrollToBottomConditional;
            }
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
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // SPLENDID: Enhanced scroll handling that respects user scroll position
                var scrollViewer = GetScrollViewer();
                if (scrollViewer != null)
                {
                    UpdateScrollState(scrollViewer);
                    
                    // Only auto-scroll if user hasn't manually scrolled up recently and is near bottom
                    bool shouldAutoScroll = _isAtBottom && !IsUserCurrentlyScrolling();
                    
                    if (shouldAutoScroll)
                    {
                        ScrollToBottomImmediate();
                    }
                    else
                    {
                        // Skip auto-scroll when user is viewing older messages
                    }
                }
            }
        }

        private void UpdateScrollState(ScrollViewer scrollViewer)
        {
            if (scrollViewer == null) return;
            
            double currentPosition = scrollViewer.VerticalOffset;
            double maxScrollPosition = scrollViewer.ScrollableHeight;
            
            // Update whether we're at the bottom
            _isAtBottom = (maxScrollPosition - currentPosition) <= SCROLL_THRESHOLD;
            
            // Track if this was a significant user-initiated scroll change
            double scrollDelta = Math.Abs(currentPosition - _lastScrollPosition);
            if (scrollDelta > 5) // Minimum delta to consider it a real scroll
            {
                _lastUserScrollTime = DateTime.Now;
                _isUserScrolling = true;
            }
            
            _lastScrollPosition = currentPosition;
        }

        private bool IsUserCurrentlyScrolling()
        {
            // Consider user as "currently scrolling" if they scrolled within the timeout period
            return (DateTime.Now - _lastUserScrollTime).TotalMilliseconds < USER_SCROLL_TIMEOUT_MS;
        }

        private ScrollViewer GetScrollViewer()
        {
            if (MessageList != null)
            {
                var scrollViewer = FindVisualChild<ScrollViewer>(MessageList);
                
                // Wire up scroll changed event if not already done
                if (scrollViewer != null && !HasScrollChangedHandler(scrollViewer))
                {
                    scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
                }
                
                return scrollViewer;
            }
            return null;
        }

        private bool HasScrollChangedHandler(ScrollViewer scrollViewer)
        {
            // Simple check to avoid duplicate event handlers
            // In a production app, you might want to track this more precisely
            return scrollViewer.Tag?.ToString() == "HasScrollHandler";
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            var scrollViewer = sender as ScrollViewer;
            if (scrollViewer != null)
            {
                // Mark that we've added the handler
                scrollViewer.Tag = "HasScrollHandler";
                
                // Update our scroll state tracking
                UpdateScrollState(scrollViewer);
                
                // Reset user scrolling flag if they're back at the bottom
                if (_isAtBottom)
                {
                    _isUserScrolling = false;
                }
            }
        }

        // SPLENDID: Conditional scroll that respects user position
        private void ScrollToBottomConditional()
        {
            var scrollViewer = GetScrollViewer();
            if (scrollViewer != null)
            {
                UpdateScrollState(scrollViewer);
                
                // Only scroll if user is at bottom or hasn't scrolled recently
                if (_isAtBottom || !IsUserCurrentlyScrolling())
                {
                    ScrollToBottomImmediate();
                }
                else
                {
                    // User is viewing older messages, skip auto-scroll
                }
            }
        }

        // SPLENDID: Immediate scroll for when we know it's appropriate
        private void ScrollToBottomImmediate()
        {
            var scrollViewer = GetScrollViewer();
            if (scrollViewer != null)
            {
                // Reset user scroll state since we're going to bottom
                _isUserScrolling = false;
                _isAtBottom = true;
                
                // SPLENDID: Multi-stage scroll approach for smooth virtualization
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // First pass: Basic scroll to prepare virtualization
                    scrollViewer.ScrollToEnd();
                    _lastScrollPosition = scrollViewer.VerticalOffset;
                    
                    // Second pass: Ensure we're truly at the bottom after layout updates
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        scrollViewer.ScrollToEnd();
                        _lastScrollPosition = scrollViewer.VerticalOffset;
                        UpdateScrollState(scrollViewer);
                    }), System.Windows.Threading.DispatcherPriority.Loaded);
                    
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        // Legacy method for backwards compatibility
        private void ScrollToBottom()
        {
            ScrollToBottomImmediate();
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                {
                    return result;
                }
                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private async void TTSButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var openAITTS = new OpenAITTSService();
                if (openAITTS.IsAvailable())
                {
                    if (DataContext is ChatSidebarViewModel viewModel)
                    {
                        var lastMessage = viewModel.Messages?.LastOrDefault(m => m.Role.ToLower() == "assistant");
                        if (lastMessage != null && !string.IsNullOrWhiteSpace(lastMessage.Content))
                        {
                            // Check if already playing, then stop
                            if (openAITTS.IsPlaying)
                            {
                                openAITTS.Stop();
                                TTSButton.Content = "🔊";
                                return;
                            }

                            // Start TTS
                            TTSButton.Content = "⏹️";
                            await openAITTS.SpeakAsync(lastMessage.Content);
                            TTSButton.Content = "🔊";
                        }
                        else
                        {
                            MessageBox.Show("No assistant response to speak.", "Nothing to Read", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("OpenAI TTS is not configured. Please set your OpenAI API key in Settings.", 
                        "TTS Not Available", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                TTSButton.Content = "🔊";
                MessageBox.Show($"Error during TTS: {ex.Message}", "TTS Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
} 