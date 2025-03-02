using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Controls;
using Universa.Desktop.Models;

namespace Universa.Desktop
{
    public class MediaTypeValidationRule : ValidationRule
    {
        public string ValidTypes { get; set; }

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            if (value == null)
                return new ValidationResult(false, null);

            var mediaItem = value as MediaItem;
            if (mediaItem == null)
                return new ValidationResult(false, null);

            var validTypesList = ValidTypes?.Split(',') ?? Array.Empty<string>();
            foreach (var typeStr in validTypesList)
            {
                if (Enum.TryParse<MediaItemType>(typeStr.Trim(), true, out var validType) && mediaItem.Type == validType)
                    return ValidationResult.ValidResult;
            }

            return new ValidationResult(false, null);
        }
    }
} 