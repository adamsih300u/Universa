using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Services
{
    public class OutlineParser
    {
        public class ParsedOutline
        {
            public Dictionary<string, OutlineSection> Sections { get; set; } = new Dictionary<string, OutlineSection>();
            public List<Chapter> Chapters { get; set; } = new List<Chapter>();
            public Dictionary<string, Character> Characters { get; set; } = new Dictionary<string, Character>();
            public List<ThematicElement> Themes { get; set; } = new List<ThematicElement>();
            public List<PlotPoint> MajorPlotPoints { get; set; } = new List<PlotPoint>();
            public Dictionary<string, Location> Locations { get; set; } = new Dictionary<string, Location>();
            public List<string> TechnicalDetails { get; set; } = new List<string>();
            public ChapterSequence ChapterFlow { get; set; } = new ChapterSequence();
        }

        public class OutlineSection
        {
            public string Title { get; set; }
            public string Content { get; set; }
            public SectionType Type { get; set; }
            public List<string> KeyPoints { get; set; } = new List<string>();
        }

        public class Chapter
        {
            public int Number { get; set; }
            public string Title { get; set; }
            public List<Scene> Scenes { get; set; } = new List<Scene>();
            public List<string> CharactersPresent { get; set; } = new List<string>();
            public List<string> Locations { get; set; } = new List<string>();
            public string PointOfView { get; set; }
            public List<PlotPoint> KeyEvents { get; set; } = new List<PlotPoint>();
            public string Summary { get; set; }
        }

        public class Scene
        {
            public string Title { get; set; }
            public string Description { get; set; }
            public List<string> Characters { get; set; } = new List<string>();
            public string Location { get; set; }
            public string Focus { get; set; }
            public List<string> KeyActions { get; set; } = new List<string>();
        }

        public class Character
        {
            public string Name { get; set; }
            public string FullName { get; set; }
            public CharacterRole Role { get; set; }
            public string PhysicalDescription { get; set; }
            public string PersonalityDescription { get; set; }
            public string Background { get; set; }
            public List<string> AppearingInChapters { get; set; } = new List<string>();
            public Dictionary<string, string> Relationships { get; set; } = new Dictionary<string, string>();
            public bool IsBookSpecific { get; set; }
        }

        public class ThematicElement
        {
            public string Theme { get; set; }
            public string Description { get; set; }
            public List<string> RelevantChapters { get; set; } = new List<string>();
        }

        public class PlotPoint
        {
            public string Description { get; set; }
            public int ChapterNumber { get; set; }
            public string ChapterTitle { get; set; }
            public PlotImportance Importance { get; set; }
            public List<string> CharactersInvolved { get; set; } = new List<string>();
            public string Consequence { get; set; }
        }

        public class Location
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<int> AppearsInChapters { get; set; } = new List<int>();
            public string Significance { get; set; }
        }

        public class ChapterSequence
        {
            public List<ChapterConnection> Connections { get; set; } = new List<ChapterConnection>();
            public Dictionary<int, List<string>> ChapterDependencies { get; set; } = new Dictionary<int, List<string>>();
            public List<string> RecurringElements { get; set; } = new List<string>();
        }

        public class ChapterConnection
        {
            public int FromChapter { get; set; }
            public int ToChapter { get; set; }
            public string ConnectionType { get; set; } // "Direct continuation", "Time skip", "Parallel action", etc.
            public string Description { get; set; }
        }

        public enum SectionType
        {
            ThematicNotes,
            Characters,
            Introduction,
            Chapter,
            Scene,
            TechnicalNotes,
            Background
        }

        public enum CharacterRole
        {
            Protagonist,
            Antagonist,
            Supporting,
            Minor,
            BookSpecific
        }

        public enum PlotImportance
        {
            Critical,
            Major,
            Supporting,
            Minor
        }

        public ParsedOutline Parse(string outlineContent)
        {
            var result = new ParsedOutline();
            
            // Parse sections first
            var sections = ParseSections(outlineContent);
            
            System.Diagnostics.Debug.WriteLine($"OutlineParser: Found {sections.Count} sections");
            
            foreach (var section in sections)
            {
                var parsedSection = ParseSection(section);
                
                System.Diagnostics.Debug.WriteLine($"OutlineParser: Section Title='{parsedSection.Title}', Type={parsedSection.Type}");
                
                if (parsedSection.Type == SectionType.Chapter)
                {
                    // Check if this is an "Outline" section containing multiple chapters
                    if (parsedSection.Title.Equals("Outline", StringComparison.OrdinalIgnoreCase))
                    {
                        // Split the outline section into individual chapters
                        var individualChapters = SplitOutlineIntoChapters(section);
                        foreach (var chapterSection in individualChapters)
                        {
                            var chapter = ParseChapterDetails(chapterSection);
                            if (chapter != null)
                            {
                                result.Chapters.Add(chapter);
                            }
                        }
                    }
                    else
                    {
                        // Handle single chapter sections
                        var chapter = ParseChapterDetails(section);
                        if (chapter != null)
                        {
                            result.Chapters.Add(chapter);
                        }
                    }
                }
                else
                {
                    result.Sections[parsedSection.Title] = parsedSection;
                    
                    // Process specific section types
                    switch (parsedSection.Type)
                    {
                        case SectionType.Characters:
                            System.Diagnostics.Debug.WriteLine($"OutlineParser: Parsing Characters section");
                            ParseCharacters(section, result.Characters);
                            System.Diagnostics.Debug.WriteLine($"OutlineParser: Found {result.Characters.Count} characters");
                            break;
                        case SectionType.ThematicNotes:
                            ParseThemes(section, result.Themes);
                            break;
                        case SectionType.TechnicalNotes:
                            ParseTechnicalDetails(section, result.TechnicalDetails);
                            break;
                    }
                }
            }
            
            // Extract major plot points from chapters
            result.MajorPlotPoints = ExtractMajorPlotPoints(result.Chapters);
            
            // Build chapter sequence and connections
            result.ChapterFlow = BuildChapterSequence(result.Chapters);
            
            // Extract locations
            result.Locations = ExtractLocations(outlineContent, result.Chapters);
            
            System.Diagnostics.Debug.WriteLine($"OutlineParser: Parse complete - {result.Chapters.Count} chapters, {result.Characters.Count} characters");
            
            return result;
        }

        private List<string> ParseSections(string content)
        {
            var sections = new List<string>();
            
            // Split by major headers (# only, not ##)
            var lines = content.Split('\n');
            var currentSection = new StringBuilder();
            var currentLevel = 0;
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.+)");
                
                if (headerMatch.Success)
                {
                    var level = headerMatch.Groups[1].Value.Length;
                    
                    // Start new section if we hit a level 1 header (#)
                    if (level == 1 && currentSection.Length > 0)
                    {
                        sections.Add(currentSection.ToString());
                        currentSection.Clear();
                    }
                }
                
                currentSection.AppendLine(line);
            }
            
            // Add the last section
            if (currentSection.Length > 0)
            {
                sections.Add(currentSection.ToString());
            }
            
            return sections;
        }

        private OutlineSection ParseSection(string sectionContent)
        {
            var section = new OutlineSection();
            var lines = sectionContent.Split('\n');
            
            // Handle empty sections
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                section.Title = "Untitled Section";
                section.Content = sectionContent;
                section.Type = SectionType.Background;
                section.KeyPoints = new List<string>();
                return section;
            }
            
            // Extract title from header
            var headerMatch = Regex.Match(lines[0], @"^#{1,6}\s+(.+)");
            if (headerMatch.Success)
            {
                section.Title = headerMatch.Groups[1].Value.Trim();
            }
            else
            {
                // Try to match bracketed headers like [Characters for this book]
                var bracketMatch = Regex.Match(lines[0], @"^\[(.+)\]");
                if (bracketMatch.Success)
                {
                    section.Title = bracketMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Provide a default title if no header is found
                    section.Title = "Untitled Section";
                }
            }
            
            section.Content = sectionContent;
            section.Type = DetermineSectionType(section.Title);
            section.KeyPoints = ExtractKeyPoints(sectionContent);
            
            return section;
        }

        private List<string> SplitOutlineIntoChapters(string outlineSection)
        {
            var chapters = new List<string>();
            var lines = outlineSection.Split('\n');
            var currentChapter = new StringBuilder();
            bool inChapter = false;
            
            foreach (var line in lines)
            {
                // Check for chapter headers (## Chapter X)
                if (Regex.IsMatch(line, @"^##\s+Chapter\s+\d+", RegexOptions.IgnoreCase))
                {
                    // If we were already in a chapter, save it
                    if (inChapter && currentChapter.Length > 0)
                    {
                        chapters.Add(currentChapter.ToString());
                        currentChapter.Clear();
                    }
                    
                    // Start new chapter
                    currentChapter.AppendLine(line);
                    inChapter = true;
                }
                else if (inChapter)
                {
                    // Add line to current chapter
                    currentChapter.AppendLine(line);
                }
                // Skip lines before the first chapter (like "# Outline" header)
            }
            
            // Add the last chapter if exists
            if (inChapter && currentChapter.Length > 0)
            {
                chapters.Add(currentChapter.ToString());
            }
            
            System.Diagnostics.Debug.WriteLine($"SplitOutlineIntoChapters: Split outline into {chapters.Count} individual chapters");
            
            return chapters;
        }

        private Chapter ParseChapterDetails(string chapterContent)
        {
            var chapter = new Chapter();
            var lines = chapterContent.Split('\n');
            
            // Extract chapter number and title
            var headerMatch = Regex.Match(lines[0], @"Chapter\s+(\d+)(?:\s*[-:]\s*(.+))?", RegexOptions.IgnoreCase);
            if (!headerMatch.Success)
            {
                // Try alternative format
                headerMatch = Regex.Match(lines[0], @"##\s+Chapter\s+(\d+)(?:\s*[-:]\s*(.+))?", RegexOptions.IgnoreCase);
            }
            
            if (headerMatch.Success)
            {
                chapter.Number = int.Parse(headerMatch.Groups[1].Value);
                if (headerMatch.Groups[2].Success)
                {
                    chapter.Title = headerMatch.Groups[2].Value.Trim();
                }
            }
            
            // Parse scenes and content
            var currentScene = new Scene();
            var inScene = false;
            var contentBuilder = new StringBuilder();
            
            foreach (var line in lines.Skip(1))
            {
                // Check for scene headers (###)
                var sceneMatch = Regex.Match(line, @"^###\s+(.+)");
                if (sceneMatch.Success)
                {
                    if (inScene && currentScene.Title != null)
                    {
                        chapter.Scenes.Add(currentScene);
                    }
                    
                    currentScene = new Scene
                    {
                        Title = sceneMatch.Groups[1].Value.Trim()
                    };
                    inScene = true;
                    continue;
                }
                
                // Check for POV/Focus indicators
                var focusMatch = Regex.Match(line, @"(?:Focus|POV):\s*(.+)", RegexOptions.IgnoreCase);
                if (focusMatch.Success)
                {
                    var focus = focusMatch.Groups[1].Value.Trim();
                    if (inScene)
                    {
                        currentScene.Focus = focus;
                    }
                    else
                    {
                        chapter.PointOfView = focus;
                    }
                }
                
                // Extract character mentions
                ExtractCharacterMentions(line, chapter.CharactersPresent);
                
                // Extract locations
                ExtractLocationMentions(line, chapter.Locations);
                
                // Build content
                if (!string.IsNullOrWhiteSpace(line))
                {
                    contentBuilder.AppendLine(line);
                    
                    if (inScene)
                    {
                        if (line.StartsWith("-") || line.StartsWith("•"))
                        {
                            currentScene.KeyActions.Add(line.TrimStart('-', '•').Trim());
                        }
                    }
                }
            }
            
            // Add final scene if exists
            if (inScene && currentScene.Title != null)
            {
                chapter.Scenes.Add(currentScene);
            }
            
            chapter.Summary = contentBuilder.ToString();
            
            // Extract key events
            chapter.KeyEvents = ExtractKeyEvents(chapter.Summary, chapter.Number, chapter.Title);
            
            return chapter;
        }

        private void ParseCharacters(string section, Dictionary<string, Character> characters)
        {
            var lines = section.Split('\n');
            Character currentCharacter = null;
            var inDescription = false;
            var descriptionType = "";
            var descriptionBuilder = new StringBuilder();
            CharacterRole currentRole = CharacterRole.Supporting;
            
            foreach (var line in lines)
            {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                    
                // Check for role headers
                if (line.Trim() == "- Protagonists" || line.Contains("Protagonists"))
                {
                    currentRole = CharacterRole.Protagonist;
                    continue;
                }
                else if (line.Trim() == "- Antagonists" || line.Contains("Antagonists"))
                {
                    currentRole = CharacterRole.Antagonist;
                    continue;
                }
                else if (line.Contains("Supporting Characters"))
                {
                    currentRole = CharacterRole.Supporting;
                    continue;
                }
                else if (line.Contains("Characters Specific to this Book"))
                {
                    // Section header, skip
                    continue;
                }
                
                // Check for character description headers
                var descMatch = Regex.Match(line, @"^\[(.+)\]$");
                if (descMatch.Success)
                {
                    if (currentCharacter != null && inDescription && descriptionBuilder.Length > 0)
                    {
                        SetCharacterDescription(currentCharacter, descriptionType, descriptionBuilder.ToString());
                    }
                    
                    inDescription = true;
                    descriptionType = descMatch.Groups[1].Value;
                    descriptionBuilder.Clear();
                    continue;
                }
                
                // New character detection - handle indented bullets
                var trimmedLine = line.TrimStart();
                if (trimmedLine.StartsWith("- ") && !inDescription)
                {
                    // Save previous character
                    if (currentCharacter != null)
                    {
                        if (inDescription && descriptionBuilder.Length > 0)
                        {
                            SetCharacterDescription(currentCharacter, descriptionType, descriptionBuilder.ToString());
                        }
                        if (!string.IsNullOrEmpty(currentCharacter.Name))
                        {
                            characters[currentCharacter.Name] = currentCharacter;
                        }
                    }
                    
                    // Extract character info from the line
                    var characterLine = trimmedLine.Substring(2).Trim(); // Remove "- "
                    
                    // Split by " - " to separate name from description
                    var parts = characterLine.Split(new[] { " - " }, 2, StringSplitOptions.None);
                    
                    // Extract just the character name, handling titles and complex names
                    var namePart = parts[0].Trim();
                    
                    // Check if there's a comma indicating title/position (e.g., "Vic Kaplan, head of Kaplan Designs")
                    var commaIndex = namePart.IndexOf(',');
                    var actualName = commaIndex > 0 ? namePart.Substring(0, commaIndex).Trim() : namePart;
                    
                    // Handle special medical titles
                    actualName = actualName.Replace("Dr. ", "").Replace("MD", "").Replace("MBA", "").Replace("JD", "").Trim();
                    
                    currentCharacter = new Character
                    {
                        Name = actualName,
                        FullName = namePart, // Keep the full name with titles
                        Role = currentRole
                    };
                    
                    // If there's a description part, add it as background
                    if (parts.Length > 1)
                    {
                        currentCharacter.Background = parts[1].Trim();
                    }
                    
                    inDescription = false;
                    descriptionBuilder.Clear();
                }
                else if (inDescription && currentCharacter != null)
                {
                    descriptionBuilder.AppendLine(line);
                }
            }
            
            // Save last character
            if (currentCharacter != null)
            {
                if (inDescription && descriptionBuilder.Length > 0)
                {
                    SetCharacterDescription(currentCharacter, descriptionType, descriptionBuilder.ToString());
                }
                if (!string.IsNullOrEmpty(currentCharacter.Name))
                {
                    characters[currentCharacter.Name] = currentCharacter;
                }
            }
        }

        private void SetCharacterDescription(Character character, string descType, string description)
        {
            switch (descType.ToLower())
            {
                case "physical description":
                    character.PhysicalDescription = description.Trim();
                    break;
                case "personality & management style":
                case "personality":
                    character.PersonalityDescription = description.Trim();
                    break;
                case "background":
                    character.Background = description.Trim();
                    break;
                case "role description":
                case "story function":
                    if (string.IsNullOrEmpty(character.Background))
                        character.Background = description.Trim();
                    else
                        character.Background += "\n\n" + description.Trim();
                    break;
            }
        }

        private void ParseThemes(string section, List<ThematicElement> themes)
        {
            var lines = section.Split('\n');
            
            foreach (var line in lines)
            {
                if (line.StartsWith("-") || line.StartsWith("•"))
                {
                    var theme = new ThematicElement
                    {
                        Theme = ExtractThemeName(line),
                        Description = line.TrimStart('-', '•').Trim()
                    };
                    themes.Add(theme);
                }
            }
        }

        private void ParseTechnicalDetails(string section, List<string> technicalDetails)
        {
            var lines = section.Split('\n');
            
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                {
                    technicalDetails.Add(line.Trim());
                }
            }
        }

        private List<PlotPoint> ExtractMajorPlotPoints(List<Chapter> chapters)
        {
            var plotPoints = new List<PlotPoint>();
            
            foreach (var chapter in chapters)
            {
                foreach (var evt in chapter.KeyEvents)
                {
                    // Determine importance based on keywords
                    if (IsImportantEvent(evt.Description))
                    {
                        evt.Importance = PlotImportance.Critical;
                        plotPoints.Add(evt);
                    }
                    else if (IsMajorEvent(evt.Description))
                    {
                        evt.Importance = PlotImportance.Major;
                        plotPoints.Add(evt);
                    }
                }
            }
            
            return plotPoints.OrderBy(p => p.ChapterNumber).ToList();
        }

        private ChapterSequence BuildChapterSequence(List<Chapter> chapters)
        {
            var sequence = new ChapterSequence();
            
            // Build connections
            for (int i = 0; i < chapters.Count - 1; i++)
            {
                var connection = new ChapterConnection
                {
                    FromChapter = chapters[i].Number,
                    ToChapter = chapters[i + 1].Number,
                    ConnectionType = DetermineConnectionType(chapters[i], chapters[i + 1])
                };
                
                sequence.Connections.Add(connection);
            }
            
            // Identify recurring elements
            var allElements = new Dictionary<string, int>();
            foreach (var chapter in chapters)
            {
                foreach (var character in chapter.CharactersPresent)
                {
                    if (!allElements.ContainsKey(character))
                        allElements[character] = 0;
                    allElements[character]++;
                }
            }
            
            sequence.RecurringElements = allElements
                .Where(e => e.Value > 2)
                .Select(e => e.Key)
                .ToList();
            
            return sequence;
        }

        private Dictionary<string, Location> ExtractLocations(string content, List<Chapter> chapters)
        {
            var locations = new Dictionary<string, Location>();
            
            // Common location patterns
            var locationPatterns = new[]
            {
                @"(?:in|at|on|aboard)\s+(?:the\s+)?([A-Z][a-zA-Z\s]+(?:ship|vessel|helicopter|center|room|deck|hold|locker))",
                @"([A-Z][a-zA-Z\s]+(?:Pentagon|Oslo|Africa|Washington))"
            };
            
            foreach (var pattern in locationPatterns)
            {
                var matches = Regex.Matches(content, pattern);
                foreach (Match match in matches)
                {
                    var locationName = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(locationName) && !locations.ContainsKey(locationName) && IsValidLocation(locationName))
                    {
                        locations[locationName] = new Location
                        {
                            Name = locationName,
                            AppearsInChapters = FindChaptersWithLocation(locationName, chapters)
                        };
                    }
                }
            }
            
            return locations;
        }

        // Helper methods
        private SectionType DetermineSectionType(string title)
        {
            // Handle null or empty titles
            if (string.IsNullOrEmpty(title))
                return SectionType.Background;
                
            if (title.Contains("Chapter", StringComparison.OrdinalIgnoreCase))
                return SectionType.Chapter;
            if (title.Equals("Outline", StringComparison.OrdinalIgnoreCase))
                return SectionType.Chapter; // Treat "Outline" sections as containing chapters
            if (title.Contains("Thematic", StringComparison.OrdinalIgnoreCase))
                return SectionType.ThematicNotes;
            if (title.Contains("Character", StringComparison.OrdinalIgnoreCase) || 
                title.Equals("Characters", StringComparison.OrdinalIgnoreCase))
                return SectionType.Characters;
            if (title.Contains("Introduction", StringComparison.OrdinalIgnoreCase))
                return SectionType.Introduction;
            if (title.Contains("Technical", StringComparison.OrdinalIgnoreCase))
                return SectionType.TechnicalNotes;
                
            return SectionType.Background;
        }

        private List<string> ExtractKeyPoints(string content)
        {
            var keyPoints = new List<string>();
            var lines = content.Split('\n');
            
            foreach (var line in lines)
            {
                if ((line.StartsWith("-") || line.StartsWith("•")) && line.Length > 10)
                {
                    keyPoints.Add(line.TrimStart('-', '•').Trim());
                }
            }
            
            return keyPoints;
        }

        private void ExtractCharacterMentions(string line, List<string> characters)
        {
            // Look for common character name patterns
            var namePattern = @"\b([A-Z][a-z]+(?:\s+[A-Z][a-z]+)?)\b";
            var matches = Regex.Matches(line, namePattern);
            
            foreach (Match match in matches)
            {
                var name = match.Groups[1].Value;
                if (IsCharacterName(name) && !characters.Contains(name))
                {
                    characters.Add(name);
                }
            }
        }

        private void ExtractLocationMentions(string line, List<string> locations)
        {
            // Look for location indicators
            var locationWords = new[] { "at", "in", "on", "aboard", "near", "toward", "from" };
            foreach (var word in locationWords)
            {
                var pattern = $@"\b{word}\s+(?:the\s+)?([A-Z][a-zA-Z\s]+)";
                var match = Regex.Match(line, pattern);
                if (match.Success)
                {
                    var location = match.Groups[1].Value.Trim();
                    if (IsValidLocation(location) && !locations.Contains(location))
                    {
                        locations.Add(location);
                    }
                }
            }
        }

        private List<PlotPoint> ExtractKeyEvents(string content, int chapterNumber, string chapterTitle)
        {
            var events = new List<PlotPoint>();
            var lines = content.Split('\n');
            
            foreach (var line in lines)
            {
                if (IsPotentialPlotPoint(line))
                {
                    events.Add(new PlotPoint
                    {
                        Description = line.Trim(),
                        ChapterNumber = chapterNumber,
                        ChapterTitle = chapterTitle,
                        Importance = PlotImportance.Supporting
                    });
                }
            }
            
            return events;
        }

        private bool IsCharacterName(string text)
        {
            // Common character names from context
            var knownNames = new[] { "Napier", "Thomas", "Trent", "Olstad", "Kruger", "Mosby", "Sinclair", "Pembroke" };
            return knownNames.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase)) ||
                   (text.Split(' ').Length <= 3 && Regex.IsMatch(text, @"^[A-Z][a-z]+"));
        }

        private bool IsValidLocation(string text)
        {
            return text.Length > 3 && 
                   text.Length < 50 && 
                   !text.Equals("The", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsPotentialPlotPoint(string line)
        {
            var plotKeywords = new[] { "discovers", "reveals", "attacks", "escapes", "dies", "saves", 
                                      "launches", "activates", "destroys", "captures", "rescues" };
            return plotKeywords.Any(keyword => line.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsImportantEvent(string description)
        {
            var criticalKeywords = new[] { "nuclear", "dies", "killed", "explodes", "launches missile", 
                                          "reactor", "warhead", "hijack" };
            return criticalKeywords.Any(keyword => description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsMajorEvent(string description)
        {
            var majorKeywords = new[] { "escapes", "captures", "discovers", "arrives", "confronts", 
                                       "saves", "rescue", "attack" };
            return majorKeywords.Any(keyword => description.Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        private string ExtractThemeName(string line)
        {
            var clean = line.TrimStart('-', '•').Trim();
            var colonIndex = clean.IndexOf(':');
            if (colonIndex > 0)
            {
                return clean.Substring(0, colonIndex).Trim();
            }
            
            // Take first few words
            var words = clean.Split(' ').Take(3);
            return string.Join(" ", words);
        }

        private string DetermineConnectionType(Chapter from, Chapter to)
        {
            // Analyze chapter endings and beginnings
            if (to.Number == from.Number + 1)
            {
                // Check for time indicators
                if (to.Summary.Contains("later", StringComparison.OrdinalIgnoreCase) ||
                    to.Summary.Contains("after", StringComparison.OrdinalIgnoreCase))
                {
                    return "Time skip";
                }
                
                // Check for POV changes
                if (from.PointOfView != to.PointOfView && !string.IsNullOrEmpty(from.PointOfView))
                {
                    return "POV shift";
                }
                
                return "Direct continuation";
            }
            
            return "Chapter skip";
        }

        private List<int> FindChaptersWithLocation(string location, List<Chapter> chapters)
        {
            var chapterNumbers = new List<int>();
            
            foreach (var chapter in chapters)
            {
                if (chapter.Locations.Contains(location, StringComparer.OrdinalIgnoreCase) ||
                    chapter.Summary.Contains(location, StringComparison.OrdinalIgnoreCase))
                {
                    chapterNumbers.Add(chapter.Number);
                }
            }
            
            return chapterNumbers;
        }
    }
} 