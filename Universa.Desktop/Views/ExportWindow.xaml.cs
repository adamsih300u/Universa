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

        public ExportWindow(IFileTab currentTab)
        {
            // Initialize component first to ensure UI elements are created
            InitializeComponent();
            
            _currentTab = currentTab;
            
            // Initialize metadata
            _metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            
            // Set default metadata values
            _metadata["title"] = Path.GetFileNameWithoutExtension(_currentTab?.FilePath ?? "Untitled");
            _metadata["author"] = Environment.UserName;
            _metadata["language"] = System.Globalization.CultureInfo.CurrentCulture.TwoLetterISOLanguageName;
            
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
            
            // Fill in metadata fields from frontmatter
            if (TitleTextBox != null)
            {
                TitleTextBox.Text = _metadata["title"];
            }
            
            if (AuthorTextBox != null)
            {
                AuthorTextBox.Text = _metadata["author"];
            }
            
            if (LanguageTextBox != null)
            {
                LanguageTextBox.Text = _metadata["language"];
            }
            
            // Set up checkbox states - add null checks for all UI elements
            if (IncludeTocCheckmark != null)
                UpdateCheckboxState(IncludeTocCheckmark, _includeToc);
                
            if (SplitOnHeadingsCheckmark != null)
                UpdateCheckboxState(SplitOnHeadingsCheckmark, _splitOnHeadings);
            
            if (IncludeCoverButton != null)
            {
                // Always enable the button
                IncludeCoverButton.IsEnabled = true;
            }
                
            if (IncludeCoverCheckmark != null)
                UpdateCheckboxState(IncludeCoverCheckmark, _includeCover);
            
            if (SplitOnH1Checkmark != null)
                UpdateCheckboxState(SplitOnH1Checkmark, _splitOnHeadingLevels[0]);
                
            if (SplitOnH2Checkmark != null)
                UpdateCheckboxState(SplitOnH2Checkmark, _splitOnHeadingLevels[1]);
                
            if (SplitOnH3Checkmark != null)
                UpdateCheckboxState(SplitOnH3Checkmark, _splitOnHeadingLevels[2]);
                
            if (SplitOnH4Checkmark != null)
                UpdateCheckboxState(SplitOnH4Checkmark, _splitOnHeadingLevels[3]);
                
            if (SplitOnH5Checkmark != null)
                UpdateCheckboxState(SplitOnH5Checkmark, _splitOnHeadingLevels[4]);
                
            if (SplitOnH6Checkmark != null)
                UpdateCheckboxState(SplitOnH6Checkmark, _splitOnHeadingLevels[5]);
            
            // Initialize UI format selection - do this last to avoid premature event firing
            if (FormatComboBox != null)
            {
                // Temporarily remove the event handler
                FormatComboBox.SelectionChanged -= FormatComboBox_SelectionChanged;
                FormatComboBox.SelectedIndex = 0;
                // Re-attach the event handler
                FormatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
            }
            
            // Update output path after UI elements are initialized
            UpdateOutputPath();
            
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
                
                // Update metadata from UI
                if (TitleTextBox != null)
                    _metadata["title"] = TitleTextBox.Text;
                
                if (AuthorTextBox != null)
                    _metadata["author"] = AuthorTextBox.Text;
                
                if (LanguageTextBox != null)
                    _metadata["language"] = LanguageTextBox.Text;
                
                // Create export options
                var options = new ExportOptions
                {
                    OutputPath = OutputPathTextBox.Text,
                    IncludeToc = _includeToc,
                    SplitOnHeadings = _splitOnHeadings,
                    IncludeCover = _includeCover,
                    Metadata = _metadata
                };
                
                // Add heading levels to split on
                for (int i = 0; i < _splitOnHeadingLevels.Length; i++)
                {
                    if (_splitOnHeadingLevels[i])
                    {
                        options.SplitOnHeadingLevels.Add(i + 1);
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
                bool success = await exporter.ExportAsync(content, options);
                
                // Hide the status bar
                StatusBar.Visibility = Visibility.Collapsed;
                
                if (success)
                {
                    MessageBox.Show($"Document exported successfully to:\n{options.OutputPath}", 
                        "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    
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
                                FileName = options.OutputPath,
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
            _splitOnHeadingLevels[0] = !_splitOnHeadingLevels[0];
            UpdateCheckboxState(SplitOnH1Checkmark, _splitOnHeadingLevels[0]);
        }
        
        private void SplitOnH2Button_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadingLevels[1] = !_splitOnHeadingLevels[1];
            UpdateCheckboxState(SplitOnH2Checkmark, _splitOnHeadingLevels[1]);
        }
        
        private void SplitOnH3Button_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadingLevels[2] = !_splitOnHeadingLevels[2];
            UpdateCheckboxState(SplitOnH3Checkmark, _splitOnHeadingLevels[2]);
        }
        
        private void SplitOnH4Button_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadingLevels[3] = !_splitOnHeadingLevels[3];
            UpdateCheckboxState(SplitOnH4Checkmark, _splitOnHeadingLevels[3]);
        }
        
        private void SplitOnH5Button_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadingLevels[4] = !_splitOnHeadingLevels[4];
            UpdateCheckboxState(SplitOnH5Checkmark, _splitOnHeadingLevels[4]);
        }
        
        private void SplitOnH6Button_Click(object sender, RoutedEventArgs e)
        {
            _splitOnHeadingLevels[5] = !_splitOnHeadingLevels[5];
            UpdateCheckboxState(SplitOnH6Checkmark, _splitOnHeadingLevels[5]);
        }
        
        private void UpdateSplitOnHeadingsUI()
        {
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
    }
} 