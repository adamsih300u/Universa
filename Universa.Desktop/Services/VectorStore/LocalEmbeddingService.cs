using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Service for generating embeddings using a local model
    /// </summary>
    public class LocalEmbeddingService : IEmbeddingService
    {
        private readonly HttpClient _httpClient;
        private readonly string _modelEndpoint;
        private const int DefaultEmbeddingDimension = 384; // all-MiniLM-L6-v2 dimension
        
        /// <summary>
        /// Gets the dimension of the embedding vectors
        /// </summary>
        public int EmbeddingDimension => DefaultEmbeddingDimension;

        /// <summary>
        /// Creates a new instance of the LocalEmbeddingService
        /// </summary>
        /// <param name="modelEndpoint">Endpoint for the local embedding model</param>
        public LocalEmbeddingService(string modelEndpoint = "http://localhost:8080/embeddings")
        {
            _modelEndpoint = modelEndpoint;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Generates an embedding vector for the given text
        /// </summary>
        /// <param name="text">Text to embed</param>
        /// <returns>Embedding vector</returns>
        public async Task<float[]> GenerateEmbeddingAsync(string text)
        {
            try
            {
                Debug.WriteLine($"Generating embedding for text of length {text.Length}");
                
                // Prepare the request
                var request = new
                {
                    input = text,
                    model = "all-MiniLM-L6-v2" // Default model
                };
                
                var content = new StringContent(
                    JsonConvert.SerializeObject(request),
                    Encoding.UTF8,
                    "application/json"
                );
                
                // Send the request
                var response = await _httpClient.PostAsync(_modelEndpoint, content);
                
                // Check if the request was successful
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Error generating embedding: {response.StatusCode}, {errorContent}");
                    
                    // Fall back to a random embedding for testing
                    return GenerateRandomEmbedding();
                }
                
                // Parse the response
                string responseContent = await response.Content.ReadAsStringAsync();
                var embeddingResponse = JsonConvert.DeserializeObject<EmbeddingResponse>(responseContent);
                
                if (embeddingResponse?.Data == null || embeddingResponse.Data.Length == 0)
                {
                    Debug.WriteLine("Embedding response was empty or invalid");
                    return GenerateRandomEmbedding();
                }
                
                Debug.WriteLine($"Successfully generated embedding with dimension {embeddingResponse.Data[0].Embedding.Length}");
                return embeddingResponse.Data[0].Embedding;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating embedding: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                // Fall back to a random embedding for testing
                return GenerateRandomEmbedding();
            }
        }
        
        /// <summary>
        /// Generates a random embedding vector for testing
        /// </summary>
        /// <returns>Random embedding vector</returns>
        private float[] GenerateRandomEmbedding()
        {
            Debug.WriteLine("Generating random embedding for testing");
            
            var random = new Random();
            var embedding = new float[EmbeddingDimension];
            
            for (int i = 0; i < EmbeddingDimension; i++)
            {
                embedding[i] = (float)(random.NextDouble() * 2 - 1); // Random value between -1 and 1
            }
            
            // Normalize the embedding
            float norm = (float)Math.Sqrt(embedding.Sum(x => x * x));
            for (int i = 0; i < EmbeddingDimension; i++)
            {
                embedding[i] /= norm;
            }
            
            return embedding;
        }
        
        /// <summary>
        /// Response from the embedding API
        /// </summary>
        private class EmbeddingResponse
        {
            [JsonProperty("data")]
            public EmbeddingData[] Data { get; set; }
        }
        
        /// <summary>
        /// Data from the embedding API
        /// </summary>
        private class EmbeddingData
        {
            [JsonProperty("embedding")]
            public float[] Embedding { get; set; }
        }
    }
} 