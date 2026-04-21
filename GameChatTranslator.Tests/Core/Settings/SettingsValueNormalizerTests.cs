using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class SettingsValueNormalizerTests
    {
        [Theory]
        [InlineData(null, 120)]
        [InlineData("", 120)]
        [InlineData("not-number", 120)]
        [InlineData("0", 1)]
        [InlineData("-10", 1)]
        [InlineData("1", 1)]
        [InlineData("120", 120)]
        [InlineData("255", 255)]
        [InlineData("256", 255)]
        [InlineData("999", 255)]
        public void NormalizeThreshold_ClampsStringValues(string rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeThreshold(rawValue));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(120, 120)]
        [InlineData(255, 255)]
        [InlineData(300, 255)]
        public void NormalizeThreshold_ClampsIntegerValues(int rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeThreshold(rawValue));
        }

        [Theory]
        [InlineData(null, 5)]
        [InlineData("", 5)]
        [InlineData("not-number", 5)]
        [InlineData("0", 1)]
        [InlineData("-10", 1)]
        [InlineData("1", 1)]
        [InlineData("5", 5)]
        [InlineData("60", 60)]
        [InlineData("61", 60)]
        [InlineData("999", 60)]
        public void NormalizeAutoTranslateInterval_ClampsStringValues(string rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeAutoTranslateInterval(rawValue));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(5, 5)]
        [InlineData(60, 60)]
        [InlineData(90, 60)]
        public void NormalizeAutoTranslateInterval_ClampsIntegerValues(int rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeAutoTranslateInterval(rawValue));
        }

        [Theory]
        [InlineData(null, 3)]
        [InlineData("", 3)]
        [InlineData("not-number", 3)]
        [InlineData("0", 1)]
        [InlineData("-10", 1)]
        [InlineData("1", 1)]
        [InlineData("3", 3)]
        [InlineData("4", 4)]
        [InlineData("5", 4)]
        [InlineData("999", 4)]
        public void NormalizeScaleFactor_ClampsStringValues(string rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeScaleFactor(rawValue));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(3, 3)]
        [InlineData(4, 4)]
        [InlineData(10, 4)]
        public void NormalizeScaleFactor_ClampsIntegerValues(int rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeScaleFactor(rawValue));
        }

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

        [Theory]
        [InlineData(null, 0)]
        [InlineData("", 0)]
        [InlineData("not-number", 0)]
        [InlineData("-10", 0)]
        [InlineData("0", 0)]
        [InlineData("5", 5)]
        [InlineData("60", 60)]
        [InlineData("61", 60)]
        [InlineData("999", 60)]
        public void NormalizeTranslationResultAutoClearSeconds_ClampsStringValues(string rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeTranslationResultAutoClearSeconds(rawValue));
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(7, 7)]
        [InlineData(60, 60)]
        [InlineData(100, 60)]
        public void NormalizeTranslationResultAutoClearSeconds_ClampsIntegerValues(int rawValue, int expected)
        {
            Assert.Equal(expected, SettingsValueNormalizer.NormalizeTranslationResultAutoClearSeconds(rawValue));
        }
    }
}
