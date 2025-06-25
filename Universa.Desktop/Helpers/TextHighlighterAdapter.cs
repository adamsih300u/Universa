using System.Windows.Media;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Helpers
{
    public class TextHighlighterAdapter : ITextHighlighter
    {
        private readonly TextHighlighter _textHighlighter;

        public TextHighlighterAdapter(TextHighlighter textHighlighter)
        {
            _textHighlighter = textHighlighter ?? throw new System.ArgumentNullException(nameof(textHighlighter));
        }

        public void ClearHighlights()
        {
            _textHighlighter?.ClearHighlights();
        }

        public void HighlightText(string text, Color color)
        {
            _textHighlighter?.HighlightText(text, color);
        }
    }
} 