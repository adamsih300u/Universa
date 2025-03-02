using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    public class WeatherService : IWeatherService, IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly HttpClient _httpClient;
        private bool _isDisposed;
        private const string WeatherApiBaseUrl = "http://api.openweathermap.org/data/2.5/weather";

        public event EventHandler<WeatherUpdateEventArgs> WeatherUpdated;

        public WeatherService(IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            _httpClient = new HttpClient();

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            RefreshConfiguration();
        }

        public void RefreshConfiguration()
        {
            // Refresh weather data when configuration changes
            _ = UpdateWeatherAsync();
        }

        public async Task UpdateWeatherAsync()
        {
            if (!_config.EnableWeather)
            {
                return;
            }

            try
            {
                var apiKey = _config.WeatherApiKey;
                var zipCode = _config.WeatherZipCode;

                if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(zipCode))
                {
                    OnWeatherUpdated(null, "Weather API key or ZIP code not configured");
                    return;
                }

                var url = $"{WeatherApiBaseUrl}?zip={zipCode},us&units=imperial&appid={apiKey}";
                var response = await _httpClient.GetStringAsync(url);
                
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var weather = JsonSerializer.Deserialize<WeatherResponse>(response, options);
                
                if (weather?.Weather == null || weather.Weather.Length == 0 || weather.Main == null)
                {
                    OnWeatherUpdated(null, "Invalid weather data received");
                    return;
                }

                var weatherData = new WeatherData
                {
                    Temperature = weather.Main.Temp,
                    Condition = weather.Weather[0].Description,
                    Icon = GetWeatherEmoji(weather.Weather[0].Id),
                    MoonPhase = CalculateMoonPhase(DateTime.UtcNow)
                };

                OnWeatherUpdated(weatherData, null);
            }
            catch (Exception ex)
            {
                OnWeatherUpdated(null, ex.Message);
            }
        }

        private string GetWeatherEmoji(int weatherId)
        {
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

        private double CalculateMoonPhase(DateTime date)
        {
            // Known new moon date
            var newMoon = new DateTime(2024, 1, 11, 11, 57, 0, DateTimeKind.Utc);
            
            // Calculate days since new moon
            var daysSinceNewMoon = (date.ToUniversalTime() - newMoon).TotalDays % 29.53059;
            
            // Convert to phase (0-1)
            return daysSinceNewMoon / 29.53059;
        }

        private void OnWeatherUpdated(WeatherData data, string error)
        {
            WeatherUpdated?.Invoke(this, new WeatherUpdateEventArgs(data, error));
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _httpClient.Dispose();
                _isDisposed = true;
            }
        }
    }

    public class WeatherUpdateEventArgs : EventArgs
    {
        public WeatherData Data { get; }
        public string Error { get; }

        public WeatherUpdateEventArgs(WeatherData data, string error)
        {
            Data = data;
            Error = error;
        }
    }

    public class WeatherData
    {
        public double Temperature { get; set; }
        public string Condition { get; set; }
        public string Icon { get; set; }
        public double MoonPhase { get; set; }
    }

    internal class WeatherResponse
    {
        public MainInfo Main { get; set; }
        public WeatherInfo[] Weather { get; set; }
    }

    internal class MainInfo
    {
        public float Temp { get; set; }
        public float FeelsLike { get; set; }
        public float TempMin { get; set; }
        public float TempMax { get; set; }
        public int Humidity { get; set; }
    }

    internal class WeatherInfo
    {
        public int Id { get; set; }
        public string Main { get; set; }
        public string Description { get; set; }
        public string Icon { get; set; }
    }
} 