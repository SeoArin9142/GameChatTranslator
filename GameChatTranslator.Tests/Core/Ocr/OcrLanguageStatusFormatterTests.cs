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

        [Fact]
        public void BuildLine_InstalledButEngineMissing_IncludesRebootHint()
        {
            OcrLanguageStatusEntry entry = _formatter.CreateEntry("영어", "en-US", "Installed", false);

            string line = _formatter.BuildLine(entry);

            Assert.Equal("WARN  영어 (en-US) : 미감지 (capability 설치됨, 재부팅 필요 가능)", line);
        }

        [Fact]
        public void BuildLine_NotInstalled_DoesNotIncludeRebootHint()
        {
            OcrLanguageStatusEntry entry = _formatter.CreateEntry("일본어", "ja", "NotPresent", false);

            string line = _formatter.BuildLine(entry);

            Assert.Equal("NO  일본어 (ja) : 미감지", line);
            Assert.DoesNotContain("재부팅 필요 가능", line);
        }

        [Fact]
        public void BuildLine_EngineAvailable_ShowsSimpleAvailableText()
        {
            OcrLanguageStatusEntry entry = _formatter.CreateEntry("중국어 간체", "zh-Hans-CN", "Installed", true);

            string line = _formatter.BuildLine(entry);

            Assert.Equal("OK  중국어 간체 (zh-Hans-CN) : 사용 가능", line);
        }

        [Fact]
        public void BuildDisplayText_JoinsSimplifiedLinesOnly()
        {
            var entries = new List<OcrLanguageStatusEntry>
            {
                _formatter.CreateEntry("영어", "en-US", "Installed", false),
                _formatter.CreateEntry("일본어", "ja", "Installed", true)
            };

            string text = _formatter.BuildDisplayText(entries);

            Assert.Equal(
                "WARN  영어 (en-US) : 미감지 (capability 설치됨, 재부팅 필요 가능)" +
                System.Environment.NewLine +
                "OK  일본어 (ja) : 사용 가능",
                text);
        }

        [Fact]
        public void BuildDisplayText_ReturnsFallbackWhenEntriesAreEmpty()
        {
            Assert.Equal("OCR 언어팩 상태 확인 실패", _formatter.BuildDisplayText(new List<OcrLanguageStatusEntry>()));
        }
    }
}
