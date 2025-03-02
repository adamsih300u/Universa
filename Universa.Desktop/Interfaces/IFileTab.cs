using System;
using System.Threading.Tasks;

namespace Universa.Desktop.Interfaces
{
    public interface IFileTab
    {
        string FilePath { get; set; }
        bool IsModified { get; set; }
        Task<bool> Save();
        Task<bool> SaveAs(string newPath = null);
        void Reload();
        
        /// <summary>
        /// Gets the content of the file tab
        /// </summary>
        /// <returns>The content as a string</returns>
        string GetContent();
    }
} 