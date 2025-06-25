using System;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using System.Xml;
using System.IO;
using System.Text;

namespace Universa.Desktop.Helpers
{
    /// <summary>
    /// Provides syntax highlighting for org-mode files
    /// </summary>
    public static class OrgModeSyntaxHighlighting
    {
        /// <summary>
        /// Creates a syntax highlighting definition for org-mode
        /// </summary>
        public static IHighlightingDefinition CreateOrgModeHighlighting()
        {
            // Temporarily disable syntax highlighting to prevent infinite loop crashes
            // TODO: Implement safer syntax highlighting patterns in the future
            return null;
        }
        
        private static string CreateOrgModeXshd()
        {
            // Minimal, safe syntax definition with no rules to prevent crashes
            return @"<?xml version=""1.0""?>
<SyntaxDefinition name=""OrgMode"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Default"" foreground=""Black"" />
    <RuleSet>
        <!-- No highlighting rules to prevent infinite loops -->
    </RuleSet>
</SyntaxDefinition>";
        }
        
        /// <summary>
        /// Gets colors for different org-mode elements based on current theme
        /// </summary>
        public static class OrgModeColors
        {
            public static SolidColorBrush Header1 => new SolidColorBrush(Color.FromRgb(0xFF, 0x57, 0x33));
            public static SolidColorBrush Header2 => new SolidColorBrush(Color.FromRgb(0xFF, 0x8C, 0x42));
            public static SolidColorBrush Header3 => new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
            public static SolidColorBrush Header4 => new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));
            public static SolidColorBrush Header5 => new SolidColorBrush(Color.FromRgb(0xAD, 0xFF, 0x2F));
            public static SolidColorBrush Header6 => new SolidColorBrush(Color.FromRgb(0x32, 0xCD, 0x32));
            
            public static SolidColorBrush TodoState => new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B));
            public static SolidColorBrush DoneState => new SolidColorBrush(Color.FromRgb(0x4E, 0xCD, 0xC4));
            public static SolidColorBrush NextState => new SolidColorBrush(Color.FromRgb(0x45, 0xB7, 0xD1));
            public static SolidColorBrush WaitingState => new SolidColorBrush(Color.FromRgb(0xF7, 0xB7, 0x31));
            public static SolidColorBrush ProjectState => new SolidColorBrush(Color.FromRgb(0xA5, 0x5E, 0xEA));
            public static SolidColorBrush CancelledState => new SolidColorBrush(Color.FromRgb(0x77, 0x8C, 0xA3));
            
            public static SolidColorBrush Priority => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            public static SolidColorBrush Tag => new SolidColorBrush(Color.FromRgb(0x9B, 0x59, 0xB6));
            public static SolidColorBrush Link => new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB));
            public static SolidColorBrush Date => new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
            public static SolidColorBrush Comment => new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            public static SolidColorBrush Code => new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22));
        }
    }
} 