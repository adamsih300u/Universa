using System;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Diagnostics;
using System.Text.Json;
using Universa.Desktop.Models;  // Make sure we're using the correct namespace

namespace Universa.Desktop.Services
{
    public class MusicChain : BaseLangChainService
    {
        private string _trackListing;
        private string _nowPlaying;
        private static MusicChain _instance;
        private static readonly object _lock = new object();

        private MusicChain(string apiKey, string model, Models.AIProvider provider, string trackListing, string nowPlaying = null)
            : base(apiKey, model, provider)
        {
            _trackListing = trackListing;
            _nowPlaying = nowPlaying;
            InitializeSystemMessage();
        }

        public static MusicChain GetInstance(string apiKey, string model, Models.AIProvider provider, string trackListing, string nowPlaying = null)
        {
            lock (_lock)
            {
                if (_instance == null || _instance._trackListing != trackListing || _instance._nowPlaying != nowPlaying)
                {
                    // Create new instance or update existing one if content has changed
                    _instance = new MusicChain(apiKey, model, provider, trackListing, nowPlaying);
                }
                return _instance;
            }
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = new StringBuilder();
            systemPrompt.AppendLine("You are a music assistant specializing in playlist generation, music recommendations, and music library management.");
            systemPrompt.AppendLine("You have access to the following context:");
            systemPrompt.AppendLine("1. Currently playing track (if any)");
            systemPrompt.AppendLine("2. List of displayed tracks in the current view");
            systemPrompt.AppendLine("3. Current view mode (Tracks or Navigation)");
            systemPrompt.AppendLine("\nWhen responding to queries:");
            systemPrompt.AppendLine("- Reference the currently playing track when relevant");
            systemPrompt.AppendLine("- Consider the displayed tracks when making recommendations");
            systemPrompt.AppendLine("- Provide specific, contextual responses based on the available music information");
            systemPrompt.AppendLine("- Help with playlist management, music organization, and track selection");

            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                systemMessage.Content = systemPrompt.ToString();
            }
            else
            {
                _memory.Insert(0, new MemoryMessage("system", systemPrompt.ToString(), _model));
            }
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                if (!string.IsNullOrEmpty(request))
                {
                    // Add context information if available
                    var contextBuilder = new StringBuilder();
                    
                    // Parse the content to extract Now Playing info
                    if (!string.IsNullOrEmpty(content))
                    {
                        try
                        {
                            var musicContext = JsonSerializer.Deserialize<JsonElement>(content);
                            if (musicContext.TryGetProperty("NowPlaying", out var nowPlaying) && nowPlaying.ValueKind != JsonValueKind.Null)
                            {
                                contextBuilder.AppendLine("Now Playing:");
                                contextBuilder.AppendLine(JsonSerializer.Serialize(nowPlaying, new JsonSerializerOptions { WriteIndented = true }));
                                contextBuilder.AppendLine();
                            }
                            
                            if (musicContext.TryGetProperty("DisplayedTracks", out var tracks))
                            {
                                contextBuilder.AppendLine("Current View:");
                                contextBuilder.AppendLine(JsonSerializer.Serialize(tracks, new JsonSerializerOptions { WriteIndented = true }));
                            }
                        }
                        catch (JsonException ex)
                        {
                            Debug.WriteLine($"Error parsing music context: {ex.Message}");
                        }
                    }
                    
                    // Add the context and user request to memory
                    if (contextBuilder.Length > 0)
                    {
                        AddUserMessage(contextBuilder.ToString());
                    }
                    AddUserMessage(request);
                    
                    // Get response from AI using the memory context
                    var response = await ExecutePrompt(string.Empty);
                    
                    // Add the response to memory
                    AddAssistantMessage(response);

                    return response;
                }
                
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"\n=== MUSIC CHAIN ERROR ===\n{ex}");
                throw;
            }
        }

        protected override string BuildBasePrompt(string content, string request)
        {
            // This method is no longer used since we're handling the system message and conversation history separately
            return string.Empty;
        }

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instance = null;
            }
        }

        public async Task<string> GetMusicCharacteristicsAsync(string prompt)
        {
            // Music characterization feature has been removed
            Debug.WriteLine("Music characterization feature has been removed");
            return "[]";
        }
    }
} 