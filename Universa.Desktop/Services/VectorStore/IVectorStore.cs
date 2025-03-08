using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Interface for vector storage and similarity search
    /// </summary>
    public interface IVectorStore : IDisposable
    {
        /// <summary>
        /// Ensures a collection exists in the database
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>True if the collection exists or was created successfully</returns>
        Task<bool> EnsureCollectionExistsAsync(string collectionName);

        /// <summary>
        /// Adds a vector item to a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="item">Vector item to add</param>
        Task AddItemAsync(string collectionName, VectorItem item);

        /// <summary>
        /// Adds multiple vector items to a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="items">Vector items to add</param>
        Task AddItemsAsync(string collectionName, List<VectorItem> items);

        /// <summary>
        /// Searches for similar vectors in a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="queryEmbedding">Query embedding vector</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <param name="filter">Optional filter criteria</param>
        /// <returns>List of search results ordered by similarity</returns>
        Task<List<SearchResult>> SearchAsync(
            string collectionName,
            float[] queryEmbedding,
            int limit = 10,
            object filter = null);

        /// <summary>
        /// Deletes items from a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="ids">IDs of items to delete</param>
        Task DeleteItemsAsync(string collectionName, List<string> ids);

        /// <summary>
        /// Gets an item from a collection by ID
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="id">ID of the item to get</param>
        /// <returns>The vector item if found, null otherwise</returns>
        Task<VectorItem> GetItemAsync(string collectionName, string id);

        /// <summary>
        /// Gets the number of items in a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>Number of items in the collection</returns>
        Task<int> GetCollectionSizeAsync(string collectionName);

        /// <summary>
        /// Lists all collections in the database
        /// </summary>
        /// <returns>List of collection names</returns>
        Task<List<string>> ListCollectionsAsync();

        /// <summary>
        /// Deletes a collection and all its items
        /// </summary>
        /// <param name="collectionName">Name of the collection to delete</param>
        Task DeleteCollectionAsync(string collectionName);

        /// <summary>
        /// Checks if the database is accessible
        /// </summary>
        /// <returns>True if the database is accessible</returns>
        bool IsDatabaseAccessible();

        /// <summary>
        /// Creates a test item in the database to verify functionality
        /// </summary>
        /// <returns>True if the test was successful</returns>
        Task<bool> CreateTestItemAsync();
    }
} 