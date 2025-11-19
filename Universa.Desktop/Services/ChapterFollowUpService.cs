using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Service for adding follow-up questions after chapter generation
    /// </summary>
    public class ChapterFollowUpService
    {
        // Keywords that indicate chapter generation occurred
        private static readonly string[] CHAPTER_GENERATION_KEYWORDS = new[]
        {
            "## Chapter", "# Chapter", "Chapter \\d+", "new chapter", "chapter content"
        };

        // Keywords that indicate specific instructions were given
        private static readonly string[] INSTRUCTION_KEYWORDS = new[]
        {
            "write in the style of", "focus on", "emphasize", "include", "avoid",
            "tone should be", "perspective", "point of view", "mood", "pacing",
            "dialogue heavy", "action packed", "descriptive", "minimal dialogue",
            "first person", "third person", "present tense", "past tense"
        };

        /// <summary>
        /// Analyzes the AI response and adds a follow-up question if chapter generation was detected
        /// </summary>
        /// <param name="response">The AI response to analyze</param>
        /// <param name="originalRequest">The original user request that prompted this response</param>
        /// <returns>The potentially modified response with follow-up question</returns>
        public string ProcessResponse(string response, string originalRequest)
        {
            if (string.IsNullOrEmpty(response) || string.IsNullOrEmpty(originalRequest))
            {
                return response;
            }

            // Check if this looks like chapter generation
            if (!IsChapterGeneration(response))
            {
                return response;
            }

            // Extract the chapter number that was generated
            var chapterNumber = ExtractChapterNumber(response);
            if (!chapterNumber.HasValue)
            {
                return response;
            }

            // Extract any specific instructions from the original request
            var instructions = ExtractInstructions(originalRequest);

            // Build the follow-up question
            var followUpQuestion = BuildFollowUpQuestion(chapterNumber.Value, instructions);

            // Append the follow-up question to the response
            return AppendFollowUpQuestion(response, followUpQuestion);
        }

        /// <summary>
        /// Checks if the response contains chapter generation content
        /// </summary>
        private bool IsChapterGeneration(string response)
        {
            // Look for chapter headers or chapter-related content
            return CHAPTER_GENERATION_KEYWORDS.Any(keyword =>
                Regex.IsMatch(response, keyword, RegexOptions.IgnoreCase | RegexOptions.Multiline)) &&
                response.Length > 500; // Ensure it's substantial content, not just a title
        }

        /// <summary>
        /// Extracts the chapter number from the generated content
        /// </summary>
        private int? ExtractChapterNumber(string response)
        {
            // Look for chapter headers like "## Chapter 5" or "# Chapter 5"
            var chapterMatch = Regex.Match(response, @"^#+\s*Chapter\s+(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            if (chapterMatch.Success && int.TryParse(chapterMatch.Groups[1].Value, out int number))
            {
                return number;
            }

            // Also look for patterns like "Chapter 5:" in the content
            chapterMatch = Regex.Match(response, @"Chapter\s+(\d+)[:\-]", RegexOptions.IgnoreCase);
            if (chapterMatch.Success && int.TryParse(chapterMatch.Groups[1].Value, out number))
            {
                return number;
            }

            return null;
        }

        /// <summary>
        /// Extracts specific writing instructions from the original request
        /// </summary>
        private string ExtractInstructions(string originalRequest)
        {
            var instructions = new StringBuilder();
            var requestLower = originalRequest.ToLower();

            // Look for style instructions
            if (requestLower.Contains("write in the style of") || requestLower.Contains("style of"))
            {
                var styleMatch = Regex.Match(originalRequest, @"(?:write in the )?style of ([^,.]+)", RegexOptions.IgnoreCase);
                if (styleMatch.Success)
                {
                    instructions.AppendLine($"- Writing style: {styleMatch.Groups[1].Value.Trim()}");
                }
            }

            // Look for tone/mood instructions
            var tonePatterns = new[]
            {
                @"tone should be ([^,.]+)",
                @"(?:make it|write it) ([^,.]*(?:dramatic|comedic|serious|light|dark|humorous|tense|relaxed)[^,.]*)",
                @"focus on ([^,.]+)",
                @"emphasize ([^,.]+)"
            };

            foreach (var pattern in tonePatterns)
            {
                var match = Regex.Match(originalRequest, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    instructions.AppendLine($"- Approach: {match.Groups[1].Value.Trim()}");
                }
            }

            // Look for perspective/POV instructions
            if (requestLower.Contains("first person"))
            {
                instructions.AppendLine("- Perspective: First person");
            }
            else if (requestLower.Contains("third person"))
            {
                instructions.AppendLine("- Perspective: Third person");
            }

            // Look for dialogue/action instructions
            if (requestLower.Contains("dialogue heavy") || requestLower.Contains("lots of dialogue"))
            {
                instructions.AppendLine("- Content focus: Dialogue-heavy");
            }
            else if (requestLower.Contains("action packed") || requestLower.Contains("action-heavy"))
            {
                instructions.AppendLine("- Content focus: Action-heavy");
            }
            else if (requestLower.Contains("descriptive") || requestLower.Contains("detailed descriptions"))
            {
                instructions.AppendLine("- Content focus: Descriptive/atmospheric");
            }

            // Look for pacing instructions
            if (requestLower.Contains("fast paced") || requestLower.Contains("quick pacing"))
            {
                instructions.AppendLine("- Pacing: Fast-paced");
            }
            else if (requestLower.Contains("slow paced") || requestLower.Contains("deliberate pacing"))
            {
                instructions.AppendLine("- Pacing: Slower, deliberate");
            }

            return instructions.ToString();
        }

        /// <summary>
        /// Builds the follow-up question for the next chapter
        /// </summary>
        private string BuildFollowUpQuestion(int currentChapter, string instructions)
        {
            var nextChapter = currentChapter + 1;
            var question = new StringBuilder();

            question.AppendLine();
            question.AppendLine("---");
            question.AppendLine();
            question.AppendLine($"**Would you like me to generate Chapter {nextChapter} next?**");

            if (!string.IsNullOrEmpty(instructions))
            {
                question.AppendLine();
                question.AppendLine($"I can use the same approach I used for Chapter {currentChapter}:");
                question.Append(instructions);
            }
            else
            {
                question.AppendLine();
                question.AppendLine($"I can continue with the same style and approach I used for Chapter {currentChapter}.");
            }

            question.AppendLine();
            question.AppendLine("Just let me know if you'd like me to proceed with the next chapter, or if you'd like to make any adjustments to the writing approach!");

            return question.ToString();
        }

        /// <summary>
        /// Appends the follow-up question to the response
        /// </summary>
        private string AppendFollowUpQuestion(string response, string followUpQuestion)
        {
            // Ensure there's proper spacing
            var result = response.TrimEnd();
            if (!result.EndsWith("\n"))
            {
                result += "\n";
            }

            return result + followUpQuestion;
        }
    }
} 