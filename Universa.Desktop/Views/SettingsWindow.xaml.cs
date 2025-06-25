using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Universa.Desktop.Services;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.ViewModels;
using Universa.Desktop.Core.Theme;
using System.Diagnostics;

namespace Universa.Desktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsViewModel _viewModel;
        private readonly IConfigurationService _configService;

        public SettingsWindow(IConfigurationService configService)
        {
            InitializeComponent();
            _configService = configService;
            
            // Create and set the ViewModel
            var dialogService = ServiceLocator.Instance.GetRequiredService<IDialogService>();
            _viewModel = new SettingsViewModel(configService, dialogService);
            DataContext = _viewModel;

            // Set up password box handlers
            WeatherApiKeyBox.PasswordChanged += (s, e) => _viewModel.UpdateWeatherApiKey(WeatherApiKeyBox.Password);
            OpenAIKeyBox.PasswordChanged += (s, e) => _viewModel.UpdateOpenAIKey(OpenAIKeyBox.Password);
            AnthropicKeyBox.PasswordChanged += (s, e) => _viewModel.UpdateAnthropicKey(AnthropicKeyBox.Password);
            XAIKeyBox.PasswordChanged += (s, e) => _viewModel.UpdateXAIKey(XAIKeyBox.Password);
            OpenRouterKeyBox.PasswordChanged += (s, e) => _viewModel.UpdateOpenRouterKey(OpenRouterKeyBox.Password);
            MatrixPasswordBox.PasswordChanged += (s, e) => _viewModel.UpdateMatrixPassword(MatrixPasswordBox.Password);
            SubsonicPasswordBox.PasswordChanged += (s, e) => _viewModel.UpdateSubsonicPassword(SubsonicPasswordBox.Password);
            JellyfinPasswordBox.PasswordChanged += (s, e) => _viewModel.UpdateJellyfinPassword(JellyfinPasswordBox.Password);
            AudiobookshelfPasswordBox.PasswordChanged += (s, e) => _viewModel.UpdateAudiobookshelfPassword(AudiobookshelfPasswordBox.Password);
            SyncPasswordBox.PasswordChanged += (s, e) => _viewModel.UpdateSyncPassword(SyncPasswordBox.Password);

            // Load initial password values
            LoadPasswords();

            // Apply current theme
            ApplyCurrentTheme();

            // Subscribe to configuration changes
            _configService.ConfigurationChanged += OnConfigurationChanged;

            // Handle window closing
            _viewModel.RequestClose += (result) =>
            {
                DialogResult = result;
                Close();
            };

            // Set window theme safely
            try
            {
                var currentTheme = _configService.Provider.CurrentTheme ?? "Light"; // Default to Light if null
                ThemeManager.SetWindowTheme(this, currentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error setting window theme: {ex.Message}");
                // Default to Light theme if there's an error
                ThemeManager.SetWindowTheme(this, false);
            }
        }

        private void OnConfigurationChanged(object sender, ConfigurationChangedEventArgs e)
        {
            if (e.Key == ConfigurationKeys.Theme.Current)
            {
                ApplyCurrentTheme();
                ThemeManager.SetWindowTheme(this, _configService.Provider.CurrentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase));
            }
            else if (e.Key == ConfigurationKeys.Library.Path)
            {
                // Notify the main window to refresh the library
                var mainWindow = Application.Current.MainWindow as MainWindow;
                mainWindow?.LibraryNavigator?.RefreshItems(false);
            }
        }

        private void ApplyCurrentTheme()
        {
            var theme = _configService.Provider.GetTheme(_configService.Provider.CurrentTheme);
            if (theme != null)
            {
                // Apply theme colors to the window - child elements will inherit through bindings
                Background = new SolidColorBrush(theme.WindowBackground);
                Foreground = new SolidColorBrush(theme.ContentForeground);

                // Update resources for light/dark specific colors
                Resources["ThemeBackground"] = new SolidColorBrush(theme.WindowBackground);
                Resources["ThemeForeground"] = new SolidColorBrush(theme.ContentForeground);
                Resources["ThemeControlBackground"] = new SolidColorBrush(theme.MenuBackground);
                Resources["ThemeControlForeground"] = new SolidColorBrush(theme.MenuForeground);
                Resources["ThemeBorderBrush"] = new SolidColorBrush(theme.BorderColor);

                // Update application-wide resources
                Application.Current.Resources["WindowBackgroundBrush"] = new SolidColorBrush(theme.WindowBackground);
                Application.Current.Resources["TextBrush"] = new SolidColorBrush(theme.ContentForeground);
                Application.Current.Resources["MenuBackgroundBrush"] = new SolidColorBrush(theme.MenuBackground);
                Application.Current.Resources["MenuForeground"] = new SolidColorBrush(theme.MenuForeground);
                Application.Current.Resources["BorderBrush"] = new SolidColorBrush(theme.BorderColor);
                Application.Current.Resources["TabBackground"] = new SolidColorBrush(theme.TabBackground);
                Application.Current.Resources["TabForeground"] = new SolidColorBrush(theme.TabForeground);

                // Force refresh of MainWindow
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.Background = new SolidColorBrush(theme.WindowBackground);
                    mainWindow.Foreground = new SolidColorBrush(theme.ContentForeground);
                }
            }
        }

        private void LoadPasswords()
        {
            // Load passwords from configuration
            if (!string.IsNullOrEmpty(_viewModel.WeatherApiKey))
                WeatherApiKeyBox.Password = _viewModel.WeatherApiKey;

            if (!string.IsNullOrEmpty(_viewModel.OpenAIApiKey))
                OpenAIKeyBox.Password = _viewModel.OpenAIApiKey;

            if (!string.IsNullOrEmpty(_viewModel.AnthropicApiKey))
                AnthropicKeyBox.Password = _viewModel.AnthropicApiKey;

            if (!string.IsNullOrEmpty(_viewModel.XAIApiKey))
                XAIKeyBox.Password = _viewModel.XAIApiKey;

            if (!string.IsNullOrEmpty(_viewModel.OpenRouterApiKey))
                OpenRouterKeyBox.Password = _viewModel.OpenRouterApiKey;

            if (!string.IsNullOrEmpty(_viewModel.SyncPassword))
                SyncPasswordBox.Password = _viewModel.SyncPassword;

            if (!string.IsNullOrEmpty(_viewModel.MatrixPassword))
                MatrixPasswordBox.Password = _viewModel.MatrixPassword;

            if (!string.IsNullOrEmpty(_viewModel.SubsonicPassword))
                SubsonicPasswordBox.Password = _viewModel.SubsonicPassword;

            if (!string.IsNullOrEmpty(_viewModel.JellyfinPassword))
                JellyfinPasswordBox.Password = _viewModel.JellyfinPassword;

            if (!string.IsNullOrEmpty(_viewModel.AudiobookshelfPassword))
                AudiobookshelfPasswordBox.Password = _viewModel.AudiobookshelfPassword;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SavePasswords();
            DialogResult = true;
            Close();
        }

        private void SavePasswords()
        {
            // Save passwords to configuration
            _viewModel.SavePassword("WeatherApiKey", WeatherApiKeyBox.Password);
            _viewModel.SavePassword("OpenAIApiKey", OpenAIKeyBox.Password);
            _viewModel.SavePassword("AnthropicApiKey", AnthropicKeyBox.Password);
            _viewModel.SavePassword("XAIApiKey", XAIKeyBox.Password);
            _viewModel.SavePassword("OpenRouterApiKey", OpenRouterKeyBox.Password);
            _viewModel.SavePassword("SyncPassword", SyncPasswordBox.Password);
            _viewModel.SavePassword("MatrixPassword", MatrixPasswordBox.Password);
            _viewModel.SavePassword("SubsonicPassword", SubsonicPasswordBox.Password);
            _viewModel.SavePassword("JellyfinPassword", JellyfinPasswordBox.Password);
            _viewModel.SavePassword("AudiobookshelfPassword", AudiobookshelfPasswordBox.Password);
        }

        // TODO Tags management event handlers
        private void AddTodoTag_Click(object sender, RoutedEventArgs e)
        {
            var newTag = NewTagTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(newTag) && !_viewModel.TodoTags.Contains(newTag))
            {
                _viewModel.TodoTags.Add(newTag);
                NewTagTextBox.Text = string.Empty;
            }
        }

        private void RemoveTodoTag_Click(object sender, RoutedEventArgs e)
        {
            if (TodoTagsListBox.SelectedItem is string selectedTag)
            {
                _viewModel.TodoTags.Remove(selectedTag);
            }
        }

        private void NewTagTextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                AddTodoTag_Click(sender, e);
                e.Handled = true;
            }
        }
    }

    public static class WpfHelper
    {
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }
    }
} 