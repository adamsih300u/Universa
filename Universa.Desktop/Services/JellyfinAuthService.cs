using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace Universa.Desktop.Services
{
    public class JellyfinAuthService
    {
        private readonly HttpClient _httpClient;
        private string _accessToken;
        private readonly string _serverUrl;
        private readonly string _username;
        private readonly string _password;
        private string _userId;
        
        private const string CLIENT_NAME = "Universa";
        private const string DEVICE_ID = "UniversaApp";
        private const string VERSION = "1.0.0";

        public JellyfinAuthService(HttpClient httpClient, string serverUrl, string username, string password)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _serverUrl = serverUrl?.TrimEnd('/') ?? throw new ArgumentException("Server URL cannot be empty", nameof(serverUrl));
            _username = username ?? throw new ArgumentException("Username cannot be empty", nameof(username));
            _password = password ?? throw new ArgumentException("Password cannot be empty", nameof(password));
            
            if (!_serverUrl.StartsWith("http://") && !_serverUrl.StartsWith("https://"))
            {
                _serverUrl = "http://" + _serverUrl;
            }
        }

        public string UserId => _userId;
        public string AccessToken => _accessToken;
        public string ServerUrl => _serverUrl;

        public async Task<bool> AuthenticateAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_serverUrl) || string.IsNullOrEmpty(_username) || string.IsNullOrEmpty(_password))
                {
                    System.Diagnostics.Debug.WriteLine("Missing credentials for authentication");
                    return false;
                }

                UpdateAuthorizationHeader();

                var authBody = new
                {
                    Username = _username,
                    Pw = _password
                };

                var json = JsonSerializer.Serialize(authBody);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_serverUrl}/Users/authenticatebyname", content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                var authResult = JsonSerializer.Deserialize<AuthenticationResult>(responseContent);

                if (authResult?.AccessToken != null && authResult.User?.Id != null)
                {
                    _accessToken = authResult.AccessToken;
                    _userId = authResult.User.Id;
                    UpdateAuthorizationHeader();
                    System.Diagnostics.Debug.WriteLine($"Authentication successful for user: {authResult.User.Name}");
                    return true;
                }

                System.Diagnostics.Debug.WriteLine("Authentication failed: Invalid response");
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Authentication error: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_accessToken) && !string.IsNullOrEmpty(_userId))
            {
                return true;
            }
            return await AuthenticateAsync();
        }

        public void UpdateAuthorizationHeader()
        {
            var authHeader = $"MediaBrowser Client=\"{CLIENT_NAME}\", Device=\"Windows\", DeviceId=\"{DEVICE_ID}\", Version=\"{VERSION}\"";
            if (!string.IsNullOrEmpty(_accessToken))
            {
                authHeader += $", Token=\"{_accessToken}\"";
            }
            
            // Remove existing header if present
            if (_httpClient.DefaultRequestHeaders.Contains("X-Emby-Authorization"))
            {
                _httpClient.DefaultRequestHeaders.Remove("X-Emby-Authorization");
            }
            
            _httpClient.DefaultRequestHeaders.Add("X-Emby-Authorization", authHeader);
        }

        private class AuthenticationResult
        {
            public string AccessToken { get; set; }
            public UserInfo User { get; set; }
        }

        private class UserInfo
        {
            public string Id { get; set; }
            public string Name { get; set; }
        }
    }
} 