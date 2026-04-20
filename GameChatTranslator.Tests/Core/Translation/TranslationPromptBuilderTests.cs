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

        [Fact]
        public void CleanGoogleTranslateInput_JoinsEastAsianCharactersSplitByOcrSpaces()
        {
            string cleaned = _builder.CleanGoogleTranslateInput("猫 可 愛");

            Assert.Equal("猫可愛", cleaned);
        }

        [Fact]
        public void CleanGoogleTranslateInput_KeepsKoreanWordSpaces()
        {
            string cleaned = _builder.CleanGoogleTranslateInput("나는 고양이 좋아");

            Assert.Equal("나는 고양이 좋아", cleaned);
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

        [Theory]
        [InlineData("zh-Hans-CN", "zh-CN")]
        [InlineData("zh-CN", "zh-CN")]
        [InlineData("en-US", "en")]
        [InlineData("ko", "ko")]
        [InlineData("ja", "ja")]
        [InlineData("ru", "ru")]
        [InlineData("unknown", "auto")]
        [InlineData("", "auto")]
        [InlineData(null, "auto")]
        public void GetGoogleTranslateSourceLanguageCode_MapsGameLanguageOrFallsBackToAuto(string internalCode, string expected)
        {
            Assert.Equal(expected, _builder.GetGoogleTranslateSourceLanguageCode(internalCode));
        }

        [Fact]
        public void BuildGoogleTranslateUrl_EscapesTextAndMapsSourceAndTargetLanguage()
        {
            string url = _builder.BuildGoogleTranslateUrl("hello world?", "ja", "en-US");

            Assert.Contains("sl=ja", url);
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
