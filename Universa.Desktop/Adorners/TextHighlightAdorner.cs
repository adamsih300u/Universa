using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Input;

namespace Universa.Desktop.Adorners
{
    public class TextHighlightAdorner : Adorner
    {
        private readonly List<Rect> _highlightRects;
        private readonly Brush _highlightBrush;

        public TextHighlightAdorner(TextBox adornedElement, List<Rect> highlightRects, Brush highlightBrush) 
            : base(adornedElement)
        {
            _highlightRects = highlightRects;
            _highlightBrush = highlightBrush;
            
            // Make the adorner input-transparent
            IsHitTestVisible = false;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            var textBox = (TextBox)AdornedElement;
            
            // Draw search highlights
            foreach (var rect in _highlightRects)
            {
                drawingContext.DrawRectangle(_highlightBrush, null, rect);
            }

            // Draw selection highlight if text is selected
            if (textBox.SelectionLength > 0)
            {
                try
                {
                    var selectionStart = textBox.SelectionStart;
                    var selectionLength = textBox.SelectionLength;
                    var startRect = textBox.GetRectFromCharacterIndex(selectionStart);
                    var endRect = textBox.GetRectFromCharacterIndex(selectionStart + selectionLength);

                    var selectionBrush = new SolidColorBrush(Colors.CornflowerBlue) { Opacity = 0.3 };

                    if (startRect.Top == endRect.Top)
                    {
                        // Single line selection
                        drawingContext.DrawRectangle(
                            selectionBrush,
                            null,
                            new Rect(
                                startRect.X,
                                startRect.Y,
                                endRect.X - startRect.X,
                                startRect.Height
                            )
                        );
                    }
                    else
                    {
                        // Multi-line selection
                        // First line
                        drawingContext.DrawRectangle(
                            selectionBrush,
                            null,
                            new Rect(
                                startRect.X,
                                startRect.Y,
                                textBox.ViewportWidth - startRect.X,
                                startRect.Height
                            )
                        );

                        // Middle lines
                        var currentY = startRect.Y + startRect.Height;
                        while (currentY < endRect.Y)
                        {
                            drawingContext.DrawRectangle(
                                selectionBrush,
                                null,
                                new Rect(
                                    0,
                                    currentY,
                                    textBox.ViewportWidth,
                                    startRect.Height
                                )
                            );
                            currentY += startRect.Height;
                        }

                        // Last line
                        drawingContext.DrawRectangle(
                            selectionBrush,
                            null,
                            new Rect(
                                0,
                                endRect.Y,
                                endRect.X,
                                endRect.Height
                            )
                        );
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error rendering selection highlight: {ex}");
                }
            }
        }
    }
} 