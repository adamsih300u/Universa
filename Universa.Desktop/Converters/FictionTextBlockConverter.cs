using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Universa.Desktop.Models;

namespace Universa.Desktop.Converters
{
    public class FictionTextBlock
    {
        public string Text { get; set; }
        public string OriginalText { get; set; }
        public string ChangedText { get; set; }
        public string AnchorText { get; set; }
        public string NewText { get; set; }
        public bool IsCodeBlock { get; set; }
        public bool IsInsertion { get; set; }
    }

    public class FictionTextBlockConverter : IValueConverter
    {
        private static readonly Regex _originalTextPattern = new Regex(
            @"```\s*\r?\nOriginal text:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nChanged to:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
        private static readonly Regex _insertionPattern = new Regex(
            @"```\s*\r?\nInsert after:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nNew text:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string content && !string.IsNullOrEmpty(content))
            {
                return ParseFictionContent(content);
            }
            return new List<FictionTextBlock>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private List<FictionTextBlock> ParseFictionContent(string content)
        {
            var blocks = new List<FictionTextBlock>();
            var allMatches = new List<(int Index, int Length, FictionTextBlock Block)>();
            
            // Find all revision matches
            var revisionMatches = _originalTextPattern.Matches(content);
            foreach (Match match in revisionMatches)
            {
                string originalText = match.Groups[1].Value.Trim();
                string changedText = match.Groups[2].Value.Trim();
                
                allMatches.Add((match.Index, match.Length, new FictionTextBlock
                {
                    OriginalText = originalText,
                    ChangedText = changedText,
                    IsCodeBlock = true,
                    IsInsertion = false
                }));
            }
            
            // Find all insertion matches
            var insertionMatches = _insertionPattern.Matches(content);
            foreach (Match match in insertionMatches)
            {
                string anchorText = match.Groups[1].Value.Trim();
                string newText = match.Groups[2].Value.Trim();
                
                allMatches.Add((match.Index, match.Length, new FictionTextBlock
                {
                    AnchorText = anchorText,
                    NewText = newText,
                    IsCodeBlock = true,
                    IsInsertion = true
                }));
            }
            
            // Sort matches by position
            allMatches.Sort((a, b) => a.Index.CompareTo(b.Index));
            
            int lastIndex = 0;
            foreach (var (index, length, block) in allMatches)
            {
                // Add text before the code block
                if (index > lastIndex)
                {
                    string textBefore = content.Substring(lastIndex, index - lastIndex).Trim();
                    if (!string.IsNullOrEmpty(textBefore))
                    {
                        blocks.Add(new FictionTextBlock
                        {
                            Text = textBefore,
                            IsCodeBlock = false,
                            IsInsertion = false
                        });
                    }
                }

                // Add the code block
                blocks.Add(block);
                lastIndex = index + length;
            }

            // Add any remaining text after the last code block
            if (lastIndex < content.Length)
            {
                string remainingText = content.Substring(lastIndex).Trim();
                if (!string.IsNullOrEmpty(remainingText))
                {
                    blocks.Add(new FictionTextBlock
                    {
                        Text = remainingText,
                        IsCodeBlock = false,
                        IsInsertion = false
                    });
                }
            }

            // If no code blocks were found, return the entire content as a single text block
            if (blocks.Count == 0)
            {
                blocks.Add(new FictionTextBlock
                {
                    Text = content,
                    IsCodeBlock = false,
                    IsInsertion = false
                });
            }

            return blocks;
        }
    }
} 