using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Dialogs
{
    /// <summary>
    /// Interaction logic for FrontmatterDialog.xaml
    /// </summary>
    public partial class FrontmatterDialog : UserControl
    {
        private IFrontmatterProcessor _frontmatterProcessor;
        private string _filePath;
        private Dictionary<string, string> _originalFrontmatter;
        private Dictionary<string, string> _currentFrontmatter;

        public event EventHandler<FrontmatterChangedEventArgs> FrontmatterChanged;
        public event EventHandler SaveRequested;
        public event EventHandler CancelRequested;

        public FrontmatterDialog()
        {
            InitializeComponent();
        }

        public void Initialize(IFrontmatterProcessor frontmatterProcessor, string filePath, string currentContent = null)
        {
            _frontmatterProcessor = frontmatterProcessor ?? throw new ArgumentNullException(nameof(frontmatterProcessor));
            _filePath = filePath;
            
            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.Initialize: Initializing dialog for file: {_filePath}");
            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.Initialize: File exists: {(!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))}");
            
            LoadFrontmatter();
        }

        private void LoadFrontmatter()
        {
            // Clear existing fields
            FrontmatterFields.Children.Clear();

            // CRITICAL FIX: Always read frontmatter from THIS SPECIFIC file path to prevent cross-tab contamination
            Dictionary<string, string> frontmatter = new Dictionary<string, string>();
            
            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.LoadFrontmatter: LOADING FRONTMATTER FOR FILE: {_filePath}");
            
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                try
                {
                    string fileContent = File.ReadAllText(_filePath);
                    System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: Read {fileContent.Length} characters from file: {_filePath}");
                    System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: File content preview: {(fileContent.Length > 200 ? fileContent.Substring(0, 200) + "..." : fileContent)}");
                    
                    // Use the new stateless method to extract frontmatter from content
                    if (_frontmatterProcessor is Services.FrontmatterProcessor processor)
                    {
                        frontmatter = processor.GetFrontmatterFromContent(fileContent);
                        System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: Extracted {frontmatter.Count} frontmatter entries from file: {_filePath}");
                        foreach (var kvp in frontmatter)
                        {
                            System.Diagnostics.Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // If we can't read the file, start with empty frontmatter
                    System.Diagnostics.Debug.WriteLine($"Error reading file for frontmatter: {ex.Message}");
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: No file path or file doesn't exist: {_filePath}");
            }
            
            _originalFrontmatter = new Dictionary<string, string>(frontmatter);
            _currentFrontmatter = new Dictionary<string, string>(frontmatter);

            // SIMPLIFIED: If we don't have frontmatter, provide just a title field based on file name
            if (frontmatter.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: No frontmatter found in file {_filePath}, creating default title field");
                
                string fileName = !string.IsNullOrEmpty(_filePath) ? Path.GetFileNameWithoutExtension(_filePath) : "";
                
                frontmatter = new Dictionary<string, string>
                {
                    { "title", fileName }
                };
                _currentFrontmatter = new Dictionary<string, string>(frontmatter);
                
                // Update status text to indicate this is a new frontmatter for a file without any
                if (StatusText != null)
                {
                    StatusText.Text = $"No frontmatter found in file. Creating default title field for: {Path.GetFileName(_filePath)}";
                }
                
                System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: Created default title field for new file");
            }
            else
            {
                // Update status text to show existing frontmatter
                if (StatusText != null)
                {
                    StatusText.Text = $"Editing existing frontmatter for: {Path.GetFileName(_filePath)}";
                }
            }

            // Add fields for each frontmatter entry
            foreach (var kvp in frontmatter)
            {
                AddFrontmatterField(kvp.Key, kvp.Value);
            }
            
            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog: Added {frontmatter.Count} fields to dialog for file: {_filePath}");
        }

        private void AddFrontmatterField(string key, string value)
        {
            var fieldPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 5)
            };

            var keyTextBox = new TextBox
            {
                Text = key,
                Width = 150,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var valueTextBox = new TextBox
            {
                Text = value,
                Width = 250,
                Margin = new Thickness(0, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            var removeButton = new Button
            {
                Content = "✕",
                Padding = new Thickness(5, 0, 5, 0),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            removeButton.Click += (s, e) =>
            {
                FrontmatterFields.Children.Remove(fieldPanel);
            };

            fieldPanel.Children.Add(keyTextBox);
            fieldPanel.Children.Add(valueTextBox);
            fieldPanel.Children.Add(removeButton);

            FrontmatterFields.Children.Add(fieldPanel);
        }

        private void AddFieldButton_Click(object sender, RoutedEventArgs e)
        {
            AddFrontmatterField("", "");
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Create new frontmatter dictionary from the dialog fields
            var frontmatter = new Dictionary<string, string>();

            foreach (var child in FrontmatterFields.Children)
            {
                if (child is StackPanel panel && panel.Children.Count >= 2)
                {
                    var keyTextBox = panel.Children[0] as TextBox;
                    var valueTextBox = panel.Children[1] as TextBox;

                    if (keyTextBox != null && valueTextBox != null && !string.IsNullOrWhiteSpace(keyTextBox.Text))
                    {
                        frontmatter[keyTextBox.Text] = valueTextBox.Text;
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.SaveButton_Click: Saving {frontmatter.Count} frontmatter entries");
            foreach (var kvp in frontmatter)
            {
                System.Diagnostics.Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
            }

            // Store the current frontmatter for this dialog session
            _currentFrontmatter = new Dictionary<string, string>(frontmatter);

            // Raise events to notify the parent - let the parent handle file operations
            FrontmatterChanged?.Invoke(this, new FrontmatterChangedEventArgs(frontmatter));
            SaveRequested?.Invoke(this, EventArgs.Empty);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.CancelButton_Click: User cancelled frontmatter editing");
            
            // Simply notify the parent that cancel was requested
            // The parent will handle any necessary cleanup
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }

        public void ShowDialog()
        {
            // Don't reload frontmatter here since it's already loaded in Initialize with current content
            Visibility = Visibility.Visible;
        }

        public void HideDialog()
        {
            Visibility = Visibility.Collapsed;
        }

        // Method to get the current frontmatter without saving - read from actual dialog fields
        public Dictionary<string, string> GetCurrentFrontmatter()
        {
            var frontmatter = new Dictionary<string, string>();

            foreach (var child in FrontmatterFields.Children)
            {
                if (child is StackPanel panel && panel.Children.Count >= 2)
                {
                    var keyTextBox = panel.Children[0] as TextBox;
                    var valueTextBox = panel.Children[1] as TextBox;

                    if (keyTextBox != null && valueTextBox != null && !string.IsNullOrWhiteSpace(keyTextBox.Text))
                    {
                        frontmatter[keyTextBox.Text] = valueTextBox.Text ?? "";
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine($"FrontmatterDialog.GetCurrentFrontmatter: Returning {frontmatter.Count} entries");
            return frontmatter;
        }

        // Method to get the original frontmatter
        public Dictionary<string, string> GetOriginalFrontmatter()
        {
            return _originalFrontmatter != null ? new Dictionary<string, string>(_originalFrontmatter) : new Dictionary<string, string>();
        }
    }

    public class FrontmatterChangedEventArgs : EventArgs
    {
        public Dictionary<string, string> Frontmatter { get; }

        public FrontmatterChangedEventArgs(Dictionary<string, string> frontmatter)
        {
            Frontmatter = frontmatter;
        }
    }
} 