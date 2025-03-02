using System;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Core.Configuration
{
    public class ConfigurationValue<T>
    {
        private T _value;
        private readonly T _defaultValue;
        private readonly Func<T, bool> _validator;
        private readonly bool _isRequired;
        private readonly string _validationRegex;

        public T Value
        {
            get => _value;
            set
            {
                if (!Validate(value))
                {
                    throw new ArgumentException($"Invalid value for configuration: {value}");
                }
                _value = value;
            }
        }

        public ConfigurationValue(T defaultValue = default, bool isRequired = false, string validationRegex = null, Func<T, bool> validator = null)
        {
            _defaultValue = defaultValue;
            _value = defaultValue;
            _isRequired = isRequired;
            _validationRegex = validationRegex;
            _validator = validator;
        }

        private bool Validate(T value)
        {
            if (_isRequired && value == null)
            {
                return false;
            }

            if (_validator != null && !_validator(value))
            {
                return false;
            }

            if (!string.IsNullOrEmpty(_validationRegex) && value is string strValue)
            {
                return Regex.IsMatch(strValue, _validationRegex);
            }

            return true;
        }

        public void Reset()
        {
            _value = _defaultValue;
        }

        public bool IsValid => Validate(_value);

        public override string ToString()
        {
            return _value?.ToString() ?? string.Empty;
        }
    }
} 