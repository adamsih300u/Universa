using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Net.Http;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services.ML
{
    public class LocalEmbeddingService
    {
        private static readonly object _lock = new object();
        private static LocalEmbeddingService _instance;
        private InferenceSession? _session;
        private readonly BertTokenizer _tokenizer;
        private readonly bool _enableLocalEmbeddings;
        private readonly string _modelPath;
        private readonly HttpClient _httpClient;
        private const string ModelUrl = "https://huggingface.co/optimum/all-MiniLM-L6-v2/resolve/main/model.onnx";
        private const int MaxLength = 512;
        private TaskCompletionSource<bool> _initializationTask;

        public static LocalEmbeddingService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new LocalEmbeddingService();
                    }
                }
                return _instance;
            }
        }

        private LocalEmbeddingService()
        {
            _httpClient = new HttpClient();
            var config = Configuration.Instance;
            _enableLocalEmbeddings = config.EnableLocalEmbeddings;
            _initializationTask = new TaskCompletionSource<bool>();

            var modelsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models");
            Directory.CreateDirectory(modelsDir);
            _modelPath = Path.Combine(modelsDir, "all-MiniLM-L6-v2.onnx");
            _tokenizer = new BertTokenizer();

            if (!_enableLocalEmbeddings)
            {
                _initializationTask.SetResult(false);
                return;
            }

            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            try
            {
                if (!File.Exists(_modelPath))
                {
                    await DownloadModelAsync();
                }

                try
                {
                    var options = new SessionOptions();
                    _session = new InferenceSession(_modelPath, options);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error creating ONNX session: {ex.Message}");
                    _initializationTask.SetException(ex);
                    return;
                }
                
                _initializationTask.SetResult(true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing local embedding service: {ex.Message}");
                _initializationTask.SetException(ex);
            }
        }

        public async Task EnsureInitializedAsync()
        {
            await _initializationTask.Task;
        }

        private async Task DownloadModelAsync()
        {
            try
            {
                Debug.WriteLine($"Downloading model from {ModelUrl}");
                var response = await _httpClient.GetAsync(ModelUrl);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = File.Create(_modelPath);
                await stream.CopyToAsync(fileStream);
                
                // Ensure the file is written to disk
                fileStream.Flush(true);
                Debug.WriteLine($"Model downloaded successfully. File size: {new FileInfo(_modelPath).Length} bytes");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading model: {ex.Message}");
                Debug.WriteLine($"Inner Exception: {ex.InnerException?.Message ?? "None"}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task<float[]> GetEmbeddingsAsync(string text)
        {
            if (!_enableLocalEmbeddings)
            {
                throw new InvalidOperationException("Local embeddings are disabled");
            }

            await EnsureInitializedAsync();

            if (_session == null)
            {
                throw new InvalidOperationException("ONNX session is not initialized");
            }

            try
            {
                var (inputIds, attentionMask) = _tokenizer.Tokenize(text, MaxLength);
                var inputTensor = new DenseTensor<long>(inputIds, new[] { 1, MaxLength });
                var maskTensor = new DenseTensor<long>(attentionMask, new[] { 1, MaxLength });
                var tokenTypeTensor = new DenseTensor<long>(new long[MaxLength], new[] { 1, MaxLength });

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputTensor),
                    NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor),
                    NamedOnnxValue.CreateFromTensor("token_type_ids", tokenTypeTensor)
                };

                using var results = _session.Run(inputs);
                var embeddings = results.First().AsTensor<float>();
                
                var clsEmbeddings = new float[384];
                for (int i = 0; i < 384; i++)
                {
                    clsEmbeddings[i] = embeddings[0, 0, i];
                }
                
                return NormalizeEmbeddings(clsEmbeddings);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating local embeddings: {ex.Message}");
                throw;
            }
        }

        private float[] NormalizeEmbeddings(float[] embeddings)
        {
            float sumSquares = embeddings.Sum(x => x * x);
            float norm = (float)Math.Sqrt(sumSquares);
            return embeddings.Select(x => x / norm).ToArray();
        }
    }
} 