using System.Threading.Tasks;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Interface for services that generate embeddings from text
    /// </summary>
    public interface IEmbeddingService
    {
        /// <summary>
        /// Generates an embedding vector for the given text
        /// </summary>
        /// <param name="text">Text to embed</param>
        /// <returns>Embedding vector</returns>
        Task<float[]> GenerateEmbeddingAsync(string text);
        
        /// <summary>
        /// Gets the dimension of the embedding vectors
        /// </summary>
        int EmbeddingDimension { get; }
    }
} 