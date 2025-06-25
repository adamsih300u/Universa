using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Universa.Desktop.Models;
using Universa.Desktop.ViewModels;
using System.Diagnostics;
using Universa.Desktop.Core.Logging;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for managing chat history persistence
    /// </summary>
    public class ChatHistoryService
    {
        private readonly string _historyFilePath;
        private static ChatHistoryService _instance;
        
        public static ChatHistoryService Instance => _instance ??= new ChatHistoryService();
        
        private ChatHistoryService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var universaPath = Path.Combine(appDataPath, "Universa");
            
            if (!Directory.Exists(universaPath))
            {
                Directory.CreateDirectory(universaPath);
            }
            
            _historyFilePath = Path.Combine(universaPath, "chat_history.json");
        }
        
        /// <summary>
        /// Saves chat history to disk
        /// </summary>
        /// <param name="tabs">Collection of chat tabs</param>
        /// <param name="selectedTabIndex">Index of the currently selected tab</param>
        public void SaveChatHistory(ObservableCollection<ChatTabViewModel> tabs, int selectedTabIndex)
        {
            try
            {
                var sessionData = new ChatSessionData
                {
                    SelectedTabIndex = selectedTabIndex,
                    Tabs = tabs.Select(t => new ChatTabData
                    {
                        Name = t.Name,
                        IsContextMode = t.IsContextMode,
                        ModelName = t.SelectedModel?.Name,
                        ModelProvider = t.SelectedModel?.Provider.ToString(),
                        InputText = t.InputText,
                        Messages = new List<Models.ChatMessage>(t.Messages),
                        ChatModeMessages = new List<Models.ChatMessage>(t.ChatModeMessages),
                        ContextModeScrollPosition = t.ContextModeScrollPosition,
                        ChatModeScrollPosition = t.ChatModeScrollPosition
                    }).ToList()
                };
                
                string json = JsonConvert.SerializeObject(sessionData, Formatting.Indented, 
                    new JsonSerializerSettings 
                    { 
                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                        TypeNameHandling = TypeNameHandling.Auto,
                        Formatting = Formatting.Indented,
                        SerializationBinder = new SafeSerializationBinder()
                    });
                
                File.WriteAllText(_historyFilePath, json, Encoding.UTF8);
                Debug.WriteLine($"Chat history saved to {_historyFilePath}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save chat history");
                Debug.WriteLine($"Error saving chat history: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Loads chat history from disk
        /// </summary>
        /// <returns>The saved chat session data or null if no history exists</returns>
        public ChatSessionData LoadChatHistory()
        {
            try
            {
                if (!File.Exists(_historyFilePath))
                {
                    Debug.WriteLine("No chat history file found.");
                    return null;
                }
                
                string json = File.ReadAllText(_historyFilePath, Encoding.UTF8);
                var sessionData = JsonConvert.DeserializeObject<ChatSessionData>(json, 
                    new JsonSerializerSettings 
                    { 
                        TypeNameHandling = TypeNameHandling.Auto,
                        SerializationBinder = new SafeSerializationBinder()
                    });
                
                Debug.WriteLine($"Chat history loaded from {_historyFilePath}");
                return sessionData;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load chat history");
                Debug.WriteLine($"Error loading chat history: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>
    /// Custom serialization binder to handle type resolution safely
    /// </summary>
    public class SafeSerializationBinder : Newtonsoft.Json.Serialization.ISerializationBinder
    {
        public Type BindToType(string assemblyName, string typeName)
        {
            try
            {
                // Only bind to types in our own assembly for safety
                string currentAssembly = typeof(ChatHistoryService).Assembly.GetName().Name;
                
                // Return the ChatMessage type regardless of what assembly was saved in the file
                if (typeName.Contains("ChatMessage"))
                {
                    return typeof(Models.ChatMessage);
                }
                
                // For other types, only resolve if they're in our assembly
                if (assemblyName == currentAssembly || assemblyName.StartsWith("Universa."))
                {
                    return Type.GetType($"{typeName}, {assemblyName}");
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error binding to type {typeName}: {ex.Message}");
                return null;
            }
        }

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = serializedType.Assembly.GetName().Name;
            typeName = serializedType.FullName;
        }
    }
} 