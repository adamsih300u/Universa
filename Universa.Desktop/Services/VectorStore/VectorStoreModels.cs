using System;
using System.Collections.Generic;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Represents an item to be stored in the vector database
    /// </summary>
    public class VectorItem
    {
        /// <summary>
        /// Unique identifier for the item
        /// </summary>
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Vector embedding for the item
        /// </summary>
        public float[] Embedding { get; set; }

        /// <summary>
        /// Metadata associated with the item
        /// </summary>
        public Dictionary<string, object> Metadata { get; set; } = new Dictionary<string, object>();

        /// <summary>
        /// Default constructor
        /// </summary>
        public VectorItem() { }

        /// <summary>
        /// Creates a new vector item with the specified embedding and metadata
        /// </summary>
        /// <param name="embedding">Vector embedding</param>
        /// <param name="metadata">Optional metadata</param>
        public VectorItem(float[] embedding, Dictionary<string, object> metadata = null)
        {
            Embedding = embedding;
            if (metadata != null)
            {
                Metadata = metadata;
            }
        }
    }

    /// <summary>
    /// Represents a search result from the vector database
    /// </summary>
    public class SearchResult
    {
        /// <summary>
        /// Unique identifier of the item
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Similarity score (higher is more similar)
        /// </summary>
        public float Score { get; set; }

        /// <summary>
        /// Metadata associated with the item
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    }
} 