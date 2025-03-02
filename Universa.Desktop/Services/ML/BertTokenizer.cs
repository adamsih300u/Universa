using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Universa.Desktop.Services.ML
{
    public class BertTokenizer
    {
        private const string Cls = "[CLS]";
        private const string Sep = "[SEP]";
        private const string Pad = "[PAD]";
        private const string Unk = "[UNK]";

        public (long[] InputIds, long[] AttentionMask) Tokenize(string text, int maxLength)
        {
            // Initialize arrays
            var inputIds = new long[maxLength];
            var attentionMask = new long[maxLength];

            // Add [CLS] token at the start
            inputIds[0] = 101; // [CLS] token ID
            attentionMask[0] = 1;

            // Basic tokenization (split into words and punctuation)
            var tokens = BasicTokenize(text);

            // Convert tokens to IDs (simplified - using basic ASCII values)
            var position = 1;
            foreach (var token in tokens)
            {
                if (position >= maxLength - 1) // Leave room for [SEP]
                    break;

                // For simplicity, we'll just use the ASCII values of the first character
                // In a real implementation, you would use a proper vocabulary lookup
                inputIds[position] = token.Length > 0 ? (long)token[0] : 100;
                attentionMask[position] = 1;
                position++;
            }

            // Add [SEP] token at the end
            if (position < maxLength)
            {
                inputIds[position] = 102; // [SEP] token ID
                attentionMask[position] = 1;
                position++;
            }

            // Pad the rest with zeros
            for (int i = position; i < maxLength; i++)
            {
                inputIds[i] = 0; // [PAD] token ID
                attentionMask[i] = 0;
            }

            return (inputIds, attentionMask);
        }

        private List<string> BasicTokenize(string text)
        {
            // Convert to lowercase
            text = text.ToLowerInvariant();

            // Split on whitespace and punctuation
            var tokens = Regex.Split(text, @"(\s+|[.,!?;])")
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            return tokens;
        }
    }
} 