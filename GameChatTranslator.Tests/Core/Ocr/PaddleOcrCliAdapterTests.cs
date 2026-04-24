using GameTranslator;
using System.Runtime.Versioning;
using Xunit;

namespace GameChatTranslator.Tests
{
    [SupportedOSPlatform("windows")]
    public class PaddleOcrCliAdapterTests
    {
        private readonly PaddleOcrCliAdapter _adapter = new PaddleOcrCliAdapter();

        [Fact]
        public void BuildLanguageCodes_UsesGameLanguageFirstAndDeduplicatesDefaults()
        {
            string value = _adapter.BuildLanguageCodes(SettingsService.DefaultPaddleOcrLanguageCodes, "ja");

            Assert.Equal("japan+en+korean+ch", value);
        }

        [Fact]
        public void BuildLanguageCandidates_DefaultValue_ReturnsFourComparisonGroups()
        {
            var values = _adapter.BuildLanguageCandidates(SettingsService.DefaultPaddleOcrLanguageCodes, "ko");

            Assert.Equal(4, values.Count);
            Assert.Equal("korean", values[0]);
            Assert.Contains("en", values);
            Assert.Contains("japan", values);
            Assert.Contains("ch", values);
        }

        [Fact]
        public void BuildArguments_IncludesRunnerImageAndGroups()
        {
            var values = _adapter.BuildArguments(
                "paddleocr_runner.py",
                "input.png",
                new[] { "korean", "japan", "ch" });

            Assert.Equal(
                new[]
                {
                    "-X",
                    "utf8",
                    "paddleocr_runner.py",
                    "--images",
                    "input.png",
                    "--groups",
                    "korean|japan|ch",
                    "--gpu",
                    "false"
                },
                values);
        }

        [Fact]
        public void BuildBatchArguments_UsesImagesFlagAndPreservesOrder()
        {
            var values = _adapter.BuildBatchArguments(
                "paddleocr_runner.py",
                new[] { "input-1.png", "input-2.png", "input-3.png" },
                new[] { "korean", "japan", "ch" });

            Assert.Equal(
                new[]
                {
                    "-X",
                    "utf8",
                    "paddleocr_runner.py",
                    "--images",
                    "input-1.png|input-2.png|input-3.png",
                    "--groups",
                    "korean|japan|ch",
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
        [InlineData("ko", "korean")]
        [InlineData("en-US", "en")]
        [InlineData("zh-Hans-CN", "ch")]
        [InlineData("ru", "ru")]
        [InlineData("ja", "japan")]
        public void MapAppLanguageTagToPaddleOcr_MapsKnownTags(string input, string expected)
        {
            Assert.Equal(expected, _adapter.MapAppLanguageTagToPaddleOcr(input));
        }

        [Fact]
        public void GetFailureMessageForExitCode_ModuleMissing_ReturnsInstallGuidance()
        {
            string message = _adapter.GetFailureMessageForExitCode(3, "");

            Assert.Contains("paddleocr", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("paddlepaddle", message, System.StringComparison.OrdinalIgnoreCase);
            Assert.Contains("py -m pip", message, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
