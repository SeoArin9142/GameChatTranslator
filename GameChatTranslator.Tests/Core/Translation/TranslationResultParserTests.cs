using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class TranslationResultParserTests
    {
        private readonly TranslationResultParser _parser = new TranslationResultParser();

        [Fact]
        public void ParseGoogleTranslateResponse_CombinesTranslatedSegments()
        {
            string json = "[[[\"안녕\", \"hello\", null, null],[\" 세계\", \" world\", null, null]], null, \"en\"]";

            string result = _parser.ParseGoogleTranslateResponse(json);

            Assert.Equal("안녕 세계", result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("{}")]
        [InlineData("not-json")]
        public void ParseGoogleTranslateResponse_ReturnsEmptyForInvalidResponses(string json)
        {
            Assert.Equal("", _parser.ParseGoogleTranslateResponse(json));
        }

        [Fact]
        public void ParseGeminiTranslateResponse_ExtractsFirstCandidateText()
        {
            string json = "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\" 번역 결과 \\n\"}]}}]}";

            string result = _parser.ParseGeminiTranslateResponse(json);

            Assert.Equal("번역 결과", result);
        }

        [Fact]
        public void ParseOpenAiChatCompletionResponse_ExtractsMessageContent()
        {
            string json = "{\"choices\":[{\"message\":{\"content\":\"고양이는 귀여워요.\"},\"finish_reason\":\"stop\"}]}";

            string result = _parser.ParseOpenAiChatCompletionResponse(json);

            Assert.Equal("고양이는 귀여워요.", result);
        }

        [Fact]
        public void ParseOpenAiChatCompletionResponse_RemovesThinkBlock()
        {
            string json = "{\"choices\":[{\"message\":{\"content\":\"<think>reasoning</think>고양이는 귀여워요.\"}}]}";

            string result = _parser.ParseOpenAiChatCompletionResponse(json);

            Assert.Equal("고양이는 귀여워요.", result);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("{}")]
        [InlineData("not-json")]
        public void ParseGeminiTranslateResponse_ReturnsEmptyForInvalidResponses(string json)
        {
            Assert.Equal("", _parser.ParseGeminiTranslateResponse(json));
        }
    }
}
