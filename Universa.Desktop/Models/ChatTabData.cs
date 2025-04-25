using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Serializable model representing chat tab data for persistence
    /// </summary>
    [Serializable]
    public class ChatTabData
    {
        /// <summary>
        /// The name of the tab
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Whether the tab is in context mode
        /// </summary>
        public bool IsContextMode { get; set; }
        
        /// <summary>
        /// The selected model's name
        /// </summary>
        public string ModelName { get; set; }
        
        /// <summary>
        /// The selected model's provider
        /// </summary>
        public string ModelProvider { get; set; }
        
        /// <summary>
        /// The input text content
        /// </summary>
        public string InputText { get; set; }
        
        /// <summary>
        /// Messages in the tab's main message collection
        /// </summary>
        public List<Models.ChatMessage> Messages { get; set; }
        
        /// <summary>
        /// Messages in the tab's chat mode message collection
        /// </summary>
        public List<Models.ChatMessage> ChatModeMessages { get; set; }
        
        public ChatTabData()
        {
            Messages = new List<Models.ChatMessage>();
            ChatModeMessages = new List<Models.ChatMessage>();
        }
    }
    
    /// <summary>
    /// Container for all saved chat sessions
    /// </summary>
    [Serializable]
    public class ChatSessionData
    {
        /// <summary>
        /// List of all chat tabs
        /// </summary>
        public List<ChatTabData> Tabs { get; set; }
        
        /// <summary>
        /// Index of the selected tab
        /// </summary>
        public int SelectedTabIndex { get; set; }
        
        public ChatSessionData()
        {
            Tabs = new List<ChatTabData>();
            SelectedTabIndex = 0;
        }
    }
} 