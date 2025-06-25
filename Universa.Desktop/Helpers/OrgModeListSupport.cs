using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Handles org-mode list functionality including checkboxes
    /// </summary>
    public static class OrgModeListSupport
    {
        private static readonly Regex UnorderedListRegex = new Regex(@"^(\s*)[-+*]\s+(.*)", RegexOptions.Compiled);
        private static readonly Regex OrderedListRegex = new Regex(@"^(\s*)(\d+)\.\s+(.*)", RegexOptions.Compiled);
        private static readonly Regex CheckboxListRegex = new Regex(@"^(\s*)[-+*]\s+(\[[ Xx-]\])\s+(.*)", RegexOptions.Compiled);
        
        /// <summary>
        /// Toggles checkbox state on the current line
        /// </summary>
        public static bool ToggleCheckbox(TextEditor editor)
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            var lineText = editor.Document.GetText(line);
            var match = CheckboxListRegex.Match(lineText);
            
            if (match.Success)
            {
                var indent = match.Groups[1].Value;
                var currentState = match.Groups[2].Value;
                var content = match.Groups[3].Value;
                
                // Cycle through checkbox states: [ ] → [X] → [-] → [ ]
                var newState = currentState switch
                {
                    "[ ]" => "[X]",  // Unchecked → Checked
                    "[X]" => "[-]",  // Checked → Partial/Cancelled
                    "[-]" => "[ ]",  // Partial → Unchecked
                    "[x]" => "[-]",  // Alternative checked → Partial
                    _ => "[X]"       // Unknown → Checked
                };
                
                var newLineText = $"{indent}- {newState} {content}";
                
                editor.Document.Replace(line.Offset, line.Length, newLineText);
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Creates a new list item below the current one
        /// </summary>
        public static bool CreateNewListItem(TextEditor editor)
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            var lineText = editor.Document.GetText(line);
            
            // Check for unordered list
            var unorderedMatch = UnorderedListRegex.Match(lineText);
            if (unorderedMatch.Success)
            {
                var indent = unorderedMatch.Groups[1].Value;
                var marker = lineText.Contains("- [ ]") || lineText.Contains("- [X]") || lineText.Contains("- [-]") 
                    ? "- [ ]" : "-";
                
                var newLine = $"\n{indent}{marker} ";
                editor.Document.Insert(line.EndOffset, newLine);
                editor.CaretOffset = line.EndOffset + newLine.Length;
                return true;
            }
            
            // Check for ordered list
            var orderedMatch = OrderedListRegex.Match(lineText);
            if (orderedMatch.Success)
            {
                var indent = orderedMatch.Groups[1].Value;
                var nextNumber = int.Parse(orderedMatch.Groups[2].Value) + 1;
                
                var newLine = $"\n{indent}{nextNumber}. ";
                editor.Document.Insert(line.EndOffset, newLine);
                editor.CaretOffset = line.EndOffset + newLine.Length;
                return true;
            }
            
            // Check for checkbox list
            var checkboxMatch = CheckboxListRegex.Match(lineText);
            if (checkboxMatch.Success)
            {
                var indent = checkboxMatch.Groups[1].Value;
                var newLine = $"\n{indent}- [ ] ";
                editor.Document.Insert(line.EndOffset, newLine);
                editor.CaretOffset = line.EndOffset + newLine.Length;
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Indents or outdents the current list item
        /// </summary>
        public static bool IndentListItem(TextEditor editor, bool indent)
        {
            var line = editor.Document.GetLineByOffset(editor.CaretOffset);
            var lineText = editor.Document.GetText(line);
            
            if (UnorderedListRegex.IsMatch(lineText) || 
                OrderedListRegex.IsMatch(lineText) || 
                CheckboxListRegex.IsMatch(lineText))
            {
                if (indent)
                {
                    // Add two spaces at the beginning
                    editor.Document.Insert(line.Offset, "  ");
                }
                else
                {
                    // Remove two spaces from the beginning if they exist
                    if (lineText.StartsWith("  "))
                    {
                        editor.Document.Remove(line.Offset, 2);
                    }
                }
                return true;
            }
            
            return false;
        }
        
        /// <summary>
        /// Gets the list information for a line
        /// </summary>
        public static ListInfo GetListInfo(string lineText)
        {
            var unorderedMatch = UnorderedListRegex.Match(lineText);
            if (unorderedMatch.Success)
            {
                return new ListInfo
                {
                    Type = ListType.Unordered,
                    Indent = unorderedMatch.Groups[1].Value.Length,
                    Content = unorderedMatch.Groups[2].Value
                };
            }
            
            var orderedMatch = OrderedListRegex.Match(lineText);
            if (orderedMatch.Success)
            {
                return new ListInfo
                {
                    Type = ListType.Ordered,
                    Indent = orderedMatch.Groups[1].Value.Length,
                    Number = int.Parse(orderedMatch.Groups[2].Value),
                    Content = orderedMatch.Groups[3].Value
                };
            }
            
            var checkboxMatch = CheckboxListRegex.Match(lineText);
            if (checkboxMatch.Success)
            {
                var state = checkboxMatch.Groups[2].Value;
                return new ListInfo
                {
                    Type = ListType.Checkbox,
                    Indent = checkboxMatch.Groups[1].Value.Length,
                    CheckboxState = state switch
                    {
                        "[ ]" => CheckboxState.Unchecked,
                        "[X]" or "[x]" => CheckboxState.Checked,
                        "[-]" => CheckboxState.Partial,
                        _ => CheckboxState.Unchecked
                    },
                    Content = checkboxMatch.Groups[3].Value
                };
            }
            
            return null;
        }
    }
    
    public enum ListType
    {
        Unordered,
        Ordered,
        Checkbox
    }
    
    public enum CheckboxState
    {
        Unchecked,
        Checked,
        Partial
    }
    
    public class ListInfo
    {
        public ListType Type { get; set; }
        public int Indent { get; set; }
        public int? Number { get; set; }
        public CheckboxState? CheckboxState { get; set; }
        public string Content { get; set; }
    }
} 