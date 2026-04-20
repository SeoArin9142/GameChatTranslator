using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class TranslationApiClientTests
    {
        [Fact]
        public async Task TranslateWithGoogleAsync_CleansInputAndParsesResponse()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("translate.googleapis.com", request.RequestUri.ToString());
                Assert.Contains("sl=en", request.RequestUri.Query);
                Assert.Contains("tl=ko", request.RequestUri.Query);
                Assert.Contains("hello", WebUtility.UrlDecode(request.RequestUri.Query));
                return Task.FromResult(JsonResponse("[[[\"안녕\",\"hello\",null,null,3]],null,\"en\"]"));
            });
            TranslationApiClient client = CreateClient(handler);

            string result = await client.TranslateWithGoogleAsync("[미셸] 12:34 hello@@@", "en-US", "ko");

            Assert.Equal("안녕", result);
            Assert.Equal(1, handler.CallCount);
        }

        [Fact]
        public async Task TranslateWithGoogleAsync_SkipsNonTranslatableContent()
        {
            var handler = new StubHttpMessageHandler(_ => throw new InvalidOperationException("HTTP 호출이 없어야 합니다."));
            TranslationApiClient client = CreateClient(handler);

            string result = await client.TranslateWithGoogleAsync("123 !!!", "en-US", "ko");

            Assert.Equal("", result);
            Assert.Equal(0, handler.CallCount);
        }

        [Fact]
        public async Task ListGeminiModelsAsync_ReturnsModelNames()
        {
            var handler = new StubHttpMessageHandler(request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("v1beta/models", request.RequestUri.ToString());
                Assert.Contains("key=test-key", request.RequestUri.Query);
                return Task.FromResult(JsonResponse("{\"models\":[{\"name\":\"models/gemini-2.5-flash\"},{\"name\":\"models/gemini-2.5-pro\"}]}"));
            });
            TranslationApiClient client = CreateClient(handler);

            GeminiModelListApiResult result = await client.ListGeminiModelsAsync("test-key");

            Assert.True(result.IsSuccess);
            Assert.Equal(new[] { "gemini-2.5-flash", "gemini-2.5-pro" }, result.Models);
        }

        [Fact]
        public async Task ListGeminiModelsAsync_ReturnsHttpFailure()
        {
            var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse("{\"error\":\"denied\"}", HttpStatusCode.Forbidden)));
            TranslationApiClient client = CreateClient(handler);

            GeminiModelListApiResult result = await client.ListGeminiModelsAsync("bad-key");

            Assert.False(result.IsSuccess);
            Assert.Equal(403, result.StatusCode);
            Assert.Contains("denied", result.ErrorMessage);
        }

        [Fact]
        public async Task TranslateWithGeminiAsync_BuildsPromptAndParsesResponse()
        {
            var handler = new StubHttpMessageHandler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Contains("models/gemini-test:generateContent", request.RequestUri.ToString());
                Assert.Contains("key=test-key", request.RequestUri.Query);

                string payload = await request.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(payload);
                string prompt = document.RootElement
                    .GetProperty("contents")[0]
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();
                Assert.Contains("Korean", prompt);
                Assert.Contains("伽好", prompt);

                return JsonResponse("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"안녕하세요\"}]}}]}");
            });
            TranslationApiClient client = CreateClient(handler);

            GeminiTranslateApiResult result = await client.TranslateWithGeminiAsync("伽好", "ko", "test-key", "gemini-test");

            Assert.True(result.IsSuccess);
            Assert.Equal("안녕하세요", result.Text);
        }

        [Fact]
        public async Task TranslateWithGeminiAsync_ReturnsErrorDetailForHttpFailure()
        {
            var handler = new StubHttpMessageHandler(_ => Task.FromResult(JsonResponse("{\"error\":\"bad model\"}", HttpStatusCode.BadRequest)));
            TranslationApiClient client = CreateClient(handler);

            GeminiTranslateApiResult result = await client.TranslateWithGeminiAsync("hello", "ko", "test-key", "bad-model");

            Assert.False(result.IsSuccess);
            Assert.Equal(400, result.StatusCode);
            Assert.Contains("bad model", result.ErrorMessage);
        }

        private static TranslationApiClient CreateClient(HttpMessageHandler handler)
        {
            return new TranslationApiClient(
                new HttpClient(handler),
                new TranslationPromptBuilder(),
                new TranslationResultParser());
        }

        private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(json)
            };
        }

        private sealed class StubHttpMessageHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

            public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
            {
                _handler = handler;
            }

            public int CallCount { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                CallCount++;
                return await _handler(request);
            }
        }
    }
}
