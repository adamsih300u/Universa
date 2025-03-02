using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Universa.Desktop.Adorners;
using System.Windows.Threading;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Windows.Input;

namespace Universa.Desktop.Helpers
{
    public class TextHighlighter
    {
        private readonly TextBox _textBox;
        private TextHighlightAdorner _currentAdorner;
        private AdornerLayer _adornerLayer;
        private readonly DispatcherTimer _updateTimer;
        private string _pendingText;
        private Color _pendingColor;
        private const int UPDATE_DELAY_MS = 100; // Debounce delay
        private ScrollViewer _scrollViewer;

        public TextHighlighter(TextBox textBox)
        {
            _textBox = textBox;
            _textBox.Loaded += (s, e) => {
                _adornerLayer = AdornerLayer.GetAdornerLayer(_textBox);
                
                // Get the ScrollViewer after the template is loaded
                _scrollViewer = GetScrollViewer(_textBox);
                if (_scrollViewer != null)
                {
                    _scrollViewer.ScrollChanged += (sender, args) => {
                        if (_currentAdorner != null)
                        {
                            _currentAdorner.InvalidateVisual();
                        }
                    };
                }
            };

            // Handle selection changes
            _textBox.SelectionChanged += (s, e) => {
                if (_currentAdorner != null)
                {
                    _currentAdorner.InvalidateVisual();
                }
            };
            
            // Setup debounce timer
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(UPDATE_DELAY_MS)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        private ScrollViewer GetScrollViewer(DependencyObject depObj)
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

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            _updateTimer.Stop();
            UpdateHighlightsImmediate(_pendingText, _pendingColor);
        }

        private bool EnsureAdornerLayer()
        {
            if (_adornerLayer == null)
            {
                _adornerLayer = AdornerLayer.GetAdornerLayer(_textBox);
            }
            return _adornerLayer != null;
        }

        public void HighlightText(string text, Color highlightColor)
        {
            _pendingText = text;
            _pendingColor = highlightColor;

            // Reset and restart the timer
            _updateTimer.Stop();
            _updateTimer.Start();
        }

        private void UpdateHighlightsImmediate(string text, Color highlightColor)
        {
            if (!EnsureAdornerLayer() || string.IsNullOrEmpty(text))
            {
                ClearHighlights();
                return;
            }

            try
            {
                // Escape special regex characters but keep spaces as flexible whitespace
                var pattern = Regex.Escape(text.Trim())
                    .Replace("\\ ", "\\s+")  // Replace escaped spaces with flexible whitespace
                    .Replace("\\.", "\\.")   // Keep period as literal
                    .Replace("\\,", "\\,")   // Keep comma as literal
                    .Replace("\\!", "\\!")   // Keep exclamation as literal
                    .Replace("\\?", "\\?");  // Keep question mark as literal

                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                var match = regex.Match(_textBox.Text);

                if (!match.Success)
                {
                    ClearHighlights();
                    return;
                }

                var rects = new List<Rect>();
                var startIndex = match.Index;
                var length = match.Length;

                try
                {
                    var startPoint = _textBox.GetRectFromCharacterIndex(startIndex);
                    var endPoint = _textBox.GetRectFromCharacterIndex(startIndex + length);

                    // If the highlight spans multiple lines
                    if (startPoint.Top != endPoint.Top)
                    {
                        // Add rect for first line
                        rects.Add(new Rect(
                            startPoint.X,
                            startPoint.Y,
                            _textBox.ViewportWidth - startPoint.X,
                            startPoint.Height
                        ));

                        // Add rects for middle lines (if any)
                        double currentY = startPoint.Y + startPoint.Height;
                        while (currentY < endPoint.Y)
                        {
                            rects.Add(new Rect(
                                0,
                                currentY,
                                _textBox.ViewportWidth,
                                startPoint.Height
                            ));
                            currentY += startPoint.Height;
                        }

                        // Add rect for last line
                        rects.Add(new Rect(
                            0,
                            endPoint.Y,
                            endPoint.X,
                            endPoint.Height
                        ));
                    }
                    else
                    {
                        // Single line highlight
                        rects.Add(new Rect(
                            startPoint.X,
                            startPoint.Y,
                            endPoint.X - startPoint.X,
                            startPoint.Height
                        ));
                    }

                    // Remove the problematic scroll and focus management code
                    // Let the parent control handle scrolling if needed
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing highlight range: {ex}");
                    return;
                }

                // Clear any existing adorner before adding new one
                ClearHighlights();

                var brush = new SolidColorBrush(highlightColor) { Opacity = 0.3 };
                _currentAdorner = new TextHighlightAdorner(_textBox, rects, brush);
                _adornerLayer.Add(_currentAdorner);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error highlighting text: {ex}");
                ClearHighlights();
            }
        }

        public void ClearHighlights()
        {
            _updateTimer.Stop();
            if (_currentAdorner != null && _adornerLayer != null)
            {
                try
                {
                    _adornerLayer.Remove(_currentAdorner);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error clearing highlights: {ex}");
                }
                _currentAdorner = null;
            }
        }

        // Add back compatibility methods
        public void HighlightRanges(List<(int start, int length)> ranges, Color highlightColor)
        {
            if (ranges == null || ranges.Count == 0 || _textBox == null)
            {
                ClearHighlights();
                return;
            }

            if (!EnsureAdornerLayer())
            {
                return;
            }

            try
            {
                var rects = new List<Rect>();
                foreach (var (start, length) in ranges)
                {
                    if (start < 0 || length <= 0 || start >= _textBox.Text.Length)
                        continue;

                    try
                    {
                        var startPoint = _textBox.GetRectFromCharacterIndex(start);
                        var endPoint = _textBox.GetRectFromCharacterIndex(start + length);

                        // If the highlight spans multiple lines
                        if (startPoint.Top != endPoint.Top)
                        {
                            // Add rect for first line
                            rects.Add(new Rect(
                                startPoint.X,
                                startPoint.Y,
                                _textBox.ViewportWidth - startPoint.X,
                                startPoint.Height
                            ));

                            // Add rects for middle lines (if any)
                            double currentY = startPoint.Y + startPoint.Height;
                            while (currentY < endPoint.Y)
                            {
                                rects.Add(new Rect(
                                    0,
                                    currentY,
                                    _textBox.ViewportWidth,
                                    startPoint.Height
                                ));
                                currentY += startPoint.Height;
                            }

                            // Add rect for last line
                            rects.Add(new Rect(
                                0,
                                endPoint.Y,
                                endPoint.X,
                                endPoint.Height
                            ));
                        }
                        else
                        {
                            // Single line highlight
                            rects.Add(new Rect(
                                startPoint.X,
                                startPoint.Y,
                                endPoint.X - startPoint.X,
                                startPoint.Height
                            ));
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error processing highlight range: {ex}");
                    }
                }

                if (rects.Count > 0)
                {
                    // Clear any existing adorner
                    ClearHighlights();

                    // Create a semi-transparent brush
                    var brush = new SolidColorBrush(highlightColor) { Opacity = 0.3 };
                    
                    // Create and add the new adorner
                    _currentAdorner = new TextHighlightAdorner(_textBox, rects, brush);
                    _adornerLayer.Add(_currentAdorner);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HighlightRanges: {ex}");
                ClearHighlights();
            }
        }

        public void RefreshHighlights()
        {
            if (_pendingText != null)
            {
                UpdateHighlightsImmediate(_pendingText, _pendingColor);
            }
        }
    }
} 