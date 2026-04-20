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
