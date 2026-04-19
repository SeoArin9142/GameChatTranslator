using System;

namespace GameTranslator
{
    /// <summary>
    /// config.ini에서 읽은 문자열 설정값을 런타임 기본값과 UI 기본값으로 해석하는 순수 서비스입니다.
    /// 실제 파일 읽기/쓰기는 호출자가 담당하고, 이 클래스는 값 선택/보정/마이그레이션 판단만 수행합니다.
    /// </summary>
    public sealed class SettingsService
    {
        public const string DefaultGeminiModel = "gemini-2.5-flash";
        public const string DefaultResultDisplayMode = "Latest";

        public const string DefaultKeyMoveLock = "Ctrl+7";
        public const string DefaultKeyAreaSelect = "Ctrl+8";
        public const string DefaultKeyTranslate = "Ctrl+9";
        public const string DefaultKeyAutoTranslate = "Ctrl+0";
        public const string DefaultKeyToggleEngine = "Ctrl+-";
        public const string DefaultKeyCopyResult = "Ctrl+6";
        public const string DefaultKeyLogViewer = "Ctrl+=";

        /// <summary>
        /// Gemini API 키를 선택합니다.
        /// currentSectionKey가 있으면 우선 사용하고, 없으면 legacySectionKey를 사용합니다.
        /// </summary>
        public GeminiKeySelection SelectGeminiKey(string currentSectionKey, string legacySectionKey)
        {
            if (!string.IsNullOrWhiteSpace(currentSectionKey))
            {
                return new GeminiKeySelection(currentSectionKey.Trim(), false);
            }

            if (!string.IsNullOrWhiteSpace(legacySectionKey))
            {
                return new GeminiKeySelection(legacySectionKey.Trim(), true);
            }

            return new GeminiKeySelection("", false);
        }

        /// <summary>
        /// Gemini 모델명이 비어 있으면 기본 모델명을 반환합니다.
        /// </summary>
        public string NormalizeGeminiModel(string modelName)
        {
            return string.IsNullOrWhiteSpace(modelName) ? DefaultGeminiModel : modelName.Trim();
        }

        /// <summary>
        /// true/1/yes/y/on 값을 true로 해석합니다.
        /// </summary>
        public bool IsEnabled(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string normalized = value.Trim();
            return normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// false/0/no/n/off 값을 false로 해석합니다.
        /// 그 외 값은 defaultValue로 처리합니다.
        /// </summary>
        public bool IsEnabledOrDefault(string value, bool defaultValue)
        {
            if (IsEnabled(value)) return true;
            if (IsDisabled(value)) return false;
            return defaultValue;
        }

        /// <summary>
        /// false/0/no/n/off 값을 명시적 꺼짐으로 해석합니다.
        /// </summary>
        public bool IsDisabled(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            string normalized = value.Trim();
            return normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("0", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("n", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 저장된 단축키 문자열이 비어 있으면 기본 단축키를 반환합니다.
        /// </summary>
        public string NormalizeHotkey(string configuredHotkey, string defaultHotkey)
        {
            return string.IsNullOrWhiteSpace(configuredHotkey) ? defaultHotkey : configuredHotkey.Trim();
        }

        /// <summary>
        /// 기본 단축키 묶음을 반환합니다.
        /// </summary>
        public DefaultHotkeys GetDefaultHotkeys()
        {
            return new DefaultHotkeys(
                DefaultKeyMoveLock,
                DefaultKeyAreaSelect,
                DefaultKeyTranslate,
                DefaultKeyAutoTranslate,
                DefaultKeyToggleEngine,
                DefaultKeyCopyResult,
                DefaultKeyLogViewer);
        }
    }

    /// <summary>
    /// Gemini API 키 선택 결과입니다.
    /// ShouldMigrateLegacyKey가 true이면 구버전 [GeminiKey] 섹션 값을 현재 [Settings] 섹션에 저장해야 합니다.
    /// </summary>
    public sealed class GeminiKeySelection
    {
        public GeminiKeySelection(string key, bool shouldMigrateLegacyKey)
        {
            Key = key ?? "";
            ShouldMigrateLegacyKey = shouldMigrateLegacyKey;
        }

        public string Key { get; }
        public bool ShouldMigrateLegacyKey { get; }
    }

    /// <summary>
    /// 환경설정창과 전역 단축키 등록에서 공유하는 기본 단축키 묶음입니다.
    /// </summary>
    public sealed class DefaultHotkeys
    {
        public DefaultHotkeys(
            string moveLock,
            string areaSelect,
            string translate,
            string autoTranslate,
            string toggleEngine,
            string copyResult,
            string logViewer)
        {
            MoveLock = moveLock;
            AreaSelect = areaSelect;
            Translate = translate;
            AutoTranslate = autoTranslate;
            ToggleEngine = toggleEngine;
            CopyResult = copyResult;
            LogViewer = logViewer;
        }

        public string MoveLock { get; }
        public string AreaSelect { get; }
        public string Translate { get; }
        public string AutoTranslate { get; }
        public string ToggleEngine { get; }
        public string CopyResult { get; }
        public string LogViewer { get; }
    }
}
