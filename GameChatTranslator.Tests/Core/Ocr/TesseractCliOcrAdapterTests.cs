using GameTranslator;
using System.Runtime.Versioning;
using Xunit;

namespace GameChatTranslator.Tests
{
    [SupportedOSPlatform("windows")]
    public class TesseractCliOcrAdapterTests
    {
        private readonly TesseractCliOcrAdapter _adapter = new TesseractCliOcrAdapter();

        [Fact]
        public void BuildLanguageCodes_UsesGameLanguageFirstAndDeduplicatesDefaults()
        {
            string value = _adapter.BuildLanguageCodes(SettingsService.DefaultTesseractLanguageCodes, "ja");

            Assert.Equal("jpn+eng+kor+chi_sim", value);
        }

        [Fact]
        public void BuildLanguageCodes_UsesConfiguredCodesWhenProvided()
        {
            string value = _adapter.BuildLanguageCodes("ja, en-US, chi_sim", "ko");

            Assert.Equal("jpn+eng+chi_sim", value);
        }

        [Fact]
        public void BuildLanguageCombinations_DefaultValue_ReturnsThreeComparisonGroups()
        {
            var values = _adapter.BuildLanguageCombinations(SettingsService.DefaultTesseractLanguageCodes, "ko");

            Assert.Equal(3, values.Count);
            Assert.Equal("kor+eng", values[0]);
            Assert.Contains("jpn+eng", values);
            Assert.Contains("chi_sim+eng", values);
        }

        [Fact]
        public void BuildLanguageCombinations_CustomGroups_PreservesExplicitMatrix()
        {
            var values = _adapter.BuildLanguageCombinations("kor+eng|jpn+eng", "ko");

            Assert.Equal(2, values.Count);
            Assert.Equal("kor+eng", values[0]);
            Assert.Equal("jpn+eng", values[1]);
        }

        [Theory]
        [InlineData("ko", "kor")]
        [InlineData("en-US", "eng")]
        [InlineData("zh-Hans-CN", "chi_sim")]
        [InlineData("ru", "rus")]
        [InlineData("jpn", "jpn")]
        public void MapAppLanguageTagToTesseract_MapsKnownTags(string input, string expected)
        {
            Assert.Equal(expected, _adapter.MapAppLanguageTagToTesseract(input));
        }

        [Fact]
        public void ParseOutputLines_RemovesBlankLinesAndTrims()
        {
            var lines = _adapter.ParseOutputLines("  猫 は 可 愛 い  \r\n\r\n [치요]: 안녕 \n");

            Assert.Equal(2, lines.Count);
            Assert.Equal("猫 は 可 愛 い", lines[0]);
            Assert.Equal("[치요]: 안녕", lines[1]);
        }
    }
}
