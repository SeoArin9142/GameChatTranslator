using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrTranslationHarnessServiceTests
    {
        private readonly OcrTranslationHarnessService _service = new OcrTranslationHarnessService(new TranslationPromptBuilder());

        [Fact]
        public void BuildRequests_ParsesNicknamePrefixAndKeepsCharacterLabel()
        {
            var request = _service.BuildRequests(new[] { "SeoArin [치요]: 猫は可愛い" }).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[치요]: ", request.Prefix);
            Assert.Equal("猫は可愛い", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Strinova, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_FallsBackToRawWhenChatParseFailsButMeaningfulTextRemains()
        {
            var request = _service.BuildRequests(new[] { "猫 可 愛" }).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[RAW]: ", request.Prefix);
            Assert.Equal("猫可愛", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Etc, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_SkipsNoiseOnlyText()
        {
            var request = _service.BuildRequests(new[] { "###@@@%%%" }).Single();

            Assert.True(request.Skipped);
            Assert.Equal("글자 없음", request.SkipReason);
        }

        [Fact]
        public void BuildRequests_SkipsBrokenNoiseAfterEtcCleaning()
        {
            var request = _service.BuildRequests(new[] { "乞5U(私(z構ote.ntOØ?" }).Single();

            Assert.True(request.Skipped);
            Assert.Equal("파싱 실패 / 노이즈", request.SkipReason);
        }
    }
}
