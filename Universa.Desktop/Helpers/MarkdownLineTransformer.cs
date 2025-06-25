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
        private static readonly Regex BoldRegex = new Regex(@"\*\*([^*]+)\*\*|__([^_]+)__", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new Regex(@"\*([^*]+)\*|_([^_]+)_", RegexOptions.Compiled);
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
                var level = match.Groups[1].Value.Length;
                var brush = GetHeaderBrush(level);
                var fontSize = GetHeaderFontSize(level);
                
                // Color the entire header line
                ChangeLinePart(line.Offset, line.EndOffset, element =>
                {
                    element.TextRunProperties.SetForegroundBrush(brush);
                    element.TextRunProperties.SetFontRenderingEmSize(fontSize);
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
            
            // Bold formatting
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
            
            // Italic formatting
            foreach (Match match in ItalicRegex.Matches(lineText))
            {
                // Skip if this match is inside a bold match
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
        
        private Brush GetHeaderBrush(int level)
        {
            return level switch
            {
                1 => new SolidColorBrush(Color.FromRgb(46, 134, 171)),   // Blue
                2 => new SolidColorBrush(Color.FromRgb(162, 59, 114)),   // Purple
                3 => new SolidColorBrush(Color.FromRgb(241, 143, 1)),    // Orange
                4 => new SolidColorBrush(Color.FromRgb(199, 62, 29)),    // Red
                5 => new SolidColorBrush(Color.FromRgb(89, 46, 131)),    // Dark Purple
                6 => new SolidColorBrush(Color.FromRgb(74, 74, 74)),     // Gray
                _ => new SolidColorBrush(Colors.Black)
            };
        }
        
        private double GetHeaderFontSize(int level)
        {
            return level switch
            {
                1 => 18.0,
                2 => 16.0,
                3 => 14.0,
                4 => 12.0,
                _ => 11.0
            };
        }
    }
} 