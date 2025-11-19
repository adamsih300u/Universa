using System;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace Universa.Desktop.Interfaces
{
    /// <summary>
    /// Interface for handling org-mode UI events and keyboard shortcuts
    /// </summary>
    public interface IOrgModeUIEventHandler
    {
        /// <summary>
        /// Initializes the event handler for the given editor
        /// </summary>
        void Initialize(TextEditor editor);
        
        /// <summary>
        /// Event fired when a TODO state should be cycled
        /// </summary>
        event EventHandler<TodoStateCycleEventArgs> TodoStateCycleRequested;
        
        /// <summary>
        /// Event fired when tags should be cycled
        /// </summary>
        event EventHandler<TagCycleEventArgs> TagCycleRequested;
        
        /// <summary>
        /// Event fired when item should be refiled
        /// </summary>
        event EventHandler RefileRequested;
        
        /// <summary>
        /// Event fired when Enter is pressed on a folded header
        /// </summary>
        event EventHandler<FoldedHeaderEnterEventArgs> FoldedHeaderEnterRequested;
    }
    
    /// <summary>
    /// Event args for TODO state cycling
    /// </summary>
    public class TodoStateCycleEventArgs : EventArgs
    {
        public int CursorPosition { get; set; }
        public bool Handled { get; set; }
    }
    
    /// <summary>
    /// Event args for tag cycling
    /// </summary>
    public class TagCycleEventArgs : EventArgs
    {
        public int CursorPosition { get; set; }
        public bool Handled { get; set; }
    }
    
    /// <summary>
    /// Event args for folded header Enter handling
    /// </summary>
    public class FoldedHeaderEnterEventArgs : EventArgs
    {
        public int CursorPosition { get; set; }
        public bool Handled { get; set; }
    }
} 