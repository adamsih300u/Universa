using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    public class WebSocketService : IDisposable
    {
        private readonly Configuration _config;
        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cts;
        private bool _isConnected;
        private readonly object _lockObject = new();
        private const int InitialRetryDelayMs = 1000;
        private const int MaxRetryDelayMs = 30000;
        private const int BufferSize = 8192;
        private int _currentRetryDelay;
        private bool _isDisposed;
        private int _reconnectAttempts;

        public event EventHandler<FileMetadata> FileChanged;
        public event EventHandler<string> FileDeleted;
        public event EventHandler<FileMetadata[]> FileListReceived;
        public event EventHandler<Exception> ErrorOccurred;
        public event EventHandler<bool> ConnectionStateChanged;

        public WebSocketService()
        {
            Debug.WriteLine($"[WebSocket] Initializing WebSocketService");
            _config = Configuration.Instance;
            _cts = new CancellationTokenSource();
            _currentRetryDelay = InitialRetryDelayMs;
            _reconnectAttempts = 0;
        }

        public async Task StartAsync()
        {
            Debug.WriteLine($"[WebSocket] Starting WebSocket service");
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_isDisposed)
                    {
                        Debug.WriteLine($"[WebSocket] Service is disposed, stopping");
                        return;
                    }

                    if (_webSocket?.State == WebSocketState.Open)
                    {
                        Debug.WriteLine($"[WebSocket] Connection already open, state: {_webSocket.State}");
                        await Task.Delay(1000, _cts.Token);
                        continue;
                    }

                    Debug.WriteLine($"[WebSocket] Attempting connection (Attempt #{++_reconnectAttempts})");
                    await ConnectWithRetryAsync();
                    Debug.WriteLine($"[WebSocket] Connection successful, resetting retry delay");
                    _currentRetryDelay = InitialRetryDelayMs;
                    await ReceiveMessagesAsync();
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine($"[WebSocket] Operation cancelled");
                    break;
                }
                catch (WebSocketException ex)
                {
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        Debug.WriteLine($"[WebSocket] WebSocket error: {ex.GetType().Name} - {ex.Message}");
                        Debug.WriteLine($"[WebSocket] WebSocket state: {_webSocket?.State}");
                        Debug.WriteLine($"[WebSocket] Stack trace: {ex.StackTrace}");
                        ErrorOccurred?.Invoke(this, ex);
                        await HandleDisconnectAsync();
                        
                        Debug.WriteLine($"[WebSocket] Waiting {_currentRetryDelay}ms before retry");
                        await Task.Delay(_currentRetryDelay, _cts.Token);
                        _currentRetryDelay = Math.Min(_currentRetryDelay * 2, MaxRetryDelayMs);
                    }
                }
                catch (Exception ex)
                {
                    if (!_cts.Token.IsCancellationRequested)
                    {
                        Debug.WriteLine($"[WebSocket] Unexpected error: {ex.GetType().Name} - {ex.Message}");
                        Debug.WriteLine($"[WebSocket] Stack trace: {ex.StackTrace}");
                        ErrorOccurred?.Invoke(this, ex);
                        await HandleDisconnectAsync();
                        
                        Debug.WriteLine($"[WebSocket] Waiting {_currentRetryDelay}ms before retry");
                        await Task.Delay(_currentRetryDelay, _cts.Token);
                        _currentRetryDelay = Math.Min(_currentRetryDelay * 2, MaxRetryDelayMs);
                    }
                }
            }
        }

        private async Task ConnectWithRetryAsync()
        {
            if (_isConnected)
            {
                Debug.WriteLine($"[WebSocket] Already connected, skipping connection attempt");
                return;
            }

            try
            {
                if (_webSocket != null)
                {
                    Debug.WriteLine($"[WebSocket] Disposing old WebSocket instance in state: {_webSocket.State}");
                    _webSocket.Dispose();
                }

                _webSocket = new ClientWebSocket();
                Debug.WriteLine($"[WebSocket] Created new WebSocket instance");
                
                var authToken = Convert.ToBase64String(
                    Encoding.UTF8.GetBytes($"{_config.SyncUsername}:{_config.SyncPassword}"));
                _webSocket.Options.SetRequestHeader("Authorization", $"Basic {authToken}");
                Debug.WriteLine($"[WebSocket] Added authentication header");

                var wsUrl = _config.SyncServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
                wsUrl = $"{wsUrl.TrimEnd('/')}/api/changes";
                Debug.WriteLine($"[WebSocket] Connecting to: {wsUrl}");

                await _webSocket.ConnectAsync(new Uri(wsUrl), _cts.Token);
                Debug.WriteLine($"[WebSocket] Connection established, state: {_webSocket.State}");
                
                lock (_lockObject)
                {
                    _isConnected = true;
                    ConnectionStateChanged?.Invoke(this, true);
                }

                Debug.WriteLine($"[WebSocket] Requesting initial file list");
                await SendMessageAsync(new WebSocketMessage 
                { 
                    Type = WebSocketMessageTypes.GetFileList 
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Connection failed: {ex.GetType().Name} - {ex.Message}");
                await HandleDisconnectAsync();
                throw;
            }
        }

        private async Task ReceiveMessagesAsync()
        {
            Debug.WriteLine($"[WebSocket] Starting message receive loop");
            var buffer = new byte[BufferSize];
            var messageBuffer = new StringBuilder();

            while (_webSocket.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
            {
                try
                {
                    Debug.WriteLine($"[WebSocket] Waiting for message...");
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cts.Token);

                    Debug.WriteLine($"[WebSocket] Received message type: {result.MessageType}, " +
                        $"length: {result.Count}, endOfMessage: {result.EndOfMessage}");

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        Debug.WriteLine($"[WebSocket] Received close message");
                        await HandleDisconnectAsync();
                        break;
                    }

                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuffer.ToString();
                        Debug.WriteLine($"[WebSocket] Complete message received: {message.Substring(0, Math.Min(200, message.Length))}...");
                        await HandleMessageAsync(message);
                        messageBuffer.Clear();
                    }
                }
                catch (Exception ex) when (!_cts.Token.IsCancellationRequested)
                {
                    Debug.WriteLine($"[WebSocket] Error in receive loop: {ex.GetType().Name} - {ex.Message}");
                    await HandleDisconnectAsync();
                    throw;
                }
            }
        }

        private async Task HandleDisconnectAsync()
        {
            Debug.WriteLine($"[WebSocket] Handling disconnect, current state: {_webSocket?.State}");
            lock (_lockObject)
            {
                if (!_isConnected)
                {
                    Debug.WriteLine($"[WebSocket] Already disconnected");
                    return;
                }
                _isConnected = false;
                ConnectionStateChanged?.Invoke(this, false);
            }

            try
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    Debug.WriteLine($"[WebSocket] Sending close frame");
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure, 
                        "Client disconnecting", 
                        CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Error during close: {ex.Message}");
            }
            finally
            {
                if (_webSocket != null)
                {
                    Debug.WriteLine($"[WebSocket] Disposing WebSocket instance");
                    _webSocket.Dispose();
                    _webSocket = null;
                }
            }
        }

        private async Task HandleMessageAsync(string messageJson)
        {
            if (_cts.Token.IsCancellationRequested)
            {
                Debug.WriteLine($"[WebSocket] Skipping message handling - service is stopping");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(messageJson))
                {
                    Debug.WriteLine("[WebSocket] Received empty message - skipping");
                    return;
                }

                var message = JsonSerializer.Deserialize<WebSocketMessage>(messageJson);
                if (message == null)
                {
                    Debug.WriteLine("[WebSocket] Failed to deserialize message - skipping");
                    return;
                }

                Debug.WriteLine($"[WebSocket] Handling message of type: {message.Type}");
                
                if (message.Payload == null)
                {
                    Debug.WriteLine($"[WebSocket] Message payload is null for type {message.Type} - skipping");
                    return;
                }

                switch (message.Type)
                {
                    case WebSocketMessageTypes.FileList:
                        try
                        {
                            var fileList = JsonSerializer.Deserialize<FileListResponse>(message.Payload.ToString());
                            if (fileList?.Files == null)
                            {
                                Debug.WriteLine("[WebSocket] File list or files array is null");
                                return;
                            }
                            Debug.WriteLine($"[WebSocket] Received file list with {fileList.Files.Length} files");
                            FileListReceived?.Invoke(this, fileList.Files);
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"[WebSocket] Error deserializing file list: {ex.Message}");
                        }
                        break;

                    case WebSocketMessageTypes.FileChanged:
                        try
                        {
                            var changeNotification = JsonSerializer.Deserialize<FileChangeNotification>(message.Payload.ToString());
                            if (changeNotification?.File == null)
                            {
                                Debug.WriteLine("[WebSocket] Change notification or file is null");
                                return;
                            }
                            Debug.WriteLine($"[WebSocket] File changed: {changeNotification.File.RelativePath}");
                            FileChanged?.Invoke(this, changeNotification.File);
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"[WebSocket] Error deserializing file change: {ex.Message}");
                        }
                        break;

                    case WebSocketMessageTypes.FileDeleted:
                        try
                        {
                            var deleteNotification = JsonSerializer.Deserialize<FileDeleteNotification>(message.Payload.ToString());
                            if (deleteNotification?.RelativePath == null)
                            {
                                Debug.WriteLine("[WebSocket] Delete notification or path is null");
                                return;
                            }
                            Debug.WriteLine($"[WebSocket] File deleted: {deleteNotification.RelativePath}");
                            FileDeleted?.Invoke(this, deleteNotification.RelativePath);
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"[WebSocket] Error deserializing file deletion: {ex.Message}");
                        }
                        break;

                    default:
                        Debug.WriteLine($"[WebSocket] Unknown message type: {message.Type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Error handling message: {ex.Message}");
            }
        }

        public async Task SendMessageAsync(WebSocketMessage message)
        {
            if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            {
                Debug.WriteLine($"[WebSocket] Cannot send message - connection not ready (Connected: {_isConnected}, State: {_webSocket?.State})");
                return;
            }

            try
            {
                var json = JsonSerializer.Serialize(message);
                Debug.WriteLine($"[WebSocket] Sending message: {json}");
                var buffer = Encoding.UTF8.GetBytes(json);
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(buffer),
                    WebSocketMessageType.Text,
                    true,
                    _cts.Token);
                Debug.WriteLine($"[WebSocket] Message sent successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebSocket] Error sending message: {ex.GetType().Name} - {ex.Message}");
                ErrorOccurred?.Invoke(this, ex);
                await HandleDisconnectAsync();
            }
        }

        public void Stop()
        {
            Debug.WriteLine($"[WebSocket] Stopping service");
            _cts?.Cancel();
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                Debug.WriteLine($"[WebSocket] Already disposed");
                return;
            }
            
            Debug.WriteLine($"[WebSocket] Disposing service");
            _isDisposed = true;

            _cts?.Cancel();
            HandleDisconnectAsync().Wait();
            _cts?.Dispose();
            _webSocket?.Dispose();
            Debug.WriteLine($"[WebSocket] Service disposed");
        }
    }
} 