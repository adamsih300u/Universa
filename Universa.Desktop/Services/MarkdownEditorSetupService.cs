using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Universa.Desktop.Helpers;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for setting up and configuring markdown editors
    /// </summary>
    public class MarkdownEditorSetupService : IMarkdownEditorSetupService
    {
        private const int TAB_SIZE = 4;

        public TextHighlighter SetupEditor(
            TextBox editor,
            ScrollViewer editorScrollViewer,
            Action onNavigateToNextChapter,
            Action onNavigateToPreviousChapter,
            Action<bool> onScrollByPage,
            Action<object, TextChangedEventArgs> onTextChanged,
            Action<object, RoutedEventArgs> onSelectionChanged,
            Action<object, ScrollChangedEventArgs> onScrollChanged)
        {
            if (editor == null) throw new ArgumentNullException(nameof(editor));

            // Configure basic editor properties
            editor.FontFamily = new FontFamily("Cascadia Code");
            editor.BorderThickness = new Thickness(0);
            editor.AcceptsTab = true;
            editor.AcceptsReturn = true;
            editor.TextWrapping = TextWrapping.Wrap;
            editor.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;

            // Set up keyboard handling
            SetupKeyboardHandling(editor, onNavigateToNextChapter, onNavigateToPreviousChapter, onScrollByPage);

            // Add line spacing for comfortable reading
            editor.SetValue(Block.LineHeightProperty, 1.7);

            // Wire up event handlers
            if (onTextChanged != null)
                editor.TextChanged += (s, e) => onTextChanged(s, e);

            if (onSelectionChanged != null)
                editor.SelectionChanged += (s, e) => onSelectionChanged(s, e);

            // Initialize text highlighter
            var textHighlighter = new TextHighlighter(editor);

            // Set up scroll event handler
            if (onScrollChanged != null)
            {
                editor.Loaded += (s, e) => {
                    var scrollViewer = GetScrollViewer(editor);
                    if (scrollViewer != null)
                    {
                        scrollViewer.ScrollChanged += (sender, args) => onScrollChanged(sender, args);
                    }
                };
            }

            return textHighlighter;
        }

        private void SetupKeyboardHandling(
            TextBox editor,
            Action onNavigateToNextChapter,
            Action onNavigateToPreviousChapter,
            Action<bool> onScrollByPage)
        {
            editor.PreviewKeyDown += (s, e) => {
                // Handle chapter navigation
                if (e.Key == Key.Down && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                {
                    Debug.WriteLine("Ctrl+Down detected - navigating to next chapter");
                    e.Handled = true;
                    onNavigateToNextChapter?.Invoke();
                    return;
                }
                else if (e.Key == Key.Up && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
                {
                    Debug.WriteLine("Ctrl+Up detected - navigating to previous chapter");
                    e.Handled = true;
                    onNavigateToPreviousChapter?.Invoke();
                    return;
                }
                // Handle Page Up/Down for proper scrolling
                else if (e.Key == Key.PageUp)
                {
                    Debug.WriteLine("PageUp detected - scrolling up");
                    e.Handled = true;
                    onScrollByPage?.Invoke(false); // Scroll up
                    return;
                }
                else if (e.Key == Key.PageDown)
                {
                    Debug.WriteLine("PageDown detected - scrolling down");
                    e.Handled = true;
                    onScrollByPage?.Invoke(true); // Scroll down
                    return;
                }
                // Handle tab key to insert 4 spaces instead of a tab character
                if (e.Key == Key.Tab)
                {
                    e.Handled = true;
                    int caretIndex = editor.CaretIndex;
                    editor.Text = editor.Text.Insert(caretIndex, new string(' ', TAB_SIZE));
                    editor.CaretIndex = caretIndex + TAB_SIZE;
                }
                else if (e.Key == Key.Enter)
                {
                    HandleEnterKey(editor, e);
                }
                else if (e.Key == Key.Back && editor.CaretIndex > 0)
                {
                    HandleBackspaceKey(editor, e);
                }
            };
        }

        private void HandleEnterKey(TextBox editor, KeyEventArgs e)
        {
            // When Enter is pressed, add an extra newline for paragraph spacing
            e.Handled = true;
            int caretIndex = editor.CaretIndex;
            
            // Check if we're already at the end of a paragraph (two newlines)
            bool alreadyHasNewline = false;
            if (caretIndex < editor.Text.Length && editor.Text[caretIndex] == '\n')
            {
                alreadyHasNewline = true;
            }
            else if (caretIndex > 0 && caretIndex < editor.Text.Length && 
                     editor.Text[caretIndex-1] == '\n' && editor.Text[caretIndex] == '\n')
            {
                alreadyHasNewline = true;
            }
            
            // Insert a single newline if we're already at a paragraph break,
            // otherwise insert two newlines for paragraph spacing
            string insertion = alreadyHasNewline ? "\n" : "\n\n";
            editor.Text = editor.Text.Insert(caretIndex, insertion);
            editor.CaretIndex = caretIndex + insertion.Length;
        }

        private void HandleBackspaceKey(TextBox editor, KeyEventArgs e)
        {
            // Check if we're at a tab stop
            int spacesBeforeCaret = 0;
            int checkIndex = editor.CaretIndex - 1;
            
            while (checkIndex >= 0 && editor.Text[checkIndex] == ' ')
            {
                spacesBeforeCaret++;
                checkIndex--;
            }

            // If we have spaces before the caret and they align with a tab stop
            if (spacesBeforeCaret > 0 && spacesBeforeCaret <= TAB_SIZE)
            {
                int spacesToRemove = spacesBeforeCaret % TAB_SIZE;
                if (spacesToRemove == 0) spacesToRemove = TAB_SIZE;
                
                if (spacesBeforeCaret >= spacesToRemove)
                {
                    e.Handled = true;
                    int removeStart = editor.CaretIndex - spacesToRemove;
                    editor.Text = editor.Text.Remove(removeStart, spacesToRemove);
                    editor.CaretIndex = removeStart;
                }
            }
        }

        public void ApplyTheme(TextBox editor, bool isDarkMode)
        {
            if (editor == null) return;

            editor.Background = (Brush)Application.Current.Resources["WindowBackgroundBrush"];
            editor.Foreground = (Brush)Application.Current.Resources["TextBrush"];
            editor.CaretBrush = (Brush)Application.Current.Resources["TextBrush"];
            
            // Create a semi-transparent yellow brush similar to search highlighting
            var selectionBrush = new SolidColorBrush(Color.FromArgb(128, 255, 235, 100));
            editor.SelectionBrush = selectionBrush;
            editor.SelectionTextBrush = (Brush)Application.Current.Resources["TextBrush"];
        }

        public ScrollViewer GetScrollViewer(DependencyObject depObj)
        {
            if (depObj == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);

                if (child is ScrollViewer scrollViewer)
                    return scrollViewer;

                var result = GetScrollViewer(child);
                if (result != null)
                    return result;
            }
            return null;
        }
    }
} 