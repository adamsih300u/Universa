using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Models
{
    /// <summary>
    /// Represents a TODO state color configuration item for the settings UI
    /// </summary>
    public class StateColorItem : INotifyPropertyChanged
    {
        private string _stateName;
        private string _colorHex;
        private SolidColorBrush _colorBrush;

        public event PropertyChangedEventHandler PropertyChanged;

        public string StateName
        {
            get => _stateName;
            set
            {
                if (_stateName != value)
                {
                    _stateName = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ColorHex
        {
            get => _colorHex;
            set
            {
                if (_colorHex != value)
                {
                    _colorHex = value;
                    OnPropertyChanged();
                    
                    // Update the brush based on hex value
                    if (IsValidHexColor(value))
                    {
                        try
                        {
                            var color = (Color)ColorConverter.ConvertFromString(value);
                            ColorBrush = new SolidColorBrush(color);
                            OnPropertyChanged(nameof(ContrastTextColor));
                            
                            // Save to configuration immediately
                            ConfigurationProvider.Instance.SetStateColor(StateName, value);
                        }
                        catch
                        {
                            // Invalid color, keep current brush
                        }
                    }
                }
            }
        }

        public SolidColorBrush ColorBrush
        {
            get => _colorBrush;
            set
            {
                if (_colorBrush != value)
                {
                    _colorBrush = value;
                    OnPropertyChanged();
                }
            }
        }

        public Color ContrastTextColor
        {
            get
            {
                if (ColorBrush?.Color == null) return Colors.Black;
                
                var color = ColorBrush.Color;
                // Calculate luminance using relative luminance formula
                var luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
                
                // Use white text for dark backgrounds, black text for light backgrounds
                return luminance > 0.5 ? Colors.Black : Colors.White;
            }
        }

        public StateColorItem(string stateName, string colorHex)
        {
            _stateName = stateName;
            _colorHex = colorHex;
            UpdateColorBrush();
        }

        private void UpdateColorBrush()
        {
            try
            {
                if (!string.IsNullOrEmpty(_colorHex))
                {
                    var color = (Color)ColorConverter.ConvertFromString(_colorHex);
                    ColorBrush = new SolidColorBrush(color);
                }
                else
                {
                    ColorBrush = new SolidColorBrush(Colors.Gray);
                }
            }
            catch
            {
                ColorBrush = new SolidColorBrush(Colors.Gray);
            }
        }

        private void SaveToConfiguration()
        {
            if (!string.IsNullOrEmpty(StateName) && !string.IsNullOrEmpty(ColorHex))
            {
                ConfigurationProvider.Instance.SetStateColor(StateName, ColorHex);
            }
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool IsValidHexColor(string hexColor)
        {
            if (string.IsNullOrEmpty(hexColor))
                return false;

            // Check if it starts with # and has the right length
            if (!hexColor.StartsWith("#") || (hexColor.Length != 7 && hexColor.Length != 9))
                return false;

            // Check if all characters after # are valid hex digits
            for (int i = 1; i < hexColor.Length; i++)
            {
                char c = hexColor[i];
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    return false;
            }

            return true;
        }
    }
} 