using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows;
using Universa.Desktop.Services;
using Universa.Desktop.Commands;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.ViewModels
{
    public class ThemeViewModel : INotifyPropertyChanged
    {
        private readonly IConfigurationService _config;
        private readonly IDialogService _dialogService;
        private bool _isInitializing;
        private string _selectedTheme;

        public event PropertyChangedEventHandler PropertyChanged;

        public ThemeViewModel()
        {
            _config = ServiceLocator.Instance.GetService<IConfigurationService>();
            _dialogService = ServiceLocator.Instance.GetService<IDialogService>();
            _selectedTheme = _config.Provider.CurrentTheme;

            // Initialize commands
            SaveThemeCommand = new RelayCommand(_ => SaveTheme());
            DuplicateThemeCommand = new RelayCommand(_ => DuplicateTheme());
            DeleteThemeCommand = new RelayCommand(_ => DeleteTheme());
            SetLightThemeCommand = new RelayCommand(_ => SetTheme("Light"));
            SetDarkThemeCommand = new RelayCommand(_ => SetTheme("Dark"));
            SetSystemThemeCommand = new RelayCommand(_ => SetTheme("System"));

            // Initialize themes collection
            Themes = new List<string> { "Light", "Dark", "System" };

            LoadThemes();
        }

        #region Commands
        public ICommand SaveThemeCommand { get; }
        public ICommand DuplicateThemeCommand { get; }
        public ICommand DeleteThemeCommand { get; }
        public ICommand SetLightThemeCommand { get; }
        public ICommand SetDarkThemeCommand { get; }
        public ICommand SetSystemThemeCommand { get; }
        #endregion

        #region Properties
        private List<string> _themes;
        public List<string> Themes
        {
            get => _themes;
            set
            {
                _themes = value;
                OnPropertyChanged(nameof(Themes));
            }
        }

        public string SelectedTheme
        {
            get => _selectedTheme;
            set
            {
                if (_selectedTheme != value)
                {
                    _selectedTheme = value;
                    _config.Provider.CurrentTheme = value;
                    OnPropertyChanged(nameof(SelectedTheme));
                    LoadThemeColors();
                }
            }
        }

        private string _customThemeName;
        public string CustomThemeName
        {
            get => _customThemeName;
            set
            {
                _customThemeName = value;
                OnPropertyChanged(nameof(CustomThemeName));
            }
        }

        public Color WindowBackground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "WindowBackground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "WindowBackground", value);
                OnPropertyChanged(nameof(WindowBackground));
            }
        }

        public Color MenuBackground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "MenuBackground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "MenuBackground", value);
                OnPropertyChanged(nameof(MenuBackground));
            }
        }

        public Color MenuForeground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "MenuForeground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "MenuForeground", value);
                OnPropertyChanged(nameof(MenuForeground));
            }
        }

        public Color TabBackground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "TabBackground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "TabBackground", value);
                OnPropertyChanged(nameof(TabBackground));
            }
        }

        public Color TabForeground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "TabForeground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "TabForeground", value);
                OnPropertyChanged(nameof(TabForeground));
            }
        }

        public Color ActiveTabBackground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "ActiveTabBackground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "ActiveTabBackground", value);
                OnPropertyChanged(nameof(ActiveTabBackground));
            }
        }

        public Color ActiveTabForeground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "ActiveTabForeground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "ActiveTabForeground", value);
                OnPropertyChanged(nameof(ActiveTabForeground));
            }
        }

        public Color ContentBackground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "ContentBackground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "ContentBackground", value);
                OnPropertyChanged(nameof(ContentBackground));
            }
        }

        public Color ContentForeground
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "ContentForeground");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "ContentForeground", value);
                OnPropertyChanged(nameof(ContentForeground));
            }
        }

        public Color AccentColor
        {
            get => _config.Provider.GetThemeColor(SelectedTheme, "AccentColor");
            set
            {
                _config.Provider.SetThemeColor(SelectedTheme, "AccentColor", value);
                OnPropertyChanged(nameof(AccentColor));
            }
        }
        #endregion

        #region Methods
        private void LoadThemes()
        {
            _isInitializing = true;
            try
            {
                var availableThemes = _config.Provider.GetAvailableThemes();
                Themes = new List<string>(availableThemes);
                SelectedTheme = _config.Provider.CurrentTheme;
                LoadThemeColors();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void LoadThemeColors()
        {
            if (_isInitializing || string.IsNullOrEmpty(SelectedTheme)) return;

            // Notify all color properties to update
            OnPropertyChanged(nameof(WindowBackground));
            OnPropertyChanged(nameof(MenuBackground));
            OnPropertyChanged(nameof(MenuForeground));
            OnPropertyChanged(nameof(TabBackground));
            OnPropertyChanged(nameof(TabForeground));
            OnPropertyChanged(nameof(ActiveTabBackground));
            OnPropertyChanged(nameof(ActiveTabForeground));
            OnPropertyChanged(nameof(ContentBackground));
            OnPropertyChanged(nameof(ContentForeground));
            OnPropertyChanged(nameof(AccentColor));
        }

        private void SaveTheme()
        {
            try
            {
                _config.Save();
                _dialogService.ShowMessage("Theme saved successfully.", "Success");
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Error saving theme: {ex.Message}", "Error");
            }
        }

        private void DuplicateTheme()
        {
            if (string.IsNullOrWhiteSpace(CustomThemeName))
            {
                _dialogService.ShowError("Please enter a name for the new theme.", "Invalid Input");
                return;
            }

            if (Themes.Contains(CustomThemeName))
            {
                _dialogService.ShowError("A theme with this name already exists.", "Error");
                return;
            }

            try
            {
                _config.Provider.DuplicateTheme(SelectedTheme, CustomThemeName);
                LoadThemes();
                SelectedTheme = CustomThemeName;
                CustomThemeName = string.Empty;
            }
            catch (Exception ex)
            {
                _dialogService.ShowError($"Failed to duplicate theme: {ex.Message}", "Error");
            }
        }

        private void DeleteTheme()
        {
            if (SelectedTheme == "Light" || SelectedTheme == "Dark" || SelectedTheme == "System")
            {
                _dialogService.ShowError("Cannot delete built-in themes.", "Invalid Operation");
                return;
            }

            if (_dialogService.ShowConfirmation($"Are you sure you want to delete the theme '{SelectedTheme}'?", "Confirm Delete"))
            {
                try
                {
                    _config.Provider.DeleteTheme(SelectedTheme);
                    LoadThemes();
                    SelectedTheme = "Light";
                }
                catch (Exception ex)
                {
                    _dialogService.ShowError($"Failed to delete theme: {ex.Message}", "Error");
                }
            }
        }

        private void SetTheme(string themeName)
        {
            _config.Provider.CurrentTheme = themeName;
            _selectedTheme = themeName;
            OnPropertyChanged(nameof(SelectedTheme));
            
            // Apply theme changes immediately
            var mainWindow = Application.Current.MainWindow as Views.MainWindow;
            if (mainWindow != null)
            {
                mainWindow.ApplyTheme(themeName);
            }
            
            // Save the configuration and let the ConfigurationManager handle the change notification
            _config.Save();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
} 