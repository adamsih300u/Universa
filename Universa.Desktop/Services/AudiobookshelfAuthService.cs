using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Handles authentication with Audiobookshelf server
    /// </summary>
    public class AudiobookshelfAuthService : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private string _token;
        private DateTime _tokenExpiry;
        private bool _disposed;

        public string Token => _token;
        public bool IsAuthenticated => !string.IsNullOrEmpty(_token) && DateTime.UtcNow < _tokenExpiry;

        public AudiobookshelfAuthService(HttpClient client, string baseUrl, string username, string password)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _baseUrl = baseUrl?.TrimEnd('/') ?? throw new ArgumentNullException(nameof(baseUrl));
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        /// <summary>
        /// Authenticates with the Audiobookshelf server and obtains an access token
        /// </summary>
        public async Task<bool> LoginAsync()
        {
            try
            {
                var loginUrl = $"{_baseUrl}/login";
                Debug.WriteLine($"Attempting to login to Audiobookshelf at {loginUrl}");

                var loginRequest = new LoginRequest
                {
                    Username = _username,
                    Password = _password
                };

                var response = await _client.PostAsJsonAsync(loginUrl, loginRequest);
                response.EnsureSuccessStatusCode();

                var rawContent = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"Raw login response: {rawContent}");

                var options = new JsonSerializerOptions
                {
                    NumberHandling = JsonNumberHandling.AllowReadingFromString
                };

                var loginResponse = JsonSerializer.Deserialize<LoginResponse>(rawContent, options);

                if (loginResponse?.User?.Token == null)
                {
                    Debug.WriteLine("Login failed - no token in response");
                    return false;
                }

                _token = loginResponse.User.Token;
                _tokenExpiry = DateTime.UtcNow.AddHours(12); // Assume 12-hour token validity
                
                // Set authorization header for future requests
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                
                Debug.WriteLine("Login successful, token obtained");
                return true;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Network error during login: {ex.Message}");
                return false;
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Invalid JSON response during login: {ex.Message}");
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unexpected error during login: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Ensures the client is authenticated, logging in if necessary
        /// </summary>
        public async Task<bool> EnsureAuthenticatedAsync()
        {
            if (IsAuthenticated)
            {
                return true;
            }

            Debug.WriteLine("Token expired or missing, attempting to re-authenticate");
            return await LoginAsync();
        }

        /// <summary>
        /// Clears the current authentication token
        /// </summary>
        public void Logout()
        {
            _token = null;
            _tokenExpiry = DateTime.MinValue;
            _client.DefaultRequestHeaders.Authorization = null;
            Debug.WriteLine("Logged out successfully");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                Logout();
                _disposed = true;
            }
        }
    }
} 