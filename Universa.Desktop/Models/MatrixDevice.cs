using System;
using Newtonsoft.Json;
using Universa.Desktop.Helpers;

namespace Universa.Desktop.Models
{
    public class MatrixDevice
    {
        [JsonProperty("device_id")]
        public string DeviceId { get; set; }

        [JsonProperty("display_name")]
        public string DisplayName { get; set; }

        [JsonProperty("last_seen_ip")]
        public string LastSeenIp { get; set; }

        [JsonProperty("last_seen_ts")]
        public long? LastSeenTimestamp { get; set; }

        public DateTime LastSeen
        {
            get
            {
                if (!LastSeenTimestamp.HasValue || LastSeenTimestamp.Value == 0)
                    return DateTime.MinValue;
                
                try
                {
                    return TimeZoneHelper.FromUnixTimeMilliseconds(LastSeenTimestamp.Value);
                }
                catch
                {
                    return DateTime.MinValue;
                }
            }
        }

        [JsonProperty("user_id")]
        public string UserId { get; set; }

        public override string ToString()
        {
            return $"{DisplayName ?? "Unknown Device"} ({DeviceId})";
        }
    }
} 