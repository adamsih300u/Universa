using System;

namespace Universa.Desktop.Models
{
    public class MatrixRoom
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Topic { get; set; }
        public int MemberCount { get; set; }
        public DateTime LastActivity { get; set; }

        public MatrixRoom(string id, string name, string topic = null)
        {
            Id = id;
            Name = name;
            Topic = topic;
        }
    }
} 