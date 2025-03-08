using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;

namespace Universa.Desktop.Converters
{
    /// <summary>
    /// Markup extension that formats code blocks in a RichTextBox
    /// </summary>
    public class CodeBlockFormatterExtension : MarkupExtension
    {
        /// <summary>
        /// The content to format
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public CodeBlockFormatterExtension()
        {
        }

        /// <summary>
        /// Constructor with content
        /// </summary>
        /// <param name="content">The content to format</param>
        public CodeBlockFormatterExtension(string content)
        {
            Content = content;
        }

        /// <summary>
        /// Provides the formatted FlowDocument
        /// </summary>
        /// <param name="serviceProvider">The service provider</param>
        /// <returns>A FlowDocument with formatted code blocks</returns>
        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var document = new FlowDocument();
            
            // Apply code block formatting
            CodeBlockFormatter.FormatContent(Content, document);
            
            return document;
        }
    }
} 