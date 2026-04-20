using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class TranslationPromptBuilderTests
    {
        private readonly TranslationPromptBuilder _builder = new TranslationPromptBuilder();

        [Fact]
        public void CleanGoogleTranslateInput_RemovesOcrNoiseAndNormalizesWhitespace()
        {
            string cleaned = _builder.CleanGoogleTranslateInput("[미셸] 12:34 hello@@@ ---   world");

            Assert.Equal("미셸 hello - world", cleaned);
        }

        [Theory]
        [InlineData("hello", true)]
        [InlineData("안", true)]
        [InlineData("你", true)]
        [InlineData("a", false)]
        [InlineData("123 !!", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void HasTranslatableContent_AppliesMinimumContentRules(string cleanedText, bool expected)
        {
            Assert.Equal(expected, _builder.HasTranslatableContent(cleanedText));
        }

        [Theory]
        [InlineData("zh-Hans-CN", "zh-CN")]
        [InlineData("en-US", "en")]
        [InlineData("ko", "ko")]
        [InlineData("ja", "ja")]
        public void GetGoogleTranslateLanguageCode_MapsInternalCodes(string internalCode, string expected)
        {
            Assert.Equal(expected, _builder.GetGoogleTranslateLanguageCode(internalCode));
        }

        [Fact]
        public void BuildGoogleTranslateUrl_EscapesTextAndMapsTargetLanguage()
        {
            string url = _builder.BuildGoogleTranslateUrl("hello world?", "en-US");

            Assert.Contains("tl=en", url);
            Assert.Contains("q=hello%20world%3F", url);
        }

        [Theory]
        [InlineData("ko", "Korean")]
        [InlineData("en-US", "English")]
        [InlineData("ja", "ja")]
        public void GetGeminiTargetLanguageName_MapsKnownLanguageNames(string internalCode, string expected)
        {
            Assert.Equal(expected, _builder.GetGeminiTargetLanguageName(internalCode));
        }

        [Fact]
        public void BuildGeminiPrompt_IncludesTargetLanguageAndOriginalText()
        {
            string prompt = _builder.BuildGeminiPrompt("伽好", "ko");

            Assert.Contains("Korean", prompt);
            Assert.Contains("伽好", prompt);
            Assert.Contains("Output ONLY the translation", prompt);
        }
    }
}
