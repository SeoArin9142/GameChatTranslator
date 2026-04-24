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
        public void BuildWorkerStartArguments_IncludesWorkerFlag()
        {
            var values = _adapter.BuildWorkerStartArguments("paddleocr_runner.py");

            Assert.Equal(
                new[]
                {
                    "-X",
                    "utf8",
                    "-u",
                    "paddleocr_runner.py",
                    "--worker"
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

        [Fact]
        public void ParseWorkerResponse_ReadsTopLevelFieldsAndImages()
        {
            PaddleOcrCliAdapter.PaddleOcrWorkerResponse response = PaddleOcrCliAdapter.ParseWorkerResponse(
                """
                {
                  "requestId": "req-1",
                  "ok": true,
                  "error": "",
                  "errorCode": "",
                  "images": [
                    {
                      "index": 0,
                      "groups": [
                        {
                          "languageCodes": "korean",
                          "success": true,
                          "error": "",
                          "lines": [
                            { "top": 2, "bottom": 8, "text": "테스트" }
                          ]
                        }
                      ]
                    }
                  ]
                }
                """);

            Assert.NotNull(response);
            Assert.Equal("req-1", response.RequestId);
            Assert.True(response.Ok);
            PaddleOcrCliAdapter.PaddleOcrWorkerImage image = Assert.Single(response.Images);
            Assert.Equal(0, image.Index);
            PaddleOcrCliAdapter.PaddleOcrWorkerGroup group = Assert.Single(image.Groups);
            Assert.Equal("korean", group.LanguageCodes);
            Assert.Equal("테스트", Assert.Single(group.Lines).Text);
        }

        [Fact]
        public void CreateSuccess_PreservesResidentWorkerMetadata()
        {
            PaddleOcrCliBatchResult result = PaddleOcrCliBatchResult.CreateSuccess(
                "python",
                new[] { "korean" },
                new[]
                {
                    new PaddleOcrCliImageResult(0, new[]
                    {
                        new PaddleOcrCliGroupResult("korean", true, new[] { new OcrLine { Text = "테스트" } }, "")
                    })
                },
                standardError: "stderr",
                usedResidentWorker: true,
                startedWorker: true,
                restartedWorker: false,
                usedInitializationTimeout: true);

            Assert.True(result.UsedResidentWorker);
            Assert.True(result.StartedWorker);
            Assert.False(result.RestartedWorker);
            Assert.True(result.UsedInitializationTimeout);
            Assert.False(result.TimedOut);
            Assert.Equal("stderr", result.StandardError);
        }
    }
}
