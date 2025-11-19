using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

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
        // Pattern for code-block wrapped revisions
        private static readonly Regex _originalTextPattern = new Regex(
            @"```\s*\r?\nOriginal text:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nChanged to:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
        // Pattern for code-block wrapped insertions
        private static readonly Regex _insertionPattern = new Regex(
            @"```\s*\r?\nInsert after:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nNew text:\r?\n(.*?)\r?\n```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
        // Pattern for plain text revisions (no code blocks)
        private static readonly Regex _plainRevisionPattern = new Regex(
            @"Original text:\r?\n(.*?)\r?\n\r?\nChanged to:\r?\n(.*?)(?=\r?\n\r?\n|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
        // Pattern for plain text insertions (no code blocks)
        private static readonly Regex _plainInsertionPattern = new Regex(
            @"Insert after:\r?\n(.*?)\r?\n\r?\nNew text:\r?\n(.*?)(?=\r?\n\r?\n|$)",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string content && !string.IsNullOrEmpty(content))
            {
                // Use the new FictionEditParser which handles both JSON and markdown
                return FictionEditParser.Parse(content);
            }
            return new List<FictionTextBlock>();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Static method for parsing markdown content without going through the converter
        /// Used by FictionEditParser to avoid circular reference
        /// </summary>
        public static List<FictionTextBlock> ParseFictionContentStatic(string content)
        {
            var instance = new FictionTextBlockConverter();
            return instance.ParseFictionContent(content);
        }

        private List<FictionTextBlock> ParseFictionContent(string content)
        {
            var blocks = new List<FictionTextBlock>();
            var allMatches = new List<(int Index, int Length, FictionTextBlock Block)>();
            
            // Find all code-block wrapped revision matches
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
            
            // Find all code-block wrapped insertion matches
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
            
            // Find all plain text revision matches (no code blocks)
            var plainRevisionMatches = _plainRevisionPattern.Matches(content);
            foreach (Match match in plainRevisionMatches)
            {
                // Skip if this area is already covered by a code block match
                bool overlaps = allMatches.Any(m => 
                    match.Index < m.Index + m.Length && 
                    match.Index + match.Length > m.Index);
                
                if (!overlaps)
                {
                    string originalText = match.Groups[1].Value.Trim();
                    string changedText = match.Groups[2].Value.Trim();
                    
                    allMatches.Add((match.Index, match.Length, new FictionTextBlock
                    {
                        OriginalText = originalText,
                        ChangedText = changedText,
                        IsCodeBlock = false,
                        IsInsertion = false
                    }));
                }
            }
            
            // Find all plain text insertion matches (no code blocks)
            var plainInsertionMatches = _plainInsertionPattern.Matches(content);
            foreach (Match match in plainInsertionMatches)
            {
                // Skip if this area is already covered by a code block match
                bool overlaps = allMatches.Any(m => 
                    match.Index < m.Index + m.Length && 
                    match.Index + match.Length > m.Index);
                
                if (!overlaps)
                {
                    string anchorText = match.Groups[1].Value.Trim();
                    string newText = match.Groups[2].Value.Trim();
                    
                    allMatches.Add((match.Index, match.Length, new FictionTextBlock
                    {
                        AnchorText = anchorText,
                        NewText = newText,
                        IsCodeBlock = false,
                        IsInsertion = true
                    }));
                }
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