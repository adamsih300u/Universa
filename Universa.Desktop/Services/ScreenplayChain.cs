using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.Text.RegularExpressions;
using Universa.Desktop.Models;
using Universa.Desktop.Services;
using System.Text;

namespace Universa.Desktop.Services
{
    public class ScreenplayChain : BaseLangChainService
    {
        private string _screenplayContent;
        private string _styleGuide;
        private string _rules;
        private string _outline;
        private readonly FileReferenceService _fileReferenceService;
        private static ScreenplayChain _instance;
        private static readonly object _lock = new object();

        private ScreenplayChain(string apiKey, string model, AIProvider provider, string content) 
            : base(apiKey, model, provider)
        {
            _fileReferenceService = new FileReferenceService(Configuration.Instance.LibraryPath);
            UpdateContentAndInitialize(content).Wait();
        }

        public static ScreenplayChain GetInstance(string apiKey, string model, AIProvider provider, string content)
        {
            lock (_lock)
            {
                if (_instance == null)
                {
                    _instance = new ScreenplayChain(apiKey, model, provider, content);
                }
                else if (_instance._screenplayContent != content)
                {
                    // Update content if it has changed
                    _instance.UpdateContentAndInitialize(content).Wait();
                }
                return _instance;
            }
        }

        private async Task UpdateContentAndInitialize(string content)
        {
            await UpdateContent(content);
            InitializeSystemMessage();
        }

        private async Task UpdateContent(string content)
        {
            if (string.IsNullOrEmpty(content))
            {
                _screenplayContent = string.Empty;
                _styleGuide = string.Empty;
                _rules = string.Empty;
                _outline = string.Empty;
                return;
            }

            // Load any file references first
            var references = await _fileReferenceService.LoadReferencesAsync(content);
            
            var lines = content.Split('\n');
            var styleLines = new List<string>();
            var rulesLines = new List<string>();
            var outlineLines = new List<string>();
            var screenplayLines = new List<string>();
            
            bool inStyleSection = false;
            bool inRulesSection = false;
            bool inOutlineSection = false;
            bool inBodySection = false;
            bool inDefaultSection = false;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Skip reference lines as they've been processed
                if (trimmedLine.StartsWith("#ref "))
                    continue;
                
                // Check for section starts
                if (trimmedLine.Equals("#screenplay", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultSection = true;
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.Equals("#rules", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultSection = false;
                    inStyleSection = false;
                    inRulesSection = true;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.Equals("#outline", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultSection = false;
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = true;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.Equals("#style", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultSection = false;
                    inStyleSection = true;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = false;
                    continue;
                }
                else if (trimmedLine.Equals("#body", StringComparison.OrdinalIgnoreCase))
                {
                    inDefaultSection = false;
                    inStyleSection = false;
                    inRulesSection = false;
                    inOutlineSection = false;
                    inBodySection = true;
                    continue;
                }

                // Add line to appropriate section
                if (inStyleSection || (inDefaultSection && styleLines.Count == 0))
                    styleLines.Add(line);
                else if (inRulesSection)
                    rulesLines.Add(line);
                else if (inOutlineSection)
                    outlineLines.Add(line);
                else if (inBodySection || inDefaultSection)
                    screenplayLines.Add(line);
            }

            // Process references
            foreach (var reference in references)
            {
                switch (reference.Type.ToLowerInvariant())
                {
                    case "style":
                        styleLines.Add(reference.Content);
                        break;
                    case "rules":
                        rulesLines.Add(reference.Content);
                        break;
                    case "outline":
                        outlineLines.Add(reference.Content);
                        break;
                }
            }

            _styleGuide = string.Join("\n", styleLines).Trim();
            _rules = string.Join("\n", rulesLines).Trim();
            _outline = string.Join("\n", outlineLines).Trim();
            _screenplayContent = string.Join("\n", screenplayLines).Trim();

            // Debug the parsed sections
            System.Diagnostics.Debug.WriteLine("\n=== PARSED SECTIONS DEBUG ===", "ScreenplayChain");
            System.Diagnostics.Debug.WriteLine($"Rules length: {_rules.Length}", "ScreenplayChain");
            System.Diagnostics.Debug.WriteLine($"Rules content: {_rules}", "ScreenplayChain");
            System.Diagnostics.Debug.WriteLine($"Style Guide length: {_styleGuide.Length}", "ScreenplayChain");
            System.Diagnostics.Debug.WriteLine($"Outline length: {_outline.Length}", "ScreenplayChain");
            System.Diagnostics.Debug.WriteLine($"Screenplay Content length: {_screenplayContent.Length}", "ScreenplayChain");
            if (!string.IsNullOrEmpty(_screenplayContent))
                System.Diagnostics.Debug.WriteLine($"Screenplay Content preview: {_screenplayContent.Substring(0, Math.Min(100, _screenplayContent.Length))}", "ScreenplayChain");
        }

        private void InitializeSystemMessage()
        {
            var systemPrompt = BuildScreenplayPrompt("");
            var systemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            if (systemMessage != null)
            {
                systemMessage.Content = systemPrompt;
            }
            else
            {
                _memory.Insert(0, new MemoryMessage("system", systemPrompt, _model));
            }
        }

        private string BuildScreenplayPrompt(string request)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine(@"You are an AI assistant specialized in helping users write and edit screenplays. You will analyze and respond based on the sections provided below.

IMPORTANT: Only the #body section must follow strict screenplay formatting rules. Other sections (#rules, #outline, #style) can use regular text formatting.

SCREENPLAY FORMATTING RULES (APPLY ONLY TO #body SECTION):
1. Scene Headings (Sluglines):
   - Always in CAPS
   - Start with INT. or EXT.
   - Include location and time of day
   - Example: INT. OFFICE - DAY

2. Action/Description:
   - Present tense
   - Describe only what can be seen or heard
   - Keep paragraphs short (3-4 lines max)
   - No camera directions unless absolutely necessary

3. Character Names:
   - ALL CAPS when first introduced
   - ALL CAPS above dialogue
   - (V.O.) or (O.S.) for voice-over or off-screen
   - Parentheticals in (parentheses) below name

4. Dialogue:
   - Centered under character name
   - No quotation marks
   - Keep speeches brief (3-4 lines recommended)
   - Use parentheticals sparingly

5. Transitions:
   - Right-aligned
   - ALL CAPS
   - Common ones: CUT TO:, FADE OUT., DISSOLVE TO:

6. Page Formatting:
   - Courier 12pt font
   - 1.5-inch left margin
   - 1-inch right margin
   - 1-inch top and bottom margins
   - Dialogue block starts 2.5 inches from left

7. General Guidelines:
   - One page equals approximately one minute of screen time
   - Focus on visual storytelling
   - Be concise and specific
   - Avoid unfilmable elements (thoughts, feelings unless shown through action)
   - Use proper spacing between elements");

            // Add global rules if available
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== GLOBAL SCREENPLAY RULES ===");
                prompt.AppendLine("These are the project rules and can be written in any format:");
                prompt.AppendLine(_rules);
            }

            // Add story outline if available
            if (!string.IsNullOrEmpty(_outline))
            {
                prompt.AppendLine("\n=== SCREENPLAY OUTLINE ===");
                prompt.AppendLine("This is the planned screenplay structure and can be written in any format:");
                prompt.AppendLine(_outline);
            }

            // Add style guide if available
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine(@"
=== SCREENPLAY STYLE GUIDE ===
The text below demonstrates the desired screenplay format. You must NEVER:
- Use any characters from this sample
- Reference any locations or settings
- Borrow any plot elements or situations
- Copy any specific descriptions
- Use any unique phrases or metaphors
- Include any story elements or content

Instead, ONLY analyze and match these technical aspects:
1. SCREENPLAY FORMAT:
   - Scene headings (INT./EXT.)
   - Action descriptions
   - Character names
   - Dialogue formatting
   - Parentheticals
   - Transitions

2. LANGUAGE USE:
   - Present tense
   - Active voice
   - Visual descriptions
   - Concise action lines
   - Natural dialogue
   - Proper capitalization

3. STRUCTURAL ELEMENTS:
   - Scene transitions
   - Beat pacing
   - Page formatting
   - White space usage
   - Margin rules
   - Standard abbreviations

--- STYLE REFERENCE TEXT ---
" + _styleGuide);
            }

            // Add current content
            if (!string.IsNullOrEmpty(_screenplayContent))
            {
                prompt.AppendLine("\n=== CURRENT SCREENPLAY CONTENT ===");
                prompt.AppendLine("This is the current screenplay text in the #body section. It MUST follow standard screenplay format:");
                prompt.AppendLine(_screenplayContent);
            }

            // Add response format instructions
            prompt.AppendLine(@"
=== RESPONSE FORMAT ===
When suggesting specific text changes, you MUST use EXACTLY this format:

```
Original code:
[paste the exact text to be replaced]
```

```
Changed to:
[your new version of the text]
```

CRITICAL INSTRUCTIONS:
1. If the user asks about a specific section:
   - For #rules, #outline, or #style sections: Respond in regular text format
   - For #body section: Use proper screenplay format and follow all formatting rules
2. When suggesting changes:
   - Use the exact labels 'Original code:' and 'Changed to:'
   - Include the triple backticks exactly as shown
   - The original text must be an exact match of what appears in the content
   - Do not include any other text between or inside the code blocks
3. When providing guidance:
   - For #body section: Be specific about screenplay formatting rules
   - For other sections: Focus on content and clarity, not formatting
4. Always:
   - Follow standard screenplay format ONLY in the #body section
   - Keep other sections in regular text format
   - Follow all global screenplay rules
   - Stay consistent with the story outline");

            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                // Only process the request if one was provided
                if (!string.IsNullOrEmpty(request))
                {
                    // Update content if needed
                    if (!string.IsNullOrEmpty(content) && content != _screenplayContent)
                    {
                        await UpdateContentAndInitialize(content);
                    }

                    // Add the user request to memory
                    AddUserMessage(request);
                    
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
                System.Diagnostics.Debug.WriteLine($"\n=== SCREENPLAY CHAIN ERROR ===\n{ex}");
                throw;
            }
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