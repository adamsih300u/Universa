using System;
using System.Threading;
using System.Threading.Tasks;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Interface for managing org-mode folding operations with performance optimization
    /// </summary>
    public interface IOrgModeFoldingManager : IDisposable
    {
        /// <summary>
        /// Initializes folding for the given text editor
        /// </summary>
        void Initialize(TextEditor editor);
        
        /// <summary>
        /// Updates folding with throttling and performance optimization
        /// </summary>
        Task UpdateFoldingsAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Forces immediate folding update (for user-initiated actions)
        /// </summary>
        void ForceUpdateFoldings();
        
        /// <summary>
        /// Collapses all folding sections
        /// </summary>
        void CollapseAll();
        
        /// <summary>
        /// Expands all folding sections
        /// </summary>
        void ExpandAll();
        
        /// <summary>
        /// Toggles folding at cursor position
        /// </summary>
        bool TryToggleFoldingAtCursor(bool expand = true);
        
        /// <summary>
        /// Collapses folding at current cursor position
        /// </summary>
        void CollapseCurrentFolding();
        
        /// <summary>
        /// Expands folding at current cursor position
        /// </summary>
        void ExpandCurrentFolding();
        
        /// <summary>
        /// Handles Enter key on folded headers
        /// </summary>
        bool HandleEnterOnFoldedHeader();
        
        /// <summary>
        /// Event fired when folding state changes
        /// </summary>
        event EventHandler FoldingStateChanged;
    }
} 