using GameTranslator;
using System.Runtime.Versioning;
using Xunit;

namespace GameChatTranslator.Tests
{
    [SupportedOSPlatform("windows")]
    public class EasyOcrCliAdapterTests
    {
        private readonly EasyOcrCliAdapter _adapter = new EasyOcrCliAdapter();

        [Fact]
        public void BuildLanguageCodes_UsesGameLanguageFirstAndDeduplicatesDefaults()
        {
            string value = _adapter.BuildLanguageCodes(SettingsService.DefaultEasyOcrLanguageCodes, "ja");

            Assert.Equal("ja+en+ko+ch_sim", value);
        }

        [Fact]
        public void BuildLanguageCombinations_DefaultValue_ReturnsThreeComparisonGroups()
        {
            var values = _adapter.BuildLanguageCombinations(SettingsService.DefaultEasyOcrLanguageCodes, "ko");

            Assert.Equal(3, values.Count);
            Assert.Equal("ko+en", values[0]);
            Assert.Contains("ja+en", values);
            Assert.Contains("ch_sim+en", values);
        }

        [Fact]
        public void BuildArguments_IncludesRunnerImageAndGroups()
        {
            var values = _adapter.BuildArguments(
                "easyocr_runner.py",
                "input.png",
                new[] { "ko+en", "ja+en" });

            Assert.Equal(
                new[]
                {
                    "-X",
                    "utf8",
                    "easyocr_runner.py",
                    "--image",
                    "input.png",
                    "--groups",
                    "ko+en|ja+en",
                    "--gpu",
                    "false"
                },
                values);
        }

        [Fact]
        public void BuildPythonCandidates_IncludesPyLauncherFallback()
        {
            var values = _adapter.GetPythonCandidatesForTesting("");

            Assert.Contains("python", values);
            Assert.Contains("py", values);
            Assert.Contains("python3", values);
        }

        [Theory]
        [InlineData("ko", "ko")]
        [InlineData("en-US", "en")]
        [InlineData("zh-Hans-CN", "ch_sim")]
        [InlineData("ru", "ru")]
        [InlineData("ja", "ja")]
        public void MapAppLanguageTagToEasyOcr_MapsKnownTags(string input, string expected)
        {
            Assert.Equal(expected, _adapter.MapAppLanguageTagToEasyOcr(input));
        }

        [Fact]
        public void GetFailureMessageForExitCode_ModuleMissing_ReturnsInstallGuidance()
        {
            string message = _adapter.GetFailureMessageForExitCode(3, "");

            Assert.Contains("easyocr", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("torch", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("py -m pip", message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
