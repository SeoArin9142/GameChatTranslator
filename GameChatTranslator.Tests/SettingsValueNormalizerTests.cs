using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class SettingsValueNormalizerTests
    {
        [Theory]
        [InlineData(null, 5)]
        [InlineData("", 5)]
        [InlineData("not-number", 5)]
        [InlineData("0", 1)]
        [InlineData("-10", 1)]
        [InlineData("5", 5)]
        [InlineData("10", 10)]
        [InlineData("11", 10)]
        [InlineData("100", 10)]
        public void NormalizeResultHistoryLimit_ClampsStringValues(string rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeResultHistoryLimit(rawValue));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(7, 7)]
        [InlineData(10, 10)]
        [InlineData(15, 10)]
        public void NormalizeResultHistoryLimit_ClampsIntegerValues(int rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeResultHistoryLimit(rawValue));
        }
    }
}
