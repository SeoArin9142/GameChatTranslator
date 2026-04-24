using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class SettingsServiceTests
    {
        private readonly SettingsService _service = new SettingsService();

        [Fact]
        public void SelectGeminiKey_UsesCurrentSettingsKeyFirst()
        {
            GeminiKeySelection selection = _service.SelectGeminiKey("  current-key  ", "legacy-key");

            Assert.Equal("current-key", selection.Key);
            Assert.False(selection.ShouldMigrateLegacyKey);
        }

        [Fact]
        public void SelectGeminiKey_UsesLegacyKeyWhenCurrentKeyIsEmpty()
        {
            GeminiKeySelection selection = _service.SelectGeminiKey("   ", "  legacy-key  ");

            Assert.Equal("legacy-key", selection.Key);
            Assert.True(selection.ShouldMigrateLegacyKey);
        }

        [Fact]
        public void SelectGeminiKey_ReturnsEmptyWhenNoKeyExists()
        {
            GeminiKeySelection selection = _service.SelectGeminiKey(null, "");

            Assert.Equal("", selection.Key);
            Assert.False(selection.ShouldMigrateLegacyKey);
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultGeminiModel)]
        [InlineData("", SettingsService.DefaultGeminiModel)]
        [InlineData("   ", SettingsService.DefaultGeminiModel)]
        [InlineData("  gemini-custom  ", "gemini-custom")]
        public void NormalizeGeminiModel_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeGeminiModel(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultLocalLlmEndpoint)]
        [InlineData("", SettingsService.DefaultLocalLlmEndpoint)]
        [InlineData("  http://localhost:1234/v1/chat/completions  ", "http://localhost:1234/v1/chat/completions")]
        public void NormalizeLocalLlmEndpoint_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeLocalLlmEndpoint(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultLocalLlmModel)]
        [InlineData("", SettingsService.DefaultLocalLlmModel)]
        [InlineData("  custom-model  ", "custom-model")]
        public void NormalizeLocalLlmModel_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeLocalLlmModel(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultLocalLlmTimeoutSeconds)]
        [InlineData("abc", SettingsService.DefaultLocalLlmTimeoutSeconds)]
        [InlineData("0", SettingsService.MinLocalLlmTimeoutSeconds)]
        [InlineData("15", 15)]
        [InlineData("999", SettingsService.MaxLocalLlmTimeoutSeconds)]
        public void NormalizeLocalLlmTimeoutSeconds_ClampsRange(string rawValue, int expected)
        {
            Assert.Equal(expected, _service.NormalizeLocalLlmTimeoutSeconds(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultLocalLlmMaxTokens)]
        [InlineData("abc", SettingsService.DefaultLocalLlmMaxTokens)]
        [InlineData("1", SettingsService.MinLocalLlmMaxTokens)]
        [InlineData("200", 200)]
        [InlineData("9999", SettingsService.MaxLocalLlmMaxTokens)]
        public void NormalizeLocalLlmMaxTokens_ClampsRange(string rawValue, int expected)
        {
            Assert.Equal(expected, _service.NormalizeLocalLlmMaxTokens(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultTesseractExecutablePath)]
        [InlineData("", SettingsService.DefaultTesseractExecutablePath)]
        [InlineData("  C:\\Tesseract\\tesseract.exe  ", "C:\\Tesseract\\tesseract.exe")]
        public void NormalizeTesseractExecutablePath_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeTesseractExecutablePath(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultEasyOcrPythonPath)]
        [InlineData("", SettingsService.DefaultEasyOcrPythonPath)]
        [InlineData("  C:\\Python311\\python.exe  ", "C:\\Python311\\python.exe")]
        public void NormalizeEasyOcrPythonPath_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeEasyOcrPythonPath(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultEasyOcrLanguageCodes)]
        [InlineData("", SettingsService.DefaultEasyOcrLanguageCodes)]
        [InlineData("  ko+en|ja+en  ", "ko+en|ja+en")]
        public void NormalizeEasyOcrLanguageCodes_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeEasyOcrLanguageCodes(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultPaddleOcrPythonPath)]
        [InlineData("", SettingsService.DefaultPaddleOcrPythonPath)]
        [InlineData("  C:\\Python311\\python.exe  ", "C:\\Python311\\python.exe")]
        public void NormalizePaddleOcrPythonPath_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizePaddleOcrPythonPath(rawValue));
        }

        [Theory]
        [InlineData(null, SettingsService.DefaultPaddleOcrLanguageCodes)]
        [InlineData("", SettingsService.DefaultPaddleOcrLanguageCodes)]
        [InlineData("  korean|japan|ch  ", "korean|japan|ch")]
        public void NormalizePaddleOcrLanguageCodes_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizePaddleOcrLanguageCodes(rawValue));
        }

        [Theory]
        [InlineData(null, TranslationEngineMode.Google)]
        [InlineData("", TranslationEngineMode.Google)]
        [InlineData("Google", TranslationEngineMode.Google)]
        [InlineData("Gemini", TranslationEngineMode.Gemini)]
        [InlineData("LocalLlm", TranslationEngineMode.LocalLlm)]
        [InlineData("Local LLM", TranslationEngineMode.LocalLlm)]
        [InlineData("unknown", TranslationEngineMode.Google)]
        public void NormalizeTranslationEngineMode_MapsKnownValuesAndDefaultsToGoogle(string rawValue, TranslationEngineMode expected)
        {
            Assert.Equal(expected, _service.NormalizeTranslationEngineMode(rawValue));
        }

        [Theory]
        [InlineData(TranslationEngineMode.Google, "Google")]
        [InlineData(TranslationEngineMode.Gemini, "Gemini")]
        [InlineData(TranslationEngineMode.LocalLlm, "LocalLlm")]
        public void GetTranslationEngineTag_ReturnsConfigValue(TranslationEngineMode mode, string expected)
        {
            Assert.Equal(expected, _service.GetTranslationEngineTag(mode));
        }

        [Theory]
        [InlineData(null, MainOcrEngine.WindowsOcr)]
        [InlineData("", MainOcrEngine.WindowsOcr)]
        [InlineData("WindowsOcr", MainOcrEngine.WindowsOcr)]
        [InlineData("Tesseract", MainOcrEngine.Tesseract)]
        [InlineData("EasyOCR", MainOcrEngine.EasyOcr)]
        [InlineData("PaddleOCR", MainOcrEngine.PaddleOcr)]
        [InlineData("unknown", MainOcrEngine.WindowsOcr)]
        public void NormalizeMainOcrEngine_MapsKnownValuesAndDefaultsToWindows(string rawValue, MainOcrEngine expected)
        {
            Assert.Equal(expected, _service.NormalizeMainOcrEngine(rawValue));
        }

        [Theory]
        [InlineData(MainOcrEngine.WindowsOcr, "WindowsOcr")]
        [InlineData(MainOcrEngine.Tesseract, "Tesseract")]
        [InlineData(MainOcrEngine.EasyOcr, "EasyOcr")]
        [InlineData(MainOcrEngine.PaddleOcr, "PaddleOcr")]
        public void GetMainOcrEngineTag_ReturnsConfigValue(MainOcrEngine engine, string expected)
        {
            Assert.Equal(expected, _service.GetMainOcrEngineTag(engine));
        }

        [Theory]
        [InlineData(MainOcrEngine.WindowsOcr, "Windows OCR")]
        [InlineData(MainOcrEngine.Tesseract, "Tesseract")]
        [InlineData(MainOcrEngine.EasyOcr, "EasyOCR")]
        [InlineData(MainOcrEngine.PaddleOcr, "PaddleOCR")]
        public void GetMainOcrEngineDisplayName_ReturnsUserFacingLabel(MainOcrEngine engine, string expected)
        {
            Assert.Equal(expected, _service.GetMainOcrEngineDisplayName(engine));
        }

        [Theory]
        [InlineData(null, TranslationContentMode.Strinova)]
        [InlineData("", TranslationContentMode.Strinova)]
        [InlineData("Strinova", TranslationContentMode.Strinova)]
        [InlineData("ETC", TranslationContentMode.Etc)]
        [InlineData("Etc", TranslationContentMode.Etc)]
        [InlineData("General", TranslationContentMode.Etc)]
        [InlineData("unknown", TranslationContentMode.Strinova)]
        public void NormalizeTranslationContentMode_MapsKnownValuesAndDefaultsToStrinova(string rawValue, TranslationContentMode expected)
        {
            Assert.Equal(expected, _service.NormalizeTranslationContentMode(rawValue));
        }

        [Theory]
        [InlineData(TranslationContentMode.Strinova, "Strinova")]
        [InlineData(TranslationContentMode.Etc, "ETC")]
        public void GetTranslationContentModeTag_ReturnsConfigValue(TranslationContentMode mode, string expected)
        {
            Assert.Equal(expected, _service.GetTranslationContentModeTag(mode));
        }

        [Theory]
        [InlineData(null, ConfiguredOcrEngine.All)]
        [InlineData("", ConfiguredOcrEngine.All)]
        [InlineData("All", ConfiguredOcrEngine.All)]
        [InlineData("WindowsOcr", ConfiguredOcrEngine.WindowsOcr)]
        [InlineData("Tesseract", ConfiguredOcrEngine.Tesseract)]
        [InlineData("EasyOCR", ConfiguredOcrEngine.EasyOcr)]
        [InlineData("PaddleOCR", ConfiguredOcrEngine.PaddleOcr)]
        [InlineData("unknown", ConfiguredOcrEngine.All)]
        public void NormalizeConfiguredOcrEngine_MapsKnownValuesAndDefaultsToAll(string rawValue, ConfiguredOcrEngine expected)
        {
            Assert.Equal(expected, _service.NormalizeConfiguredOcrEngine(rawValue));
        }

        [Theory]
        [InlineData(ConfiguredOcrEngine.All, "All")]
        [InlineData(ConfiguredOcrEngine.WindowsOcr, "WindowsOcr")]
        [InlineData(ConfiguredOcrEngine.Tesseract, "Tesseract")]
        [InlineData(ConfiguredOcrEngine.EasyOcr, "EasyOcr")]
        [InlineData(ConfiguredOcrEngine.PaddleOcr, "PaddleOcr")]
        public void GetConfiguredOcrEngineTag_ReturnsConfigValue(ConfiguredOcrEngine engine, string expected)
        {
            Assert.Equal(expected, _service.GetConfiguredOcrEngineTag(engine));
        }

        [Theory]
        [InlineData(ConfiguredOcrEngine.All, "전체 비교")]
        [InlineData(ConfiguredOcrEngine.WindowsOcr, "Windows OCR")]
        [InlineData(ConfiguredOcrEngine.Tesseract, "Tesseract")]
        [InlineData(ConfiguredOcrEngine.EasyOcr, "EasyOCR")]
        [InlineData(ConfiguredOcrEngine.PaddleOcr, "PaddleOCR")]
        public void GetConfiguredOcrEngineDisplayName_ReturnsUserFacingLabel(ConfiguredOcrEngine engine, string expected)
        {
            Assert.Equal(expected, _service.GetConfiguredOcrEngineDisplayName(engine));
        }

        [Theory]
        [InlineData(null, new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr })]
        [InlineData("", new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr })]
        [InlineData("All", new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr })]
        [InlineData("WindowsOcr", new[] { ConfiguredOcrEngine.WindowsOcr })]
        [InlineData("Tesseract,EasyOcr", new[] { ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr })]
        [InlineData("PaddleOCR|WindowsOcr", new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.PaddleOcr })]
        [InlineData("unknown", new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr })]
        public void NormalizeConfiguredOcrEngineSelection_MapsLegacyAndMultiSelectValues(string rawValue, ConfiguredOcrEngine[] expected)
        {
            Assert.Equal(expected, _service.NormalizeConfiguredOcrEngineSelection(rawValue).ToArray());
        }

        [Theory]
        [InlineData(new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr }, "All")]
        [InlineData(new[] { ConfiguredOcrEngine.WindowsOcr }, "WindowsOcr")]
        [InlineData(new[] { ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.PaddleOcr }, "Tesseract,PaddleOcr")]
        [InlineData(new ConfiguredOcrEngine[0], "All")]
        public void GetConfiguredOcrEngineSelectionTag_ReturnsNormalizedConfigValue(ConfiguredOcrEngine[] engines, string expected)
        {
            Assert.Equal(expected, _service.GetConfiguredOcrEngineSelectionTag(engines));
        }

        [Theory]
        [InlineData(new[] { ConfiguredOcrEngine.WindowsOcr, ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr, ConfiguredOcrEngine.PaddleOcr }, "전체 비교")]
        [InlineData(new[] { ConfiguredOcrEngine.WindowsOcr }, "Windows OCR")]
        [InlineData(new[] { ConfiguredOcrEngine.Tesseract, ConfiguredOcrEngine.EasyOcr }, "Tesseract + EasyOCR")]
        public void GetConfiguredOcrEngineSelectionDisplayName_ReturnsUserFacingLabel(ConfiguredOcrEngine[] engines, string expected)
        {
            Assert.Equal(expected, _service.GetConfiguredOcrEngineSelectionDisplayName(engines));
        }


        [Theory]
        [InlineData("true")]
        [InlineData("TRUE")]
        [InlineData("1")]
        [InlineData("yes")]
        [InlineData("Y")]
        [InlineData("on")]
        [InlineData("  on  ")]
        public void IsEnabled_AcceptsTruthyValues(string rawValue)
        {
            Assert.True(_service.IsEnabled(rawValue));
        }

        [Theory]
        [InlineData("false")]
        [InlineData("FALSE")]
        [InlineData("0")]
        [InlineData("no")]
        [InlineData("N")]
        [InlineData("off")]
        [InlineData("  off  ")]
        public void IsDisabled_AcceptsFalsyValues(string rawValue)
        {
            Assert.True(_service.IsDisabled(rawValue));
        }

        [Theory]
        [InlineData(null, true, true)]
        [InlineData("", false, false)]
        [InlineData("unknown", true, true)]
        [InlineData("unknown", false, false)]
        [InlineData("true", false, true)]
        [InlineData("false", true, false)]
        public void IsEnabledOrDefault_UsesDefaultOnlyForUnknownValues(string rawValue, bool defaultValue, bool expected)
        {
            Assert.Equal(expected, _service.IsEnabledOrDefault(rawValue, defaultValue));
        }

        [Theory]
        [InlineData(null, "Ctrl+7")]
        [InlineData("", "Ctrl+7")]
        [InlineData("   ", "Ctrl+7")]
        [InlineData("  Ctrl+8  ", "Ctrl+8")]
        public void NormalizeHotkey_UsesDefaultForBlankValues(string rawValue, string expected)
        {
            Assert.Equal(expected, _service.NormalizeHotkey(rawValue, SettingsService.DefaultKeyMoveLock));
        }

        [Fact]
        public void GetDefaultHotkeys_ReturnsSharedDefaultHotkeyValues()
        {
            DefaultHotkeys hotkeys = _service.GetDefaultHotkeys();

            Assert.Equal("Ctrl+7", hotkeys.MoveLock);
            Assert.Equal("Ctrl+8", hotkeys.AreaSelect);
            Assert.Equal("Ctrl+-", hotkeys.Translate);
            Assert.Equal("Ctrl+=", hotkeys.AutoTranslate);
            Assert.Equal("Ctrl+-", hotkeys.ToggleEngine);
            Assert.Equal("Ctrl+6", hotkeys.CopyResult);
            Assert.Equal("Ctrl+=", hotkeys.LogViewer);
            Assert.Equal("Ctrl+5", hotkeys.OcrDiagnostic);
            Assert.Equal("Ctrl+F10", hotkeys.HotkeyGuideToggle);
            Assert.Equal("Ctrl+0", hotkeys.OpenSettings);
        }

        [Fact]
        public void SettingsService_DoesNotReferenceIniFileOrSystemIoTypes()
        {
            string settingsTypeNames = string.Join(
                "\n",
                typeof(SettingsService).Assembly
                    .GetTypes()
                    .Where(type =>
                        type == typeof(SettingsService) ||
                        type == typeof(GeminiKeySelection) ||
                        type == typeof(DefaultHotkeys))
                    .Select(type => type.AssemblyQualifiedName));

            Assert.DoesNotContain("IniFile", settingsTypeNames);
            Assert.DoesNotContain("System.IO", settingsTypeNames);
        }
    }
}
