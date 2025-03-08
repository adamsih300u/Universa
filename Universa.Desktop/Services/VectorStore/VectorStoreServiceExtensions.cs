using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Threading.Tasks;
using Universa.Desktop.Commands;
using Universa.Desktop.Services.ML;

namespace Universa.Desktop.Services.VectorStore
{
    /// <summary>
    /// Extension methods for registering vector store services
    /// </summary>
    public static class VectorStoreServiceExtensions
    {
        /// <summary>
        /// Adds vector store services to the service collection
        /// </summary>
        /// <param name="services">Service collection</param>
        /// <param name="applicationDataPath">Path to application data</param>
        /// <returns>Service collection</returns>
        public static IServiceCollection AddVectorStore(this IServiceCollection services, string applicationDataPath)
        {
            Debug.WriteLine($"Adding vector store services with data path: {applicationDataPath}");
            
            // Check if the application data directory exists
            bool dirExists = Directory.Exists(applicationDataPath);
            Debug.WriteLine($"Application data directory exists: {dirExists}");
            
            // Ensure the application data directory exists
            if (!dirExists)
            {
                Directory.CreateDirectory(applicationDataPath);
                Debug.WriteLine($"Created application data directory: {applicationDataPath}");
                
                // Verify directory was created
                dirExists = Directory.Exists(applicationDataPath);
                Debug.WriteLine($"Application data directory exists after creation: {dirExists}");
            }

            // Register the vector store as a singleton
            Debug.WriteLine("Registering IVectorStore as singleton");
            services.AddSingleton<IVectorStore>(serviceProvider =>
            {
                try
                {
                    Debug.WriteLine("Creating SqliteVectorStore instance");
                    var vectorStore = new Universa.Desktop.Services.VectorStore.SqliteVectorStore(applicationDataPath);
                    
                    // Don't check database accessibility here as it might block the UI thread
                    // The SqliteVectorStore will initialize asynchronously in the background
                    
                    Debug.WriteLine("SqliteVectorStore instance created successfully");
                    return vectorStore;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating SqliteVectorStore: {ex.Message}");
                    Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                    
                    // Return a fallback implementation or rethrow
                    throw;
                }
            });

            // Register the embedding service
            Debug.WriteLine("Registering IEmbeddingService as singleton");
            services.AddSingleton<IEmbeddingService, OfflineEmbeddingService>();

            // Register the content vectorization service as a singleton
            Debug.WriteLine("Registering ContentVectorizationService as singleton");
            services.AddSingleton<ContentVectorizationService>();

            // Register the chat history vector service as a singleton
            Debug.WriteLine("Registering ChatHistoryVectorService as singleton");
            services.AddSingleton<ChatHistoryVectorService>();

            // Register the music library vector service as a singleton
            Debug.WriteLine("Registering MusicLibraryVectorService as singleton");
            services.AddSingleton<MusicLibraryVectorService>();

            // Register the VectorizeLibraryCommand
            Debug.WriteLine("Registering VectorizeLibraryCommand as singleton");
            services.AddSingleton<Commands.VectorizeLibraryCommand>(sp => 
            {
                var contentVectorizationService = sp.GetService<ContentVectorizationService>();
                var configService = sp.GetService<Core.Configuration.IConfigurationService>();
                var libraryPath = configService?.Provider?.LibraryPath;
                
                if (contentVectorizationService == null || string.IsNullOrEmpty(libraryPath))
                {
                    Debug.WriteLine("Cannot create VectorizeLibraryCommand: missing dependencies");
                    return null;
                }
                
                return new Commands.VectorizeLibraryCommand(contentVectorizationService, libraryPath);
            });

            Debug.WriteLine("Vector store services registered successfully");
            return services;
        }
    }
} 