using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Represents the type of editing operation
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum EditOperationType
    {
        /// <summary>Replace existing text with new text</summary>
        Replace,
        
        /// <summary>Insert new text after anchor text</summary>
        Insert,
        
        /// <summary>Delete existing text</summary>
        Delete,
        
        /// <summary>Generate new text (for continuing scenes, new chapters, etc.)</summary>
        Generate
    }

    /// <summary>
    /// Represents a single editing operation
    /// </summary>
    public class FictionEditOperation
    {
        /// <summary>
        /// The type of operation to perform
        /// </summary>
        [JsonPropertyName("operation")]
        public EditOperationType Operation { get; set; }

        /// <summary>
        /// For Replace/Delete: The exact text to find and replace/delete
        /// </summary>
        [JsonPropertyName("original")]
        public string Original { get; set; }

        /// <summary>
        /// For Replace: The new text to replace with
        /// </summary>
        [JsonPropertyName("changed")]
        public string Changed { get; set; }

        /// <summary>
        /// For Insert: The exact text to insert after
        /// </summary>
        [JsonPropertyName("anchor")]
        public string Anchor { get; set; }

        /// <summary>
        /// For Insert/Generate: The new text to insert or generate
        /// </summary>
        [JsonPropertyName("new")]
        public string New { get; set; }

        /// <summary>
        /// Optional explanation for the edit
        /// </summary>
        [JsonPropertyName("explanation")]
        public string Explanation { get; set; }
    }

    /// <summary>
    /// Represents the type of response from the LLM
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ResponseType
    {
        /// <summary>Plain text response (analysis, questions, new content)</summary>
        Text,
        
        /// <summary>Structured editing operations</summary>
        Edits
    }

    /// <summary>
    /// Represents the structured response from the LLM
    /// </summary>
    public class FictionEditResponse
    {
        /// <summary>
        /// The type of response
        /// </summary>
        [JsonPropertyName("response_type")]
        public ResponseType ResponseType { get; set; }

        /// <summary>
        /// Plain text response (used when response_type is Text)
        /// </summary>
        [JsonPropertyName("text")]
        public string Text { get; set; }

        /// <summary>
        /// List of editing operations (used when response_type is Edits)
        /// </summary>
        [JsonPropertyName("edits")]
        public List<FictionEditOperation> Edits { get; set; }

        /// <summary>
        /// Optional commentary to display before edits
        /// </summary>
        [JsonPropertyName("commentary")]
        public string Commentary { get; set; }
    }
}





