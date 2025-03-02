using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Placeholder exporter for PDF format
    /// </summary>
    public class PdfExporter : IExporter
    {
        /// <summary>
        /// Exports the document content to PDF format
        /// </summary>
        public Task<bool> ExportAsync(string content, ExportOptions options)
        {
            // Show a message to the user that this feature is coming soon
            MessageBox.Show(
                "PDF export is not yet implemented. This feature will be available in a future update.",
                "Feature Coming Soon",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
                
            return Task.FromResult(false);
        }
    }
} 