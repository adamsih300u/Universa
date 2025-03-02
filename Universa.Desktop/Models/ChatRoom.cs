using System;

namespace Universa.Desktop.Models
{
    public class ChatRoom
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Topic { get; set; }
        public string Type { get; set; }  // Identifies the chat service type (e.g., "matrix", "irc")
        public int MemberCount { get; set; }
        public DateTime LastActivity { get; set; }

        public ChatRoom()
        {
            LastActivity = DateTime.Now;
        }

        public override string ToString()
        {
            return Name ?? Id;
        }
    }
} 