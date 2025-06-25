using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Services
{
    public class StyleGuideParser
    {
        public class ParsedStyleGuide
        {
            public Dictionary<string, StyleSection> Sections { get; set; } = new Dictionary<string, StyleSection>();
            public string WritingSample { get; set; }
            public List<StyleRule> AllRules { get; set; } = new List<StyleRule>();
            public Dictionary<string, List<string>> VocabularyAlternatives { get; set; } = new Dictionary<string, List<string>>();
            public List<string> CriticalRules { get; set; } = new List<string>();
            public NarrativeGuidelines NarrativeRules { get; set; } = new NarrativeGuidelines();
        }

        public class StyleSection
        {
            public string Title { get; set; }
            public string Content { get; set; }
            public List<StyleRule> Rules { get; set; } = new List<StyleRule>();
            public int Priority { get; set; } // Higher = more important
            public SectionType Type { get; set; }
        }

        public class StyleRule
        {
            public string Category { get; set; }
            public string RuleText { get; set; }
            public RuleType Type { get; set; }
            public List<string> Examples { get; set; } = new List<string>();
            public List<string> Restrictions { get; set; } = new List<string>();
            public int MaxOccurrences { get; set; } = -1; // -1 means unlimited
        }

        public class NarrativeGuidelines
        {
            public string PrimaryPerspective { get; set; }
            public List<string> AllowedPerspectiveShifts { get; set; } = new List<string>();
            public Dictionary<string, string> TechnicalTermHandling { get; set; } = new Dictionary<string, string>();
            public List<string> DialogueRules { get; set; } = new List<string>();
            public List<string> DescriptionRules { get; set; } = new List<string>();
            
            public NarrativeGuidelines()
            {
                AllowedPerspectiveShifts = new List<string>();
                TechnicalTermHandling = new Dictionary<string, string>();
                DialogueRules = new List<string>();
                DescriptionRules = new List<string>();
            }
        }

        public enum SectionType
        {
            VoiceRules,
            WritingSample,
            TechnicalGuidelines,
            CharacterGuidelines,
            DialogueRules,
            DescriptionRules,
            GeneralGuidelines
        }

        public enum RuleType
        {
            MustFollow,      // Critical rules that must always be followed
            ShouldFollow,    // Strong recommendations
            CanConsider,     // Optional guidelines
            Restriction,     // Things to avoid
            Example          // Illustrative examples
        }

        public ParsedStyleGuide Parse(string styleGuideContent)
        {
            var result = new ParsedStyleGuide();
            
            // First, extract the writing sample if present
            result.WritingSample = ExtractWritingSample(styleGuideContent);
            
            // Parse the hierarchical structure
            var sections = ParseSections(styleGuideContent);
            
            foreach (var section in sections)
            {
                var parsedSection = ParseSection(section);
                result.Sections[parsedSection.Title] = parsedSection;
                result.AllRules.AddRange(parsedSection.Rules);
            }
            
            // Extract vocabulary alternatives
            result.VocabularyAlternatives = ExtractVocabularyAlternatives(styleGuideContent);
            
            // Identify critical rules
            result.CriticalRules = ExtractCriticalRules(styleGuideContent);
            
            // Parse narrative-specific guidelines
            result.NarrativeRules = ParseNarrativeGuidelines(sections);
            
            return result;
        }

        private string ExtractWritingSample(string content)
        {
            // Look for section marked as "Writing Sample"
            var writingSampleMatch = Regex.Match(content, 
                @"#\s*Writing Sample.*?(?=^#|\z)", 
                RegexOptions.Multiline | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            
            if (writingSampleMatch.Success)
            {
                var sample = writingSampleMatch.Value;
                // Remove the header line
                sample = Regex.Replace(sample, @"^#.*Writing Sample.*$", "", RegexOptions.Multiline | RegexOptions.IgnoreCase).Trim();
                return sample;
            }
            
            return string.Empty;
        }

        private List<string> ParseSections(string content)
        {
            var sections = new List<string>();
            
            // Split by main headers (# or ##)
            var matches = Regex.Matches(content, @"^#{1,2}\s+.+$", RegexOptions.Multiline);
            
            for (int i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = (i < matches.Count - 1) ? matches[i + 1].Index : content.Length;
                sections.Add(content.Substring(start, end - start));
            }
            
            return sections;
        }

        private StyleSection ParseSection(string sectionContent)
        {
            var section = new StyleSection();
            
            // Extract title
            var titleMatch = Regex.Match(sectionContent, @"^#{1,2}\s+(.+)$", RegexOptions.Multiline);
            if (titleMatch.Success)
            {
                section.Title = titleMatch.Groups[1].Value.Trim();
                section.Type = DetermineSectionType(section.Title);
                section.Priority = DeterminePriority(section.Title, sectionContent);
            }
            
            section.Content = sectionContent;
            
            // Parse rules within the section
            section.Rules = ParseRules(sectionContent, section.Type);
            
            return section;
        }

        private List<StyleRule> ParseRules(string content, SectionType sectionType)
        {
            var rules = new List<StyleRule>();
            
            // Parse bullet points as individual rules
            var bulletMatches = Regex.Matches(content, @"^\s*[-•]\s*(.+?)(?=^\s*[-•]|\z)", 
                RegexOptions.Multiline | RegexOptions.Singleline);
            
            foreach (Match match in bulletMatches)
            {
                var ruleText = match.Groups[1].Value.Trim();
                var rule = new StyleRule
                {
                    Category = sectionType.ToString(),
                    RuleText = ruleText,
                    Type = DetermineRuleType(ruleText)
                };
                
                // Extract examples if present
                rule.Examples = ExtractExamples(ruleText);
                
                // Extract restrictions
                rule.Restrictions = ExtractRestrictions(ruleText);
                
                // Check for occurrence limits
                var limitMatch = Regex.Match(ruleText, @"maximum of (\w+) instance", RegexOptions.IgnoreCase);
                if (limitMatch.Success)
                {
                    rule.MaxOccurrences = ParseNumberWord(limitMatch.Groups[1].Value);
                }
                
                rules.Add(rule);
            }
            
            return rules;
        }

        private Dictionary<string, List<string>> ExtractVocabularyAlternatives(string content)
        {
            var alternatives = new Dictionary<string, List<string>>();
            
            // Look for patterns like "For 'term': alternative1, alternative2, ..."
            var matches = Regex.Matches(content, @"For\s+[""'](\w+)[""']:\s*([^.]+)");
            
            foreach (Match match in matches)
            {
                var term = match.Groups[1].Value;
                var alts = match.Groups[2].Value
                    .Split(',')
                    .Select(a => a.Trim())
                    .Where(a => !string.IsNullOrWhiteSpace(a))
                    .ToList();
                
                alternatives[term] = alts;
            }
            
            return alternatives;
        }

        private List<string> ExtractCriticalRules(string content)
        {
            var criticalRules = new List<string>();
            
            // Look for rules marked as "MUST", "CRITICAL", "ALWAYS", etc.
            var lines = content.Split('\n');
            foreach (var line in lines)
            {
                if (Regex.IsMatch(line, @"\b(MUST|CRITICAL|ALWAYS|NEVER)\b", RegexOptions.IgnoreCase))
                {
                    criticalRules.Add(line.Trim());
                }
            }
            
            return criticalRules;
        }

        private NarrativeGuidelines ParseNarrativeGuidelines(List<string> sections)
        {
            var guidelines = new NarrativeGuidelines();
            
            foreach (var section in sections)
            {
                // Extract primary perspective
                var perspectiveMatch = Regex.Match(section, @"Primary Perspective:\s*([^.]+)");
                if (perspectiveMatch.Success)
                {
                    guidelines.PrimaryPerspective = perspectiveMatch.Groups[1].Value.Trim();
                }
                
                // Extract dialogue rules
                if (section.Contains("Dialogue", StringComparison.OrdinalIgnoreCase))
                {
                    var dialogueRules = Regex.Matches(section, @"^\s*[-•]\s*(.+)$", RegexOptions.Multiline);
                    foreach (Match match in dialogueRules)
                    {
                        guidelines.DialogueRules.Add(match.Groups[1].Value.Trim());
                    }
                }
                
                // Extract technical term handling
                var techTermMatches = Regex.Matches(section, @"(Narration|Dialogue|Exposition):\s*([^.]+\.)");
                foreach (Match match in techTermMatches)
                {
                    guidelines.TechnicalTermHandling[match.Groups[1].Value] = match.Groups[2].Value.Trim();
                }
            }
            
            return guidelines;
        }

        private SectionType DetermineSectionType(string title)
        {
            // Handle null or empty titles
            if (string.IsNullOrEmpty(title))
                return SectionType.GeneralGuidelines;
                
            if (title.Contains("Voice", StringComparison.OrdinalIgnoreCase))
                return SectionType.VoiceRules;
            if (title.Contains("Writing Sample", StringComparison.OrdinalIgnoreCase))
                return SectionType.WritingSample;
            if (title.Contains("Dialogue", StringComparison.OrdinalIgnoreCase))
                return SectionType.DialogueRules;
            if (title.Contains("Description", StringComparison.OrdinalIgnoreCase))
                return SectionType.DescriptionRules;
            if (title.Contains("Character", StringComparison.OrdinalIgnoreCase))
                return SectionType.CharacterGuidelines;
            if (title.Contains("Technical", StringComparison.OrdinalIgnoreCase))
                return SectionType.TechnicalGuidelines;
            
            return SectionType.GeneralGuidelines;
        }

        private RuleType DetermineRuleType(string ruleText)
        {
            if (Regex.IsMatch(ruleText, @"\b(must|always|never|critical)\b", RegexOptions.IgnoreCase))
                return RuleType.MustFollow;
            if (Regex.IsMatch(ruleText, @"\b(should|prefer|recommended)\b", RegexOptions.IgnoreCase))
                return RuleType.ShouldFollow;
            if (Regex.IsMatch(ruleText, @"\b(can|may|optional)\b", RegexOptions.IgnoreCase))
                return RuleType.CanConsider;
            if (Regex.IsMatch(ruleText, @"\b(avoid|don't|do not|limit)\b", RegexOptions.IgnoreCase))
                return RuleType.Restriction;
            if (Regex.IsMatch(ruleText, @"\b(e\.g\.|for example|such as)\b", RegexOptions.IgnoreCase))
                return RuleType.Example;
            
            return RuleType.ShouldFollow; // Default
        }

        private int DeterminePriority(string title, string content)
        {
            var priority = 5; // Default medium priority
            
            if (title.Contains("MUST BE FOLLOWED", StringComparison.OrdinalIgnoreCase))
                priority = 10;
            else if (title.Contains("Voice", StringComparison.OrdinalIgnoreCase))
                priority = 9;
            else if (title.Contains("Critical", StringComparison.OrdinalIgnoreCase))
                priority = 8;
            
            // Boost priority if content contains many MUST/ALWAYS rules
            var criticalWordCount = Regex.Matches(content, @"\b(MUST|ALWAYS|CRITICAL)\b", RegexOptions.IgnoreCase).Count;
            priority += Math.Min(criticalWordCount / 3, 3); // Add up to 3 extra priority points
            
            return priority;
        }

        private List<string> ExtractExamples(string ruleText)
        {
            var examples = new List<string>();
            
            // Extract content in parentheses that looks like examples
            var exampleMatches = Regex.Matches(ruleText, @"\(e\.g\.,?\s*([^)]+)\)");
            foreach (Match match in exampleMatches)
            {
                examples.Add(match.Groups[1].Value.Trim());
            }
            
            // Extract quoted examples
            var quotedMatches = Regex.Matches(ruleText, @"[""]([^""]+)[""]");
            foreach (Match match in quotedMatches)
            {
                examples.Add(match.Groups[1].Value);
            }
            
            return examples;
        }

        private List<string> ExtractRestrictions(string ruleText)
        {
            var restrictions = new List<string>();
            
            // Look for "avoid", "don't", "never" patterns
            var restrictionMatches = Regex.Matches(ruleText, 
                @"(avoid|don't|do not|never|must not)\s+([^,.]+)[,.]", 
                RegexOptions.IgnoreCase);
            
            foreach (Match match in restrictionMatches)
            {
                restrictions.Add(match.Groups[2].Value.Trim());
            }
            
            return restrictions;
        }

        private int ParseNumberWord(string word)
        {
            switch (word.ToLower())
            {
                case "one": return 1;
                case "two": return 2;
                case "three": return 3;
                case "four": return 4;
                case "five": return 5;
                default: return int.TryParse(word, out int num) ? num : -1;
            }
        }
    }
} 