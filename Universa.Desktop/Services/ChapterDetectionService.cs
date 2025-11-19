using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Unified service for detecting chapter boundaries and numbers in manuscript content
    /// Ensures consistent chapter detection across all Fiction Writing Beta components
    /// </summary>
    public static class ChapterDetectionService
    {
        // Comprehensive regex pattern for chapter detection
        // Matches: "## Chapter 1", "## Chapter 1: Title", "##Chapter 1", "##Chapter1", etc.
        private static readonly Regex ChapterPattern = new Regex(
            @"^##\s*Chapter\s*(\d+)(?:\s*[:]\s*.*)?$", 
            RegexOptions.IgnoreCase | RegexOptions.Compiled
        );

        // Additional patterns for other heading types (for content boundary detection)
        private static readonly List<Regex> BoundaryPatterns = new List<Regex>
        {
            // Main chapter pattern (numbered chapters only)
            new Regex(@"^##\s*Chapter\s*(\d+)(?:\s*[:]\s*.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            
            // Part/Section headings (numbered)
            new Regex(@"^##\s*(Part|Section)\s+(\d+)(?:\s*[:]\s*.*)?$", RegexOptions.IgnoreCase | RegexOptions.Compiled)
        };

        /// <summary>
        /// Detects the current chapter number based on cursor position in manuscript content
        /// </summary>
        /// <param name="manuscriptContent">The full manuscript content</param>
        /// <param name="cursorPosition">Current cursor position</param>
        /// <returns>The chapter number the cursor is currently in</returns>
        public static int GetCurrentChapterNumber(string manuscriptContent, int cursorPosition)
        {
            if (string.IsNullOrEmpty(manuscriptContent))
            {
                Debug.WriteLine("ChapterDetectionService: Empty manuscript content, returning chapter 1");
                return 1;
            }

            var lines = manuscriptContent.Split('\n');
            int currentPosition = 0;
            int currentChapterNumber = 1;

            // Skip #file: line if present
            int startIndex = 0;
            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#file:"))
            {
                Debug.WriteLine("ChapterDetectionService: Skipping #file: metadata line");
                startIndex = 1;
                currentPosition += lines[0].Length + 1;
            }

            // Find chapters at or before cursor position
            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Check if this line contains a chapter marker
                var chapterMatch = ChapterPattern.Match(line);
                if (chapterMatch.Success)
                {
                    if (int.TryParse(chapterMatch.Groups[1].Value, out int chapterNum))
                    {
                        // Only update chapter number if this chapter starts at or before cursor
                        if (currentPosition <= cursorPosition)
                        {
                            currentChapterNumber = chapterNum;
                            Debug.WriteLine($"ChapterDetectionService: Found Chapter {chapterNum} at line {i}, position {currentPosition}");
                        }
                        else
                        {
                            // We've gone past the cursor position
                            Debug.WriteLine($"ChapterDetectionService: Chapter {chapterNum} at line {i} is beyond cursor position {cursorPosition}, stopping");
                            break;
                        }
                    }
                }

                currentPosition += lines[i].Length + 1;
            }

            Debug.WriteLine($"ChapterDetectionService: Current chapter number determined: {currentChapterNumber} for cursor position {cursorPosition}");
            return currentChapterNumber;
        }

        /// <summary>
        /// Finds all chapter boundaries in the manuscript content for context building
        /// </summary>
        /// <param name="manuscriptContent">The full manuscript content</param>
        /// <returns>List of line indices where chapters/boundaries begin</returns>
        public static List<int> GetChapterBoundaries(string manuscriptContent)
        {
            if (string.IsNullOrEmpty(manuscriptContent))
            {
                Debug.WriteLine("ChapterDetectionService: Empty content for boundary detection");
                return new List<int> { 0 };
            }

            var lines = manuscriptContent.Split('\n');
            var boundaries = new List<int>();

            // Skip #file: line if present
            int startIndex = 0;
            if (lines.Length > 0 && lines[0].TrimStart().StartsWith("#file:"))
            {
                startIndex = 1;
            }

            // Always include the document start as first boundary
            boundaries.Add(startIndex);

            for (int i = startIndex; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                
                // Check against all boundary patterns
                bool isBoundary = false;
                
                foreach (var pattern in BoundaryPatterns)
                {
                    if (pattern.IsMatch(line))
                    {
                        // Additional checks to avoid false positives
                        if (IsValidChapterBoundary(lines, i, line))
                        {
                            boundaries.Add(i);
                            Debug.WriteLine($"ChapterDetectionService: Found boundary at line {i}: '{line}'");
                            isBoundary = true;
                            break;
                        }
                    }
                }

                // Log when we skip potential boundaries
                if (!isBoundary && (line.StartsWith("#") || Regex.IsMatch(line, @"^\d+[\.\)]")))
                {
                    Debug.WriteLine($"ChapterDetectionService: Skipped potential boundary at line {i}: '{line}'");
                }
            }

            // Add end of document as final boundary if not already included
            if (!boundaries.Contains(lines.Length))
            {
                boundaries.Add(lines.Length);
            }

            Debug.WriteLine($"ChapterDetectionService: Found {boundaries.Count} total boundaries");
            return boundaries.Distinct().OrderBy(b => b).ToList();
        }

        /// <summary>
        /// Validates if a line should be considered a chapter boundary
        /// </summary>
        private static bool IsValidChapterBoundary(string[] lines, int lineIndex, string line)
        {
            // Skip level 1 headings (these are typically titles)
            if (line.StartsWith("# ", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("## "))
            {
                return false;
            }

            // Skip level 3+ headings (these are subsections)
            if (line.StartsWith("### "))
            {
                return false;
            }

            // For numbered items (1., 2.), check if it's part of a list
            if (Regex.IsMatch(line, @"^\d+[\.\)]"))
            {
                return !IsPartOfNumberedList(lines, lineIndex);
            }

            return true;
        }

        /// <summary>
        /// Checks if a numbered line is part of a numbered list rather than a chapter heading
        /// </summary>
        private static bool IsPartOfNumberedList(string[] lines, int lineIndex)
        {
            // Check previous line
            if (lineIndex > 0)
            {
                var prevLine = lines[lineIndex - 1].Trim();
                if (Regex.IsMatch(prevLine, @"^\d+[\.\)]"))
                {
                    return true;
                }
            }

            // Check next line
            if (lineIndex < lines.Length - 1)
            {
                var nextLine = lines[lineIndex + 1].Trim();
                if (Regex.IsMatch(nextLine, @"^\d+[\.\)]"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Extracts chapter number from a line if it contains a chapter marker
        /// </summary>
        /// <param name="line">The line to check</param>
        /// <returns>Chapter number if found, null otherwise</returns>
        public static int? ExtractChapterNumber(string line)
        {
            if (string.IsNullOrEmpty(line))
                return null;

            var match = ChapterPattern.Match(line.Trim());
            if (match.Success && int.TryParse(match.Groups[1].Value, out int chapterNum))
            {
                return chapterNum;
            }

            return null;
        }

        /// <summary>
        /// Checks if a line contains a chapter marker
        /// </summary>
        /// <param name="line">The line to check</param>
        /// <returns>True if the line is a chapter heading</returns>
        public static bool IsChapterHeading(string line)
        {
            return !string.IsNullOrEmpty(line) && ChapterPattern.IsMatch(line.Trim());
        }
    }
} 