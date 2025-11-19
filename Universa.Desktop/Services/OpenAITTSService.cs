using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using System.Windows.Media;
using Universa.Desktop.Interfaces;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// OpenAI Text-to-Speech service for high-quality voice synthesis
    /// Supports multiple voices and formats using OpenAI's TTS API
    /// </summary>
    public class OpenAITTSService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly IConfigurationService _configService;
        private readonly MediaPlayer _mediaPlayer;
        private bool _isPlaying;
        private bool _disposed;

        // OpenAI TTS Voice options
        public static readonly string[] AvailableVoices = new[]
        {
            "alloy",
            "echo", 
            "fable",
            "onyx",
            "nova",
            "shimmer"
        };

        // OpenAI TTS Model options
        public static readonly string[] AvailableModels = new[]
        {
            "tts-1",        // Standard quality, faster
            "tts-1-hd"      // High definition, slower but higher quality
        };

        public event EventHandler<bool> PlayingStateChanged;
        public event EventHandler<string> ErrorOccurred;
        public event EventHandler PlaybackStarted;
        public event EventHandler PlaybackCompleted;

        public bool IsPlaying
        {
            get => _isPlaying;
            private set
            {
                if (_isPlaying != value)
                {
                    _isPlaying = value;
                    PlayingStateChanged?.Invoke(this, value);
                }
            }
        }

        public string SelectedVoice { get; set; } = "alloy";
        public string SelectedModel { get; set; } = "tts-1";

        public OpenAITTSService(IConfigurationService configService = null)
        {
            _configService = configService ?? ServiceLocator.Instance.GetService<IConfigurationService>();
            _httpClient = new HttpClient();
            _mediaPlayer = new MediaPlayer();
            
            // Setup HTTP client
            if (!string.IsNullOrEmpty(_configService?.Provider?.OpenAIApiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization = 
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _configService.Provider.OpenAIApiKey);
            }

            // Setup media player events
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;
        }

        /// <summary>
        /// Synthesize text to speech using OpenAI TTS API
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="voice">Voice to use (default: alloy)</param>
        /// <param name="model">Model to use (default: tts-1)</param>
        /// <returns>Audio data as byte array</returns>
        public async Task<byte[]> SynthesizeTextAsync(string text, string voice = null, string model = null)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty", nameof(text));

            if (string.IsNullOrEmpty(_configService?.Provider?.OpenAIApiKey))
                throw new InvalidOperationException("OpenAI API key is not configured");

            voice = voice ?? SelectedVoice;
            model = model ?? SelectedModel;

            var requestBody = new
            {
                model = model,
                input = text,
                voice = voice,
                response_format = "mp3"
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            try
            {
                Debug.WriteLine($"[OpenAI TTS] Synthesizing text: {text.Substring(0, Math.Min(50, text.Length))}...");
                Debug.WriteLine($"[OpenAI TTS] Using voice: {voice}, model: {model}");

                var response = await _httpClient.PostAsync("https://api.openai.com/v1/audio/speech", content);
                
                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var errorMessage = $"OpenAI TTS API error: {response.StatusCode} - {errorContent}";
                    Debug.WriteLine($"[OpenAI TTS] {errorMessage}");
                    throw new HttpRequestException(errorMessage);
                }

                var audioData = await response.Content.ReadAsByteArrayAsync();
                Debug.WriteLine($"[OpenAI TTS] Successfully synthesized {audioData.Length} bytes of audio");
                return audioData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenAI TTS] Error: {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Synthesize and play text directly
        /// </summary>
        /// <param name="text">Text to speak</param>
        /// <param name="voice">Voice to use</param>
        /// <param name="model">Model to use</param>
        public async Task SpeakAsync(string text, string voice = null, string model = null)
        {
            try
            {
                if (IsPlaying)
                {
                    Stop();
                }

                IsPlaying = true;
                PlaybackStarted?.Invoke(this, EventArgs.Empty);

                var audioData = await SynthesizeTextAsync(text, voice, model);
                await PlayAudioDataAsync(audioData);
            }
            catch (Exception ex)
            {
                IsPlaying = false;
                ErrorOccurred?.Invoke(this, ex.Message);
                throw;
            }
        }

        /// <summary>
        /// Play audio data from byte array
        /// </summary>
        /// <param name="audioData">MP3 audio data</param>
        private async Task PlayAudioDataAsync(byte[] audioData)
        {
            try
            {
                // Create a temporary file for the audio data
                var tempFile = Path.GetTempFileName() + ".mp3";
                await File.WriteAllBytesAsync(tempFile, audioData);

                // Play the audio
                _mediaPlayer.Open(new Uri(tempFile));
                _mediaPlayer.Play();

                Debug.WriteLine($"[OpenAI TTS] Playing audio from temp file: {tempFile}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[OpenAI TTS] Error playing audio: {ex.Message}");
                IsPlaying = false;
                ErrorOccurred?.Invoke(this, ex.Message);
            }
        }

        /// <summary>
        /// Stop current playback
        /// </summary>
        public void Stop()
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Stop();
                IsPlaying = false;
                Debug.WriteLine("[OpenAI TTS] Playback stopped");
            }
        }

        /// <summary>
        /// Check if OpenAI TTS is available (API key configured)
        /// </summary>
        public bool IsAvailable()
        {
            return !string.IsNullOrEmpty(_configService?.Provider?.OpenAIApiKey);
        }

        /// <summary>
        /// Get estimated cost for text synthesis
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <param name="model">Model to use for cost calculation</param>
        /// <returns>Estimated cost in USD</returns>
        public decimal GetEstimatedCost(string text, string model = null)
        {
            model = model ?? SelectedModel;
            var characterCount = text.Length;
            
            // OpenAI TTS pricing as of 2024 (per 1M characters)
            // tts-1: $15.00 per 1M characters
            // tts-1-hd: $30.00 per 1M characters
            var pricePerMillionChars = model == "tts-1-hd" ? 30.00m : 15.00m;
            
            return (characterCount / 1_000_000m) * pricePerMillionChars;
        }

        private void OnMediaEnded(object sender, EventArgs e)
        {
            IsPlaying = false;
            PlaybackCompleted?.Invoke(this, EventArgs.Empty);
            Debug.WriteLine("[OpenAI TTS] Playback completed");
        }

        private void OnMediaFailed(object sender, ExceptionEventArgs e)
        {
            IsPlaying = false;
            var errorMessage = $"Media playback failed: {e.ErrorException?.Message}";
            Debug.WriteLine($"[OpenAI TTS] {errorMessage}");
            ErrorOccurred?.Invoke(this, errorMessage);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Stop();
                _mediaPlayer?.Close();
                _httpClient?.Dispose();
                _disposed = true;
            }
        }
    }
} 