using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class TranslationServiceTests
    {
        private readonly TranslationService _service = new TranslationService();

        [Fact]
        public void CreatePlan_SkipsApiWhenTextAlreadyMatchesTargetLanguage()
        {
            TranslationPlan plan = _service.CreatePlan("이미 한국어입니다", "ko", true);

            Assert.Equal(TranslationRequestKind.None, plan.RequestKind);
            Assert.True(plan.HasImmediateResult);
            Assert.Equal("이미 한국어입니다", plan.ImmediateResult.TranslatedText);
            Assert.Equal("Skip", plan.ImmediateResult.EngineName);
            Assert.True(plan.ImmediateResult.Skipped);
            Assert.False(plan.ImmediateResult.FallbackUsed);
        }

        [Fact]
        public void CreatePlan_UsesGeminiWhenGeminiIsAvailableAndTranslationIsNeeded()
        {
            TranslationPlan plan = _service.CreatePlan("hello squad", "ko", TranslationEngineMode.Gemini, true, true);
            TranslationDecisionResult result = _service.CreateGeminiResult("안녕 팀", "gemini-test");

            Assert.Equal(TranslationRequestKind.Gemini, plan.RequestKind);
            Assert.False(plan.HasImmediateResult);
            Assert.Equal("안녕 팀", result.TranslatedText);
            Assert.Equal("Gemini gemini-test", result.EngineName);
            Assert.False(result.Skipped);
            Assert.False(result.FallbackUsed);
        }

        [Fact]
        public void CreatePlan_UsesLocalLlmWhenLocalLlmIsSelectedAndTranslationIsNeeded()
        {
            TranslationPlan plan = _service.CreatePlan("猫可愛 0", "ko", TranslationEngineMode.LocalLlm, true, true);
            TranslationDecisionResult result = _service.CreateLocalLlmResult("고양이는 귀여워", "qwen-test");

            Assert.Equal(TranslationRequestKind.LocalLlm, plan.RequestKind);
            Assert.False(plan.HasImmediateResult);
            Assert.Equal("고양이는 귀여워", result.TranslatedText);
            Assert.Equal("Local LLM qwen-test", result.EngineName);
            Assert.False(result.Skipped);
            Assert.False(result.FallbackUsed);
        }

        [Fact]
        public void ShouldFallbackToGoogle_AndGoogleFallbackResult_HandleEmptyGeminiResult()
        {
            Assert.True(_service.ShouldFallbackToGoogle(""));

            TranslationDecisionResult result = _service.CreateGoogleResult("구글 결과", true);

            Assert.Equal("[Gemini 에러 - 구글 전환됨] 구글 결과", result.TranslatedText);
            Assert.Equal("Google (Fallback)", result.EngineName);
            Assert.False(result.Skipped);
            Assert.True(result.FallbackUsed);
        }

        [Fact]
        public void ResolveGeminiAttempt_ReturnsFinalResultWhenGeminiTextExists()
        {
            TranslationAttemptResolution resolution = _service.ResolveGeminiAttempt("제미나이 결과", "gemini-test");

            Assert.True(resolution.HasFinalResult);
            Assert.False(resolution.RequiresGoogleFallback);
            Assert.Equal(TranslationRequestKind.None, resolution.NextRequestKind);
            Assert.Equal("제미나이 결과", resolution.FinalResult.TranslatedText);
            Assert.Equal("Gemini gemini-test", resolution.FinalResult.EngineName);
            Assert.False(resolution.FinalResult.FallbackUsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void ResolveGeminiAttempt_RequestsGoogleFallbackWhenGeminiResultIsEmpty(string geminiResult)
        {
            TranslationAttemptResolution resolution = _service.ResolveGeminiAttempt(geminiResult, "gemini-test");

            Assert.False(resolution.HasFinalResult);
            Assert.True(resolution.RequiresGoogleFallback);
            Assert.Equal(TranslationRequestKind.Google, resolution.NextRequestKind);
            Assert.Null(resolution.FinalResult);
        }

        [Theory]
        [InlineData(false, "Google", "구글 결과")]
        [InlineData(true, "Google (Fallback)", "[Gemini 에러 - 구글 전환됨] 구글 결과")]
        public void ResolveGoogleAttempt_ReturnsFinalGoogleResult(bool fallback, string expectedEngine, string expectedText)
        {
            TranslationAttemptResolution resolution = _service.ResolveGoogleAttempt("구글 결과", fallback);

            Assert.True(resolution.HasFinalResult);
            Assert.False(resolution.RequiresGoogleFallback);
            Assert.Equal(TranslationRequestKind.None, resolution.NextRequestKind);
            Assert.Equal(expectedEngine, resolution.FinalResult.EngineName);
            Assert.Equal(expectedText, resolution.FinalResult.TranslatedText);
            Assert.Equal(fallback, resolution.FinalResult.FallbackUsed);
        }

        [Fact]
        public void ResolveGoogleAttempt_UsesFallbackSourceName()
        {
            TranslationAttemptResolution resolution = _service.ResolveGoogleAttempt("구글 결과", true, "Local LLM");

            Assert.Equal("[Local LLM 에러 - 구글 전환됨] 구글 결과", resolution.FinalResult.TranslatedText);
            Assert.Equal("Google (Fallback)", resolution.FinalResult.EngineName);
            Assert.True(resolution.FinalResult.FallbackUsed);
        }

        [Fact]
        public void ResolveLocalLlmAttempt_ReturnsFinalResultWhenTextExists()
        {
            TranslationAttemptResolution resolution = _service.ResolveLocalLlmAttempt("로컬 결과", "qwen-test");

            Assert.True(resolution.HasFinalResult);
            Assert.False(resolution.RequiresGoogleFallback);
            Assert.Equal(TranslationRequestKind.None, resolution.NextRequestKind);
            Assert.Equal("로컬 결과", resolution.FinalResult.TranslatedText);
            Assert.Equal("Local LLM qwen-test", resolution.FinalResult.EngineName);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void ResolveLocalLlmAttempt_RequestsGoogleFallbackWhenResultIsEmpty(string localResult)
        {
            TranslationAttemptResolution resolution = _service.ResolveLocalLlmAttempt(localResult, "qwen-test");

            Assert.False(resolution.HasFinalResult);
            Assert.True(resolution.RequiresGoogleFallback);
            Assert.Equal(TranslationRequestKind.Google, resolution.NextRequestKind);
            Assert.Null(resolution.FinalResult);
        }

        [Fact]
        public void CreatePlan_UsesGoogleWhenGeminiIsDisabled()
        {
            TranslationPlan plan = _service.CreatePlan("hello squad", "ko", false);
            TranslationDecisionResult result = _service.CreateGoogleResult("안녕 팀", false);

            Assert.Equal(TranslationRequestKind.Google, plan.RequestKind);
            Assert.False(plan.HasImmediateResult);
            Assert.Equal("안녕 팀", result.TranslatedText);
            Assert.Equal("Google", result.EngineName);
            Assert.False(result.Skipped);
            Assert.False(result.FallbackUsed);
        }

        [Fact]
        public void CreateGoogleResult_AllowsEmptyGoogleResultWithoutFallbackPrefix()
        {
            TranslationDecisionResult result = _service.CreateGoogleResult("", false);

            Assert.Equal("", result.TranslatedText);
            Assert.Equal("Google", result.EngineName);
            Assert.False(result.Skipped);
            Assert.False(result.FallbackUsed);
        }

        [Fact]
        public void CreatePlan_PreservesSymbolOnlyTextWithoutApiCall()
        {
            TranslationPlan plan = _service.CreatePlan("123 !!", "ko", true);

            Assert.Equal(TranslationRequestKind.None, plan.RequestKind);
            Assert.Equal("123 !!", plan.ImmediateResult.TranslatedText);
            Assert.Equal("Google", plan.ImmediateResult.EngineName);
            Assert.False(plan.ImmediateResult.Skipped);
        }

        [Theory]
        [InlineData("hello", "en-US", true)]
        [InlineData("Привет", "ru", true)]
        [InlineData("こんにちは", "ja", true)]
        [InlineData("你好", "zh-Hans-CN", true)]
        [InlineData("hello 안녕", "en-US", false)]
        public void IsSameLanguage_DetectsTargetLanguageText(string text, string targetLanguageCode, bool expected)
        {
            Assert.Equal(expected, _service.IsSameLanguage(text, targetLanguageCode));
        }

        [Fact]
        public void TranslationService_DoesNotReferenceHttpOrUiTypes()
        {
            string serviceTypeNames = string.Join(
                "\n",
                typeof(TranslationService).Assembly
                    .GetTypes()
                    .Where(type =>
                        type == typeof(TranslationService) ||
                        type == typeof(TranslationPlan) ||
                        type == typeof(TranslationDecisionResult) ||
                        type == typeof(TranslationAttemptResolution) ||
                        type == typeof(TranslationRequestKind) ||
                        type == typeof(TranslationEngineMode))
                    .Select(type => type.AssemblyQualifiedName));

            Assert.DoesNotContain("HttpClient", serviceTypeNames);
            Assert.DoesNotContain("System.Net", serviceTypeNames);
            Assert.DoesNotContain("System.Windows", serviceTypeNames);
        }
    }
}
