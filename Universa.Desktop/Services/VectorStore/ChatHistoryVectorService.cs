using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Services.ML;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Service for storing and retrieving chat history using vector search
    /// </summary>
    public class ChatHistoryVectorService
    {
        private readonly IVectorStore _vectorDb;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _collectionName = "chat_history";
        private readonly Configuration _config;

        /// <summary>
        /// Creates a new instance of the ChatHistoryVectorService
        /// </summary>
        /// <param name="vectorDb">Vector database service</param>
        public ChatHistoryVectorService(IVectorStore vectorDb)
        {
            _vectorDb = vectorDb;
            _embeddingService = ServiceLocator.Instance.GetService<IEmbeddingService>();
            _config = Configuration.Instance;
            
            Debug.WriteLine($"Initialized ChatHistoryVectorService with EnableLocalEmbeddings={_config.EnableLocalEmbeddings}");
            
            // Ensure collection exists if local embeddings are enabled
            if (_config.EnableLocalEmbeddings)
            {
                Task.Run(async () => 
                {
                    try
                    {
                        await _vectorDb.EnsureCollectionExistsAsync(_collectionName);
                        Debug.WriteLine($"Ensured collection exists: {_collectionName}");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error ensuring collection exists: {ex.Message}");
                    }
                });
            }
        }

        /// <summary>
        /// Stores a chat message in the vector database
        /// </summary>
        /// <param name="message">Chat message to store</param>
        /// <returns>ID of the stored message</returns>
        public async Task<string> StoreMessageAsync(ChatMessage message)
        {
            try
            {
                if (!_config.EnableLocalEmbeddings)
                {
                    Debug.WriteLine("Local embeddings are disabled, skipping vector storage");
                    return null;
                }

                // Generate embeddings for the message content
                var embedding = await _embeddingService.GenerateEmbeddingAsync(message.Content);

                // Create metadata for the message
                var metadata = new Dictionary<string, object>
                {
                    ["role"] = message.Role,
                    ["timestamp"] = message.Timestamp.ToString("o"),
                    ["content"] = message.Content
                };

                if (!string.IsNullOrEmpty(message.ModelName))
                {
                    metadata["model"] = message.ModelName;
                }

                // Create vector item
                var vectorItem = new VectorItem(embedding, metadata)
                {
                    Id = Guid.NewGuid().ToString() // Generate a new ID for each message
                };

                // Store in vector database
                await _vectorDb.AddItemAsync(_collectionName, vectorItem);
                Debug.WriteLine($"Stored chat message in vector database with ID {vectorItem.Id}");

                return vectorItem.Id;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error storing chat message in vector database: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Finds similar messages to the query
        /// </summary>
        /// <param name="query">Query text</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <param name="roleFilter">Optional role filter (user, assistant, system)</param>
        /// <returns>List of similar messages</returns>
        public async Task<List<ChatMessage>> FindSimilarMessagesAsync(string query, int limit = 5, string roleFilter = null)
        {
            try
            {
                if (!_config.EnableLocalEmbeddings)
                {
                    Debug.WriteLine("Local embeddings are disabled, skipping vector search");
                    return new List<ChatMessage>();
                }

                // Generate embeddings for the query
                var embedding = await _embeddingService.GenerateEmbeddingAsync(query);

                // Search for similar messages
                var results = await _vectorDb.SearchAsync(_collectionName, embedding, limit);

                // Convert search results to chat messages
                return results.Select(r => new ChatMessage
                {
                    Role = r.Metadata.TryGetValue("role", out var role) ? role : "unknown",
                    Content = r.Metadata.TryGetValue("content", out var content) ? content : "",
                    ModelName = r.Metadata.TryGetValue("model", out var model) ? model : null,
                    Timestamp = r.Metadata.TryGetValue("timestamp", out var timestamp) 
                        ? DateTime.Parse(timestamp) 
                        : DateTime.Now,
                    Score = r.Score
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding similar messages: {ex.Message}");
                return new List<ChatMessage>();
            }
        }

        /// <summary>
        /// Gets the total number of messages in the chat history
        /// </summary>
        /// <returns>Number of messages</returns>
        public async Task<int> GetMessageCountAsync()
        {
            try
            {
                return await _vectorDb.GetCollectionSizeAsync(_collectionName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting message count: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Clears all chat history
        /// </summary>
        public async Task ClearHistoryAsync()
        {
            try
            {
                await _vectorDb.DeleteCollectionAsync(_collectionName);
                Debug.WriteLine("Cleared chat history");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing chat history: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Chat message model for vector storage
    /// </summary>
    public class ChatMessage
    {
        /// <summary>
        /// Role of the message sender (user, assistant, system)
        /// </summary>
        public string Role { get; set; }

        /// <summary>
        /// Content of the message
        /// </summary>
        public string Content { get; set; }

        /// <summary>
        /// Name of the model that generated the message (for assistant messages)
        /// </summary>
        public string ModelName { get; set; }

        /// <summary>
        /// Timestamp when the message was created
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Similarity score (for search results)
        /// </summary>
        public float Score { get; set; }
    }
} 