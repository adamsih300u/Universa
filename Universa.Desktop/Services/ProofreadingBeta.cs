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
    public class ProofreadingBeta : BaseLangChainService
    {
        private string _textContent;
        private string _styleGuide;
        private string _consistencyRules; // Lightweight version of rules for names/terms only
        private List<string> _characterProfiles; // Full character profiles for compliance checks
        private readonly FileReferenceService _fileReferenceService;
        private static Dictionary<string, ProofreadingBeta> _instances = new Dictionary<string, ProofreadingBeta>();
        private static readonly object _lock = new object();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
        private const int MESSAGE_HISTORY_LIMIT = 2;  // Keep minimal history for proofreading
        private int _currentCursorPosition;
        private string _currentFilePath;
        private AIProvider _currentProvider;
        private string _currentModel;
        private Dictionary<string, string> _frontmatter;

        // Properties to access provider and model
        protected AIProvider CurrentProvider => _currentProvider;
        protected string CurrentModel => _currentModel;

        private ProofreadingBeta(string apiKey, string model, AIProvider provider, string content, string filePath = null, string libraryPath = null) 
            : base(apiKey, model, provider)
        {
            _currentProvider = provider;
            _currentModel = model;

            // Get the universal library path from configuration
            var configService = ServiceLocator.Instance.GetService<IConfigurationService>();
            var universalLibraryPath = configService?.Provider?.LibraryPath;

            if (string.IsNullOrEmpty(universalLibraryPath))
            {
                System.Diagnostics.Debug.WriteLine("CRITICAL ERROR: Universal library path is not configured for ProofreadingBeta.");
                throw new InvalidOperationException("Universal library path is not configured. Please set it in the application settings.");
            }
            
            _fileReferenceService = new FileReferenceService(universalLibraryPath);
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta FileReferenceService initialized with universal library path: {universalLibraryPath}");

            _currentFilePath = filePath;
            if (!string.IsNullOrEmpty(filePath))
            {
                _fileReferenceService.SetCurrentFile(filePath);
                System.Diagnostics.Debug.WriteLine($"Current file for ProofreadingBeta set to: {filePath}");
            }
        }

        public static async Task<ProofreadingBeta> GetInstance(string apiKey, string model, AIProvider provider, string filePath, string libraryPath)
        {
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("API key and model must be provided");
            }

            if (string.IsNullOrEmpty(libraryPath))
            {
                throw new ArgumentNullException(nameof(libraryPath));
            }

            if (string.IsNullOrEmpty(filePath))
            {
                filePath = "default";
            }

            await _semaphore.WaitAsync();
            try
            {
                ProofreadingBeta instance;
                
                // Always create new instance for proper library path handling
                System.Diagnostics.Debug.WriteLine($"Creating new ProofreadingBeta instance for file: {filePath}");
                instance = new ProofreadingBeta(apiKey, model, provider, null, filePath, libraryPath);
                
                // Update the cache
                _instances[filePath] = instance;
                
                return instance;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error in ProofreadingBeta GetInstance: {ex}");
                throw;
            }
            finally
            {
                _semaphore.Release();
            }
        }

        public async Task UpdateContentAndInitialize(string content)
        {
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta UpdateContentAndInitialize called with content length: {content?.Length ?? 0}");
            
            // Store minimal conversation context for proofreading
            var recentMessages = _memory.Where(m => !m.Role.Equals("system", StringComparison.OrdinalIgnoreCase))
                                      .TakeLast(MESSAGE_HISTORY_LIMIT)
                                      .ToList();
            
            // Clear all messages
            _memory.Clear();
            
            // Update content
            await UpdateContent(content);
            
            // Initialize system message
            InitializeSystemMessage();
            
            // Restore minimal recent messages
            _memory.AddRange(recentMessages);
            
            System.Diagnostics.Debug.WriteLine("ProofreadingBeta UpdateContentAndInitialize completed");
        }

        private async Task UpdateContent(string content)
        {
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta UpdateContent called with content length: {content?.Length ?? 0}");
            
            if (string.IsNullOrEmpty(content))
            {
                _textContent = string.Empty;
                _frontmatter = new Dictionary<string, string>();
                return;
            }
            
            _textContent = content;
            
            // Process frontmatter for references (following FictionWritingBeta pattern)
            bool hasFrontmatter = false;
            _frontmatter = new Dictionary<string, string>();
            
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Checking for frontmatter in content starting with: '{content.Substring(0, Math.Min(20, content.Length))}...'");
            
            if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
            {
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
                                
                                // Remove quotes if present
                                if (value.StartsWith("\"") && value.EndsWith("\""))
                                    value = value.Substring(1, value.Length - 2);
                                
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
                        
                        // Update _textContent to only contain the content after frontmatter
                        int contentStartIndex = endIndex + 4;
                        if (contentStartIndex < content.Length)
                        {
                            if (content[contentStartIndex] == '\n')
                                contentStartIndex++;
                            
                            _textContent = content.Substring(contentStartIndex);
                        }
                        
                        // Set flag indicating we found and processed frontmatter
                        hasFrontmatter = true;
                    }
                }
            }
            
            // Log the extracted frontmatter
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Extracted {_frontmatter.Count} frontmatter items");
            foreach (var key in _frontmatter.Keys)
            {
                System.Diagnostics.Debug.WriteLine($"  '{key}' = '{_frontmatter[key]}'");
            }
            
            // Check for references in frontmatter and process them with cascade support
            if (hasFrontmatter)
            {
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta: Processing frontmatter references with cascade support");
                
                // NEW: Use cascade loading to get style guide references from outline files
                var allReferences = await _fileReferenceService.LoadReferencesWithCascadeAsync(content, enableCascade: true);
                
                // Process style, rules, and character references (keep proofreader focused on style/consistency; DO NOT include outline text)
                foreach (var reference in allReferences)
                {
                    switch (reference.Type)
                    {
                        case FileReferenceType.Style:
                            _styleGuide = reference.Content;
                            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta loaded style guide via cascade: {reference.Content?.Length ?? 0} chars");
                            break;
                            
                        case FileReferenceType.Rules:
                            // Extract only consistency-relevant information from rules
                            _consistencyRules = ExtractConsistencyRules(reference.Content);
                            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta extracted consistency rules via cascade: {_consistencyRules?.Length ?? 0} chars");
                            break;

                        case FileReferenceType.Character:
                            if (_characterProfiles == null)
                                _characterProfiles = new List<string>();
                            // Strip frontmatter from character profiles to avoid muddying content
                            _characterProfiles.Add(StripFrontmatter(reference.Content));
                            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta loaded character profile via cascade: {reference.Content?.Length ?? 0} chars");
                            break;

                        case FileReferenceType.Outline:
                            // Explicitly skip including outline content in ProofreadingBeta prompts
                            System.Diagnostics.Debug.WriteLine("ProofreadingBeta skipping outline content (not included in proofreader prompts)");
                            break;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta: Cascade processing complete");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta: No frontmatter found or no references to process");
            }
            
            System.Diagnostics.Debug.WriteLine("=== PROOFREADING CONTENT SUMMARY ===");
            System.Diagnostics.Debug.WriteLine($"Style guide: {(_styleGuide != null ? $"{_styleGuide.Length} chars" : "NULL")}");
            System.Diagnostics.Debug.WriteLine($"Consistency rules: {(_consistencyRules != null ? $"{_consistencyRules.Length} chars" : "NULL")}");
            System.Diagnostics.Debug.WriteLine($"Text content: {(_textContent != null ? $"{_textContent.Length} chars" : "NULL")}");
        }



        private string ExtractConsistencyRules(string rulesContent)
        {
            var consistencyInfo = new StringBuilder();
            
            // Extract character names, place names, and key terms for consistency checking
            var lines = rulesContent.Split('\n');
            
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                
                // Look for patterns that indicate names or terms
                if (trimmedLine.Contains("Character:") || trimmedLine.Contains("Name:") ||
                    trimmedLine.Contains("Place:") || trimmedLine.Contains("Location:") ||
                    trimmedLine.Contains("Term:") || trimmedLine.Contains("Word:") ||
                    Regex.IsMatch(trimmedLine, @"^[A-Z][a-zA-Z\s]+:") || // Likely character names
                    Regex.IsMatch(trimmedLine, @"^\*\*[A-Z]"))  // Bold names
                {
                    consistencyInfo.AppendLine(trimmedLine);
                }
            }
            
            return consistencyInfo.ToString().Trim();
        }

        private void InitializeSystemMessage()
        {
            string systemPrompt = BuildProofreadingPrompt();
            AddSystemMessage(systemPrompt);
        }

        private string BuildProofreadingPrompt()
        {
            var prompt = new StringBuilder();
            
            // Enhanced proofreading identity and expertise
            prompt.AppendLine("You are a MASTER COPY EDITOR and PROFESSIONAL PROOFREADER with expertise in grammar, style, consistency, and technical accuracy. Your goal is to identify and correct errors while maintaining the author's voice and style.");
            
            // Add current date and time context
            prompt.AppendLine("");
            prompt.AppendLine("=== CURRENT DATE AND TIME ===");
            prompt.AppendLine($"Current Date/Time: {DateTime.Now:F}");
            prompt.AppendLine($"Local Time Zone: {TimeZoneInfo.Local.DisplayName}");
            
            // Add genre-specific proofreading context
            if (_frontmatter != null)
            {
                prompt.AppendLine("");
                prompt.AppendLine("=== DOCUMENT METADATA ===");
                
                if (_frontmatter.TryGetValue("title", out string title) && !string.IsNullOrEmpty(title))
                {
                    prompt.AppendLine($"Title: {title}");
                }
                
                if (_frontmatter.TryGetValue("author", out string author) && !string.IsNullOrEmpty(author))
                {
                    prompt.AppendLine($"Author: {author}");
                }
                
                if (_frontmatter.TryGetValue("genre", out string genre) && !string.IsNullOrEmpty(genre))
                {
                    prompt.AppendLine($"Genre: {genre}");
                    
                    // Add genre-specific proofreading guidance
                    prompt.AppendLine("");
                    prompt.AppendLine("=== GENRE-SPECIFIC PROOFREADING AWARENESS ===");
                    
                    switch (genre.ToLowerInvariant())
                    {
                        case "fantasy":
                            prompt.AppendLine("FANTASY CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Archaic language patterns (thou, amongst, whilst, etc.) may be intentional");
                            prompt.AppendLine("- Invented words, names, and terminology specific to the fantasy world");
                            prompt.AppendLine("- Formal or ceremonial dialogue patterns and speech");
                            prompt.AppendLine("- Made-up place names and proper nouns");
                            break;
                        case "science fiction":
                        case "sci-fi":
                            prompt.AppendLine("SCIENCE FICTION CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Technical jargon and scientific terminology");
                            prompt.AppendLine("- Neologisms and invented future technology terms");
                            prompt.AppendLine("- Unconventional future tense constructions");
                            prompt.AppendLine("- Made-up scientific concepts and terminology");
                            break;
                        case "romance":
                            prompt.AppendLine("ROMANCE CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Emotional sentence fragments for dramatic effect");
                            prompt.AppendLine("- Intimate and passionate dialogue styles");
                            prompt.AppendLine("- Repetitive emotional emphasis (breathless, overwhelming, etc.)");
                            prompt.AppendLine("- Stream-of-consciousness internal thoughts");
                            break;
                        case "historical fiction":
                        case "historical":
                            prompt.AppendLine("HISTORICAL FICTION CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Period-appropriate language that may seem incorrect by modern standards");
                            prompt.AppendLine("- Historical spellings and terminology");
                            prompt.AppendLine("- Formal speech patterns appropriate to the time period");
                            prompt.AppendLine("- Cultural references and expressions from the era");
                            break;
                        case "young adult":
                        case "ya":
                            prompt.AppendLine("YOUNG ADULT CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Contemporary slang and informal language");
                            prompt.AppendLine("- Deliberately casual grammar and sentence structure");
                            prompt.AppendLine("- Shorter, punchy sentences for pacing");
                            prompt.AppendLine("- Modern dialogue patterns and speech rhythms");
                            break;
                        case "literary fiction":
                        case "literary":
                            prompt.AppendLine("LITERARY FICTION CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Experimental punctuation and sentence structures");
                            prompt.AppendLine("- Stream of consciousness and unconventional narration");
                            prompt.AppendLine("- Complex, multi-layered sentence constructions");
                            prompt.AppendLine("- Intentional grammatical variations for artistic effect");
                            break;
                        case "thriller":
                        case "political thriller":
                        case "legal thriller":
                        case "medical thriller":
                            prompt.AppendLine("THRILLER CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Short, punchy sentences for tension and pacing");
                            prompt.AppendLine("- Present tense for immediacy and urgency");
                            prompt.AppendLine("- Technical/professional jargon appropriate to the thriller type");
                            prompt.AppendLine("- Cliffhanger sentence fragments and incomplete thoughts");
                            prompt.AppendLine("- Fast-paced dialogue with minimal tags");
                            prompt.AppendLine("- Journalistic or investigative writing influences");
                            break;
                        case "mystery":
                        case "detective":
                        case "cozy mystery":
                            prompt.AppendLine("MYSTERY CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Procedural language and investigative terminology");
                            prompt.AppendLine("- Deliberate withholding of information from reader");
                            prompt.AppendLine("- Red herring dialogue and misdirection");
                            prompt.AppendLine("- Police/detective procedural jargon");
                            prompt.AppendLine("- Observational, detail-focused narrative style");
                            break;
                        case "horror":
                        case "psychological horror":
                            prompt.AppendLine("HORROR CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Fragmented sentences during tension/fear scenes");
                            prompt.AppendLine("- Stream of consciousness during panic or terror");
                            prompt.AppendLine("- Deliberately unsettling or unusual word choices");
                            prompt.AppendLine("- Short, abrupt sentences for shock value");
                            prompt.AppendLine("- Sensory-heavy descriptions that may seem excessive");
                            break;
                        case "western":
                            prompt.AppendLine("WESTERN CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Period-appropriate colloquial speech and dialect");
                            prompt.AppendLine("- Frontier terminology and historical language");
                            prompt.AppendLine("- Sparse, laconic dialogue style");
                            prompt.AppendLine("- Regional expressions and cowboy vernacular");
                            break;
                        case "urban fantasy":
                            prompt.AppendLine("URBAN FANTASY CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Modern slang mixed with fantasy terminology");
                            prompt.AppendLine("- Casual, contemporary voice with magical elements");
                            prompt.AppendLine("- Technical modern terms alongside fantasy concepts");
                            prompt.AppendLine("- Fast-paced, action-oriented sentence structure");
                            break;
                        case "dystopian":
                        case "post-apocalyptic":
                            prompt.AppendLine("DYSTOPIAN CONVENTIONS - Consider these intentional stylistic choices:");
                            prompt.AppendLine("- Invented governmental/societal terminology");
                            prompt.AppendLine("- Stripped-down, sparse language reflecting harsh world");
                            prompt.AppendLine("- Technical survival and resource-related vocabulary");
                            prompt.AppendLine("- Propaganda-style language and doublespeak");
                            break;
                        default:
                            prompt.AppendLine($"GENRE: {genre} - Be aware that this genre may have specific stylistic conventions.");
                            prompt.AppendLine("- Research typical conventions for this genre when unsure about style choices");
                            prompt.AppendLine("- Consider whether apparent 'errors' might be intentional genre conventions");
                            break;
                    }
                }
                
                if (_frontmatter.TryGetValue("series", out string series) && !string.IsNullOrEmpty(series))
                {
                    prompt.AppendLine($"Series: {series}");
                }
            }
            
            // Add style guide if available (CRITICAL: This was being loaded but never used!)
            if (!string.IsNullOrEmpty(_styleGuide))
            {
                prompt.AppendLine("");
                prompt.AppendLine("=== AUTHOR'S STYLE GUIDE ===");
                prompt.AppendLine("The following style guide defines the author's preferred conventions and intentional choices:");
                prompt.AppendLine("CRITICAL: These are the author's deliberate style decisions - do NOT correct text that follows these guidelines!");
                prompt.AppendLine(_styleGuide);
                prompt.AppendLine("");
                prompt.AppendLine("STYLE GUIDE PRIORITY: When the style guide conflicts with general grammar rules, FOLLOW THE STYLE GUIDE.");
            }
            
            // Add consistency rules if available
            if (!string.IsNullOrEmpty(_consistencyRules))
            {
                prompt.AppendLine("");
                prompt.AppendLine("=== CONSISTENCY REFERENCE ===");
                prompt.AppendLine("Ensure these names and terms are spelled consistently throughout:");
                prompt.AppendLine(_consistencyRules);
            }

            // Add character profiles if available (full cascade, no limiting)
            if (_characterProfiles != null && _characterProfiles.Count > 0)
            {
                prompt.AppendLine("");
                prompt.AppendLine("=== CHARACTER PROFILES ===");
                prompt.AppendLine("Use these profiles to enforce character consistency in voice, behavior, relationships, facts, and abilities. Do not alter text that intentionally follows these profiles.");
                for (int i = 0; i < _characterProfiles.Count; i++)
                {
                    prompt.AppendLine("");
                    prompt.AppendLine($"--- Character Profile {i + 1} ---");
                    prompt.AppendLine(_characterProfiles[i]);
                }
            }
            
            // Enhanced response format instructions with exact text matching guidance
            prompt.AppendLine("");
            prompt.AppendLine("=== RESPONSE FORMAT (STRICT) ===");
            prompt.AppendLine("You MUST return corrections as structured blocks. Do NOT return reasons-only. For each correction, include all three blocks below. If there are NO corrections, respond with 'NO CORRECTIONS NEEDED' and nothing else.");
            prompt.AppendLine("");
            prompt.AppendLine("For CORRECTIONS (fixing errors):");
            prompt.AppendLine("```");
            prompt.AppendLine("Original text:");
            prompt.AppendLine("[paste the exact text to be replaced]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("```");
            prompt.AppendLine("Changed to:");
            prompt.AppendLine("[your corrected version of the text]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("```");
            prompt.AppendLine("Reason:");
            prompt.AppendLine("[brief explanation for the correction]");
            prompt.AppendLine("```");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL SCOPE ANALYSIS - Before selecting original text, always ask:");
            prompt.AppendLine("- Does this correction affect sentence flow or meaning in surrounding sentences?");
            prompt.AppendLine("- Would adjacent sentences become grammatically incorrect after this fix?");
            prompt.AppendLine("- Is this part of a larger grammatical structure that spans multiple clauses?");
            prompt.AppendLine("- Does the correction logic continue beyond the obvious error?");
            prompt.AppendLine("- Are there related phrases or clauses that reference the corrected content?");
            prompt.AppendLine("");
            prompt.AppendLine("COMPLETE CORRECTION UNITS - When selecting original text, include ALL affected content:");
            prompt.AppendLine("- Complete sentences if grammar or punctuation affects sentence structure");
            prompt.AppendLine("- Full clauses that share grammatical dependencies");
            prompt.AppendLine("- Any surrounding text that would become inconsistent after the correction");
            prompt.AppendLine("- Related phrases that continue the corrected content's meaning");
            prompt.AppendLine("- Punctuation sequences that span multiple sentences or clauses");
            prompt.AppendLine("");
            prompt.AppendLine("SCOPE EXAMPLES:");
            prompt.AppendLine("BAD - Incomplete scope: Only corrects \"they was going\" but leaves dependent clause creating grammar inconsistency");
            prompt.AppendLine("GOOD - Complete scope: Corrects entire sentence to maintain grammatical coherence throughout");
            prompt.AppendLine("");
            prompt.AppendLine("CRITICAL TEXT MATCHING REQUIREMENTS:");
            prompt.AppendLine("- Original text must be EXACT, COMPLETE, and VERBATIM from the source");
            prompt.AppendLine("- Include complete sentences or natural text boundaries (periods, paragraph breaks)");
            prompt.AppendLine("- Don't use partial snippets - provide enough context for precise matching");
            prompt.AppendLine("- Include surrounding punctuation and whitespace exactly as it appears");
            prompt.AppendLine("- When in doubt, provide MORE context rather than less for accurate replacement");
            prompt.AppendLine("");
            prompt.AppendLine("STRICTNESS:");
            prompt.AppendLine("- Do NOT output general lists of reasons without corresponding 'Original text' and 'Changed to' blocks.");
            prompt.AppendLine("- Each correction MUST include a 'Reason' block immediately after the 'Changed to' block.");
            prompt.AppendLine("");
            prompt.AppendLine("TEXT SELECTION BEST PRACTICES:");
            prompt.AppendLine("- Start and end at natural boundaries (sentence start, period, paragraph break)");
            prompt.AppendLine("- Include 2-3 words before and after the actual error for precise matching");
            prompt.AppendLine("- For punctuation errors, include the complete sentence containing the error");
            prompt.AppendLine("- For word choice errors, include the complete phrase or clause");
            prompt.AppendLine("- For grammar errors, include all grammatically dependent elements");
            prompt.AppendLine("");
            prompt.AppendLine("=== PROOFREADING PRIORITIES ===");
            prompt.AppendLine("1. **RESPECT INTENTIONAL CHOICES**: Do NOT correct style guide preferences or genre conventions");
            prompt.AppendLine("2. **ACTUAL ERRORS**: Focus on true grammar, spelling, and syntax mistakes");
            prompt.AppendLine("3. **CONSISTENCY**: Ensure character names, places, and terms match established patterns");
            prompt.AppendLine("4. **CLARITY**: Improve readability without changing the author's voice");
            prompt.AppendLine("5. **TECHNICAL ACCURACY**: Fix punctuation, capitalization, and formatting errors");
            prompt.AppendLine("");
            prompt.AppendLine("=== DECISION FRAMEWORK ===");
            prompt.AppendLine("Before suggesting a correction, ask:");
            prompt.AppendLine("- Is this an actual error, or could it be an intentional stylistic choice?");
            prompt.AppendLine("- Does the style guide or genre convention support this usage?");
            prompt.AppendLine("- Would this correction improve technical accuracy without changing the author's voice?");
            prompt.AppendLine("- Is this correction necessary for clarity or consistency?");
            prompt.AppendLine("");
            prompt.AppendLine("When providing corrections:");
            prompt.AppendLine("- Focus on technical accuracy and precision");
            prompt.AppendLine("- Preserve the author's voice and style");
            prompt.AppendLine("- Provide clear, actionable corrections with exact text replacement");
            prompt.AppendLine("- Include brief explanations only when the correction might be unclear");
            prompt.AppendLine("- Prioritize errors that affect meaning or readability");
            
            return prompt.ToString();
        }

        public override async Task<string> ProcessRequest(string content, string request)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"ProofreadingBeta ProcessRequest called with content length: {content?.Length ?? 0}");
                
                // Update content if it has changed
                if (_textContent != content)
                {
                    await UpdateContentAndInitialize(content);
                }

                // Build focused context for proofreading
                var contextPrompt = BuildContextPrompt(request);
                
                // Add the user request to memory
                AddUserMessage(request);
                
                try
                {
                    // Process the request with the AI
                    var response = await ExecutePrompt(contextPrompt);
                    
                    // Add the response to memory
                    AddAssistantMessage(response);
                    
                    return response;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"\n=== PROOFREADING BETA ERROR ===\n{ex}");
                    throw;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"\n=== PROOFREADING BETA ERROR ===\n{ex}");
                throw;
            }
        }

        private string BuildContextPrompt(string request)
        {
            var prompt = new StringBuilder();
            
            // Reinforce structured output requirements in the context prompt
            prompt.AppendLine("=== OUTPUT REQUIREMENTS ===");
            prompt.AppendLine("Return corrections ONLY as structured blocks with 'Original text', 'Changed to', and 'Reason'. If there are no corrections, output exactly: NO CORRECTIONS NEEDED");
            
            // Include relevant section of text content for proofreading
            if (!string.IsNullOrEmpty(_textContent))
            {
                var relevantContent = GetRelevantContentForProofreading(_textContent, request);
                prompt.AppendLine("\n=== TEXT TO PROOFREAD ===");
                prompt.AppendLine("Focus on technical accuracy, grammar, and consistency in this text. The content below represents the current chapter where the cursor is positioned, ensuring complete context for proofreading:");
                prompt.AppendLine(relevantContent);
            }

            return prompt.ToString();
        }

		/// <summary>
		/// Removes leading YAML frontmatter delimited by --- from markdown content.
		/// Returns the original content if no well-formed frontmatter is found.
		/// </summary>
		private string StripFrontmatter(string content)
		{
			if (string.IsNullOrEmpty(content))
			{
				return string.Empty;
			}

			try
			{
				if (content.StartsWith("---\n") || content.StartsWith("---\r\n"))
				{
					int firstNewline = content.IndexOf('\n');
					int searchStart = firstNewline >= 0 ? firstNewline + 1 : 0;
					int endIndex = content.IndexOf("\n---", searchStart);
					if (endIndex > searchStart)
					{
						int contentStartIndex = endIndex + 4;
						if (contentStartIndex < content.Length && content[contentStartIndex] == '\n')
						{
							contentStartIndex++;
						}
						return content.Substring(contentStartIndex);
					}
				}
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"ProofreadingBeta StripFrontmatter error: {ex.Message}");
			}

			return content;
		}

        private string GetRelevantContentForProofreading(string content, string request)
        {
            // Check for empty content
            if (string.IsNullOrEmpty(content))
            {
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta GetRelevantContentForProofreading: Content is empty");
                return string.Empty;
            }

            System.Diagnostics.Debug.WriteLine($"🔍 ProofreadingBeta: Starting content analysis with cursor position {_currentCursorPosition}");
            System.Diagnostics.Debug.WriteLine($"   Content length: {content.Length} characters");

            // For small files, return everything for complete context
            if (content.Length < 5000)
            {
                System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Small file ({content.Length} chars), returning all content");
                return content;
            }

            // For larger files, use chapter-based chunking for better context
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Large file ({content.Length} chars), using chapter-based chunking");
            
            // Split content into lines to find chapter boundaries
            var lines = content.Split('\n');
            System.Diagnostics.Debug.WriteLine($"   Split into {lines.Length} lines");
            
            // Skip the #file: line if present - this is our own metadata
            int startLineIndex = 0;
            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#file:"))
            {
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta: Skipping #file: metadata line");
                startLineIndex = 1;
            }
            
            // Find the current line index based on cursor position
            int currentLineIndex = startLineIndex;
            int currentPosition = 0;
            
            // Skip the first line positions if we're skipping the #file: line
            if (startLineIndex > 0 && lines.Length > 0)
            {
                currentPosition += lines[0].Length + 1;
            }
            
            // Find which line the cursor is on
            bool foundCursorLine = false;
            for (int i = startLineIndex; i < lines.Length; i++)
            {
                if (currentPosition <= _currentCursorPosition && 
                    _currentCursorPosition <= currentPosition + lines[i].Length + 1)
                {
                    currentLineIndex = i;
                    foundCursorLine = true;
                    System.Diagnostics.Debug.WriteLine($"📍 ProofreadingBeta: Found cursor at line {i} (position {_currentCursorPosition})");
                    System.Diagnostics.Debug.WriteLine($"   Line content: '{lines[i].Substring(0, Math.Min(50, lines[i].Length))}...'");
                    break;
                }
                currentPosition += lines[i].Length + 1;
            }
            
            if (!foundCursorLine)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  ProofreadingBeta: Cursor line not found! Cursor at {_currentCursorPosition}, content ends at {currentPosition}");
                currentLineIndex = Math.Min(startLineIndex, lines.Length - 1);
            }
            
            // Use unified chapter detection service for consistent boundary detection
            var chapterBoundaries = ChapterDetectionService.GetChapterBoundaries(content);
            System.Diagnostics.Debug.WriteLine($"📚 ProofreadingBeta: Found {chapterBoundaries.Count} chapter boundaries:");
            
            // Log all chapter boundaries for debugging
            for (int i = 0; i < chapterBoundaries.Count; i++)
            {
                var boundaryLine = chapterBoundaries[i];
                if (boundaryLine < lines.Length)
                {
                    var lineContent = lines[boundaryLine].Trim();
                    System.Diagnostics.Debug.WriteLine($"   Boundary [{i}] Line {boundaryLine}: '{lineContent}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"   Boundary [{i}] Line {boundaryLine}: [END OF DOCUMENT]");
                }
            }

            // Find which chapter contains the cursor
            int currentChapterIndex = 0;
            
            for (int i = 0; i < chapterBoundaries.Count - 1; i++)
            {
                if (currentLineIndex >= chapterBoundaries[i] && currentLineIndex < chapterBoundaries[i + 1])
                {
                    currentChapterIndex = i;
                    System.Diagnostics.Debug.WriteLine($"🎯 ProofreadingBeta: Cursor is in chapter {i} (lines {chapterBoundaries[i]} to {chapterBoundaries[i + 1]})");
                    break;
                }
            }
            
            // Ensure currentChapterIndex is valid
            if (currentChapterIndex >= chapterBoundaries.Count - 1)
            {
                currentChapterIndex = chapterBoundaries.Count - 2;
                System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Clamped chapter index to {currentChapterIndex}");
            }
            
            // Extract the current chapter content
            var chapterStart = chapterBoundaries[currentChapterIndex];
            var chapterEnd = Math.Min(chapterBoundaries[currentChapterIndex + 1], lines.Length - 1);
            
            System.Diagnostics.Debug.WriteLine($"📖 ProofreadingBeta: Extracting chapter {currentChapterIndex} content (lines {chapterStart}-{chapterEnd})");
            
            // Show what chapter we're actually extracting
            if (chapterStart < lines.Length)
            {
                var chapterTitle = lines[chapterStart].Trim();
                System.Diagnostics.Debug.WriteLine($"   Chapter title: '{chapterTitle}'");
                
                // Extract chapter number for additional verification
                var extractedChapterNum = ChapterDetectionService.ExtractChapterNumber(chapterTitle);
                if (extractedChapterNum.HasValue)
                {
                    System.Diagnostics.Debug.WriteLine($"   ✅ Confirmed: Extracting Chapter {extractedChapterNum.Value}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"   ⚠️  Warning: Could not extract chapter number from '{chapterTitle}'");
                }
            }
            
            if (chapterStart >= lines.Length || chapterEnd >= lines.Length)
            {
                System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Invalid line range, falling back to cursor-based snippet");
                // Fallback to simple snippet around cursor if chapter detection fails
                return GetSimpleCursorSnippet(content);
            }
            
            // Get the complete current chapter content
            var currentChapterContent = string.Join("\n",
                lines.Skip(chapterStart)
                     .Take(chapterEnd - chapterStart + 1));
            
            if (string.IsNullOrEmpty(currentChapterContent))
            {
                System.Diagnostics.Debug.WriteLine("ProofreadingBeta: Chapter content is empty, falling back to cursor snippet");
                return GetSimpleCursorSnippet(content);
            }
            
            // Build the result with chapter context
            var result = new StringBuilder();
            
            // Add marker if we're not at the beginning
            if (currentChapterIndex > 0)
            {
                result.AppendLine("... (Previous chapters not shown for focused proofreading) ...");
                result.AppendLine();
            }
            
            // Add the current chapter (no cursor marker included)
            result.AppendLine("=== CURRENT CHAPTER FOR PROOFREADING ===");
            result.AppendLine(currentChapterContent);
            
            // Add marker if there are more chapters
            if (currentChapterIndex < chapterBoundaries.Count - 2)
            {
                result.AppendLine();
                result.AppendLine("... (Subsequent chapters not shown for focused proofreading) ...");
            }
            
            var selectedContent = result.ToString();
            System.Diagnostics.Debug.WriteLine($"✅ ProofreadingBeta: Selected chapter content: {selectedContent.Length} characters ({(selectedContent.Length * 100.0 / content.Length):F1}% of full content)");
            
            return selectedContent;
        }

        /// <summary>
        /// Fallback method for simple cursor-based snippet when chapter detection fails
        /// </summary>
        private string GetSimpleCursorSnippet(string content)
        {
            int snippetRadius = 2500; // Smaller chunks for focused proofreading
            int start = Math.Max(0, _currentCursorPosition - snippetRadius);
            int snippetLength = snippetRadius * 2;
            
            if (start + snippetLength > content.Length)
            {
                start = Math.Max(0, content.Length - snippetLength);
            }
            
            string snippet = content.Substring(start, Math.Min(snippetLength, content.Length - start));
            
            var result = new StringBuilder();
            if (start > 0)
            {
                result.AppendLine("... (Earlier content not shown) ...");
            }
            result.AppendLine(snippet);
            if (start + snippet.Length < content.Length)
            {
                result.AppendLine("... (Later content not shown) ...");
            }
            
            return result.ToString();
        }

        public void UpdateCursorPosition(int position)
        {
            var previousPosition = _currentCursorPosition;
            _currentCursorPosition = position;
            System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Cursor position updated from {previousPosition} to {position}");
            
            // Immediately determine which chapter this cursor position is in for debugging
            if (!string.IsNullOrEmpty(_textContent))
            {
                var currentChapter = ChapterDetectionService.GetCurrentChapterNumber(_textContent, position);
                System.Diagnostics.Debug.WriteLine($"ProofreadingBeta: Cursor position {position} is in Chapter {currentChapter}");
            }
        }

        public void SetCurrentFilePath(string filePath)
        {
            _currentFilePath = filePath;
            _fileReferenceService?.SetCurrentFile(filePath);
        }

        public static void ClearInstance(string filePath)
        {
            lock (_lock)
            {
                _instances.Remove(filePath);
            }
        }

        public static void ClearInstance()
        {
            lock (_lock)
            {
                _instances.Clear();
            }
        }

        public event EventHandler<RetryEventArgs> OnRetryingOverloadedRequest;

        public override async Task UpdateContextAsync(string context)
        {
            await UpdateContentAndInitialize(context);
        }
    }
} 