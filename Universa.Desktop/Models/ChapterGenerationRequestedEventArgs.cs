using System;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Event arguments for chapter generation requests with cursor context support
    /// </summary>
    public class ChapterGenerationRequestedEventArgs : EventArgs
    {
        public int ChapterNumber { get; set; }
        public string ChapterTitle { get; set; }
        public string ChapterSummary { get; set; }
        public string ExistingContent { get; set; }
        public string OutlineContent { get; set; }
        public bool IsCompleteManuscript { get; set; }
        
        /// <summary>
        /// Cursor position in the document for context-aware generation
        /// This is automatically used by the Fiction Writing Beta chain to provide:
        /// - Previous chapter content for continuity
        /// - Current chapter context around cursor position  
        /// - Next chapter content for forward planning
        /// </summary>
        public int? CursorPosition { get; set; }
        
        /// <summary>
        /// Whether to use cursor context for generation
        /// When true (default), provides rich context around cursor position
        /// When false, uses simplified context for bulk generation
        /// </summary>
        public bool UseCursorContext { get; set; } = true;
    }
} 