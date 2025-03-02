using System.Collections.Generic;

namespace Universa.Desktop.Models
{
    public class TabInfo
    {
        public string Type { get; set; }  // "Markdown", "Editor", "Folder", "Music", "Media", "Chat", "RSS"
        public string Title { get; set; }
        public string Path { get; set; }  // Optional, for file/folder based tabs
    }

    public class TabState
    {
        public List<TabInfo> Tabs { get; set; }
        public int SelectedIndex { get; set; }
    }
} 