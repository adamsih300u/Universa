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
using System.Threading.Tasks;

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
                // Normalize the search text and create a robust pattern
                var searchText = text.Trim();
                
                // Split into lines and process each line separately for better matching
                var lines = searchText.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
                string pattern;
                
                if (lines.Length == 1)
                {
                    // Single line - use simpler pattern
                    pattern = Regex.Escape(searchText).Replace("\\ ", "\\s+");
                }
                else
                {
                    // Multi-line - build pattern line by line
                    var patternParts = new List<string>();
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();
                        if (string.IsNullOrEmpty(line))
                        {
                            // Empty line - match any whitespace
                            patternParts.Add("\\s*");
                        }
                        else
                        {
                            // Non-empty line - escape and allow flexible spacing
                            var linePart = Regex.Escape(line).Replace("\\ ", "\\s+");
                            patternParts.Add($"\\s*{linePart}\\s*");
                        }
                        
                        // Add line break pattern between lines (except after the last line)
                        if (i < lines.Length - 1)
                        {
                            patternParts.Add("\\r?\\n");
                        }
                    }
                    
                    pattern = string.Join("", patternParts);
                }

                // Create robust regex that handles multi-line text properly
                var regex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
                var match = regex.Match(_textBox.Text);

                // Debug output for troubleshooting
                Debug.WriteLine($"Search text length: {searchText.Length}");
                Debug.WriteLine($"Match found: {match.Success}, Index: {match.Index}, Length: {match.Length}");

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

        // Optimized method for highlighting ranges with viewport culling
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

            // PERFORMANCE FIX: For large highlight operations, use async processing
            if (ranges.Count > 100 || ranges.Any(r => r.length > 1000))
            {
                HighlightRangesAsync(ranges, highlightColor);
                return;
            }

            try
            {
                var rects = new List<Rect>();
                var viewportTop = _scrollViewer?.VerticalOffset ?? 0;
                var viewportBottom = viewportTop + (_scrollViewer?.ViewportHeight ?? _textBox.ActualHeight);
                
                // Only process highlights that are likely to be visible or near the viewport
                var processedCount = 0;
                const int MAX_HIGHLIGHTS_TO_PROCESS = 500; // Limit for performance

                foreach (var (start, length) in ranges)
                {
                    if (processedCount >= MAX_HIGHLIGHTS_TO_PROCESS)
                        break;

                    if (start < 0 || length <= 0 || start >= _textBox.Text.Length)
                        continue;

                    try
                    {
                        var startPoint = _textBox.GetRectFromCharacterIndex(start);
                        
                        // Skip highlights that are far outside the viewport for performance
                        if (startPoint.Top < viewportTop - 1000 || startPoint.Top > viewportBottom + 1000)
                        {
                            continue;
                        }

                        var endPoint = _textBox.GetRectFromCharacterIndex(start + length);

                        // If the highlight spans multiple lines
                        if (startPoint.Top != endPoint.Top)
                        {
                            // Add rect for first line
                            rects.Add(new Rect(
                                startPoint.X,
                                startPoint.Y,
                                Math.Max(0, _textBox.ViewportWidth - startPoint.X),
                                startPoint.Height
                            ));

                            // Add rects for middle lines (if any)
                            double currentY = startPoint.Y + startPoint.Height;
                            while (currentY < endPoint.Y && rects.Count < MAX_HIGHLIGHTS_TO_PROCESS)
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
                            if (rects.Count < MAX_HIGHLIGHTS_TO_PROCESS)
                            {
                                rects.Add(new Rect(
                                    0,
                                    endPoint.Y,
                                    Math.Max(0, endPoint.X),
                                    endPoint.Height
                                ));
                            }
                        }
                        else
                        {
                            // Single line highlight
                            var width = Math.Max(0, endPoint.X - startPoint.X);
                            if (width > 0)
                            {
                                rects.Add(new Rect(
                                    startPoint.X,
                                    startPoint.Y,
                                    width,
                                    startPoint.Height
                                ));
                            }
                        }

                        processedCount++;
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
                    
                    Debug.WriteLine($"Created {rects.Count} highlight rectangles from {ranges.Count} ranges");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HighlightRanges: {ex}");
                ClearHighlights();
            }
        }

        private async void HighlightRangesAsync(List<(int start, int length)> ranges, Color highlightColor)
        {
            try
            {
                // Capture UI values on the UI thread before going async
                var textLength = _textBox.Text.Length;
                var viewportTop = _scrollViewer?.VerticalOffset ?? 0;
                var viewportBottom = viewportTop + (_scrollViewer?.ViewportHeight ?? _textBox.ActualHeight);
                
                // Process highlights on background thread to avoid UI blocking
                var validRanges = await Task.Run(() =>
                {
                    var validRangesList = new List<(int start, int length)>();
                    var processedCount = 0;
                    const int MAX_HIGHLIGHTS_TO_PROCESS = 200; // Lower limit for async processing

                    foreach (var (start, length) in ranges.Take(MAX_HIGHLIGHTS_TO_PROCESS))
                    {
                        if (start < 0 || length <= 0 || start >= textLength)
                            continue;

                        validRangesList.Add((start, Math.Min(length, textLength - start)));
                        processedCount++;
                    }

                    return validRangesList;
                });

                // Update UI on main thread
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        var finalRects = new List<Rect>();
                        
                        foreach (var (start, length) in validRanges)
                        {
                            try
                            {
                                var startPoint = _textBox.GetRectFromCharacterIndex(start);
                                var endPoint = _textBox.GetRectFromCharacterIndex(start + length);

                                if (startPoint.Top == endPoint.Top)
                                {
                                    // Single line highlight
                                    var width = Math.Max(0, endPoint.X - startPoint.X);
                                    if (width > 0)
                                    {
                                        finalRects.Add(new Rect(startPoint.X, startPoint.Y, width, startPoint.Height));
                                    }
                                }
                                else
                                {
                                    // Multi-line highlight - simplified for async processing
                                    finalRects.Add(new Rect(startPoint.X, startPoint.Y, 
                                        Math.Max(0, _textBox.ViewportWidth - startPoint.X), startPoint.Height));
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error processing highlight range ({start}, {length}): {ex}");
                            }
                        }

                        if (finalRects.Count > 0)
                        {
                            ClearHighlights();
                            var brush = new SolidColorBrush(highlightColor) { Opacity = 0.3 };
                            _currentAdorner = new TextHighlightAdorner(_textBox, finalRects, brush);
                            _adornerLayer.Add(_currentAdorner);
                            
                            Debug.WriteLine($"Created {finalRects.Count} async highlight rectangles from {validRanges.Count} ranges");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in async highlight UI update: {ex}");
                        ClearHighlights();
                    }
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in HighlightRangesAsync: {ex}");
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