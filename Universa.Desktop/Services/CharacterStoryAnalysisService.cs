using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Universa.Desktop.Models;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for analyzing characters across story chapters using search-based discovery
    /// Integrates with CharacterDevelopmentChain for AI-powered character analysis
    /// </summary>
    public class CharacterStoryAnalysisService
    {
        private readonly ChapterSearchService _chapterSearchService;
        private readonly FileReferenceService _fileReferenceService;
        private readonly string _libraryPath;

        public CharacterStoryAnalysisService(
            ChapterSearchService chapterSearchService,
            FileReferenceService fileReferenceService,
            string libraryPath)
        {
            _chapterSearchService = chapterSearchService ?? throw new ArgumentNullException(nameof(chapterSearchService));
            _fileReferenceService = fileReferenceService ?? throw new ArgumentNullException(nameof(fileReferenceService));
            _libraryPath = libraryPath ?? throw new ArgumentNullException(nameof(libraryPath));
        }

        /// <summary>
        /// Analyzes a character across all referenced story files
        /// </summary>
        public async Task<CharacterStoryAnalysis> AnalyzeCharacterAcrossStoriesAsync(
            string characterName, 
            List<FileReference> storyReferences,
            int maxChaptersPerStory = 5)
        {
            var analysis = new CharacterStoryAnalysis
            {
                CharacterName = characterName,
                StoryAnalyses = new List<StoryAnalysis>()
            };

            Debug.WriteLine($"Starting character analysis for '{characterName}' across {storyReferences.Count} stories");

            foreach (var storyRef in storyReferences.Where(r => r.Type == FileReferenceType.Story))
            {
                try
                {
                    var storyContent = await _fileReferenceService.GetFileContent(storyRef.Path);
                    if (string.IsNullOrEmpty(storyContent))
                    {
                        Debug.WriteLine($"Failed to load story content from: {storyRef.Path}");
                        continue;
                    }

                    var storyAnalysis = await AnalyzeCharacterInStoryAsync(
                        characterName, 
                        storyContent, 
                        storyRef.Path, 
                        maxChaptersPerStory);

                    if (storyAnalysis.ChapterAnalyses.Any())
                    {
                        analysis.StoryAnalyses.Add(storyAnalysis);
                        Debug.WriteLine($"Found character in {storyAnalysis.ChapterAnalyses.Count} chapters of {storyRef.Path}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error analyzing story {storyRef.Path}: {ex.Message}");
                }
            }

            // Calculate overall insights
            analysis.OverallInsights = GenerateOverallInsights(analysis);

            Debug.WriteLine($"Character analysis complete: Found in {analysis.StoryAnalyses.Count} stories, {analysis.StoryAnalyses.Sum(s => s.ChapterAnalyses.Count)} total chapters");

            return analysis;
        }

        /// <summary>
        /// Analyzes a character within a specific story
        /// </summary>
        private async Task<StoryAnalysis> AnalyzeCharacterInStoryAsync(
            string characterName, 
            string storyContent, 
            string storyPath, 
            int maxChapters)
        {
            var storyAnalysis = new StoryAnalysis
            {
                StoryPath = storyPath,
                StoryTitle = Path.GetFileNameWithoutExtension(storyPath),
                ChapterAnalyses = new List<ChapterAnalysis>()
            };

            // Find character mentions across chapters
            var chapterMatches = await _chapterSearchService.FindCharacterInBookAsync(storyContent, characterName);

            // Take the most relevant chapters (up to maxChapters)
            var topChapters = chapterMatches
                .OrderByDescending(c => c.RelevanceScore)
                .Take(maxChapters)
                .ToList();

            Debug.WriteLine($"Processing top {topChapters.Count} chapters for character analysis in {storyPath}");

            foreach (var chapterMatch in topChapters)
            {
                var chapterAnalysis = new ChapterAnalysis
                {
                    ChapterIndex = chapterMatch.ChapterIndex,
                    ChapterTitle = chapterMatch.ChapterTitle,
                    MatchCount = chapterMatch.MatchCount,
                    RelevanceScore = chapterMatch.RelevanceScore,
                    CharacterMentions = chapterMatch.Matches.Select(m => new CharacterMentionSummary
                    {
                        Type = m.Type.ToString(),
                        Context = m.Context,
                        RelevanceScore = m.RelevanceScore
                    }).ToList(),
                    ChapterContent = chapterMatch.ChapterContent // Store for AI analysis
                };

                storyAnalysis.ChapterAnalyses.Add(chapterAnalysis);
            }

            return storyAnalysis;
        }

        /// <summary>
        /// Prepares chapter content for AI analysis by the CharacterDevelopmentChain
        /// </summary>
        public string PrepareChapterForAIAnalysis(ChapterAnalysis chapter, string characterName)
        {
            var context = new StringBuilder();
            
            context.AppendLine($"=== CHARACTER ANALYSIS REQUEST ===");
            context.AppendLine($"Character: {characterName}");
            context.AppendLine($"Chapter: {chapter.ChapterTitle}");
            context.AppendLine($"Relevance Score: {chapter.RelevanceScore:F2}");
            context.AppendLine($"Mention Count: {chapter.MatchCount}");
            context.AppendLine();
            
            context.AppendLine("=== CHARACTER MENTIONS IN THIS CHAPTER ===");
            foreach (var mention in chapter.CharacterMentions.OrderByDescending(m => m.RelevanceScore))
            {
                context.AppendLine($"• {mention.Type}: {mention.Context}");
            }
            context.AppendLine();
            
            context.AppendLine("=== FULL CHAPTER CONTENT ===");
            context.AppendLine(chapter.ChapterContent);
            
            return context.ToString();
        }

        /// <summary>
        /// Generates insights about character consistency across stories
        /// </summary>
        public string BuildCrossStoryConsistencyQuery(CharacterStoryAnalysis analysis)
        {
            var query = new StringBuilder();
            
            query.AppendLine($"=== CROSS-STORY CHARACTER CONSISTENCY ANALYSIS ===");
            query.AppendLine($"Character: {analysis.CharacterName}");
            query.AppendLine($"Stories Analyzed: {analysis.StoryAnalyses.Count}");
            query.AppendLine($"Total Chapters: {analysis.StoryAnalyses.Sum(s => s.ChapterAnalyses.Count)}");
            query.AppendLine();
            
            query.AppendLine("=== STORY BREAKDOWN ===");
            foreach (var story in analysis.StoryAnalyses)
            {
                query.AppendLine($"**{story.StoryTitle}**: {story.ChapterAnalyses.Count} chapters");
                
                // Highlight top mentions per story
                var topMentions = story.ChapterAnalyses
                    .SelectMany(c => c.CharacterMentions)
                    .OrderByDescending(m => m.RelevanceScore)
                    .Take(3)
                    .ToList();
                
                foreach (var mention in topMentions)
                {
                    query.AppendLine($"  - {mention.Type}: {mention.Context}");
                }
                query.AppendLine();
            }
            
            query.AppendLine("=== ANALYSIS REQUEST ===");
            query.AppendLine("Please analyze this character's consistency across these stories. Consider:");
            query.AppendLine("1. **Character Voice**: Are dialogue patterns consistent?");
            query.AppendLine("2. **Behavior Patterns**: Do actions align with established personality?");
            query.AppendLine("3. **Character Development**: How does the character grow/change across stories?");
            query.AppendLine("4. **Relationship Dynamics**: How do interactions with others remain consistent?");
            query.AppendLine("5. **Potential Inconsistencies**: Flag any contradictions or concerning variations");
            query.AppendLine();
            query.AppendLine("Focus on providing actionable feedback for maintaining character consistency in future writing.");
            
            return query.ToString();
        }

        /// <summary>
        /// Generates targeted character development questions based on story analysis
        /// </summary>
        public List<string> GenerateCharacterDevelopmentQuestions(CharacterStoryAnalysis analysis)
        {
            var questions = new List<string>();
            
            // Dialogue-focused questions
            var dialogueMentions = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .SelectMany(c => c.CharacterMentions)
                .Where(m => m.Type == "DialogueAttribution")
                .Count();
                
            if (dialogueMentions > 0)
            {
                questions.Add($"Analyze {analysis.CharacterName}'s dialogue patterns across these {dialogueMentions} dialogue instances. What speech patterns, vocabulary choices, and verbal mannerisms make this character distinctive?");
            }
            
            // Action-focused questions
            var actionMentions = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .SelectMany(c => c.CharacterMentions)
                .Where(m => m.Type == "ActionDescription")
                .Count();
                
            if (actionMentions > 0)
            {
                questions.Add($"Based on {actionMentions} action descriptions, what are {analysis.CharacterName}'s behavioral patterns? How do their actions reflect their personality and motivations?");
            }
            
            // Cross-story development
            if (analysis.StoryAnalyses.Count > 1)
            {
                questions.Add($"How does {analysis.CharacterName} develop and change across these {analysis.StoryAnalyses.Count} stories? What growth arc can you identify?");
                questions.Add($"Are there any inconsistencies in {analysis.CharacterName}'s characterization between stories that need to be addressed?");
            }
            
            // Relationship questions
            questions.Add($"Based on the context around {analysis.CharacterName}'s appearances, what can you infer about their relationships with other characters?");
            
            // Character depth questions
            questions.Add($"What gaps exist in {analysis.CharacterName}'s character development that could be expanded in future writing?");
            
            return questions;
        }

        /// <summary>
        /// Generates overall insights from the character analysis
        /// </summary>
        private CharacterInsights GenerateOverallInsights(CharacterStoryAnalysis analysis)
        {
            var insights = new CharacterInsights();
            
            // Calculate mention statistics
            var allMentions = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .SelectMany(c => c.CharacterMentions)
                .ToList();
            
            insights.TotalMentions = allMentions.Count;
            insights.DialogueMentions = allMentions.Count(m => m.Type == "DialogueAttribution");
            insights.ActionMentions = allMentions.Count(m => m.Type == "ActionDescription");
            insights.NameMentions = allMentions.Count(m => m.Type == "NameMention");
            insights.PossessiveMentions = allMentions.Count(m => m.Type == "PossessiveReference");
            
            insights.AverageRelevanceScore = allMentions.Any() ? allMentions.Average(m => m.RelevanceScore) : 0.0;
            
            // Identify most relevant chapters
            insights.TopChapters = analysis.StoryAnalyses
                .SelectMany(s => s.ChapterAnalyses)
                .OrderByDescending(c => c.RelevanceScore)
                .Take(5)
                .Select(c => $"{Path.GetFileNameWithoutExtension(c.ChapterTitle)} (Score: {c.RelevanceScore:F2})")
                .ToList();
            
            return insights;
        }
    }

    /// <summary>
    /// Represents a complete character analysis across multiple stories
    /// </summary>
    public class CharacterStoryAnalysis
    {
        public string CharacterName { get; set; }
        public List<StoryAnalysis> StoryAnalyses { get; set; } = new List<StoryAnalysis>();
        public CharacterInsights OverallInsights { get; set; }
    }

    /// <summary>
    /// Represents character analysis within a specific story
    /// </summary>
    public class StoryAnalysis
    {
        public string StoryPath { get; set; }
        public string StoryTitle { get; set; }
        public List<ChapterAnalysis> ChapterAnalyses { get; set; } = new List<ChapterAnalysis>();
    }

    /// <summary>
    /// Represents character analysis within a specific chapter
    /// </summary>
    public class ChapterAnalysis
    {
        public int ChapterIndex { get; set; }
        public string ChapterTitle { get; set; }
        public int MatchCount { get; set; }
        public double RelevanceScore { get; set; }
        public List<CharacterMentionSummary> CharacterMentions { get; set; } = new List<CharacterMentionSummary>();
        public string ChapterContent { get; set; }
    }

    /// <summary>
    /// Summary of a character mention for analysis purposes
    /// </summary>
    public class CharacterMentionSummary
    {
        public string Type { get; set; }
        public string Context { get; set; }
        public double RelevanceScore { get; set; }
    }

    /// <summary>
    /// Overall insights about a character across all stories
    /// </summary>
    public class CharacterInsights
    {
        public int TotalMentions { get; set; }
        public int DialogueMentions { get; set; }
        public int ActionMentions { get; set; }
        public int NameMentions { get; set; }
        public int PossessiveMentions { get; set; }
        public double AverageRelevanceScore { get; set; }
        public List<string> TopChapters { get; set; } = new List<string>();
    }
} 