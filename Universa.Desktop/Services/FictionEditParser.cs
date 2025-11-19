using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Universa.Desktop.Converters;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Parses LLM responses into fiction edit operations
    /// Supports both legacy markdown format and new JSON structured output
    /// </summary>
    public static class FictionEditParser
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        /// <summary>
        /// Attempts to parse the response as structured JSON first, falls back to markdown parsing
        /// </summary>
        public static List<FictionTextBlock> Parse(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                return new List<FictionTextBlock>();
            }

            // Try to parse as JSON first
            var jsonBlocks = TryParseJson(content);
            if (jsonBlocks != null && jsonBlocks.Count > 0)
            {
                Debug.WriteLine("Successfully parsed response as structured JSON");
                return jsonBlocks;
            }

            // Fall back to markdown parsing for backward compatibility
            Debug.WriteLine("Falling back to markdown parsing");
            return ParseMarkdown(content);
        }

        /// <summary>
        /// Attempts to parse the content as JSON structured output
        /// </summary>
        private static List<FictionTextBlock> TryParseJson(string content)
        {
            try
            {
                // Strip markdown code block markers if present
                content = Regex.Replace(content, @"```json\s*", "", RegexOptions.IgnoreCase);
                content = Regex.Replace(content, @"```\s*$", "", RegexOptions.Multiline);
                
                // Look for JSON object in the content
                var jsonMatch = Regex.Match(content, @"\{[\s\S]*""response_type""[\s\S]*\}", RegexOptions.Multiline);
                if (!jsonMatch.Success)
                {
                    return null;
                }

                var jsonString = jsonMatch.Value;
                
                // Try to auto-complete incomplete JSON from streaming
                jsonString = TryCompleteJson(jsonString);
                
                var response = JsonSerializer.Deserialize<FictionEditResponse>(jsonString, JsonOptions);

                if (response == null)
                {
                    return null;
                }

                var blocks = new List<FictionTextBlock>();

                // Handle text-only responses
                if (response.ResponseType == ResponseType.Text)
                {
                    if (!string.IsNullOrEmpty(response.Text))
                    {
                        blocks.Add(new FictionTextBlock
                        {
                            Text = response.Text,
                            IsCodeBlock = false,
                            IsInsertion = false
                        });
                    }
                    return blocks;
                }

                // Handle edit responses
                if (response.ResponseType == ResponseType.Edits)
                {
                    // Add commentary if present
                    if (!string.IsNullOrEmpty(response.Commentary))
                    {
                        blocks.Add(new FictionTextBlock
                        {
                            Text = response.Commentary,
                            IsCodeBlock = false,
                            IsInsertion = false
                        });
                    }

                    // Convert each edit operation to FictionTextBlock
                    if (response.Edits != null)
                    {
                        foreach (var edit in response.Edits)
                        {
                            var block = ConvertEditToBlock(edit);
                            if (block != null)
                            {
                                blocks.Add(block);
                            }
                        }
                    }
                }

                return blocks;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"JSON parsing failed: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error parsing JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Converts a FictionEditOperation to a FictionTextBlock
        /// </summary>
        private static FictionTextBlock ConvertEditToBlock(FictionEditOperation edit)
        {
            switch (edit.Operation)
            {
                case EditOperationType.Replace:
                    return new FictionTextBlock
                    {
                        OriginalText = edit.Original?.Trim(),
                        ChangedText = edit.Changed?.Trim(),
                        IsCodeBlock = true,
                        IsInsertion = false,
                        Text = !string.IsNullOrEmpty(edit.Explanation) 
                            ? $"**Replace:** {edit.Explanation}" 
                            : null
                    };

                case EditOperationType.Delete:
                    return new FictionTextBlock
                    {
                        OriginalText = edit.Original?.Trim(),
                        ChangedText = "", // Empty string for deletion
                        IsCodeBlock = true,
                        IsInsertion = false,
                        Text = !string.IsNullOrEmpty(edit.Explanation)
                            ? $"**Delete:** {edit.Explanation}"
                            : "**Delete this text**"
                    };

                case EditOperationType.Insert:
                    return new FictionTextBlock
                    {
                        AnchorText = edit.Anchor?.Trim(),
                        NewText = edit.New?.Trim(),
                        IsCodeBlock = true,
                        IsInsertion = true,
                        Text = !string.IsNullOrEmpty(edit.Explanation)
                            ? $"**Insert:** {edit.Explanation}"
                            : null
                    };

                case EditOperationType.Generate:
                    return new FictionTextBlock
                    {
                        Text = edit.New?.Trim(),
                        IsCodeBlock = false,
                        IsInsertion = false
                    };

                default:
                    Debug.WriteLine($"Unknown operation type: {edit.Operation}");
                    return null;
            }
        }

        /// <summary>
        /// Attempts to complete incomplete JSON by closing open brackets/braces
        /// </summary>
        private static string TryCompleteJson(string jsonString)
        {
            if (string.IsNullOrEmpty(jsonString))
                return jsonString;

            // Count open/close brackets and braces
            int openBraces = jsonString.Count(c => c == '{');
            int closeBraces = jsonString.Count(c => c == '}');
            int openBrackets = jsonString.Count(c => c == '[');
            int closeBrackets = jsonString.Count(c => c == ']');

            // If already balanced, return as-is
            if (openBraces == closeBraces && openBrackets == closeBrackets)
                return jsonString;

            Debug.WriteLine($"JSON incomplete: braces {openBraces}/{closeBraces}, brackets {openBrackets}/{closeBrackets}");
            Debug.WriteLine("Attempting to auto-complete JSON...");

            var completed = new System.Text.StringBuilder(jsonString);

            // Close arrays first (inner structures)
            while (openBrackets > closeBrackets)
            {
                completed.Append("\n  ]");
                closeBrackets++;
            }

            // Then close objects
            while (openBraces > closeBraces)
            {
                completed.Append("\n}");
                closeBraces++;
            }

            Debug.WriteLine("JSON auto-completed successfully");
            return completed.ToString();
        }

        /// <summary>
        /// Parses markdown-formatted content (legacy format)
        /// </summary>
        private static List<FictionTextBlock> ParseMarkdown(string content)
        {
            // Call the internal parsing method directly to avoid circular reference
            return FictionTextBlockConverter.ParseFictionContentStatic(content);
        }
    }
}

