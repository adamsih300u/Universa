using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Universa.Desktop.Converters
{
    /// <summary>
    /// Provides attached properties for ComboBox controls
    /// </summary>
    public static class ComboBoxExtensions
    {
        /// <summary>
        /// Attached property for setting a DataTemplate for the Tag property
        /// </summary>
        public static readonly DependencyProperty TagTemplateProperty =
            DependencyProperty.RegisterAttached(
                "TagTemplate",
                typeof(DataTemplate),
                typeof(ComboBoxExtensions),
                new PropertyMetadata(null));

        /// <summary>
        /// Gets the TagTemplate property value
        /// </summary>
        public static DataTemplate GetTagTemplate(DependencyObject obj)
        {
            return (DataTemplate)obj.GetValue(TagTemplateProperty);
        }

        /// <summary>
        /// Sets the TagTemplate property value
        /// </summary>
        public static void SetTagTemplate(DependencyObject obj, DataTemplate value)
        {
            obj.SetValue(TagTemplateProperty, value);
        }
    }
} 