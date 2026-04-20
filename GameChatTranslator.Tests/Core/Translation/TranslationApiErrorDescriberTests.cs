using System;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class TranslationApiErrorDescriberTests
    {
        private readonly TranslationApiErrorDescriber _describer = new TranslationApiErrorDescriber();

        [Fact]
        public void DescribeGeminiTranslateFailure_ExplainsInvalidModel()
        {
            GeminiTranslateApiResult result = GeminiTranslateApiResult.Failed(
                404,
                "{\"error\":{\"code\":404,\"message\":\"models/old-model is not found\",\"status\":\"NOT_FOUND\"}}");

            string message = _describer.DescribeGeminiTranslateFailure(result, "old-model");

            Assert.Contains("old-model", message);
            Assert.Contains("모델을 찾지 못했습니다", message);
            Assert.Contains("404 / NOT_FOUND / models/old-model is not found", message);
        }

        [Fact]
        public void DescribeGeminiModelListFailure_ExplainsPermissionIssue()
        {
            GeminiModelListApiResult result = GeminiModelListApiResult.Failed(
                403,
                "{\"error\":{\"message\":\"API key not valid\",\"status\":\"PERMISSION_DENIED\"}}");

            string message = _describer.DescribeGeminiModelListFailure(result);

            Assert.Contains("권한", message);
            Assert.Contains("API 키", message);
            Assert.Contains("PERMISSION_DENIED / API key not valid", message);
        }

        [Fact]
        public void DescribeGeminiTranslateFailure_ExplainsQuotaLimit()
        {
            GeminiTranslateApiResult result = GeminiTranslateApiResult.Failed(
                429,
                "{\"error\":{\"message\":\"Resource exhausted\",\"status\":\"RESOURCE_EXHAUSTED\"}}");

            string message = _describer.DescribeGeminiTranslateFailure(result, "gemini-2.5-flash");

            Assert.Contains("할당량", message);
            Assert.Contains("호출 제한", message);
            Assert.Contains("RESOURCE_EXHAUSTED / Resource exhausted", message);
        }

        [Fact]
        public void DescribeGeminiEmptyResponse_IncludesModelAndFallbackHint()
        {
            string message = _describer.DescribeGeminiEmptyResponse("gemini-test");

            Assert.Contains("gemini-test", message);
            Assert.Contains("번역문을 찾지 못했습니다", message);
            Assert.Contains("Google 번역으로 전환", message);
        }

        [Fact]
        public void DescribeGoogleFailure_IncludesNetworkGuidanceAndDetail()
        {
            string message = _describer.DescribeGoogleFailure(new TimeoutException("request timed out"));

            Assert.Contains("Google 번역 실패", message);
            Assert.Contains("네트워크", message);
            Assert.Contains("request timed out", message);
        }

        [Fact]
        public void DescribeGeminiModelListException_IncludesNetworkGuidance()
        {
            string message = _describer.DescribeGeminiModelListException(new TimeoutException("connection timed out"));

            Assert.Contains("Gemini 모델 목록 확인 실패", message);
            Assert.Contains("네트워크", message);
            Assert.Contains("connection timed out", message);
        }

        [Fact]
        public void ExtractApiErrorDetail_ReturnsPlainTextWhenJsonIsInvalid()
        {
            string detail = _describer.ExtractApiErrorDetail("temporary backend failure");

            Assert.Equal("temporary backend failure", detail);
        }

        [Theory]
        [InlineData(401, "API Key")]
        [InlineData(403, "권한")]
        [InlineData(404, "GeminiModel")]
        [InlineData(429, "할당량")]
        public void DescribeShortGeminiTranslateFailure_ReturnsOverlayFriendlyMessage(int statusCode, string expected)
        {
            GeminiTranslateApiResult result = GeminiTranslateApiResult.Failed(statusCode, "failure");

            string message = _describer.DescribeShortGeminiTranslateFailure(result);

            Assert.Contains(expected, message);
            Assert.DoesNotContain("상세", message);
        }

        [Fact]
        public void DescribeShortGoogleFailure_ReturnsShortOverlayMessage()
        {
            string message = _describer.DescribeShortGoogleFailure(new TimeoutException("timeout"));

            Assert.Equal("Google 번역 실패: 로그창 확인", message);
        }

        [Fact]
        public void DescribeShortGeminiEmptyResponse_ReturnsFallbackMessage()
        {
            Assert.Equal("Gemini 응답 오류: Google 전환", _describer.DescribeShortGeminiEmptyResponse());
        }
    }
}
