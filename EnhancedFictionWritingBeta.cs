using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Universa.Desktop.Models;
using Universa.Desktop.Services;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Example of how to integrate the StyleGuideParser into FictionWritingBeta
    /// </summary>
    public partial class EnhancedFictionWritingBeta : BaseLangChainService
    {
        private readonly StyleGuideParser _styleParser;
        private StyleGuideParser.ParsedStyleGuide _parsedStyle;
        private readonly StyleAnalyzer _styleAnalyzer;
        
        // Enhanced style processing
        private async Task ProcessStyleReference(string refPath)
        {
            try
            {
                string styleContent = await _fileReferenceService.GetFileContent(refPath, _currentFilePath);
                if (!string.IsNullOrEmpty(styleContent))
                {
                    // Parse the style guide into structured components
                    _parsedStyle = _styleParser.Parse(styleContent);
                    
                    // Analyze the writing sample for key characteristics
                    if (!string.IsNullOrEmpty(_parsedStyle.WritingSample))
                    {
                        _styleAnalyzer.AnalyzeSample(_parsedStyle.WritingSample);
                    }
                    
                    System.Diagnostics.Debug.WriteLine($"Parsed style guide with {_parsedStyle.Sections.Count} sections");
                    System.Diagnostics.Debug.WriteLine($"Found {_parsedStyle.AllRules.Count} total rules");
                    System.Diagnostics.Debug.WriteLine($"Writing sample length: {_parsedStyle.WritingSample?.Length ?? 0} chars");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error processing style guide: {ex.Message}");
            }
        }
        
        // Build a more intelligent system prompt using parsed style
        private string BuildEnhancedFictionPrompt(string request)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("You are an AI assistant specialized in helping users write and edit fiction.");
            
            // Add parsed style information in priority order
            if (_parsedStyle != null)
            {
                // Skip the problematic parsed critical rules - use raw style guide instead
                
                // 2. Writing sample with analysis
                if (!string.IsNullOrEmpty(_parsedStyle.WritingSample))
                {
                    prompt.AppendLine("\n=== WRITING SAMPLE TO EMULATE ===");
                    prompt.AppendLine("Study this sample carefully and match its:");
                    
                    if (_styleAnalyzer != null && _styleAnalyzer.HasAnalysis)
                    {
                        prompt.AppendLine($"• Sentence structure: {_styleAnalyzer.GetSentenceStructureDescription()}");
                        prompt.AppendLine($"• Narrative voice: {_styleAnalyzer.GetNarrativeVoiceDescription()}");
                        prompt.AppendLine($"• Descriptive style: {_styleAnalyzer.GetDescriptiveStyleDescription()}");
                        prompt.AppendLine($"• Pacing: {_styleAnalyzer.GetPacingDescription()}");
                    }
                    
                    prompt.AppendLine("\n[SAMPLE START]");
                    prompt.AppendLine(_parsedStyle.WritingSample);
                    prompt.AppendLine("[SAMPLE END]");
                }
                
                // 3. Narrative-specific guidelines
                if (_parsedStyle.NarrativeRules != null)
                {
                    prompt.AppendLine("\n=== NARRATIVE GUIDELINES ===");
                    prompt.AppendLine($"Primary perspective: {_parsedStyle.NarrativeRules.PrimaryPerspective}");
                    
                    if (_parsedStyle.NarrativeRules.TechnicalTermHandling.Any())
                    {
                        prompt.AppendLine("\nTechnical term handling:");
                        foreach (var kvp in _parsedStyle.NarrativeRules.TechnicalTermHandling)
                        {
                            prompt.AppendLine($"• In {kvp.Key}: {kvp.Value}");
                        }
                    }
                }
                
                // 4. Vocabulary alternatives
                if (_parsedStyle.VocabularyAlternatives.Any())
                {
                    prompt.AppendLine("\n=== VOCABULARY VARIATION ===");
                    prompt.AppendLine("Use these alternatives to avoid repetition:");
                    foreach (var kvp in _parsedStyle.VocabularyAlternatives)
                    {
                        prompt.AppendLine($"• Instead of '{kvp.Key}': {string.Join(", ", kvp.Value)}");
                    }
                }
                
                // 5. Restrictions and limits
                var restrictions = _parsedStyle.AllRules
                    .Where(r => r.Type == StyleGuideParser.RuleType.Restriction || r.MaxOccurrences > 0)
                    .ToList();
                
                if (restrictions.Any())
                {
                    prompt.AppendLine("\n=== RESTRICTIONS ===");
                    foreach (var restriction in restrictions)
                    {
                        if (restriction.MaxOccurrences > 0)
                        {
                            prompt.AppendLine($"• {restriction.RuleText} (Max {restriction.MaxOccurrences} per scene)");
                        }
                        else
                        {
                            prompt.AppendLine($"• {restriction.RuleText}");
                        }
                    }
                }
            }
            
            // Add other components (rules, outline, etc.) as before
            if (!string.IsNullOrEmpty(_rules))
            {
                prompt.AppendLine("\n=== STORY RULES ===");
                prompt.AppendLine(_rules);
            }
            
            return prompt.ToString();
        }
        
        // Context-aware rule application
        private string ApplyStyleRulesToResponse(string response, string userRequest)
        {
            if (_parsedStyle == null) return response;
            
            var adjustedResponse = response;
            
            // Check for rule violations
            var violations = new List<string>();
            
            // Example: Check metaphor limits
            var metaphorRule = _parsedStyle.AllRules
                .FirstOrDefault(r => r.RuleText.Contains("profession-specific metaphors") && r.MaxOccurrences > 0);
            
            if (metaphorRule != null)
            {
                var metaphorCount = CountProfessionSpecificMetaphors(adjustedResponse);
                if (metaphorCount > metaphorRule.MaxOccurrences)
                {
                    violations.Add($"Too many profession-specific metaphors ({metaphorCount} found, max {metaphorRule.MaxOccurrences})");
                    // Could automatically reduce them here
                }
            }
            
            // Check vocabulary repetition
            foreach (var term in _parsedStyle.VocabularyAlternatives.Keys)
            {
                var count = CountOccurrences(adjustedResponse, term);
                if (count > 2) // Example threshold
                {
                    // Replace some occurrences with alternatives
                    adjustedResponse = ReplaceWithAlternatives(adjustedResponse, term, 
                        _parsedStyle.VocabularyAlternatives[term], count - 2);
                }
            }
            
            return adjustedResponse;
        }
        
        // Helper class for style analysis
        private class StyleAnalyzer
        {
            public bool HasAnalysis { get; private set; }
            private StyleMetrics _metrics;
            
            public void AnalyzeSample(string writingSample)
            {
                _metrics = new StyleMetrics();
                
                // Analyze sentence structure
                var sentences = SplitIntoSentences(writingSample);
                _metrics.AverageSentenceLength = sentences.Average(s => s.Split(' ').Length);
                _metrics.SentenceLengthVariation = CalculateStandardDeviation(sentences.Select(s => s.Split(' ').Length));
                
                // Analyze paragraph structure
                var paragraphs = writingSample.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
                _metrics.AverageParagraphLength = paragraphs.Average(p => p.Split(' ').Length);
                
                // Analyze descriptive density
                _metrics.AdjectiveRatio = CountAdjectives(writingSample) / (double)CountWords(writingSample);
                _metrics.SensoryDetailFrequency = CountSensoryDetails(writingSample) / (double)sentences.Length;
                
                // Analyze dialogue vs narration
                _metrics.DialogueRatio = CalculateDialogueRatio(writingSample);
                
                HasAnalysis = true;
            }
            
            public string GetSentenceStructureDescription()
            {
                if (!HasAnalysis) return "Not analyzed";
                
                if (_metrics.AverageSentenceLength < 10)
                    return "Short, punchy sentences";
                else if (_metrics.AverageSentenceLength < 20)
                    return "Medium-length, balanced sentences";
                else
                    return "Complex, flowing sentences";
            }
            
            public string GetNarrativeVoiceDescription()
            {
                if (!HasAnalysis) return "Not analyzed";
                
                // This would be more sophisticated in practice
                return "Third person limited, deep character immersion";
            }
            
            public string GetDescriptiveStyleDescription()
            {
                if (!HasAnalysis) return "Not analyzed";
                
                if (_metrics.AdjectiveRatio > 0.15)
                    return "Rich, detailed descriptions";
                else if (_metrics.AdjectiveRatio > 0.08)
                    return "Moderate descriptive detail";
                else
                    return "Sparse, action-focused descriptions";
            }
            
            public string GetPacingDescription()
            {
                if (!HasAnalysis) return "Not analyzed";
                
                if (_metrics.SentenceLengthVariation > 10)
                    return "Variable pacing with dramatic rhythm";
                else
                    return "Steady, consistent pacing";
            }
            
            private class StyleMetrics
            {
                public double AverageSentenceLength { get; set; }
                public double SentenceLengthVariation { get; set; }
                public double AverageParagraphLength { get; set; }
                public double AdjectiveRatio { get; set; }
                public double SensoryDetailFrequency { get; set; }
                public double DialogueRatio { get; set; }
            }
            
            // Helper methods would go here...
            private List<string> SplitIntoSentences(string text)
            {
                // Simplified - real implementation would be more sophisticated
                return text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => s.Length > 0)
                          .ToList();
            }
            
            private double CalculateStandardDeviation(IEnumerable<int> values)
            {
                var avg = values.Average();
                var sum = values.Sum(v => Math.Pow(v - avg, 2));
                return Math.Sqrt(sum / values.Count());
            }
            
            private int CountWords(string text)
            {
                return text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            }
            
            private int CountAdjectives(string text)
            {
                // Simplified - would use NLP in practice
                return 0;
            }
            
            private int CountSensoryDetails(string text)
            {
                // Count words related to senses
                var sensoryWords = new[] { "saw", "heard", "felt", "smelled", "tasted", "touched" };
                return sensoryWords.Sum(word => CountOccurrences(text.ToLower(), word));
            }
            
            private double CalculateDialogueRatio(string text)
            {
                // Count quoted text vs total
                var dialogueChars = 0;
                var inQuotes = false;
                foreach (var c in text)
                {
                    if (c == '"') inQuotes = !inQuotes;
                    if (inQuotes) dialogueChars++;
                }
                return dialogueChars / (double)text.Length;
            }
            
            private int CountOccurrences(string text, string word)
            {
                return (text.Length - text.Replace(word, "").Length) / word.Length;
            }
        }
        
        // Placeholder methods for demonstration
        private int CountProfessionSpecificMetaphors(string text)
        {
            // This would use NLP or pattern matching to identify profession-specific metaphors
            return 0;
        }
        
        private int CountOccurrences(string text, string term)
        {
            return (text.Length - text.Replace(term, "", StringComparison.OrdinalIgnoreCase).Length) / term.Length;
        }
        
        private string ReplaceWithAlternatives(string text, string term, List<string> alternatives, int replacementCount)
        {
            // Smart replacement that maintains grammar and context
            // This is a simplified version - real implementation would be more sophisticated
            var result = text;
            var random = new Random();
            
            for (int i = 0; i < replacementCount && alternatives.Any(); i++)
            {
                var alternative = alternatives[random.Next(alternatives.Count)];
                // Replace first occurrence
                var index = result.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    result = result.Substring(0, index) + alternative + result.Substring(index + term.Length);
                }
            }
            
            return result;
        }
    }
} 