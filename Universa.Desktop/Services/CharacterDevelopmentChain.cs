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
    /// Character Development Chain - AI assistant for character creation and development
    /// Activated when document type is "characters"
    /// </summary>
    public class CharacterDevelopmentChain : BaseLangChainService
    {
        private readonly FileReferenceService _fileReferenceService;
        private readonly CharacterStoryAnalysisService _storyAnalysisService;
        private string _currentFilePath;
        private List<FileReference> _references;
        private string _rules; // Series rules for universe consistency
        private Dictionary<string, string> _frontmatter; // Frontmatter for reference loading
        
        // Enhanced rules parsing like FictionWritingBeta
        private readonly RulesParser _rulesParser = new RulesParser();
        private RulesParser.ParsedRules _parsedRules;

        public CharacterDevelopmentChain(string apiKey, string model, Models.AIProvider provider, FileReferenceService fileReferenceService, CharacterStoryAnalysisService storyAnalysisService = null) 
            : base(apiKey, model, provider)
        {
            _fileReferenceService = fileReferenceService;
            _storyAnalysisService = storyAnalysisService;
            _references = new List<FileReference>();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            return await ProcessCharacterDevelopmentAsync(content, request, _currentFilePath);
        }

        public override async Task<string> ProcessRequest(string content, string request, CancellationToken cancellationToken)
        {
            return await ProcessCharacterDevelopmentAsync(content, request, _currentFilePath);
        }

        public override async Task UpdateContextAsync(string context)
        {
            _currentContext = context;
            
            // Load references from the updated context
            if (_fileReferenceService != null)
            {
                _references = await _fileReferenceService.LoadReferencesAsync(context);
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Loaded {_references.Count} references from context");
            }
        }

        protected async Task<string> SendRequestAsync(string systemPrompt, string userRequest)
        {
            // Only clear memory and reinitialize if system prompt has changed significantly
            // This preserves conversation context for iterative character development
            var existingSystemMessage = _memory.FirstOrDefault(m => m.Role.Equals("system", StringComparison.OrdinalIgnoreCase));
            
            if (existingSystemMessage == null || existingSystemMessage.Content != systemPrompt)
            {
                // System prompt needs updating - clear and reinitialize
                ClearMemory();
                AddSystemMessage(systemPrompt);
                System.Diagnostics.Debug.WriteLine("CharacterDevelopmentChain: Refreshed system message with updated content");
            }
            
            // Add user request to conversation history
            AddUserMessage(userRequest);
            
            // Execute the prompt
            var response = await ExecutePrompt(userRequest);
            
            // Add assistant response to conversation history
            AddAssistantMessage(response);
            
            return response;
        }

        public async Task<string> ProcessCharacterDevelopmentAsync(string content, string prompt, string currentFilePath = null)
        {
            try
            {
                _currentFilePath = currentFilePath;
                
                // BULLY FIX: Set the current context so it appears in system prompt
                _currentContext = content;
                
                // Update file reference service with current path
                if (!string.IsNullOrEmpty(currentFilePath))
                {
                    _fileReferenceService.SetCurrentFilePath(currentFilePath);
                }

                // BULLY NEW: Parse frontmatter for rules and other references like FictionWritingBeta
                await ParseFrontmatterAndLoadReferences(content);

                // Load references from content
                _references = await _fileReferenceService.LoadReferencesAsync(content);
                
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Current context length: {_currentContext?.Length ?? 0}");
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Story analysis request? {IsStoryAnalysisRequest(prompt)}");

                // BULLY NEW: Check if this is a story-analysis request
                if (IsStoryAnalysisRequest(prompt) && _storyAnalysisService != null)
                {
                    return await ProcessStoryAnalysisRequest(content, prompt);
                }

                // Determine character file type from content and references
                var characterFileType = DetermineCharacterFileType(content);
                
                var systemPrompt = BuildCharacterDevelopmentPrompt(characterFileType, prompt);
                
                return await SendRequestAsync(systemPrompt, prompt);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in character development chain: {ex.Message}");
                return $"Error processing character development request: {ex.Message}";
            }
        }

        /// <summary>
        /// Parse frontmatter and load references using cascade system like FictionWritingBeta
        /// </summary>
        private async Task ParseFrontmatterAndLoadReferences(string content)
        {
            try
            {
                // Parse frontmatter from content
                _frontmatter = ParseFrontmatter(content);
                
                if (_frontmatter != null && _frontmatter.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Found frontmatter with {_frontmatter.Count} keys");
                    
                    // NEW: Use cascade loading to get all references including those from outline
                    // Enable rules character cascade for character development to track relationships
                    var allReferences = await _fileReferenceService.LoadReferencesWithCascadeAsync(content, enableCascade: true, enableRulesCharacterCascade: true);
                    
                    // Process all loaded references
                    foreach (var reference in allReferences)
                    {
                        switch (reference.Type)
                        {
                            case FileReferenceType.Rules:
                                _rules = reference.Content;
                                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Loaded rules via cascade: {reference.Content?.Length ?? 0} chars");
                                
                                // Parse the rules for enhanced understanding
                                try
                                {
                                    _parsedRules = _rulesParser.Parse(reference.Content);
                                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Successfully parsed cascaded rules: {_parsedRules.Characters.Count} characters");
                                }
                                catch (Exception parseEx)
                                {
                                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Error parsing cascaded rules: {parseEx.Message}");
                                }
                                break;
                                
                            case FileReferenceType.Character:
                                // Add to references list for character development context
                                _references.Add(reference);
                                var characterName = reference.GetCharacterName() ?? "Unknown Character";
                                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Loaded character via cascade: {characterName} ({reference.Content?.Length ?? 0} chars)");
                                break;
                                
                            case FileReferenceType.Style:
                            case FileReferenceType.Outline:
                                // Add to references list for additional context
                                _references.Add(reference);
                                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Loaded {reference.Type} via cascade: {reference.Content?.Length ?? 0} chars");
                                break;
                        }
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Cascade processing complete - loaded {_references.Count} total references");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("CharacterDevelopmentChain: No frontmatter found or no references to process");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Error parsing frontmatter: {ex.Message}");
            }
        }



        /// <summary>
        /// Parse frontmatter from content like FictionWritingBeta
        /// </summary>
        private Dictionary<string, string> ParseFrontmatter(string content)
        {
            var frontmatter = new Dictionary<string, string>();
            
            if (string.IsNullOrEmpty(content)) return frontmatter;
            
            var lines = content.Split('\n');
            if (lines.Length == 0 || !lines[0].Trim().Equals("---")) return frontmatter;
            
            bool inFrontmatter = false;
            int frontmatterEnd = -1;
            
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
                    frontmatterEnd = i;
                    break;
                }
                
                if (inFrontmatter && !string.IsNullOrEmpty(line) && line.Contains(":"))
                {
                    var colonIndex = line.IndexOf(':');
                    var key = line.Substring(0, colonIndex).Trim();
                    var value = line.Substring(colonIndex + 1).Trim();
                    
                    if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    {
                        frontmatter[key] = value;
                    }
                }
            }
            
            return frontmatter;
        }

        private CharacterFileType DetermineCharacterFileType(string content)
        {
            // Check filename patterns
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var fileName = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath).ToLowerInvariant();
                
                if (fileName.Contains("supporting") || fileName.Contains("minor") || fileName.Contains("secondary"))
                    return CharacterFileType.SupportingCast;
                
                if (fileName.Contains("relationship") || fileName.Contains("dynamics"))
                    return CharacterFileType.Relationships;
            }

            // Check content patterns
            var lowerContent = content.ToLowerInvariant();
            
            // Look for supporting cast indicators
            if (lowerContent.Contains("## supporting characters") || 
                lowerContent.Contains("## minor characters") ||
                lowerContent.Contains("## recurring characters"))
                return CharacterFileType.SupportingCast;

            // Look for relationship indicators
            if (lowerContent.Contains("## relationships") ||
                lowerContent.Contains("## character dynamics") ||
                lowerContent.Contains("relationship:"))
                return CharacterFileType.Relationships;

            // Default to major character if focused on single character
            return CharacterFileType.MajorCharacter;
        }

        private string BuildCharacterDevelopmentPrompt(CharacterFileType fileType, string request = "")
        {
            var systemPrompt = new StringBuilder();
            
            systemPrompt.AppendLine("You are a Character Development Assistant specializing in creating compelling, consistent characters for fiction writing. You excel at both creating new character content and revising existing character profiles.");
            systemPrompt.AppendLine();
            
            switch (fileType)
            {
                case CharacterFileType.MajorCharacter:
                    systemPrompt.AppendLine("FOCUS: Major Character Development");
                    systemPrompt.AppendLine("You help create deep, complex main characters with:");
                    systemPrompt.AppendLine("- Complete backstories and motivations");
                    systemPrompt.AppendLine("- Distinctive dialogue patterns and voice");
                    systemPrompt.AppendLine("- Internal monologue styles");
                    systemPrompt.AppendLine("- Character growth arcs across the series");
                    systemPrompt.AppendLine("- Detailed personality traits, flaws, and strengths");
                    systemPrompt.AppendLine("- Relationship dynamics with other major characters");
                    break;
                    
                case CharacterFileType.SupportingCast:
                    systemPrompt.AppendLine("FOCUS: Supporting Cast Management");
                    systemPrompt.AppendLine("You help create and manage minor/supporting characters with:");
                    systemPrompt.AppendLine("- Concise but memorable character sketches");
                    systemPrompt.AppendLine("- Key personality traits and quirks");
                    systemPrompt.AppendLine("- Basic dialogue style notes");
                    systemPrompt.AppendLine("- Roles in relation to major characters");
                    systemPrompt.AppendLine("- Potential for future development");
                    systemPrompt.AppendLine("- Consistency across multiple appearances");
                    break;
                    
                case CharacterFileType.Relationships:
                    systemPrompt.AppendLine("FOCUS: Character Relationship Dynamics");
                    systemPrompt.AppendLine("You help develop and track character relationships with:");
                    systemPrompt.AppendLine("- Relationship dynamics and tensions");
                    systemPrompt.AppendLine("- Interaction patterns and dialogue styles");
                    systemPrompt.AppendLine("- Conflict sources and resolutions");
                    systemPrompt.AppendLine("- Relationship evolution throughout the series");
                    systemPrompt.AppendLine("- Power dynamics and hierarchies");
                    break;
            }
            
            systemPrompt.AppendLine();
            
            // BULLY NEW: Add request type detection and revision instructions like OutlineWritingBeta
            if (!string.IsNullOrEmpty(request))
            {
                // Detect granular revision requests
                var granularKeywords = new[]
                {
                    "add", "modify", "change", "update", "adjust", "fix", "correct", "revise",
                    "expand this", "enhance", "improve", "refine", "tweak", "small change",
                    "minor adjustment", "just need", "only change", "specific", "particular"
                };
                
                var developmentKeywords = new[]
                {
                    "create character", "develop character", "new character", "full character",
                    "complete character", "character profile", "character sheet", "build character"
                };
                
                bool isGranularRequest = granularKeywords.Any(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                bool isDevelopmentRequest = developmentKeywords.Any(keyword => 
                    request.Contains(keyword, StringComparison.OrdinalIgnoreCase));
                
                if (isDevelopmentRequest)
                {
                    systemPrompt.AppendLine("=== CHARACTER DEVELOPMENT REQUEST ===");
                    systemPrompt.AppendLine("The user is requesting comprehensive character development. Focus on:");
                    systemPrompt.AppendLine("- Complete character background and psychology");
                    systemPrompt.AppendLine("- Distinctive personality traits and flaws");
                    systemPrompt.AppendLine("- Character voice and dialogue patterns");
                    systemPrompt.AppendLine("- Relationship dynamics with other characters");
                    systemPrompt.AppendLine("- Character arc and growth potential");
                    systemPrompt.AppendLine("- Proper formatting for character sheets");
                    systemPrompt.AppendLine("");
                }
                else if (isGranularRequest)
                {
                    systemPrompt.AppendLine("=== GRANULAR REVISION REQUEST ===");
                    systemPrompt.AppendLine("The user is requesting a specific, targeted change. Focus on:");
                    systemPrompt.AppendLine("- Making ONLY the requested modifications");
                    systemPrompt.AppendLine("- Preserving all existing character content that works well");
                    systemPrompt.AppendLine("- Using surgical precision rather than broad rewrites");
                    systemPrompt.AppendLine("- Maintaining established character traits and relationships");
                    systemPrompt.AppendLine("- Integrating changes smoothly with existing character profile");
                    systemPrompt.AppendLine("");
                }
            }
            
            systemPrompt.AppendLine("CHARACTER DEVELOPMENT GUIDELINES:");
            systemPrompt.AppendLine("- Create realistic, flawed characters with clear motivations");
            systemPrompt.AppendLine("- Ensure distinctive voices and speech patterns");
            systemPrompt.AppendLine("- Consider character consistency across long series");
            systemPrompt.AppendLine("- Balance character strengths with meaningful weaknesses");
            systemPrompt.AppendLine("- Think about how characters change and grow");
            systemPrompt.AppendLine("- Consider how characters relate to and influence each other");
            
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("PREFERRED CHARACTER SHEET FORMATTING:");
            
            switch (fileType)
            {
                case CharacterFileType.MajorCharacter:
                    systemPrompt.AppendLine("For Major Characters, use this structure:");
                    systemPrompt.AppendLine("```");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("title: [Character Name]");
                    systemPrompt.AppendLine("type: character");
                    systemPrompt.AppendLine("style: \"path/to/style-guide.md\"");
                    systemPrompt.AppendLine("rules: \"path/to/writing-rules.md\"");
                    systemPrompt.AppendLine("ref_story_book1: \"path/to/first-book.md\"");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Basic Information");
                    systemPrompt.AppendLine("- **Full Name**: [Name]");
                    systemPrompt.AppendLine("- **Age**: [Age]");
                    systemPrompt.AppendLine("- **Occupation**: [Job/Role]");
                    systemPrompt.AppendLine("- **Physical Description**: [Brief physical traits]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Personality");
                    systemPrompt.AppendLine("### Core Traits");
                    systemPrompt.AppendLine("- [Primary personality traits]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Strengths");
                    systemPrompt.AppendLine("- [Character strengths]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Flaws/Weaknesses");
                    systemPrompt.AppendLine("- [Character flaws and weaknesses]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Dialogue Patterns");
                    systemPrompt.AppendLine("### Speech Style");
                    systemPrompt.AppendLine("- [How they speak, vocabulary level, formality]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Common Phrases");
                    systemPrompt.AppendLine("- \"[Catchphrases or recurring expressions]\"");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Dialogue Tags");
                    systemPrompt.AppendLine("- [Preferred dialogue attribution: said, murmured, etc.]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Internal Monologue");
                    systemPrompt.AppendLine("### Thought Patterns");
                    systemPrompt.AppendLine("- [How they think, internal voice style]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Internal Conflicts");
                    systemPrompt.AppendLine("- [Key internal struggles and dilemmas]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Relationships");
                    systemPrompt.AppendLine("### [Character Name]");
                    systemPrompt.AppendLine("- **Relationship**: [Type of relationship]");
                    systemPrompt.AppendLine("- **Dynamics**: [How they interact]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## Character Arc");
                    systemPrompt.AppendLine("### Starting Point");
                    systemPrompt.AppendLine("- [Where character begins in the series]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Growth/Change");
                    systemPrompt.AppendLine("- [How they develop over time]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Key Moments");
                    systemPrompt.AppendLine("- [Important character development scenes]");
                    systemPrompt.AppendLine("```");
                    break;
                    
                case CharacterFileType.SupportingCast:
                    systemPrompt.AppendLine("For Supporting Cast, use this structure:");
                    systemPrompt.AppendLine("```");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("title: Supporting Characters");
                    systemPrompt.AppendLine("type: character");
                    systemPrompt.AppendLine("style: \"path/to/style-guide.md\"");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## [Character Name 1]");
                    systemPrompt.AppendLine("- **Role**: [Function in story]");
                    systemPrompt.AppendLine("- **Traits**: [Key personality traits]");
                    systemPrompt.AppendLine("- **Speech**: [Brief dialogue style notes]");
                    systemPrompt.AppendLine("- **Relationship to MC**: [Connection to main characters]");
                    systemPrompt.AppendLine("- **Notes**: [Additional development notes]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## [Character Name 2]");
                    systemPrompt.AppendLine("[Same format as above]");
                    systemPrompt.AppendLine("```");
                    break;
                    
                case CharacterFileType.Relationships:
                    systemPrompt.AppendLine("For Character Relationships, use this structure:");
                    systemPrompt.AppendLine("```");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("title: Character Relationships");
                    systemPrompt.AppendLine("type: character");
                    systemPrompt.AppendLine("ref_character_main: \"path/to/main-character.md\"");
                    systemPrompt.AppendLine("---");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("## [Character A] & [Character B]");
                    systemPrompt.AppendLine("### Relationship Type");
                    systemPrompt.AppendLine("- [Romantic, friendship, rivalry, family, etc.]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Dynamics");
                    systemPrompt.AppendLine("- [How they interact, power balance]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Conflict Sources");
                    systemPrompt.AppendLine("- [What creates tension between them]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Interaction Patterns");
                    systemPrompt.AppendLine("- [Typical dialogue/behavior when together]");
                    systemPrompt.AppendLine("");
                    systemPrompt.AppendLine("### Evolution");
                    systemPrompt.AppendLine("- [How relationship changes over time]");
                    systemPrompt.AppendLine("```");
                    break;
            }
            
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("FORMATTING GUIDELINES:");
            systemPrompt.AppendLine("- Always use YAML frontmatter with proper fields");
            systemPrompt.AppendLine("- Use markdown headers (##) for major sections");
            systemPrompt.AppendLine("- Use bullet points (-) for lists");
            systemPrompt.AppendLine("- Use **bold** for field labels");
            systemPrompt.AppendLine("- Include reference files in frontmatter when available");
            systemPrompt.AppendLine("- Keep supporting character entries concise but distinctive");
            systemPrompt.AppendLine("- For major characters, include all sections even if brief");
            
            // BULLY NEW: Add revision format instructions like OutlineWritingBeta
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("=== REVISION FORMAT ===");
            systemPrompt.AppendLine("When suggesting specific text changes to existing character content, you MUST use EXACTLY this format:");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("For REVISIONS (replacing existing text):");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("Original text:");
            systemPrompt.AppendLine("[paste the exact text to be replaced]");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("Changed to:");
            systemPrompt.AppendLine("[your new version of the text]");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("For INSERTIONS (adding new text after existing text):");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("Insert after:");
            systemPrompt.AppendLine("[paste the exact anchor text to insert after]");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("New text:");
            systemPrompt.AppendLine("[the new content to insert]");
            systemPrompt.AppendLine("```");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("CRITICAL TEXT PRECISION REQUIREMENTS:");
            systemPrompt.AppendLine("- Original text and Insert after must be EXACT, COMPLETE, and VERBATIM from the character file");
            systemPrompt.AppendLine("- Include ALL whitespace, line breaks, and formatting exactly as written");
            systemPrompt.AppendLine("- Include complete sentences or natural text boundaries (periods, paragraph breaks)");
            systemPrompt.AppendLine("- NEVER paraphrase, summarize, or reformat the original text");
            systemPrompt.AppendLine("- COPY AND PASTE directly from the character file - do not retype or modify");
            systemPrompt.AppendLine("- Include sufficient context (minimum 10-20 words) for unique identification");
            systemPrompt.AppendLine("- If bullet points, include the bullet symbols and indentation exactly");
            systemPrompt.AppendLine("- If headers, include the ## markdown symbols exactly");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("TEXT MATCHING VALIDATION:");
            systemPrompt.AppendLine("- Your original text MUST be findable with exact text search in the character file");
            systemPrompt.AppendLine("- If you cannot copy exact text, provide surrounding context for identification");
            systemPrompt.AppendLine("- Test your original text by mentally searching for it in the character file");
            systemPrompt.AppendLine("- Incomplete or modified text will cause Apply buttons to fail");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("ANCHOR TEXT GUIDELINES FOR INSERTIONS:");
            systemPrompt.AppendLine("- Include COMPLETE sections, headers, or bullet points that end BEFORE where you want to insert");
            systemPrompt.AppendLine("- NEVER use partial sentences or incomplete phrases as anchor text");
            systemPrompt.AppendLine("- ALWAYS end anchor text at natural boundaries: section endings, headers, paragraph breaks");
            systemPrompt.AppendLine("- Include enough context (at least 10-20 words) to ensure unique identification");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("GRANULAR REVISION GUIDELINES:");
            systemPrompt.AppendLine("- For SMALL CHANGES: Use the 'Original text/Changed to' format with minimal scope");
            systemPrompt.AppendLine("- For TARGETED ADDITIONS: Use the 'Insert after/New text' format to add content at specific locations");
            systemPrompt.AppendLine("- For ENHANCEMENTS: Modify only the specific sections that need updating");
            systemPrompt.AppendLine("- AVOID rewriting entire character profiles unless specifically requested");
            systemPrompt.AppendLine("- PRESERVE formatting, structure, and content of unchanged sections");
            systemPrompt.AppendLine("- For character trait additions: Add only new traits, keep existing ones unchanged");
            systemPrompt.AppendLine("- For relationship updates: Modify only affected relationships, preserve others");
            systemPrompt.AppendLine("- For dialogue pattern changes: Update specific speech elements, maintain voice consistency");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("**REVISION FOCUS**: When providing character revisions, be concise:");
            systemPrompt.AppendLine("- Provide the revision blocks with minimal explanation");
            systemPrompt.AppendLine("- Only add commentary if the user specifically asks for reasoning or analysis");
            systemPrompt.AppendLine("- Focus on the character changes themselves, not lengthy explanations");
            systemPrompt.AppendLine("- If multiple revisions are needed, provide them cleanly in sequence");
            systemPrompt.AppendLine("- Let the revised character content speak for itself");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("If you are generating new character content (e.g., adding new characters, expanding profiles), simply provide the new text directly without the 'Original text/Changed to' format.");
            
            // BULLY FIX: Add the current character file content with stronger emphasis on revision capabilities
            if (!string.IsNullOrEmpty(_currentContext))
            {
                systemPrompt.AppendLine();
                systemPrompt.AppendLine("=== CURRENT CHARACTER FILE CONTENT ===");
                systemPrompt.AppendLine("This is the character file you are helping to develop. Your suggestions should build upon and improve this content:");
                systemPrompt.AppendLine("- For revisions: Use the 'Original text/Changed to' format shown above");
                systemPrompt.AppendLine("- For additions: Provide new content that integrates smoothly");
                systemPrompt.AppendLine("- For enhancements: Focus on deepening existing character elements");
                systemPrompt.AppendLine("");
                // Strip frontmatter from character content to avoid muddying the prompt
                string cleanedContent = StripFrontmatter(_currentContext);
                systemPrompt.AppendLine(cleanedContent);
            }
            
            // Add reference materials if available
            if (_references?.Count > 0)
            {
                systemPrompt.AppendLine();
                systemPrompt.AppendLine("REFERENCE MATERIALS AVAILABLE:");
                
                foreach (var reference in _references)
                {
                    switch (reference.Type)
                    {
                        case FileReferenceType.Style:
                            systemPrompt.AppendLine();
                            systemPrompt.AppendLine("WRITING STYLE GUIDE:");
                            systemPrompt.AppendLine(reference.Content);
                            break;
                            
                        case FileReferenceType.Rules:
                            systemPrompt.AppendLine();
                            systemPrompt.AppendLine("WRITING RULES:");
                            systemPrompt.AppendLine(reference.Content);
                            break;
                            
                        case FileReferenceType.Outline:
                            systemPrompt.AppendLine();
                            systemPrompt.AppendLine("STORY OUTLINE:");
                            systemPrompt.AppendLine(reference.Content);
                            break;
                            
                        case FileReferenceType.Character:
                            var charName = reference.GetCharacterName();
                            systemPrompt.AppendLine();
                            systemPrompt.AppendLine($"EXISTING CHARACTER - {charName?.ToUpperInvariant() ?? "UNKNOWN"}:");
                            systemPrompt.AppendLine(reference.Content);
                            break;
                            
                        case FileReferenceType.Relationship:
                            var relName = reference.GetRelationshipName();
                            systemPrompt.AppendLine();
                            systemPrompt.AppendLine($"RELATIONSHIP DYNAMICS - {relName?.ToUpperInvariant() ?? "UNKNOWN"}:");
                            systemPrompt.AppendLine(reference.Content);
                            break;
                    }
                }
            }
            
            // BULLY NEW: Add direct rules integration like FictionWritingBeta
            if (!string.IsNullOrEmpty(_rules))
            {
                systemPrompt.AppendLine();
                systemPrompt.AppendLine("=== SERIES RULES & UNIVERSE CONSISTENCY ===");
                systemPrompt.AppendLine("These are the established universe rules that ALL character development must follow:");
                systemPrompt.AppendLine("- Characters must fit within established magic systems, technology levels, and cultural norms");
                systemPrompt.AppendLine("- Character relationships must respect existing character network and hierarchies");
                systemPrompt.AppendLine("- Character backstories must align with established timeline and world events");
                systemPrompt.AppendLine("- Character names, organizations, and affiliations must be consistent with universe conventions");
                systemPrompt.AppendLine("- Character abilities and skills must respect established power levels and limitations");
                systemPrompt.AppendLine();
                
                // Enhanced rules processing if parsed
                if (_parsedRules != null)
                {
                    if (_parsedRules.Characters.Any())
                    {
                        systemPrompt.AppendLine("EXISTING CHARACTERS IN UNIVERSE:");
                        foreach (var character in _parsedRules.Characters.Take(10)) // Limit to prevent prompt overflow
                        {
                            var description = !string.IsNullOrEmpty(character.Value.Background) ? character.Value.Background : "No description available";
                            systemPrompt.AppendLine($"- {character.Key}: {description}");
                        }
                        systemPrompt.AppendLine();
                    }
                    
                    if (_parsedRules.Organizations.Any())
                    {
                        systemPrompt.AppendLine("ORGANIZATIONS:");
                        foreach (var org in _parsedRules.Organizations.Take(8))
                        {
                            var description = !string.IsNullOrEmpty(org.Value.Purpose) ? org.Value.Purpose : 
                                             !string.IsNullOrEmpty(org.Value.Type) ? $"{org.Value.Type} organization" : "No description available";
                            systemPrompt.AppendLine($"- {org.Key}: {description}");
                        }
                        systemPrompt.AppendLine();
                    }
                    
                    if (_parsedRules.Locations.Any())
                    {
                        systemPrompt.AppendLine("UNIVERSE LOCATIONS:");
                        foreach (var location in _parsedRules.Locations.Take(8))
                        {
                            var description = !string.IsNullOrEmpty(location.Value.Description) ? location.Value.Description : 
                                             !string.IsNullOrEmpty(location.Value.Significance) ? location.Value.Significance : "No description available";
                            systemPrompt.AppendLine($"- {location.Key}: {description}");
                        }
                        systemPrompt.AppendLine();
                    }
                    
                    if (_parsedRules.CriticalFacts.Any())
                    {
                        systemPrompt.AppendLine("CRITICAL UNIVERSE FACTS:");
                        foreach (var fact in _parsedRules.CriticalFacts.Take(10))
                        {
                            systemPrompt.AppendLine($"- {fact}");
                        }
                        systemPrompt.AppendLine();
                    }
                }
                else
                {
                    // Fallback to raw rules if parsing failed
                    systemPrompt.AppendLine("SERIES RULES (Raw):");
                    systemPrompt.AppendLine(_rules);
                    systemPrompt.AppendLine();
                }
                
                systemPrompt.AppendLine("CHARACTER UNIVERSE COMPLIANCE:");
                systemPrompt.AppendLine("- VALIDATE character abilities against established power systems");
                systemPrompt.AppendLine("- ENSURE character backgrounds fit universe timeline and events");
                systemPrompt.AppendLine("- CHECK character relationships don't conflict with existing network");
                systemPrompt.AppendLine("- VERIFY character organizations and affiliations are universe-appropriate");
                systemPrompt.AppendLine("- MAINTAIN naming conventions and cultural consistency");
                systemPrompt.AppendLine("- RESPECT established character hierarchies and power dynamics");
                systemPrompt.AppendLine();
            }
            
            systemPrompt.AppendLine();
            systemPrompt.AppendLine("CRITICAL INSTRUCTIONS:");
            systemPrompt.AppendLine("1. When developing characters, focus on:");
            systemPrompt.AppendLine("   - Rich psychological depth and complexity");
            systemPrompt.AppendLine("   - Distinctive voice and speech patterns");
            systemPrompt.AppendLine("   - Clear character motivations and goals");
            systemPrompt.AppendLine("   - Meaningful relationships with other characters");
            systemPrompt.AppendLine("   - Character growth potential and arc development");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("2. For character revisions and processing:");
            systemPrompt.AppendLine("   - PREFER TARGETED CHANGES: When a user requests specific modifications, make only the changes needed");
            systemPrompt.AppendLine("   - PRESERVE EXISTING CONTENT: Keep all good character content that doesn't conflict with the request");
            systemPrompt.AppendLine("   - INCREMENTAL IMPROVEMENTS: Add or modify individual traits, relationships, or details rather than rewriting entire profiles");
            systemPrompt.AppendLine("   - SURGICAL EDITS: Replace only the specific sections that need updating");
            systemPrompt.AppendLine("   - CONTEXTUAL ADDITIONS: When adding new elements, integrate them smoothly with existing character content");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("3. Content depth requirements:");
            systemPrompt.AppendLine("   - Provide enough detail for writers to maintain character consistency");
            systemPrompt.AppendLine("   - Include specific examples of dialogue patterns and speech quirks");
            systemPrompt.AppendLine("   - Detail internal thought processes and emotional responses");
            systemPrompt.AppendLine("   - Specify relationship dynamics and interaction patterns");
            systemPrompt.AppendLine("   - Note character development opportunities and growth moments");
            systemPrompt.AppendLine("");
            systemPrompt.AppendLine("Provide helpful, creative guidance for character development while maintaining consistency with established series elements. Excel at both creating new character content and precisely revising existing character profiles.");
            
            return systemPrompt.ToString();
        }

        /// <summary>
        /// Determines if the prompt is requesting story analysis
        /// </summary>
        private bool IsStoryAnalysisRequest(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();
            
            // BULLY ENHANCEMENT: Expanded detection patterns for story analysis requests
            return 
                // Original patterns
                lowerPrompt.Contains("analyze") && lowerPrompt.Contains("story") ||
                lowerPrompt.Contains("analyze") && lowerPrompt.Contains("book") ||
                lowerPrompt.Contains("analyze") && lowerPrompt.Contains("chapter") ||
                lowerPrompt.Contains("consistency") && lowerPrompt.Contains("story") ||
                lowerPrompt.Contains("character development") && lowerPrompt.Contains("story") ||
                lowerPrompt.Contains("cross-story") ||
                lowerPrompt.Contains("across stories") ||
                lowerPrompt.Contains("in the book") ||
                lowerPrompt.Contains("how does") && lowerPrompt.Contains("appear in") ||
                
                // NEW: Natural language patterns for story review/analysis
                lowerPrompt.Contains("look over") && lowerPrompt.Contains("stories") ||
                lowerPrompt.Contains("review") && lowerPrompt.Contains("stories") ||
                lowerPrompt.Contains("check") && lowerPrompt.Contains("stories") ||
                lowerPrompt.Contains("examine") && lowerPrompt.Contains("stories") ||
                lowerPrompt.Contains("go through") && lowerPrompt.Contains("stories") ||
                
                // NEW: Activity/behavior analysis patterns
                lowerPrompt.Contains("activity") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("books")) ||
                lowerPrompt.Contains("behavior") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("books")) ||
                lowerPrompt.Contains("actions") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("books")) ||
                
                // NEW: Matching/comparison patterns
                lowerPrompt.Contains("matches") && lowerPrompt.Contains("profile") ||
                lowerPrompt.Contains("compare") && lowerPrompt.Contains("profile") ||
                lowerPrompt.Contains("consistent with") && lowerPrompt.Contains("profile") ||
                lowerPrompt.Contains("fits") && lowerPrompt.Contains("profile") ||
                
                // NEW: Reference-based patterns
                lowerPrompt.Contains("referenced") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("books")) ||
                lowerPrompt.Contains("story references") ||
                lowerPrompt.Contains("book references") ||
                
                // NEW: Verification patterns
                lowerPrompt.Contains("verify") && (lowerPrompt.Contains("character") || lowerPrompt.Contains("profile")) ||
                lowerPrompt.Contains("validate") && (lowerPrompt.Contains("character") || lowerPrompt.Contains("profile")) ||
                lowerPrompt.Contains("confirm") && (lowerPrompt.Contains("character") || lowerPrompt.Contains("profile")) ||
                
                // NEW: Revision-focused patterns
                lowerPrompt.Contains("revise") && (lowerPrompt.Contains("profile") || lowerPrompt.Contains("stories") || lowerPrompt.Contains("based on")) ||
                lowerPrompt.Contains("update") && lowerPrompt.Contains("profile") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("based on")) ||
                lowerPrompt.Contains("improve") && lowerPrompt.Contains("profile") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("using")) ||
                lowerPrompt.Contains("suggest") && (lowerPrompt.Contains("revisions") || lowerPrompt.Contains("changes")) ||
                lowerPrompt.Contains("modify") && lowerPrompt.Contains("profile") && lowerPrompt.Contains("based on") ||
                lowerPrompt.Contains("enhance") && lowerPrompt.Contains("profile") && (lowerPrompt.Contains("stories") || lowerPrompt.Contains("using")) ||
                lowerPrompt.Contains("corrections") && (lowerPrompt.Contains("profile") || lowerPrompt.Contains("character")) ||
                lowerPrompt.Contains("adjustments") && (lowerPrompt.Contains("profile") || lowerPrompt.Contains("character"));
        }

        /// <summary>
        /// Processes story analysis requests using the CharacterStoryAnalysisService
        /// </summary>
        private async Task<string> ProcessStoryAnalysisRequest(string content, string prompt)
        {
            try
            {
                // Extract character name from content or prompt
                var characterName = ExtractCharacterName(content, prompt);
                if (string.IsNullOrEmpty(characterName))
                {
                    return "I couldn't identify which character to analyze. Please specify the character name in your request.";
                }

                // Get story references
                var storyReferences = _references.Where(r => r.Type == FileReferenceType.Story).ToList();
                if (!storyReferences.Any())
                {
                    return $"No story references found in this character file. To analyze {characterName} across stories, add story references using 'ref_story_*' in the frontmatter.";
                }

                System.Diagnostics.Debug.WriteLine($"Starting story analysis for {characterName} across {storyReferences.Count} stories");

                // BULLY ENHANCEMENT: Use more comprehensive chapter analysis for revision requests
                int maxChaptersPerStory = IsRevisionAnalysisRequest(prompt) ? 15 : 5; // More chapters for revisions
                
                // Perform character analysis across stories
                var analysis = await _storyAnalysisService.AnalyzeCharacterAcrossStoriesAsync(characterName, storyReferences, maxChaptersPerStory);

                if (!analysis.StoryAnalyses.Any())
                {
                    return $"No mentions of {characterName} found in the referenced stories. The character may not appear in these books, or the search patterns may need adjustment.";
                }

                // Determine the type of analysis to perform based on the prompt
                if (IsRevisionAnalysisRequest(prompt))
                {
                    return await ProcessRevisionAnalysis(analysis, prompt);
                }
                else if (IsConsistencyAnalysisRequest(prompt))
                {
                    return await ProcessConsistencyAnalysis(analysis, prompt);
                }
                else if (IsChapterAnalysisRequest(prompt))
                {
                    return await ProcessChapterAnalysis(analysis, prompt);
                }
                else
                {
                    return await ProcessGeneralStoryAnalysis(analysis, prompt);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in story analysis: {ex.Message}");
                return $"Error analyzing character across stories: {ex.Message}";
            }
        }

        /// <summary>
        /// Processes revision analysis requests - comprehensive story-based profile updates
        /// </summary>
        private async Task<string> ProcessRevisionAnalysis(CharacterStoryAnalysis analysis, string prompt)
        {
            var revisionQuery = new StringBuilder();
            
            revisionQuery.AppendLine($"=== COMPREHENSIVE CHARACTER REVISION ANALYSIS ===");
            revisionQuery.AppendLine($"Character: {analysis.CharacterName}");
            revisionQuery.AppendLine($"Stories Analyzed: {analysis.StoryAnalyses.Count}");
            revisionQuery.AppendLine($"Total Chapters Reviewed: {analysis.StoryAnalyses.Sum(s => s.ChapterAnalyses.Count)}");
            revisionQuery.AppendLine($"Total Character Mentions: {analysis.OverallInsights.TotalMentions}");
            revisionQuery.AppendLine();
            
            // Include detailed story breakdowns for comprehensive revision
            revisionQuery.AppendLine("=== DETAILED STORY EVIDENCE ===");
            foreach (var story in analysis.StoryAnalyses)
            {
                revisionQuery.AppendLine($"**{story.StoryTitle}** ({story.ChapterAnalyses.Count} chapters analyzed):");
                
                // Include top dialogue and action examples
                var topDialogue = story.ChapterAnalyses
                    .SelectMany(c => c.CharacterMentions)
                    .Where(m => m.Type == "DialogueAttribution")
                    .OrderByDescending(m => m.RelevanceScore)
                    .Take(5)
                    .ToList();
                
                var topActions = story.ChapterAnalyses
                    .SelectMany(c => c.CharacterMentions)
                    .Where(m => m.Type == "ActionDescription")
                    .OrderByDescending(m => m.RelevanceScore)
                    .Take(5)
                    .ToList();
                
                if (topDialogue.Any())
                {
                    revisionQuery.AppendLine("  **Key Dialogue Examples:**");
                    foreach (var dialogue in topDialogue)
                    {
                        revisionQuery.AppendLine($"    - {dialogue.Context}");
                    }
                }
                
                if (topActions.Any())
                {
                    revisionQuery.AppendLine("  **Key Action Examples:**");
                    foreach (var action in topActions)
                    {
                        revisionQuery.AppendLine($"    - {action.Context}");
                    }
                }
                revisionQuery.AppendLine();
            }
            
            revisionQuery.AppendLine("=== REVISION REQUEST ===");
            revisionQuery.AppendLine("Based on this comprehensive story evidence, please:");
            revisionQuery.AppendLine("1. **Compare the current profile against story evidence**");
            revisionQuery.AppendLine("2. **Identify gaps or inaccuracies in the current profile**");
            revisionQuery.AppendLine("3. **Suggest specific revisions using the exact code block format required for apply buttons**");
            revisionQuery.AppendLine("4. **Add new profile sections if significant story evidence supports them**");
            revisionQuery.AppendLine("5. **Focus on dialogue patterns, relationship dynamics, and character development based on actual story content**");
            revisionQuery.AppendLine("");
            revisionQuery.AppendLine("CRITICAL: Each revision must use this EXACT format for apply buttons to work:");
            revisionQuery.AppendLine("```");
            revisionQuery.AppendLine("Original text:");
            revisionQuery.AppendLine("[exact text from profile]");
            revisionQuery.AppendLine("```");
            revisionQuery.AppendLine("```");
            revisionQuery.AppendLine("Changed to:");
            revisionQuery.AppendLine("[improved version]");
            revisionQuery.AppendLine("```");
            revisionQuery.AppendLine("");
            revisionQuery.AppendLine("Priority areas for revision:");
            revisionQuery.AppendLine("- Character voice and dialogue patterns from story evidence");
            revisionQuery.AppendLine("- Relationship dynamics with other characters as shown in stories");
            revisionQuery.AppendLine("- Behavioral patterns and character growth across books");
            revisionQuery.AppendLine("- Any profile elements that contradict or miss story evidence");
            
            var systemPrompt = BuildStoryAnalysisPrompt("revision");
            return await SendRequestAsync(systemPrompt, $"{revisionQuery}\n\nUser Request: {prompt}");
        }

        /// <summary>
        /// Processes consistency analysis requests
        /// </summary>
        private async Task<string> ProcessConsistencyAnalysis(CharacterStoryAnalysis analysis, string prompt)
        {
            var consistencyQuery = _storyAnalysisService.BuildCrossStoryConsistencyQuery(analysis);
            var systemPrompt = BuildStoryAnalysisPrompt("consistency");
            
            return await SendRequestAsync(systemPrompt, $"{consistencyQuery}\n\nUser Request: {prompt}");
        }

        /// <summary>
        /// Processes chapter-specific analysis requests
        /// </summary>
        private async Task<string> ProcessChapterAnalysis(CharacterStoryAnalysis analysis, string prompt)
        {
            // For chapter analysis, focus on the most relevant chapters
            var topChapters = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .OrderByDescending(c => c.RelevanceScore)
                .Take(3)
                .ToList();

            var chapterContext = new StringBuilder();
            chapterContext.AppendLine($"=== TOP CHAPTERS FOR {analysis.CharacterName} ===");
            
            foreach (var chapter in topChapters)
            {
                chapterContext.AppendLine(_storyAnalysisService.PrepareChapterForAIAnalysis(chapter, analysis.CharacterName));
                chapterContext.AppendLine();
            }

            var systemPrompt = BuildStoryAnalysisPrompt("chapter");
            return await SendRequestAsync(systemPrompt, $"{chapterContext}\n\nUser Request: {prompt}");
        }

        /// <summary>
        /// Processes general story analysis requests
        /// </summary>
        private async Task<string> ProcessGeneralStoryAnalysis(CharacterStoryAnalysis analysis, string prompt)
        {
            var questions = _storyAnalysisService.GenerateCharacterDevelopmentQuestions(analysis);
            var generalQuery = new StringBuilder();
            
            generalQuery.AppendLine($"=== CHARACTER STORY ANALYSIS FOR {analysis.CharacterName} ===");
            generalQuery.AppendLine($"Found in {analysis.StoryAnalyses.Count} stories, {analysis.StoryAnalyses.Sum(s => s.ChapterAnalyses.Count)} chapters total");
            generalQuery.AppendLine();
            
            generalQuery.AppendLine("=== ANALYSIS STATISTICS ===");
            generalQuery.AppendLine($"Total Mentions: {analysis.OverallInsights.TotalMentions}");
            generalQuery.AppendLine($"Dialogue Mentions: {analysis.OverallInsights.DialogueMentions}");
            generalQuery.AppendLine($"Action Mentions: {analysis.OverallInsights.ActionMentions}");
            generalQuery.AppendLine($"Average Relevance: {analysis.OverallInsights.AverageRelevanceScore:F2}");
            generalQuery.AppendLine();
            
            generalQuery.AppendLine("=== SUGGESTED ANALYSIS QUESTIONS ===");
            foreach (var question in questions)
            {
                generalQuery.AppendLine($"• {question}");
            }
            generalQuery.AppendLine();
            
            // Include some sample contexts from top chapters
            var sampleContexts = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .SelectMany(c => c.CharacterMentions)
                .OrderByDescending(m => m.RelevanceScore)
                .Take(10)
                .ToList();
                
            generalQuery.AppendLine("=== SAMPLE CHARACTER CONTEXTS ===");
            foreach (var context in sampleContexts)
            {
                generalQuery.AppendLine($"**{context.Type}**: {context.Context}");
            }

            var systemPrompt = BuildStoryAnalysisPrompt("general");
            return await SendRequestAsync(systemPrompt, $"{generalQuery}\n\nUser Request: {prompt}");
        }

        /// <summary>
        /// Builds system prompt for story analysis - ALWAYS includes current character content
        /// </summary>
        private string BuildStoryAnalysisPrompt(string analysisType)
        {
            var prompt = new StringBuilder();
            
            prompt.AppendLine("You are a Character Story Analysis Assistant specializing in analyzing character consistency and development across multiple stories.");
            prompt.AppendLine();
            
            switch (analysisType)
            {
                case "revision":
                    prompt.AppendLine("FOCUS: Story-Based Character Profile Revision");
                    prompt.AppendLine("Your primary task is to revise the existing character profile based on comprehensive story evidence.");
                    prompt.AppendLine("CRITICAL: You MUST provide specific revisions using this EXACT format:");
                    prompt.AppendLine("");
                    prompt.AppendLine("```");
                    prompt.AppendLine("Original text:");
                    prompt.AppendLine("[exact text from current profile to replace]");
                    prompt.AppendLine("```");
                    prompt.AppendLine("```");
                    prompt.AppendLine("Changed to:");
                    prompt.AppendLine("[your improved version based on story evidence]");
                    prompt.AppendLine("```");
                    prompt.AppendLine("");
                    prompt.AppendLine("Analyze the story evidence to:");
                    prompt.AppendLine("- Compare current profile against actual story content");
                    prompt.AppendLine("- Identify gaps, inaccuracies, or missing elements in the profile");
                    prompt.AppendLine("- Extract dialogue patterns and speech quirks from story evidence");
                    prompt.AppendLine("- Document relationship dynamics as shown in the stories");
                    prompt.AppendLine("- Suggest new profile sections based on significant story evidence");
                    prompt.AppendLine("- Provide actionable revisions that improve profile accuracy");
                    prompt.AppendLine("");
                    prompt.AppendLine("REVISION GUIDELINES:");
                    prompt.AppendLine("- Base ALL suggestions on the provided story evidence");
                    prompt.AppendLine("- Use specific examples from the stories to justify changes");
                    prompt.AppendLine("- Focus on dialogue patterns, character voice, and relationship dynamics");
                    prompt.AppendLine("- Preserve good existing content that aligns with story evidence");
                    prompt.AppendLine("- Add missing elements that stories clearly demonstrate");
                    prompt.AppendLine("- Each revision MUST use the exact code block format shown above");
                    break;
                    
                case "consistency":
                    prompt.AppendLine("FOCUS: Cross-Story Character Consistency Analysis");
                    prompt.AppendLine("Analyze the provided character data to identify:");
                    prompt.AppendLine("- Dialogue pattern consistency across stories");
                    prompt.AppendLine("- Behavioral consistency and character growth");
                    prompt.AppendLine("- Relationship dynamic consistency");
                    prompt.AppendLine("- Any concerning inconsistencies or contradictions");
                    prompt.AppendLine("- Recommendations for maintaining character consistency");
                    break;
                    
                case "chapter":
                    prompt.AppendLine("FOCUS: Chapter-Level Character Analysis");
                    prompt.AppendLine("Analyze the provided chapter content to provide insights about:");
                    prompt.AppendLine("- Character's role and importance in these scenes");
                    prompt.AppendLine("- Dialogue patterns and voice consistency");
                    prompt.AppendLine("- Character actions and motivations");
                    prompt.AppendLine("- Relationship dynamics with other characters");
                    prompt.AppendLine("- Character development moments");
                    break;
                    
                case "general":
                    prompt.AppendLine("FOCUS: General Character Story Analysis");
                    prompt.AppendLine("Provide comprehensive insights about the character based on their appearances across stories:");
                    prompt.AppendLine("- Overall character presence and importance");
                    prompt.AppendLine("- Key character traits and patterns");
                    prompt.AppendLine("- Character development opportunities");
                    prompt.AppendLine("- Suggestions for future character usage");
                    break;
            }
            
            prompt.AppendLine();
            prompt.AppendLine("ANALYSIS GUIDELINES:");
            prompt.AppendLine("- Base conclusions on the provided evidence from the stories");
            prompt.AppendLine("- Highlight specific examples from the text when possible");
            prompt.AppendLine("- Provide actionable insights for improving character consistency");
            prompt.AppendLine("- Consider both strengths and areas for improvement");
            prompt.AppendLine("- Be specific about which stories or chapters support your conclusions");
            
            // BULLY FIX: ALWAYS include current character content for context
            if (!string.IsNullOrEmpty(_currentContext))
            {
                prompt.AppendLine();
                prompt.AppendLine("=== CURRENT CHARACTER PROFILE FOR COMPARISON ===");
                prompt.AppendLine("This is the existing character profile that you are comparing against the story evidence:");
                prompt.AppendLine("Use this as the baseline to identify consistencies, inconsistencies, and areas for improvement.");
                prompt.AppendLine();
                // Strip frontmatter from character content to avoid muddying the prompt
                string cleanedContent = StripFrontmatter(_currentContext);
                prompt.AppendLine(cleanedContent);
            }
            
            // BULLY FIX: Include reference materials for complete context
            if (_references?.Count > 0)
            {
                prompt.AppendLine();
                prompt.AppendLine("REFERENCE MATERIALS AVAILABLE:");
                
                foreach (var reference in _references)
                {
                    switch (reference.Type)
                    {
                        case FileReferenceType.Style:
                            prompt.AppendLine();
                            prompt.AppendLine("WRITING STYLE GUIDE:");
                            prompt.AppendLine(reference.Content);
                            break;
                            
                        case FileReferenceType.Rules:
                            prompt.AppendLine();
                            prompt.AppendLine("WRITING RULES:");
                            prompt.AppendLine(reference.Content);
                            break;
                    }
                }
            }
            
            return prompt.ToString();
        }

        /// <summary>
        /// Extracts character name from content or prompt
        /// </summary>
        private string ExtractCharacterName(string content, string prompt)
        {
            // First try to extract from frontmatter title
            if (content.StartsWith("---"))
            {
                var frontmatterEnd = content.IndexOf("\n---", 3);
                if (frontmatterEnd > 0)
                {
                    var frontmatter = content.Substring(3, frontmatterEnd - 3);
                    var titleMatch = System.Text.RegularExpressions.Regex.Match(frontmatter, @"title:\s*[""']?([^""'\n]+)[""']?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (titleMatch.Success)
                    {
                        return titleMatch.Groups[1].Value.Trim();
                    }
                }
            }
            
            // Try to extract from filename if available
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var filename = System.IO.Path.GetFileNameWithoutExtension(_currentFilePath);
                // Remove common prefixes/suffixes
                filename = filename.Replace("character-", "").Replace("-character", "").Replace("char-", "").Replace("-char", "");
                if (!string.IsNullOrEmpty(filename) && !filename.Equals("character", StringComparison.OrdinalIgnoreCase))
                {
                    return filename.Replace("-", " ").Replace("_", " ");
                }
            }
            
            // Try to extract from prompt (look for quoted names or "analyze [Name]")
            var promptNameMatch = System.Text.RegularExpressions.Regex.Match(prompt, @"""([^""]+)""|analyze\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (promptNameMatch.Success)
            {
                return (promptNameMatch.Groups[1].Success ? promptNameMatch.Groups[1] : promptNameMatch.Groups[2]).Value.Trim();
            }
            
            return null;
        }

        /// <summary>
        /// Determines if the request is specifically for consistency analysis
        /// </summary>
        private bool IsConsistencyAnalysisRequest(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();
            return lowerPrompt.Contains("consistency") || 
                   lowerPrompt.Contains("consistent") ||
                   lowerPrompt.Contains("inconsistent") ||
                   lowerPrompt.Contains("contradiction") ||
                   lowerPrompt.Contains("compare across") ||
                   lowerPrompt.Contains("differences between");
        }

        /// <summary>
        /// Determines if the request is specifically for profile revision based on stories
        /// </summary>
        private bool IsRevisionAnalysisRequest(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();
            return lowerPrompt.Contains("revise") && (lowerPrompt.Contains("profile") || lowerPrompt.Contains("based on")) ||
                   lowerPrompt.Contains("update") && lowerPrompt.Contains("profile") ||
                   lowerPrompt.Contains("improve") && lowerPrompt.Contains("profile") ||
                   lowerPrompt.Contains("suggest") && (lowerPrompt.Contains("revisions") || lowerPrompt.Contains("changes")) ||
                   lowerPrompt.Contains("modify") && lowerPrompt.Contains("profile") ||
                   lowerPrompt.Contains("enhance") && lowerPrompt.Contains("profile") ||
                   lowerPrompt.Contains("corrections") && lowerPrompt.Contains("profile") ||
                   lowerPrompt.Contains("adjustments") && lowerPrompt.Contains("profile");
        }

        /// <summary>
        /// Determines if the request is specifically for chapter analysis
        /// </summary>
        private bool IsChapterAnalysisRequest(string prompt)
        {
            var lowerPrompt = prompt.ToLowerInvariant();
            return lowerPrompt.Contains("chapter") ||
                   lowerPrompt.Contains("scene") ||
                   lowerPrompt.Contains("in this part") ||
                   lowerPrompt.Contains("specific instance") ||
                   lowerPrompt.Contains("particular moment");
        }

        /// <summary>
        /// Directly sets the rules content without relying on frontmatter - like FictionWritingBeta
        /// </summary>
        /// <param name="rulesContent">The rules content to use</param>
        public void SetRulesContent(string rulesContent)
        {
            if (!string.IsNullOrEmpty(rulesContent))
            {
                _rules = rulesContent;
                
                // Parse the rules for enhanced understanding
                try
                {
                    _parsedRules = _rulesParser.Parse(rulesContent);
                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Successfully parsed directly set rules:");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Characters.Count} characters");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.Timeline.Books.Count} books");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.PlotConnections.Count} plot connections");
                    System.Diagnostics.Debug.WriteLine($"  - {_parsedRules.CriticalFacts.Count} critical facts");
                }
                catch (Exception parseEx)
                {
                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Error parsing directly set rules: {parseEx.Message}");
                    // Continue with unparsed rules
                }
                
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Rules content directly set: {rulesContent.Length} characters");
            }
        }

        /// <summary>
        /// Sets the current file path for reference resolution - like FictionWritingBeta
        /// </summary>
        /// <param name="filePath">The absolute path to the current file</param>
        public void SetCurrentFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                System.Diagnostics.Debug.WriteLine("CharacterDevelopmentChain: Warning: Attempted to set empty file path");
                return;
            }
            
            System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Setting current file path: '{filePath}'");
            _currentFilePath = filePath;
            
            // Also update the file reference service
            if (_fileReferenceService != null)
            {
                _fileReferenceService.SetCurrentFile(filePath);
                System.Diagnostics.Debug.WriteLine("CharacterDevelopmentChain: Updated FileReferenceService with the new file path");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("CharacterDevelopmentChain: Warning: FileReferenceService is null, cannot update");
            }
        }

        /// <summary>
        /// Directly loads reference files from paths without relying on frontmatter - like FictionWritingBeta
        /// </summary>
        /// <param name="rulesPath">Path to rules file</param>
        /// <returns>Async task</returns>
        public async Task LoadRulesDirectly(string rulesPath)
        {
            if (!string.IsNullOrEmpty(rulesPath))
            {
                System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Loading rules directly from: {rulesPath}");
                try
                {
                    string rulesContent = await _fileReferenceService.GetFileContent(rulesPath, _currentFilePath);
                    if (!string.IsNullOrEmpty(rulesContent))
                    {
                        SetRulesContent(rulesContent);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Failed to load rules from path: {rulesPath}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"CharacterDevelopmentChain: Error loading rules directly: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Strips frontmatter from content to provide clean character profile data
        /// </summary>
        private string StripFrontmatter(string content)
        {
            if (string.IsNullOrEmpty(content))
                return content;

            // Check for frontmatter (starts with ---)
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
                // Find the closing ---
                int secondDelimiterPos = content.IndexOf("\n---", 3);
                if (secondDelimiterPos == -1)
                {
                    secondDelimiterPos = content.IndexOf("\r\n---", 3);
                }
                
                if (secondDelimiterPos != -1)
                {
                    // Skip past the closing --- and any following newlines
                    int contentStart = secondDelimiterPos + 4; // Skip past "\n---"
                    if (contentStart < content.Length && content[contentStart] == '\n')
                        contentStart++;
                    else if (contentStart < content.Length - 1 && content.Substring(contentStart, 2) == "\r\n")
                        contentStart += 2;
                    
                    if (contentStart < content.Length)
                    {
                        return content.Substring(contentStart).Trim();
                    }
                }
            }

            // No frontmatter found, return original content
            return content;
        }

        private enum CharacterFileType
        {
            MajorCharacter,
            SupportingCast,
            Relationships
        }
    }
} 