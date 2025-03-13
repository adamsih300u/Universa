using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Universa.Desktop.Services.Export;
using Universa.Desktop.Interfaces;
using System.Globalization;

namespace Universa.Desktop.Views
{
    /// <summary>
    /// Interaction logic for ExportWindow.xaml
    /// </summary>
    public partial class ExportWindow : Window
    {
        private readonly IFileTab _currentTab;
        private string _defaultFileName;
        private string _defaultDirectory;
        private Dictionary<string, string> _metadata;
        
        // Checkbox states
        private bool _includeToc = true;
        private bool _splitOnHeadings = true;
        private bool _includeCover = true;
        private bool[] _splitOnHeadingLevels = new bool[] { true, true, false, false, false, false };
        
        // Heading alignments
        private Dictionary<int, ExportOptions.TextAlignment> _headingAlignments = new Dictionary<int, ExportOptions.TextAlignment>();
        
        // Export options
        private ExportOptions _options;

        public ExportWindow(IFileTab currentTab)
        {
            // Initialize options first
            _options = new ExportOptions
            {
                IncludeToc = _includeToc,
                SplitOnHeadings = _splitOnHeadings,
                IncludeCover = _includeCover,
                HeadingAlignments = _headingAlignments
            };
            
            // Convert boolean array to list of integers for heading levels
            for (int i = 0; i < _splitOnHeadingLevels.Length; i++)
            {
                if (_splitOnHeadingLevels[i])
                {
                    _options.SplitOnHeadingLevels.Add(i + 1);
                }
            }

            // Temporarily remove event handlers to prevent them from firing during initialization
            InitializeComponent();

            // Initialize metadata dictionary
            _metadata = new Dictionary<string, string>();
            _currentTab = currentTab;

            // Set default file name and directory
            _defaultFileName = "Untitled";
            _defaultDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

            if (_currentTab != null && !string.IsNullOrEmpty(_currentTab.FilePath))
            {
                _defaultFileName = Path.GetFileNameWithoutExtension(_currentTab.FilePath);
                string directory = Path.GetDirectoryName(_currentTab.FilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    _defaultDirectory = directory;
                }
            }

            // Set default metadata values
            if (_currentTab is MarkdownTab markdownTab)
            {
                _metadata["title"] = _defaultFileName;
                _metadata["author"] = Environment.UserName;
                _metadata["language"] = CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
                _metadata["source"] = _currentTab.FilePath ?? "";
                _metadata["fontFamily"] = "Arial";
                _metadata["fontSize"] = "12";
            }

            // Initialize UI elements with values from metadata
            TitleTextBox.Text = _metadata.TryGetValue("title", out string title) ? title : _defaultFileName;
            AuthorTextBox.Text = _metadata.TryGetValue("author", out string author) ? author : Environment.UserName;
            
            // Set checkbox states
            UpdateCheckboxState(IncludeTocCheckmark, _includeToc);
            UpdateCheckboxState(SplitOnHeadingsCheckmark, _splitOnHeadings);
            UpdateCheckboxState(IncludeCoverCheckmark, _includeCover);
            
            // Set split on heading level checkboxes
            UpdateCheckboxState(SplitOnH1Checkmark, _splitOnHeadingLevels[0]);
            UpdateCheckboxState(SplitOnH2Checkmark, _splitOnHeadingLevels[1]);
            UpdateCheckboxState(SplitOnH3Checkmark, _splitOnHeadingLevels[2]);
            UpdateCheckboxState(SplitOnH4Checkmark, _splitOnHeadingLevels[3]);
            UpdateCheckboxState(SplitOnH5Checkmark, _splitOnHeadingLevels[4]);
            UpdateCheckboxState(SplitOnH6Checkmark, _splitOnHeadingLevels[5]);
            
            // Update the output path after all UI elements are initialized
            UpdateOutputPath();
            
            // Update the UI for split on headings
            UpdateSplitOnHeadingsUI();
        }
        
        private void FormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Add null checks to prevent NullReferenceException
            if (EpubOptionsPanel == null || PdfOptionsPanel == null || DocxOptionsPanel == null)
                return;
                
            // Update the output path with the appropriate extension
            UpdateOutputPath();
            
            // Show/hide format-specific options
            int selectedIndex = FormatComboBox?.SelectedIndex ?? 0;
            EpubOptionsPanel.Visibility = selectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            PdfOptionsPanel.Visibility = selectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            DocxOptionsPanel.Visibility = selectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            
            // If PDF is selected, ensure PDF-specific fields are initialized
            if (selectedIndex == 1)
            {
                // Ensure _metadata is initialized
                if (_metadata == null)
                {
                    _metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                }
                
                // Initialize PDF-specific fields if they haven't been already
                if (!_metadata.ContainsKey("pageSize"))
                {
                    _metadata["pageSize"] = "A4";
                }
                
                // Update UI elements
                if (PdfTitleTextBox != null && !string.IsNullOrEmpty(_metadata["title"]))
                {
                    PdfTitleTextBox.Text = _metadata["title"];
                }
                
                if (PdfAuthorTextBox != null && !string.IsNullOrEmpty(_metadata["author"]))
                {
                    PdfAuthorTextBox.Text = _metadata["author"];
                }
                
                // Set page size
                if (PageSizeComboBox != null)
                {
                    // Temporarily remove event handler
                    PageSizeComboBox.SelectionChanged -= PageSizeComboBox_SelectionChanged;
                    
                    // Set selected index based on page size
                    string pageSize = _metadata.GetValueOrDefault("pageSize", "A4");
                    switch (pageSize.ToUpper())
                    {
                        case "A3":
                            PageSizeComboBox.SelectedIndex = 1;
                            break;
                        case "A5":
                            PageSizeComboBox.SelectedIndex = 2;
                            break;
                        case "LETTER":
                            PageSizeComboBox.SelectedIndex = 3;
                            break;
                        case "LEGAL":
                            PageSizeComboBox.SelectedIndex = 4;
                            break;
                        default: // A4
                            PageSizeComboBox.SelectedIndex = 0;
                            break;
                    }
                    
                    // Reattach event handler
                    PageSizeComboBox.SelectionChanged += PageSizeComboBox_SelectionChanged;
                }
            }
        }
        
        private void UpdateOutputPath()
        {
            // If OutputPathTextBox is not initialized yet, exit early
            if (OutputPathTextBox == null)
                return;
                
            string extension = ".epub";
            
            if (FormatComboBox?.SelectedIndex == 1)
            {
                extension = ".pdf";
            }
            else if (FormatComboBox?.SelectedIndex == 2)
            {
                extension = ".docx";
            }
            
            // Ensure we have valid values for directory and filename
            string directory = _defaultDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string filename = _defaultFileName ?? "Untitled";
            
            string outputPath = Path.Combine(directory, filename + extension);
            OutputPathTextBox.Text = outputPath;
        }
        
        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            string extension = "epub";
            string filter = "ePub Files (*.epub)|*.epub";
            
            if (FormatComboBox.SelectedIndex == 1)
            {
                extension = "pdf";
                filter = "PDF Files (*.pdf)|*.pdf";
            }
            else if (FormatComboBox.SelectedIndex == 2)
            {
                extension = "docx";
                filter = "Word Documents (*.docx)|*.docx";
            }
            
            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = filter,
                DefaultExt = extension,
                AddExtension = true,
                FileName = _defaultFileName + "." + extension,
                InitialDirectory = _defaultDirectory
            };
            
            if (saveFileDialog.ShowDialog() == true)
            {
                OutputPathTextBox.Text = saveFileDialog.FileName;
            }
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private async void ExportButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Disable the export button to prevent multiple clicks
                ExportButton.IsEnabled = false;
                
                // Get the content to export
                string content = _currentTab?.GetContent() ?? string.Empty;
                
                // Check if the content is suitable for export
                if (content.Contains("not available for export"))
                {
                    MessageBox.Show(
                        "This type of content cannot be exported. Only markdown documents can be exported.",
                        "Export Not Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    
                    DialogResult = false;
                    Close();
                    return;
                }
                
                // Update metadata from UI based on selected format
                if (FormatComboBox.SelectedIndex == 0) // ePub
                {
                    if (TitleTextBox != null)
                        _metadata["title"] = TitleTextBox.Text;
                    
                    if (AuthorTextBox != null)
                        _metadata["author"] = AuthorTextBox.Text;
                    
                    if (LanguageTextBox != null)
                        _metadata["language"] = LanguageTextBox.Text;
                }
                else if (FormatComboBox.SelectedIndex == 1) // PDF
                {
                    if (PdfTitleTextBox != null)
                        _metadata["title"] = PdfTitleTextBox.Text;
                    
                    if (PdfAuthorTextBox != null)
                        _metadata["author"] = PdfAuthorTextBox.Text;
                    
                    // Add page size to metadata
                    if (PageSizeComboBox != null && PageSizeComboBox.SelectedItem is ComboBoxItem selectedItem)
                    {
                        _metadata["pageSize"] = selectedItem.Content.ToString();
                    }
                    
                    // Add font information from the current tab
                    if (_currentTab is MarkdownTab markdownTab)
                    {
                        _metadata["fontFamily"] = markdownTab.Editor.FontFamily.Source;
                        _metadata["fontSize"] = markdownTab.Editor.FontSize.ToString();
                    }
                }
                
                // Update the _options object with current values
                _options.OutputPath = OutputPathTextBox.Text;
                _options.IncludeToc = _includeToc;
                _options.SplitOnHeadings = _splitOnHeadings;
                _options.IncludeCover = _includeCover;
                _options.Metadata = new Dictionary<string, string>(_metadata); // Create a copy of the metadata
                _options.HeadingAlignments = new Dictionary<int, ExportOptions.TextAlignment>(_headingAlignments); // Copy heading alignments
                
                // Clear and update heading levels to split on
                _options.SplitOnHeadingLevels.Clear();
                for (int i = 0; i < _splitOnHeadingLevels.Length; i++)
                {
                    if (_splitOnHeadingLevels[i])
                    {
                        _options.SplitOnHeadingLevels.Add(i + 1);
                    }
                }
                
                // Create the appropriate exporter based on the selected format
                IExporter exporter;
                
                switch (FormatComboBox.SelectedIndex)
                {
                    case 0: // ePub
                        exporter = new CustomEpubExporter();
                        break;
                    case 1: // PDF
                        exporter = new PdfExporter();
                        break;
                    case 2: // DOCX
                        exporter = new DocxExporter();
                        break;
                    default:
                        exporter = new CustomEpubExporter();
                        break;
                }
                
                // Show a progress message
                StatusBar.Visibility = Visibility.Visible;
                StatusMessage.Text = "Exporting document...";
                
                // Perform the export
                bool success = await exporter.ExportAsync(content, _options);
                
                // Hide the status bar
                StatusBar.Visibility = Visibility.Collapsed;
                
                if (success)
                {
                    // Check if there were any warnings
                    if (_options.Warnings != null && _options.Warnings.Count > 0)
                    {
                        // Build the warning message
                        StringBuilder warningMessage = new StringBuilder();
                        warningMessage.AppendLine("Document exported with the following warnings:");
                        
                        foreach (string warning in _options.Warnings)
                        {
                            warningMessage.AppendLine($"• {warning}");
                        }
                        
                        MessageBox.Show(warningMessage.ToString(), 
                            "Export Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        MessageBox.Show($"Document exported successfully to:\n{_options.OutputPath}", 
                            "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    
                    // Ask if the user wants to open the exported file
                    var result = MessageBox.Show("Do you want to open the exported file?", 
                        "Open File", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            // Open the file with the default application
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = _options.OutputPath,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Error opening file: {ex.Message}");
                            MessageBox.Show($"Could not open the file: {ex.Message}", 
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                    
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Failed to export document. Please check the output path and try again.", 
                        "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    // Re-enable the export button
                    ExportButton.IsEnabled = true;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred during export: {ex.Message}", 
                    "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Re-enable the export button
                ExportButton.IsEnabled = true;
            }
        }
        
        private void IncludeTocButton_Click(object sender, RoutedEventArgs e)
        {
            _includeToc = !_includeToc;
            UpdateCheckboxState(IncludeTocCheckmark, _includeToc);
        }
        
        private void SplitOnHeadingsButton_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadings = !_splitOnHeadings;
            UpdateCheckboxState(SplitOnHeadingsCheckmark, _splitOnHeadings);
            UpdateSplitOnHeadingsUI();
        }
        
        private void IncludeCoverButton_Click(object sender, RoutedEventArgs e)
        {
            _includeCover = !_includeCover;
            UpdateCheckboxState(IncludeCoverCheckmark, _includeCover);
        }
        
        private void SplitOnH1Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[0] = !_splitOnHeadingLevels[0];
            UpdateCheckboxState(SplitOnH1Checkmark, _splitOnHeadingLevels[0]);
            
            // Update the options
            if (_splitOnHeadingLevels[0])
            {
                if (!_options.SplitOnHeadingLevels.Contains(1))
                {
                    _options.SplitOnHeadingLevels.Add(1);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(1);
            }
        }
        
        private void SplitOnH2Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[1] = !_splitOnHeadingLevels[1];
            UpdateCheckboxState(SplitOnH2Checkmark, _splitOnHeadingLevels[1]);
            
            // Update the options
            if (_splitOnHeadingLevels[1])
            {
                if (!_options.SplitOnHeadingLevels.Contains(2))
                {
                    _options.SplitOnHeadingLevels.Add(2);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(2);
            }
        }
        
        private void SplitOnH3Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[2] = !_splitOnHeadingLevels[2];
            UpdateCheckboxState(SplitOnH3Checkmark, _splitOnHeadingLevels[2]);
            
            // Update the options
            if (_splitOnHeadingLevels[2])
            {
                if (!_options.SplitOnHeadingLevels.Contains(3))
                {
                    _options.SplitOnHeadingLevels.Add(3);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(3);
            }
        }
        
        private void SplitOnH4Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[3] = !_splitOnHeadingLevels[3];
            UpdateCheckboxState(SplitOnH4Checkmark, _splitOnHeadingLevels[3]);
            
            // Update the options
            if (_splitOnHeadingLevels[3])
            {
                if (!_options.SplitOnHeadingLevels.Contains(4))
                {
                    _options.SplitOnHeadingLevels.Add(4);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(4);
            }
        }
        
        private void SplitOnH5Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[4] = !_splitOnHeadingLevels[4];
            UpdateCheckboxState(SplitOnH5Checkmark, _splitOnHeadingLevels[4]);
            
            // Update the options
            if (_splitOnHeadingLevels[4])
            {
                if (!_options.SplitOnHeadingLevels.Contains(5))
                {
                    _options.SplitOnHeadingLevels.Add(5);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(5);
            }
        }
        
        private void SplitOnH6Button_Click(object sender, RoutedEventArgs e)
        {
            if (_options == null) return;
            
            _splitOnHeadingLevels[5] = !_splitOnHeadingLevels[5];
            UpdateCheckboxState(SplitOnH6Checkmark, _splitOnHeadingLevels[5]);
            
            // Update the options
            if (_splitOnHeadingLevels[5])
            {
                if (!_options.SplitOnHeadingLevels.Contains(6))
                {
                    _options.SplitOnHeadingLevels.Add(6);
                }
            }
            else
            {
                _options.SplitOnHeadingLevels.Remove(6);
            }
        }
        
        private void UpdateSplitOnHeadingsUI()
        {
            // Update the main split on headings checkbox
            if (SplitOnHeadingsCheckmark != null)
            {
                UpdateCheckboxState(SplitOnHeadingsCheckmark, _splitOnHeadings);
            }
            
            // Enable/disable heading level buttons based on split on headings state
            bool enabled = _splitOnHeadings;
            
            if (SplitOnH1Button != null)
                SplitOnH1Button.IsEnabled = enabled;
                
            if (SplitOnH2Button != null)
                SplitOnH2Button.IsEnabled = enabled;
                
            if (SplitOnH3Button != null)
                SplitOnH3Button.IsEnabled = enabled;
                
            if (SplitOnH4Button != null)
                SplitOnH4Button.IsEnabled = enabled;
                
            if (SplitOnH5Button != null)
                SplitOnH5Button.IsEnabled = enabled;
                
            if (SplitOnH6Button != null)
                SplitOnH6Button.IsEnabled = enabled;
                
            // Enable/disable PDF heading level buttons
            if (PdfSplitOnH1Button != null)
                PdfSplitOnH1Button.IsEnabled = enabled;
                
            if (PdfSplitOnH2Button != null)
                PdfSplitOnH2Button.IsEnabled = enabled;
                
            if (PdfSplitOnH3Button != null)
                PdfSplitOnH3Button.IsEnabled = enabled;
                
            if (PdfSplitOnH4Button != null)
                PdfSplitOnH4Button.IsEnabled = enabled;
                
            if (PdfSplitOnH5Button != null)
                PdfSplitOnH5Button.IsEnabled = enabled;
                
            if (PdfSplitOnH6Button != null)
                PdfSplitOnH6Button.IsEnabled = enabled;
            
            // Update individual heading level checkboxes
            if (SplitOnH1Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH1Checkmark, _splitOnHeadingLevels[0]);
            }
            
            if (SplitOnH2Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH2Checkmark, _splitOnHeadingLevels[1]);
            }
            
            if (SplitOnH3Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH3Checkmark, _splitOnHeadingLevels[2]);
            }
            
            if (SplitOnH4Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH4Checkmark, _splitOnHeadingLevels[3]);
            }
            
            if (SplitOnH5Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH5Checkmark, _splitOnHeadingLevels[4]);
            }
            
            if (SplitOnH6Checkmark != null)
            {
                UpdateCheckboxState(SplitOnH6Checkmark, _splitOnHeadingLevels[5]);
            }
            
            // Update PDF-specific heading level checkboxes
            if (PdfSplitOnH1Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH1Checkmark, _splitOnHeadingLevels[0]);
            }
            
            if (PdfSplitOnH2Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH2Checkmark, _splitOnHeadingLevels[1]);
            }
            
            if (PdfSplitOnH3Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH3Checkmark, _splitOnHeadingLevels[2]);
            }
            
            if (PdfSplitOnH4Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH4Checkmark, _splitOnHeadingLevels[3]);
            }
            
            if (PdfSplitOnH5Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH5Checkmark, _splitOnHeadingLevels[4]);
            }
            
            if (PdfSplitOnH6Checkmark != null)
            {
                UpdateCheckboxState(PdfSplitOnH6Checkmark, _splitOnHeadingLevels[5]);
            }
        }
        
        /// <summary>
        /// Updates the visual state of a checkbox border
        /// </summary>
        private void UpdateCheckboxState(Border checkmark, bool isChecked)
        {
            if (checkmark == null)
                return;
                
            checkmark.Background = isChecked ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Transparent);
        }

        /// <summary>
        /// Handles the selection changed event for heading alignment combo boxes
        /// </summary>
        private void HeadingAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_headingAlignments == null) return;
            
            if (sender is ComboBox comboBox && comboBox.Tag is string tagStr && int.TryParse(tagStr, out int level))
            {
                // Get the selected alignment
                string alignment = ((ComboBoxItem)comboBox.SelectedItem).Content.ToString();
                
                // Update the heading alignment in the options
                switch (alignment)
                {
                    case "Left":
                        _headingAlignments[level] = ExportOptions.TextAlignment.Left;
                        break;
                    case "Center":
                        _headingAlignments[level] = ExportOptions.TextAlignment.Center;
                        break;
                    case "Right":
                        _headingAlignments[level] = ExportOptions.TextAlignment.Right;
                        break;
                    case "Justify":
                        _headingAlignments[level] = ExportOptions.TextAlignment.Justify;
                        break;
                }
            }
        }
        
        /// <summary>
        /// Handles the selection changed event for heading font family combo boxes
        /// </summary>
        private void HeadingFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            if (sender is ComboBox comboBox && comboBox.Tag is string tagStr && int.TryParse(tagStr, out int headingLevel))
            {
                if (comboBox.SelectedItem is FontFamily fontFamily)
                {
                    if (_metadata == null)
                    {
                        _metadata = new Dictionary<string, string>();
                    }
                    
                    string fontName = fontFamily.Source;
                    _metadata[$"h{headingLevel}FontFamily"] = fontName;
                    _options.HeadingFontFamilies[headingLevel] = fontName;
                }
            }
        }
        
        /// <summary>
        /// Handles the selection changed event for heading font size combo boxes
        /// </summary>
        private void HeadingFontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            if (sender is ComboBox comboBox && comboBox.Tag is string tagStr && int.TryParse(tagStr, out int level))
            {
                // Get the selected font size
                if (comboBox.SelectedItem is ComboBoxItem selectedItem && 
                    double.TryParse(selectedItem.Content.ToString(), out double fontSize))
                {
                    // Store the font size in the options
                    if (_options.HeadingFontSizes == null)
                    {
                        _options.HeadingFontSizes = new Dictionary<int, double>();
                    }
                    
                    _options.HeadingFontSizes[level] = fontSize;
                }
            }
        }
        
        /// <summary>
        /// Handles the selection changed event for body font family combo box
        /// </summary>
        private void BodyFontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            if (sender is ComboBox comboBox && comboBox.SelectedItem is FontFamily fontFamily)
            {
                if (_metadata == null)
                {
                    _metadata = new Dictionary<string, string>();
                }
                
                string fontName = fontFamily.Source;
                _metadata["fontFamily"] = fontName;
                _options.BodyFontFamily = fontName;
            }
        }
        
        /// <summary>
        /// Handles the selection changed event for body font size combo box
        /// </summary>
        private void BodyFontSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem &&
                double.TryParse(selectedItem.Content.ToString(), out double fontSize))
            {
                // Store the font size in the options
                _options.BodyFontSize = fontSize;
            }
        }
        
        /// <summary>
        /// Handles the selection changed event for body text alignment combo box
        /// </summary>
        private void BodyTextAlignmentComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                // Get the selected alignment
                string alignment = selectedItem.Content.ToString();
                
                // Update the body text alignment in the options
                switch (alignment)
                {
                    case "Left":
                        _options.BodyTextAlignment = ExportOptions.TextAlignment.Left;
                        break;
                    case "Center":
                        _options.BodyTextAlignment = ExportOptions.TextAlignment.Center;
                        break;
                    case "Right":
                        _options.BodyTextAlignment = ExportOptions.TextAlignment.Right;
                        break;
                    case "Justify":
                        _options.BodyTextAlignment = ExportOptions.TextAlignment.Justify;
                        break;
                }
            }
        }
        
        private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_options == null) return;
            
            // Check if _metadata is initialized
            if (_metadata == null)
            {
                _metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
            
            // Get the selected page size
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                string pageSize = selectedItem.Content.ToString();
                _metadata["pageSize"] = pageSize;
            }
        }

        private void LoadSystemFonts_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string comboBoxName)
            {
                ComboBox comboBox = FindName(comboBoxName) as ComboBox;
                if (comboBox == null) return;

                // Store the currently selected font
                FontFamily currentFont = comboBox.SelectedItem as FontFamily;
                
                // Get all system fonts
                var systemFonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source);
                
                // Set the new ItemsSource
                comboBox.ItemsSource = systemFonts;
                
                // Try to restore the previous selection
                if (currentFont != null)
                {
                    var matchingFont = systemFonts.FirstOrDefault(f => f.Source == currentFont.Source);
                    if (matchingFont != null)
                    {
                        comboBox.SelectedItem = matchingFont;
                    }
                    else
                    {
                        comboBox.SelectedItem = systemFonts.FirstOrDefault();
                    }
                }
                else
                {
                    comboBox.SelectedItem = systemFonts.FirstOrDefault();
                }
            }
        }

        private void LoadAllSystemFonts_Click(object sender, RoutedEventArgs e)
        {
            // Get all system fonts
            var systemFonts = Fonts.SystemFontFamilies.OrderBy(f => f.Source).ToList();
            
            // List of all font combo boxes
            var fontComboBoxes = new[] 
            {
                "H1FontFamilyComboBox",
                "H2FontFamilyComboBox",
                "H3FontFamilyComboBox",
                "H4FontFamilyComboBox",
                "H5FontFamilyComboBox",
                "H6FontFamilyComboBox",
                "BodyFontFamilyComboBox"
            };
            
            // Update each combo box
            foreach (var comboBoxName in fontComboBoxes)
            {
                ComboBox comboBox = FindName(comboBoxName) as ComboBox;
                if (comboBox == null) continue;
                
                // Store the currently selected font
                FontFamily currentFont = comboBox.SelectedItem as FontFamily;
                
                // Set the new ItemsSource
                comboBox.ItemsSource = systemFonts;
                
                // Try to restore the previous selection
                if (currentFont != null)
                {
                    var matchingFont = systemFonts.FirstOrDefault(f => f.Source == currentFont.Source);
                    if (matchingFont != null)
                    {
                        comboBox.SelectedItem = matchingFont;
                    }
                    else
                    {
                        comboBox.SelectedItem = systemFonts.FirstOrDefault();
                    }
                }
                else
                {
                    comboBox.SelectedItem = systemFonts.FirstOrDefault();
                }
            }
        }
    }
} 