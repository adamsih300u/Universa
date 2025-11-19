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
using System.Collections.Generic;
using System.Linq;

namespace Universa.Desktop.ViewModels
{
    public enum ChainType
    {
        Chat,
        FictionWriting,
        Proofreader,
        StoryAnalysis,
        OutlineWriter,
        RulesWriter,
        CharacterDevelopment,
        StyleGuide
    }
    
    public class ChainInfo
    {
        public ChainType Type { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        
        /// <summary>
        /// Gets available chains for the specified file type from frontmatter (e.g., type: fiction)
        /// </summary>
        /// <param name="fileType">File type from frontmatter 'type:' field (fiction, outline, rules, etc.)</param>
        /// <returns>List of available chains for the file type</returns>
        public static List<ChainInfo> GetAvailableChainsForFileType(string fileType)
        {
            var chains = new List<ChainInfo>();
            
            // Always include pure Chat mode
            chains.Add(new ChainInfo 
            { 
                Type = ChainType.Chat, 
                DisplayName = "Chat", 
                Description = "General AI conversation independent of any file content" 
            });
            
            if (string.IsNullOrEmpty(fileType))
            {
                return chains;
            }
            
            switch (fileType.ToLowerInvariant())
            {
                case "fiction":
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.FictionWriting, 
                        DisplayName = "Fiction Writing", 
                        Description = "Specialized for fiction writing with style guides and character profiles" 
                    });
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.Proofreader, 
                        DisplayName = "Proofreader", 
                        Description = "Focus on grammar, consistency, and technical accuracy" 
                    });
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.StoryAnalysis, 
                        DisplayName = "Story Analysis", 
                        Description = "Analyze plot holes, character arcs, and story structure" 
                    });
                    break;
                    
                case "outline":
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.OutlineWriter, 
                        DisplayName = "Outline Writer", 
                        Description = "Specialized for developing and refining story outlines" 
                    });
                    break;
                    
                case "rules":
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.RulesWriter, 
                        DisplayName = "Rules Writer", 
                        Description = "Specialized for universe documentation and character development" 
                    });
                    break;
                    
                case "character":
                case "characters":
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.CharacterDevelopment, 
                        DisplayName = "Character Development", 
                        Description = "Specialized for creating and developing character profiles and relationships" 
                    });
                    break;
                    
                case "style":
                case "styleguide":
                case "style-guide":
                    chains.Add(new ChainInfo 
                    { 
                        Type = ChainType.StyleGuide, 
                        DisplayName = "Style Guide Chain", 
                        Description = "Interactive assistant for creating and formatting style guides" 
                    });
                    break;
            }
            
            return chains;
        }
    }

    public class ChatTabViewModel : INotifyPropertyChanged
    {
        // Message limit constants
        private const int MAX_MESSAGES_PER_TAB = 50;
        private const int CLEANUP_THRESHOLD = 60; // Start cleanup at 60 messages
        private const int MAX_TOTAL_MEMORY_MB = 100; // Approximate memory limit per tab
        
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
        
        // SPLENDID: Properties to lock tab to specific file and chain
        private string _lockedFilePath;
        private ChainType? _lockedChainType;
        private bool _isLocked = false;
        
        // Chain selection fields
        private List<ChainInfo> _availableChains;
        private ChainInfo _selectedChain;
        private string _detectedFileType;
        
        // Persona field
        private string _currentPersona;
        

        
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
                    
                    // Update tab name for generic chat tabs when model changes
                    if (!_isLocked && (SelectedChain == null || SelectedChain.Type == ChainType.Chat))
                    {
                        if (!string.IsNullOrEmpty(_currentPersona))
                        {
                            UpdateTabNameForPersona();
                        }
                        else
                        {
                            UpdateTabNameForGenericChat();
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// Available chains for the current file type
        /// </summary>
        public List<ChainInfo> AvailableChains
        {
            get => _availableChains ?? new List<ChainInfo>();
            private set
            {
                if (_availableChains != value)
                {
                    _availableChains = value;
                    OnPropertyChanged();
                    System.Diagnostics.Debug.WriteLine($"Tab '{Name}' AvailableChains updated: {_availableChains?.Count ?? 0} chains");
                }
            }
        }
        
        /// <summary>
        /// Currently selected chain
        /// </summary>
        public ChainInfo SelectedChain
        {
            get => _selectedChain;
            set
            {
                if (_selectedChain != value)
                {
                    _selectedChain = value;
                    OnPropertyChanged();
                    
                    // When chain changes, dispose current service to force recreation
                    if (Service != null)
                    {
                        Service.Dispose();
                        Service = null;
                    }
                    
                    // Mark context for refresh
                    ContextRequiresRefresh = true;
                    
                    // Auto-update tab name when any chain is selected (if tab has generic name)
                    if (value != null && (Name == "New Chat" || Name == "Select Chain..." || Name.StartsWith("Chat ")))
                    {
                        if (value.Type == ChainType.Chat)
                        {
                            // SPLENDID: For general chat, respect persona if set, otherwise include model name
                            if (!string.IsNullOrEmpty(_currentPersona))
                            {
                                UpdateTabNameForPersona();
                            }
                            else
                            {
                                UpdateTabNameForGenericChat();
                            }
                        }
                        else
                        {
                            // SPLENDID: For specialized chains, auto-lock to file if available
                            if (!_isLocked && !string.IsNullOrEmpty(_associatedFilePath))
                            {
                                LockToFileAndChain(_associatedFilePath, value.Type);
                                System.Diagnostics.Debug.WriteLine($"Auto-locked tab when chain selected: {_associatedFilePath} ({value.Type})");
                            }
                            else if (_isLocked)
                            {
                                // Already locked, just update the name to match current lock
                                UpdateTabNameForLock();
                            }
                            else
                            {
                                // No file context, use display name
                                Name = value.DisplayName;
                            }
                        }
                        System.Diagnostics.Debug.WriteLine($"Tab auto-renamed to '{Name}' based on selected chain");
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Tab '{Name}' chain changed to: {value?.DisplayName ?? "None"}");
                }
            }
        }
        
        /// <summary>
        /// Detected file type for this tab
        /// </summary>
        public string DetectedFileType
        {
            get => _detectedFileType;
            private set
            {
                if (_detectedFileType != value)
                {
                    _detectedFileType = value;
                    OnPropertyChanged();
                    
                    // Update available chains when file type changes
                    UpdateAvailableChains();
                }
            }
        }
        
        /// <summary>
        /// Current persona for this chat tab (overrides default setting)
        /// </summary>
        public string CurrentPersona
        {
            get => _currentPersona;
            set
            {
                if (_currentPersona != value)
                {
                    _currentPersona = value;
                    OnPropertyChanged();
                    
                    // When persona changes, dispose current service to force recreation with new persona
                    if (Service != null)
                    {
                        Service.Dispose();
                        Service = null;
                    }
                    
                    // SPLENDID: Update tab name for general chat tabs with persona
                    if (!_isLocked && (SelectedChain == null || SelectedChain.Type == ChainType.Chat))
                    {
                        UpdateTabNameForPersona();
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Tab '{Name}' persona changed to: {value ?? "Default"}");
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
        
        /// <summary>
        /// Gets the path of the file this tab is locked to, if any
        /// </summary>
        public string LockedFilePath => _lockedFilePath;
        
        /// <summary>
        /// Gets the chain type this tab is locked to, if any
        /// </summary>
        public ChainType? LockedChainType => _lockedChainType;
        
        /// <summary>
        /// Gets whether this tab is locked to a specific file and chain
        /// </summary>
        public bool IsLocked => _isLocked;
        
        /// <summary>
        /// Locks this chat tab to a specific file and chain type
        /// </summary>
        public void LockToFileAndChain(string filePath, ChainType chainType)
        {
            _lockedFilePath = filePath;
            _lockedChainType = chainType;
            _isLocked = true;
            
            // Update the tab name to reflect the lock
            UpdateTabNameForLock();
            
            System.Diagnostics.Debug.WriteLine($"Chat tab '{Name}' locked to file '{filePath}' with chain '{chainType}'");
        }
        
        /// <summary>
        /// Unlocks this chat tab, allowing it to dynamically adapt to context again
        /// </summary>
        public void Unlock()
        {
            _lockedFilePath = null;
            _lockedChainType = null;
            _isLocked = false;
            
            // SPLENDID: Reset tab name based on current state after unlocking
            if (!string.IsNullOrEmpty(_currentPersona))
            {
                UpdateTabNameForPersona();
            }
            else
            {
                UpdateTabNameForGenericChat();
            }
            
            System.Diagnostics.Debug.WriteLine($"Chat tab '{Name}' unlocked");
        }
        
        /// <summary>
        /// Updates the tab name to include filename and chain type when locked
        /// </summary>
        private void UpdateTabNameForLock()
        {
            if (_isLocked && !string.IsNullOrEmpty(_lockedFilePath) && _lockedChainType.HasValue)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(_lockedFilePath);
                string chainName = _lockedChainType.Value.ToString();
                
                // Create a more readable chain name
                string displayChainName = chainName switch
                {
                    "FictionWriting" => "Fiction",
                    "CharacterDevelopment" => "Character",
                    "StoryAnalysis" => "Analysis",
                    "OutlineWriter" => "Outline",
                    "RulesWriter" => "Rules",
                    "Proofreader" => "Proofread",
                    _ => chainName
                };
                
                Name = $"{fileName} - {displayChainName}";
            }
        }
        
        /// <summary>
        /// Updates the tab name to include persona for general chat tabs
        /// </summary>
        private void UpdateTabNameForPersona()
        {
            if (!string.IsNullOrEmpty(_currentPersona))
            {
                // Extract a short, readable persona name
                string shortPersonaName = GetShortPersonaName(_currentPersona);
                Name = $"Chat - {shortPersonaName}";
            }
            else
            {
                UpdateTabNameForGenericChat();
            }
        }
        
        /// <summary>
        /// Updates the tab name for generic chat tabs to include model name
        /// </summary>
        private void UpdateTabNameForGenericChat()
        {
            if (_selectedModel != null)
            {
                // Get a short, user-friendly model name
                string shortModelName = GetShortModelName(_selectedModel);
                Name = $"Chat - {shortModelName}";
            }
            else
            {
                Name = "Chat";
            }
        }
        
        /// <summary>
        /// Extracts a short, readable name from a persona string
        /// </summary>
        private string GetShortPersonaName(string persona)
        {
            if (string.IsNullOrEmpty(persona))
                return "Default";
                
            // Common persona patterns and their short names
            var personaMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "You are a helpful assistant", "Assistant" },
                { "You are a creative writing assistant", "Writer" },
                { "You are a coding assistant", "Coder" },
                { "You are a research assistant", "Researcher" },
                { "You are a brainstorming partner", "Brainstorm" },
                { "You are a proofreader", "Proofreader" },
                { "You are an expert", "Expert" },
                { "You are a teacher", "Teacher" },
                { "You are a therapist", "Therapist" },
                { "You are a coach", "Coach" },
            };
            
            // Check for exact matches first
            foreach (var mapping in personaMapping)
            {
                if (persona.Equals(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value;
                }
            }
            
            // Look for partial matches
            foreach (var mapping in personaMapping)
            {
                if (persona.Contains(mapping.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return mapping.Value;
                }
            }
            
            // If persona contains "You are a/an [role]", extract the role
            if (persona.ToLowerInvariant().StartsWith("you are a "))
            {
                string role = persona.Substring(10).Split(' ', '.', ',')[0];
                if (!string.IsNullOrEmpty(role) && role.Length > 2)
                {
                    return char.ToUpper(role[0]) + role.Substring(1).ToLowerInvariant();
                }
            }
            else if (persona.ToLowerInvariant().StartsWith("you are an "))
            {
                string role = persona.Substring(11).Split(' ', '.', ',')[0];
                if (!string.IsNullOrEmpty(role) && role.Length > 2)
                {
                    return char.ToUpper(role[0]) + role.Substring(1).ToLowerInvariant();
                }
            }
            
            // Fallback: use first 15 characters if no pattern matches
            if (persona.Length <= 15)
                return persona;
            else
                return persona.Substring(0, 12) + "...";
        }
        
        /// <summary>
        /// Extracts a short, user-friendly name from a model info
        /// </summary>
        private string GetShortModelName(AIModelInfo model)
        {
            if (model == null)
                return "Unknown";
            
            string modelName = model.Name;
            
            // Common patterns to create user-friendly names
            if (modelName.StartsWith("claude-sonnet-4") || modelName.StartsWith("claude-4"))
                return "Claude 4 Sonnet";
            else if (modelName.StartsWith("claude-3-5-sonnet"))
                return "Claude 3.5 Sonnet";
            else if (modelName.StartsWith("claude-3-sonnet"))
                return "Claude 3 Sonnet";
            else if (modelName.StartsWith("claude-3-haiku"))
                return "Claude 3 Haiku";
            else if (modelName.StartsWith("claude-3-opus"))
                return "Claude 3 Opus";
            else if (modelName.StartsWith("gpt-4o-mini"))
                return "GPT-4o Mini";
            else if (modelName.StartsWith("gpt-4o"))
                return "GPT-4o";
            else if (modelName.StartsWith("gpt-4-turbo"))
                return "GPT-4 Turbo";
            else if (modelName.StartsWith("gpt-4"))
                return "GPT-4";
            else if (modelName.StartsWith("gpt-3.5-turbo"))
                return "GPT-3.5 Turbo";
            else if (modelName.StartsWith("grok-"))
                return "Grok";
            else if (modelName.Contains("llama"))
                return "Llama";
            else if (modelName.Contains("mistral"))
                return "Mistral";
            else if (modelName.Contains("gemma"))
                return "Gemma";
            
            // For other models, try to extract a readable name
            // Remove common prefixes and suffixes
            string cleanName = modelName;
            if (cleanName.Contains("/"))
            {
                // For OpenRouter models like "openrouter/anthropic/claude-3-sonnet"
                cleanName = cleanName.Split('/').Last();
            }
            
            // Remove version numbers and technical suffixes
            cleanName = cleanName.Replace("-latest", "").Replace("-preview", "");
            
            // Capitalize first letter and limit length
            if (cleanName.Length > 20)
                cleanName = cleanName.Substring(0, 17) + "...";
            
            return char.ToUpper(cleanName[0]) + cleanName.Substring(1);
        }

        public ChatTabViewModel(string name = "New Chat")
        {
            Name = name;
            Messages = new ObservableCollection<Models.ChatMessage>();
            ChatModeMessages = new ObservableCollection<Models.ChatMessage>();
            InputText = string.Empty;
            
            // Initialize with default Chat chain
            UpdateAvailableChains();
        }
        
        /// <summary>
        /// Updates the available chains based on the current file type
        /// </summary>
        private void UpdateAvailableChains()
        {
            var previousSelectedType = SelectedChain?.Type;
            AvailableChains = ChainInfo.GetAvailableChainsForFileType(DetectedFileType);
            
            System.Diagnostics.Debug.WriteLine($"UpdateAvailableChains for tab '{Name}': FileType='{DetectedFileType}', Found {AvailableChains.Count} chains");
            foreach (var chain in AvailableChains)
            {
                System.Diagnostics.Debug.WriteLine($"  - {chain.DisplayName} ({chain.Type})");
            }
            
            // SPLENDID: Don't reset chain selection for locked tabs - they maintain their locked chain
            if (_isLocked)
            {
                // For locked tabs, ensure the locked chain is available and selected
                if (_lockedChainType.HasValue)
                {
                    var lockedChainInfo = AvailableChains.FirstOrDefault(c => c.Type == _lockedChainType.Value);
                    if (lockedChainInfo == null)
                    {
                        // Add the locked chain to available chains even if it's not normally available for this file type
                        string displayName = _lockedChainType.Value.ToString() switch
                        {
                            "FictionWriting" => "Fiction Writing",
                            "CharacterDevelopment" => "Character Development", 
                            "StoryAnalysis" => "Story Analysis",
                            "OutlineWriter" => "Outline Writer",
                            "RulesWriter" => "Rules Writer",
                            "Proofreader" => "Proofreader",
                            "StyleGuide" => "Style Guide Chain",
                            _ => _lockedChainType.Value.ToString()
                        };
                        
                        lockedChainInfo = new ChainInfo 
                        { 
                            Type = _lockedChainType.Value, 
                            DisplayName = displayName,
                            Description = "Locked chain for this tab"
                        };
                        AvailableChains.Add(lockedChainInfo);
                    }
                    
                    // Ensure the locked chain stays selected
                    if (SelectedChain == null || SelectedChain.Type != _lockedChainType.Value)
                    {
                        SelectedChain = lockedChainInfo;
                        System.Diagnostics.Debug.WriteLine($"Locked tab '{Name}' maintaining chain selection: {lockedChainInfo.DisplayName}");
                    }
                }
                return;
            }
            
            // For non-locked tabs, apply normal logic
            // If current selection is no longer available, reset to Chat
            if (SelectedChain == null || !AvailableChains.Any(c => c.Type == SelectedChain.Type))
            {
                SelectedChain = AvailableChains.FirstOrDefault(c => c.Type == ChainType.Chat);
                System.Diagnostics.Debug.WriteLine($"Tab '{Name}' defaulting to Chat chain. SelectedChain is now: {SelectedChain?.DisplayName ?? "NULL"}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"Tab '{Name}' keeping existing chain selection: {SelectedChain?.DisplayName ?? "NULL"}");
            }
            
            // Ensure we ALWAYS have a selection
            if (SelectedChain == null && AvailableChains.Any())
            {
                SelectedChain = AvailableChains.First();
                System.Diagnostics.Debug.WriteLine($"Tab '{Name}' emergency fallback to first chain: {SelectedChain?.DisplayName ?? "NULL"}");
            }
            
            System.Diagnostics.Debug.WriteLine($"Tab '{Name}' updated chains for file type '{DetectedFileType}': {string.Join(", ", AvailableChains.Select(c => c.DisplayName))}");
            System.Diagnostics.Debug.WriteLine($"Tab '{Name}' final SelectedChain: {SelectedChain?.DisplayName ?? "NULL"}");
        }
        
        /// <summary>
        /// Updates the file type and refreshes available chains
        /// </summary>
        public void UpdateFileType(string fileType)
        {
            DetectedFileType = fileType;
        }
        
        /// <summary>
        /// Adds a message to the appropriate collection and manages cleanup
        /// </summary>
        public void AddMessage(Models.ChatMessage message)
        {
            if (IsContextMode)
            {
                Messages.Add(message);
                CleanupMessagesIfNeeded(Messages);
            }
            else
            {
                ChatModeMessages.Add(message);
                CleanupMessagesIfNeeded(ChatModeMessages);
            }
        }
        
        /// <summary>
        /// Cleans up old messages when limits are exceeded
        /// </summary>
        private void CleanupMessagesIfNeeded(ObservableCollection<Models.ChatMessage> messageCollection)
        {
            if (messageCollection.Count <= CLEANUP_THRESHOLD)
                return;
                
            System.Diagnostics.Debug.WriteLine($"Tab '{Name}' has {messageCollection.Count} messages, starting cleanup...");
            
            // Calculate how many messages to remove
            int messagesToRemove = messageCollection.Count - MAX_MESSAGES_PER_TAB;
            if (messagesToRemove <= 0)
                return;
            
            // Remove oldest messages, but preserve system messages and recent user context
            var messagesToDelete = new List<Models.ChatMessage>();
            int removedCount = 0;
            
            for (int i = 0; i < messageCollection.Count && removedCount < messagesToRemove; i++)
            {
                var message = messageCollection[i];
                
                // Keep system messages, user messages, and recent context (last 10 messages)
                if (message.Role == "system" || message.Role == "user" || i >= messageCollection.Count - 10)
                    continue;
                    
                messagesToDelete.Add(message);
                removedCount++;
            }
            
            // Remove selected messages
            foreach (var message in messagesToDelete)
            {
                messageCollection.Remove(message);
            }
            
            // Add a system message indicating cleanup occurred
            if (messagesToDelete.Count > 0)
            {
                var cleanupMessage = new Models.ChatMessage("system", 
                    $"[Memory cleanup: Removed {messagesToDelete.Count} old messages to maintain performance]")
                {
                    Timestamp = DateTime.Now
                };
                messageCollection.Insert(0, cleanupMessage);
                
                System.Diagnostics.Debug.WriteLine($"Tab '{Name}' cleanup complete: removed {messagesToDelete.Count} messages, {messageCollection.Count} remaining");
                
                // Also optimize remaining messages for memory efficiency
                OptimizeMessageStorage();
            }
        }
        
        /// <summary>
        /// Gets estimated memory usage for this tab
        /// </summary>
        public long GetEstimatedMemoryUsage()
        {
            long totalBytes = 0;
            
            // Estimate memory for messages (rough calculation)
            foreach (var message in Messages)
            {
                totalBytes += (message.Content?.Length ?? 0) * 2; // UTF-16 characters
                totalBytes += 100; // Overhead for object, timestamp, etc.
            }
            
            foreach (var message in ChatModeMessages)
            {
                totalBytes += (message.Content?.Length ?? 0) * 2; // UTF-16 characters
                totalBytes += 100; // Overhead for object, timestamp, etc.
            }
            
            return totalBytes;
        }
        
        /// <summary>
        /// Clears all messages in this tab
        /// </summary>
        public void ClearAllMessages()
        {
            Messages.Clear();
            ChatModeMessages.Clear();
            System.Diagnostics.Debug.WriteLine($"Tab '{Name}' cleared all messages");
        }
        
        /// <summary>
        /// Optimizes memory usage by compressing older messages
        /// </summary>
        public void OptimizeMessageStorage()
        {
            OptimizeMessageCollection(Messages);
            OptimizeMessageCollection(ChatModeMessages);
        }
        
        private void OptimizeMessageCollection(ObservableCollection<Models.ChatMessage> messageCollection)
        {
            if (messageCollection.Count <= 10)
                return; // Only optimize if we have more than 10 messages
                
            int messagesToOptimize = Math.Max(0, messageCollection.Count - 10); // Keep last 10 uncompressed
            int optimizedCount = 0;
            
            for (int i = 0; i < messagesToOptimize; i++)
            {
                var message = messageCollection[i];
                
                // Skip system messages, user messages, and already optimized messages
                if (message.Role == "system" || message.Role == "user" || IsMessageOptimized(message))
                    continue;
                    
                // Compress message content if it's large
                if (message.Content?.Length > 1000)
                {
                    message.Content = CompressMessageContent(message.Content);
                    optimizedCount++;
                }
            }
            
            if (optimizedCount > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Tab '{Name}' optimized {optimizedCount} messages for memory efficiency");
            }
        }
        
        private bool IsMessageOptimized(Models.ChatMessage message)
        {
            // Simple check: if content starts with our compression marker
            return message.Content?.StartsWith("📦") == true;
        }
        
        private string CompressMessageContent(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length <= 1000)
                return content;
                
            try
            {
                // Simple compression: keep first and last parts, summarize middle
                int keepStart = 300;
                int keepEnd = 200;
                
                if (content.Length <= keepStart + keepEnd + 100)
                    return content; // Not worth compressing
                    
                string startPart = content.Substring(0, keepStart);
                string endPart = content.Substring(content.Length - keepEnd);
                int removedLength = content.Length - keepStart - keepEnd;
                
                // Create compressed representation
                string compressed = $"{startPart}\n\n📦 [Compressed: {removedLength} characters hidden for memory optimization]\n\n{endPart}";
                
                System.Diagnostics.Debug.WriteLine($"Compressed message content from {content.Length} to {compressed.Length} characters");
                return compressed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error compressing message content: {ex.Message}");
                return content; // Return original if compression fails
            }
        }
        
        private void SubscribeToEditor(IFileTab editor)
        {
            try
            {
                if (editor is EditorTab editorTab)
                {
                    editorTab.ContentChanged += Editor_ContentChanged;
                }
                else if (editor is Views.MarkdownTabAvalon markdownTab)
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
                else if (editor is Views.MarkdownTabAvalon markdownTab)
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
                else if (_associatedEditor is Views.MarkdownTabAvalon markdownTab) 
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