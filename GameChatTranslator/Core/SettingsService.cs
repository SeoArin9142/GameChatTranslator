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
        public const string DefaultTesseractExecutablePath = "tesseract";
        public const string DefaultTesseractLanguageCodes = "eng+kor+jpn+chi_sim";
        public const string DefaultEasyOcrPythonPath = "python";
        public const string DefaultEasyOcrLanguageCodes = "en+ko+ja+ch_sim";
        public const string DefaultPaddleOcrPythonPath = "python";
        public const string DefaultPaddleOcrLanguageCodes = "en+korean+japan+ch";
        public const string DefaultTranslationEngine = "Google";
        public const string DefaultTranslationContentMode = "Strinova";
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
        public const string DefaultKeyOpenSettings = "Ctrl+8";
        public const string DefaultOcrEngineSelection = "All";
        public const string DefaultAutoCopyTranslationResult = "false";

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
        /// Tesseract 실행 파일 경로가 비어 있으면 기본 명령 이름을 반환합니다.
        /// 실제 설치 경로 자동 탐지는 런타임 어댑터에서 수행하고, 설정 파일에는 단순 기본값만 유지합니다.
        /// </summary>
        public string NormalizeTesseractExecutablePath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultTesseractExecutablePath : value.Trim();
        }

        /// <summary>
        /// Tesseract 언어 코드 문자열이 비어 있으면 기본 다국어 조합을 반환합니다.
        /// </summary>
        public string NormalizeTesseractLanguageCodes(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultTesseractLanguageCodes : value.Trim();
        }

        /// <summary>
        /// EasyOCR Python 실행 경로가 비어 있으면 기본 python 명령을 반환합니다.
        /// </summary>
        public string NormalizeEasyOcrPythonPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultEasyOcrPythonPath : value.Trim();
        }

        /// <summary>
        /// EasyOCR 언어 코드 문자열이 비어 있으면 기본 다국어 조합을 반환합니다.
        /// </summary>
        public string NormalizeEasyOcrLanguageCodes(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultEasyOcrLanguageCodes : value.Trim();
        }

        /// <summary>
        /// PaddleOCR Python 실행 경로가 비어 있으면 기본 python 명령을 반환합니다.
        /// </summary>
        public string NormalizePaddleOcrPythonPath(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultPaddleOcrPythonPath : value.Trim();
        }

        /// <summary>
        /// PaddleOCR 언어 코드 문자열이 비어 있으면 기본 다국어 조합을 반환합니다.
        /// </summary>
        public string NormalizePaddleOcrLanguageCodes(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? DefaultPaddleOcrLanguageCodes : value.Trim();
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
        /// 저장된 번역 대상 방식 문자열을 앱 내부 enum으로 변환합니다.
        /// Strinova는 기존 "[캐릭터명]: 채팅내용" 검증 방식을 사용하고,
        /// Etc는 OCR로 읽은 전체 텍스트를 하나의 번역 대상으로 사용합니다.
        /// </summary>
        public TranslationContentMode NormalizeTranslationContentMode(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.Equals("ETC", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Etc", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("General", StringComparison.OrdinalIgnoreCase))
            {
                return TranslationContentMode.Etc;
            }

            return TranslationContentMode.Strinova;
        }

        /// <summary>
        /// 번역 대상 방식 enum을 config.ini와 RadioButton.Tag에 저장할 문자열로 변환합니다.
        /// </summary>
        public string GetTranslationContentModeTag(TranslationContentMode mode)
        {
            return mode == TranslationContentMode.Etc ? "ETC" : DefaultTranslationContentMode;
        }

        /// <summary>
        /// OCR 엔진 선택값을 앱 내부 enum으로 변환합니다.
        /// 알 수 없는 값은 전체 비교(All)로 되돌려 진단 흐름이 끊기지 않게 합니다.
        /// </summary>
        public ConfiguredOcrEngine NormalizeConfiguredOcrEngine(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.Equals("WindowsOcr", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Windows", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("WinOcr", StringComparison.OrdinalIgnoreCase))
            {
                return ConfiguredOcrEngine.WindowsOcr;
            }

            if (normalized.Equals("Tesseract", StringComparison.OrdinalIgnoreCase))
            {
                return ConfiguredOcrEngine.Tesseract;
            }

            if (normalized.Equals("EasyOcr", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("EasyOCR", StringComparison.OrdinalIgnoreCase))
            {
                return ConfiguredOcrEngine.EasyOcr;
            }

            if (normalized.Equals("PaddleOcr", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("PaddleOCR", StringComparison.OrdinalIgnoreCase))
            {
                return ConfiguredOcrEngine.PaddleOcr;
            }

            return ConfiguredOcrEngine.All;
        }

        /// <summary>
        /// OCR 엔진 enum을 config.ini와 ComboBox.Tag에 저장할 문자열로 변환합니다.
        /// </summary>
        public string GetConfiguredOcrEngineTag(ConfiguredOcrEngine engine)
        {
            return engine switch
            {
                ConfiguredOcrEngine.WindowsOcr => "WindowsOcr",
                ConfiguredOcrEngine.Tesseract => "Tesseract",
                ConfiguredOcrEngine.EasyOcr => "EasyOcr",
                ConfiguredOcrEngine.PaddleOcr => "PaddleOcr",
                _ => DefaultOcrEngineSelection
            };
        }

        /// <summary>
        /// OCR 엔진 enum을 설정창과 진단 요약에 표시할 사용자용 이름으로 변환합니다.
        /// </summary>
        public string GetConfiguredOcrEngineDisplayName(ConfiguredOcrEngine engine)
        {
            return engine switch
            {
                ConfiguredOcrEngine.WindowsOcr => "Windows OCR",
                ConfiguredOcrEngine.Tesseract => "Tesseract",
                ConfiguredOcrEngine.EasyOcr => "EasyOCR",
                ConfiguredOcrEngine.PaddleOcr => "PaddleOCR",
                _ => "전체 비교"
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
                DefaultKeyHotkeyGuideToggle,
                DefaultKeyOpenSettings);
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
            string hotkeyGuideToggle,
            string openSettings)
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
            OpenSettings = openSettings;
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
        public string OpenSettings { get; }
    }

    /// <summary>
    /// 설정창에서 사용자가 선택한 OCR 엔진 범위입니다.
    /// All은 비교 모드, 나머지는 선택한 엔진 후보만 점수 계산합니다.
    /// </summary>
    public enum ConfiguredOcrEngine
    {
        All,
        WindowsOcr,
        Tesseract,
        EasyOcr,
        PaddleOcr
    }

    /// <summary>
    /// OCR 결과에서 실제 번역 대상으로 사용할 텍스트 선택 방식입니다.
    /// Strinova는 캐릭터명 검증을 통과한 채팅만 번역하고, Etc는 OCR 전체 텍스트를 번역합니다.
    /// </summary>
    public enum TranslationContentMode
    {
        Strinova,
        Etc
    }
}
