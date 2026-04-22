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

        [Fact]
        public void BuildDiagnosticProfiles_ReturnsThreeProfilesInExpectedOrder()
        {
            var profiles = _adapter.BuildDiagnosticProfiles();

            Assert.Equal(3, profiles.Count);

            Assert.Equal("Baseline", profiles[0].CandidateSuffix);
            Assert.Null(profiles[0].PageSegmentationMode);
            Assert.Null(profiles[0].OcrEngineMode);

            Assert.Equal("PSM6-LSTM", profiles[1].CandidateSuffix);
            Assert.Equal(6, profiles[1].PageSegmentationMode);
            Assert.Equal(1, profiles[1].OcrEngineMode);

            Assert.Equal("PSM11-LSTM", profiles[2].CandidateSuffix);
            Assert.Equal(11, profiles[2].PageSegmentationMode);
            Assert.Equal(1, profiles[2].OcrEngineMode);
        }

        [Fact]
        public void BuildArguments_BaselineProfile_DoesNotAppendPsmOrOemFlags()
        {
            var profile = _adapter.BuildDiagnosticProfiles()[0];

            var values = _adapter.BuildArguments("input.png", "jpn+eng", profile);

            Assert.Equal(new[] { "input.png", "stdout", "-l", "jpn+eng" }, values);
        }

        [Fact]
        public void BuildArguments_Psm11LstmProfile_AppendsExpectedFlags()
        {
            var profile = _adapter.BuildDiagnosticProfiles()[2];

            var values = _adapter.BuildArguments("input.png", "chi_sim+eng", profile);

            Assert.Equal(new[] { "input.png", "stdout", "-l", "chi_sim+eng", "--psm", "11", "--oem", "1" }, values);
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
