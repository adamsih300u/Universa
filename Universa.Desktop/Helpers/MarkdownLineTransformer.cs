using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Line transformer for markdown that provides enhanced visual formatting
    /// </summary>
    public class MarkdownLineTransformer : DocumentColorizingTransformer
    {
        private static readonly Regex HeaderRegex = new Regex(@"^(#{1,6})\s+(.*)$", RegexOptions.Compiled);
        // Updated regex patterns for better bold/italic handling
        private static readonly Regex BoldRegex = new Regex(@"\*\*(?=\S)([^*]|\*(?!\*))+(?<=\S)\*\*|__(?=\S)([^_]|_(?!_))+(?<=\S)__", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"(?<![\*\\])\*(?=\S)([^*])+(?<=\S)\*(?!\*)|(?<![_\\])_(?=\S)([^_])+(?<=\S)_(?!_)", RegexOptions.Compiled);
        private static readonly Regex CodeRegex = new Regex(@"`([^`]+)`", RegexOptions.Compiled);
        private static readonly Regex LinkRegex = new Regex(@"\[([^\]]*)\]\(([^)]*)\)", RegexOptions.Compiled);
        
        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);
            
            // Apply header formatting
            ApplyHeaderFormatting(line, lineText);
            
            // Apply inline formatting
            ApplyInlineFormatting(line, lineText);
        }
        
        private void ApplyHeaderFormatting(DocumentLine line, string lineText)
        {
            var match = HeaderRegex.Match(lineText);
            if (match.Success)
            {
                var lineStartOffset = line.Offset;
                var headerLevel = match.Groups[1].Value.Length;
                
                // Apply different colors and sizes based on header level
                Color headerColor;
                double fontSize = 12;
                
                switch (headerLevel)
                {
                    case 1:
                        headerColor = Color.FromRgb(46, 134, 171); // #2E86AB
                        fontSize = 18;
                        break;
                    case 2:
                        headerColor = Color.FromRgb(162, 59, 114); // #A23B72
                        fontSize = 16;
                        break;
                    case 3:
                        headerColor = Color.FromRgb(241, 143, 1); // #F18F01
                        fontSize = 14;
                        break;
                    case 4:
                        headerColor = Color.FromRgb(199, 62, 29); // #C73E1D
                        fontSize = 12;
                        break;
                    case 5:
                        headerColor = Color.FromRgb(89, 46, 131); // #592E83
                        break;
                    case 6:
                        headerColor = Color.FromRgb(74, 74, 74); // #4A4A4A
                        break;
                    default:
                        headerColor = Colors.Black;
                        break;
                }
                
                ChangeLinePart(lineStartOffset, line.EndOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(headerColor));
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal));
                });
            }
        }
        
        private void ApplyInlineFormatting(DocumentLine line, string lineText)
        {
            var lineStartOffset = line.Offset;
            
            // Apply bold formatting first (higher precedence)
            foreach (Match match in BoldRegex.Matches(lineText))
            {
                var startOffset = lineStartOffset + match.Index;
                var endOffset = startOffset + match.Length;
                
                ChangeLinePart(startOffset, endOffset, element =>
                {
                    element.TextRunProperties.SetTypeface(new Typeface(
                        element.TextRunProperties.Typeface.FontFamily,
                        FontStyles.Normal,
                        FontWeights.Bold,
                        FontStretches.Normal));
                });
            }
            
            // Apply italic formatting, but skip areas already marked as bold
            foreach (Match match in ItalicRegex.Matches(lineText))
            {
                // Check if this italic match is inside a bold match
                bool insideBold = false;
                foreach (Match boldMatch in BoldRegex.Matches(lineText))
                {
                    if (match.Index >= boldMatch.Index && match.Index + match.Length <= boldMatch.Index + boldMatch.Length)
                    {
                        insideBold = true;
                        break;
                    }
                }
                
                if (!insideBold)
                {
                    var startOffset = lineStartOffset + match.Index;
                    var endOffset = startOffset + match.Length;
                    
                    ChangeLinePart(startOffset, endOffset, element =>
                    {
                        element.TextRunProperties.SetTypeface(new Typeface(
                            element.TextRunProperties.Typeface.FontFamily,
                            FontStyles.Italic,
                            FontWeights.Normal,
                            FontStretches.Normal));
                    });
                }
            }
            
            // Code formatting
            foreach (Match match in CodeRegex.Matches(lineText))
            {
                var startOffset = lineStartOffset + match.Index;
                var endOffset = startOffset + match.Length;
                
                ChangeLinePart(startOffset, endOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(214, 51, 132)));
                    element.TextRunProperties.SetBackgroundBrush(new SolidColorBrush(Color.FromRgb(248, 249, 250)));
                    element.TextRunProperties.SetTypeface(new Typeface("Consolas, Courier New, monospace"));
                });
            }
            
            // Link formatting
            foreach (Match match in LinkRegex.Matches(lineText))
            {
                var startOffset = lineStartOffset + match.Index;
                var endOffset = startOffset + match.Length;
                
                ChangeLinePart(startOffset, endOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Color.FromRgb(13, 110, 253)));
                    element.TextRunProperties.SetTextDecorations(TextDecorations.Underline);
                });
            }
        }
    }
} 