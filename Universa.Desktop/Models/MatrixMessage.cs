using System;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace Universa.Desktop.Models
{
    public class MatrixMessage
    {
        public string Id { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string DisplayContent { get; set; }
        public string MessageType { get; set; }
        public JObject RawContent { get; set; }

        public MatrixMessage()
        {
            Timestamp = DateTime.Now;
            MessageType = "m.room.message";
            RawContent = new JObject();
        }

        public static MatrixMessage CreateUserMessage(string id, string sender, string content)
        {
            return new MatrixMessage
            {
                Id = id,
                Sender = FormatSender(sender),
                Content = content,
                DisplayContent = content,
                Timestamp = DateTime.Now,
                MessageType = "m.room.message",
                RawContent = new JObject
                {
                    ["msgtype"] = "m.text",
                    ["body"] = content
                }
            };
        }

        public static MatrixMessage FromMatrixEvent(string eventId, string sender, string type, JObject content)
        {
            var timestamp = DateTime.Now;

            // Try to get timestamp from the event content
            if (content.TryGetValue("origin_server_ts", out var tsToken))
            {
                var unixTimestamp = tsToken.Value<long>();
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp).LocalDateTime;
            }
            else if (content.Parent is JObject eventObj && eventObj.TryGetValue("origin_server_ts", out tsToken))
            {
                // Try to get timestamp from parent event object if not in content
                var unixTimestamp = tsToken.Value<long>();
                timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp).LocalDateTime;
            }

            var message = new MatrixMessage
            {
                Id = eventId,
                Sender = FormatSender(sender),
                MessageType = type,
                RawContent = content,
                Content = content?["body"]?.ToString() ?? "",
                DisplayContent = content?["formatted_body"]?.ToString() ?? content?["body"]?.ToString() ?? "",
                Timestamp = timestamp
            };

            Debug.WriteLine($"Created message: ID={message.Id}, Sender={message.Sender}, Content={message.Content}, Time={message.Timestamp}");
            return message;
        }

        private static string FormatSender(string sender)
        {
            if (string.IsNullOrEmpty(sender)) return "";
            if (sender.StartsWith("@"))
            {
                var colonIndex = sender.IndexOf(':');
                if (colonIndex > 0)
                {
                    return sender.Substring(1, colonIndex - 1);
                }
            }
            return sender;
        }
    }
} 