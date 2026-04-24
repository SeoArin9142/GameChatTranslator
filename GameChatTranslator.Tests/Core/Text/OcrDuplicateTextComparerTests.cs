using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrDuplicateTextComparerTests
    {
        [Fact]
        public void NormalizeForComparison_CollapsesWhitespaceAndRemovesSimplePunctuation()
        {
            string normalized = OcrDuplicateTextComparer.NormalizeForComparison("  Hello...   world?!  ");

            Assert.Equal("HELLO WORLD", normalized);
        }

        [Fact]
        public void Compare_ShortText_UsesExactMatchOnly()
        {
            OcrDuplicateTextComparisonResult result = OcrDuplicateTextComparer.Compare("가자!", "가자?");

            Assert.False(result.IsDuplicate);
            Assert.False(result.UsedFuzzyComparison);
        }

        [Fact]
        public void Compare_LongTextWithWhitespaceAndPunctuationNoise_ReturnsDuplicate()
        {
            OcrDuplicateTextComparisonResult result = OcrDuplicateTextComparer.Compare(
                "[미셸]: hello...   world!",
                "[미셸]: hello.. world");

            Assert.True(result.IsDuplicate);
            Assert.True(result.UsedFuzzyComparison);
            Assert.Equal(0, result.EditDistance);
        }

        [Fact]
        public void Compare_LongTextWithSmallOcrNoise_ReturnsDuplicate()
        {
            OcrDuplicateTextComparisonResult result = OcrDuplicateTextComparer.Compare(
                "[미셸]: this is a long sample message",
                "[미셸]: this is a long samplf message");

            Assert.True(result.IsDuplicate);
            Assert.True(result.UsedFuzzyComparison);
            Assert.True(result.EditDistance <= OcrDuplicateTextComparer.MaximumEditDistance);
        }

        [Fact]
        public void Compare_ClearlyDifferentLongText_ReturnsNotDuplicate()
        {
            OcrDuplicateTextComparisonResult result = OcrDuplicateTextComparer.Compare(
                "[미셸]: this is a long sample message",
                "[미셸]: completely different translated input");

            Assert.False(result.IsDuplicate);
            Assert.True(result.UsedFuzzyComparison);
        }
    }
}
