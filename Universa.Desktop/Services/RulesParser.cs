using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Services
{
    public class RulesParser
    {
        public class ParsedRules
        {
            public Dictionary<string, RuleSection> Sections { get; set; } = new Dictionary<string, RuleSection>();
            public SeriesTimeline Timeline { get; set; } = new SeriesTimeline();
            public Dictionary<string, Character> Characters { get; set; } = new Dictionary<string, Character>();
            public List<PlotConnection> PlotConnections { get; set; } = new List<PlotConnection>();
            public List<string> CriticalFacts { get; set; } = new List<string>();
            public Dictionary<string, Location> Locations { get; set; } = new Dictionary<string, Location>();
            public Dictionary<string, Organization> Organizations { get; set; } = new Dictionary<string, Organization>();
            
            // Three-Tier Enhancement: New hierarchical organization
            public UniverseCore Core { get; set; } = new UniverseCore();
            public List<SeriesArc> Arcs { get; set; } = new List<SeriesArc>();
            public Dictionary<int, BookSpecific> BookDetails { get; set; } = new Dictionary<int, BookSpecific>();
        }

        // Three-Tier Structure: Universe Core (always included)
        public class UniverseCore
        {
            public List<string> UniversalLaws { get; set; } = new List<string>();
            public List<string> CoreThemes { get; set; } = new List<string>();
            public Dictionary<string, Character> CoreCharacters { get; set; } = new Dictionary<string, Character>();
            public List<string> RecurringElements { get; set; } = new List<string>();
            public Dictionary<string, string> FamilyLineages { get; set; } = new Dictionary<string, string>();
            public List<string> GenerationalConnections { get; set; } = new List<string>();
        }

        // Three-Tier Structure: Arc Level (context-dependent)
        public class SeriesArc
        {
            public string Name { get; set; }
            public List<int> BookNumbers { get; set; } = new List<int>();
            public TimeRange TimeRange { get; set; }
            public string PrimaryProtagonist { get; set; }
            public List<string> ArcThemes { get; set; } = new List<string>();
            public List<string> ArcCharacters { get; set; } = new List<string>();
            public Dictionary<string, string> TechnologyLevel { get; set; } = new Dictionary<string, string>();
            public List<string> HistoricalContext { get; set; } = new List<string>();
        }

        // Three-Tier Structure: Book Specific (only when relevant)  
        public class BookSpecific
        {
            public int BookNumber { get; set; }
            public string Title { get; set; }
            public List<string> OneTimeCharacters { get; set; } = new List<string>();
            public List<string> BookSpecificLocations { get; set; } = new List<string>();
            public List<string> SpecificPlotElements { get; set; } = new List<string>();
            public string ImmediateContext { get; set; }
        }

        public class TimeRange
        {
            public int? StartYear { get; set; }
            public int? EndYear { get; set; }
            public string Era { get; set; } // "1920s Aviation", "Modern Digital", etc.
        }

        public class RuleSection
        {
            public string Title { get; set; }
            public string Content { get; set; }
            public SectionType Type { get; set; }
            public List<string> KeyFacts { get; set; } = new List<string>();
        }

        public class SeriesTimeline
        {
            public List<Book> Books { get; set; } = new List<Book>();
            public Dictionary<string, List<BookEvent>> CharacterAppearances { get; set; } = new Dictionary<string, List<BookEvent>>();
            public List<TimelineEvent> MajorEvents { get; set; } = new List<TimelineEvent>();
        }

        public class Book
        {
            public int Number { get; set; }
            public string Title { get; set; }
            public bool IsWritten { get; set; }
            public string Synopsis { get; set; }
            public List<string> IntroducedCharacters { get; set; } = new List<string>();
            public List<string> KeyEvents { get; set; } = new List<string>();
            public List<string> Locations { get; set; } = new List<string>();
        }

        public class Character
        {
            public string Name { get; set; }
            public string FullName { get; set; }
            public CharacterType Type { get; set; }
            public Dictionary<int, int> AgeByBook { get; set; } = new Dictionary<int, int>();
            public List<string> PhysicalTraits { get; set; } = new List<string>();
            public List<string> PersonalityTraits { get; set; } = new List<string>();
            public Dictionary<string, string> Relationships { get; set; } = new Dictionary<string, string>();
            public List<BookAppearance> Appearances { get; set; } = new List<BookAppearance>();
            public string Background { get; set; }
            public string SpecialSkills { get; set; }
            public List<string> KeyQuotes { get; set; } = new List<string>();
            public string Fate { get; set; } // For characters who die or disappear
        }

        public class BookAppearance
        {
            public int BookNumber { get; set; }
            public string BookTitle { get; set; }
            public string Role { get; set; }
            public List<string> KeyActions { get; set; } = new List<string>();
        }

        public class PlotConnection
        {
            public string Element { get; set; } // Character, object, or event
            public int FirstBook { get; set; }
            public int ConnectedBook { get; set; }
            public string ConnectionType { get; set; }
            public string Description { get; set; }
        }

        public class Location
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public List<int> AppearsInBooks { get; set; } = new List<int>();
            public List<string> AssociatedCharacters { get; set; } = new List<string>();
            public string Significance { get; set; }
        }

        public class Organization
        {
            public string Name { get; set; }
            public string Type { get; set; } // Criminal, Government, Corporate, etc.
            public string Leader { get; set; }
            public List<string> Members { get; set; } = new List<string>();
            public List<int> ActiveInBooks { get; set; } = new List<int>();
            public string Purpose { get; set; }
        }

        public class BookEvent
        {
            public int BookNumber { get; set; }
            public string BookTitle { get; set; }
            public string EventDescription { get; set; }
        }

        public class TimelineEvent
        {
            public int BookNumber { get; set; }
            public string Description { get; set; }
            public List<string> CharactersInvolved { get; set; } = new List<string>();
            public string Impact { get; set; }
        }

        public enum SectionType
        {
            Background,
            SeriesSynopsis,
            CharacterProfiles,
            LocationDetails,
            OrganizationInfo,
            Timeline,
            GeneralRules
        }

        public enum CharacterType
        {
            MainProtagonist,
            Protagonist,
            Antagonist,
            Supporting,
            Minor
        }

        public ParsedRules Parse(string rulesContent)
        {
            var result = new ParsedRules();
            
            // Parse sections
            var sections = ParseSections(rulesContent);
            
            foreach (var section in sections)
            {
                var parsedSection = ParseSection(section);
                result.Sections[parsedSection.Title] = parsedSection;
                
                // Process specific section types
                switch (parsedSection.Type)
                {
                    case SectionType.SeriesSynopsis:
                        result.Timeline = ParseSeriesTimeline(section);
                        break;
                    case SectionType.CharacterProfiles:
                        ParseCharacters(section, result.Characters);
                        break;
                }
            }
            
            // Extract critical facts
            result.CriticalFacts = ExtractCriticalFacts(rulesContent);
            
            // Build plot connections
            result.PlotConnections = BuildPlotConnections(result);
            
            // Extract locations
            result.Locations = ExtractLocations(rulesContent, result.Timeline);
            
            // Extract organizations
            result.Organizations = ExtractOrganizations(rulesContent);
            
            // NEW: Build three-tier enhancement structure
            BuildThreeTierStructure(result, rulesContent);
            
            return result;
        }

        /// <summary>
        /// Builds the three-tier hierarchical structure from parsed content
        /// </summary>
        private void BuildThreeTierStructure(ParsedRules result, string rulesContent)
        {
            // Build Universe Core (always included)
            result.Core = ExtractUniverseCore(result, rulesContent);
            
            // Build Series Arcs (context-dependent)
            result.Arcs = ExtractSeriesArcs(result, rulesContent);
            
            // Build Book-Specific Details (only when relevant)
            result.BookDetails = ExtractBookSpecificDetails(result, rulesContent);
        }

        /// <summary>
        /// Gets contextually filtered content based on current book/character focus
        /// </summary>
        public string GetFilteredRulesContent(ParsedRules parsedRules, string currentContent, string currentFilePath = null)
        {
            var currentBook = DetermineCurrentBook(currentContent, currentFilePath);
            var currentEra = DetermineCurrentEra(currentContent, parsedRules);
            var protagonistGeneration = DetermineProtagonistGeneration(currentContent, parsedRules);

            var filteredContent = new StringBuilder();

            // ALWAYS include Universe Core
            filteredContent.AppendLine("=== UNIVERSE CORE ===");
            filteredContent.AppendLine(BuildUniverseCoreContent(parsedRules.Core));

            // For rules development, include ALL arcs to ensure comprehensive understanding
            // rather than limiting to just one "relevant" arc
            if (parsedRules.Arcs.Any())
            {
                filteredContent.AppendLine("\n=== ALL SERIES ARCS ===");
                filteredContent.AppendLine("Complete arc structure for comprehensive universe understanding:");
                filteredContent.AppendLine();
                
                foreach (var arc in parsedRules.Arcs.OrderBy(a => a.BookNumbers.FirstOrDefault()))
                {
                    filteredContent.AppendLine($"**{arc.Name}**");
                    filteredContent.AppendLine($"Books: {string.Join(", ", arc.BookNumbers)}");
                    if (!string.IsNullOrEmpty(arc.PrimaryProtagonist))
                    {
                        filteredContent.AppendLine($"Primary Protagonist: {arc.PrimaryProtagonist}");
                    }
                    if (arc.ArcThemes.Any())
                    {
                        filteredContent.AppendLine($"Themes: {string.Join(", ", arc.ArcThemes)}");
                    }
                    filteredContent.AppendLine();
                }
            }

            // Include specific book context if relevant, but don't limit it
            if (currentBook > 0 && parsedRules.BookDetails.ContainsKey(currentBook))
            {
                filteredContent.AppendLine("\n=== CURRENT FOCUS BOOK ===");
                filteredContent.AppendLine(BuildBookSpecificContent(parsedRules.BookDetails[currentBook]));
            }

            // Include complete series synopsis (our previous fix)
            var adjacentContext = BuildAdjacentBookContext(parsedRules, currentBook);
            if (!string.IsNullOrEmpty(adjacentContext))
            {
                filteredContent.AppendLine("\n=== COMPLETE SERIES CONTEXT ===");
                filteredContent.AppendLine(adjacentContext);
            }
            
            // Add comprehensive universe connections
            if (parsedRules.PlotConnections.Any())
            {
                filteredContent.AppendLine("\n=== UNIVERSE CONNECTIONS ===");
                filteredContent.AppendLine("Cross-book plot and character connections:");
                foreach (var connection in parsedRules.PlotConnections.Take(10)) // Limit to top 10 to avoid overwhelming
                {
                    filteredContent.AppendLine($"- {connection.Element}: {connection.ConnectionType} (Books {connection.FirstBook}-{connection.ConnectedBook})");
                }
            }

            return filteredContent.ToString();
        }

        private List<string> ParseSections(string content)
        {
            var sections = new List<string>();
            
            // Parse markdown headers (# ## ###) - much better than bracket format!
            var headerMatches = Regex.Matches(content, @"^(#{1,3})\s+(.+)$", RegexOptions.Multiline);
            
            for (int i = 0; i < headerMatches.Count; i++)
            {
                var currentMatch = headerMatches[i];
                var currentLevel = currentMatch.Groups[1].Value.Length; // Number of # symbols
                var start = currentMatch.Index;
                
                // Find the end of this section by looking for the next header at same or higher level
                int end = content.Length;
                for (int j = i + 1; j < headerMatches.Count; j++)
                {
                    var nextMatch = headerMatches[j];
                    var nextLevel = nextMatch.Groups[1].Value.Length;
                    
                    // If we find a header at the same level or higher (fewer #), that's our boundary
                    if (nextLevel <= currentLevel)
                    {
                        end = nextMatch.Index;
                        break;
                    }
                }
                
                var sectionContent = content.Substring(start, end - start).Trim();
                if (!string.IsNullOrWhiteSpace(sectionContent))
                {
                    sections.Add(sectionContent);
                }
            }
            
            // If no markdown headers found, fall back to looking for old bracket format for compatibility
            if (!sections.Any())
            {
                var bracketMatches = Regex.Matches(content, @"^\[([^\]]+)\]", RegexOptions.Multiline);
                for (int i = 0; i < bracketMatches.Count; i++)
                {
                    var start = bracketMatches[i].Index;
                    var end = (i < bracketMatches.Count - 1) ? bracketMatches[i + 1].Index : content.Length;
                    sections.Add(content.Substring(start, end - start));
                }
            }
            
            return sections;
        }

        private RuleSection ParseSection(string sectionContent)
        {
            var section = new RuleSection();
            
            // Extract title from markdown headers (preferred format)
            var markdownHeaderMatch = Regex.Match(sectionContent, @"^(#{1,3})\s+(.+)$", RegexOptions.Multiline);
            if (markdownHeaderMatch.Success)
            {
                var headerLevel = markdownHeaderMatch.Groups[1].Value.Length;
                var title = markdownHeaderMatch.Groups[2].Value.Trim();
                
                // Clean up title - remove any trailing formatting
                title = Regex.Replace(title, @"\s*[-–—]\s*.+$", ""); // Remove subtitle after dash
                section.Title = title;
            }
            else
            {
                // Fall back to old bracket format for compatibility
                var bracketMatch = Regex.Match(sectionContent, @"^\[([^\]]+)\]", RegexOptions.Multiline);
                if (bracketMatch.Success)
                {
                    section.Title = bracketMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Last resort: try to extract from character-style headers
                    var charMatch = Regex.Match(sectionContent, @"^#\s+(.+?)(?:\s+-\s+.+)?$", RegexOptions.Multiline);
                    if (charMatch.Success)
                    {
                        section.Title = charMatch.Groups[1].Value.Trim();
                    }
                }
            }
            
            section.Content = sectionContent;
            section.Type = DetermineSectionType(section.Title);
            section.KeyFacts = ExtractKeyFacts(sectionContent);
            
            return section;
        }

        private SeriesTimeline ParseSeriesTimeline(string synopsisSection)
        {
            var timeline = new SeriesTimeline();
            
            // Look for book entries in markdown format: "## Book X - Title" 
            var bookHeaderMatches = Regex.Matches(synopsisSection, 
                @"^##\s+Book\s+(\d+(?:\.\d+)?)\s*-?\s*([^\r\n]*)", 
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            
            foreach (Match headerMatch in bookHeaderMatches)
            {
                var bookNumberStr = headerMatch.Groups[1].Value;
                var bookTitle = headerMatch.Groups[2].Value.Trim();
                
                // Handle decimal book numbers (like Book 3.5)
                if (decimal.TryParse(bookNumberStr, out var bookDecimal))
                {
                    var bookNumber = (int)Math.Floor(bookDecimal); // Convert 3.5 to 3 for primary tracking
                    
                    // Extract the content under this book header
                    var start = headerMatch.Index + headerMatch.Length;
                    var nextBookMatch = bookHeaderMatches.Cast<Match>()
                        .FirstOrDefault(m => m.Index > headerMatch.Index);
                    var end = nextBookMatch?.Index ?? synopsisSection.Length;
                    
                    var bookContent = synopsisSection.Substring(start, end - start).Trim();
                    
                    // Determine if book is written based on common patterns
                    bool isWritten = !bookContent.Contains("not written", StringComparison.OrdinalIgnoreCase) &&
                                   !bookContent.Contains("Not written", StringComparison.OrdinalIgnoreCase) &&
                                   !bookTitle.Contains("not written", StringComparison.OrdinalIgnoreCase);
                    
                    var book = new Book
                    {
                        Number = bookNumber,
                        Title = string.IsNullOrWhiteSpace(bookTitle) ? $"Book {bookNumberStr}" : bookTitle,
                        IsWritten = isWritten,
                        Synopsis = bookContent
                    };
                    
                    // Extract introduced characters from bullet points and content
                    var introMatches = Regex.Matches(bookContent, @"(?:Introduces?|introduces?)\s+([^,\r\n]+?)(?:\s+as\s+|,|\.|$)");
                    foreach (Match intro in introMatches)
                    {
                        var characterName = ExtractCharacterName(intro.Groups[1].Value);
                        if (!string.IsNullOrWhiteSpace(characterName))
                        {
                            book.IntroducedCharacters.Add(characterName);
                        }
                    }
                    
                    // Extract key events from bullet points
                    var bulletPoints = Regex.Matches(bookContent, @"^\s*[-•]\s*(.+)$", RegexOptions.Multiline);
                    foreach (Match bullet in bulletPoints)
                    {
                        var eventText = bullet.Groups[1].Value.Trim();
                        if (eventText.Length > 20) // Only substantial events
                        {
                            book.KeyEvents.Add(eventText);
                        }
                    }
                    
                    // Extract locations
                    var locationMatches = Regex.Matches(bookContent, @"(?:in|at|to)\s+([A-Z][a-zA-Z\s]+?)(?:\s+where|\s+to|\s*,|\s*\.|\s*$)", RegexOptions.IgnoreCase);
                    foreach (Match loc in locationMatches)
                    {
                        var location = loc.Groups[1].Value.Trim();
                        if (IsValidLocation(location))
                        {
                            book.Locations.Add(location);
                        }
                    }
                    
                    timeline.Books.Add(book);
                }
            }
            
            // Sort books by number to ensure proper order
            timeline.Books = timeline.Books.OrderBy(b => b.Number).ToList();
            
            return timeline;
        }

        private void ParseCharacters(string characterSection, Dictionary<string, Character> characters)
        {
            // Parse character and organization profiles using markdown headers (## Name)
            var profileMatches = Regex.Matches(characterSection, @"^##\s+(.+?)(?:\s+-\s+(.+?))?$", RegexOptions.Multiline);
            
            foreach (Match match in profileMatches)
            {
                var profileName = match.Groups[1].Value.Trim();
                var profileSubtitle = match.Groups[2].Success ? match.Groups[2].Value.Trim() : "";
                
                // Skip if this looks like a book entry (contains "Book" followed by number)
                if (Regex.IsMatch(profileName, @"Book\s+\d+", RegexOptions.IgnoreCase))
                    continue;
                
                var character = ParseCharacterDetails(characterSection, match.Index, profileName);
                
                if (character != null)
                {
                    // Use the full profile name as the key, handling both characters and organizations
                    characters[profileName] = character;
                    
                    // If there's a subtitle, store it as additional context
                    if (!string.IsNullOrWhiteSpace(profileSubtitle))
                    {
                        character.Background = $"{profileSubtitle}. {character.Background}".Trim();
                    }
                }
            }
            
            // Also look for # headers for compatibility (single # character headers)
            var singleHeaderMatches = Regex.Matches(characterSection, @"^#\s+(.+?)(?:\s+-\s+(.+?))?$", RegexOptions.Multiline);
            foreach (Match match in singleHeaderMatches)
            {
                var profileName = match.Groups[1].Value.Trim();
                
                // Skip if already processed or if this is a major section header
                if (characters.ContainsKey(profileName) || 
                    profileName.Contains("Character", StringComparison.OrdinalIgnoreCase) ||
                    profileName.Contains("Profile", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                var character = ParseCharacterDetails(characterSection, match.Index, profileName);
                if (character != null && !characters.ContainsKey(profileName))
                {
                    characters[profileName] = character;
                }
            }
        }

        private Character ParseCharacterDetails(string content, int startIndex, string profileName = null)
        {
            var character = new Character();
            
            // Find the end of this character/organization section
            var nextProfileIndex = content.IndexOf("\n##", startIndex + 1);
            if (nextProfileIndex == -1)
                nextProfileIndex = content.IndexOf("\n#", startIndex + 1);
            var endIndex = nextProfileIndex > 0 ? nextProfileIndex : content.Length;
            var profileSection = content.Substring(startIndex, endIndex - startIndex);
            
            // Set the name from parameter or extract from content
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                character.Name = profileName;
                character.FullName = profileName;
                
                // Clean up name - remove any "- Expanded Character Profile" type suffixes
                character.Name = Regex.Replace(character.Name, @"\s*-\s*.*(?:Profile|Character).*$", "", RegexOptions.IgnoreCase).Trim();
                if (string.IsNullOrWhiteSpace(character.Name))
                    character.Name = profileName; // Fallback to original if cleaning removed everything
            }
            else
            {
                // Fallback: extract from header in content
                var nameMatch = Regex.Match(profileSection, @"^#{1,2}\s+(.+?)(?:\s+-\s+(.+))?$", RegexOptions.Multiline);
                if (nameMatch.Success)
                {
                    character.Name = nameMatch.Groups[1].Value.Trim();
                    character.FullName = nameMatch.Groups[2].Success ? nameMatch.Groups[2].Value : character.Name;
                }
            }
            
            // Parse subsections using ### headers
            var subsections = Regex.Matches(profileSection, @"^###\s+(.+?)$", RegexOptions.Multiline);
            foreach (Match subsection in subsections)
            {
                var subsectionTitle = subsection.Groups[1].Value.Trim().ToLowerInvariant();
                var subsectionStart = subsection.Index;
                var nextSubsection = subsections.Cast<Match>()
                    .FirstOrDefault(m => m.Index > subsectionStart);
                var subsectionEnd = nextSubsection?.Index ?? profileSection.Length;
                var subsectionContent = profileSection.Substring(subsectionStart, subsectionEnd - subsectionStart);
                
                // Parse different types of subsections
                switch (subsectionTitle)
                {
                    case "core characteristics":
                    case "characteristics":
                        ParseCharacteristics(subsectionContent, character);
                        break;
                    case "background":
                    case "background & education":
                        character.Background = ExtractParagraphContent(subsectionContent);
                        break;
                    case "career progression":
                    case "career":
                    case "professional":
                        character.SpecialSkills = ExtractParagraphContent(subsectionContent);
                        break;
                    case "physical appearance":
                    case "appearance":
                        character.PhysicalTraits = ExtractBulletPoints(subsectionContent);
                        break;
                    case "personality":
                    case "personality traits":
                        character.PersonalityTraits = ExtractBulletPoints(subsectionContent);
                        break;
                }
            }
            
            // Parse age progression patterns anywhere in the section
            var ageMatches = Regex.Matches(profileSection, @"(\d+)\s*\(Book\s+(\d+)\)");
            foreach (Match ageMatch in ageMatches)
            {
                if (int.TryParse(ageMatch.Groups[1].Value, out var age) &&
                    int.TryParse(ageMatch.Groups[2].Value, out var bookNum))
                {
                    character.AgeByBook[bookNum] = age;
                }
            }
            
            // Determine character type (works for organizations too)
            character.Type = DetermineCharacterType(character.Name, profileSection);
            
            return character;
        }

        private void ParseCharacteristics(string content, Character character)
        {
            // Extract age information
            var ageMatches = Regex.Matches(content, @"Age:\s*(.+?)(?:\n|$)");
            foreach (Match match in ageMatches)
            {
                var ageInfo = match.Groups[1].Value.Trim();
                // Parse age progression if present
                var progressionMatches = Regex.Matches(ageInfo, @"(\d+)\s*\(Book\s+(\d+)\)");
                foreach (Match progression in progressionMatches)
                {
                    if (int.TryParse(progression.Groups[1].Value, out var age) &&
                        int.TryParse(progression.Groups[2].Value, out var bookNum))
                    {
                        character.AgeByBook[bookNum] = age;
                    }
                }
            }
            
            // Extract physical traits
            var physicalMatches = Regex.Matches(content, @"Physical.*?:\s*(.+?)(?:\n\n|\n-|\n\*|$)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            foreach (Match match in physicalMatches)
            {
                var traits = match.Groups[1].Value.Split(',', ';')
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();
                character.PhysicalTraits.AddRange(traits);
            }
            
            // Extract other bullet points as general characteristics
            var bulletPoints = ExtractBulletPoints(content);
            character.PersonalityTraits.AddRange(bulletPoints.Where(bp => 
                !bp.Contains("Age:", StringComparison.OrdinalIgnoreCase) &&
                !bp.Contains("Physical", StringComparison.OrdinalIgnoreCase)));
        }
        
        private string ExtractParagraphContent(string content)
        {
            // Remove the header line and extract paragraph content
            var lines = content.Split('\n');
            var contentLines = lines.Skip(1) // Skip the ### header
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();
                
            return string.Join(" ", contentLines).Trim();
        }
        
        private List<string> ExtractBulletPoints(string content)
        {
            var bulletPoints = new List<string>();
            var matches = Regex.Matches(content, @"^\s*[-•*]\s*(.+)$", RegexOptions.Multiline);
            
            foreach (Match match in matches)
            {
                var point = match.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(point))
                {
                    bulletPoints.Add(point);
                }
            }
            
            return bulletPoints;
        }

        private List<PlotConnection> BuildPlotConnections(ParsedRules rules)
        {
            var connections = new List<PlotConnection>();
            
            // Find characters appearing in multiple books
            foreach (var character in rules.Characters.Values)
            {
                var bookNumbers = new List<int>();
                foreach (var book in rules.Timeline.Books)
                {
                    if (book.Synopsis.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        bookNumbers.Add(book.Number);
                    }
                }
                
                if (bookNumbers.Count > 1)
                {
                    for (int i = 1; i < bookNumbers.Count; i++)
                    {
                        connections.Add(new PlotConnection
                        {
                            Element = character.Name,
                            FirstBook = bookNumbers[0],
                            ConnectedBook = bookNumbers[i],
                            ConnectionType = "Character Recurrence",
                            Description = $"{character.Name} appears in both books"
                        });
                    }
                }
            }
            
            // Find plot threads (weapons, organizations, etc.)
            var plotElements = new[] { "McNamara", "syndicate", "nuclear", "missile", "terrorist", "Kruger" };
            foreach (var element in plotElements)
            {
                var elementBooks = rules.Timeline.Books
                    .Where(b => b.Synopsis.Contains(element, StringComparison.OrdinalIgnoreCase))
                    .Select(b => b.Number)
                    .ToList();
                    
                if (elementBooks.Count > 1)
                {
                    connections.Add(new PlotConnection
                    {
                        Element = element,
                        FirstBook = elementBooks.First(),
                        ConnectedBook = elementBooks.Last(),
                        ConnectionType = "Plot Thread",
                        Description = $"{element} connects multiple books"
                    });
                }
            }
            
            return connections;
        }

        private List<string> ExtractCriticalFacts(string content)
        {
            var facts = new List<string>();
            
            // Look for death mentions
            var deathMatches = Regex.Matches(content, @"([^.]+(?:dies?|killed|death)[^.]+\.)", RegexOptions.IgnoreCase);
            facts.AddRange(deathMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            
            // Look for relationship changes
            var relationshipMatches = Regex.Matches(content, @"([^.]+(?:marries?|married|wife|husband)[^.]+\.)", RegexOptions.IgnoreCase);
            facts.AddRange(relationshipMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            
            // Look for major position changes
            var positionMatches = Regex.Matches(content, @"([^.]+(?:becomes?|appointed|elected)\s+(?:President|Secretary|Director)[^.]+\.)", RegexOptions.IgnoreCase);
            facts.AddRange(positionMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            
            return facts.Distinct().ToList();
        }

        private Dictionary<string, Location> ExtractLocations(string content, SeriesTimeline timeline)
        {
            var locations = new Dictionary<string, Location>();
            
            // Common location patterns
            var locationPatterns = new[]
            {
                @"in\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
                @"at\s+(?:the\s+)?([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)",
                @"(?:travel|sent|goes?)\s+to\s+([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*)"
            };
            
            foreach (var pattern in locationPatterns)
            {
                var matches = Regex.Matches(content, pattern);
                foreach (Match match in matches)
                {
                    var locationName = match.Groups[1].Value;
                    if (!locations.ContainsKey(locationName) && IsValidLocation(locationName))
                    {
                        locations[locationName] = new Location
                        {
                            Name = locationName,
                            AppearsInBooks = FindBooksWithLocation(locationName, timeline)
                        };
                    }
                }
            }
            
            return locations;
        }

        private Dictionary<string, Organization> ExtractOrganizations(string content)
        {
            var organizations = new Dictionary<string, Organization>();
            
            // Known organizations from the content
            var knownOrgs = new Dictionary<string, string>
            {
                { "McNamara syndicate", "Criminal" },
                { "McNamara Limited", "Corporate" },
                { "Al-Qaeda", "Terrorist" },
                { "FEMA", "Government" },
                { "SAS", "Military" },
                { "Verdensomspennende Fraktlogistikk", "Corporate" }
            };
            
            foreach (var org in knownOrgs)
            {
                if (content.Contains(org.Key, StringComparison.OrdinalIgnoreCase))
                {
                    organizations[org.Key] = new Organization
                    {
                        Name = org.Key,
                        Type = org.Value
                    };
                }
            }
            
            return organizations;
        }

        // Helper methods
        private SectionType DetermineSectionType(string title)
        {
            // Handle null or empty titles
            if (string.IsNullOrEmpty(title))
                return SectionType.GeneralRules;
                
            if (title.Contains("Background", StringComparison.OrdinalIgnoreCase))
                return SectionType.Background;
            if (title.Contains("Synopsis", StringComparison.OrdinalIgnoreCase))
                return SectionType.SeriesSynopsis;
            if (title.Contains("Character", StringComparison.OrdinalIgnoreCase))
                return SectionType.CharacterProfiles;
            if (title.Contains("Location", StringComparison.OrdinalIgnoreCase))
                return SectionType.LocationDetails;
            
            // Check if it's a character name
            if (Regex.IsMatch(title, @"^[A-Z][a-z]+(?:\s+[A-Z][a-z]+)*$"))
                return SectionType.CharacterProfiles;
            
            return SectionType.GeneralRules;
        }

        private CharacterType DetermineCharacterType(string name, string description)
        {
            if (name.Contains("Thomas") || name.Contains("Napier") || name.Contains("Ashbaugh"))
                return CharacterType.MainProtagonist;
            if (description.Contains("antagonist", StringComparison.OrdinalIgnoreCase) || 
                name.Contains("McNamara") || name.Contains("Kruger"))
                return CharacterType.Antagonist;
            if (description.Contains("minor", StringComparison.OrdinalIgnoreCase))
                return CharacterType.Minor;
                
            return CharacterType.Supporting;
        }

        private string ExtractCharacterName(string text)
        {
            // Remove titles and descriptors
            var name = Regex.Replace(text, @"(Dr\.|Mr\.|Ms\.|Mrs\.)\s*", "");
            name = Regex.Replace(name, @"\s*(as|who|,).*", "");
            return name.Trim();
        }

        private List<int> FindBooksWithLocation(string location, SeriesTimeline timeline)
        {
            return timeline.Books
                .Where(b => b.Locations.Contains(location, StringComparer.OrdinalIgnoreCase))
                .Select(b => b.Number)
                .ToList();
        }

        private bool IsValidLocation(string name)
        {
            // Filter out common false positives
            var invalidTerms = new[] { "The", "Book", "Chapter", "Part", "Section" };
            return !invalidTerms.Any(term => name.Equals(term, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> ExtractKeyFacts(string section)
        {
            var facts = new List<string>();
            
            // Extract bullet points
            var bulletMatches = Regex.Matches(section, @"^\s*[-•]\s*(.+)$", RegexOptions.Multiline);
            facts.AddRange(bulletMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            
            return facts;
        }

        // ======= THREE-TIER ENHANCEMENT METHODS =======

        /// <summary>
        /// Extracts Universe Core content that should always be included
        /// </summary>
        private UniverseCore ExtractUniverseCore(ParsedRules result, string rulesContent)
        {
            var core = new UniverseCore();

            // Extract core themes from Background section
            if (result.Sections.TryGetValue("Background", out var backgroundSection))
            {
                core.CoreThemes.AddRange(ExtractThemesFromSection(backgroundSection.Content));
                core.UniversalLaws.AddRange(ExtractUniversalLaws(backgroundSection.Content));
            }

            // Identify core recurring characters (appear in 3+ books or marked as main)
            foreach (var character in result.Characters.Values)
            {
                var appearanceCount = result.Timeline.Books.Count(b => 
                    b.Synopsis.Contains(character.Name, StringComparison.OrdinalIgnoreCase) ||
                    b.IntroducedCharacters.Contains(character.Name));

                if (appearanceCount >= 3 || character.Type == CharacterType.MainProtagonist || character.Type == CharacterType.Antagonist)
                {
                    core.CoreCharacters[character.Name] = character;
                }
            }

            // Extract family lineages and generational connections
            core.FamilyLineages = ExtractFamilyLineages(rulesContent, result.Characters);
            core.GenerationalConnections = ExtractGenerationalConnections(rulesContent);

            // Extract recurring elements (themes, objects, organizations that span multiple books)
            core.RecurringElements = ExtractRecurringElements(result);

            return core;
        }

        /// <summary>
        /// Extracts Series Arcs based on time periods, protagonists, and book groupings
        /// </summary>
        private List<SeriesArc> ExtractSeriesArcs(ParsedRules result, string rulesContent)
        {
            var arcs = new List<SeriesArc>();

            // Auto-detect arcs by protagonist and time period
            var protagonistGroups = GroupBooksByProtagonist(result.Timeline.Books);
            var timeGroups = GroupBooksByTimePeriod(result.Timeline.Books, rulesContent);

            // Create arcs based on protagonist groupings
            foreach (var protagonistGroup in protagonistGroups)
            {
                var arc = new SeriesArc
                {
                    Name = $"{protagonistGroup.Key} Arc",
                    PrimaryProtagonist = protagonistGroup.Key,
                    BookNumbers = protagonistGroup.Value.Select(b => b.Number).ToList(),
                    TimeRange = DetermineTimeRangeForBooks(protagonistGroup.Value, rulesContent)
                };

                // Extract arc-specific themes and characters
                arc.ArcThemes = ExtractArcThemes(protagonistGroup.Value);
                arc.ArcCharacters = ExtractArcCharacters(protagonistGroup.Value);
                arc.TechnologyLevel = DetermineTechnologyLevel(arc.TimeRange);
                arc.HistoricalContext = ExtractHistoricalContext(arc.TimeRange, rulesContent);

                arcs.Add(arc);
            }

            // Also create era-based arcs for multi-generational stories
            var eraArcs = CreateEraBasedArcs(result.Timeline.Books, rulesContent);
            arcs.AddRange(eraArcs);

            return arcs;
        }

        /// <summary>
        /// Extracts book-specific details that are only relevant to individual books
        /// </summary>
        private Dictionary<int, BookSpecific> ExtractBookSpecificDetails(ParsedRules result, string rulesContent)
        {
            var bookDetails = new Dictionary<int, BookSpecific>();

            foreach (var book in result.Timeline.Books)
            {
                var details = new BookSpecific
                {
                    BookNumber = book.Number,
                    Title = book.Title,
                    ImmediateContext = book.Synopsis
                };

                // Extract one-time characters (only appear in this book)
                details.OneTimeCharacters = ExtractOneTimeCharacters(book, result.Timeline.Books);

                // Extract book-specific locations
                details.BookSpecificLocations = ExtractBookSpecificLocations(book, result.Timeline.Books);

                // Extract specific plot elements unique to this book
                details.SpecificPlotElements = ExtractSpecificPlotElements(book);

                bookDetails[book.Number] = details;
            }

            return bookDetails;
        }

        /// <summary>
        /// Determines the current book being worked on
        /// </summary>
        private int DetermineCurrentBook(string content, string filePath = null)
        {
            // Look for book references in the content
            var bookMatch = Regex.Match(content, @"Book\s+(\d+)", RegexOptions.IgnoreCase);
            if (bookMatch.Success && int.TryParse(bookMatch.Groups[1].Value, out var bookNum))
            {
                return bookNum;
            }

            // Look in filename
            if (!string.IsNullOrEmpty(filePath))
            {
                var fileBookMatch = Regex.Match(filePath, @"Book[\s_-]?(\d+)", RegexOptions.IgnoreCase);
                if (fileBookMatch.Success && int.TryParse(fileBookMatch.Groups[1].Value, out var fileBookNum))
                {
                    return fileBookNum;
                }
            }

            return 0; // Unknown book
        }

        /// <summary>
        /// Determines the current era/time period being worked on
        /// </summary>
        private string DetermineCurrentEra(string content, ParsedRules parsedRules)
        {
            // Look for year mentions
            var yearMatches = Regex.Matches(content, @"(19\d{2}|20\d{2})");
            if (yearMatches.Count > 0)
            {
                var year = int.Parse(yearMatches[0].Groups[1].Value);
                if (year < 1950) return "Early 20th Century";
                if (year < 1980) return "Mid 20th Century";
                if (year < 2000) return "Late 20th Century";
                return "Modern Era";
            }

            // Look for technology markers
            if (content.Contains("airplane", StringComparison.OrdinalIgnoreCase) || 
                content.Contains("aviation", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("biplane", StringComparison.OrdinalIgnoreCase))
            {
                return "Early Aviation Era";
            }

            if (content.Contains("computer", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("internet", StringComparison.OrdinalIgnoreCase) ||
                content.Contains("digital", StringComparison.OrdinalIgnoreCase))
            {
                return "Digital Era";
            }

            return "Contemporary";
        }

        /// <summary>
        /// Determines which protagonist generation is active
        /// </summary>
        private string DetermineProtagonistGeneration(string content, ParsedRules parsedRules)
        {
            // Look for generation indicators in character names and descriptions
            foreach (var character in parsedRules.Characters.Values)
            {
                if (content.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
                {
                    // Check for generational indicators
                    if (character.Background?.Contains("granddaughter", StringComparison.OrdinalIgnoreCase) == true ||
                        character.Background?.Contains("grandson", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return "Third Generation";
                    }
                    if (character.Background?.Contains("daughter", StringComparison.OrdinalIgnoreCase) == true ||
                        character.Background?.Contains("son", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return "Second Generation";
                    }
                    if (character.Type == CharacterType.MainProtagonist)
                    {
                        return "First Generation";
                    }
                }
            }

            return "Current Generation";
        }

        // Content building methods for filtered output
        private string BuildUniverseCoreContent(UniverseCore core)
        {
            var content = new StringBuilder();

            if (core.CoreThemes.Any())
            {
                content.AppendLine("**Core Universe Themes:**");
                foreach (var theme in core.CoreThemes)
                {
                    content.AppendLine($"- {theme}");
                }
                content.AppendLine();
            }

            if (core.CoreCharacters.Any())
            {
                content.AppendLine("**Core Characters (appear throughout series):**");
                foreach (var character in core.CoreCharacters.Values)
                {
                    content.AppendLine($"- **{character.Name}**: {character.Type} - {character.Background}");
                }
                content.AppendLine();
            }

            if (core.FamilyLineages.Any())
            {
                content.AppendLine("**Family Lineages:**");
                foreach (var lineage in core.FamilyLineages)
                {
                    content.AppendLine($"- {lineage.Key}: {lineage.Value}");
                }
                content.AppendLine();
            }

            if (core.RecurringElements.Any())
            {
                content.AppendLine("**Recurring Elements:**");
                foreach (var element in core.RecurringElements)
                {
                    content.AppendLine($"- {element}");
                }
            }

            return content.ToString();
        }

        private string BuildArcContent(SeriesArc arc, int currentBook)
        {
            var content = new StringBuilder();
            content.AppendLine($"**{arc.Name}** (Books {string.Join(", ", arc.BookNumbers)})");
            
            if (arc.TimeRange != null)
            {
                content.AppendLine($"Era: {arc.TimeRange.Era}");
                if (arc.TimeRange.StartYear.HasValue)
                {
                    content.AppendLine($"Time Period: {arc.TimeRange.StartYear}-{arc.TimeRange.EndYear}");
                }
            }

            if (!string.IsNullOrEmpty(arc.PrimaryProtagonist))
            {
                content.AppendLine($"Primary Protagonist: {arc.PrimaryProtagonist}");
            }

            if (arc.ArcThemes.Any())
            {
                content.AppendLine("Arc Themes:");
                foreach (var theme in arc.ArcThemes)
                {
                    content.AppendLine($"- {theme}");
                }
            }

            if (arc.TechnologyLevel.Any())
            {
                content.AppendLine("Technology Context:");
                foreach (var tech in arc.TechnologyLevel)
                {
                    content.AppendLine($"- {tech.Key}: {tech.Value}");
                }
            }

            return content.ToString();
        }

        private string BuildBookSpecificContent(BookSpecific bookDetails)
        {
            var content = new StringBuilder();
            content.AppendLine($"**Book {bookDetails.BookNumber}: {bookDetails.Title}**");
            content.AppendLine($"Context: {bookDetails.ImmediateContext}");

            if (bookDetails.OneTimeCharacters.Any())
            {
                content.AppendLine("Book-Specific Characters:");
                foreach (var character in bookDetails.OneTimeCharacters)
                {
                    content.AppendLine($"- {character}");
                }
            }

            if (bookDetails.BookSpecificLocations.Any())
            {
                content.AppendLine("Book-Specific Locations:");
                foreach (var location in bookDetails.BookSpecificLocations)
                {
                    content.AppendLine($"- {location}");
                }
            }

            return content.ToString();
        }

        private string BuildAdjacentBookContext(ParsedRules parsedRules, int currentBook)
        {
            var content = new StringBuilder();
            
            // For rules files, include the complete series synopsis to ensure comprehensive context
            // This ensures users can ask about any book regardless of cursor position
            content.AppendLine("**COMPLETE SERIES SYNOPSIS:**");
            
            foreach (var book in parsedRules.Timeline.Books.OrderBy(b => b.Number))
            {
                content.AppendLine($"**Book {book.Number} - {book.Title}** ({(book.IsWritten ? "Written" : "Not written yet")})");
                
                // Include full synopsis for comprehensive context
                if (!string.IsNullOrEmpty(book.Synopsis))
                {
                    content.AppendLine(book.Synopsis);
                }
                
                if (book.IntroducedCharacters.Any())
                {
                    content.AppendLine($"Introduces: {string.Join(", ", book.IntroducedCharacters)}");
                }
                
                content.AppendLine(); // Add spacing between books
            }
            
            // If there's a specific current book, highlight it
            if (currentBook > 0)
            {
                var currentBookInfo = parsedRules.Timeline.Books.FirstOrDefault(b => b.Number == currentBook);
                if (currentBookInfo != null)
                {
                    content.AppendLine($"**CURRENT FOCUS: Book {currentBook} - {currentBookInfo.Title}**");
                }
            }

            return content.ToString();
        }

        // Helper methods for extraction
        private List<string> ExtractThemesFromSection(string content)
        {
            var themes = new List<string>();
            // Extract themes from bullet points and paragraphs
            var themeMatches = Regex.Matches(content, @"(?:theme|focus|core):\s*([^.\n]+)", RegexOptions.IgnoreCase);
            themes.AddRange(themeMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            return themes;
        }

        private List<string> ExtractUniversalLaws(string content)
        {
            var laws = new List<string>();
            // Extract universal rules and laws
            var ruleMatches = Regex.Matches(content, @"(?:rule|law|principle):\s*([^.\n]+)", RegexOptions.IgnoreCase);
            laws.AddRange(ruleMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            return laws;
        }

        private Dictionary<string, string> ExtractFamilyLineages(string content, Dictionary<string, Character> characters)
        {
            var lineages = new Dictionary<string, string>();
            
            // Look for family relationship patterns
            var familyMatches = Regex.Matches(content, 
                @"([A-Z][a-z]+\s+[A-Z][a-z]+).*?(?:granddaughter|grandson|daughter|son|mother|father).*?([A-Z][a-z]+\s+[A-Z][a-z]+)", 
                RegexOptions.IgnoreCase);
                
            foreach (Match match in familyMatches)
            {
                var person1 = match.Groups[1].Value.Trim();
                var person2 = match.Groups[2].Value.Trim();
                lineages[person1] = $"Family connection to {person2}";
            }
            
            return lineages;
        }

        private List<string> ExtractGenerationalConnections(string content)
        {
            var connections = new List<string>();
            // Extract generational connections
            var connectionMatches = Regex.Matches(content, @"([^.\n]*(?:generation|legacy|family|lineage)[^.\n]*)", RegexOptions.IgnoreCase);
            connections.AddRange(connectionMatches.Cast<Match>().Select(m => m.Groups[1].Value.Trim()));
            return connections;
        }

        private List<string> ExtractRecurringElements(ParsedRules result)
        {
            var elements = new List<string>();
            
            // Elements that appear in multiple books
            var allSynopses = string.Join(" ", result.Timeline.Books.Select(b => b.Synopsis));
            
            // Common recurring elements to look for
            var potentialElements = new[] { "medallion", "heirloom", "organization", "syndicate", "method", "approach" };
            
            foreach (var element in potentialElements)
            {
                var count = Regex.Matches(allSynopses, element, RegexOptions.IgnoreCase).Count;
                if (count > 1)
                {
                    elements.Add($"{element} (appears in {count} books)");
                }
            }
            
            return elements;
        }

        private Dictionary<string, List<Book>> GroupBooksByProtagonist(List<Book> books)
        {
            var groups = new Dictionary<string, List<Book>>();
            
            foreach (var book in books)
            {
                // Extract protagonist from synopsis
                var protagonistMatch = Regex.Match(book.Synopsis, @"(Introduces?\s+)?([A-Z][a-z]+\s+[A-Z][a-z]+)", RegexOptions.IgnoreCase);
                if (protagonistMatch.Success)
                {
                    var protagonist = protagonistMatch.Groups[2].Value;
                    if (!groups.ContainsKey(protagonist))
                    {
                        groups[protagonist] = new List<Book>();
                    }
                    groups[protagonist].Add(book);
                }
            }
            
            return groups;
        }

        private Dictionary<string, List<Book>> GroupBooksByTimePeriod(List<Book> books, string content)
        {
            var groups = new Dictionary<string, List<Book>>();
            
            foreach (var book in books)
            {
                var era = DetermineBookEra(book, content);
                if (!groups.ContainsKey(era))
                {
                    groups[era] = new List<Book>();
                }
                groups[era].Add(book);
            }
            
            return groups;
        }

        private string DetermineBookEra(Book book, string content)
        {
            // Look for era indicators in synopsis
            if (book.Synopsis.Contains("WWI", StringComparison.OrdinalIgnoreCase) ||
                book.Synopsis.Contains("1920", StringComparison.OrdinalIgnoreCase) ||
                book.Synopsis.Contains("aviation", StringComparison.OrdinalIgnoreCase))
            {
                return "Early Aviation Era";
            }
            
            if (book.Synopsis.Contains("2000", StringComparison.OrdinalIgnoreCase) ||
                book.Synopsis.Contains("digital", StringComparison.OrdinalIgnoreCase) ||
                book.Synopsis.Contains("internet", StringComparison.OrdinalIgnoreCase))
            {
                return "Digital Era";
            }
            
            return "Contemporary";
        }

        private TimeRange DetermineTimeRangeForBooks(List<Book> books, string content)
        {
            var timeRange = new TimeRange();
            
            // Extract years from book synopses
            var allYears = new List<int>();
            foreach (var book in books)
            {
                var yearMatches = Regex.Matches(book.Synopsis, @"(19\d{2}|20\d{2})");
                foreach (Match match in yearMatches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var year))
                    {
                        allYears.Add(year);
                    }
                }
            }
            
            if (allYears.Any())
            {
                timeRange.StartYear = allYears.Min();
                timeRange.EndYear = allYears.Max();
            }
            
            // Determine era
            timeRange.Era = DetermineBookEra(books.First(), content);
            
            return timeRange;
        }

        private List<string> ExtractArcThemes(List<Book> books)
        {
            var themes = new List<string>();
            
            // Extract common themes from book synopses
            var allSynopses = string.Join(" ", books.Select(b => b.Synopsis));
            
            // Look for theme-related keywords
            if (allSynopses.Contains("mystery", StringComparison.OrdinalIgnoreCase))
                themes.Add("Mystery solving");
            if (allSynopses.Contains("aviation", StringComparison.OrdinalIgnoreCase))
                themes.Add("Aviation and flight");
            if (allSynopses.Contains("family", StringComparison.OrdinalIgnoreCase))
                themes.Add("Family legacy");
            
            return themes;
        }

        private List<string> ExtractArcCharacters(List<Book> books)
        {
            var characters = new List<string>();
            
            foreach (var book in books)
            {
                characters.AddRange(book.IntroducedCharacters);
            }
            
            return characters.Distinct().ToList();
        }

        private Dictionary<string, string> DetermineTechnologyLevel(TimeRange timeRange)
        {
            var tech = new Dictionary<string, string>();
            
            if (timeRange?.Era?.Contains("Aviation") == true)
            {
                tech["Aircraft"] = "Early biplanes, limited navigation";
                tech["Communication"] = "Telegraph, early radio";
                tech["Transportation"] = "Automobiles, trains, ships";
            }
            else if (timeRange?.Era?.Contains("Digital") == true)
            {
                tech["Computing"] = "Personal computers, internet access";
                tech["Communication"] = "Cell phones, email, early social media";
                tech["Transportation"] = "Modern aircraft, GPS navigation";
            }
            
            return tech;
        }

        private List<string> ExtractHistoricalContext(TimeRange timeRange, string content)
        {
            var context = new List<string>();
            
            if (timeRange?.Era?.Contains("Aviation") == true)
            {
                context.Add("Post-WWI aviation boom");
                context.Add("Women breaking barriers in traditionally male fields");
                context.Add("Prohibition era in United States");
            }
            else if (timeRange?.Era?.Contains("Digital") == true)
            {
                context.Add("Rise of personal computing");
                context.Add("Early internet and digital communication");
                context.Add("Corporate technology expansion");
            }
            
            return context;
        }

        private List<SeriesArc> CreateEraBasedArcs(List<Book> books, string content)
        {
            var arcs = new List<SeriesArc>();
            
            // Group books by era
            var eraGroups = GroupBooksByTimePeriod(books, content);
            
            foreach (var eraGroup in eraGroups)
            {
                if (eraGroup.Value.Count > 1) // Only create arc if multiple books
                {
                    var arc = new SeriesArc
                    {
                        Name = $"{eraGroup.Key} Books",
                        BookNumbers = eraGroup.Value.Select(b => b.Number).ToList(),
                        TimeRange = DetermineTimeRangeForBooks(eraGroup.Value, content),
                        ArcThemes = ExtractArcThemes(eraGroup.Value),
                        TechnologyLevel = DetermineTechnologyLevel(DetermineTimeRangeForBooks(eraGroup.Value, content))
                    };
                    
                    arcs.Add(arc);
                }
            }
            
            return arcs;
        }

        private List<string> ExtractOneTimeCharacters(Book currentBook, List<Book> allBooks)
        {
            var oneTimeCharacters = new List<string>();
            
            foreach (var character in currentBook.IntroducedCharacters)
            {
                // Check if character appears in other books
                var appearanceCount = allBooks.Count(b => 
                    b.Number != currentBook.Number && 
                    (b.Synopsis.Contains(character, StringComparison.OrdinalIgnoreCase) ||
                     b.IntroducedCharacters.Contains(character)));
                     
                if (appearanceCount == 0)
                {
                    oneTimeCharacters.Add(character);
                }
            }
            
            return oneTimeCharacters;
        }

        private List<string> ExtractBookSpecificLocations(Book currentBook, List<Book> allBooks)
        {
            var specificLocations = new List<string>();
            
            foreach (var location in currentBook.Locations)
            {
                // Check if location appears in other books
                var appearanceCount = allBooks.Count(b => 
                    b.Number != currentBook.Number && 
                    b.Locations.Contains(location, StringComparer.OrdinalIgnoreCase));
                    
                if (appearanceCount == 0)
                {
                    specificLocations.Add(location);
                }
            }
            
            return specificLocations;
        }

        private List<string> ExtractSpecificPlotElements(Book book)
        {
            var elements = new List<string>();
            
            // Extract specific plot elements from key events
            foreach (var keyEvent in book.KeyEvents)
            {
                // Look for specific objects, situations, or unique plot devices
                var plotMatches = Regex.Matches(keyEvent, @"([A-Z][a-z]+(?:\s+[A-Z][a-z]+)*(?:\s+(?:device|weapon|plan|operation|mission)))", RegexOptions.IgnoreCase);
                foreach (Match match in plotMatches)
                {
                    elements.Add(match.Groups[1].Value);
                }
            }
            
            return elements;
        }
    }
} 