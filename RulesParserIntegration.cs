using System;
using System.Linq;
using System.Text;
using Universa.Desktop.Services;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Example integration of RulesParser into FictionWritingBeta
    /// This shows how parsed rules can enhance the AI's understanding
    /// </summary>
    public partial class EnhancedFictionWritingBetaWithRules
    {
        private readonly RulesParser _rulesParser = new RulesParser();
        private RulesParser.ParsedRules _parsedRules;

        private async Task ProcessRulesReference(string refPath)
        {
            try
            {
                string rulesContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(rulesContent))
                {
                    _rules = rulesContent;
                    _parsedRules = _rulesParser.Parse(rulesContent);
                    System.Diagnostics.Debug.WriteLine($"Successfully parsed rules with {_parsedRules.Characters.Count} characters, {_parsedRules.Timeline.Books.Count} books");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing rules reference: {ex.Message}");
            }
        }

        private string BuildEnhancedFictionPrompt(string request)
        {
            var prompt = new StringBuilder();
            
            // Base prompt
            prompt.AppendLine("You are an AI assistant specialized in helping users write and edit fiction. You will analyze and respond based on the sections provided below.");

            // NEW: Use the three-tier filtering system for optimal context
            if (_parsedRules != null)
            {
                prompt.AppendLine("\n## ENHANCED CONTEXTUAL RULES:");
                
                try
                {
                    // Use the new filtered content system that provides exactly what's needed
                    var filteredRulesContent = _rulesParser.GetFilteredRulesContent(_parsedRules, _fictionContent, _currentFilePath);
                    
                    if (!string.IsNullOrEmpty(filteredRulesContent))
                    {
                        prompt.AppendLine(filteredRulesContent);
                        System.Diagnostics.Debug.WriteLine("Successfully integrated three-tier filtered rules content");
                    }
                    else
                    {
                        // Fallback to legacy method if filtering fails
                        AddLegacyRulesContent(prompt);
                        System.Diagnostics.Debug.WriteLine("Using legacy rules integration as fallback");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error with enhanced rules filtering: {ex.Message}");
                    // Fallback to legacy method
                    AddLegacyRulesContent(prompt);
                }
            }
            else if (!string.IsNullOrEmpty(_rules))
            {
                // If no parsed rules, use raw content
                prompt.AppendLine("\n## CORE RULES (Full Document):");
                prompt.AppendLine(_rules);
            }

            return prompt.ToString();
        }

        /// <summary>
        /// Legacy rules integration method for fallback compatibility
        /// </summary>
        private void AddLegacyRulesContent(StringBuilder prompt)
        {
            prompt.AppendLine("\n## CRITICAL STORY FACTS (legacy mode):");
            
            // Add critical facts
            if (_parsedRules.CriticalFacts.Any())
            {
                prompt.AppendLine("Key facts you must remember:");
                foreach (var fact in _parsedRules.CriticalFacts.Take(10))
                {
                    prompt.AppendLine($"- {fact}");
                }
            }
            
            // Add character context based on current document
            var mentionedCharacters = ExtractMentionedCharacters(_fictionContent);
            if (mentionedCharacters.Any())
            {
                prompt.AppendLine("\n### Active Characters in Current Scene:");
                foreach (var charName in mentionedCharacters)
                {
                    if (_parsedRules.Characters.TryGetValue(charName, out var character))
                    {
                        prompt.AppendLine($"\n**{character.Name}**:");
                        
                        // Add age if we know the current book number
                        var currentBook = DetermineCurrentBook(_fictionContent);
                        if (currentBook > 0 && character.AgeByBook.TryGetValue(currentBook, out var age))
                        {
                            prompt.AppendLine($"- Age: {age}");
                        }
                        
                        // Add physical traits
                        if (character.PhysicalTraits.Any())
                        {
                            prompt.AppendLine($"- Appearance: {string.Join(", ", character.PhysicalTraits.Take(3))}");
                        }
                        
                        // Add personality
                        if (character.PersonalityTraits.Any())
                        {
                            prompt.AppendLine($"- Personality: {string.Join(", ", character.PersonalityTraits.Take(3))}");
                        }
                        
                        // Add relevant relationships
                        var relevantRelationships = character.Relationships
                            .Where(r => mentionedCharacters.Contains(r.Key))
                            .ToList();
                        if (relevantRelationships.Any())
                        {
                            prompt.AppendLine($"- Relationships: {string.Join("; ", relevantRelationships.Select(r => $"{r.Key}: {r.Value}"))}");
                        }
                    }
                }
            }
            
            // Add the traditional rules section as final fallback
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n## CORE RULES (Full Document):");
                prompt.AppendLine(_rules);
            }
        }

        private List<string> ExtractMentionedCharacters(string content)
        {
            if (_parsedRules == null || string.IsNullOrEmpty(content))
                return new List<string>();

            var mentioned = new List<string>();
            
            foreach (var character in _parsedRules.Characters.Keys)
            {
                // Check for full name or common variations
                if (content.Contains(character, StringComparison.OrdinalIgnoreCase))
                {
                    mentioned.Add(character);
                }
                else
                {
                    // Check for last name only (common in fiction)
                    var lastName = character.Split(' ').LastOrDefault();
                    if (!string.IsNullOrEmpty(lastName) && 
                        content.Contains(lastName, StringComparison.OrdinalIgnoreCase))
                    {
                        mentioned.Add(character);
                    }
                }
            }
            
            return mentioned.Distinct().ToList();
        }

        private int DetermineCurrentBook(string content)
        {
            // Look for book references in the content
            var bookMatch = System.Text.RegularExpressions.Regex.Match(content, 
                @"Book\s+(\d+)", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                
            if (bookMatch.Success && int.TryParse(bookMatch.Groups[1].Value, out var bookNum))
            {
                return bookNum;
            }
            
            // Look in filename or metadata
            if (!string.IsNullOrEmpty(_currentFilePath))
            {
                var fileBookMatch = System.Text.RegularExpressions.Regex.Match(_currentFilePath, 
                    @"Book[\s_-]?(\d+)", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                if (fileBookMatch.Success && int.TryParse(fileBookMatch.Groups[1].Value, out var fileBookNum))
                {
                    return fileBookNum;
                }
            }
            
            return 0;
        }

        private RulesParser.Book GetCurrentBookInfo()
        {
            if (_parsedRules == null) return null;
            
            var currentBook = DetermineCurrentBook(_fictionContent);
            if (currentBook > 0)
            {
                return _parsedRules.Timeline.Books.FirstOrDefault(b => b.Number == currentBook);
            }
            
            return null;
        }

        // Context-aware validation
        public string ValidateAgainstRules(string content)
        {
            if (_parsedRules == null) return null;
            
            var errors = new StringBuilder();
            
            // Check character consistency
            var mentionedChars = ExtractMentionedCharacters(content);
            var currentBook = DetermineCurrentBook(content);
            
            foreach (var charName in mentionedChars)
            {
                if (_parsedRules.Characters.TryGetValue(charName, out var character))
                {
                    // Check if character should be alive
                    if (!string.IsNullOrEmpty(character.Fate) && 
                        character.Fate.Contains("dies", StringComparison.OrdinalIgnoreCase))
                    {
                        // Extract book where they die
                        var deathBookMatch = System.Text.RegularExpressions.Regex.Match(character.Fate, @"Book\s+(\d+)");
                        if (deathBookMatch.Success && 
                            int.TryParse(deathBookMatch.Groups[1].Value, out var deathBook) && 
                            currentBook > deathBook)
                        {
                            errors.AppendLine($"WARNING: {charName} died in Book {deathBook} but appears in current text (Book {currentBook})");
                        }
                    }
                    
                    // Check age consistency
                    if (currentBook > 0 && character.AgeByBook.Any())
                    {
                        // Find closest known age
                        var closestBook = character.AgeByBook.Keys
                            .Where(b => b <= currentBook)
                            .OrderByDescending(b => b)
                            .FirstOrDefault();
                            
                        if (closestBook > 0)
                        {
                            var baseAge = character.AgeByBook[closestBook];
                            var estimatedAge = baseAge + (currentBook - closestBook);
                            
                            // Look for age mentions in content
                            var ageMatch = System.Text.RegularExpressions.Regex.Match(content, 
                                $@"{charName}.*?(\d{{2,3}})\s*years?\s*old", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                
                            if (ageMatch.Success && int.TryParse(ageMatch.Groups[1].Value, out var mentionedAge))
                            {
                                if (Math.Abs(mentionedAge - estimatedAge) > 2)
                                {
                                    errors.AppendLine($"WARNING: {charName}'s age ({mentionedAge}) doesn't match expected age (~{estimatedAge})");
                                }
                            }
                        }
                    }
                }
            }
            
            // Check for plot consistency
            foreach (var fact in _parsedRules.CriticalFacts)
            {
                // Simple contradiction check
                if (fact.Contains("married", StringComparison.OrdinalIgnoreCase))
                {
                    var marriageMatch = System.Text.RegularExpressions.Regex.Match(fact, @"(\w+)\s+(?:marries|married)\s+(\w+)");
                    if (marriageMatch.Success)
                    {
                        var person1 = marriageMatch.Groups[1].Value;
                        var person2 = marriageMatch.Groups[2].Value;
                        
                        // Check if content contradicts this
                        if (content.Contains($"{person1} and {person2} divorced", StringComparison.OrdinalIgnoreCase) ||
                            content.Contains($"{person1}'s wife", StringComparison.OrdinalIgnoreCase) && 
                            !content.Contains(person2, StringComparison.OrdinalIgnoreCase))
                        {
                            errors.AppendLine($"WARNING: Possible contradiction with established fact: {fact}");
                        }
                    }
                }
            }
            
            return errors.Length > 0 ? errors.ToString() : null;
        }
    }
} 