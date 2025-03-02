using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Placeholder exporter for DOCX format
    /// </summary>
    public class DocxExporter : IExporter
    {
        /// <summary>
        /// Exports the document content to DOCX format
        /// </summary>
        public Task<bool> ExportAsync(string content, ExportOptions options)
        {
            // Show a message to the user that this feature is coming soon
            MessageBox.Show(
                "DOCX export is not yet implemented. This feature will be available in a future update.",
                "Feature Coming Soon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
                
            return Task.FromResult(false);
        }
    }
} 