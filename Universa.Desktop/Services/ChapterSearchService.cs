using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for searching character mentions within specific chapters of books
    /// Provides targeted search patterns for dialogue, actions, and relationships
    /// </summary>
    public class ChapterSearchService
    {
        private readonly ITextSearchService _textSearchService;

        public ChapterSearchService(ITextSearchService textSearchService)
        {
            _textSearchService = textSearchService ?? throw new ArgumentNullException(nameof(textSearchService));
        }

        /// <summary>
        /// Searches for character mentions across all chapters in a book
        /// </summary>
        public async Task<List<CharacterChapterMatch>> FindCharacterInBookAsync(string bookContent, string characterName)
        {
            var matches = new List<CharacterChapterMatch>();
            
            if (string.IsNullOrEmpty(bookContent) || string.IsNullOrEmpty(characterName))
                return matches;

            try
            {
                Debug.WriteLine($"Searching for character '{characterName}' in book content");
                
                // Get chapter boundaries using existing service
                var chapterBoundaries = ChapterDetectionService.GetChapterBoundaries(bookContent);
                var lines = bookContent.Split('\n');
                
                Debug.WriteLine($"Found {chapterBoundaries.Count} chapter boundaries");

                // Search each chapter
                for (int i = 0; i < chapterBoundaries.Count - 1; i++)
                {
                    var chapterStart = chapterBoundaries[i];
                    var chapterEnd = chapterBoundaries[i + 1];
                    
                    // Extract chapter content
                    var chapterLines = lines.Skip(chapterStart).Take(chapterEnd - chapterStart);
                    var chapterContent = string.Join("\n", chapterLines);
                    
                    // Get chapter title/number
                    var chapterTitle = ExtractChapterTitle(lines, chapterStart);
                    
                    // Search for character in this chapter
                    var characterMatches = await SearchCharacterInChapterAsync(chapterContent, characterName);
                    
                    if (characterMatches.Any())
                    {
                        matches.Add(new CharacterChapterMatch
                        {
                            ChapterIndex = i,
                            ChapterTitle = chapterTitle,
                            ChapterStartLine = chapterStart,
                            ChapterEndLine = chapterEnd,
                            ChapterContent = chapterContent,
                            MatchCount = characterMatches.Count,
                            Matches = characterMatches,
                            RelevanceScore = CalculateChapterRelevance(characterMatches, chapterContent)
                        });
                        
                        Debug.WriteLine($"Found {characterMatches.Count} matches in chapter: {chapterTitle}");
                    }
                }

                // Sort by relevance score (highest first)
                matches.Sort((a, b) => b.RelevanceScore.CompareTo(a.RelevanceScore));
                
                Debug.WriteLine($"Total: Found character in {matches.Count} chapters");
                return matches;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching for character in book: {ex.Message}");
                return matches;
            }
        }

        /// <summary>
        /// Searches for character mentions within a specific chapter
        /// </summary>
        private async Task<List<CharacterMention>> SearchCharacterInChapterAsync(string chapterContent, string characterName)
        {
            var mentions = new List<CharacterMention>();
            
            try
            {
                // Pattern 1: Direct name mentions
                var nameMatches = FindNameMentions(chapterContent, characterName);
                mentions.AddRange(nameMatches);
                
                // Pattern 2: Dialogue attribution
                var dialogueMatches = FindDialogueMentions(chapterContent, characterName);
                mentions.AddRange(dialogueMatches);
                
                // Pattern 3: Action descriptions
                var actionMatches = FindActionMentions(chapterContent, characterName);
                mentions.AddRange(actionMatches);
                
                // Pattern 4: Possessive references (Derek's, his/her when Derek is subject)
                var possessiveMatches = FindPossessiveMentions(chapterContent, characterName);
                mentions.AddRange(possessiveMatches);
                
                // Remove duplicates and sort by position
                mentions = mentions
                    .GroupBy(m => m.Position)
                    .Select(g => g.OrderByDescending(m => m.RelevanceScore).First())
                    .OrderBy(m => m.Position)
                    .ToList();
                
                return mentions;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error searching character in chapter: {ex.Message}");
                return mentions;
            }
        }

        /// <summary>
        /// Finds direct name mentions of the character
        /// </summary>
        private List<CharacterMention> FindNameMentions(string content, string characterName)
        {
            var mentions = new List<CharacterMention>();
            
            // Split character name to handle first/last names
            var nameParts = characterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var namePart in nameParts)
            {
                if (namePart.Length < 2) continue; // Skip very short name parts
                
                // Find word boundary matches for name parts
                var pattern = $@"\b{Regex.Escape(namePart)}\b";
                var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var context = ExtractContext(content, match.Index, 100);
                    
                    mentions.Add(new CharacterMention
                    {
                        Type = MentionType.NameMention,
                        Position = match.Index,
                        Text = match.Value,
                        Context = context,
                        RelevanceScore = CalculateNameMentionRelevance(namePart, characterName, context)
                    });
                }
            }
            
            return mentions;
        }

        /// <summary>
        /// Finds dialogue attribution (said Derek, Derek replied, etc.)
        /// </summary>
        private List<CharacterMention> FindDialogueMentions(string content, string characterName)
        {
            var mentions = new List<CharacterMention>();
            var nameParts = characterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var namePart in nameParts)
            {
                if (namePart.Length < 2) continue;
                
                // Pattern for dialogue attribution: "said Derek", "Derek said", etc.
                var patterns = new[]
                {
                    $@"""[^""]*""\s*,?\s*{Regex.Escape(namePart)}\s+(said|asked|replied|answered|whispered|shouted|declared|announced|muttered)",
                    $@"{Regex.Escape(namePart)}\s+(said|asked|replied|answered|whispered|shouted|declared|announced|muttered)\s*,?\s*""",
                    $@"""[^""]*""\s*,?\s*(said|asked|replied|answered|whispered|shouted|declared|announced|muttered)\s+{Regex.Escape(namePart)}"
                };
                
                foreach (var pattern in patterns)
                {
                    var matches = Regex.Matches(content, pattern, RegexOptions.IgnoreCase);
                    
                    foreach (Match match in matches)
                    {
                        var context = ExtractContext(content, match.Index, 150);
                        
                        mentions.Add(new CharacterMention
                        {
                            Type = MentionType.DialogueAttribution,
                            Position = match.Index,
                            Text = match.Value,
                            Context = context,
                            RelevanceScore = 0.9 // High relevance for dialogue
                        });
                    }
                }
            }
            
            return mentions;
        }

        /// <summary>
        /// Finds action descriptions involving the character
        /// </summary>
        private List<CharacterMention> FindActionMentions(string content, string characterName)
        {
            var mentions = new List<CharacterMention>();
            var nameParts = characterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var namePart in nameParts)
            {
                if (namePart.Length < 2) continue;
                
                // Pattern for common action verbs: "Derek walked", "Derek thought", etc.
                var actionWords = new[] { "walked", "ran", "looked", "turned", "smiled", "frowned", "nodded", "shook", "grabbed", "held", "moved", "stepped", "sat", "stood", "came", "went", "saw", "heard", "felt", "thought", "knew", "realized", "remembered", "wondered", "hoped", "feared", "wanted", "needed", "tried", "began", "started", "finished", "stopped", "continued", "decided", "chose", "picked", "took", "gave", "put", "placed", "opened", "closed", "pulled", "pushed", "lifted", "dropped", "threw", "caught", "hit", "touched", "reached", "pointed", "gestured", "laughed", "cried", "sighed", "breathed", "spoke", "whispered", "shouted", "called", "answered", "replied", "asked", "told", "explained", "described", "mentioned", "suggested", "offered", "refused", "agreed", "disagreed", "argued", "fought", "struggled", "worked", "played", "studied", "read", "wrote", "watched", "observed", "noticed", "discovered", "found", "lost", "searched", "explored", "traveled", "drove", "flew", "climbed", "jumped", "fell", "stumbled", "slipped", "tripped", "hurt", "helped", "saved", "protected", "attacked", "defended", "escaped", "hid", "revealed", "showed", "demonstrated", "proved", "tested", "checked", "examined", "investigated", "questioned", "doubted", "believed", "trusted", "loved", "hated", "liked", "disliked", "enjoyed", "suffered", "endured", "survived", "died", "lived", "existed", "appeared", "disappeared", "arrived", "left", "returned", "stayed", "remained", "waited", "hurried", "rushed", "approached", "followed", "led", "guided", "directed", "controlled", "managed" };
                var actionPattern = $@"\b{Regex.Escape(namePart)}\s+({string.Join("|", actionWords)})";
                
                var matches = Regex.Matches(content, actionPattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var context = ExtractContext(content, match.Index, 100);
                    
                    mentions.Add(new CharacterMention
                    {
                        Type = MentionType.ActionDescription,
                        Position = match.Index,
                        Text = match.Value,
                        Context = context,
                        RelevanceScore = 0.7 // Moderate relevance for actions
                    });
                }
            }
            
            return mentions;
        }

        /// <summary>
        /// Finds possessive references (Derek's, his/her when Derek is the subject)
        /// </summary>
        private List<CharacterMention> FindPossessiveMentions(string content, string characterName)
        {
            var mentions = new List<CharacterMention>();
            var nameParts = characterName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            foreach (var namePart in nameParts)
            {
                if (namePart.Length < 2) continue;
                
                // Pattern for possessives: "Derek's", "Derek's eyes", etc.
                var possessivePattern = $@"\b{Regex.Escape(namePart)}'s\s+\w+";
                var matches = Regex.Matches(content, possessivePattern, RegexOptions.IgnoreCase);
                
                foreach (Match match in matches)
                {
                    var context = ExtractContext(content, match.Index, 100);
                    
                    mentions.Add(new CharacterMention
                    {
                        Type = MentionType.PossessiveReference,
                        Position = match.Index,
                        Text = match.Value,
                        Context = context,
                        RelevanceScore = 0.6 // Lower relevance for possessives
                    });
                }
            }
            
            return mentions;
        }

        /// <summary>
        /// Extracts chapter title from lines array starting at given index
        /// </summary>
        private string ExtractChapterTitle(string[] lines, int startIndex)
        {
            if (startIndex >= lines.Length) return "Unknown Chapter";
            
            var line = lines[startIndex].Trim();
            
            // Remove markdown heading markers
            line = Regex.Replace(line, @"^#+\s*", "");
            
            return string.IsNullOrEmpty(line) ? "Unknown Chapter" : line;
        }

        /// <summary>
        /// Extracts context around a position in text
        /// </summary>
        private string ExtractContext(string content, int position, int radius)
        {
            int start = Math.Max(0, position - radius);
            int end = Math.Min(content.Length, position + radius);
            
            var context = content.Substring(start, end - start);
            
            // Add ellipsis if truncated
            if (start > 0) context = "..." + context;
            if (end < content.Length) context = context + "...";
            
            return context;
        }

        /// <summary>
        /// Calculates relevance score for a name mention
        /// </summary>
        private double CalculateNameMentionRelevance(string namePart, string fullName, string context)
        {
            double score = 0.5; // Base score
            
            // Higher score if it's the full name
            if (namePart.Equals(fullName, StringComparison.OrdinalIgnoreCase))
                score += 0.3;
            
            // Higher score if in dialogue context
            if (context.Contains("\""))
                score += 0.2;
            
            // Higher score if capitalized properly (likely a proper name usage)
            if (char.IsUpper(namePart[0]))
                score += 0.1;
            
            return Math.Min(1.0, score);
        }

        /// <summary>
        /// Calculates overall relevance score for a chapter based on character matches
        /// </summary>
        private double CalculateChapterRelevance(List<CharacterMention> matches, string chapterContent)
        {
            if (!matches.Any()) return 0.0;
            
            // Base score from average mention relevance
            double avgMentionScore = matches.Average(m => m.RelevanceScore);
            
            // Bonus for high mention count
            double countBonus = Math.Min(0.3, matches.Count * 0.05);
            
            // Bonus for dialogue mentions
            double dialogueBonus = matches.Count(m => m.Type == MentionType.DialogueAttribution) * 0.1;
            
            // Penalty for very long chapters (mentions might be diluted)
            double lengthPenalty = chapterContent.Length > 10000 ? 0.1 : 0.0;
            
            return Math.Min(1.0, avgMentionScore + countBonus + dialogueBonus - lengthPenalty);
        }
    }

    /// <summary>
    /// Represents a character match within a specific chapter
    /// </summary>
    public class CharacterChapterMatch
    {
        public int ChapterIndex { get; set; }
        public string ChapterTitle { get; set; }
        public int ChapterStartLine { get; set; }
        public int ChapterEndLine { get; set; }
        public string ChapterContent { get; set; }
        public int MatchCount { get; set; }
        public List<CharacterMention> Matches { get; set; } = new List<CharacterMention>();
        public double RelevanceScore { get; set; }
    }

    /// <summary>
    /// Represents a specific mention of a character within text
    /// </summary>
    public class CharacterMention
    {
        public MentionType Type { get; set; }
        public int Position { get; set; }
        public string Text { get; set; }
        public string Context { get; set; }
        public double RelevanceScore { get; set; }
    }

    /// <summary>
    /// Types of character mentions
    /// </summary>
    public enum MentionType
    {
        NameMention,
        DialogueAttribution,
        ActionDescription,
        PossessiveReference
    }
}