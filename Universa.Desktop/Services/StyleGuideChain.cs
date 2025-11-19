using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using Universa.Desktop.Models;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Style Guide Chain - AI assistant for formatting and structuring style guides
    /// Transforms raw style guidance into parser-optimized markdown format
    /// Activated when document type is "style" or when formatting style guides
    /// </summary>
    public class StyleGuideChain : BaseLangChainService
    {
        private readonly FileReferenceService _fileReferenceService;
        private string _currentFilePath;
        private List<FileReference> _references;
        private Dictionary<string, string> _frontmatter;

        public StyleGuideChain(string apiKey, string model, Models.AIProvider provider, FileReferenceService fileReferenceService) 
            : base(apiKey, model, provider)
        {
            _fileReferenceService = fileReferenceService;
            _references = new List<FileReference>();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            return await ProcessStyleGuideFormattingAsync(content, request, _currentFilePath);
        }

        public override async Task<string> ProcessRequest(string content, string request, CancellationToken cancellationToken)
        {
            return await ProcessStyleGuideFormattingAsync(content, request, _currentFilePath);
        }

        public override async Task UpdateContextAsync(string context)
        {
            _currentContext = context;
            
            // Load references from the updated context
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                await ParseFrontmatterAndLoadReferences(context);
            }
        }

        protected async Task<string> SendRequestAsync(string systemPrompt, string userRequest)
        {
            try
            {
                // CRITICAL FIX: Use proper memory system for conversation history
                // Initialize system message if not already present
                var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
                if (systemMessage == null)
                {
                    AddSystemMessage(systemPrompt);
                }
                else if (systemMessage.Content != systemPrompt)
                {
                    // Update system message if content changed (e.g., different document context)
                    systemMessage.Content = systemPrompt;
                }
                
                // Add user request to memory for conversation history
                AddUserMessage(userRequest);
                
                // Execute with memory context (empty prompt since we're using memory)
                var response = await ExecutePrompt("");
                
                // Add response to memory to preserve conversation
                AddAssistantMessage(response);
                
                return response;
            }
            catch (Exception ex)
            {
                return $"Error processing style guide formatting request: {ex.Message}";
            }
        }

        public async Task<string> ProcessStyleGuideFormattingAsync(string content, string prompt, string currentFilePath = null)
        {
            if (!string.IsNullOrEmpty(currentFilePath))
            {
                _currentFilePath = currentFilePath;
                await ParseFrontmatterAndLoadReferences(content);
            }

            // Determine the type of style guide request
            var requestType = DetermineStyleGuideRequestType(prompt);
            
            // Build the specialized prompt for style guide formatting
            var systemPrompt = BuildStyleGuideFormattingPrompt(requestType);
            
            return await SendRequestAsync(systemPrompt, prompt);
        }

        private async Task ParseFrontmatterAndLoadReferences(string content)
        {
            _frontmatter = ParseFrontmatter(content);
            _references.Clear();

            if (_frontmatter != null && _fileReferenceService != null)
            {
                // Load any referenced style files
                foreach (var kvp in _frontmatter)
                {
                    if (kvp.Key.StartsWith("ref_") && !string.IsNullOrEmpty(kvp.Value))
                    {
                        try
                        {
                            var referenceContent = await _fileReferenceService.GetFileContent(kvp.Value, _currentFilePath);
                            if (!string.IsNullOrEmpty(referenceContent))
                            {
                                var reference = new FileReference(kvp.Key, kvp.Value) { Content = referenceContent };
                                _references.Add(reference);
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log but don't fail - continue without this reference
                            System.Diagnostics.Debug.WriteLine($"Failed to load reference {kvp.Value}: {ex.Message}");
                        }
                    }
                }
            }
        }

        private Dictionary<string, string> ParseFrontmatter(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            if (!content.StartsWith("---"))
                return frontmatter;

            var lines = content.Split('\n');
            var inFrontmatter = false;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                if (i == 0 && line == "---")
                {
                    inFrontmatter = true;
                    continue;
                }
                
                if (inFrontmatter && line == "---")
                {
                    break;
                }
                
                if (inFrontmatter && line.Contains(":"))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim();
                        var value = parts[1].Trim().Trim('"');
                        frontmatter[key] = value;
                    }
                }
            }
            
            return frontmatter;
        }

        private StyleGuideRequestType DetermineStyleGuideRequestType(string prompt)
        {
            var lowerPrompt = prompt.ToLower();
            
            if (lowerPrompt.Contains("format") || lowerPrompt.Contains("structure") || lowerPrompt.Contains("organize"))
                return StyleGuideRequestType.Format;
            if (lowerPrompt.Contains("voice") || lowerPrompt.Contains("tone") || lowerPrompt.Contains("style"))
                return StyleGuideRequestType.Voice;
            if (lowerPrompt.Contains("dialogue") || lowerPrompt.Contains("conversation"))
                return StyleGuideRequestType.Dialogue;
            if (lowerPrompt.Contains("description") || lowerPrompt.Contains("narrative") || lowerPrompt.Contains("prose"))
                return StyleGuideRequestType.Description;
            if (lowerPrompt.Contains("technical") || lowerPrompt.Contains("terminology"))
                return StyleGuideRequestType.Technical;
            if (lowerPrompt.Contains("sample") || lowerPrompt.Contains("example"))
                return StyleGuideRequestType.WritingSample;
            
            return StyleGuideRequestType.General;
        }

        private string BuildStyleGuideFormattingPrompt(StyleGuideRequestType requestType)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine("You are an Interactive Style Guide Assistant for the Universa writing platform.");
            prompt.AppendLine("Your role is to work collaboratively with the user to create, improve, and format style guides into properly structured markdown that the StyleGuideParser can process optimally.");
            prompt.AppendLine("");
            
            // Add current content context
            if (!string.IsNullOrEmpty(_currentContext))
            {
                prompt.AppendLine("=== CURRENT STYLE GUIDE CONTENT ===");
                prompt.AppendLine("This is the current style guide content the user is working with:");
                prompt.AppendLine(_currentContext);
                prompt.AppendLine("");
            }
            
            prompt.AppendLine("=== PARSER COMPATIBILITY REQUIREMENTS ===");
            prompt.AppendLine("CRITICAL: The output must use these EXACT section headers for proper parsing:");
            prompt.AppendLine("- ## Voice Rules (for narrative voice, tone, writing style)");
            prompt.AppendLine("- ## Writing Sample (for prose examples)");
            prompt.AppendLine("- ## Dialogue Rules (for general dialogue guidelines - NOT character-specific)");
            prompt.AppendLine("- ## Description Rules (for narrative description guidelines)");
            prompt.AppendLine("- ## Technical Guidelines (for terminology, research, accuracy)");
            prompt.AppendLine("- ## Character Guidelines (for general character development - NOT specific characters)");
            prompt.AppendLine("- ## General Guidelines (for any other style guidance)");
            prompt.AppendLine("");
            
            prompt.AppendLine("=== FORMATTING STANDARDS ===");
            prompt.AppendLine("MANDATORY formatting requirements:");
            prompt.AppendLine("- Use ## (level 2) headers for main sections");
            prompt.AppendLine("- Use - (dash) bullet points for individual rules");
            prompt.AppendLine("- Use markdown hierarchy: ## sections, ### subsections if needed");
            prompt.AppendLine("- Each rule should be one bullet point");
            prompt.AppendLine("- Use keywords that trigger proper rule classification:");
            prompt.AppendLine("  * 'must', 'always', 'never', 'critical' = Critical rules");
            prompt.AppendLine("  * 'should', 'prefer', 'recommended' = Strong recommendations"); 
            prompt.AppendLine("  * 'can', 'may', 'optional' = Optional guidelines");
            prompt.AppendLine("  * 'avoid', 'don't', 'limit' = Restrictions");
            prompt.AppendLine("  * 'e.g.', 'for example', 'such as' = Examples");
            prompt.AppendLine("");
            
            prompt.AppendLine("=== CHARACTER DIALOGUE SEPARATION ===");
            prompt.AppendLine("IMPORTANT: Keep character-specific dialogue styles OUT of the main style guide:");
            prompt.AppendLine("- Character-specific dialogue patterns belong in individual character profile files");
            prompt.AppendLine("- Only include GENERAL dialogue rules that apply to all characters");
            prompt.AppendLine("- Focus on formatting, punctuation, attribution, and universal dialogue principles");
            prompt.AppendLine("- Example general rule: 'Use action beats instead of dialogue tags when possible'");
            prompt.AppendLine("- NOT character-specific: 'Sarah speaks in short, clipped sentences'");
            prompt.AppendLine("");
            
            prompt.AppendLine("=== CONTENT ORGANIZATION PRINCIPLES ===");
            prompt.AppendLine("Structure content for maximum parser effectiveness:");
            prompt.AppendLine("- Group related rules under appropriate section headers");
            prompt.AppendLine("- Order sections by importance: Voice Rules first, General Guidelines last");
            prompt.AppendLine("- Break complex guidance into clear, actionable bullet points");
            prompt.AppendLine("- Include concrete examples when helpful");
            prompt.AppendLine("- Maintain consistent rule phrasing and terminology");
            prompt.AppendLine("");

            // Add reference materials if available
            if (_references.Any())
            {
                prompt.AppendLine("=== REFERENCE MATERIALS ===");
                prompt.AppendLine("Use these project references for consistency:");
                foreach (var reference in _references)
                {
                    var displayName = !string.IsNullOrEmpty(reference.Key) ? reference.Key : System.IO.Path.GetFileNameWithoutExtension(reference.Path);
                    prompt.AppendLine($"** {displayName} **");
                    prompt.AppendLine(reference.Content);
                    prompt.AppendLine("");
                }
            }

            // Add specialized guidance based on request type
            switch (requestType)
            {
                case StyleGuideRequestType.Format:
                    prompt.AppendLine("=== FORMATTING FOCUS ===");
                    prompt.AppendLine("Pay special attention to:");
                    prompt.AppendLine("- Proper section organization and markdown hierarchy");
                    prompt.AppendLine("- Clear rule categorization and bullet point formatting");
                    prompt.AppendLine("- Parser-compatible header structure");
                    break;
                    
                case StyleGuideRequestType.Voice:
                    prompt.AppendLine("=== VOICE RULES FOCUS ===");
                    prompt.AppendLine("Ensure Voice Rules section includes:");
                    prompt.AppendLine("- Narrative perspective (first person, third person, etc.)");
                    prompt.AppendLine("- Tone and mood guidelines");
                    prompt.AppendLine("- Writing style preferences (sentence structure, pacing, etc.)");
                    break;
                    
                case StyleGuideRequestType.Dialogue:
                    prompt.AppendLine("=== DIALOGUE RULES FOCUS ===");
                    prompt.AppendLine("Focus on GENERAL dialogue principles only:");
                    prompt.AppendLine("- Punctuation and formatting standards");
                    prompt.AppendLine("- Attribution and dialogue tag guidelines");
                    prompt.AppendLine("- General dialogue flow and rhythm");
                    prompt.AppendLine("- Do NOT include character-specific speech patterns");
                    break;
                    
                case StyleGuideRequestType.Description:
                    prompt.AppendLine("=== DESCRIPTION RULES FOCUS ===");
                    prompt.AppendLine("Ensure Description Rules section covers:");
                    prompt.AppendLine("- Sensory detail guidelines");
                    prompt.AppendLine("- Scene setting and atmosphere");
                    prompt.AppendLine("- Character description principles");
                    prompt.AppendLine("- Action sequence formatting");
                    break;
            }
            
            prompt.AppendLine("");
            
            // Enhanced interactive guidance
            prompt.AppendLine("=== INTERACTIVE COLLABORATION GUIDELINES ===");
            prompt.AppendLine("APPROACH: Be collaborative and ask clarifying questions when needed:");
            prompt.AppendLine("- If the user's style guidance is unclear, ask specific questions for clarification");
            prompt.AppendLine("- If you need examples to understand their preferences, request them");
            prompt.AppendLine("- If multiple formatting approaches are possible, offer options and ask for preference");
            prompt.AppendLine("- If content seems incomplete, suggest what might be missing and ask if they'd like to add it");
            prompt.AppendLine("- If you see conflicting rules, point them out and ask for resolution");
            prompt.AppendLine("");
            prompt.AppendLine("RESPONSE STYLE:");
            prompt.AppendLine("- Be conversational and helpful, not formal or robotic");
            prompt.AppendLine("- Explain your suggestions and reasoning");
            prompt.AppendLine("- Offer specific improvements with examples when possible");
            prompt.AppendLine("- Ask questions to better understand their writing goals and preferences");
            prompt.AppendLine("");
            
            // Add revision format instructions like other chains
            prompt.AppendLine("=== REVISION FORMAT ===");
            prompt.AppendLine("When suggesting specific text changes to existing style guide content, you MUST use EXACTLY this format:");
            prompt.AppendLine("");
            prompt.AppendLine("For REVISIONS (replacing existing text):");
            prompt.AppendLine("```");
            prompt.AppendLine("Original text:");
            prompt.AppendLine("[paste the exact text to be replaced]");
            prompt.AppendLine("```");
            prompt.AppendLine("```");
            prompt.AppendLine("Changed to:");
            prompt.AppendLine("[your new version of the text]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("For INSERTIONS (adding new text after existing text):");
            prompt.AppendLine("```");
            prompt.AppendLine("Insert after:");
            prompt.AppendLine("[paste the exact anchor text to insert after]");
            prompt.AppendLine("```");
            prompt.AppendLine("```");
            prompt.AppendLine("New text:");
            prompt.AppendLine("[the new content to insert]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL TEXT PRECISION REQUIREMENTS:");
            prompt.AppendLine("- Original text and Insert after must be EXACT, COMPLETE, and VERBATIM from the style guide file");
            prompt.AppendLine("- Include ALL whitespace, line breaks, and formatting exactly as written");
            prompt.AppendLine("- Include complete sentences or natural text boundaries (periods, paragraph breaks)");
            prompt.AppendLine("- NEVER paraphrase, summarize, or reformat the original text");
            prompt.AppendLine("- COPY AND PASTE directly from the style guide file - do not retype or modify");
            prompt.AppendLine("- Include sufficient context (minimum 10-20 words) for unique identification");
            prompt.AppendLine("- If bullet points, include the bullet symbols and indentation exactly");
            prompt.AppendLine("- If headers, include the ## markdown symbols exactly");
            prompt.AppendLine("");
            prompt.AppendLine("TEXT MATCHING VALIDATION:");
            prompt.AppendLine("- Your original text MUST be findable with exact text search in the style guide file");
            prompt.AppendLine("- If you cannot copy exact text, provide surrounding context for identification");
            prompt.AppendLine("- Test your original text by mentally searching for it in the style guide file");
            prompt.AppendLine("- Incomplete or modified text will cause Apply buttons to fail");
            prompt.AppendLine("");
            prompt.AppendLine("ANCHOR TEXT GUIDELINES FOR INSERTIONS:");
            prompt.AppendLine("- Include COMPLETE sections, headers, or bullet points that end BEFORE where you want to insert");
            prompt.AppendLine("- NEVER use partial sentences or incomplete phrases as anchor text");
            prompt.AppendLine("- ALWAYS end anchor text at natural boundaries: section endings, headers, paragraph breaks");
            prompt.AppendLine("- Include enough context (at least 10-20 words) to ensure unique identification");
            prompt.AppendLine("");
            prompt.AppendLine("REVISION GUIDELINES:");
            prompt.AppendLine("- For SMALL CHANGES: Use the 'Original text/Changed to' format with minimal scope");
            prompt.AppendLine("- For TARGETED ADDITIONS: Use the 'Insert after/New text' format to add content at specific locations");
            prompt.AppendLine("- For REORGANIZATION: Modify only the specific sections that need restructuring");
            prompt.AppendLine("- AVOID rewriting entire style guides unless specifically requested");
            prompt.AppendLine("- PRESERVE formatting, structure, and content of unchanged sections");
            prompt.AppendLine("- For rule additions: Add only new rules, keep existing ones unchanged");
            prompt.AppendLine("- For section updates: Modify only affected sections, preserve others");
            prompt.AppendLine("- For header changes: Update specific section headers while maintaining content");
            prompt.AppendLine("");
            prompt.AppendLine("**REVISION FOCUS**: When providing style guide revisions, be concise:");
            prompt.AppendLine("- Provide the revision blocks with minimal explanation");
            prompt.AppendLine("- Only add commentary if the user specifically asks for reasoning or analysis");
            prompt.AppendLine("- Focus on the style changes themselves, not lengthy explanations");
            prompt.AppendLine("- If multiple revisions are needed, provide them cleanly in sequence");
            prompt.AppendLine("- Let the revised style guide content speak for itself");
            prompt.AppendLine("");
            prompt.AppendLine("If you are generating new style guide content (e.g., adding new sections, expanding rules), simply provide the new text directly without the 'Original text/Changed to' format.");
            prompt.AppendLine("");
            
            // CRITICAL FIX: Include current document content like other chains do
            if (!string.IsNullOrEmpty(_currentContext))
            {
                prompt.AppendLine("=== CURRENT STYLE GUIDE CONTENT ===");
                prompt.AppendLine("This is the style guide content you are helping to develop and format.");
                prompt.AppendLine("Work with this content to improve formatting, structure, and parser compatibility:");
                prompt.AppendLine("");
                // Strip frontmatter to avoid confusing the prompt
                string cleanedContent = StripFrontmatter(_currentContext);
                prompt.AppendLine(cleanedContent);
                prompt.AppendLine("");
            }
            
            // Add reference materials if available
            if (_references?.Count > 0)
            {
                prompt.AppendLine("=== REFERENCE MATERIALS ===");
                foreach (var reference in _references)
                {
                    var displayName = !string.IsNullOrEmpty(reference.Key) ? reference.Key : System.IO.Path.GetFileNameWithoutExtension(reference.Path);
                    prompt.AppendLine($"** {displayName} **");
                    prompt.AppendLine(reference.Content);
                    prompt.AppendLine("");
                }
            }
            
            prompt.AppendLine("GOAL: Work together to create a comprehensive, well-organized style guide that serves the user's specific writing needs while ensuring optimal parser compatibility.");
            
            return prompt.ToString();
        }

        public void SetCurrentFilePath(string filePath)
        {
            _currentFilePath = filePath;
        }

        /// <summary>
        /// Strips frontmatter from content to avoid muddying the prompt
        /// </summary>
        private string StripFrontmatter(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // Look for frontmatter delimiters
            if (content.StartsWith("---"))
            {
                var secondDelimiterIndex = content.IndexOf("\n---", 3);
                if (secondDelimiterIndex > 0)
                {
                    // Found closing delimiter, return content after it
                    return content.Substring(secondDelimiterIndex + 4).TrimStart('\n', '\r');
                }
            }

            return content;
        }

        private enum StyleGuideRequestType
        {
            General,
            Format,
            Voice,
            Dialogue,
            Description,
            Technical,
            WritingSample
        }
    }
} 