using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Universa.Desktop.Services.ML
{
    public class TokenizerService
    {
        private readonly Dictionary<string, int> _vocab;
        private const int PadToken = 0;
        private const int UnknownToken = 1;
        private const int ClsToken = 2;
        private const int SepToken = 3;
        private const string VocabUrl = "https://huggingface.co/sentence-transformers/all-MiniLM-L6-v2/raw/main/vocab.txt";
        private static readonly object _lock = new object();
        private static TokenizerService _instance;

        public static TokenizerService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TokenizerService();
                    }
                }
                return _instance;
            }
        }

        private TokenizerService()
        {
            var vocabPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Models",
                "vocab.txt"
            );

            if (!File.Exists(vocabPath))
            {
                Debug.WriteLine("Downloading vocabulary file...");
                DownloadVocabAsync(vocabPath).Wait();
            }

            _vocab = LoadVocabulary(vocabPath);
            Debug.WriteLine($"Tokenizer initialized with {_vocab.Count} tokens");
        }

        private Dictionary<string, int> LoadVocabulary(string path)
        {
            var vocab = new Dictionary<string, int>();
            var lines = File.ReadAllLines(path);
            
            // Add special tokens
            vocab["[PAD]"] = PadToken;
            vocab["[UNK]"] = UnknownToken;
            vocab["[CLS]"] = ClsToken;
            vocab["[SEP]"] = SepToken;

            // Add vocabulary tokens
            for (int i = 0; i < lines.Length; i++)
            {
                var token = lines[i].Trim();
                if (!vocab.ContainsKey(token))
                {
                    vocab[token] = i + 4; // +4 for special tokens
                }
            }

            return vocab;
        }

        private async Task DownloadVocabAsync(string path)
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                Debug.WriteLine($"Downloading vocabulary from {VocabUrl}");
                var response = await client.GetAsync(VocabUrl);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                await File.WriteAllTextAsync(path, content);
                Debug.WriteLine("Vocabulary downloaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error downloading vocabulary: {ex.Message}");
                throw;
            }
        }

        public (long[] InputIds, long[] AttentionMask) Tokenize(string text, int maxLength)
        {
            try
            {
                // Preprocess text
                text = PreprocessText(text);

                // Split into words and subwords
                var tokens = TokenizeText(text);

                // Convert to input IDs
                var inputIds = new long[maxLength];
                var attentionMask = new long[maxLength];

                // Add [CLS] token at start
                inputIds[0] = ClsToken;
                attentionMask[0] = 1;

                // Add tokens
                var tokenLength = Math.Min(tokens.Count, maxLength - 2); // -2 for [CLS] and [SEP]
                for (int i = 0; i < tokenLength; i++)
                {
                    inputIds[i + 1] = GetTokenId(tokens[i]);
                    attentionMask[i + 1] = 1;
                }

                // Add [SEP] token at end
                inputIds[tokenLength + 1] = SepToken;
                attentionMask[tokenLength + 1] = 1;

                // Pad remaining positions
                for (int i = tokenLength + 2; i < maxLength; i++)
                {
                    inputIds[i] = PadToken;
                    attentionMask[i] = 0;
                }

                return (inputIds, attentionMask);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error tokenizing text: {ex.Message}");
                throw;
            }
        }

        private List<string> TokenizeText(string text)
        {
            var tokens = new List<string>();
            var words = text.Split(' ');

            foreach (var word in words)
            {
                if (string.IsNullOrWhiteSpace(word)) continue;

                // Try to find the word in vocabulary
                if (_vocab.ContainsKey(word))
                {
                    tokens.Add(word);
                    continue;
                }

                // If not found, split into subwords
                var remaining = word;
                while (remaining.Length > 0)
                {
                    var longestMatch = "";
                    foreach (var token in _vocab.Keys)
                    {
                        if (remaining.StartsWith(token) && token.Length > longestMatch.Length)
                        {
                            longestMatch = token;
                        }
                    }

                    if (longestMatch.Length > 0)
                    {
                        tokens.Add(longestMatch);
                        remaining = remaining.Substring(longestMatch.Length);
                    }
                    else
                    {
                        // No match found, add as unknown token and move to next character
                        tokens.Add("[UNK]");
                        remaining = remaining.Substring(1);
                    }
                }
            }

            return tokens;
        }

        private int GetTokenId(string token)
        {
            return _vocab.TryGetValue(token, out var id) ? id : UnknownToken;
        }

        private string PreprocessText(string text)
        {
            // Basic text preprocessing
            text = text.ToLower();
            
            // Remove extra whitespace
            text = Regex.Replace(text, @"\s+", " ").Trim();
            
            // Remove special characters but keep basic punctuation
            text = Regex.Replace(text, @"[^\w\s.,!?-]", "");
            
            return text;
        }
    }
} 