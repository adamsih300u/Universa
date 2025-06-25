using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Universa.Desktop.Models;
using Universa.Desktop.Core;
using Universa.Desktop.Core.Configuration;
using Universa.Desktop.Services;

namespace Universa.Desktop.Dialogs
{
    public partial class ManuscriptGenerationDialog : Window
    {
        public ManuscriptGenerationSettings Settings { get; private set; }
        public bool WasGenerated { get; private set; } = false;
        
        private readonly string _currentFilePath;
        private readonly ManuscriptGenerationService _manuscriptService;
        private readonly ModelProvider _modelProvider;
        private List<(int ChapterNumber, string Title, string Summary)> _chapters;
        private List<AIModelInfo> _availableModels;

        public ManuscriptGenerationDialog(string currentFilePath)
        {
            InitializeComponent();
            _currentFilePath = currentFilePath;
            _manuscriptService = new ManuscriptGenerationService();
            
            // Initialize ModelProvider to get actual available models
            var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            _modelProvider = new ModelProvider(configService);
            
            InitializeDialog();
        }

        private async void InitializeDialog()
        {
            // Set up default settings
            Settings = new ManuscriptGenerationSettings();
            
            // Load actual available models from configuration
            await LoadAvailableModelsAsync();
            
            // Load current configuration
            LoadCurrentConfiguration();
            
            // Set up provider combo box change handler
            ProviderComboBox.SelectionChanged += ProviderComboBox_SelectionChanged;
            
            // Load outline information
            LoadOutlineInformation();
        }

        private void LoadCurrentConfiguration()
        {
            try
            {
                var configInstance = Models.Configuration.Instance;
                
                // Select current provider if available
                foreach (ComboBoxItem item in ProviderComboBox.Items)
                {
                    if (item.Tag.ToString() == configInstance.DefaultAIProvider.ToString())
                    {
                        ProviderComboBox.SelectedItem = item;
                        break;
                    }
                }
                
                // Set current model if it exists in available models
                if (!string.IsNullOrEmpty(configInstance.LastUsedModel) && _availableModels != null)
                {
                    var currentModel = _availableModels.FirstOrDefault(m => m.Name == configInstance.LastUsedModel);
                    if (currentModel != null)
                    {
                        // Find the ComboBoxItem with matching Tag
                        foreach (ComboBoxItem item in ModelComboBox.Items)
                        {
                            if (item.Tag?.ToString() == currentModel.Name)
                            {
                                ModelComboBox.SelectedItem = item;
                                System.Diagnostics.Debug.WriteLine($"Set current model to: {currentModel.DisplayName}");
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading current configuration: {ex.Message}");
            }
        }

        private void ProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ProviderComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                LoadModelsForProvider(selectedItem.Tag.ToString());
            }
        }

        private void LoadModelsForProvider(string providerName)
        {
            ModelComboBox.Items.Clear();
            
            if (_availableModels == null) return;

            // Parse provider enum from string
            if (Enum.TryParse<AIProvider>(providerName, out var provider))
            {
                var providerModels = _availableModels.Where(m => m.Provider == provider).ToList();
                
                foreach (var model in providerModels)
                {
                    // Create ComboBoxItem with DisplayName but store actual Name as Tag
                    var item = new ComboBoxItem
                    {
                        Content = model.DisplayName,
                        Tag = model.Name
                    };
                    ModelComboBox.Items.Add(item);
                }
                
                // Select first model as default
                if (ModelComboBox.Items.Count > 0)
                {
                    ModelComboBox.SelectedIndex = 0;
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded {providerModels.Count} models for {providerName}");
            }
        }

        /// <summary>
        /// Loads actual available models from the enabled providers
        /// </summary>
        private async Task LoadAvailableModelsAsync()
        {
            try
            {
                _availableModels = await _modelProvider.GetModels();
                
                // Populate provider combo box with only providers that have models
                ProviderComboBox.Items.Clear();
                var providersWithModels = _availableModels.GroupBy(m => m.Provider).ToList();
                
                foreach (var providerGroup in providersWithModels)
                {
                    var providerName = GetProviderDisplayName(providerGroup.Key);
                    var item = new ComboBoxItem 
                    { 
                        Content = providerName, 
                        Tag = providerGroup.Key.ToString() 
                    };
                    ProviderComboBox.Items.Add(item);
                }
                
                System.Diagnostics.Debug.WriteLine($"Loaded {_availableModels.Count} models from {providersWithModels.Count} providers");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading available models: {ex.Message}");
                _availableModels = new List<AIModelInfo>();
            }
        }



        /// <summary>
        /// Gets display name for provider
        /// </summary>
        private string GetProviderDisplayName(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => "OpenAI",
                AIProvider.Anthropic => "Anthropic (Claude)",
                AIProvider.XAI => "XAI (Grok)",
                AIProvider.OpenRouter => "OpenRouter",
                AIProvider.Ollama => "Ollama",
                _ => provider.ToString()
            };
        }

        private void LoadOutlineInformation()
        {
            try
            {
                // Find outline file
                var outlineFilePath = FindOutlineFile();
                
                if (!string.IsNullOrEmpty(outlineFilePath))
                {
                    OutlinePathText.Text = $"Outline file: {Path.GetFileName(outlineFilePath)}";
                    
                    // Load and parse outline
                    var outlineContent = File.ReadAllText(outlineFilePath);
                    _chapters = _manuscriptService.ExtractChaptersFromOutline(outlineContent);
                    
                    ChapterCountText.Text = $"Chapters to generate: {_chapters.Count}";
                    
                    // Estimate time (rough calculation: 30-60 seconds per chapter)
                    var estimatedMinutes = (_chapters.Count * 45) / 60; // 45 seconds average per chapter
                    EstimatedTimeText.Text = $"Estimated time: {estimatedMinutes} minutes";
                    
                    // Enable generate button
                    GenerateButton.IsEnabled = _chapters.Count > 0;
                }
                else
                {
                    OutlinePathText.Text = "Outline file: Not found";
                    ChapterCountText.Text = "Please create an outline file or add 'ref outline:' to frontmatter";
                    EstimatedTimeText.Text = "Cannot estimate without outline";
                    GenerateButton.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                OutlinePathText.Text = $"Error loading outline: {ex.Message}";
                GenerateButton.IsEnabled = false;
            }
        }

        private string FindOutlineFile()
        {
            if (string.IsNullOrEmpty(_currentFilePath))
                return null;

            try
            {
                // FIRST: Check frontmatter for "ref outline:" reference (same as integration service)
                if (File.Exists(_currentFilePath))
                {
                    var content = File.ReadAllText(_currentFilePath);
                    var frontmatterProcessor = new Services.FrontmatterProcessor();
                    var frontmatter = frontmatterProcessor.GetFrontmatterFromContent(content);

                    // Look for "ref outline" in frontmatter
                    var outlineRef = frontmatter.FirstOrDefault(kv => 
                        kv.Key.Equals("ref outline", StringComparison.OrdinalIgnoreCase) ||
                        kv.Key.Equals("outline", StringComparison.OrdinalIgnoreCase)).Value;

                    if (!string.IsNullOrEmpty(outlineRef))
                    {
                        var directory = Path.GetDirectoryName(_currentFilePath);
                        var outlinePath = Path.Combine(directory, outlineRef);
                        
                        if (File.Exists(outlinePath))
                        {
                            System.Diagnostics.Debug.WriteLine($"[ManuscriptDialog] Found outline via frontmatter: {outlineRef}");
                            return outlinePath;
                        }
                    }
                }

                // FALLBACK: Look for outline files with common naming patterns
                var directory2 = Path.GetDirectoryName(_currentFilePath);
                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(_currentFilePath);

                var outlinePatterns = new[]
                {
                    $"{fileNameWithoutExt}-outline.md",
                    $"{fileNameWithoutExt}_outline.md",
                    $"outline-{fileNameWithoutExt}.md",
                    "outline.md",
                    $"{fileNameWithoutExt}.outline.md"
                };

                foreach (var pattern in outlinePatterns)
                {
                    var outlinePath = Path.Combine(directory2, pattern);
                    if (File.Exists(outlinePath))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ManuscriptDialog] Found outline via pattern: {pattern}");
                        return outlinePath;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ManuscriptDialog] Error finding outline: {ex.Message}");
                return null;
            }
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Create settings from UI
                Settings = new ManuscriptGenerationSettings
                {
                    UseCurrentChatSettings = UseCurrentSettingsRadio.IsChecked == true,
                    GenerateSequentially = GenerateSequentiallyCheckBox.IsChecked == true,
                    ShowProgressDialog = ShowProgressCheckBox.IsChecked == true,
                    AutoSaveAfterGeneration = AutoSaveCheckBox.IsChecked == true
                };

                // Parse delay
                if (int.TryParse(DelayTextBox.Text, out var delay))
                {
                    Settings.DelayBetweenChapters = Math.Max(100, delay); // Minimum 100ms
                }

                // Set custom model if selected
                if (UseCustomModelRadio.IsChecked == true)
                {
                    if (ProviderComboBox.SelectedItem is ComboBoxItem selectedProvider)
                    {
                        if (Enum.TryParse<AIProvider>(selectedProvider.Tag.ToString(), out var provider))
                        {
                            Settings.Provider = provider;
                        }
                    }
                    
                    // Get model name from selected ComboBoxItem's Tag
                    if (ModelComboBox.SelectedItem is ComboBoxItem selectedModel)
                    {
                        Settings.Model = selectedModel.Tag.ToString();
                    }
                    else
                    {
                        Settings.Model = null; // No fallback since it's not editable
                    }
                    
                    if (string.IsNullOrEmpty(Settings.Model))
                    {
                        MessageBox.Show("Please select a model for generation.", "Model Required", 
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                WasGenerated = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error preparing generation settings: {ex.Message}", 
                    "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
} 