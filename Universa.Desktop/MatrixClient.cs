using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using System.Net.Http.Headers;
using Universa.Desktop.Models;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Universa.Desktop.Views;

namespace Universa.Desktop
{
    public class MatrixClient : IDisposable
    {
        private static readonly object _lock = new object();
        private static MatrixClient _instance;
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private MatrixCrypto _crypto;
        private string _accessToken;
        private string _userId;
        private string _deviceId;
        private readonly JsonSerializerSettings _jsonSettings;
        private DateTime _lastRequest = DateTime.MinValue;
        private const int MinRequestInterval = 100; // milliseconds
        private readonly Dictionary<string, VerificationSession> _verificationSessions = new Dictionary<string, VerificationSession>();
        private bool _isSyncing;
        private readonly List<Action<string, Dictionary<string, object>>> _toDeviceEventHandlers = new List<Action<string, Dictionary<string, object>>>();
        private readonly List<Action<string, MatrixMessage>> _roomMessageHandlers = new List<Action<string, MatrixMessage>>();
        private bool _isDeviceVerified;
        private bool _isConnected;

        public static MatrixClient Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new MatrixClient("https://matrix.org");
                    }
                }
                return _instance;
            }
        }

        public MatrixClient(string baseUrl)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(_baseUrl)
            };
            _jsonSettings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            };
            _crypto = null;
            System.Diagnostics.Debug.WriteLine($"MatrixClient: Initialized with base URL {_baseUrl}");
        }

        private async Task ThrottleRequest()
        {
            var timeSinceLastRequest = DateTime.Now - _lastRequest;
            if (timeSinceLastRequest.TotalMilliseconds < MinRequestInterval)
            {
                await Task.Delay(MinRequestInterval - (int)timeSinceLastRequest.TotalMilliseconds);
            }
            _lastRequest = DateTime.Now;
        }

        private async Task<T> SendRequestWithRetry<T>(Func<Task<T>> request, int maxRetries = 3)
        {
            int retryCount = 0;
            int retryDelay = 1000; // Start with 1 second delay

            while (true)
            {
                try
                {
                    await ThrottleRequest();
                    return await request();
                }
                catch (HttpRequestException ex) when (ex.Message.Contains("429"))
                {
                    if (retryCount >= maxRetries)
                        throw new Exception($"Max retries ({maxRetries}) exceeded", ex);

                    System.Diagnostics.Debug.WriteLine($"Rate limited, waiting {retryDelay}ms before retry {retryCount + 1}/{maxRetries}");
                    await Task.Delay(retryDelay);
                    retryDelay *= 2; // Exponential backoff
                    retryCount++;
                }
            }
        }

        public async Task Login(string username, string password)
        {
            try
            {
                var loginData = new Dictionary<string, string>
                {
                    ["type"] = "m.login.password",
                    ["user"] = username,
                    ["password"] = password,
                    ["initial_device_display_name"] = "Universa Desktop"
                };

                Debug.WriteLine($"Attempting to login as {username}");
                var response = await SendRequestWithRetry(async () =>
                {
                    var content = new StringContent(
                        JsonConvert.SerializeObject(loginData),
                        Encoding.UTF8,
                        "application/json"
                    );
                    return await _httpClient.PostAsync("/_matrix/client/r0/login", content);
                });

                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Login response: {responseContent}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(responseContent);
                    var errorMessage = "Login failed: ";

                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        var retryAfterMs = errorResponse.ContainsKey("retry_after_ms") 
                            ? errorResponse["retry_after_ms"].ToString() 
                            : "unknown";
                        errorMessage = $"Rate limited - Please wait {retryAfterMs}ms before trying again";
                    }
                    else if (errorResponse.ContainsKey("error"))
                    {
                        errorMessage += errorResponse["error"].ToString();
                    }

                    throw new Exception(errorMessage);
                }

                var loginResponse = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                _accessToken = loginResponse["access_token"];
                _userId = loginResponse["user_id"];
                _deviceId = loginResponse["device_id"];

                // Initialize crypto after we have userId and deviceId
                _crypto = new MatrixCrypto(_userId, _deviceId);

                Debug.WriteLine($"Login successful. User ID: {_userId}, Device ID: {_deviceId}");

                // Add authorization header for future requests
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);

                // Get server capabilities
                var capabilitiesResponse = await SendRequestWithRetry(async () =>
                    await _httpClient.GetAsync("/_matrix/client/r0/capabilities")
                );

                if (capabilitiesResponse.IsSuccessStatusCode)
                {
                    var capabilitiesContent = await capabilitiesResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Server capabilities: {capabilitiesContent}");
                }

                await UploadKeys();
                IsConnected = true;  // Set IsConnected to true after successful login
            }
            catch (Exception ex)
            {
                IsConnected = false;  // Set IsConnected to false on failure
                Debug.WriteLine($"Login error: {ex.Message}");
                throw;
            }
        }

        private async Task UploadKeys()
        {
            try
            {
                if (_crypto == null)
                {
                    if (string.IsNullOrEmpty(_userId) || string.IsNullOrEmpty(_deviceId))
                    {
                        throw new InvalidOperationException("Cannot upload keys: User ID or Device ID is not set. Please ensure you are logged in.");
                    }
                    _crypto = new MatrixCrypto(_userId, _deviceId);
                }

                // First, query existing keys
                var queryResponse = await SendRequestWithRetry(async () =>
                    await _httpClient.PostAsync(
                        "/_matrix/client/v3/keys/query",
                        new StringContent(
                            JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                ["device_keys"] = new Dictionary<string, object>
                                {
                                    [_userId] = new string[] { }
                                }
                            }),
                            Encoding.UTF8,
                            "application/json"
                        )
                    )
                );

                if (!queryResponse.IsSuccessStatusCode)
                {
                    var error = await queryResponse.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Failed to query keys: {queryResponse.StatusCode} - {error}");
                    throw new Exception($"Failed to query keys: {queryResponse.StatusCode}");
                }

                var queryContent = await queryResponse.Content.ReadAsStringAsync();
                Debug.WriteLine($"Existing keys: {queryContent}");

                // Generate and upload device keys
                var deviceKeys = await _crypto.GenerateDeviceKeys();
                var oneTimeKeys = await _crypto.GenerateOneTimeKeys(50);

                var keysData = new Dictionary<string, object>
                {
                    ["device_keys"] = deviceKeys,
                    ["one_time_keys"] = oneTimeKeys["one_time_keys"]
                };

                Debug.WriteLine($"Uploading keys: {JsonConvert.SerializeObject(keysData)}");

                var response = await SendRequestWithRetry(async () =>
                {
                    var content = new StringContent(
                        JsonConvert.SerializeObject(keysData, _jsonSettings),
                        Encoding.UTF8,
                        "application/json"
                    );
                    return await _httpClient.PostAsync("/_matrix/client/v3/keys/upload", content);
                });

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Failed to upload keys: {response.StatusCode} - {error}");
                    
                    // If we get a 400 error about existing keys, we can continue
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest && 
                        error.Contains("already exists"))
                    {
                        Debug.WriteLine("Some keys already exist, continuing with verification");
                        return;
                    }
                    
                    throw new Exception($"Failed to upload keys: {response.StatusCode} - {error}");
                }

                _crypto.MarkKeysAsPublished();
                Debug.WriteLine("Keys uploaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to upload keys: {ex.Message}");
                throw;
            }
        }

        public async Task<List<string>> GetJoinedRooms()
        {
            try
            {
                var response = await SendRequestWithRetry(async () =>
                    await _httpClient.GetAsync("/_matrix/client/r0/joined_rooms")
                );

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to get joined rooms: {response.StatusCode} - {error}");
                }

                var result = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(
                    await response.Content.ReadAsStringAsync()
                );

                return result["joined_rooms"];
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get joined rooms: {ex.Message}");
                throw;
            }
        }

        public async Task<(string Name, string Topic)> GetRoomInfo(string roomId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/state");
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to get room info: {response.StatusCode} - {error}");
                }

                var events = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(
                    await response.Content.ReadAsStringAsync()
                );

                string name = null;
                string topic = null;

                foreach (var evt in events)
                {
                    if (evt["type"].ToString() == "m.room.name")
                    {
                        var content = evt["content"] as JObject;
                        name = content?["name"]?.ToString();
                    }
                    else if (evt["type"].ToString() == "m.room.topic")
                    {
                        var content = evt["content"] as JObject;
                        topic = content?["topic"]?.ToString();
                    }
                }

                // If no name is found, try to get canonical alias
                if (string.IsNullOrEmpty(name))
                {
                    foreach (var evt in events)
                    {
                        if (evt["type"].ToString() == "m.room.canonical_alias")
                        {
                            var content = evt["content"] as JObject;
                            name = content?["alias"]?.ToString();
                            break;
                        }
                    }
                }

                // If still no name, use room ID
                name = name ?? roomId;
                topic = topic ?? string.Empty;

                System.Diagnostics.Debug.WriteLine($"Room info - Name: {name}, Topic: {topic}");
                return (name, topic);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get room info: {ex.Message}");
                return (roomId, string.Empty);
            }
        }

        public async Task<List<MatrixMessage>> GetRoomMessages(string roomId)
        {
            try
            {
                var endpoint = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/messages?dir=b&limit=50";
                var response = await _httpClient.GetAsync(endpoint);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var messages = new List<MatrixMessage>();
                
                var eventsResponse = JsonConvert.DeserializeObject<JObject>(content);
                var events = eventsResponse["chunk"] as JArray;
                
                if (events != null)
                {
                    foreach (var evt in events)
                    {
                        if (evt["type"]?.ToString() == "m.room.message")
                        {
                            var msgContent = evt["content"] as JObject;
                            if (msgContent != null && msgContent["msgtype"]?.ToString() == "m.text")
                            {
                                var message = MatrixMessage.FromMatrixEvent(
                                    evt["event_id"]?.ToString(),
                                    evt["sender"]?.ToString(),
                                    evt["type"]?.ToString(),
                                    msgContent
                                );
                                messages.Add(message);
                            }
                        }
                    }
                }
                
                return messages.OrderBy(m => m.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting room messages: {ex.Message}");
                return new List<MatrixMessage>();
            }
        }

        public async Task<string> SendMessage(string roomId, string messageText)
        {
            try
            {
                var endpoint = $"/_matrix/client/v3/rooms/{Uri.EscapeDataString(roomId)}/send/m.room.message";
                var content = new JObject
                {
                    ["msgtype"] = "m.text",
                    ["body"] = messageText
                };

                var response = await SendRequestWithRetry(async () =>
                {
                    var stringContent = new StringContent(
                        JsonConvert.SerializeObject(content),
                        Encoding.UTF8,
                        "application/json"
                    );
                    return await _httpClient.PostAsync(endpoint, stringContent);
                });

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Failed to send message: {response.StatusCode} - {errorContent}");
                    throw new Exception($"Failed to send message: {response.StatusCode}");
                }

                var responseContent = await response.Content.ReadAsStringAsync();
                var eventId = JObject.Parse(responseContent)["event_id"]?.ToString();
                
                if (string.IsNullOrEmpty(eventId))
                {
                    throw new Exception("No event ID returned from server");
                }

                return eventId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error sending message: {ex.Message}");
                throw;
            }
        }

        public async Task StartVerification(string userId, string deviceId = "*")
        {
            System.Diagnostics.Debug.WriteLine($"Starting verification with user {userId} device {deviceId}");
            
            var transactionId = Guid.NewGuid().ToString();
            var content = new Dictionary<string, object>
            {
                ["from_device"] = _deviceId,
                ["methods"] = new[] { "m.sas.v1" },
                ["timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                ["transaction_id"] = transactionId,
                ["type"] = "m.key.verification.request"
            };

            try 
            {
                // Get list of devices for the user
                var response = await SendRequestWithRetry(async () =>
                    await _httpClient.GetAsync($"/_matrix/client/r0/devices")
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Failed to get device list: {response.StatusCode}");
                }

                var devicesResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(
                    await response.Content.ReadAsStringAsync()
                );

                var devices = ((JArray)devicesResponse["devices"]).ToObject<List<Dictionary<string, object>>>();
                System.Diagnostics.Debug.WriteLine($"Found {devices.Count} devices");

                // Filter out our current device
                var otherDevices = devices
                    .Where(d => d["device_id"].ToString() != _deviceId)
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"Found {otherDevices.Count} other devices");

                if (!otherDevices.Any())
                {
                    throw new Exception("No other devices found to verify with");
                }

                // Send verification request to each device
                foreach (var device in otherDevices)
                {
                    var targetDeviceId = device["device_id"].ToString();
                    System.Diagnostics.Debug.WriteLine($"Sending verification request to device: {targetDeviceId}");

                    var requestContent = new Dictionary<string, object>
                    {
                        ["messages"] = new Dictionary<string, object>
                        {
                            [_userId] = new Dictionary<string, object>
                            {
                                [targetDeviceId] = new Dictionary<string, object>(content)
                                {
                                    ["to"] = _userId,
                                    ["to_device"] = targetDeviceId
                                }
                            }
                        }
                    };

                    System.Diagnostics.Debug.WriteLine($"Sending verification request: {JsonConvert.SerializeObject(requestContent)}");

                    var sendResponse = await _httpClient.PutAsync(
                        $"/_matrix/client/r0/sendToDevice/m.key.verification.request/{transactionId}",
                        new StringContent(
                            JsonConvert.SerializeObject(requestContent),
                            Encoding.UTF8,
                            "application/json"
                        )
                    );

                    if (!sendResponse.IsSuccessStatusCode)
                    {
                        var responseContent = await sendResponse.Content.ReadAsStringAsync();
                        System.Diagnostics.Debug.WriteLine($"Failed to send verification request to device {targetDeviceId}: {sendResponse.StatusCode} - {responseContent}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Successfully sent verification request to device {targetDeviceId}");
                    }
                }

                // Store the verification session
                _verificationSessions[transactionId] = new VerificationSession
                {
                    UserId = _userId,
                    DeviceId = deviceId,
                    TransactionId = transactionId,
                    State = VerificationState.Requested
                };

                System.Diagnostics.Debug.WriteLine($"Verification requests sent to {otherDevices.Count} devices");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error starting verification: {ex.Message}\n{ex.StackTrace}");
                throw;
            }
        }

        public async Task HandleVerificationEvent(string type, string content)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Handling verification event: {type}");
                System.Diagnostics.Debug.WriteLine($"Event content: {content}");
                
                // Parse event content
                var eventContent = JsonConvert.DeserializeObject<Dictionary<string, object>>(content);
                var transactionId = eventContent["transaction_id"]?.ToString();
                
                if (string.IsNullOrEmpty(transactionId))
                {
                    System.Diagnostics.Debug.WriteLine("No transaction_id in event content");
                    return;
                }
                
                // Find matching verification session
                if (!_verificationSessions.TryGetValue(transactionId, out var session))
                {
                    System.Diagnostics.Debug.WriteLine($"No session found for transaction_id: {transactionId}");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine($"Found session for transaction_id: {transactionId}");
                
                switch (type)
                {
                    case "m.key.verification.accept":
                        await session.HandleAccept(eventContent);
                        break;
                        
                    case "m.key.verification.key":
                        await session.HandleKey(eventContent);
                        break;
                        
                    case "m.key.verification.mac":
                        await session.HandleMac(eventContent);
                        break;
                        
                    case "m.key.verification.cancel":
                        var reason = eventContent.ContainsKey("reason") ? eventContent["reason"].ToString() : null;
                        session.Cancel(reason);
                        break;
                        
                    default:
                        System.Diagnostics.Debug.WriteLine($"Unhandled verification event type: {type}");
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling verification event: {ex.Message}");
                throw;
            }
        }

        public void Dispose()
        {
            IsConnected = false;  // Set IsConnected to false when disposing
            _httpClient?.Dispose();
            _crypto?.Dispose();
        }

        public string UserId => _userId;
        public string DeviceId => _deviceId;

        public void AddToDeviceEventHandler(Action<string, Dictionary<string, object>> handler)
        {
            _toDeviceEventHandlers.Add(handler);
        }

        public void AddRoomMessageHandler(Action<string, MatrixMessage> handler)
        {
            _roomMessageHandlers.Add(handler);
        }

        public async Task StartSync()
        {
            if (_isSyncing)
            {
                Debug.WriteLine("Sync already in progress");
                return;
            }

            _isSyncing = true;
            string nextBatch = null;
            int consecutiveFailures = 0;
            int timeout = 30000; // Start with 30 seconds

            while (_isSyncing)
            {
                try
                {
                    var queryParams = new Dictionary<string, string>();
                    if (nextBatch != null)
                    {
                        queryParams["since"] = nextBatch;
                        queryParams["timeout"] = timeout.ToString();
                    }

                    var response = await SendRequestWithRetry(async () =>
                        await _httpClient.GetAsync($"/_matrix/client/v3/sync{(queryParams.Any() ? "?" + string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={kvp.Value}")) : "")}")
                    );

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Sync failed: {response.StatusCode}");
                    }

                    var syncContent = await response.Content.ReadAsStringAsync();
                    var syncResponse = JsonConvert.DeserializeObject<JObject>(syncContent);

                    // Process to-device events first as they are critical for verification
                    var toDeviceEvents = syncResponse["to_device"]?["events"] as JArray;
                    if (toDeviceEvents != null)
                    {
                        foreach (var evt in toDeviceEvents)
                        {
                            var type = evt["type"]?.ToString();
                            if (type?.StartsWith("m.key.verification.") == true)
                            {
                                Debug.WriteLine($"Received verification event: {type}");
                                Debug.WriteLine($"Event content: {evt["content"]}");
                                
                                var eventContent = evt["content"].ToObject<Dictionary<string, object>>();
                                foreach (var handler in _toDeviceEventHandlers.ToList())
                                {
                                    try
                                    {
                                        handler(type, eventContent);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine($"Error in to-device event handler: {ex.Message}");
                                    }
                                }
                            }
                        }
                    }

                    // Process room events
                    var rooms = syncResponse["rooms"]?["join"] as JObject;
                    if (rooms != null)
                    {
                        foreach (var room in rooms)
                        {
                            var roomId = room.Key;
                            var timeline = room.Value["timeline"]?["events"] as JArray;
                            if (timeline != null)
                            {
                                foreach (var evt in timeline)
                                {
                                    if (evt["type"]?.ToString() == "m.room.message")
                                    {
                                        var msgContent = evt["content"] as JObject;
                                        if (msgContent != null && msgContent["msgtype"]?.ToString() == "m.text")
                                        {
                                            var message = MatrixMessage.FromMatrixEvent(
                                                evt["event_id"]?.ToString(),
                                                evt["sender"]?.ToString(),
                                                evt["type"]?.ToString(),
                                                msgContent
                                            );
                                            foreach (var handler in _roomMessageHandlers.ToList())
                                            {
                                                try
                                                {
                                                    handler(roomId, message);
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"Error in room message handler: {ex.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    nextBatch = syncResponse["next_batch"]?.ToString();
                    consecutiveFailures = 0;
                    timeout = 30000; // Reset timeout on success
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Sync error: {ex.Message}");
                    consecutiveFailures++;
                    timeout = Math.Min(timeout * 2, 300000); // Double timeout up to 5 minutes max
                    await Task.Delay(Math.Min(consecutiveFailures * 1000, 30000)); // Backoff delay
                }
            }
        }

        public void StopSync()
        {
            _isSyncing = false;
            IsConnected = false;  // Set IsConnected to false when stopping sync
        }

        public VerificationSession GetVerificationSession(string userId, string deviceId)
        {
            System.Diagnostics.Debug.WriteLine($"Looking for verification session for user {userId} device {deviceId}");
            return _verificationSessions.Values.FirstOrDefault(s => 
                s.UserId == userId && 
                (s.DeviceId == deviceId || deviceId == "*") &&
                s.State != VerificationState.Completed &&
                s.State != VerificationState.Cancelled);
        }

        public async Task CancelVerification(string transactionId, string reason)
        {
            System.Diagnostics.Debug.WriteLine($"Cancelling verification {transactionId}: {reason}");
            
            if (_verificationSessions.TryGetValue(transactionId, out var session))
            {
                var content = new Dictionary<string, object>
                {
                    ["transaction_id"] = transactionId,
                    ["reason"] = reason
                };

                try
                {
                    var response = await _httpClient.PutAsync(
                        $"/_matrix/client/v3/sendToDevice/m.key.verification.cancel/{Guid.NewGuid()}",
                        new StringContent(
                            JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                ["messages"] = new Dictionary<string, object>
                                {
                                    [session.UserId] = new Dictionary<string, object>
                                    {
                                        [session.DeviceId] = content
                                    }
                                }
                            }),
                            Encoding.UTF8,
                            "application/json"
                        )
                    );

                    if (!response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to send cancellation: {response.StatusCode} - {responseContent}");
                    }

                    session.Cancel(reason);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error cancelling verification: {ex.Message}");
                    throw;
                }
            }
        }

        public async Task ConfirmVerification(string transactionId)
        {
            System.Diagnostics.Debug.WriteLine($"Confirming verification {transactionId}");
            
            if (_verificationSessions.TryGetValue(transactionId, out var session))
            {
                var content = new Dictionary<string, object>
                {
                    ["transaction_id"] = transactionId,
                    ["key"] = "..." // TODO: Add actual key data
                };

                try
                {
                    var response = await _httpClient.PutAsync(
                        $"/_matrix/client/v3/sendToDevice/m.key.verification.key/{Guid.NewGuid()}",
                        new StringContent(
                            JsonConvert.SerializeObject(new Dictionary<string, object>
                            {
                                ["messages"] = new Dictionary<string, object>
                                {
                                    [session.UserId] = new Dictionary<string, object>
                                    {
                                        [session.DeviceId] = content
                                    }
                                }
                            }),
                            Encoding.UTF8,
                            "application/json"
                        )
                    );

                    if (!response.IsSuccessStatusCode)
                    {
                        var responseContent = await response.Content.ReadAsStringAsync();
                        throw new Exception($"Failed to send confirmation: {response.StatusCode} - {responseContent}");
                    }

                    session.UpdateState(VerificationState.KeysVerified);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error confirming verification: {ex.Message}");
                    throw;
                }
            }
        }

        private async Task<string> SendRequest(HttpMethod method, string endpoint, Dictionary<string, string> queryParams = null)
        {
            try
            {
                var url = endpoint;
                if (queryParams != null && queryParams.Any())
                {
                    var queryString = string.Join("&", queryParams.Select(kvp => $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));
                    url += $"?{queryString}";
                }

                var request = new HttpRequestMessage(method, url);
                var response = await SendRequestWithRetry(async () => await _httpClient.SendAsync(request));

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Request failed: {response.StatusCode} - {error}");
                }

                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SendRequest failed: {ex.Message}");
                throw;
            }
        }

        public async Task<List<ChatRoom>> GetRooms()
        {
            try
            {
                var response = await _httpClient.GetAsync($"/_matrix/client/v3/joined_rooms");
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync();
                var roomsJson = JObject.Parse(content);
                var roomIds = roomsJson["joined_rooms"]?.ToObject<List<string>>() ?? new List<string>();
                
                var rooms = new List<ChatRoom>();
                foreach (var roomId in roomIds)
                {
                    try 
                    {
                        var (name, topic) = await GetRoomInfo(roomId);
                        var room = new ChatRoom
                        {
                            Id = roomId,
                            Name = name,
                            Topic = topic,
                            Type = "matrix"  // Identify this as a Matrix room
                        };
                        rooms.Add(room);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error getting info for room {roomId}: {ex.Message}");
                        // Continue with next room if one fails
                        continue;
                    }
                }
                
                return rooms;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting rooms: {ex.Message}");
                return new List<ChatRoom>();
            }
        }

        public async Task StartVerificationWithDevice(string deviceId)
        {
            Debug.WriteLine($"Starting verification with device {deviceId}");
            
            // First, ensure we have uploaded our keys
            await UploadKeys();
            
            // Ensure we're syncing to receive responses
            if (!_isSyncing)
            {
                Debug.WriteLine("Starting sync to receive verification events");
                _ = StartSync(); // Start syncing in the background
            }
            
            var transactionId = Guid.NewGuid().ToString();
            var content = new Dictionary<string, object>
            {
                ["from_device"] = _deviceId,
                ["to"] = _userId,
                ["to_device"] = deviceId,
                ["transaction_id"] = transactionId,
                ["methods"] = new[] { "m.sas.v1" }
            };

            var requestContent = new Dictionary<string, object>
            {
                ["messages"] = new Dictionary<string, object>
                {
                    [_userId] = new Dictionary<string, object>
                    {
                        [deviceId] = new Dictionary<string, object>
                        {
                            ["type"] = "m.key.verification.request",
                            ["content"] = content
                        }
                    }
                }
            };

            Debug.WriteLine($"Sending verification request: {JsonConvert.SerializeObject(requestContent, Formatting.Indented)}");

            // Register for to-device events before sending the request
            AddToDeviceEventHandler((type, eventContent) =>
            {
                Debug.WriteLine($"Received to-device event: {type}");
                Debug.WriteLine($"Event content: {JsonConvert.SerializeObject(eventContent, Formatting.Indented)}");
                if (type.StartsWith("m.key.verification."))
                {
                    _ = HandleVerificationEvent(type, JsonConvert.SerializeObject(eventContent));
                }
            });

            var response = await _httpClient.PutAsync(
                $"/_matrix/client/v3/sendToDevice/m.key.verification.request/{transactionId}",
                new StringContent(
                    JsonConvert.SerializeObject(requestContent),
                    Encoding.UTF8,
                    "application/json"
                )
            );

            if (!response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Failed to send verification request: {response.StatusCode} - {responseContent}");
                throw new Exception($"Failed to send verification request: {response.StatusCode}");
            }

            Debug.WriteLine($"Successfully sent verification request to device {deviceId}");

            // Store the verification session
            var session = new VerificationSession
            {
                UserId = _userId,
                DeviceId = deviceId,
                TransactionId = transactionId,
                State = VerificationState.Requested
            };
            _verificationSessions[transactionId] = session;
            
            // Create and show the verification window
            var verificationWindow = new VerificationWindow(session, this);
            verificationWindow.Show();
        }

        // Response classes for Matrix API
        private class JoinedRoomsResponse
        {
            public List<string> JoinedRooms { get; set; }
        }

        private class RoomState
        {
            public string Name { get; set; }
            public string Topic { get; set; }
        }

        public async Task<List<MatrixDevice>> GetDevices()
        {
            try
            {
                Debug.WriteLine("Fetching devices from Matrix server...");
                var response = await SendRequestWithRetry(async () =>
                    await _httpClient.GetAsync("/_matrix/client/v3/devices")
                );

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to get devices: {response.StatusCode} - {error}");
                }

                var content = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Received devices response: {content}");

                var result = JsonConvert.DeserializeObject<DevicesResponse>(content);
                if (result?.Devices == null)
                {
                    throw new Exception("Invalid response format from server");
                }

                Debug.WriteLine($"Found {result.Devices.Count} devices");
                return result.Devices;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting devices: {ex.Message}");
                throw;
            }
        }

        private class DevicesResponse
        {
            [JsonProperty("devices")]
            public List<MatrixDevice> Devices { get; set; }
        }

        public bool IsDeviceVerified
        {
            get => _isDeviceVerified;
            set
            {
                if (_isDeviceVerified != value)
                {
                    _isDeviceVerified = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsConnected
        {
            get => _isConnected;
            private set
            {
                if (_isConnected != value)
                {
                    _isConnected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Room
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Topic { get; set; }
        public int MemberCount { get; set; }
        public RoomType Type { get; set; }
    }

    public class Message
    {
        public string Id { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
    }
} 