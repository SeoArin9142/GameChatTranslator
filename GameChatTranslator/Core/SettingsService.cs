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
        public const string DefaultLocalLlmEndpoint = "http://localhost:1234/v1/chat/completions";
        public const string DefaultLocalLlmModel = "qwen/qwen3.5-9b";
        public const int DefaultLocalLlmTimeoutSeconds = 10;
        public const int MinLocalLlmTimeoutSeconds = 1;
        public const int MaxLocalLlmTimeoutSeconds = 60;
        public const int DefaultLocalLlmMaxTokens = 160;
        public const int MinLocalLlmMaxTokens = 40;
        public const int MaxLocalLlmMaxTokens = 512;
        public const string DefaultTranslationEngine = "Google";
        public const string DefaultResultDisplayMode = "Latest";

        public const string DefaultKeyMoveLock = "Ctrl+7";
        public const string DefaultKeyAreaSelect = "Ctrl+8";
        public const string DefaultKeyTranslate = "Ctrl+9";
        public const string DefaultKeyAutoTranslate = "Ctrl+0";
        public const string DefaultKeyToggleEngine = "Ctrl+-";
        public const string DefaultKeyCopyResult = "Ctrl+6";
        public const string DefaultKeyLogViewer = "Ctrl+=";
        public const string DefaultKeyOcrDiagnostic = "Ctrl+5";
        public const string DefaultKeyHotkeyGuideToggle = "Ctrl+F10";

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
        /// Local LLM 엔드포인트가 비어 있으면 LM Studio 기본 chat completions 주소를 반환합니다.
        /// </summary>
        public string NormalizeLocalLlmEndpoint(string endpoint)
        {
            return string.IsNullOrWhiteSpace(endpoint) ? DefaultLocalLlmEndpoint : endpoint.Trim();
        }

        /// <summary>
        /// Local LLM 모델명이 비어 있으면 현재 검증한 기본 LM Studio 모델명을 반환합니다.
        /// </summary>
        public string NormalizeLocalLlmModel(string modelName)
        {
            return string.IsNullOrWhiteSpace(modelName) ? DefaultLocalLlmModel : modelName.Trim();
        }

        /// <summary>
        /// Local LLM 요청 타임아웃을 1~60초 범위로 보정합니다.
        /// </summary>
        public int NormalizeLocalLlmTimeoutSeconds(string value)
        {
            return NormalizeInt(value, DefaultLocalLlmTimeoutSeconds, MinLocalLlmTimeoutSeconds, MaxLocalLlmTimeoutSeconds);
        }

        /// <summary>
        /// Local LLM 응답 최대 토큰 수를 40~512 범위로 보정합니다.
        /// </summary>
        public int NormalizeLocalLlmMaxTokens(string value)
        {
            return NormalizeInt(value, DefaultLocalLlmMaxTokens, MinLocalLlmMaxTokens, MaxLocalLlmMaxTokens);
        }

        /// <summary>
        /// 저장된 번역 엔진 문자열을 앱 내부 enum으로 변환합니다.
        /// 알 수 없는 값은 Google로 되돌려 잘못된 설정 때문에 실행이 막히지 않게 합니다.
        /// </summary>
        public TranslationEngineMode NormalizeTranslationEngineMode(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.Equals("Gemini", StringComparison.OrdinalIgnoreCase))
            {
                return TranslationEngineMode.Gemini;
            }

            if (normalized.Equals("LocalLlm", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("LocalLLM", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Local LLM", StringComparison.OrdinalIgnoreCase))
            {
                return TranslationEngineMode.LocalLlm;
            }

            return TranslationEngineMode.Google;
        }

        /// <summary>
        /// 번역 엔진 enum을 config.ini와 ComboBox.Tag에 저장할 문자열로 변환합니다.
        /// </summary>
        public string GetTranslationEngineTag(TranslationEngineMode mode)
        {
            return mode switch
            {
                TranslationEngineMode.Gemini => "Gemini",
                TranslationEngineMode.LocalLlm => "LocalLlm",
                _ => DefaultTranslationEngine
            };
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

        private static int NormalizeInt(string value, int defaultValue, int minValue, int maxValue)
        {
            if (!int.TryParse(value, out int parsedValue))
            {
                return defaultValue;
            }

            return Math.Max(minValue, Math.Min(maxValue, parsedValue));
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
                DefaultKeyLogViewer,
                DefaultKeyOcrDiagnostic,
                DefaultKeyHotkeyGuideToggle);
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
            string logViewer,
            string ocrDiagnostic,
            string hotkeyGuideToggle)
        {
            MoveLock = moveLock;
            AreaSelect = areaSelect;
            Translate = translate;
            AutoTranslate = autoTranslate;
            ToggleEngine = toggleEngine;
            CopyResult = copyResult;
            LogViewer = logViewer;
            OcrDiagnostic = ocrDiagnostic;
            HotkeyGuideToggle = hotkeyGuideToggle;
        }

        public string MoveLock { get; }
        public string AreaSelect { get; }
        public string Translate { get; }
        public string AutoTranslate { get; }
        public string ToggleEngine { get; }
        public string CopyResult { get; }
        public string LogViewer { get; }
        public string OcrDiagnostic { get; }
        public string HotkeyGuideToggle { get; }
    }
}
