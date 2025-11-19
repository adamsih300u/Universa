using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Handles inline formatting for org-mode text (bold, italic, code, etc.)
    /// </summary>
    public class OrgModeInlineFormatter : DocumentColorizingTransformer
    {
        private static readonly List<FormattingRule> Rules = new List<FormattingRule>
        {
            // Bold: **text** - Updated regex for better handling
            new FormattingRule(@"\*\*(?=\S)([^*]|\*(?!\*))+(?<=\S)\*\*", FontWeights.Bold, Colors.White),
            
            // Italic: *text* - Updated regex to avoid conflicts with bold
            new FormattingRule(@"(?<![\*\\])\*(?=\S)([^*])+(?<=\S)\*(?!\*)", FontWeights.Normal, Colors.LightBlue, FontStyles.Italic),
            
            // Code: =text=
            new FormattingRule(@"=([^=]+)=", FontWeights.Normal, Colors.Orange, FontStyles.Normal, "Consolas"),
            
            // Verbatim: ~text~
            new FormattingRule(@"~([^~]+)~", FontWeights.Normal, Colors.LightGreen, FontStyles.Normal, "Consolas"),
            
            // Underlined: _text_ - Updated regex for consistency
            new FormattingRule(@"(?<![_\\])_(?=\S)([^_])+(?<=\S)_(?!_)", FontWeights.Normal, Colors.Yellow, FontStyles.Normal, null, true),
            
            // Strikethrough: +text+
            new FormattingRule(@"\+([^+]+)\+", FontWeights.Normal, Colors.Gray, FontStyles.Normal, null, false, true),
            
            // Links: [[url][description]] or [[url]]
            new FormattingRule(@"\[\[([^\]]+)\](?:\[([^\]]+)\])?\]", FontWeights.Normal, Colors.CornflowerBlue, FontStyles.Normal, null, true),
            
            // Timestamps: <2024-01-15> or <2024-01-15 Mon>
            new FormattingRule(@"<\d{4}-\d{2}-\d{2}(?:\s+\w{3})?(?:\s+\d{2}:\d{2}(?:-\d{2}:\d{2})?)?(?:\s+\+\d+[dwmy])*>", FontWeights.Normal, Colors.Green),
            
            // Tags: :tag1:tag2:
            new FormattingRule(@":[a-zA-Z_@#%][a-zA-Z0-9_@#%]*:", FontWeights.Normal, Colors.Purple),
            
            // TODO keywords (dynamic based on configuration)
            new FormattingRule(@"\b(?:TODO|NEXT|WAITING|PROJECT|DONE|CANCELLED)\b", FontWeights.Bold, Colors.Red)
        };

        // TODO keywords will be handled separately with dynamic colors
        private static readonly Regex TodoKeywordRegex = new Regex(
            @"(?<=^\*+\s+)(TODO|NEXT|STARTED|WAITING|DEFERRED|DONE|CANCELLED|PROJECT|SOMEDAY|DELEGATED)", 
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        protected override void ColorizeLine(DocumentLine line)
        {
            var lineText = CurrentContext.Document.GetText(line);
            
            // Handle TODO keywords with dynamic colors
            var todoMatches = TodoKeywordRegex.Matches(lineText);
            foreach (Match match in todoMatches)
            {
                var stateName = match.Value;
                var colorHex = ConfigurationProvider.Instance.GetStateColor(stateName);
                
                try
                {
                    var color = (Color)ColorConverter.ConvertFromString(colorHex);
                    ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(color));
                        
                        // Set font weight using Typeface
                        var currentTypeface = element.TextRunProperties.Typeface;
                        var newTypeface = new Typeface(
                            currentTypeface.FontFamily,
                            currentTypeface.Style,
                            FontWeights.Bold,
                            currentTypeface.Stretch
                        );
                        element.TextRunProperties.SetTypeface(newTypeface);
                    });
                }
                catch
                {
                    // Fallback to red if color parsing fails
                    ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(Colors.Red));
                        
                        // Set font weight using Typeface
                        var currentTypeface = element.TextRunProperties.Typeface;
                        var newTypeface = new Typeface(
                            currentTypeface.FontFamily,
                            currentTypeface.Style,
                            FontWeights.Bold,
                            currentTypeface.Stretch
                        );
                        element.TextRunProperties.SetTypeface(newTypeface);
                    });
                }
            }
            
            // Handle other formatting rules
            foreach (var rule in Rules)
            {
                var matches = rule.Regex.Matches(lineText);
                foreach (Match match in matches)
                {
                    ChangeLinePart(line.Offset + match.Index, line.Offset + match.Index + match.Length, element =>
                    {
                        element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(rule.Color));
                        
                        // Set font properties using Typeface
                        var currentTypeface = element.TextRunProperties.Typeface;
                        var fontFamily = !string.IsNullOrEmpty(rule.FontFamily) 
                            ? new FontFamily(rule.FontFamily) 
                            : currentTypeface.FontFamily;
                        
                        var newTypeface = new Typeface(
                            fontFamily,
                            rule.FontStyle,
                            rule.FontWeight,
                            currentTypeface.Stretch
                        );
                        element.TextRunProperties.SetTypeface(newTypeface);
                        
                        if (rule.IsUnderlined || rule.IsStrikethrough)
                        {
                            var decorations = new TextDecorationCollection();
                            if (rule.IsUnderlined)
                                decorations.Add(TextDecorations.Underline[0]);
                            if (rule.IsStrikethrough)
                                decorations.Add(TextDecorations.Strikethrough[0]);
                            element.TextRunProperties.SetTextDecorations(decorations);
                        }
                    });
                }
            }
        }
    }

    public class FormattingRule
    {
        public Regex Regex { get; }
        public FontWeight FontWeight { get; }
        public Color Color { get; }
        public FontStyle FontStyle { get; }
        public string FontFamily { get; }
        public bool IsUnderlined { get; }
        public bool IsStrikethrough { get; }

        public FormattingRule(string pattern, FontWeight fontWeight, Color color, 
                            FontStyle fontStyle = default, string fontFamily = null, 
                            bool isUnderlined = false, bool isStrikethrough = false)
        {
            Regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
            FontWeight = fontWeight;
            Color = color;
            FontStyle = fontStyle;
            FontFamily = fontFamily;
            IsUnderlined = isUnderlined;
            IsStrikethrough = isStrikethrough;
        }
    }
} 