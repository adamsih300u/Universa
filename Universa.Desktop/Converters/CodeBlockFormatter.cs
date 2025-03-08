using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Universa.Desktop.Converters
{
    public class CodeBlockFormatter
    {
        // Regex pattern to match code blocks with optional language identifier
        private static readonly Regex _codeBlockPattern = new Regex(@"```(?<language>[a-zA-Z0-9]*)\r?\n(?<code>.*?)```", RegexOptions.Singleline);

        /// <summary>
        /// Formats the content by identifying code blocks and applying special formatting
        /// </summary>
        /// <param name="content">The message content</param>
        /// <param name="document">The FlowDocument to populate</param>
        public static void FormatContent(string content, FlowDocument document)
        {
            if (string.IsNullOrEmpty(content))
            {
                document.Blocks.Add(new Paragraph());
                return;
            }

            // Split the content by code blocks
            int lastIndex = 0;
            var matches = _codeBlockPattern.Matches(content);

            foreach (Match match in matches)
            {
                // Add text before the code block
                if (match.Index > lastIndex)
                {
                    string textBefore = content.Substring(lastIndex, match.Index - lastIndex);
                    AddTextParagraph(document, textBefore);
                }

                // Add the code block with special formatting
                string language = match.Groups["language"].Value;
                string code = match.Groups["code"].Value;
                AddCodeBlock(document, code, language);

                lastIndex = match.Index + match.Length;
            }

            // Add any remaining text after the last code block
            if (lastIndex < content.Length)
            {
                string remainingText = content.Substring(lastIndex);
                AddTextParagraph(document, remainingText);
            }
        }

        private static void AddTextParagraph(FlowDocument document, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run(text));
            document.Blocks.Add(paragraph);
        }

        private static void AddCodeBlock(FlowDocument document, string code, string language)
        {
            try
            {
                // Default colors
                var defaultCodeBackground = new SolidColorBrush(Color.FromRgb(40, 44, 52));
                var defaultCodeBorder = new SolidColorBrush(Color.FromRgb(60, 64, 72));
                var defaultCodeForeground = new SolidColorBrush(Colors.White);
                var defaultLanguageForeground = new SolidColorBrush(Color.FromRgb(180, 180, 180));
                
                // Try to get theme resources, fall back to defaults
                var codeBackground = TryFindResource("CodeBlockBackgroundBrush") as SolidColorBrush ?? defaultCodeBackground;
                var codeBorder = TryFindResource("CodeBlockBorderBrush") as SolidColorBrush ?? defaultCodeBorder;
                var codeForeground = TryFindResource("CodeBlockForegroundBrush") as SolidColorBrush ?? defaultCodeForeground;
                var languageForeground = TryFindResource("CodeBlockLanguageBrush") as SolidColorBrush ?? defaultLanguageForeground;

                // Create a border for the code block
                var border = new Border
                {
                    Background = codeBackground,
                    BorderBrush = codeBorder,
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10),
                    Margin = new Thickness(0, 5, 0, 5),
                    CornerRadius = new CornerRadius(3)
                };

                // Create a text block for the code
                var textBlock = new TextBlock
                {
                    Text = code,
                    FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                    Foreground = codeForeground,
                    TextWrapping = TextWrapping.Wrap
                };

                // Add language label if provided
                if (!string.IsNullOrEmpty(language))
                {
                    var languageLabel = new TextBlock
                    {
                        Text = language,
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 10,
                        Foreground = languageForeground,
                        Margin = new Thickness(0, 0, 0, 5)
                    };

                    var stackPanel = new StackPanel();
                    stackPanel.Children.Add(languageLabel);
                    stackPanel.Children.Add(textBlock);
                    border.Child = stackPanel;
                }
                else
                {
                    border.Child = textBlock;
                }

                // Add the border to a BlockUIContainer
                var container = new BlockUIContainer(border);
                document.Blocks.Add(container);
            }
            catch (Exception ex)
            {
                // Fallback to simple paragraph if UI elements can't be created
                var paragraph = new Paragraph();
                paragraph.Inlines.Add(new Run($"```{language}\n{code}\n```"));
                document.Blocks.Add(paragraph);
            }
        }

        private static object TryFindResource(string resourceKey)
        {
            try
            {
                if (Application.Current != null)
                {
                    return Application.Current.Resources[resourceKey];
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
} 