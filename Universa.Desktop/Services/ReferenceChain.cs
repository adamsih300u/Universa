using System;
using System.Threading.Tasks;
using System.Linq;
using Universa.Desktop.Models;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    public class ReferenceChain : BaseLangChainService
    {
        private string _referenceContent;
        private readonly FileReferenceService _fileReferenceService;
        private static ReferenceChain _instance;
        private static readonly object _lock = new object();
        private List<FileReference> _references;
        private string _currentFilePath;

        private ReferenceChain(string apiKey, string model, Models.AIProvider provider, string referenceContent)
            : base(apiKey, model, provider)
        {
            _referenceContent = referenceContent;
            var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            var libraryPath = configService?.Provider?.LibraryPath;
            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new InvalidOperationException("Library path is not configured. Please set it in the settings.");
            }
            _fileReferenceService = new FileReferenceService(libraryPath);
            _references = new List<FileReference>();

            // Extract current file path from content if available
            var contentLines = referenceContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var filePathLine = contentLines.FirstOrDefault(l => l.TrimStart().StartsWith("#file:"));
            if (filePathLine != null)
            {
                _currentFilePath = filePathLine.TrimStart().Substring(6).Trim();
                _fileReferenceService.SetCurrentFile(_currentFilePath);
                Debug.WriteLine($"Set current file path from content: {_currentFilePath}");
            }
            else
            {
                Debug.WriteLine("Warning: No current file path set. Relative paths may not resolve correctly.");
            }
        }

        public static ReferenceChain GetInstance(string apiKey, string model, Models.AIProvider provider, string referenceContent)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new ReferenceChain(apiKey, model, provider, referenceContent);
                    Task.Run(async () => await _instance.InitializeSystemMessage()).ConfigureAwait(false);
                }
                else if (_instance._referenceContent != referenceContent)
                {
                    // If reference content changed, update it and reinitialize
                    _instance._referenceContent = referenceContent;
                    Task.Run(async () => await _instance.InitializeSystemMessage()).ConfigureAwait(false);
                }
                return _instance;
            }
        }

        public static async Task<ReferenceChain> GetInstanceAsync(string apiKey, string model, Models.AIProvider provider, string referenceContent)
        {
            ReferenceChain instance;
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new ReferenceChain(apiKey, model, provider, referenceContent);
                    instance = _instance;
                }
                else if (_instance._referenceContent != referenceContent)
                {
                    _instance._referenceContent = referenceContent;
                    instance = _instance;
                }
                else
                {
                    instance = _instance;
                }
            }
            await instance.InitializeSystemMessage();
            return instance;
        }

        private async Task LoadReferences()
        {
            try
            {
                Debug.WriteLine("Starting LoadReferences...");
                Debug.WriteLine($"Reference content length: {_referenceContent?.Length ?? 0}");
                if (!string.IsNullOrEmpty(_referenceContent))
                {
                    Debug.WriteLine($"First 100 chars: {_referenceContent.Substring(0, Math.Min(100, _referenceContent.Length))}");
                }

                // Check for frontmatter
                Dictionary<string, string> frontmatter = null;
                string contentWithoutFrontmatter = _referenceContent;
                
                if (_referenceContent.StartsWith("---\n") || _referenceContent.StartsWith("---\r\n"))
                {
                    // Extract frontmatter
                    frontmatter = ExtractFrontmatter(_referenceContent, out contentWithoutFrontmatter);
                    if (frontmatter != null)
                    {
                        Debug.WriteLine("Frontmatter found in reference document:");
                        foreach (var kvp in frontmatter)
                        {
                            Debug.WriteLine($"  {kvp.Key}: {kvp.Value}");
                        }
                    }
                }

                // Look for #data or #ref data: lines at the start of the content
                var contentLines = contentWithoutFrontmatter.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var referenceLines = new StringBuilder();
                var mainContent = new StringBuilder();
                bool parsingRefs = true;

                // First, find and set the current file path if not already set
                if (string.IsNullOrEmpty(_currentFilePath))
                {
                    var filePathLine = contentLines
                        .FirstOrDefault(l => l.TrimStart().StartsWith("#file:"));
                    if (filePathLine != null)
                    {
                        _currentFilePath = filePathLine.TrimStart().Substring(6).Trim();
                        _fileReferenceService.SetCurrentFile(_currentFilePath);
                        Debug.WriteLine($"Set current file path from content: {_currentFilePath}");
                    }
                    else
                    {
                        Debug.WriteLine("Warning: No current file path set. Relative paths may not resolve correctly.");
                    }
                }

                // Process references from frontmatter if available
                if (frontmatter != null)
                {
                    // Process style reference
                    if (frontmatter.TryGetValue("ref style", out string styleRef) || 
                        frontmatter.TryGetValue("style", out styleRef))
                    {
                        Debug.WriteLine($"Processing style reference from frontmatter: {styleRef}");
                        referenceLines.AppendLine($"#ref style:{styleRef}");
                    }
                    
                    // Process rules reference
                    if (frontmatter.TryGetValue("ref rules", out string rulesRef) || 
                        frontmatter.TryGetValue("rules", out rulesRef))
                    {
                        Debug.WriteLine($"Processing rules reference from frontmatter: {rulesRef}");
                        referenceLines.AppendLine($"#ref rules:{rulesRef}");
                    }
                    
                    // Process outline reference
                    if (frontmatter.TryGetValue("ref outline", out string outlineRef) || 
                        frontmatter.TryGetValue("outline", out outlineRef))
                    {
                        Debug.WriteLine($"Processing outline reference from frontmatter: {outlineRef}");
                        referenceLines.AppendLine($"#ref outline:{outlineRef}");
                    }
                    
                    // Process data reference
                    if (frontmatter.TryGetValue("ref data", out string dataRef) || 
                        frontmatter.TryGetValue("data", out dataRef))
                    {
                        Debug.WriteLine($"Processing data reference from frontmatter: {dataRef}");
                        referenceLines.AppendLine($"#ref data:{dataRef}");
                    }
                    
                    // Process any other references with 'ref' prefix
                    foreach (var kvp in frontmatter)
                    {
                        if (kvp.Key.StartsWith("ref ") && 
                            !kvp.Key.Equals("ref style") && 
                            !kvp.Key.Equals("ref rules") && 
                            !kvp.Key.Equals("ref outline") && 
                            !kvp.Key.Equals("ref data"))
                        {
                            string refType = kvp.Key.Substring(4); // Remove 'ref ' prefix
                            Debug.WriteLine($"Processing {refType} reference from frontmatter: {kvp.Value}");
                            referenceLines.AppendLine($"#ref {refType}:{kvp.Value}");
                        }
                    }
                }

                foreach (var line in contentLines)
                {
                    var trimmedLine = line.TrimStart();
                    Debug.WriteLine($"Processing line: {trimmedLine}");

                    if (string.IsNullOrWhiteSpace(trimmedLine))
                        continue;

                    if (parsingRefs)
                    {
                        if (trimmedLine.StartsWith("#data ") || trimmedLine.StartsWith("#ref data:"))
                        {
                            // If it's a #data line, convert it to #ref data: format
                            if (trimmedLine.StartsWith("#data "))
                            {
                                var dataPath = trimmedLine.Substring(6).Trim();
                                referenceLines.AppendLine($"#ref data:{dataPath}");
                                Debug.WriteLine($"Converted #data line to: #ref data:{dataPath}");
                            }
                            else
                            {
                                // It's already in #ref data: format, use as is
                                referenceLines.AppendLine(trimmedLine);
                                Debug.WriteLine($"Added reference line: {trimmedLine}");
                            }
                        }
                        else if (!trimmedLine.StartsWith("#"))
                        {
                            // If we hit a non-# line, stop parsing references
                            parsingRefs = false;
                            mainContent.AppendLine(line);
                        }
                        else if (trimmedLine.StartsWith("#reference"))
                        {
                            // Skip the #reference tag
                            Debug.WriteLine("Skipping #reference tag");
                            continue;
                        }
                    }
                    else
                    {
                        mainContent.AppendLine(line);
                    }
                }

                // Load referenced files
                var referencesContent = referenceLines.ToString();
                Debug.WriteLine($"References to load:\n{referencesContent}");
                _references = await _fileReferenceService.LoadReferencesAsync(referencesContent);
                _referenceContent = mainContent.ToString().Trim();

                Debug.WriteLine($"Loaded {_references.Count} data references");
                foreach (var reference in _references)
                {
                    Debug.WriteLine($"Reference loaded - Type: {reference.Type}, Path: {reference.Path}, Content length: {reference.Content?.Length ?? 0}");
                    if (string.IsNullOrEmpty(reference.Content))
                    {
                        Debug.WriteLine($"Warning: Empty content for reference {reference.Path}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading references: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Extracts frontmatter from content
        /// </summary>
        private Dictionary<string, string> ExtractFrontmatter(string content, out string contentWithoutFrontmatter)
        {
            contentWithoutFrontmatter = content;
            var frontmatter = new Dictionary<string, string>();
            
            // Check if the content starts with frontmatter delimiter
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing delimiter
                int endIndex = -1;
                
                // Skip the first line (opening delimiter)
                int startIndex = content.IndexOf('\n') + 1;
                if (startIndex < content.Length)
                {
                    // Look for closing delimiter
                    endIndex = content.IndexOf("\n---", startIndex);
                    if (endIndex > startIndex)
                    {
                        // Extract frontmatter content
                        string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
                        
                        // Parse frontmatter
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string line in lines)
                        {
                            // Look for key-value pairs (key: value)
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                string key = line.Substring(0, colonIndex).Trim();
                                string value = line.Substring(colonIndex + 1).Trim();
                                
                                // Store in dictionary - remove hashtag if present
                                if (key.StartsWith("#"))
                                {
                                    key = key.Substring(1);
                                }
                                
                                frontmatter[key] = value;
                            }
                            else if (line.StartsWith("#"))
                            {
                                // Handle tags (like #fiction) - remove hashtag
                                string tag = line.Trim().Substring(1);
                                frontmatter[tag] = "true";
                            }
                        }
                        
                        // Skip past the closing delimiter
                        int contentStartIndex = endIndex + 4; // Length of "\n---"
                        if (contentStartIndex < content.Length)
                        {
                            // If there's a newline after the closing delimiter, skip it too
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            // Return the content without frontmatter
                            contentWithoutFrontmatter = content.Substring(contentStartIndex);
                        }
                    }
                }
            }
            
            return frontmatter;
        }

        private async Task InitializeSystemMessage()
        {
            try
            {
                await LoadReferences();

                var systemPrompt = new StringBuilder();
                systemPrompt.AppendLine("You are a reference material assistant. Help analyze and provide insights about the reference content. When answering questions, focus on the specific reference material provided.");
                systemPrompt.AppendLine();

                // Add referenced data files first
                if (_references.Any())
                {
                    systemPrompt.AppendLine("Reference Data:");
                    foreach (var reference in _references)
                    {
                        if (!string.IsNullOrEmpty(reference.Content))
                        {
                            systemPrompt.AppendLine($"=== Data from: {reference.Path} ===");
                            systemPrompt.AppendLine(reference.Content);
                            systemPrompt.AppendLine();
                        }
                        else
                        {
                            Debug.WriteLine($"Warning: Empty content for reference {reference.Path}");
                        }
                    }
                    systemPrompt.AppendLine("Main Content:");
                }

                if (!string.IsNullOrEmpty(_referenceContent))
                {
                    systemPrompt.AppendLine(_referenceContent);
                }
                else
                {
                    Debug.WriteLine("Warning: Empty main content");
                }

                // Set the system message
                _memory.RemoveAll(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                _memory.Insert(0, new MemoryMessage("system", systemPrompt.ToString(), _model));
                
                Debug.WriteLine("\n=== SYSTEM MESSAGE DEBUG ===");
                Debug.WriteLine($"System message length: {systemPrompt.Length}");
                Debug.WriteLine($"System message preview: {systemPrompt.ToString().Substring(0, Math.Min(200, systemPrompt.Length))}...");
                Debug.WriteLine($"Number of references: {_references.Count}");
                foreach (var reference in _references)
                {
                    Debug.WriteLine($"Reference {reference.Path}: {reference.Content?.Length ?? 0} bytes");
                }
                Debug.WriteLine($"Main content: {_referenceContent?.Length ?? 0} bytes");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error initializing system message: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task SetCurrentFilePathAsync(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath))
            {
                _currentFilePath = filePath;
                _fileReferenceService.SetCurrentFile(filePath);
                Debug.WriteLine($"Set current file path in ReferenceChain: {filePath}");
                
                // Now that we have the file path, initialize the system message
                await InitializeSystemMessage();
            }
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                // Only process the request if one was provided
                if (!string.IsNullOrEmpty(request))
                {
                    // Make sure system message is initialized
                    if (!_memory.Any(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase)))
                    {
                        await InitializeSystemMessage();
                    }

                    // Add the user request to memory
                    AddUserMessage(request);
                    
                    Debug.WriteLine("\n=== SYSTEM MESSAGE DEBUG ===");
                    var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                    if (systemMessage != null)
                    {
                        Debug.WriteLine($"System message length: {systemMessage.Content?.Length ?? 0}");
                        Debug.WriteLine($"System message preview: {systemMessage.Content?.Substring(0, Math.Min(200, systemMessage.Content?.Length ?? 0))}...");
                    }
                    else
                    {
                        Debug.WriteLine("No system message found!");
                    }
                    
                    // Get response from AI using the memory context
                    var response = await ExecutePrompt(string.Empty);
                    
                    // Add the response to memory
                    AddAssistantMessage(response);

                    return response;
                }
                
                return string.Empty;  // No request to process
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== REFERENCE CHAIN ERROR ===\n{ex}");
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
    }
} 