using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Universa.Desktop.Services.Export
{
    /// <summary>
    /// Interface for document exporters
    /// </summary>
    public interface IExporter
    {
        /// <summary>
        /// Exports the document content to the specified format
        /// </summary>
        /// <param name="content">The document content to export</param>
        /// <param name="options">Export options</param>
        /// <returns>True if the export was successful, false otherwise</returns>
        Task<bool> ExportAsync(string content, ExportOptions options);
    }
} 