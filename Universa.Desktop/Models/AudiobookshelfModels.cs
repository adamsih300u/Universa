using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Universa.Desktop.Models
{
    // Authentication Models
    public class LoginRequest
    {
        [JsonPropertyName("username")]
        public string Username { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; }
    }

    public class LoginResponse
    {
        [JsonPropertyName("user")]
        public UserInfo User { get; set; }
    }

    public class UserInfo
    {
        [JsonPropertyName("token")]
        public string Token { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; }
    }

    // Library Models
    public class LibrariesResponse
    {
        [JsonPropertyName("libraries")]
        public List<AudiobookshelfLibraryResponse> Libraries { get; set; } = new List<AudiobookshelfLibraryResponse>();
    }

    public class AudiobookshelfLibraryResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("mediaType")]
        public string MediaType { get; set; }

        [JsonPropertyName("provider")]
        public string Provider { get; set; }

        [JsonPropertyName("displayOrder")]
        public int DisplayOrder { get; set; }

        [JsonPropertyName("icon")]
        public string Icon { get; set; }

        [JsonPropertyName("createdAt")]
        public long CreatedAt { get; set; }

        [JsonPropertyName("lastUpdate")]
        public long LastUpdate { get; set; }
    }

    // Items Models
    public class ItemsResponse
    {
        [JsonPropertyName("results")]
        public List<AudiobookItemResponse> Results { get; set; } = new List<AudiobookItemResponse>();

        [JsonPropertyName("total")]
        public int Total { get; set; }

        [JsonPropertyName("limit")]
        public int Limit { get; set; }

        [JsonPropertyName("page")]
        public int Page { get; set; }
    }

    public class AudiobookItemResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("libraryItemId")]
        public string LibraryItemId { get; set; }

        [JsonPropertyName("libraryId")]
        public string LibraryId { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("author")]
        public string Author { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("mediaType")]
        public string MediaType { get; set; }

        [JsonPropertyName("media")]
        public MediaInfo Media { get; set; }

        [JsonPropertyName("series")]
        public List<SeriesInfo> Series { get; set; }
    }

    public class MediaInfo
    {
        [JsonPropertyName("metadata")]
        public MetadataInfo Metadata { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("tracks")]
        public List<AudiobookshelfTrackInfo> Tracks { get; set; }
    }

    public class MetadataInfo
    {
        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("authorName")]
        public string AuthorName { get; set; }

        [JsonPropertyName("narratorName")]
        public string NarratorName { get; set; }

        [JsonPropertyName("series")]
        public List<SeriesInfo> Series { get; set; }

        [JsonPropertyName("publishedYear")]
        public string PublishedYear { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("isbn")]
        public string Isbn { get; set; }

        [JsonPropertyName("language")]
        public string Language { get; set; }

        [JsonPropertyName("explicit")]
        public bool Explicit { get; set; }
    }

    public class SeriesInfo
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("sequence")]
        public string Sequence { get; set; }
    }

    public class AudiobookshelfTrackInfo
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("startOffset")]
        public double StartOffset { get; set; }

        [JsonPropertyName("duration")]
        public double Duration { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; }

        [JsonPropertyName("contentUrl")]
        public string ContentUrl { get; set; }

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; }

        [JsonPropertyName("filename")]
        public string Filename { get; set; }
    }

    // Progress Models
    public class UserResponse
    {
        [JsonPropertyName("mediaProgress")]
        public List<MediaProgress> MediaProgress { get; set; } = new List<MediaProgress>();
    }

    public class MediaProgress
    {
        [JsonPropertyName("libraryItemId")]
        public string LibraryItemId { get; set; }

        [JsonPropertyName("progress")]
        public double Progress { get; set; }

        [JsonPropertyName("currentTime")]
        public double CurrentTime { get; set; }

        [JsonPropertyName("isFinished")]
        public bool IsFinished { get; set; }

        [JsonPropertyName("lastUpdate")]
        public long LastUpdate { get; set; }
    }

    // In-Progress Items Models
    public class InProgressItemsResponse
    {
        [JsonPropertyName("libraryItems")]
        public List<AudiobookItemResponse> LibraryItems { get; set; } = new List<AudiobookItemResponse>();
    }
} 