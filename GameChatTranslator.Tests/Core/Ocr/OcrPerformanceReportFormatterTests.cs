using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrPerformanceReportFormatterTests
    {
        [Fact]
        public void BuildLogLine_IncludesFastPathAndFallbackDetails()
        {
            var report = new OcrPerformanceReport
            {
                ModeLabel = "자동",
                CaptureMs = 1,
                ResizeMs = 2,
                PreprocessMs = 3,
                CropMs = 4,
                OcrMs = 5,
                ScoringMs = 6,
                TranslateMs = 7,
                TotalMs = 28,
                PreprocessCandidateCount = 3,
                OcrLanguageCallCount = 6,
                MergedLineCount = 2,
                TranslatedLineCount = 1,
                SkippedLineCount = 1,
                SelectedPreprocessName = "Adaptive",
                SelectedOcrLanguages = "ko+en-US",
                SelectedScore = 12000,
                Outcome = "Translated",
                FastPathAttempted = true,
                FastPathSucceeded = false,
                FallbackAttempted = true,
                FallbackReason = "FastPathFailed"
            };

            string line = OcrPerformanceReportFormatter.BuildLogLine(report);

            Assert.Contains("[OCR PERF]", line);
            Assert.Contains("Mode=자동", line);
            Assert.Contains("Selected=Adaptive/ko+en-US", line);
            Assert.Contains("FastPath=Failed", line);
            Assert.Contains("Fallback=Yes(FastPathFailed)", line);
            Assert.Contains("Outcome=Translated", line);
        }

        [Fact]
        public void BuildLogLine_FormatsUnusedFastPathAndNoFallback()
        {
            var report = new OcrPerformanceReport
            {
                ModeLabel = "정확",
                SelectedPreprocessName = "Color",
                SelectedOcrLanguages = "ko",
                Outcome = "NoOcrCandidate"
            };

            string line = OcrPerformanceReportFormatter.BuildLogLine(report);

            Assert.Contains("FastPath=NotUsed", line);
            Assert.Contains("Fallback=No", line);
        }

        [Fact]
        public void BuildAveragePart_IncludesFastPathAndFallbackCounts()
        {
            var average = new OcrPerformanceAverageReport
            {
                ModeLabel = "자동",
                Count = 4,
                TotalElapsedMs = 400,
                TotalOcrMs = 120,
                TotalTranslateMs = 80,
                TotalOcrCalls = 12,
                FastPathAttemptCount = 4,
                FastPathSuccessCount = 3,
                FallbackCount = 1
            };

            string part = OcrPerformanceReportFormatter.BuildAveragePart(average);

            Assert.Contains("자동 n=4", part);
            Assert.Contains("Total 100ms", part);
            Assert.Contains("OCR 30ms", part);
            Assert.Contains("Translate 20ms", part);
            Assert.Contains("Calls 3", part);
            Assert.Contains("FastPath 3/4", part);
            Assert.Contains("Fallback 1/4", part);
        }

        [Fact]
        public void BuildAveragePart_HandlesModeWithoutFastPath()
        {
            var average = new OcrPerformanceAverageReport
            {
                ModeLabel = "정확",
                Count = 2,
                TotalElapsedMs = 100,
                TotalOcrMs = 60,
                TotalTranslateMs = 20,
                TotalOcrCalls = 10
            };

            string part = OcrPerformanceReportFormatter.BuildAveragePart(average);

            Assert.Contains("FastPath -", part);
            Assert.Contains("Fallback 0/2", part);
        }
    }
}
