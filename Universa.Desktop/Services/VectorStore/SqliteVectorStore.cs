using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SQLite;

namespace Universa.Desktop.Services.VectorStore
{
    public class SqliteVectorStore : IVectorStore
    {
        private readonly string _dbPath;
        private SQLiteAsyncConnection _database;
        private bool _disposed = false;
        private readonly int _vectorSize = 384; // Size of embeddings from all-MiniLM-L6-v2

        /// <summary>
        /// Creates a new instance of the SqliteVectorStore
        /// </summary>
        /// <param name="applicationDataPath">Base path for storing SQLite data</param>
        public SqliteVectorStore(string applicationDataPath)
        {
            try
            {
                // Check SQLite version to ensure the library is loaded
                Debug.WriteLine($"Initializing SqliteVectorStore");
                
                // Create directory for SQLite data
                _dbPath = Path.Combine(applicationDataPath, "VectorDB", "vectors.db");
                var directory = Path.GetDirectoryName(_dbPath);
                
                Debug.WriteLine($"Vector database path: {_dbPath}");
                Debug.WriteLine($"Vector database directory: {directory}");
                
                // Check if directory exists before creating
                bool dirExists = Directory.Exists(directory);
                Debug.WriteLine($"Vector database directory exists before creation: {dirExists}");
                
                if (!dirExists)
                {
                    Directory.CreateDirectory(directory);
                    Debug.WriteLine($"Created vector database directory: {directory}");
                    
                    // Verify directory was created
                    dirExists = Directory.Exists(directory);
                    Debug.WriteLine($"Vector database directory exists after creation: {dirExists}");
                }

                // Check if database file exists before connection
                bool dbExists = File.Exists(_dbPath);
                Debug.WriteLine($"Database file exists before connection: {dbExists}");

                // Initialize database connection
                Debug.WriteLine("Creating SQLiteAsyncConnection");
                _database = new SQLiteAsyncConnection(_dbPath);
                Debug.WriteLine("SQLiteAsyncConnection created successfully");
                
                // Initialize database schema asynchronously
                Debug.WriteLine("Initializing database schema asynchronously");
                
                // Start initialization in the background
                Task.Run(async () => 
                {
                    try 
                    {
                        // Use a timeout to prevent indefinite hanging
                        var initTask = InitializeDatabaseAsync();
                        var completedTask = await Task.WhenAny(initTask, Task.Delay(TimeSpan.FromSeconds(10)));
                        
                        if (completedTask == initTask)
                        {
                            // Task completed normally
                            await initTask; // Propagate any exceptions
                            Debug.WriteLine("Database initialization completed successfully");
                            
                            // Create a test item to verify database functionality
                            Debug.WriteLine("Creating test item to verify database functionality");
                            bool testResult = await CreateTestItemAsync();
                            Debug.WriteLine($"Test item creation result: {testResult}");
                        }
                        else
                        {
                            // Task timed out
                            Debug.WriteLine("WARNING: Database initialization timed out after 10 seconds");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error in background initialization: {ex.Message}");
                        Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    }
                });
                
                Debug.WriteLine($"SqliteVectorStore initialization started in background");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing SqliteVectorStore: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private async Task InitializeDatabaseAsync()
        {
            try
            {
                Debug.WriteLine("Starting database initialization");
                
                // Enable WAL mode for better performance
                Debug.WriteLine("Enabling Write-Ahead Logging");
                var walTask = _database.EnableWriteAheadLoggingAsync();
                var walCompleted = await Task.WhenAny(walTask, Task.Delay(TimeSpan.FromSeconds(5)));
                
                if (walCompleted == walTask)
                {
                    await walTask; // Propagate any exceptions
                    Debug.WriteLine("Write-Ahead Logging enabled successfully");
                }
                else
                {
                    Debug.WriteLine("WARNING: Enabling Write-Ahead Logging timed out after 5 seconds");
                    // Continue with initialization anyway
                }
                
                // Create Collections table
                Debug.WriteLine("Creating Collections table");
                await _database.CreateTableAsync<Collection>();
                Debug.WriteLine("Collections table created successfully");
                
                // Create VectorItems table
                Debug.WriteLine("Creating VectorItemEntity table");
                await _database.CreateTableAsync<VectorItemEntity>();
                Debug.WriteLine("VectorItemEntity table created successfully");
                
                // Check if database file exists after table creation
                bool dbExists = File.Exists(_dbPath);
                Debug.WriteLine($"Database file exists after table creation: {dbExists}");
                
                if (dbExists)
                {
                    var fileInfo = new FileInfo(_dbPath);
                    Debug.WriteLine($"Database file size after table creation: {fileInfo.Length} bytes");
                }
                
                // Register custom functions for vector similarity
                RegisterCustomFunctions();
                
                Debug.WriteLine("Database schema initialization completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing database schema: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private void RegisterCustomFunctions()
        {
            // SQLite-net-pcl doesn't directly support custom functions like System.Data.SQLite
            // We'll implement similarity calculations in C# code instead
            Debug.WriteLine("Custom vector functions will be handled in C# code");
        }

        /// <summary>
        /// Ensures a collection exists in the database
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>True if the collection exists or was created successfully</returns>
        public async Task<bool> EnsureCollectionExistsAsync(string collectionName)
        {
            try
            {
                var collection = await _database.Table<Collection>()
                    .Where(c => c.Name == collectionName)
                    .FirstOrDefaultAsync();

                if (collection == null)
                {
                    await _database.InsertAsync(new Collection { Name = collectionName });
                    Debug.WriteLine($"Created new collection: {collectionName}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error ensuring collection exists: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds a vector item to a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="item">Vector item to add</param>
        public async Task AddItemAsync(string collectionName, VectorItem item)
        {
            try
            {
                // Ensure collection exists
                await EnsureCollectionExistsAsync(collectionName);

                // Convert VectorItem to VectorItemEntity
                var entity = new VectorItemEntity
                {
                    Id = item.Id,
                    CollectionName = collectionName,
                    Embedding = SerializeVector(item.Embedding),
                    Metadata = JsonConvert.SerializeObject(item.Metadata)
                };

                // Insert or replace the item
                await _database.InsertOrReplaceAsync(entity);
                Debug.WriteLine($"Added item {item.Id} to collection {collectionName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding item to collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Adds multiple vector items to a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="items">Vector items to add</param>
        public async Task AddItemsAsync(string collectionName, List<VectorItem> items)
        {
            try
            {
                // Ensure collection exists
                await EnsureCollectionExistsAsync(collectionName);

                // Convert VectorItems to VectorItemEntities
                var entities = items.Select(item => new VectorItemEntity
                {
                    Id = item.Id,
                    CollectionName = collectionName,
                    Embedding = SerializeVector(item.Embedding),
                    Metadata = JsonConvert.SerializeObject(item.Metadata)
                }).ToList();

                // Insert all items in a transaction
                await _database.RunInTransactionAsync(db =>
                {
                    foreach (var entity in entities)
                    {
                        db.InsertOrReplace(entity);
                    }
                });

                Debug.WriteLine($"Added {items.Count} items to collection {collectionName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding items to collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Searches for similar vectors in a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection to search</param>
        /// <param name="queryEmbedding">Query embedding vector</param>
        /// <param name="limit">Maximum number of results to return</param>
        /// <param name="filter">Optional filter criteria</param>
        /// <returns>List of search results ordered by similarity</returns>
        public async Task<List<SearchResult>> SearchAsync(
            string collectionName,
            float[] queryEmbedding,
            int limit = 10,
            object filter = null)
        {
            try
            {
                // Get all items in the collection
                var entities = await _database.Table<VectorItemEntity>()
                    .Where(i => i.CollectionName == collectionName)
                    .ToListAsync();

                // Calculate similarity for each item
                var results = new List<SearchResult>();
                foreach (var entity in entities)
                {
                    var embedding = DeserializeVector(entity.Embedding);
                    var similarity = CosineSimilarity(queryEmbedding, embedding);
                    
                    // Parse metadata
                    Dictionary<string, string> metadata = new Dictionary<string, string>();
                    try
                    {
                        var jsonMetadata = JsonConvert.DeserializeObject<Dictionary<string, object>>(entity.Metadata);
                        if (jsonMetadata != null)
                        {
                            metadata = jsonMetadata.ToDictionary(
                                kv => kv.Key,
                                kv => kv.Value?.ToString() ?? string.Empty
                            );
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing metadata: {ex.Message}");
                    }
                    
                    results.Add(new SearchResult
                    {
                        Id = entity.Id,
                        Score = (float)similarity,
                        Metadata = metadata
                    });
                }

                // Sort by similarity (descending) and take top results
                return results
                    .OrderByDescending(r => r.Score)
                    .Take(limit)
                    .ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Deletes items from a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="ids">IDs of items to delete</param>
        public async Task DeleteItemsAsync(string collectionName, List<string> ids)
        {
            try
            {
                await _database.RunInTransactionAsync(db =>
                {
                    foreach (var id in ids)
                    {
                        db.Table<VectorItemEntity>()
                          .Delete(i => i.CollectionName == collectionName && i.Id == id);
                    }
                });

                Debug.WriteLine($"Deleted {ids.Count} items from collection {collectionName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting items from collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets an item from a collection by ID
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <param name="id">ID of the item to get</param>
        /// <returns>The vector item if found, null otherwise</returns>
        public async Task<VectorItem> GetItemAsync(string collectionName, string id)
        {
            try
            {
                var entity = await _database.Table<VectorItemEntity>()
                    .Where(i => i.CollectionName == collectionName && i.Id == id)
                    .FirstOrDefaultAsync();

                if (entity == null)
                    return null;

                // Convert VectorItemEntity to VectorItem
                var item = new VectorItem
                {
                    Id = entity.Id,
                    Embedding = DeserializeVector(entity.Embedding)
                };

                // Parse metadata
                try
                {
                    item.Metadata = JsonConvert.DeserializeObject<Dictionary<string, object>>(entity.Metadata)
                        ?? new Dictionary<string, object>();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error parsing metadata: {ex.Message}");
                    item.Metadata = new Dictionary<string, object>();
                }

                return item;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting item from collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Gets the number of items in a collection
        /// </summary>
        /// <param name="collectionName">Name of the collection</param>
        /// <returns>Number of items in the collection</returns>
        public async Task<int> GetCollectionSizeAsync(string collectionName)
        {
            try
            {
                return await _database.Table<VectorItemEntity>()
                    .Where(i => i.CollectionName == collectionName)
                    .CountAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting collection size: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Lists all collections in the database
        /// </summary>
        /// <returns>List of collection names</returns>
        public async Task<List<string>> ListCollectionsAsync()
        {
            try
            {
                var collections = await _database.Table<Collection>().ToListAsync();
                return collections.Select(c => c.Name).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error listing collections: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Deletes a collection and all its items
        /// </summary>
        /// <param name="collectionName">Name of the collection to delete</param>
        public async Task DeleteCollectionAsync(string collectionName)
        {
            try
            {
                await _database.RunInTransactionAsync(db =>
                {
                    // Delete all items in the collection
                    db.Execute("DELETE FROM VectorItemEntity WHERE CollectionName = ?", collectionName);
                    
                    // Delete the collection
                    db.Delete<Collection>(collectionName);
                });

                Debug.WriteLine($"Deleted collection {collectionName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error deleting collection: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Checks if the database is accessible
        /// </summary>
        /// <returns>True if the database is accessible</returns>
        public bool IsDatabaseAccessible()
        {
            try
            {
                Debug.WriteLine($"Checking if database is accessible: {_dbPath}");
                
                // Check if the database file exists
                bool fileExists = File.Exists(_dbPath);
                Debug.WriteLine($"Database file exists: {fileExists}");
                
                if (!fileExists)
                {
                    Debug.WriteLine("Database file does not exist, cannot be accessible");
                    return false;
                }
                
                // Try to execute a simple query
                Debug.WriteLine("Attempting to execute a simple query");
                int count = _database.GetConnection().Table<Collection>().Count();
                Debug.WriteLine($"Query executed successfully, collection count: {count}");
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Database is not accessible: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Creates a test item in the database to verify functionality
        /// </summary>
        /// <returns>True if the test was successful</returns>
        public async Task<bool> CreateTestItemAsync()
        {
            try
            {
                const string testCollection = "test_collection";
                const string testId = "test_item";

                // Create a test vector
                var testVector = new float[_vectorSize];
                for (int i = 0; i < _vectorSize; i++)
                {
                    testVector[i] = i;
                }

                // Create a test item
                var testItem = new VectorItem
                {
                    Id = testId,
                    Embedding = testVector,
                    Metadata = new Dictionary<string, object> { { "test", true } }
                };

                // Add the test item
                await AddItemAsync(testCollection, testItem);

                // Retrieve the test item
                var retrievedItem = await GetItemAsync(testCollection, testId);
                if (retrievedItem == null)
                {
                    Debug.WriteLine("Test item not found after insertion");
                    return false;
                }

                // Delete the test collection
                await DeleteCollectionAsync(testCollection);

                Debug.WriteLine("Test item created and retrieved successfully");
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating test item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Serializes a vector to a byte array
        /// </summary>
        /// <param name="vector">Vector to serialize</param>
        /// <returns>Serialized vector as a byte array</returns>
        private byte[] SerializeVector(float[] vector)
        {
            var bytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        /// <summary>
        /// Deserializes a byte array to a vector
        /// </summary>
        /// <param name="bytes">Serialized vector</param>
        /// <returns>Deserialized vector</returns>
        private float[] DeserializeVector(byte[] bytes)
        {
            var vector = new float[bytes.Length / sizeof(float)];
            Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            return vector;
        }

        /// <summary>
        /// Calculates the cosine similarity between two vectors
        /// </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>Cosine similarity</returns>
        private double CosineSimilarity(float[] a, float[] b)
        {
            double dotProduct = 0.0;
            double normA = 0.0;
            double normB = 0.0;

            for (int i = 0; i < a.Length; i++)
            {
                dotProduct += a[i] * b[i];
                normA += a[i] * a[i];
                normB += b[i] * b[i];
            }

            if (normA == 0.0 || normB == 0.0)
                return 0.0;

            return dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
        }

        /// <summary>
        /// Calculates the L2 distance between two vectors
        /// </summary>
        /// <param name="a">First vector</param>
        /// <param name="b">Second vector</param>
        /// <returns>L2 distance</returns>
        private double L2Distance(float[] a, float[] b)
        {
            double sum = 0.0;

            for (int i = 0; i < a.Length; i++)
            {
                double diff = a[i] - b[i];
                sum += diff * diff;
            }

            return Math.Sqrt(sum);
        }

        /// <summary>
        /// Disposes the SQLite connection
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the SQLite connection
        /// </summary>
        /// <param name="disposing">True if disposing, false if finalizing</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _database?.CloseAsync().GetAwaiter().GetResult();
                }

                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a collection in the vector store
    /// </summary>
    [Table("Collection")]
    public class Collection
    {
        [PrimaryKey]
        public string Name { get; set; }
    }

    /// <summary>
    /// Entity class for storing vector items in SQLite
    /// </summary>
    [Table("VectorItemEntity")]
    public class VectorItemEntity
    {
        [PrimaryKey]
        public string Id { get; set; }
        
        [Indexed]
        public string CollectionName { get; set; }
        
        public byte[] Embedding { get; set; }
        
        public string Metadata { get; set; }
    }
} 