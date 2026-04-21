using System.Collections.Generic;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrLanguageStatusFormatterTests
    {
        private readonly OcrLanguageStatusFormatter _formatter = new OcrLanguageStatusFormatter();

        [Theory]
        [InlineData("ko", "ko-KR")]
        [InlineData("en-US", "en-US")]
        [InlineData("zh-Hans-CN", "zh-CN")]
        [InlineData("ja", "ja-JP")]
        [InlineData("ru", "ru-RU")]
        [InlineData("custom-tag", "custom-tag")]
        public void GetCapabilityLanguageTag_MapsAppTagsToCapabilityTags(string appLanguageTag, string expected)
        {
            Assert.Equal(expected, _formatter.GetCapabilityLanguageTag(appLanguageTag));
        }

        [Theory]
        [InlineData("Installed", "설치됨(Installed)")]
        [InlineData("NotPresent", "미설치(NotPresent)")]
        [InlineData("Removed", "미설치(Removed)")]
        [InlineData("InstallPending", "설치 후 재시작 대기(InstallPending)")]
        [InlineData("Unknown", "확인 실패")]
        [InlineData("CustomState", "CustomState")]
        public void FormatCapabilityState_MapsKnownStates(string state, string expected)
        {
            Assert.Equal(expected, _formatter.FormatCapabilityState(state));
        }

        [Fact]
        public void BuildLine_InstalledButEngineMissing_IncludesRebootHint()
        {
            OcrLanguageStatusEntry entry = _formatter.CreateEntry("영어", "en-US", "Installed", false);

            string line = _formatter.BuildLine(entry);

            Assert.Contains("WARN", line);
            Assert.Contains("capability: 설치됨(Installed)", line);
            Assert.Contains("OCR 엔진: 미감지", line);
            Assert.Contains("재부팅 필요 가능", line);
        }

        [Fact]
        public void BuildLine_NotInstalled_DoesNotIncludeRebootHint()
        {
            OcrLanguageStatusEntry entry = _formatter.CreateEntry("일본어", "ja", "NotPresent", false);

            string line = _formatter.BuildLine(entry);

            Assert.Contains("NO", line);
            Assert.Contains("capability: 미설치(NotPresent)", line);
            Assert.DoesNotContain("재부팅 필요 가능", line);
        }

        [Fact]
        public void BuildDisplayText_AddsGlobalRebootHintWhenNeeded()
        {
            var entries = new List<OcrLanguageStatusEntry>
            {
                _formatter.CreateEntry("영어", "en-US", "Installed", false),
                _formatter.CreateEntry("일본어", "ja", "Installed", true)
            };

            string text = _formatter.BuildDisplayText(entries);

            Assert.Contains("영어 (en-US)", text);
            Assert.Contains("일본어 (ja)", text);
            Assert.Contains("안내: capability는 설치됐지만 OCR 엔진이 아직 생성되지 않은 언어가 있습니다.", text);
        }

        [Fact]
        public void BuildDisplayText_ReturnsFallbackWhenEntriesAreEmpty()
        {
            Assert.Equal("OCR 언어팩 상태 확인 실패", _formatter.BuildDisplayText(new List<OcrLanguageStatusEntry>()));
        }
    }
}
