using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Universa.Desktop.Services;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Builds enhanced prompts that combine parsed outline structure with full outline context
    /// </summary>
    public class OutlineEnhancedPromptBuilder
    {
        private readonly OutlineParser _outlineParser = new OutlineParser();
        
        /// <summary>
        /// Builds a prompt for generating the next chapter
        /// </summary>
        public string BuildNextChapterPrompt(
            string fullOutline, 
            string currentChapterText, 
            string previousChapterText,
            int currentChapterNumber)
        {
            var prompt = new StringBuilder();
            
            // Parse the outline for structured information
            var parsed = _outlineParser.Parse(fullOutline);
            
            // Add structured context first
            prompt.AppendLine("=== CHAPTER GENERATION CONTEXT ===");
            
            // Current chapter info from outline
            var targetChapter = parsed.Chapters.FirstOrDefault(c => c.Number == currentChapterNumber + 1);
            if (targetChapter != null)
            {
                prompt.AppendLine($"\nYou are writing Chapter {targetChapter.Number}: {targetChapter.Title}");
                prompt.AppendLine("\nKey elements for this chapter:");
                
                // Characters who should appear
                if (targetChapter.CharactersPresent.Any())
                {
                    prompt.AppendLine($"Characters: {string.Join(", ", targetChapter.CharactersPresent)}");
                }
                
                // Locations
                if (targetChapter.Locations.Any())
                {
                    prompt.AppendLine($"Locations: {string.Join(", ", targetChapter.Locations)}");
                }
                
                // Key events
                if (targetChapter.KeyEvents.Any())
                {
                    prompt.AppendLine("\nMajor events to include:");
                    foreach (var evt in targetChapter.KeyEvents)
                    {
                        prompt.AppendLine($"- {evt.Description}");
                    }
                }
                
                // POV if specified
                if (!string.IsNullOrEmpty(targetChapter.PointOfView))
                {
                    prompt.AppendLine($"\nPoint of View: {targetChapter.PointOfView}");
                }
            }
            
            // Add relevant character details
            var relevantCharacters = GetRelevantCharacters(parsed, targetChapter);
            if (relevantCharacters.Any())
            {
                prompt.AppendLine("\n=== CHARACTER DETAILS ===");
                foreach (var character in relevantCharacters)
                {
                    prompt.AppendLine($"\n{character.Name}:");
                    if (!string.IsNullOrEmpty(character.PhysicalDescription))
                    {
                        prompt.AppendLine($"Appearance: {character.PhysicalDescription.Split('\n')[0]}"); // First line only
                    }
                    if (!string.IsNullOrEmpty(character.PersonalityDescription))
                    {
                        prompt.AppendLine($"Personality: {character.PersonalityDescription.Split('\n')[0]}");
                    }
                }
            }
            
            // Add the full outline for complete context
            prompt.AppendLine("\n=== COMPLETE OUTLINE ===");
            prompt.AppendLine("Use this for full story context and detailed scene information:");
            prompt.AppendLine("**IMPORTANT**: Transform outline content into original narrative - never copy text directly.");
            prompt.AppendLine(fullOutline);
            
            // Add previous chapter for continuity
            if (!string.IsNullOrEmpty(previousChapterText))
            {
                prompt.AppendLine("\n=== PREVIOUS CHAPTER ===");
                prompt.AppendLine(previousChapterText);
            }
            
            // Add current chapter if revising
            if (!string.IsNullOrEmpty(currentChapterText))
            {
                prompt.AppendLine("\n=== CURRENT CHAPTER TEXT ===");
                prompt.AppendLine(currentChapterText);
            }
            
            prompt.AppendLine("\n=== INSTRUCTIONS ===");
            prompt.AppendLine("Generate the next chapter following the outline closely. Ensure you:");
            prompt.AppendLine("1. Include all key events listed for this chapter");
            prompt.AppendLine("2. Maintain consistency with previous chapters");
            prompt.AppendLine("3. Follow the character voices and personalities established");
            prompt.AppendLine("4. Match the style and pacing of previous chapters");
            prompt.AppendLine("5. ⚠️ CRITICAL: Transform outline objectives into completely original narrative prose - NEVER copy or expand outline text");
            prompt.AppendLine("6. 🎨 Create fresh dialogue, descriptions, and narrative flow that ACHIEVES outline goals without using outline language");
            
            return prompt.ToString();
        }
        
        /// <summary>
        /// Builds a prompt for checking if a chapter matches the outline
        /// </summary>
        public string BuildOutlineComplianceCheckPrompt(
            string fullOutline,
            string chapterText,
            int chapterNumber)
        {
            var prompt = new StringBuilder();
            var parsed = _outlineParser.Parse(fullOutline);
            
            var outlineChapter = parsed.Chapters.FirstOrDefault(c => c.Number == chapterNumber);
            if (outlineChapter == null)
            {
                return "Unable to find chapter in outline.";
            }
            
            prompt.AppendLine("=== OUTLINE COMPLIANCE CHECK ===");
            prompt.AppendLine($"\nChecking Chapter {chapterNumber}: {outlineChapter.Title}");
            
            prompt.AppendLine("\n=== EXPECTED ELEMENTS FROM OUTLINE ===");
            
            // List expected elements
            prompt.AppendLine("\nExpected Characters:");
            foreach (var character in outlineChapter.CharactersPresent)
            {
                prompt.AppendLine($"- {character}");
            }
            
            prompt.AppendLine("\nExpected Locations:");
            foreach (var location in outlineChapter.Locations)
            {
                prompt.AppendLine($"- {location}");
            }
            
            prompt.AppendLine("\nExpected Key Events:");
            foreach (var evt in outlineChapter.KeyEvents)
            {
                prompt.AppendLine($"- {evt.Description}");
            }
            
            if (outlineChapter.Scenes.Any())
            {
                prompt.AppendLine("\nExpected Scenes:");
                foreach (var scene in outlineChapter.Scenes)
                {
                    prompt.AppendLine($"- {scene.Title}");
                    if (scene.KeyActions.Any())
                    {
                        foreach (var action in scene.KeyActions)
                        {
                            prompt.AppendLine($"  • {action}");
                        }
                    }
                }
            }
            
            prompt.AppendLine("\n=== CHAPTER TEXT TO CHECK ===");
            prompt.AppendLine(chapterText);
            
            prompt.AppendLine("\n=== FULL OUTLINE FOR REFERENCE ===");
            prompt.AppendLine("**CRITICAL**: Use for structure only - create original prose, never copy outline text.");
            prompt.AppendLine(fullOutline);
            
            prompt.AppendLine("\n=== ANALYSIS REQUEST ===");
            prompt.AppendLine("Please analyze if the chapter text matches the outline by checking:");
            prompt.AppendLine("1. Are all expected characters present?");
            prompt.AppendLine("2. Are all expected locations used?");
            prompt.AppendLine("3. Are all key events included?");
            prompt.AppendLine("4. Does the chapter follow the scene structure?");
            prompt.AppendLine("5. Are there any significant deviations from the outline?");
            prompt.AppendLine("\nProvide specific examples of matches and any discrepancies found.");
            
            return prompt.ToString();
        }
        
        /// <summary>
        /// Builds a prompt for revising a chapter to match updated outline
        /// </summary>
        public string BuildRevisionPrompt(
            string fullOutline,
            string currentChapterText,
            int chapterNumber,
            string specificChanges = null)
        {
            var prompt = new StringBuilder();
            var parsed = _outlineParser.Parse(fullOutline);
            
            var outlineChapter = parsed.Chapters.FirstOrDefault(c => c.Number == chapterNumber);
            
            prompt.AppendLine("=== CHAPTER REVISION REQUEST ===");
            prompt.AppendLine($"\nRevising Chapter {chapterNumber} to match updated outline");
            
            if (!string.IsNullOrEmpty(specificChanges))
            {
                prompt.AppendLine("\n=== SPECIFIC CHANGES NOTED ===");
                prompt.AppendLine(specificChanges);
            }
            
            if (outlineChapter != null)
            {
                prompt.AppendLine("\n=== UPDATED OUTLINE REQUIREMENTS ===");
                prompt.AppendLine($"Chapter {outlineChapter.Number}: {outlineChapter.Title}");
                
                // Show what the outline now expects
                prompt.AppendLine("\nThis chapter should now include:");
                foreach (var evt in outlineChapter.KeyEvents)
                {
                    prompt.AppendLine($"- {evt.Description}");
                }
            }
            
            prompt.AppendLine("\n=== CURRENT CHAPTER TEXT ===");
            prompt.AppendLine(currentChapterText);
            
            prompt.AppendLine("\n=== COMPLETE UPDATED OUTLINE ===");
            prompt.AppendLine(fullOutline);
            
            prompt.AppendLine("\n=== REVISION INSTRUCTIONS ===");
            prompt.AppendLine("Revise the chapter to match the updated outline while:");
            prompt.AppendLine("1. Preserving as much of the original prose as possible");
            prompt.AppendLine("2. Smoothly integrating any new elements with original prose - never copy outline text");
            prompt.AppendLine("3. Removing any elements no longer in the outline");
            prompt.AppendLine("4. Maintaining consistent style and voice");
            prompt.AppendLine("5. Ensuring smooth transitions for any structural changes");
            prompt.AppendLine("6. Using outline updates as guidance only - create fresh narrative content");
            
            return prompt.ToString();
        }
        
        private List<OutlineParser.Character> GetRelevantCharacters(
            OutlineParser.ParsedOutline parsed, 
            OutlineParser.Chapter chapter)
        {
            if (chapter == null || !chapter.CharactersPresent.Any())
                return new List<OutlineParser.Character>();
            
            return parsed.Characters.Values
                .Where(c => chapter.CharactersPresent.Contains(c.Name))
                .ToList();
        }
    }
} 