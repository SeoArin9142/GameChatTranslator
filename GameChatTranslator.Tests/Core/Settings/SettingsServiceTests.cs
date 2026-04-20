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
            Assert.Equal("Ctrl+9", hotkeys.Translate);
            Assert.Equal("Ctrl+0", hotkeys.AutoTranslate);
            Assert.Equal("Ctrl+-", hotkeys.ToggleEngine);
            Assert.Equal("Ctrl+6", hotkeys.CopyResult);
            Assert.Equal("Ctrl+=", hotkeys.LogViewer);
            Assert.Equal("Ctrl+5", hotkeys.OcrDiagnostic);
            Assert.Equal("Ctrl+F10", hotkeys.HotkeyGuideToggle);
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
