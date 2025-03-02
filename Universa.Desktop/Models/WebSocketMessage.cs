using System.Text.Json.Serialization;

namespace Universa.Desktop.Models
{
    public class WebSocketMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }

        [JsonPropertyName("payload")]
        public object Payload { get; set; }
    }

    public static class WebSocketMessageTypes
    {
        public const string GetFileList = "getFileList";
        public const string FileList = "fileList";
        public const string FileChanged = "fileChanged";
        public const string FileDeleted = "fileDeleted";
    }

    public class FileChangeNotification
    {
        [JsonPropertyName("file")]
        public FileMetadata File { get; set; }
    }

    public class FileDeleteNotification
    {
        [JsonPropertyName("relativePath")]
        public string RelativePath { get; set; }
    }

    public class FileListResponse
    {
        [JsonPropertyName("files")]
        public FileMetadata[] Files { get; set; }
    }
} 