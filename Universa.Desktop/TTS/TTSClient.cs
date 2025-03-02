using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections.Generic;

namespace Universa.Desktop.TTS
{
    public class TTSClient : IDisposable
    {
        private readonly string _serverUrl;
        private ClientWebSocket _webSocket;
        private bool _isConnected;
        private CancellationTokenSource _cancellationTokenSource;
        private string _currentText;
        private TTSPlayback _playback;
        private int _messageId = 0;
        private Dictionary<long, List<(byte[] audio, string text, int index, int total, bool played)>> _pendingAudioChunks = new Dictionary<long, List<(byte[] audio, string text, int index, int total, bool played)>>();

        public bool IsConnected => _isConnected;
        public string CurrentText 
        { 
            get => _currentText;
            private set => _currentText = value;
        }

        public event EventHandler<string> OnConnected;
        public event EventHandler<string> OnDisconnected;
        public event EventHandler<string> OnError;
        public event EventHandler<string> OnVoiceSet;
        public event EventHandler<string[]> OnVoicesAvailable;
        public event EventHandler<string> OnHighlightText;
        public event EventHandler OnPlaybackStarted;
        public event EventHandler OnPlaybackCompleted;
        public event Action<string> OnSpeakingStarted;
        public event Action<string> OnSpeakingCompleted;
        public event Action<string> OnSpeakingError;

        public TTSClient(string serverUrl)
        {
            _serverUrl = serverUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            if (!_serverUrl.StartsWith("ws://") && !_serverUrl.StartsWith("wss://"))
            {
                _serverUrl = "ws://" + _serverUrl;
            }
            if (!_serverUrl.EndsWith("/ws"))
            {
                _serverUrl = _serverUrl.TrimEnd('/') + "/ws";
            }
            _isConnected = false;
            _currentText = string.Empty;
            _playback = new TTSPlayback();
            _pendingAudioChunks = new Dictionary<long, List<(byte[] audio, string text, int index, int total, bool played)>>();

            // Wire up TTSPlayback events
            _playback.OnHighlightText += (s, text) => OnHighlightText?.Invoke(this, text);
            _playback.OnPlaybackStarted += (s, e) => OnPlaybackStarted?.Invoke(this, e);
            _playback.OnPlaybackCompleted += (s, e) => OnPlaybackCompleted?.Invoke(this, e);
        }

        public async Task ConnectAsync()
        {
            if (_webSocket != null)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    return; // Already connected
                }
                _webSocket.Dispose();
            }

            _webSocket = new ClientWebSocket();
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                await _webSocket.ConnectAsync(new Uri(_serverUrl), _cancellationTokenSource.Token);
                _isConnected = true;
                OnConnected?.Invoke(this, "Connected to TTS server");
                _ = ReceiveMessagesAsync();
            }
            catch (Exception ex)
            {
                _isConnected = false;
                OnError?.Invoke(this, $"Connection failed: {ex.Message}");
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Client disconnecting", CancellationToken.None);
                }
                catch (Exception ex)
                {
                    OnError?.Invoke(this, $"Error during disconnect: {ex.Message}");
                }
                finally
                {
                    _isConnected = false;
                    _webSocket.Dispose();
                    _webSocket = null;
                    OnDisconnected?.Invoke(this, "Disconnected from TTS server");
                }
            }
        }

        public void Stop()
        {
            // Stop playback first
            _playback?.Stop();

            // Then close the websocket connection
            if (_webSocket?.State == WebSocketState.Open)
            {
                try
                {
                    // Use a new cancellation token for cleanup
                    using (var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopping", cleanupCts.Token).Wait();
                    }
                }
                catch
                {
                    // Ignore any errors during cleanup
                }
            }
        }

        public void Dispose()
        {
            // Stop playback and websocket first
            Stop();

            // Then dispose resources in order
            try
            {
                if (_cancellationTokenSource?.IsCancellationRequested == false)
                {
                    _cancellationTokenSource?.Cancel();
                }
            }
            catch
            {
                // Ignore disposal errors
            }

            _cancellationTokenSource?.Dispose();
            _webSocket?.Dispose();
            _playback?.Dispose();

            _cancellationTokenSource = null;
            _webSocket = null;
            _playback = null;
        }

        private async Task ProcessAudioChunk(byte[] audioData, string text, long messageId, int chunkIndex, int totalChunks, bool isFinal)
        {
            if (!_pendingAudioChunks.ContainsKey(messageId))
            {
                _pendingAudioChunks[messageId] = new List<(byte[] audio, string text, int index, int total, bool played)>();
            }

            // Add the new chunk
            _pendingAudioChunks[messageId].Add((audioData, text, chunkIndex, totalChunks, false));

            // Get chunks in order
            var orderedChunks = _pendingAudioChunks[messageId]
                .OrderBy(c => c.index)
                .ToList();

            // If we have all chunks for this message, combine and play
            if (orderedChunks.Count == totalChunks)
            {
                // Get the first occurrence of the text (remove duplicates)
                var uniqueText = text.Split('.')[0].Trim() + ".";

                // Combine all audio data
                var combinedAudio = new List<byte>();
                foreach (var chunk in orderedChunks)
                {
                    combinedAudio.AddRange(chunk.audio);
                }

                // Play the complete audio
                await _playback.PlayAudioAsync(combinedAudio.ToArray(), uniqueText, (int)messageId, 0, 1);
                _playback.CurrentText = CurrentText;

                // Clean up if this was the final message
                if (isFinal)
                {
                    _pendingAudioChunks.Remove(messageId);
                }
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            var buffer = new byte[4096];
            var messageBuffer = new StringBuilder();

            try
            {
                while (_webSocket.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            await DisconnectAsync();
                            return;
                        }

                        var message = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuffer.Append(message);
                    }
                    while (!result.EndOfMessage);

                    var completeMessage = messageBuffer.ToString();
                    messageBuffer.Clear();

                    try
                    {
                        var response = System.Text.Json.JsonDocument.Parse(completeMessage);
                        var root = response.RootElement;

                        if (root.TryGetProperty("type", out var typeElement))
                        {
                            var type = typeElement.GetString();
                            switch (type)
                            {
                                case "voices":
                                    if (root.TryGetProperty("voices", out var voicesElement))
                                    {
                                        var voices = voicesElement.EnumerateArray()
                                            .Select(v => v.GetString())
                                            .Where(v => v != null)
                                            .ToArray();
                                        OnVoicesAvailable?.Invoke(this, voices);
                                    }
                                    break;

                                case "audio_chunk":
                                    if (root.TryGetProperty("audio_chunk", out var audioElement) &&
                                        root.TryGetProperty("text", out var textElement) &&
                                        root.TryGetProperty("message_id", out var messageIdElement) &&
                                        root.TryGetProperty("chunk_index", out var chunkIndexElement) &&
                                        root.TryGetProperty("total_chunks", out var totalChunksElement) &&
                                        root.TryGetProperty("is_final", out var isFinalElement))
                                    {
                                        var audioBase64 = audioElement.GetString();
                                        var text = textElement.GetString();
                                        var messageId = messageIdElement.GetInt64();
                                        var chunkIndex = chunkIndexElement.GetInt32();
                                        var totalChunks = totalChunksElement.GetInt32();
                                        var isFinal = isFinalElement.GetBoolean();

                                        if (audioBase64 != null && text != null)
                                        {
                                            var audioData = Convert.FromBase64String(audioBase64);
                                            await ProcessAudioChunk(audioData, text, messageId, chunkIndex, totalChunks, isFinal);
                                        }
                                    }
                                    break;

                                case "voice_set":
                                    if (root.TryGetProperty("voice", out var voiceElement))
                                    {
                                        var voice = voiceElement.GetString();
                                        if (voice != null)
                                        {
                                            OnVoiceSet?.Invoke(this, $"Voice set to: {voice}");
                                        }
                                    }
                                    break;

                                case "error":
                                    if (root.TryGetProperty("message", out var messageElement))
                                    {
                                        var errorMessage = messageElement.GetString();
                                        if (errorMessage != null)
                                        {
                                            OnError?.Invoke(this, errorMessage);
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        OnError?.Invoke(this, $"Error processing message: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                OnError?.Invoke(this, $"WebSocket receive error: {ex.Message}");
                _isConnected = false;
            }
        }

        public async Task SetVoiceAsync(string voice)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            var message = System.Text.Json.JsonSerializer.Serialize(new
            {
                type = "set_voice",
                voice = voice
            });
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
        }

        public async Task SpeakAsync(string text)
        {
            if (_webSocket?.State != WebSocketState.Open)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            CurrentText = text;
            var chunks = SplitIntoSentences(text);
            _messageId++;
            var currentMessageId = _messageId;

            for (int i = 0; i < chunks.Count; i++)
            {
                var message = System.Text.Json.JsonSerializer.Serialize(new
                {
                    command = "tts",
                    text = chunks[i].Trim()
                });
                var bytes = System.Text.Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
            }
        }

        private List<string> SplitIntoSentences(string text)
        {
            // Normalize line endings and clean up the text
            text = text.Replace("\r\n", " ")
                      .Replace("\n", " ")
                      .Replace("\r", " ");

            // Remove multiple spaces
            text = Regex.Replace(text, @"\s+", " ");

            var chunks = new List<string>();
            
            // Split by sentence endings, keeping the punctuation
            var sentences = Regex.Split(text, @"(?<=[.!?])\s+")
                               .Where(s => !string.IsNullOrWhiteSpace(s))
                               .Select(s => s.Trim())
                               .ToList();

            foreach (var sentence in sentences)
            {
                chunks.Add(sentence);
            }

            return chunks;
        }
    }
} 