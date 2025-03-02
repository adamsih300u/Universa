using System;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Threading.Tasks;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;

namespace Universa.Desktop.Managers
{
    public class WeatherManager : IDisposable
    {
        private readonly IConfigurationService _configService;
        private readonly ConfigurationProvider _config;
        private readonly WeatherService _weatherService;
        private readonly TextBlock _weatherDisplay;
        private readonly TextBlock _moonPhaseDisplay;
        private readonly TextBlock _moonPhaseDescription;
        private readonly DispatcherTimer _updateTimer;
        private bool _isDisposed;

        public WeatherManager(TextBlock weatherDisplay, TextBlock moonPhaseDisplay, TextBlock moonPhaseDescription, IConfigurationService configService)
        {
            _configService = configService;
            _config = _configService.Provider;
            _weatherDisplay = weatherDisplay;
            _moonPhaseDisplay = moonPhaseDisplay;
            _moonPhaseDescription = moonPhaseDescription;

            // Initialize weather service
            _weatherService = new WeatherService(_configService);
            _weatherService.WeatherUpdated += OnWeatherUpdated;

            // Initialize update timer
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30)
            };
            _updateTimer.Tick += async (s, e) => await UpdateWeather();

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;

            // Initial update
            UpdateWeatherDisplay();
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            if (e.Key == nameof(ConfigurationProvider.WeatherApiKey) || 
                e.Key == nameof(ConfigurationProvider.WeatherZipCode) || 
                e.Key == nameof(ConfigurationProvider.EnableWeather))
            {
                UpdateWeatherDisplay();
            }
        }

        private void UpdateWeatherDisplay()
        {
            if (_config.EnableWeather)
            {
                _weatherDisplay.Visibility = System.Windows.Visibility.Visible;
                _updateTimer.Start();
                _ = UpdateWeather();
            }
            else
            {
                _weatherDisplay.Visibility = System.Windows.Visibility.Collapsed;
                _moonPhaseDisplay.Visibility = System.Windows.Visibility.Collapsed;
                _moonPhaseDescription.Visibility = System.Windows.Visibility.Collapsed;
                _updateTimer.Stop();
            }
        }

        public async Task UpdateWeather()
        {
            if (!_config.EnableWeather)
            {
                return;
            }

            await _weatherService.UpdateWeatherAsync();
        }

        private void OnWeatherUpdated(object sender, WeatherUpdateEventArgs e)
        {
            _weatherDisplay.Dispatcher.Invoke(() =>
            {
                if (e.Error != null)
                {
                    _weatherDisplay.Text = $"Weather Error: {e.Error}";
                    return;
                }

                if (e.Data != null)
                {
                    _weatherDisplay.Text = $"{e.Data.Icon} {e.Data.Temperature:F0}°F {e.Data.Condition}";

                    if (_config.EnableMoonPhase)
                    {
                        _moonPhaseDisplay.Visibility = System.Windows.Visibility.Visible;
                        _moonPhaseDescription.Visibility = System.Windows.Visibility.Visible;
                        UpdateMoonPhase(e.Data.MoonPhase);
                    }
                    else
                    {
                        _moonPhaseDisplay.Visibility = System.Windows.Visibility.Collapsed;
                        _moonPhaseDescription.Visibility = System.Windows.Visibility.Collapsed;
                    }
                }
            });
        }

        private void UpdateMoonPhase(double phase)
        {
            var (symbol, description) = GetMoonPhaseInfo(phase);
            _moonPhaseDisplay.Text = symbol;
            _moonPhaseDescription.Text = description;
        }

        private (string Symbol, string Description) GetMoonPhaseInfo(double phase)
        {
            return phase switch
            {
                var p when p < 0.125 => ("🌑", "New Moon"),
                var p when p < 0.25 => ("🌒", "Waxing Crescent"),
                var p when p < 0.375 => ("🌓", "First Quarter"),
                var p when p < 0.625 => ("🌔", "Waxing Gibbous"),
                var p when p < 0.75 => ("🌕", "Full Moon"),
                var p when p < 0.875 => ("🌖", "Waning Gibbous"),
                var p when p < 1 => ("🌗", "Last Quarter"),
                _ => ("🌘", "Waning Crescent")
            };
        }

        public void RefreshConfiguration()
        {
            UpdateWeatherDisplay();
            _ = UpdateWeather();
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _updateTimer.Stop();
                _weatherService.Dispose();
                _isDisposed = true;
            }
        }
    }
} 