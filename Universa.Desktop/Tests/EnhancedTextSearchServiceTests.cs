using System;
using System.Diagnostics;
using Universa.Desktop.Services;

namespace Universa.Desktop.Tests
{
    /// <summary>
    /// Test class for validating EnhancedTextSearchService functionality
    /// </summary>
    public static class EnhancedTextSearchServiceTests
    {
        public static void RunAllTests()
        {
            Debug.WriteLine("=== Running EnhancedTextSearchService Tests ===");
            
            TestExactMatch();
            TestCaseInsensitiveMatch();
            TestWhitespaceNormalization();
            TestFuzzyMatching();
            TestPartialSentenceMatch();
            TestApplyChanges();
            TestEdgeCases();
            
            // NEW TESTS: Validate the critical fixes for length calculation issues
            TestLengthCalculationConsistency();
            TestNormalizedWhitespaceLengthAccuracy();
            TestFuzzyMatchLengthCoverage();
            TestApplyChangesWithLengthValidation();
            
            Debug.WriteLine("=== All tests completed ===");
        }

        private static void TestExactMatch()
        {
            Debug.WriteLine("Testing exact match...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog.";
            string searchText = "brown fox";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index == 10, $"Expected index 10, got {result.Index}");
            Assert(result.IsExactMatch, "Expected exact match");
            Assert(result.Confidence == 1.0, $"Expected confidence 1.0, got {result.Confidence}");
            Assert(result.MatchType == "Exact", $"Expected 'Exact', got '{result.MatchType}'");
            
            Debug.WriteLine("✓ Exact match test passed");
        }

        private static void TestCaseInsensitiveMatch()
        {
            Debug.WriteLine("Testing case insensitive match...");
            var service = new EnhancedTextSearchService();
            
            string content = "The Quick Brown Fox Jumps Over The Lazy Dog.";
            string searchText = "quick brown fox";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index == 4, $"Expected index 4, got {result.Index}");
            Assert(!result.IsExactMatch, "Expected non-exact match");
            Assert(result.Confidence == 0.95, $"Expected confidence 0.95, got {result.Confidence}");
            Assert(result.MatchType == "Case-insensitive", $"Expected 'Case-insensitive', got '{result.MatchType}'");
            
            Debug.WriteLine("✓ Case insensitive match test passed");
        }

        private static void TestWhitespaceNormalization()
        {
            Debug.WriteLine("Testing whitespace normalization...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick    brown\n\nfox jumps\tover the lazy dog.";
            string searchText = "quick brown fox jumps";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected match found, got index {result.Index}");
            Assert(result.MatchType == "Normalized whitespace", $"Expected 'Normalized whitespace', got '{result.MatchType}'");
            Assert(result.Confidence == 0.85, $"Expected confidence 0.85, got {result.Confidence}");
            
            Debug.WriteLine("✓ Whitespace normalization test passed");
        }

        private static void TestFuzzyMatching()
        {
            Debug.WriteLine("Testing fuzzy matching...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog.";
            string searchText = "quick bown fox jumps"; // "brown" misspelled as "bown"
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected fuzzy match found, got index {result.Index}");
            Assert(result.MatchType == "Fuzzy", $"Expected 'Fuzzy', got '{result.MatchType}'");
            Assert(result.Confidence >= 0.6, $"Expected confidence >= 0.6, got {result.Confidence}");
            
            Debug.WriteLine("✓ Fuzzy matching test passed");
        }

        private static void TestPartialSentenceMatch()
        {
            Debug.WriteLine("Testing partial sentence match...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog. The fox was very clever.";
            string searchText = "The quick brown fox jumps over the lazy dog. The fox was extremely clever.";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected partial sentence match found, got index {result.Index}");
            Assert(result.MatchType == "Partial sentence", $"Expected 'Partial sentence', got '{result.MatchType}'");
            
            Debug.WriteLine("✓ Partial sentence match test passed");
        }

        private static void TestApplyChanges()
        {
            Debug.WriteLine("Testing apply changes...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog.";
            string originalText = "brown fox";
            string changedText = "red fox";
            
            bool success = service.ApplyTextChanges(ref content, originalText, changedText, out string errorMessage);
            
            Assert(success, $"Expected success, got failure: {errorMessage}");
            Assert(content.Contains("red fox"), "Expected content to contain 'red fox'");
            Assert(!content.Contains("brown fox"), "Expected content to not contain 'brown fox'");
            
            Debug.WriteLine("✓ Apply changes test passed");
        }

        private static void TestEdgeCases()
        {
            Debug.WriteLine("Testing edge cases...");
            var service = new EnhancedTextSearchService();
            
            // Test empty content
            var result1 = service.FindTextInContent("", "test");
            Assert(result1.Index == -1, "Expected no match for empty content");
            
            // Test empty search text
            var result2 = service.FindTextInContent("test content", "");
            Assert(result2.Index == -1, "Expected no match for empty search text");
            
            // Test text not found
            var result3 = service.FindTextInContent("The quick brown fox", "elephant");
            Assert(result3.Index == -1, "Expected no match for non-existent text");
            
            // Test apply changes with non-existent text
            string content = "The quick brown fox";
            bool success = service.ApplyTextChanges(ref content, "elephant", "mouse", out string errorMessage);
            Assert(!success, "Expected failure for non-existent text");
            Assert(!string.IsNullOrEmpty(errorMessage), "Expected error message");
            
            Debug.WriteLine("✓ Edge cases test passed");
        }

        // NEW TESTS FOR CRITICAL FIXES

        private static void TestLengthCalculationConsistency()
        {
            Debug.WriteLine("Testing length calculation consistency across search strategies...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog. The fox was very clever and agile.";
            string searchText = "quick brown fox jumps over the lazy dog";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected match found, got index {result.Index}");
            
            // CRITICAL TEST: Ensure the matched length is reasonable compared to search text
            Assert(result.Length >= searchText.Length * 0.8, 
                $"Matched length ({result.Length}) should be at least 80% of search text length ({searchText.Length})");
            
            // CRITICAL TEST: Ensure the matched text can encompass the full original text
            string matchedText = content.Substring(result.Index, result.Length);
            Assert(matchedText.Length >= searchText.Length * 0.8, 
                $"Matched text length ({matchedText.Length}) should adequately cover search text ({searchText.Length})");
            
            Debug.WriteLine($"✓ Length calculation consistency test passed - Length: {result.Length}, Match type: {result.MatchType}");
        }

        private static void TestNormalizedWhitespaceLengthAccuracy()
        {
            Debug.WriteLine("Testing normalized whitespace length accuracy...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick    brown\n\nfox jumps\tover\r\n\r\nthe lazy dog.";
            string searchText = "quick brown fox jumps over the lazy dog";
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected normalized whitespace match found, got index {result.Index}");
            Assert(result.MatchType == "Normalized whitespace", $"Expected 'Normalized whitespace', got '{result.MatchType}'");
            
            // CRITICAL TEST: The matched text should encompass all the words from the search text
            string matchedText = content.Substring(result.Index, result.Length);
            string[] searchWords = searchText.Split(' ');
            
            foreach (string word in searchWords)
            {
                Assert(matchedText.Contains(word, StringComparison.OrdinalIgnoreCase), 
                    $"Matched text should contain word '{word}'. Matched: '{matchedText}'");
            }
            
            Debug.WriteLine($"✓ Normalized whitespace length accuracy test passed - Matched text covers all search words");
        }

        private static void TestFuzzyMatchLengthCoverage()
        {
            Debug.WriteLine("Testing fuzzy match length coverage...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumped over the lazy dog yesterday.";
            string searchText = "quick brown fox jumps over the lazy dog"; // "jumped" vs "jumps"
            
            var result = service.FindTextInContent(content, searchText);
            
            Assert(result.Index >= 0, $"Expected fuzzy match found, got index {result.Index}");
            
            // CRITICAL TEST: For fuzzy matches, ensure we don't under-match
            Assert(result.Length >= searchText.Length * 0.7, 
                $"Fuzzy match length ({result.Length}) should be at least 70% of search text length ({searchText.Length})");
            
            string matchedText = content.Substring(result.Index, result.Length);
            
            // CRITICAL TEST: Ensure key words are covered
            string[] keyWords = { "quick", "brown", "fox", "over", "lazy", "dog" };
            int coveredWords = 0;
            foreach (string word in keyWords)
            {
                if (matchedText.Contains(word, StringComparison.OrdinalIgnoreCase))
                    coveredWords++;
            }
            
            Assert(coveredWords >= keyWords.Length * 0.8, 
                $"Fuzzy match should cover at least 80% of key words. Covered: {coveredWords}/{keyWords.Length}");
            
            Debug.WriteLine($"✓ Fuzzy match length coverage test passed - Covered {coveredWords}/{keyWords.Length} key words");
        }

        private static void TestApplyChangesWithLengthValidation()
        {
            Debug.WriteLine("Testing apply changes with enhanced length validation...");
            var service = new EnhancedTextSearchService();
            
            string content = "The quick brown fox jumps over the lazy dog. The fox was very clever.";
            string originalText = "quick brown fox jumps over the lazy dog";
            string changedText = "swift red fox leaps over the sleeping cat";
            
            string contentCopy = content;
            bool success = service.ApplyTextChanges(ref contentCopy, originalText, changedText, out string errorMessage);
            
            Assert(success, $"Expected success applying changes, got failure: {errorMessage}");
            Assert(contentCopy.Contains(changedText), "Expected content to contain the changed text");
            Assert(!contentCopy.Contains(originalText), "Expected content to not contain the original text after replacement");
            
            // CRITICAL TEST: Verify the replacement was complete and didn't leave partial text
            string[] originalWords = originalText.Split(' ');
            foreach (string word in originalWords)
            {
                // Should not contain sequences of original words after replacement
                if (word.Length > 3) // Skip very short words that might appear elsewhere
                {
                    int wordOccurrences = (contentCopy.Length - contentCopy.Replace(word, "").Length) / word.Length;
                    Assert(wordOccurrences <= 1, 
                        $"Word '{word}' appears too many times after replacement, suggesting incomplete replacement");
                }
            }
            
            Debug.WriteLine("✓ Apply changes with length validation test passed");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                Debug.WriteLine($"❌ ASSERTION FAILED: {message}");
                throw new Exception($"Test assertion failed: {message}");
            }
        }
    }
} 