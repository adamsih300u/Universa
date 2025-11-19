using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Universa.Desktop.Interfaces;

namespace Universa.Desktop.Services
{
    /// <summary>
    /// Enhanced text search service that provides fuzzy matching, context-aware search,
    /// and robust text replacement capabilities for AI Chat sidebar functionality.
    /// </summary>
    public class EnhancedTextSearchService : ITextSearchService
    {
        private const int DEFAULT_CONTEXT_RADIUS = 100;
        private const double MINIMUM_SIMILARITY_THRESHOLD = 0.6;
        private const int MAX_SEARCH_CANDIDATES = 10;
        private const int MAX_CONTENT_LENGTH_FOR_FUZZY = 50000; // Skip fuzzy search for very large content
        private const int FUZZY_SEARCH_TIMEOUT_MS = 2000; // 2 second timeout for fuzzy search

        /// <summary>
        /// Result of a text search operation
        /// </summary>
        public class SearchResult
        {
            public int Index { get; set; } = -1;
            public int Length { get; set; }
            public double Confidence { get; set; }
            public string MatchedText { get; set; }
            public string Context { get; set; }
            public bool IsExactMatch { get; set; }
            public string MatchType { get; set; }
        }

        /// <summary>
        /// Finds text in content using multiple strategies for maximum accuracy
        /// </summary>
        public SearchResult FindTextInContent(string content, string searchText, int contextRadius = DEFAULT_CONTEXT_RADIUS)
        {
            // Run async version synchronously for compatibility
            return FindTextInContentAsync(content, searchText, contextRadius, CancellationToken.None).Result;
        }

        /// <summary>
        /// Finds text in content using multiple strategies for maximum accuracy (async version)
        /// </summary>
        public async Task<SearchResult> FindTextInContentAsync(string content, string searchText, int contextRadius = DEFAULT_CONTEXT_RADIUS, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchText))
            {
                return new SearchResult { MatchType = "Empty input" };
            }

            Debug.WriteLine($"Searching for text: '{searchText.Substring(0, Math.Min(50, searchText.Length))}...'");

            try
            {
                // Strategy 1: Exact match (highest confidence) - always fast
                var exactResult = FindExactMatch(content, searchText);
                if (exactResult.Index >= 0)
                {
                    exactResult.Context = ExtractContext(content, exactResult.Index, exactResult.Length, contextRadius);
                    exactResult.MatchType = "Exact";
                    Debug.WriteLine($"Found exact match at index {exactResult.Index}");
                    return exactResult;
                }

                // Strategy 2: Case-insensitive match - fast
                var caseInsensitiveResult = FindCaseInsensitiveMatch(content, searchText);
                if (caseInsensitiveResult.Index >= 0)
                {
                    caseInsensitiveResult.Context = ExtractContext(content, caseInsensitiveResult.Index, caseInsensitiveResult.Length, contextRadius);
                    caseInsensitiveResult.MatchType = "Case-insensitive";
                    Debug.WriteLine($"Found case-insensitive match at index {caseInsensitiveResult.Index}");
                    return caseInsensitiveResult;
                }

                // Strategy 3: Normalized whitespace match - moderate speed
                var normalizedResult = await Task.Run(() => FindNormalizedWhitespaceMatch(content, searchText), cancellationToken);
                if (normalizedResult.Index >= 0)
                {
                    normalizedResult.Context = ExtractContext(content, normalizedResult.Index, normalizedResult.Length, contextRadius);
                    normalizedResult.MatchType = "Normalized whitespace";
                    Debug.WriteLine($"Found normalized whitespace match at index {normalizedResult.Index}");
                    return normalizedResult;
                }

                // Strategy 4: AI Chat specialized search - handles AI-generated text patterns
                var aiChatResult = await Task.Run(() => FindAIChatMatch(content, searchText), cancellationToken);
                if (aiChatResult.Index >= 0)
                {
                    aiChatResult.Context = ExtractContext(content, aiChatResult.Index, aiChatResult.Length, contextRadius);
                    aiChatResult.MatchType = "AI Chat pattern";
                    Debug.WriteLine($"Found AI Chat pattern match at index {aiChatResult.Index}");
                    return aiChatResult;
                }

                // Strategy 5: Partial sentence matching - moderate speed
                var sentenceResult = await Task.Run(() => FindPartialSentenceMatch(content, searchText), cancellationToken);
                if (sentenceResult.Index >= 0)
                {
                    sentenceResult.Context = ExtractContext(content, sentenceResult.Index, sentenceResult.Length, contextRadius);
                    sentenceResult.MatchType = "Partial sentence";
                    Debug.WriteLine($"Found partial sentence match at index {sentenceResult.Index}");
                    return sentenceResult;
                }

                // Strategy 6: Fuzzy matching - expensive, skip for large content or long search text
                if (content.Length <= MAX_CONTENT_LENGTH_FOR_FUZZY && searchText.Length <= 1000)
                {
                    using (var timeoutCts = new CancellationTokenSource(FUZZY_SEARCH_TIMEOUT_MS))
                    using (var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
                    {
                        try
                        {
                            var fuzzyResult = await Task.Run(() => FindFuzzyMatchWithLimits(content, searchText, combinedCts.Token), combinedCts.Token);
                            if (fuzzyResult.Index >= 0 && fuzzyResult.Confidence >= MINIMUM_SIMILARITY_THRESHOLD)
                            {
                                fuzzyResult.Context = ExtractContext(content, fuzzyResult.Index, fuzzyResult.Length, contextRadius);
                                fuzzyResult.MatchType = "Fuzzy";
                                Debug.WriteLine($"Found fuzzy match at index {fuzzyResult.Index} with confidence {fuzzyResult.Confidence:F2}");
                                return fuzzyResult;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            Debug.WriteLine("Fuzzy search timed out or was cancelled");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine($"Skipping fuzzy search: content too large ({content.Length} chars) or search text too long ({searchText.Length} chars)");
                }

                Debug.WriteLine("No match found with any strategy");
                return new SearchResult { MatchType = "No match found" };
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Search operation was cancelled");
                return new SearchResult { MatchType = "Search cancelled" };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in text search: {ex.Message}");
                return new SearchResult { MatchType = $"Search error: {ex.Message}" };
            }
        }

        /// <summary>
        /// Applies text changes with enhanced accuracy and validation
        /// </summary>
        public bool ApplyTextChanges(ref string content, string originalText, string changedText, out string errorMessage)
        {
            errorMessage = null;

            try
            {
                var searchResult = FindTextInContent(content, originalText);
                
                if (searchResult.Index < 0)
                {
                    errorMessage = $"Original text not found in document. Search attempted with multiple strategies but no suitable match was found.";
                    return false;
                }

                // CRITICAL FIX: Enhanced validation of the match before applying changes
                if (searchResult.Confidence < MINIMUM_SIMILARITY_THRESHOLD)
                {
                    errorMessage = $"Match confidence too low ({searchResult.Confidence:F2}). Manual verification recommended.";
                    return false;
                }

                // CRITICAL FIX: Validate that the matched length is reasonable compared to original text
                if (searchResult.Length < originalText.Length * 0.5)
                {
                    errorMessage = $"Matched text length ({searchResult.Length}) is significantly shorter than original text ({originalText.Length}). This could result in incomplete replacement.";
                    return false;
                }

                // Extract the actual matched text for verification
                string actualMatchedText = content.Substring(searchResult.Index, searchResult.Length);
                
                // CRITICAL FIX: Additional validation - ensure the matched text contains key phrases from original
                if (originalText.Length > 20) // Only do this check for longer texts
                {
                    // Extract first and last few words from original text
                    var originalWords = originalText.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    if (originalWords.Length >= 3)
                    {
                        string firstPhrase = string.Join(" ", originalWords.Take(2));
                        string lastPhrase = string.Join(" ", originalWords.Skip(Math.Max(0, originalWords.Length - 2)));
                        
                        // Check if matched text contains these key phrases (case insensitive)
                        if (!actualMatchedText.Contains(firstPhrase, StringComparison.OrdinalIgnoreCase) ||
                            !actualMatchedText.Contains(lastPhrase, StringComparison.OrdinalIgnoreCase))
                        {
                            errorMessage = $"Matched text doesn't contain key phrases from original text. Match may be incomplete or incorrect.";
                            Debug.WriteLine($"Original text: {originalText.Substring(0, Math.Min(100, originalText.Length))}...");
                            Debug.WriteLine($"Matched text: {actualMatchedText.Substring(0, Math.Min(100, actualMatchedText.Length))}...");
                            return false;
                        }
                    }
                }

                // Apply the replacement
                string newContent = content.Substring(0, searchResult.Index) + 
                                   changedText + 
                                   content.Substring(searchResult.Index + searchResult.Length);

                content = newContent;

                Debug.WriteLine($"Successfully applied text changes using {searchResult.MatchType} match");
                Debug.WriteLine($"Replaced: '{actualMatchedText.Substring(0, Math.Min(50, actualMatchedText.Length))}...'");
                Debug.WriteLine($"With: '{changedText.Substring(0, Math.Min(50, changedText.Length))}...'");

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = $"Error applying text changes: {ex.Message}";
                Debug.WriteLine($"Error in ApplyTextChanges: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates if an anchor text position is appropriate for insertion (not in middle of paragraphs/quotes)
        /// </summary>
        public bool ValidateAnchorTextForInsertion(string content, string anchorText, out string errorMessage, out string suggestedAnchorText)
        {
            errorMessage = null;
            suggestedAnchorText = null;

            if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(anchorText))
            {
                errorMessage = "Content or anchor text is empty";
                return false;
            }

            var searchResult = FindTextInContent(content, anchorText);
            if (searchResult.Index < 0)
            {
                errorMessage = "Anchor text not found in document";
                return false;
            }

            int insertionPoint = searchResult.Index + searchResult.Length;
            
            // First check if the anchor text itself is complete (ends appropriately)
            bool anchorEndsWell = DoesAnchorTextEndAppropriately(anchorText);
            
            // Then check if insertion point is at the end of a logical text unit
            bool isAppropriate = IsInsertionPointAppropriate(content, insertionPoint);
            
            if (!anchorEndsWell || !isAppropriate)
            {
                // Try to find a better anchor text that ends at a sentence/paragraph boundary
                string betterAnchor = FindBetterAnchorText(content, anchorText, searchResult.Index);
                if (!string.IsNullOrEmpty(betterAnchor))
                {
                    suggestedAnchorText = betterAnchor;
                    
                    if (!anchorEndsWell)
                        errorMessage = "Anchor text appears incomplete (doesn't end at sentence/paragraph boundary). Better anchor text suggested.";
                    else
                        errorMessage = "Insertion would occur in middle of paragraph/sentence. Better anchor text suggested.";
                    
                    return false;
                }
                else
                {
                    if (!anchorEndsWell)
                        errorMessage = "Anchor text appears incomplete (doesn't end at natural boundary) and no better anchor found";
                    else
                        errorMessage = "Insertion point is inappropriate (middle of paragraph/sentence) and no better anchor found";
                    
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Checks if anchor text ends at appropriate boundaries (sentence ending, paragraph break, etc.)
        /// </summary>
        private bool DoesAnchorTextEndAppropriately(string anchorText)
        {
            if (string.IsNullOrEmpty(anchorText))
                return false;

            string trimmed = anchorText.TrimEnd();
            
            // Check if ends with sentence punctuation
            if (trimmed.EndsWith(".") || trimmed.EndsWith("!") || trimmed.EndsWith("?"))
                return true;
            
            // Check if ends with dialogue
            if (trimmed.EndsWith("\"") || trimmed.EndsWith("'"))
                return true;
            
            // Check if ends with dialogue tag after quote
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"""[^""]*""\s*(he|she|they|it|\w+)\s+(said|whispered|shouted|asked|replied|answered|muttered|declared|announced)\w*\.?\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                return true;
            
            // Check if it's a short anchor (less than 15 chars) which might be intentionally minimal
            if (trimmed.Length < 15)
                return true; // Give benefit of doubt for very short anchors
            
            // Check if ends with paragraph break
            if (anchorText.EndsWith("\n\n") || anchorText.EndsWith("\r\n\r\n"))
                return true;
                
            return false; // Likely incomplete
        }

        /// <summary>
        /// Checks if an insertion point is at an appropriate location (sentence/paragraph end)
        /// </summary>
        private bool IsInsertionPointAppropriate(string content, int insertionPoint)
        {
            if (insertionPoint >= content.Length)
                return true; // End of document is always appropriate

            // Look ahead to see what comes after the insertion point
            int lookAheadLength = Math.Min(100, content.Length - insertionPoint);
            string afterText = content.Substring(insertionPoint, lookAheadLength);
            
            // Look behind to see what comes before
            int lookBehindStart = Math.Max(0, insertionPoint - 100);
            int lookBehindLength = insertionPoint - lookBehindStart;
            string beforeText = content.Substring(lookBehindStart, lookBehindLength);

            // Check if we're at the end of a sentence (followed by space and capital letter)
            if (Regex.IsMatch(afterText, @"^\s+[A-Z]"))
                return true;

            // Check if we're at the end of a paragraph (followed by double newline)
            if (Regex.IsMatch(afterText, @"^\s*\n\s*\n"))
                return true;

            // Check if we're at the end of a quote
            if (beforeText.TrimEnd().EndsWith("\"") && Regex.IsMatch(afterText, @"^\s+[A-Z]"))
                return true;

            // Check if we're at the end of dialogue
            if (beforeText.TrimEnd().EndsWith("\"") && Regex.IsMatch(afterText, @"^\s*\n"))
                return true;

            // Check if we're in the middle of a quote (bad)
            string surroundingText = beforeText + afterText;
            int quotesBefore = beforeText.Count(c => c == '\"');
            int quotesAfter = afterText.Count(c => c == '\"');
            
            // If odd number of quotes before us, we're inside a quote
            if (quotesBefore % 2 == 1)
                return false;

            // Check if we're in the middle of a sentence (no period, question, or exclamation before next capital)
            string textAroundInsertion = content.Substring(
                Math.Max(0, insertionPoint - 200), 
                Math.Min(400, content.Length - Math.Max(0, insertionPoint - 200))
            );
            
            int relativePosition = Math.Min(200, insertionPoint);
            
            // Look for sentence ending punctuation before the next capital letter
            var afterInsertionText = textAroundInsertion.Substring(relativePosition);
            var nextCapitalMatch = Regex.Match(afterInsertionText, @"[A-Z]");
            
            if (nextCapitalMatch.Success)
            {
                string textToNextCapital = afterInsertionText.Substring(0, nextCapitalMatch.Index);
                // If there's no sentence-ending punctuation before the next capital, we're mid-sentence
                if (!Regex.IsMatch(textToNextCapital, @"[.!?]"))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Tries to find a better anchor text that ends at a sentence or paragraph boundary
        /// </summary>
        private string FindBetterAnchorText(string content, string originalAnchor, int originalIndex)
        {
            // Start from the original position and look for the next sentence/paragraph ending
            int searchStart = originalIndex + originalAnchor.Length;
            int maxSearchDistance = 500; // Don't search too far
            
            for (int i = searchStart; i < Math.Min(content.Length, searchStart + maxSearchDistance); i++)
            {
                char currentChar = content[i];
                
                // Found sentence ending
                if (currentChar == '.' || currentChar == '!' || currentChar == '?')
                {
                    // Make sure it's followed by whitespace and not part of abbreviation
                    if (i + 1 < content.Length && char.IsWhiteSpace(content[i + 1]))
                    {
                        // Include the punctuation in the anchor text
                        int betterAnchorStart = Math.Max(0, originalIndex - 50); // Include some context before
                        int betterAnchorLength = (i + 1) - betterAnchorStart;
                        
                        if (betterAnchorLength > 0 && betterAnchorStart + betterAnchorLength <= content.Length)
                        {
                            string betterAnchor = content.Substring(betterAnchorStart, betterAnchorLength).Trim();
                            if (betterAnchor.Length >= 10) // Ensure meaningful anchor text
                            {
                                return betterAnchor;
                            }
                        }
                    }
                }
                
                // Found paragraph ending (double newline)
                if (currentChar == '\n' && i + 1 < content.Length && content[i + 1] == '\n')
                {
                    int betterAnchorStart = Math.Max(0, originalIndex - 50);
                    int betterAnchorLength = i - betterAnchorStart;
                    
                    if (betterAnchorLength > 0 && betterAnchorStart + betterAnchorLength <= content.Length)
                    {
                        string betterAnchor = content.Substring(betterAnchorStart, betterAnchorLength).Trim();
                        if (betterAnchor.Length >= 10)
                        {
                            return betterAnchor;
                        }
                    }
                }
            }

            return null; // No better anchor found
        }

        private SearchResult FindExactMatch(string content, string searchText)
        {
            int index = content.IndexOf(searchText, StringComparison.Ordinal);
            return new SearchResult
            {
                Index = index,
                Length = index >= 0 ? searchText.Length : 0,
                Confidence = index >= 0 ? 1.0 : 0.0,
                MatchedText = index >= 0 ? searchText : null,
                IsExactMatch = index >= 0
            };
        }

        private SearchResult FindCaseInsensitiveMatch(string content, string searchText)
        {
            int index = content.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            return new SearchResult
            {
                Index = index,
                Length = index >= 0 ? searchText.Length : 0,
                Confidence = index >= 0 ? 0.95 : 0.0,
                MatchedText = index >= 0 ? content.Substring(index, searchText.Length) : null,
                IsExactMatch = false
            };
        }

        private SearchResult FindNormalizedWhitespaceMatch(string content, string searchText)
        {
            try
            {
                // Normalize whitespace in both content and search text
                string normalizedContent = NormalizeWhitespace(content);
                string normalizedSearch = NormalizeWhitespace(searchText);

                int normalizedIndex = normalizedContent.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase);
                if (normalizedIndex < 0)
                {
                    return new SearchResult { Index = -1 };
                }

                // Map back to original content position and calculate exact length
                var (originalIndex, originalLength) = MapNormalizedToOriginalBounds(content, normalizedContent, normalizedIndex, normalizedSearch.Length);

                return new SearchResult
                {
                    Index = originalIndex,
                    Length = originalLength,
                    Confidence = 0.85,
                    MatchedText = originalIndex >= 0 && originalIndex + originalLength <= content.Length ? 
                        content.Substring(originalIndex, originalLength) : null,
                    IsExactMatch = false
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in normalized whitespace matching: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        private SearchResult FindFuzzyMatch(string content, string searchText)
        {
            var candidates = GenerateSearchCandidates(content, searchText);
            SearchResult bestMatch = new SearchResult { Index = -1, Confidence = 0.0 };

            foreach (var candidate in candidates.Take(MAX_SEARCH_CANDIDATES))
            {
                double similarity = CalculateSimilarity(searchText, candidate.Text);
                if (similarity > bestMatch.Confidence)
                {
                    // For fuzzy matches, try to find the actual bounds of the best matching subsequence
                    var (actualStart, actualLength) = FindBestMatchingSubsequence(candidate.Text, searchText);
                    
                    bestMatch = new SearchResult
                    {
                        Index = candidate.Index + actualStart,
                        Length = actualLength,
                        Confidence = similarity,
                        MatchedText = candidate.Text.Substring(actualStart, actualLength),
                        IsExactMatch = false
                    };
                }
            }

            return bestMatch;
        }

        private SearchResult FindFuzzyMatchWithLimits(string content, string searchText, CancellationToken cancellationToken)
        {
            var candidates = GenerateSearchCandidatesWithLimits(content, searchText, cancellationToken);
            SearchResult bestMatch = new SearchResult { Index = -1, Confidence = 0.0 };

            int processed = 0;
            foreach (var candidate in candidates.Take(MAX_SEARCH_CANDIDATES))
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                double similarity = CalculateSimilarityFast(searchText, candidate.Text);
                if (similarity > bestMatch.Confidence)
                {
                    // For fuzzy matches, try to find the actual bounds of the best matching subsequence
                    var (actualStart, actualLength) = FindBestMatchingSubsequence(candidate.Text, searchText);
                    
                    bestMatch = new SearchResult
                    {
                        Index = candidate.Index + actualStart,
                        Length = actualLength,
                        Confidence = similarity,
                        MatchedText = candidate.Text.Substring(actualStart, actualLength),
                        IsExactMatch = false
                    };
                }

                processed++;
                if (processed % 10 == 0) // Check cancellation periodically
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }

            return bestMatch;
        }

        private (int start, int length) FindBestMatchingSubsequence(string candidateText, string searchText)
        {
            // CRITICAL FIX: Ensure we always return a length that can encompass the full search text
            int candidateLen = candidateText.Length;
            int searchLen = searchText.Length;
            
            // Start with the assumption that we want to match the full candidate text
            int bestStart = 0;
            int bestLength = candidateLen;
            double bestScore = 0;
            
            // Try different starting positions in the candidate text
            // But ensure we don't artificially limit the length too much
            int minLength = Math.Max(searchLen / 2, Math.Min(searchLen, candidateLen));
            int maxLength = candidateLen;
            
            for (int start = 0; start <= candidateLen - minLength; start++)
            {
                for (int length = minLength; length <= maxLength - start; length++)
                {
                    if (start + length > candidateLen) break;
                    
                    string subsequence = candidateText.Substring(start, length);
                    double similarity = CalculateSimilarityFast(searchText, subsequence);
                    
                    // Weight longer matches more heavily to prefer complete coverage
                    // But also consider similarity - balance between length and accuracy
                    double lengthWeight = (double)length / searchLen;
                    double score = similarity * (0.7 + 0.3 * Math.Min(lengthWeight, 1.5)); // Prefer longer matches up to 1.5x search length
                    
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestLength = length;
                    }
                }
            }
            
            // CRITICAL FIX: If we have a very short result compared to search text, 
            // extend it to ensure we capture the full context
            if (bestLength < searchLen * 0.8 && bestStart + searchLen <= candidateLen)
            {
                int extendedLength = Math.Min(searchLen, candidateLen - bestStart);
                
                // Verify the extended length doesn't decrease similarity too much
                string extendedSubsequence = candidateText.Substring(bestStart, extendedLength);
                double extendedSimilarity = CalculateSimilarityFast(searchText, extendedSubsequence);
                
                // Use extended length if similarity is reasonable
                if (extendedSimilarity >= bestScore * 0.8) // Allow 20% decrease in similarity for better coverage
                {
                    bestLength = extendedLength;
                }
            }
            
            // Final safety check: ensure we return at least some reasonable length
            if (bestLength == 0)
            {
                bestLength = Math.Min(searchLen, candidateLen);
                bestStart = 0;
            }
            
            return (bestStart, bestLength);
        }

        /// <summary>
        /// Specialized search for AI Chat sidebar text segments
        /// Handles AI-generated text patterns, markdown formatting, and partial matches
        /// </summary>
        private SearchResult FindAIChatMatch(string content, string searchText)
        {
            try
            {
                Debug.WriteLine($"Attempting AI Chat pattern search for: {searchText.Substring(0, Math.Min(50, searchText.Length))}...");

                // Strategy 1: Strip markdown and normalize both texts
                var normalizedSearchResult = FindMarkdownNormalizedMatch(content, searchText);
                if (normalizedSearchResult.Index >= 0)
                {
                    Debug.WriteLine("Found match using markdown normalization");
                    return normalizedSearchResult;
                }

                // Strategy 2: Find by key phrase extraction
                var keyPhraseResult = FindByKeyPhrases(content, searchText);
                if (keyPhraseResult.Index >= 0)
                {
                    Debug.WriteLine("Found match using key phrase extraction");
                    return keyPhraseResult;
                }

                // Strategy 3: Semantic sentence boundary matching
                var semanticResult = FindSemanticSentenceMatch(content, searchText);
                if (semanticResult.Index >= 0)
                {
                    Debug.WriteLine("Found match using semantic sentence boundaries");
                    return semanticResult;
                }

                // Strategy 4: Flexible word order matching
                var wordOrderResult = FindFlexibleWordOrderMatch(content, searchText);
                if (wordOrderResult.Index >= 0)
                {
                    Debug.WriteLine("Found match using flexible word order");
                    return wordOrderResult;
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in AI Chat pattern matching: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        /// <summary>
        /// Strips markdown formatting and searches for normalized text
        /// </summary>
        private SearchResult FindMarkdownNormalizedMatch(string content, string searchText)
        {
            try
            {
                // Strip markdown from both content and search text
                string cleanContent = StripMarkdownFormatting(content);
                string cleanSearchText = StripMarkdownFormatting(searchText);

                // Remove quotes and emphasis markers that AI might add
                cleanSearchText = StripAIFormatting(cleanSearchText);

                // Try case-insensitive match on cleaned text
                int cleanIndex = cleanContent.IndexOf(cleanSearchText, StringComparison.OrdinalIgnoreCase);
                if (cleanIndex >= 0)
                {
                    // Map back to original content position
                    var (originalIndex, originalLength) = MapCleanToOriginalPosition(content, cleanContent, cleanIndex, cleanSearchText.Length);
                    
                    if (originalIndex >= 0)
                    {
                        return new SearchResult
                        {
                            Index = originalIndex,
                            Length = originalLength,
                            Confidence = 0.85,
                            MatchedText = content.Substring(originalIndex, originalLength),
                            IsExactMatch = false
                        };
                    }
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in markdown normalized search: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        /// <summary>
        /// Extracts key phrases and searches for them
        /// </summary>
        private SearchResult FindByKeyPhrases(string content, string searchText)
        {
            try
            {
                var keyPhrases = ExtractKeyPhrases(searchText);
                if (!keyPhrases.Any())
                {
                    return new SearchResult { Index = -1 };
                }

                // Find the best matching phrase
                foreach (var phrase in keyPhrases.OrderByDescending(p => p.Length))
                {
                    if (phrase.Length < 8) continue; // Skip very short phrases

                    int index = content.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
                    if (index >= 0)
                    {
                        // Extend the match to include more context
                        var (expandedIndex, expandedLength) = ExpandMatchContext(content, index, phrase.Length, searchText);
                        
                        return new SearchResult
                        {
                            Index = expandedIndex,
                            Length = expandedLength,
                            Confidence = 0.80,
                            MatchedText = content.Substring(expandedIndex, expandedLength),
                            IsExactMatch = false
                        };
                    }
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in key phrase search: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        /// <summary>
        /// Finds matches using semantic sentence boundaries
        /// </summary>
        private SearchResult FindSemanticSentenceMatch(string content, string searchText)
        {
            try
            {
                // Split content into semantic chunks (sentences/paragraphs)
                var contentChunks = SplitIntoSemanticChunks(content);
                var searchWords = ExtractSignificantWords(searchText);
                
                if (searchWords.Count < 2) return new SearchResult { Index = -1 };

                // Find the chunk with the highest word overlap
                double bestScore = 0;
                int bestIndex = -1;
                int bestLength = 0;

                foreach (var chunk in contentChunks)
                {
                    var chunkWords = ExtractSignificantWords(chunk.Text);
                    double overlap = CalculateWordOverlap(searchWords, chunkWords);
                    
                    if (overlap > bestScore && overlap >= 0.4) // At least 40% word overlap
                    {
                        bestScore = overlap;
                        bestIndex = chunk.Index;
                        bestLength = chunk.Text.Length;
                    }
                }

                if (bestIndex >= 0)
                {
                    return new SearchResult
                    {
                        Index = bestIndex,
                        Length = bestLength,
                        Confidence = bestScore * 0.9, // Slightly lower confidence for semantic matching
                        MatchedText = content.Substring(bestIndex, bestLength),
                        IsExactMatch = false
                    };
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in semantic sentence matching: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        /// <summary>
        /// Handles flexible word order and minor paraphrasing
        /// </summary>
        private SearchResult FindFlexibleWordOrderMatch(string content, string searchText)
        {
            try
            {
                var searchWords = ExtractSignificantWords(searchText);
                if (searchWords.Count < 3) return new SearchResult { Index = -1 };

                // Create sliding windows of text roughly the size of search text
                int windowSize = Math.Max(searchText.Length, 100);
                double bestScore = 0;
                int bestIndex = -1;
                int bestLength = 0;

                for (int i = 0; i <= content.Length - windowSize; i += windowSize / 2)
                {
                    int actualLength = Math.Min(windowSize, content.Length - i);
                    string window = content.Substring(i, actualLength);
                    var windowWords = ExtractSignificantWords(window);
                    
                    double score = CalculateWordOverlap(searchWords, windowWords);
                    if (score > bestScore && score >= 0.5) // At least 50% word overlap
                    {
                        bestScore = score;
                        bestIndex = i;
                        bestLength = actualLength;
                    }
                }

                if (bestIndex >= 0)
                {
                    // Refine the boundaries to sentence/paragraph boundaries
                    var (refinedIndex, refinedLength) = RefineToBoundaries(content, bestIndex, bestLength);
                    
                    return new SearchResult
                    {
                        Index = refinedIndex,
                        Length = refinedLength,
                        Confidence = bestScore * 0.8, // Lower confidence for flexible matching
                        MatchedText = content.Substring(refinedIndex, refinedLength),
                        IsExactMatch = false
                    };
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in flexible word order matching: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        private SearchResult FindPartialSentenceMatch(string content, string searchText)
        {
            try
            {
                // Split search text into sentences
                var searchSentences = SplitIntoSentences(searchText);
                if (!searchSentences.Any())
                {
                    return new SearchResult { Index = -1 };
                }

                // Try to find the first sentence
                string firstSentence = searchSentences.First().Trim();
                if (firstSentence.Length < 10) // Too short to be reliable
                {
                    return new SearchResult { Index = -1 };
                }

                int index = content.IndexOf(firstSentence, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // Try to extend the match to include more of the original text
                    int extendedLength = CalculateExtendedMatchLength(content, index, searchText);
                    
                    return new SearchResult
                    {
                        Index = index,
                        Length = extendedLength,
                        Confidence = 0.75,
                        MatchedText = content.Substring(index, extendedLength),
                        IsExactMatch = false
                    };
                }

                return new SearchResult { Index = -1 };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in partial sentence matching: {ex.Message}");
                return new SearchResult { Index = -1 };
            }
        }

        private string NormalizeWhitespace(string text)
        {
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        private (int originalIndex, int originalLength) MapNormalizedToOriginalBounds(string original, string normalized, int normalizedIndex, int normalizedLength)
        {
            // CRITICAL FIX: Improve mapping accuracy to ensure full text coverage
            var positionMap = new List<int>();
            int originalPos = 0;
            int normalizedPos = 0;

            // Build a comprehensive position mapping
            while (originalPos < original.Length && normalizedPos < normalized.Length)
            {
                positionMap.Add(originalPos);
                
                if (char.IsWhiteSpace(original[originalPos]))
                {
                    // Skip consecutive whitespace in original
                    while (originalPos < original.Length && char.IsWhiteSpace(original[originalPos]))
                    {
                        originalPos++;
                    }
                    
                    // Only advance normalized position if we're at a space in normalized text
                    if (normalizedPos < normalized.Length && char.IsWhiteSpace(normalized[normalizedPos]))
                    {
                        normalizedPos++; // Single space in normalized
                    }
                }
                else
                {
                    originalPos++;
                    normalizedPos++;
                }
            }
            
            // Add remaining positions for edge cases
            while (originalPos <= original.Length)
            {
                positionMap.Add(originalPos);
                originalPos++;
            }

            // CRITICAL FIX: Map the normalized match back to original bounds with safety checks
            int startIndex = normalizedIndex < positionMap.Count ? positionMap[normalizedIndex] : original.Length;
            
            // Calculate end position more carefully
            int normalizedEndIndex = normalizedIndex + normalizedLength;
            int endIndex;
            
            if (normalizedEndIndex < positionMap.Count)
            {
                endIndex = positionMap[normalizedEndIndex];
            }
            else
            {
                // If we're past the mapped positions, extend to cover the remaining text
                endIndex = original.Length;
            }
            
            // CRITICAL FIX: Ensure we don't return a zero or negative length
            int mappedLength = Math.Max(1, endIndex - startIndex);
            
            // Additional safety: if the mapped length seems too short compared to normalized length,
            // extend it to ensure we capture the full context
            if (mappedLength < normalizedLength * 0.8)
            {
                int minExpectedLength = (int)(normalizedLength * 0.9); // Allow for some compression
                int maxAvailableLength = original.Length - startIndex;
                mappedLength = Math.Min(minExpectedLength, maxAvailableLength);
            }
            
            // Final bounds check
            if (startIndex + mappedLength > original.Length)
            {
                mappedLength = original.Length - startIndex;
            }
            
            return (startIndex, Math.Max(1, mappedLength));
        }

        private int CalculateOriginalLength(string content, int startIndex, string searchText)
        {
            // This method is now deprecated - use MapNormalizedToOriginalBounds instead
            // Keeping for backward compatibility but with better estimation
            if (startIndex < 0 || startIndex >= content.Length)
                return 0;

            // For safety, try to find the actual text boundaries
            int remainingLength = content.Length - startIndex;
            int maxSearchLength = Math.Min(searchText.Length * 3, remainingLength); // Allow for whitespace expansion
            
            // Look for sentence or paragraph boundaries near the expected end
            int estimatedEnd = startIndex + Math.Min(searchText.Length, maxSearchLength);
            
            // Try to find a natural boundary (sentence end, paragraph break, etc.)
            for (int i = estimatedEnd; i < Math.Min(startIndex + maxSearchLength, content.Length); i++)
            {
                char c = content[i];
                if (c == '.' || c == '!' || c == '?' || c == '\n' || c == '\r')
                {
                    // Check if this looks like a sentence boundary
                    if (i + 1 < content.Length && (char.IsWhiteSpace(content[i + 1]) || content[i + 1] == '\n'))
                    {
                        return i - startIndex + 1;
                    }
                }
            }
            
            return Math.Min(searchText.Length, maxSearchLength);
        }

        private List<(int Index, string Text)> GenerateSearchCandidates(string content, string searchText)
        {
            var candidates = new List<(int Index, string Text)>();
            int searchLength = searchText.Length;
            int tolerance = Math.Max(searchLength / 4, 20); // 25% tolerance or minimum 20 chars

            // Generate candidates of varying lengths around the expected length
            for (int length = searchLength - tolerance; length <= searchLength + tolerance; length += 10)
            {
                if (length <= 0) continue;

                for (int i = 0; i <= content.Length - length; i += Math.Max(1, length / 4))
                {
                    candidates.Add((i, content.Substring(i, Math.Min(length, content.Length - i))));
                    
                    if (candidates.Count > MAX_SEARCH_CANDIDATES * 3) // Limit candidate generation
                        break;
                }
                
                if (candidates.Count > MAX_SEARCH_CANDIDATES * 3)
                    break;
            }

            return candidates;
        }

        private List<(int Index, string Text)> GenerateSearchCandidatesWithLimits(string content, string searchText, CancellationToken cancellationToken)
        {
            var candidates = new List<(int Index, string Text)>();
            int searchLength = searchText.Length;
            int tolerance = Math.Min(searchLength / 4, 50); // Smaller tolerance for performance
            int maxCandidates = MAX_SEARCH_CANDIDATES * 2; // Reduced limit

            // Use larger steps for performance
            int stepSize = Math.Max(searchLength / 8, 10);

            // Generate candidates more efficiently
            for (int length = searchLength - tolerance; length <= searchLength + tolerance; length += 20)
            {
                if (length <= 0) continue;
                cancellationToken.ThrowIfCancellationRequested();

                for (int i = 0; i <= content.Length - length; i += stepSize)
                {
                    candidates.Add((i, content.Substring(i, Math.Min(length, content.Length - i))));
                    
                    if (candidates.Count >= maxCandidates)
                        return candidates;
                    
                    if (candidates.Count % 50 == 0) // Check cancellation periodically
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
            }

            return candidates;
        }

        private double CalculateSimilarity(string text1, string text2)
        {
            // Use Levenshtein distance for similarity calculation
            int distance = CalculateLevenshteinDistance(text1, text2);
            int maxLength = Math.Max(text1.Length, text2.Length);
            
            if (maxLength == 0) return 1.0;
            
            return 1.0 - (double)distance / maxLength;
        }

        private double CalculateSimilarityFast(string text1, string text2)
        {
            // Fast similarity calculation using character frequency comparison
            if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2)) return 1.0;
            if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return 0.0;
            
            // For very long strings, use sampling
            if (text1.Length > 500 || text2.Length > 500)
            {
                return CalculateJaccardSimilarity(text1, text2);
            }
            
            // For shorter strings, use optimized Levenshtein with early termination
            return CalculateSimilarity(text1, text2);
        }

        private double CalculateJaccardSimilarity(string text1, string text2)
        {
            // Create character frequency maps
            var freq1 = new Dictionary<char, int>();
            var freq2 = new Dictionary<char, int>();
            
            foreach (char c in text1.ToLowerInvariant())
            {
                freq1[c] = freq1.GetValueOrDefault(c, 0) + 1;
            }
            
            foreach (char c in text2.ToLowerInvariant())
            {
                freq2[c] = freq2.GetValueOrDefault(c, 0) + 1;
            }
            
            // Calculate Jaccard similarity
            var allChars = freq1.Keys.Union(freq2.Keys);
            int intersection = 0;
            int union = 0;
            
            foreach (var c in allChars)
            {
                int count1 = freq1.GetValueOrDefault(c, 0);
                int count2 = freq2.GetValueOrDefault(c, 0);
                intersection += Math.Min(count1, count2);
                union += Math.Max(count1, count2);
            }
            
            return union == 0 ? 0.0 : (double)intersection / union;
        }

        private int CalculateLevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return s2?.Length ?? 0;
            if (string.IsNullOrEmpty(s2)) return s1.Length;

            int[,] matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[s1.Length, s2.Length];
        }

        private List<string> SplitIntoSentences(string text)
        {
            return Regex.Split(text, @"(?<=[.!?])\s+")
                       .Where(s => !string.IsNullOrWhiteSpace(s))
                       .ToList();
        }

        private int CalculateExtendedMatchLength(string content, int startIndex, string searchText)
        {
            if (startIndex < 0 || startIndex >= content.Length)
                return 0;

            int searchLen = searchText.Length;
            int maxLength = Math.Min(searchText.Length * 2, content.Length - startIndex);
            
            // CRITICAL FIX: Start with search text length as baseline, then optimize
            int bestLength = Math.Min(searchLen, maxLength);
            double bestSimilarity = 0;

            // Calculate baseline similarity with search text length
            if (startIndex + searchLen <= content.Length)
            {
                string baseline = content.Substring(startIndex, searchLen);
                bestSimilarity = CalculateSimilarity(searchText, baseline);
            }

            // Try different lengths to find the best match, but be more conservative
            int minLength = Math.Max(searchLen / 2, 10);
            int step = Math.Max(1, maxLength / 50); // Smaller steps for better accuracy

            for (int length = minLength; length <= maxLength; length += step)
            {
                if (startIndex + length > content.Length) break;

                string candidate = content.Substring(startIndex, length);
                double similarity = CalculateSimilarity(searchText, candidate);
                
                // CRITICAL FIX: Weight similarity more heavily than length to avoid over-extension
                // Only accept longer matches if they significantly improve similarity
                if (similarity > bestSimilarity + 0.05 || // Require 5% improvement in similarity
                    (similarity >= bestSimilarity && length <= searchLen * 1.2)) // Or allow minor extensions
                {
                    bestSimilarity = similarity;
                    bestLength = length;
                }
                
                // Stop early if we find a very good match to avoid over-extending
                if (similarity > 0.95) break;
            }

            // CRITICAL FIX: Look for natural boundaries only within a reasonable range
            if (bestLength > 0 && startIndex + bestLength < content.Length)
            {
                int maxBoundarySearch = Math.Min(30, content.Length - (startIndex + bestLength));
                
                for (int i = 0; i < maxBoundarySearch; i++)
                {
                    char c = content[startIndex + bestLength + i];
                    
                    // Look for sentence endings first (highest priority)
                    if (c == '.' || c == '!' || c == '?')
                    {
                        // Check if this looks like a sentence boundary
                        if (startIndex + bestLength + i + 1 < content.Length && 
                            (char.IsWhiteSpace(content[startIndex + bestLength + i + 1]) || 
                             content[startIndex + bestLength + i + 1] == '\n'))
                        {
                            return bestLength + i + 1;
                        }
                    }
                    // Look for paragraph breaks (medium priority) 
                    else if (c == '\n' || c == '\r')
                    {
                        return bestLength + i;
                    }
                    // Look for word boundaries only if we haven't extended too far
                    else if (char.IsWhiteSpace(c) && i <= 15)
                    {
                        // Only use word boundary if it's close to our best match
                        int proposedLength = bestLength + i;
                        if (proposedLength <= searchLen * 1.3) // Don't extend more than 30% beyond search length
                        {
                            return proposedLength;
                        }
                    }
                }
            }

            // CRITICAL FIX: Ensure we never return a length longer than reasonable
            int maxReasonableLength = Math.Min((int)(searchLen * 1.5), content.Length - startIndex);
            return Math.Min(bestLength, maxReasonableLength);
        }

        private string ExtractContext(string content, int index, int length, int contextRadius)
        {
            int start = Math.Max(0, index - contextRadius);
            int end = Math.Min(content.Length, index + length + contextRadius);
            
            var context = new StringBuilder();
            
            if (start > 0)
                context.Append("...");
            
            context.Append(content.Substring(start, end - start));
            
            if (end < content.Length)
                context.Append("...");
            
            return context.ToString();
        }

        #region AI Chat Search Helper Methods

        /// <summary>
        /// Strips markdown formatting from text
        /// </summary>
        private string StripMarkdownFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var cleaned = text;
            
            // Remove headers (# ## ###)
            cleaned = Regex.Replace(cleaned, @"^#+\s*", "", RegexOptions.Multiline);
            
            // Remove bold/italic (**text** *text*)
            cleaned = Regex.Replace(cleaned, @"\*{1,2}([^*]+)\*{1,2}", "$1");
            
            // Remove code blocks (```text```)
            cleaned = Regex.Replace(cleaned, @"```[^`]*```", "");
            
            // Remove inline code (`text`)
            cleaned = Regex.Replace(cleaned, @"`([^`]+)`", "$1");
            
            // Remove links ([text](url))
            cleaned = Regex.Replace(cleaned, @"\[([^\]]+)\]\([^)]+\)", "$1");
            
            // Remove blockquotes (> text)
            cleaned = Regex.Replace(cleaned, @"^>\s*", "", RegexOptions.Multiline);
            
            return cleaned.Trim();
        }

        /// <summary>
        /// Strips AI-added formatting like quotes and emphasis
        /// </summary>
        private string StripAIFormatting(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var cleaned = text;
            
            // Remove quotes that AI might add
            cleaned = Regex.Replace(cleaned, @"^[""](.*)[""]$", "$1");
            cleaned = Regex.Replace(cleaned, @"^'(.*)'$", "$1");
            
            // Remove emphasis markers
            cleaned = Regex.Replace(cleaned, @"\s*\(emphasis added\)\s*", " ");
            cleaned = Regex.Replace(cleaned, @"\s*\[emphasis mine\]\s*", " ");
            
            return cleaned.Trim();
        }

        /// <summary>
        /// Maps position from cleaned text back to original text
        /// </summary>
        private (int originalIndex, int originalLength) MapCleanToOriginalPosition(string original, string cleaned, int cleanIndex, int cleanLength)
        {
            try
            {
                // Simple approximation - find the cleaned substring in original
                string searchSubstring = cleaned.Substring(cleanIndex, Math.Min(cleanLength, cleaned.Length - cleanIndex));
                
                // Try to find this substring in the original
                var normalizedOriginal = NormalizeWhitespace(original);
                var normalizedSearch = NormalizeWhitespace(searchSubstring);
                
                int index = normalizedOriginal.IndexOf(normalizedSearch, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                {
                    // Map back to original position
                    return MapNormalizedToOriginalBounds(original, normalizedOriginal, index, normalizedSearch.Length);
                }
                
                return (-1, 0);
            }
            catch
            {
                return (-1, 0);
            }
        }

        /// <summary>
        /// Extracts key phrases from text
        /// </summary>
        private List<string> ExtractKeyPhrases(string text)
        {
            var phrases = new List<string>();
            
            // Split into sentences and extract meaningful phrases
            var sentences = SplitIntoSentences(text);
            
            foreach (var sentence in sentences)
            {
                // Extract phrases of 3-8 words
                var words = sentence.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                for (int i = 0; i <= words.Length - 3; i++)
                {
                    for (int len = 3; len <= Math.Min(8, words.Length - i); len++)
                    {
                        var phrase = string.Join(" ", words.Skip(i).Take(len)).Trim();
                        if (phrase.Length >= 15 && !IsStopWordPhrase(phrase))
                        {
                            phrases.Add(phrase);
                        }
                    }
                }
            }
            
            return phrases.Distinct().ToList();
        }

        /// <summary>
        /// Expands a match to include more context
        /// </summary>
        private (int expandedIndex, int expandedLength) ExpandMatchContext(string content, int startIndex, int matchLength, string originalSearch)
        {
            // Try to expand to sentence boundaries
            int expandedStart = startIndex;
            int expandedEnd = startIndex + matchLength;
            
            // Expand backward to sentence start
            for (int i = startIndex - 1; i >= 0 && i >= startIndex - 200; i--)
            {
                if (content[i] == '.' || content[i] == '!' || content[i] == '?' || content[i] == '\n')
                {
                    expandedStart = i + 1;
                    break;
                }
            }
            
            // Expand forward to sentence end
            for (int i = startIndex + matchLength; i < content.Length && i <= startIndex + matchLength + 200; i++)
            {
                if (content[i] == '.' || content[i] == '!' || content[i] == '?' || content[i] == '\n')
                {
                    expandedEnd = i + 1;
                    break;
                }
            }
            
            // Ensure we don't expand beyond reasonable bounds
            int maxLength = Math.Min(originalSearch.Length * 3, 1000);
            if (expandedEnd - expandedStart > maxLength)
            {
                expandedEnd = expandedStart + maxLength;
            }
            
            return (expandedStart, expandedEnd - expandedStart);
        }

        /// <summary>
        /// Splits content into semantic chunks
        /// </summary>
        private List<(int Index, string Text)> SplitIntoSemanticChunks(string content)
        {
            var chunks = new List<(int, string)>();
            
            // Split by paragraphs first
            var paragraphs = content.Split(new string[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            int currentIndex = 0;
            
            foreach (var paragraph in paragraphs)
            {
                int paragraphIndex = content.IndexOf(paragraph, currentIndex);
                if (paragraphIndex >= 0)
                {
                    // If paragraph is long, split into sentences
                    if (paragraph.Length > 300)
                    {
                        var sentences = SplitIntoSentences(paragraph);
                        int sentenceIndex = paragraphIndex;
                        
                        foreach (var sentence in sentences)
                        {
                            int sentencePos = content.IndexOf(sentence, sentenceIndex);
                            if (sentencePos >= 0)
                            {
                                chunks.Add((sentencePos, sentence));
                                sentenceIndex = sentencePos + sentence.Length;
                            }
                        }
                    }
                    else
                    {
                        chunks.Add((paragraphIndex, paragraph));
                    }
                    
                    currentIndex = paragraphIndex + paragraph.Length;
                }
            }
            
            return chunks;
        }

        /// <summary>
        /// Extracts significant words (filters out stop words)
        /// </summary>
        private HashSet<string> ExtractSignificantWords(string text)
        {
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by",
                "is", "are", "was", "were", "be", "been", "have", "has", "had", "do", "does", "did",
                "will", "would", "could", "should", "may", "might", "can", "this", "that", "these", "those",
                "i", "you", "he", "she", "it", "we", "they", "me", "him", "her", "us", "them"
            };
            
            var words = Regex.Matches(text, @"\b\w{3,}\b")
                            .Cast<Match>()
                            .Select(m => m.Value.ToLowerInvariant())
                            .Where(w => !stopWords.Contains(w))
                            .ToHashSet();
            
            return words;
        }

        /// <summary>
        /// Calculates word overlap percentage between two sets
        /// </summary>
        private double CalculateWordOverlap(HashSet<string> words1, HashSet<string> words2)
        {
            if (!words1.Any() || !words2.Any()) return 0.0;
            
            var intersection = words1.Intersect(words2).Count();
            var union = words1.Union(words2).Count();
            
            return (double)intersection / union;
        }

        /// <summary>
        /// Refines match boundaries to sentence/paragraph boundaries
        /// </summary>
        private (int refinedIndex, int refinedLength) RefineToBoundaries(string content, int startIndex, int length)
        {
            int refinedStart = startIndex;
            int refinedEnd = startIndex + length;
            
            // Find sentence boundaries
            for (int i = startIndex - 1; i >= 0 && i >= startIndex - 100; i--)
            {
                if (char.IsWhiteSpace(content[i]) && i + 1 < content.Length && char.IsUpper(content[i + 1]))
                {
                    refinedStart = i + 1;
                    break;
                }
            }
            
            for (int i = startIndex + length; i < content.Length && i <= startIndex + length + 100; i++)
            {
                if (content[i] == '.' || content[i] == '!' || content[i] == '?')
                {
                    refinedEnd = i + 1;
                    break;
                }
            }
            
            return (refinedStart, refinedEnd - refinedStart);
        }

        /// <summary>
        /// Checks if a phrase consists mainly of stop words
        /// </summary>
        private bool IsStopWordPhrase(string phrase)
        {
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var significantWords = ExtractSignificantWords(phrase);
            
            return significantWords.Count < words.Length * 0.3; // Less than 30% significant words
        }

        #endregion
    }
} 