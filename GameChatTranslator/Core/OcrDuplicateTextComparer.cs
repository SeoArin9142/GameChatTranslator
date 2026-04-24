using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GameTranslator
{
    public sealed class OcrDuplicateTextComparisonResult
    {
        public bool IsDuplicate { get; set; }
        public bool UsedFuzzyComparison { get; set; }
        public string PreviousNormalizedText { get; set; } = "";
        public string CurrentNormalizedText { get; set; } = "";
        public int EditDistance { get; set; } = int.MaxValue;
        public double Similarity { get; set; }
    }

    public static class OcrDuplicateTextComparer
    {
        public const int MinimumFuzzyLength = 10;
        public const int MaximumEditDistance = 2;
        public const double MinimumSimilarity = 0.90d;

        private static readonly HashSet<char> IgnoredPunctuationCharacters = new HashSet<char>
        {
            '.', ',', '!', '?', ';', ':',
            '…', '·', '•', '`', '\'', '"',
            '‘', '’', '“', '”',
            '。', '，', '！', '？', '：', '；'
        };

        public static OcrDuplicateTextComparisonResult Compare(string previousText, string currentText)
        {
            var result = new OcrDuplicateTextComparisonResult
            {
                PreviousNormalizedText = NormalizeForComparison(previousText),
                CurrentNormalizedText = NormalizeForComparison(currentText)
            };

            if (string.IsNullOrWhiteSpace(previousText) || string.IsNullOrWhiteSpace(currentText))
            {
                return result;
            }

            if (string.Equals(previousText, currentText, StringComparison.Ordinal))
            {
                result.IsDuplicate = true;
                result.EditDistance = 0;
                result.Similarity = 1d;
                return result;
            }

            int comparisonLength = Math.Max(result.PreviousNormalizedText.Length, result.CurrentNormalizedText.Length);
            if (comparisonLength < MinimumFuzzyLength)
            {
                return result;
            }

            result.UsedFuzzyComparison = true;

            if (string.Equals(result.PreviousNormalizedText, result.CurrentNormalizedText, StringComparison.Ordinal))
            {
                result.IsDuplicate = true;
                result.EditDistance = 0;
                result.Similarity = 1d;
                return result;
            }

            result.EditDistance = ComputeLevenshteinDistance(result.PreviousNormalizedText, result.CurrentNormalizedText);
            result.Similarity = ComputeSimilarity(result.PreviousNormalizedText, result.CurrentNormalizedText, result.EditDistance);
            result.IsDuplicate = result.EditDistance <= MaximumEditDistance || result.Similarity >= MinimumSimilarity;
            return result;
        }

        public static string NormalizeForComparison(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }

            string collapsedWhitespace = Regex.Replace(value.Trim(), @"\s+", " ");
            var builder = new StringBuilder(collapsedWhitespace.Length);

            foreach (char character in collapsedWhitespace)
            {
                if (IgnoredPunctuationCharacters.Contains(character))
                {
                    continue;
                }

                builder.Append(char.IsLetter(character) ? char.ToUpperInvariant(character) : character);
            }

            return Regex.Replace(builder.ToString().Trim(), @"\s+", " ");
        }

        private static double ComputeSimilarity(string previousText, string currentText, int distance)
        {
            int maxLength = Math.Max(previousText?.Length ?? 0, currentText?.Length ?? 0);
            if (maxLength <= 0)
            {
                return 1d;
            }

            return 1d - ((double)distance / maxLength);
        }

        private static int ComputeLevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return target?.Length ?? 0;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var distances = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
            {
                distances[i, 0] = i;
            }

            for (int j = 0; j <= target.Length; j++)
            {
                distances[0, j] = j;
            }

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(
                            distances[i - 1, j] + 1,
                            distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + cost);
                }
            }

            return distances[source.Length, target.Length];
        }
    }
}
