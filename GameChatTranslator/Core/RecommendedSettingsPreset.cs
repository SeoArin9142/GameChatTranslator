using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTranslator
{
    /// <summary>
    /// 환경설정창에서 바로 적용할 수 있는 내장 추천 설정 프리셋입니다.
    /// 사용자 프리셋처럼 config.ini에 자동 저장하지 않고, UI 값만 채우는 용도로 사용합니다.
    /// </summary>
    public sealed class RecommendedSettingsPreset
    {
        private static readonly IReadOnlyList<RecommendedSettingsPreset> Presets = new[]
        {
            new RecommendedSettingsPreset(
                "low-spec",
                "저사양",
                "OCR 부하를 줄이고 자동 번역 주기를 길게 잡습니다.",
                scaleFactor: 1,
                threshold: 125,
                autoTranslateInterval: 8,
                resultDisplayMode: SettingsService.DefaultResultDisplayMode,
                resultHistoryLimit: SettingsValueNormalizer.DefaultResultHistoryLimit,
                saveDebugImages: false),
            new RecommendedSettingsPreset(
                "fast",
                "빠름",
                "실시간 자동 번역 반응 속도를 우선합니다.",
                scaleFactor: 2,
                threshold: SettingsValueNormalizer.DefaultThreshold,
                autoTranslateInterval: 2,
                resultDisplayMode: SettingsService.DefaultResultDisplayMode,
                resultHistoryLimit: SettingsValueNormalizer.DefaultResultHistoryLimit,
                saveDebugImages: false),
            new RecommendedSettingsPreset(
                "accurate",
                "정확도",
                "OCR 입력을 크게 만들어 인식률을 우선합니다.",
                scaleFactor: 4,
                threshold: 115,
                autoTranslateInterval: SettingsValueNormalizer.DefaultAutoTranslateInterval,
                resultDisplayMode: "History",
                resultHistoryLimit: SettingsValueNormalizer.MaxResultHistoryLimit,
                saveDebugImages: false)
        };

        public RecommendedSettingsPreset(
            string id,
            string displayName,
            string description,
            int scaleFactor,
            int threshold,
            int autoTranslateInterval,
            string resultDisplayMode,
            int resultHistoryLimit,
            bool saveDebugImages)
        {
            Id = id ?? "";
            DisplayName = displayName ?? "";
            Description = description ?? "";
            ScaleFactor = SettingsValueNormalizer.NormalizeScaleFactor(scaleFactor);
            Threshold = SettingsValueNormalizer.NormalizeThreshold(threshold);
            AutoTranslateInterval = SettingsValueNormalizer.NormalizeAutoTranslateInterval(autoTranslateInterval);
            ResultDisplayMode = string.IsNullOrWhiteSpace(resultDisplayMode) ? SettingsService.DefaultResultDisplayMode : resultDisplayMode.Trim();
            ResultHistoryLimit = SettingsValueNormalizer.NormalizeResultHistoryLimit(resultHistoryLimit);
            SaveDebugImages = saveDebugImages;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public int ScaleFactor { get; }
        public int Threshold { get; }
        public int AutoTranslateInterval { get; }
        public string ResultDisplayMode { get; }
        public int ResultHistoryLimit { get; }
        public bool SaveDebugImages { get; }

        /// <summary>
        /// UI에 노출할 내장 추천 프리셋 목록을 반환합니다.
        /// </summary>
        public static IReadOnlyList<RecommendedSettingsPreset> GetAll()
        {
            return Presets;
        }

        /// <summary>
        /// 지정한 ID와 일치하는 추천 프리셋을 찾습니다.
        /// <paramref name="id"/>는 XAML Button.Tag에 들어가는 stable ID입니다.
        /// </summary>
        public static RecommendedSettingsPreset FindById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            return Presets.FirstOrDefault(preset => preset.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
