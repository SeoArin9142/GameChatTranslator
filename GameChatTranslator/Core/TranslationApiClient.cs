using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace GameTranslator
{
    /// <summary>
    /// Google Translate와 Gemini API의 실제 HTTP 요청/응답 처리를 담당합니다.
    /// 재시도, fallback, UI 로그, 엔진 선택은 호출자가 담당하고, 이 클래스는 네트워크 호출과 응답 파싱만 수행합니다.
    /// </summary>
    public sealed class TranslationApiClient
    {
        private readonly HttpClient _httpClient;
        private readonly TranslationPromptBuilder _promptBuilder;
        private readonly TranslationResultParser _resultParser;

        /// <summary>
        /// 번역 API 클라이언트를 생성합니다.
        /// <paramref name="httpClient"/>는 외부에서 수명 관리하는 HTTP 클라이언트,
        /// <paramref name="promptBuilder"/>는 요청 URL/프롬프트 생성기,
        /// <paramref name="resultParser"/>는 API 응답 JSON 파서입니다.
        /// </summary>
        public TranslationApiClient(
            HttpClient httpClient,
            TranslationPromptBuilder promptBuilder,
            TranslationResultParser resultParser)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _promptBuilder = promptBuilder ?? throw new ArgumentNullException(nameof(promptBuilder));
            _resultParser = resultParser ?? throw new ArgumentNullException(nameof(resultParser));
        }

        /// <summary>
        /// Google Translate API를 호출할 만큼 의미 있는 문자열인지 판단합니다.
        /// OCR 노이즈 정리 후 숫자/기호뿐이면 API 호출을 하지 않도록 false를 반환합니다.
        /// </summary>
        public bool CanTranslateWithGoogle(string text)
        {
            string cleaned = _promptBuilder.CleanGoogleTranslateInput(text);
            return _promptBuilder.HasTranslatableContent(cleaned);
        }

        /// <summary>
        /// Google Translate 비공식 무료 엔드포인트를 1회 호출합니다.
        /// <paramref name="text"/>는 OCR 후처리 문장이고,
        /// <paramref name="sourceLanguageCode"/>는 ko/en-US/zh-Hans-CN 같은 앱 내부 게임 원문 언어 코드,
        /// <paramref name="targetLanguageCode"/>는 ko/en-US/zh-Hans-CN 같은 앱 내부 목표 언어 코드입니다.
        /// 반환값은 파싱된 번역문이며, 의미 없는 입력이면 빈 문자열입니다.
        /// </summary>
        public async Task<string> TranslateWithGoogleAsync(string text, string sourceLanguageCode, string targetLanguageCode)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string cleaned = _promptBuilder.CleanGoogleTranslateInput(text);
            if (!_promptBuilder.HasTranslatableContent(cleaned)) return "";

            string url = _promptBuilder.BuildGoogleTranslateUrl(cleaned, sourceLanguageCode, targetLanguageCode);
            string responseJson = await _httpClient.GetStringAsync(url);
            return _resultParser.ParseGoogleTranslateResponse(responseJson);
        }

        /// <summary>
        /// Gemini API 키로 사용 가능한 모델 목록을 조회합니다.
        /// <paramref name="apiKey"/>는 Google AI Studio에서 발급받은 Gemini API 키입니다.
        /// </summary>
        public async Task<GeminiModelListApiResult> ListGeminiModelsAsync(string apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return GeminiModelListApiResult.Failed(null, "API 키가 비어 있습니다.");
            }

            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return GeminiModelListApiResult.Failed((int)response.StatusCode, responseJson);
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseJson);
                if (!document.RootElement.TryGetProperty("models", out JsonElement modelsElement) ||
                    modelsElement.ValueKind != JsonValueKind.Array)
                {
                    return GeminiModelListApiResult.Failed(null, "models 배열이 없습니다.");
                }

                List<string> models = modelsElement
                    .EnumerateArray()
                    .Select(ReadGeminiModelName)
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .ToList();

                return GeminiModelListApiResult.Succeeded(models);
            }
            catch (Exception ex)
            {
                return GeminiModelListApiResult.Failed(null, ex.Message);
            }
        }

        /// <summary>
        /// Gemini generateContent API를 1회 호출합니다.
        /// <paramref name="text"/>는 OCR 후처리 문장,
        /// <paramref name="targetLanguageCode"/>는 앱 내부 목표 언어 코드,
        /// <paramref name="apiKey"/>는 Gemini API 키,
        /// <paramref name="modelName"/>은 호출할 Gemini 모델명입니다.
        /// </summary>
        public async Task<GeminiTranslateApiResult> TranslateWithGeminiAsync(
            string text,
            string targetLanguageCode,
            string apiKey,
            string modelName)
        {
            string url = $"https://generativelanguage.googleapis.com/v1/models/{modelName}:generateContent?key={apiKey}";
            string prompt = _promptBuilder.BuildGeminiPrompt(text, targetLanguageCode);

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            string jsonPayload = JsonSerializer.Serialize(requestBody);

            using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _httpClient.PostAsync(url, content);
            string responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return GeminiTranslateApiResult.Failed((int)response.StatusCode, responseJson);
            }

            string parsedText = _resultParser.ParseGeminiTranslateResponse(responseJson);
            return GeminiTranslateApiResult.Succeeded(parsedText);
        }

        private static string ReadGeminiModelName(JsonElement modelElement)
        {
            if (!modelElement.TryGetProperty("name", out JsonElement nameElement)) return "";
            if (nameElement.ValueKind != JsonValueKind.String) return "";
            return (nameElement.GetString() ?? "").Replace("models/", "");
        }
    }

    /// <summary>
    /// Gemini 모델 목록 조회 결과입니다.
    /// IsSuccess가 false이면 StatusCode 또는 ErrorMessage를 UI 로그에 표시할 수 있습니다.
    /// </summary>
    public sealed class GeminiModelListApiResult
    {
        private GeminiModelListApiResult(bool isSuccess, IReadOnlyList<string> models, int? statusCode, string errorMessage)
        {
            IsSuccess = isSuccess;
            Models = models;
            StatusCode = statusCode;
            ErrorMessage = errorMessage ?? "";
        }

        public bool IsSuccess { get; }
        public IReadOnlyList<string> Models { get; }
        public int? StatusCode { get; }
        public string ErrorMessage { get; }

        public static GeminiModelListApiResult Succeeded(IReadOnlyList<string> models)
        {
            return new GeminiModelListApiResult(true, models ?? Array.Empty<string>(), null, "");
        }

        public static GeminiModelListApiResult Failed(int? statusCode, string errorMessage)
        {
            return new GeminiModelListApiResult(false, Array.Empty<string>(), statusCode, errorMessage);
        }
    }

    /// <summary>
    /// Gemini 번역 API 호출 결과입니다.
    /// IsSuccess가 true여도 Text가 비어 있으면 호출자가 기존 fallback 정책에 따라 Google로 전환할 수 있습니다.
    /// </summary>
    public sealed class GeminiTranslateApiResult
    {
        private GeminiTranslateApiResult(bool isSuccess, string text, int? statusCode, string errorMessage)
        {
            IsSuccess = isSuccess;
            Text = text ?? "";
            StatusCode = statusCode;
            ErrorMessage = errorMessage ?? "";
        }

        public bool IsSuccess { get; }
        public string Text { get; }
        public int? StatusCode { get; }
        public string ErrorMessage { get; }

        public static GeminiTranslateApiResult Succeeded(string text)
        {
            return new GeminiTranslateApiResult(true, text, null, "");
        }

        public static GeminiTranslateApiResult Failed(int? statusCode, string errorMessage)
        {
            return new GeminiTranslateApiResult(false, "", statusCode, errorMessage);
        }
    }
}
