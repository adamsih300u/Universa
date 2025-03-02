using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using System.Text.Json.Serialization;
using Universa.Desktop.Models;

namespace Universa.Desktop.Helpers
{
    public class WeatherService
    {
        private readonly HttpClient _client;
        private readonly Models.Configuration _config;
        private const string WeatherApiBaseUrl = "http://api.openweathermap.org/data/2.5/weather";

        public WeatherService()
        {
            _client = new HttpClient();
            _config = Models.Configuration.Instance;
        }

        public async Task<string> GetWeatherAsync(string zipCode)
        {
            var config = Models.Configuration.Instance;
            Debug.WriteLine("Attempting to get weather data...");
            Debug.WriteLine($"API Key configured: {!string.IsNullOrEmpty(config.WeatherApiKey)}");
            Debug.WriteLine($"ZIP Code: {zipCode}");

            if (string.IsNullOrEmpty(config.WeatherApiKey))
            {
                Debug.WriteLine("Weather API key not configured");
                return "Weather API key not configured";
            }

            if (string.IsNullOrEmpty(zipCode))
            {
                Debug.WriteLine("ZIP code not configured");
                return "ZIP code not configured";
            }

            try
            {
                var url = $"{WeatherApiBaseUrl}?zip={zipCode},us&units=imperial&appid={config.WeatherApiKey}";
                Debug.WriteLine($"Requesting weather from: {url.Replace(config.WeatherApiKey, "[API_KEY]")}");
                
                var response = await _client.GetStringAsync(url);
                Debug.WriteLine($"Weather API Response: {response}");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var weather = JsonSerializer.Deserialize<WeatherResponse>(response, options);
                Debug.WriteLine($"Weather object: Main={weather?.Main != null}, Weather={weather?.Weather != null}, WeatherLength={weather?.Weather?.Length ?? 0}");
                
                if (weather?.Weather == null || weather.Weather.Length == 0)
                {
                    Debug.WriteLine("Weather array is null or empty");
                    return "Invalid weather data received";
                }

                if (weather.Main == null)
                {
                    Debug.WriteLine("Main weather info is null");
                    return "Invalid weather data received";
                }

                string weatherEmoji = GetWeatherEmoji(weather.Weather[0].Id);
                var result = $"{weatherEmoji} {weather.Main.Temp:F0}°F {weather.Weather[0].Description}";
                Debug.WriteLine($"Formatted weather result: {result}");
                return result;
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Weather HTTP error: {ex}");
                return $"Weather service unavailable: {ex.Message}";
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"Weather JSON error: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return "Invalid weather data format";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Weather error: {ex}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return "Weather unavailable";
            }
        }

        private string GetWeatherEmoji(int weatherId)
        {
            // Weather condition codes: https://openweathermap.org/weather-conditions
            return weatherId switch
            {
                >= 200 and < 300 => "⛈️",  // Thunderstorm
                >= 300 and < 400 => "🌧️",  // Drizzle
                >= 500 and < 600 => "🌧️",  // Rain
                >= 600 and < 700 => "🌨️",  // Snow
                >= 700 and < 800 => "🌫️",  // Atmosphere (fog, mist, etc.)
                800 => "☀️",                // Clear sky
                801 => "🌤️",               // Few clouds
                802 => "⛅",                // Scattered clouds
                803 or 804 => "☁️",        // Broken/overcast clouds
                _ => "❓"                   // Unknown
            };
        }

        public static (string emoji, string phase) GetMoonPhase(DateTime date)
        {
            // Known new moon date
            var newMoon = new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc);
            
            // Calculate days since new moon
            var daysSinceNewMoon = (date.ToUniversalTime() - newMoon).TotalDays % 29.53059;
            
            // Convert to phase (0-7)
            var phase = (int)Math.Floor((daysSinceNewMoon / 29.53059) * 8) % 8;

            return phase switch
            {
                0 => ("🌑", "new"),
                1 => ("🌒", "waxing_crescent"),
                2 => ("🌓", "first_quarter"),
                3 => ("🌔", "waxing_gibbous"),
                4 => ("🌕", "full"),
                5 => ("🌖", "waning_gibbous"),
                6 => ("🌗", "last_quarter"),
                7 => ("🌘", "waning_crescent"),
                _ => ("", "unknown")
            };
        }

        private class WeatherResponse
        {
            [JsonPropertyName("main")]
            public MainInfo Main { get; set; }
            
            [JsonPropertyName("weather")]
            public WeatherInfo[] Weather { get; set; }
        }

        private class MainInfo
        {
            [JsonPropertyName("temp")]
            public float Temp { get; set; }
            
            [JsonPropertyName("feels_like")]
            public float FeelsLike { get; set; }
            
            [JsonPropertyName("temp_min")]
            public float TempMin { get; set; }
            
            [JsonPropertyName("temp_max")]
            public float TempMax { get; set; }
            
            [JsonPropertyName("humidity")]
            public int Humidity { get; set; }
        }

        private class WeatherInfo
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }
            
            [JsonPropertyName("main")]
            public string Main { get; set; }
            
            [JsonPropertyName("description")]
            public string Description { get; set; }
            
            [JsonPropertyName("icon")]
            public string Icon { get; set; }
        }
    }
} 