using System;
using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class RecommendedSettingsPresetTests
    {
        [Fact]
        public void GetAll_ReturnsThreeStablePresets()
        {
            var presets = RecommendedSettingsPreset.GetAll();

            Assert.Equal(new[] { "low-spec", "fast", "accurate" }, presets.Select(preset => preset.Id).ToArray());
            Assert.Equal(3, presets.Select(preset => preset.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Theory]
        [InlineData("low-spec", 1, 125, 8, "Latest", 5)]
        [InlineData("fast", 2, 120, 2, "Latest", 5)]
        [InlineData("accurate", 4, 115, 5, "History", 10)]
        public void FindById_ReturnsExpectedPresetValues(
            string id,
            int expectedScale,
            int expectedThreshold,
            int expectedInterval,
            string expectedDisplayMode,
            int expectedHistoryLimit)
        {
            RecommendedSettingsPreset preset = RecommendedSettingsPreset.FindById(id);

            Assert.NotNull(preset);
            Assert.Equal(expectedScale, preset.ScaleFactor);
            Assert.Equal(expectedThreshold, preset.Threshold);
            Assert.Equal(expectedInterval, preset.AutoTranslateInterval);
            Assert.Equal(expectedDisplayMode, preset.ResultDisplayMode);
            Assert.Equal(expectedHistoryLimit, preset.ResultHistoryLimit);
            Assert.False(preset.SaveDebugImages);
        }

        [Fact]
        public void Constructor_NormalizesOutOfRangeValues()
        {
            var preset = new RecommendedSettingsPreset(
                "custom",
                "Custom",
                "Description",
                scaleFactor: 99,
                threshold: 999,
                autoTranslateInterval: 999,
                resultDisplayMode: "",
                resultHistoryLimit: 999,
                saveDebugImages: true);

            Assert.Equal(SettingsValueNormalizer.MaxScaleFactor, preset.ScaleFactor);
            Assert.Equal(SettingsValueNormalizer.MaxThreshold, preset.Threshold);
            Assert.Equal(SettingsValueNormalizer.MaxAutoTranslateInterval, preset.AutoTranslateInterval);
            Assert.Equal(SettingsService.DefaultResultDisplayMode, preset.ResultDisplayMode);
            Assert.Equal(SettingsValueNormalizer.MaxResultHistoryLimit, preset.ResultHistoryLimit);
            Assert.True(preset.SaveDebugImages);
        }

        [Fact]
        public void BuildDifferenceSummary_ListsOnlyChangedRecommendedValues()
        {
            RecommendedSettingsPreset preset = RecommendedSettingsPreset.FindById("fast");

            string summary = preset.BuildDifferenceSummary(
                currentScaleFactor: 3,
                currentThreshold: 120,
                currentAutoTranslateInterval: 5,
                currentResultDisplayMode: "History",
                currentResultHistoryLimit: 10,
                currentSaveDebugImages: true);

            Assert.Contains("OCR 배율: 3 → 2", summary);
            Assert.Contains("자동 번역 주기: 5초 → 2초", summary);
            Assert.Contains("결과 표시 방식: History → Latest", summary);
            Assert.Contains("누적 표시 줄 수: 10줄 → 5줄", summary);
            Assert.Contains("디버그 이미지 저장: ON → OFF", summary);
            Assert.DoesNotContain("이진화 기준", summary);
        }

        [Fact]
        public void BuildDifferenceSummary_ReturnsNoChangeMessageWhenValuesMatch()
        {
            RecommendedSettingsPreset preset = RecommendedSettingsPreset.FindById("accurate");

            string summary = preset.BuildDifferenceSummary(
                currentScaleFactor: 4,
                currentThreshold: 115,
                currentAutoTranslateInterval: 5,
                currentResultDisplayMode: "History",
                currentResultHistoryLimit: 10,
                currentSaveDebugImages: false);

            Assert.Equal("현재 고급 설정과 동일합니다.", summary);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("missing")]
        public void FindById_ReturnsNullForMissingId(string id)
        {
            Assert.Null(RecommendedSettingsPreset.FindById(id));
        }
    }
}
