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
    /// Provides syntax highlighting for markdown files
    /// </summary>
    public static class MarkdownSyntaxHighlighting
    {
        /// <summary>
        /// Creates a syntax highlighting definition for markdown
        /// </summary>
        public static IHighlightingDefinition CreateMarkdownHighlighting()
        {
            try
            {
                var xshd = CreateMarkdownXshd();
                using (var reader = new StringReader(xshd))
                using (var xmlReader = XmlReader.Create(reader))
                {
                    return HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error creating markdown highlighting: {ex.Message}");
                return null; // Fall back to no highlighting
            }
        }
        
        private static string CreateMarkdownXshd()
        {
            return @"<?xml version=""1.0""?>
<SyntaxDefinition name=""Markdown"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
    <Color name=""Header1"" foreground=""#2E86AB"" fontWeight=""bold"" fontSize=""18"" />
    <Color name=""Header2"" foreground=""#A23B72"" fontWeight=""bold"" fontSize=""16"" />
    <Color name=""Header3"" foreground=""#F18F01"" fontWeight=""bold"" fontSize=""14"" />
    <Color name=""Header4"" foreground=""#C73E1D"" fontWeight=""bold"" fontSize=""12"" />
    <Color name=""Header5"" foreground=""#592E83"" fontWeight=""bold"" />
    <Color name=""Header6"" foreground=""#4A4A4A"" fontWeight=""bold"" />
    <Color name=""Bold"" fontWeight=""bold"" />
    <Color name=""Italic"" fontStyle=""italic"" />
    <Color name=""Code"" foreground=""#D63384"" fontFamily=""Consolas"" background=""#F8F9FA"" />
    <Color name=""Link"" foreground=""#0D6EFD"" textDecorations=""Underline"" />
    <Color name=""Quote"" foreground=""#6C757D"" fontStyle=""italic"" />
    <Color name=""ListItem"" foreground=""#495057"" />
    
    <RuleSet>
        <!-- Headers -->
        <Rule color=""Header1"">
            ^#{1}\s+.*$
        </Rule>
        <Rule color=""Header2"">
            ^#{2}\s+.*$
        </Rule>
        <Rule color=""Header3"">
            ^#{3}\s+.*$
        </Rule>
        <Rule color=""Header4"">
            ^#{4}\s+.*$
        </Rule>
        <Rule color=""Header5"">
            ^#{5}\s+.*$
        </Rule>
        <Rule color=""Header6"">
            ^#{6}\s+.*$
        </Rule>
        
        <!-- Bold text -->
        <Rule color=""Bold"">
            \*\*[^*]+\*\*
        </Rule>
        <Rule color=""Bold"">
            __[^_]+__
        </Rule>
        
        <!-- Italic text -->
        <Rule color=""Italic"">
            \*[^*]+\*
        </Rule>
        <Rule color=""Italic"">
            _[^_]+_
        </Rule>
        
        <!-- Inline code -->
        <Rule color=""Code"">
            `[^`]+`
        </Rule>
        
        <!-- Links -->
        <Rule color=""Link"">
            \[[^\]]*\]\([^)]*\)
        </Rule>
        
        <!-- Blockquotes -->
        <Rule color=""Quote"">
            ^&gt;.*$
        </Rule>
        
        <!-- List items -->
        <Rule color=""ListItem"">
            ^\s*[-*+]\s+.*$
        </Rule>
        <Rule color=""ListItem"">
            ^\s*\d+\.\s+.*$
        </Rule>
    </RuleSet>
</SyntaxDefinition>";
        }
    }
} 