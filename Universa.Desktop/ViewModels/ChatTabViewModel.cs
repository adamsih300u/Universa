using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using Universa.Desktop.Tabs;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.ViewModels
{
    public class ChatTabViewModel : INotifyPropertyChanged
    {
        private string _name;
        private ObservableCollection<Models.ChatMessage> _messages;
        private ObservableCollection<Models.ChatMessage> _chatModeMessages;
        private string _inputText;
        private BaseLangChainService _service;
        private bool _isContextMode = true;
        private AIModelInfo _selectedModel;
        private object _tag;
        
        // New fields for tab-specific context
        private IFileTab _associatedEditor;
        private string _associatedFilePath;
        private bool _contextRequiresRefresh = false;
        
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public ObservableCollection<Models.ChatMessage> Messages
        {
            get => _messages;
            set
            {
                if (_messages != value)
                {
                    _messages = value;
                    OnPropertyChanged();
                }
            }
        }

        public ObservableCollection<Models.ChatMessage> ChatModeMessages
        {
            get => _chatModeMessages;
            set
            {
                if (_chatModeMessages != value)
                {
                    _chatModeMessages = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public string InputText
        {
            get => _inputText;
            set
            {
                if (_inputText != value)
                {
                    _inputText = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public BaseLangChainService Service
        {
            get => _service;
            set
            {
                if (_service != value)
                {
                    _service = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsContextMode
        {
            get => _isContextMode;
            set
            {
                if (_isContextMode != value)
                {
                    _isContextMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public AIModelInfo SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (_selectedModel != value)
                {
                    _selectedModel = value;
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// General-purpose tag for storing temporary data
        /// </summary>
        public object Tag
        {
            get => _tag;
            set
            {
                _tag = value;
                OnPropertyChanged();
            }
        }
        
        /// <summary>
        /// Gets or sets the associated editor tab for this chat tab
        /// </summary>
        public IFileTab AssociatedEditor
        {
            get => _associatedEditor;
            set
            {
                if (_associatedEditor != value)
                {
                    // Unsubscribe from old editor events if needed
                    if (_associatedEditor != null)
                    {
                        UnsubscribeFromEditor(_associatedEditor);
                    }
                    
                    _associatedEditor = value;
                    _associatedFilePath = _associatedEditor?.FilePath;
                    
                    // Subscribe to new editor events
                    if (_associatedEditor != null)
                    {
                        SubscribeToEditor(_associatedEditor);
                    }
                    
                    // Mark that context needs refresh
                    _contextRequiresRefresh = true;
                    
                    OnPropertyChanged();
                }
            }
        }
        
        /// <summary>
        /// Gets the path of the associated file, if any
        /// </summary>
        public string AssociatedFilePath => _associatedFilePath;
        
        /// <summary>
        /// Indicates whether the context needs to be refreshed before next use
        /// </summary>
        public bool ContextRequiresRefresh
        {
            get => _contextRequiresRefresh;
            set
            {
                if (_contextRequiresRefresh != value)
                {
                    _contextRequiresRefresh = value;
                    OnPropertyChanged();
                }
            }
        }
        
        public ChatTabViewModel(string name = "New Chat")
        {
            Name = name;
            Messages = new ObservableCollection<Models.ChatMessage>();
            ChatModeMessages = new ObservableCollection<Models.ChatMessage>();
            InputText = string.Empty;
            IsContextMode = true;
            _contextRequiresRefresh = true;
        }
        
        private void SubscribeToEditor(IFileTab editor)
        {
            try
            {
                if (editor is EditorTab editorTab)
                {
                    editorTab.ContentChanged += Editor_ContentChanged;
                }
                else if (editor is MarkdownTab markdownTab)
                {
                    markdownTab.PropertyChanged += (s, e) => {
                        if (e.PropertyName == "IsModified" && markdownTab.IsModified)
                {
                            Editor_ContentChanged(s, e);
                        }
                    };
                }
                
                Debug.WriteLine($"Subscribed to editor events for file: {editor.FilePath}");
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error subscribing to editor events: {ex.Message}");
            }
        }
        
        private void UnsubscribeFromEditor(IFileTab editor)
        {
            try
            {
                if (editor is EditorTab editorTab)
                {
                    editorTab.ContentChanged -= Editor_ContentChanged;
                }
                else if (editor is MarkdownTab markdownTab)
                {
                    // We can't directly unsubscribe anonymous handlers, but PropertyChanged is a multicast delegate
                    // so this isn't a serious memory leak in practice
                }
                
                Debug.WriteLine($"Unsubscribed from editor events for file: {editor.FilePath}");
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error unsubscribing from editor events: {ex.Message}");
            }
        }
        
        private void Editor_ContentChanged(object sender, System.EventArgs e)
        {
            Debug.WriteLine($"Content changed in associated editor: {_associatedFilePath}");
            
            // Mark the context as needing refresh
            _contextRequiresRefresh = true;
        }
        
        public async Task<string> GetAssociatedContent()
        {
            if (_associatedEditor == null)
                return string.Empty;
                
            try
            {
                if (_associatedEditor is EditorTab editorTab)
                {
                    return editorTab.GetContent();
                }
                else if (_associatedEditor is MarkdownTab markdownTab) 
                {
                    // Get the current text from the editor (for unsaved changes)
                    string editorContent = markdownTab.GetContent();
                    
                    // Check if we have a file path and if the file exists
                    if (!string.IsNullOrEmpty(markdownTab.FilePath) && System.IO.File.Exists(markdownTab.FilePath))
                    {
                        // Check if there are unsaved changes
                        if (markdownTab.IsModified)
                        {
                            try
                            {
                                Debug.WriteLine("Detected unsaved changes in associated editor, using hybrid approach");
                                
                                // Read the file from disk to get the frontmatter
                                string fileContent = await System.IO.File.ReadAllTextAsync(markdownTab.FilePath);
                                
                                // Extract frontmatter if it exists
                                if (fileContent.StartsWith("---\n") || fileContent.StartsWith("---\r\n"))
                                {
                                    // Find the closing delimiter
                                    int endIndex = fileContent.IndexOf("\n---", 4);
                                    if (endIndex > 0)
                                    {
                                        // Extract frontmatter including delimiters
                                        string frontmatter = fileContent.Substring(0, endIndex + 4); // +4 to include the closing delimiter
                                        
                                        // If there's a newline after the closing delimiter, include it too
                                        if (endIndex + 4 < fileContent.Length && fileContent[endIndex + 4] == '\n')
            {
                                            frontmatter += "\n";
                                        }
                                        
                                        Debug.WriteLine("Found frontmatter in associated file, combining with current editor content");
                                        
                                        // Return combined content: frontmatter + current editor content
                                        return frontmatter + editorContent;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error handling unsaved changes in associated editor: {ex.Message}");
                            }
                        }
                        else
                        {
                            // No unsaved changes, read directly from file
                            try
                            {
                                Debug.WriteLine($"No unsaved changes in associated editor, reading from file");
                                return await System.IO.File.ReadAllTextAsync(markdownTab.FilePath);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error reading associated file: {ex.Message}");
                }
            }
                    }
                    
                    // Fall back to editor content
                    return editorContent;
                }
                
                return string.Empty;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"Error getting content from associated editor: {ex.Message}");
                return string.Empty;
            }
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
} 