using System;
using System.Threading.Tasks;

namespace Universa.Desktop.Interfaces
{
    public interface IFileTab
    {
        string FilePath { get; set; }
        string Title { get; set; }
        bool IsModified { get; set; }
        
        /// <summary>
        /// Gets the last known cursor position for AI context awareness
        /// </summary>
        int LastKnownCursorPosition { get; }
        
        Task<bool> Save();
        Task<bool> SaveAs(string newPath = null);
        void Reload();
        
        /// <summary>
        /// Gets the content of the file tab
        /// </summary>
        /// <returns>The content as a string</returns>
        string GetContent();

        /// <summary>
        /// Called when the tab is selected
        /// </summary>
        void OnTabSelected();

        /// <summary>
        /// Called when the tab is deselected
        /// </summary>
        void OnTabDeselected();
    }
} 