using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Text;
using System.Threading;
using System.Diagnostics;
using Universa.Desktop.Core.Configuration;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Non-fiction writing service that leverages Fiction Writing Beta capabilities
    /// while providing specialized functionality for non-fiction content
    /// </summary>
    public class NonFictionWritingBeta : BaseLangChainService
    {
        private FictionWritingBeta _fictionWritingService;
        private NonFictionType _nonFictionType = NonFictionType.General;
        private Dictionary<string, string> _frontmatter = new Dictionary<string, string>();
        private string _currentFilePath;

        public enum NonFictionType
        {
            General,
            Biography,
            Autobiography,
            Memoir,
            History,
            Academic,
            Journalism
        }

        public NonFictionWritingBeta(string apiKey, string model, AIProvider provider, string filePath = null, string libraryPath = null)
            : base(apiKey, model, provider)
        {
            _currentFilePath = filePath;
            // Create the underlying fiction writing service but don't initialize it yet
            // We'll delegate most operations to it
        }

        public static async Task<NonFictionWritingBeta> GetInstance(string apiKey, string model, AIProvider provider, string filePath, string libraryPath)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("API key and model must be provided");
            }

            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new ArgumentNullException(nameof(libraryPath));
            }

            var instance = new NonFictionWritingBeta(apiKey, model, provider, filePath, libraryPath);
            
            // Get the underlying fiction writing service
            instance._fictionWritingService = await FictionWritingBeta.GetInstance(apiKey, model, provider, filePath, libraryPath);
            
            // Forward events from the underlying service
            instance._fictionWritingService.OnRetryingOverloadedRequest += instance.OnFictionServiceRetryEvent;
            
            return instance;
        }

        public async Task UpdateContentAndInitialize(string content)
        {
            // Process frontmatter to determine non-fiction type
            ProcessFrontmatter(content);
            
            // Let the fiction service handle the content loading
            await _fictionWritingService.UpdateContentAndInitialize(content);
            
            // Copy memory from fiction service to maintain conversation
            ClearMemory();
            foreach (var message in _fictionWritingService.GetMessageHistory())
            {
                _memory.Add(new MemoryMessage(message.Role, message.Content, message.Model));
            }
            
            // Override with non-fiction specific system message
            UpdateSystemMessage();
        }

        private void ProcessFrontmatter(string content)
        {
            _frontmatter.Clear();
            
            if (string.IsNullOrEmpty(content))
                return;

            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing delimiter
                int startIndex = content.IndexOf('\n') + 1;
                if (startIndex < content.Length)
                {
                    int endIndex = content.IndexOf("\n---", startIndex);
                    if (endIndex > startIndex)
                    {
                        string frontmatterContent = content.Substring(startIndex, endIndex - startIndex);
                        string[] lines = frontmatterContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        foreach (string line in lines)
                        {
                            int colonIndex = line.IndexOf(':');
                            if (colonIndex > 0)
                            {
                                string key = line.Substring(0, colonIndex).Trim();
                                string value = line.Substring(colonIndex + 1).Trim();
                                
                                if (key.StartsWith("#"))
                                {
                                    key = key.Substring(1);
                                }
                                
                                _frontmatter[key] = value;
                            }
                            else if (line.StartsWith("#"))
                            {
                                string tag = line.Trim().Substring(1);
                                _frontmatter[tag] = "true";
                            }
                        }
                    }
                }
            }

            // Determine non-fiction type from frontmatter
            if (_frontmatter.TryGetValue("nonfiction_type", out string typeValue) ||
                _frontmatter.TryGetValue("type", out typeValue))
            {
                if (Enum.TryParse<NonFictionType>(typeValue, true, out NonFictionType parsedType))
                {
                    _nonFictionType = parsedType;
                }
            }
        }

        private void UpdateSystemMessage()
        {
            string systemPrompt = BuildNonFictionSystemPrompt();
            AddSystemMessage(systemPrompt);
        }

        private string BuildNonFictionSystemPrompt()
        {
            string basePrompt = $@"You are an expert non-fiction writing assistant specializing in {_nonFictionType.ToString().ToLower()} writing. Your role is to help create engaging, well-researched, and factually accurate non-fiction content.

**Non-Fiction Type**: {_nonFictionType}

**Core Responsibilities**:
1. **Factual Accuracy**: Ensure all statements are verifiable and well-sourced
2. **Research Integration**: Help weave research findings naturally into the narrative
3. **Chronological Consistency**: Maintain accurate timelines and historical context
4. **Citation Management**: Suggest proper attribution and source integration
5. **Narrative Flow**: Balance factual content with engaging storytelling";

            // Add type-specific guidance
            switch (_nonFictionType)
            {
                case NonFictionType.Biography:
                case NonFictionType.Autobiography:
                    basePrompt += @"

**Biographical Focus**:
- Maintain chronological accuracy of life events
- Balance personal details with historical context
- Ensure respectful and accurate portrayal of relationships
- Verify dates, locations, and significant events
- Consider the subject's privacy and dignity";
                    break;

                case NonFictionType.Memoir:
                    basePrompt += @"

**Memoir Focus**:
- Emphasize personal reflection and emotional truth
- Maintain authenticity while protecting privacy
- Balance personal narrative with broader themes
- Consider the impact of revelations on others mentioned";
                    break;

                case NonFictionType.History:
                    basePrompt += @"

**Historical Focus**:
- Maintain strict chronological accuracy
- Verify all dates, events, and historical claims
- Consider multiple perspectives on historical events
- Ensure proper context for historical periods
- Cross-reference sources for accuracy";
                    break;

                case NonFictionType.Academic:
                    basePrompt += @"

**Academic Focus**:
- Maintain rigorous academic standards
- Ensure proper citation format
- Support all claims with credible sources
- Follow discipline-specific conventions
- Maintain objective, scholarly tone";
                    break;

                case NonFictionType.Journalism:
                    basePrompt += @"

**Journalistic Focus**:
- Verify all facts through multiple sources
- Maintain objectivity and balance
- Follow journalistic ethics and standards
- Ensure timely and relevant content
- Consider legal implications of statements";
                    break;
            }

            // Add reference processing information if available
            List<string> availableReferences = new List<string>();
            
            if (_frontmatter.ContainsKey("research") || _frontmatter.ContainsKey("ref research"))
                availableReferences.Add("research notes");
            if (_frontmatter.ContainsKey("sources") || _frontmatter.ContainsKey("ref sources"))
                availableReferences.Add("source bibliography");
            if (_frontmatter.ContainsKey("timeline") || _frontmatter.ContainsKey("ref timeline"))
                availableReferences.Add("chronological timeline");
            if (_frontmatter.ContainsKey("style") || _frontmatter.ContainsKey("ref style"))
                availableReferences.Add("style guide");
            if (_frontmatter.ContainsKey("rules") || _frontmatter.ContainsKey("ref rules"))
                availableReferences.Add("rules and guidelines");
            if (_frontmatter.ContainsKey("outline") || _frontmatter.ContainsKey("ref outline"))
                availableReferences.Add("content outline");

            if (availableReferences.Any())
            {
                basePrompt += $@"

**Available Reference Materials**:
{string.Join(", ", availableReferences)}

When providing assistance, always consider and reference these materials for consistency and accuracy.";
            }

            basePrompt += @"

**Writing Standards**:
- Maintain professional, engaging tone appropriate for the subject matter
- Ensure smooth transitions between topics and chapters
- Balance detailed information with readability
- Consider your target audience's knowledge level
- Provide specific, actionable feedback on content and structure

Always prioritize accuracy, clarity, and reader engagement in your responses.";

            return basePrompt;
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            // Process the request through the fiction service first to get the enhanced parsing and context
            string response = await _fictionWritingService.ProcessRequest(content, request);
            
            // Add user message to our memory
            AddUserMessage(request);
            
            // Add assistant response to our memory  
            AddAssistantMessage(response);
            
            return response;
        }

        public void UpdateCursorPosition(int position)
        {
            _fictionWritingService?.UpdateCursorPosition(position);
        }

        public void SetCurrentFilePath(string filePath)
        {
            _currentFilePath = filePath;
            _fictionWritingService?.SetCurrentFilePath(filePath);
        }

        public List<string> ValidateContentAgainstOutline(string content, int? chapterNumber = null)
        {
            return _fictionWritingService?.ValidateContentAgainstOutline(content, chapterNumber) ?? new List<string>();
        }

        public string GetCurrentChapterExpectations()
        {
            return _fictionWritingService?.GetCurrentChapterExpectations() ?? string.Empty;
        }

        public static void ClearInstance(string filePath)
        {
            FictionWritingBeta.ClearInstance(filePath);
        }

        public static void ClearInstance()
        {
            FictionWritingBeta.ClearInstance();
        }

        // Event to match FictionWritingBeta interface
        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;

        public override void Dispose()
        {
            if (_fictionWritingService != null)
            {
                // Unsubscribe from events to prevent memory leaks
                _fictionWritingService.OnRetryingOverloadedRequest -= OnFictionServiceRetryEvent;
                _fictionWritingService.Dispose();
            }
            base.Dispose();
        }

        private void OnFictionServiceRetryEvent(object sender, RetryEventArgs args)
        {
            OnRetryingOverloadedRequest?.Invoke(this, args);
        }
    }

    // Placeholder parsers for future implementation
    public class ResearchParser
    {
        // Future implementation for parsing research notes
    }

    public class SourcesParser
    {
        // Future implementation for parsing bibliography/sources
    }

    public class TimelineParser
    {
        // Future implementation for parsing chronological timelines
    }
} 