using System.Text.Json.Serialization;

namespace Universa.Desktop.Models
{
    public class LibraryResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("mediaType")]
        public string MediaType { get; set; }

        public override string ToString()
        {
            return Name;
        }
    }
} 