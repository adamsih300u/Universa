using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Service for vectorizing content in the library
    /// </summary>
    public class ContentVectorizationService
    {
        private readonly IVectorStore _vectorDb;
        private readonly IEmbeddingService _embeddingService;
        private readonly string _collectionName = "library_content";
        private readonly Configuration _config;
        private readonly int _chunkSize = 1000; // Characters per chunk
        private readonly int _chunkOverlap = 200; // Overlap between chunks

        /// <summary>
        /// Creates a new instance of the ContentVectorizationService
        /// </summary>
        /// <param name="vectorDb">Vector database service</param>
        public ContentVectorizationService(IVectorStore vectorDb)
        {
            _vectorDb = vectorDb ?? throw new ArgumentNullException(nameof(vectorDb));
            _embeddingService = ServiceLocator.Instance.GetService<IEmbeddingService>();
            _config = Configuration.Instance;
            
            Debug.WriteLine($"Initialized ContentVectorizationService with EnableLocalEmbeddings={_config.EnableLocalEmbeddings}");
            
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
        /// Vectorizes all content in the library
        /// </summary>
        /// <param name="libraryPath">Path to the library</param>
        /// <returns>Total number of chunks vectorized</returns>
        public async Task<int> VectorizeLibraryAsync(string libraryPath)
        {
            if (!_config.EnableLocalEmbeddings)
            {
                Debug.WriteLine("Local embeddings are disabled, skipping vectorization");
                return 0;
            }

            if (string.IsNullOrEmpty(libraryPath) || !Directory.Exists(libraryPath))
            {
                throw new ArgumentException($"Invalid library path: {libraryPath}");
            }

            Debug.WriteLine($"Starting vectorization of library at {libraryPath}");
            
            // Clear existing collection
            try
            {
                await _vectorDb.DeleteCollectionAsync(_collectionName);
                await _vectorDb.EnsureCollectionExistsAsync(_collectionName);
                Debug.WriteLine("Cleared existing library content collection");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing collection: {ex.Message}");
                // Continue with vectorization even if clearing fails
            }

            int totalChunks = 0;
            
            // Get all markdown files in the library
            var markdownFiles = Directory.GetFiles(libraryPath, "*.md", SearchOption.AllDirectories);
            Debug.WriteLine($"Found {markdownFiles.Length} markdown files in the library");

            foreach (var filePath in markdownFiles)
            {
                try
                {
                    var relativePath = filePath.Replace(libraryPath, "").TrimStart('\\', '/');
                    Debug.WriteLine($"Processing file: {relativePath}");
                    
                    var content = await File.ReadAllTextAsync(filePath);
                    var chunks = ChunkText(content, _chunkSize, _chunkOverlap);
                    
                    Debug.WriteLine($"Split file into {chunks.Count} chunks");
                    
                    var vectorItems = new List<VectorItem>();
                    
                    for (int i = 0; i < chunks.Count; i++)
                    {
                        var chunk = chunks[i];
                        var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk);
                        
                        var metadata = new Dictionary<string, object>
                        {
                            ["file_path"] = relativePath,
                            ["file_name"] = Path.GetFileName(filePath),
                            ["chunk_index"] = i,
                            ["chunk_count"] = chunks.Count,
                            ["content"] = chunk
                        };
                        
                        var vectorItem = new VectorItem(embedding, metadata);
                        vectorItems.Add(vectorItem);
                    }
                    
                    await _vectorDb.AddItemsAsync(_collectionName, vectorItems);
                    totalChunks += chunks.Count;
                    
                    Debug.WriteLine($"Vectorized {chunks.Count} chunks from {relativePath}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing file {filePath}: {ex.Message}");
                    // Continue with next file
                }
            }
            
            Debug.WriteLine($"Completed vectorization of library. Total chunks: {totalChunks}");
            return totalChunks;
        }

        /// <summary>
        /// Searches for content in the library
        /// </summary>
        /// <param name="query">Search query</param>
        /// <param name="limit">Maximum number of results</param>
        /// <returns>List of search results</returns>
        public async Task<List<ContentSearchResult>> SearchAsync(string query, int limit = 5)
        {
            if (!_config.EnableLocalEmbeddings)
            {
                Debug.WriteLine("Local embeddings are disabled, skipping search");
                return new List<ContentSearchResult>();
            }

            try
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(query);
                var results = await _vectorDb.SearchAsync(_collectionName, embedding, limit);
                
                return results.Select(r => new ContentSearchResult
                {
                    FilePath = r.Metadata.TryGetValue("file_path", out var path) ? path : "",
                    FileName = r.Metadata.TryGetValue("file_name", out var name) ? name : "",
                    Content = r.Metadata.TryGetValue("content", out var content) ? content : "",
                    ChunkIndex = r.Metadata.TryGetValue("chunk_index", out var index) ? Convert.ToInt32(index) : 0,
                    ChunkCount = r.Metadata.TryGetValue("chunk_count", out var count) ? Convert.ToInt32(count) : 0,
                    Score = r.Score
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching content: {ex.Message}");
                return new List<ContentSearchResult>();
            }
        }

        /// <summary>
        /// Splits text into chunks with overlap
        /// </summary>
        /// <param name="text">Text to split</param>
        /// <param name="chunkSize">Size of each chunk in characters</param>
        /// <param name="overlap">Overlap between chunks in characters</param>
        /// <returns>List of text chunks</returns>
        private List<string> ChunkText(string text, int chunkSize, int overlap)
        {
            var chunks = new List<string>();
            
            if (string.IsNullOrEmpty(text))
            {
                return chunks;
            }
            
            // If text is smaller than chunk size, return it as a single chunk
            if (text.Length <= chunkSize)
            {
                chunks.Add(text);
                return chunks;
            }
            
            int position = 0;
            
            while (position < text.Length)
            {
                int length = Math.Min(chunkSize, text.Length - position);
                string chunk = text.Substring(position, length);
                
                // Try to end at a sentence or paragraph boundary
                if (position + length < text.Length)
                {
                    int lastPeriod = chunk.LastIndexOf('.');
                    int lastNewline = chunk.LastIndexOf('\n');
                    int breakPoint = Math.Max(lastPeriod, lastNewline);
                    
                    if (breakPoint > chunkSize / 2)
                    {
                        // Adjust chunk to end at a natural boundary
                        chunk = chunk.Substring(0, breakPoint + 1);
                        length = breakPoint + 1;
                    }
                }
                
                chunks.Add(chunk);
                
                // Move position forward, accounting for overlap
                position += Math.Max(1, length - overlap);
            }
            
            return chunks;
        }
    }

    /// <summary>
    /// Represents a content search result
    /// </summary>
    public class ContentSearchResult
    {
        /// <summary>
        /// Relative path to the file
        /// </summary>
        public string FilePath { get; set; }
        
        /// <summary>
        /// Name of the file
        /// </summary>
        public string FileName { get; set; }
        
        /// <summary>
        /// Content of the chunk
        /// </summary>
        public string Content { get; set; }
        
        /// <summary>
        /// Index of the chunk in the file
        /// </summary>
        public int ChunkIndex { get; set; }
        
        /// <summary>
        /// Total number of chunks in the file
        /// </summary>
        public int ChunkCount { get; set; }
        
        /// <summary>
        /// Similarity score
        /// </summary>
        public float Score { get; set; }
    }
} 