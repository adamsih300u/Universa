using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class ContentBlock
    {
        public string Text { get; set; }
        public CodeBlock CodeBlock { get; set; }
        public bool HasCodeBlock => CodeBlock != null;
    }

    public class ChatMessage
    {
        public string Role { get; set; }
        public string Content { get; set; }
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; }
        public string Model { get; set; }
        public List<ContentBlock> ContentBlocks { get; private set; }

        public ChatMessage(string role, string content, bool isUser = false)
        {
            Role = role;
            Content = content;
            IsUser = isUser;
            Timestamp = DateTime.Now;
            UpdateContentBlocks();
        }

        private void UpdateContentBlocks()
        {
            ContentBlocks = new List<ContentBlock>();
            if (Content == null) return;

            var codeBlockPattern = @"```\s*\r?\nOriginal code:\r?\n(.*?)\r?\n```\s*\r?\n```\s*\r?\nChanged to:\r?\n(.*?)\r?\n```";
            var parts = Regex.Split(Content, codeBlockPattern, RegexOptions.Singleline);

            for (int i = 0; i < parts.Length; i++)
            {
                // Add text content
                if (!string.IsNullOrWhiteSpace(parts[i]))
                {
                    ContentBlocks.Add(new ContentBlock { Text = parts[i].Trim() });
                }

                // If we have a code block match (will be in the next two groups)
                if (i + 2 < parts.Length)
                {
                    var originalText = parts[i + 1];
                    var newText = parts[i + 2];
                    if (!string.IsNullOrWhiteSpace(originalText) && !string.IsNullOrWhiteSpace(newText))
                    {
                        var codeBlock = new CodeBlock
                        {
                            OriginalText = originalText.Trim(),
                            Code = newText.Trim(),
                            FullMatch = $"```\nOriginal code:\n{originalText}\n```\n\n```\nChanged to:\n{newText}\n```"
                        };
                        ContentBlocks.Add(new ContentBlock 
                        { 
                            Text = codeBlock.FullMatch,
                            CodeBlock = codeBlock
                        });
                    }
                    i += 2; // Skip the two captured groups
                }
            }
        }
    }
} 