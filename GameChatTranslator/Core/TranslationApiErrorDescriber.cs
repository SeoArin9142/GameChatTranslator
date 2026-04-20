using System;
using System.Text.Json;

namespace GameTranslator
{
    /// <summary>
    /// Google/Gemini API 실패 정보를 사용자에게 보여줄 수 있는 안내 문구로 변환합니다.
    /// HTTP 호출은 수행하지 않고 상태 코드, 예외 메시지, API 오류 JSON만 해석하므로 단위 테스트가 가능합니다.
    /// </summary>
    public sealed class TranslationApiErrorDescriber
    {
        private const int MaxDetailLength = 220;

        /// <summary>
        /// Google 번역 호출이 최종 실패했을 때 로그에 남길 안내 문구를 생성합니다.
        /// <paramref name="exception"/>은 마지막 재시도에서 발생한 예외이며, null이면 일반 실패 안내만 반환합니다.
        /// </summary>
        public string DescribeGoogleFailure(Exception exception)
        {
            string detail = NormalizeDetail(exception?.Message);
            return AppendDetail(
                "Google 번역 실패: 네트워크 연결, Google 번역 서비스 응답, 방화벽/보안 프로그램 차단 여부를 확인해 주세요.",
                detail);
        }

        /// <summary>
        /// 번역창 상단에 잠시 표시할 짧은 Google 실패 안내입니다.
        /// 상세 오류는 DescribeGoogleFailure 결과로 로그창에 남깁니다.
        /// </summary>
        public string DescribeShortGoogleFailure(Exception exception)
        {
            return "Google 번역 실패: 로그창 확인";
        }

        /// <summary>
        /// Gemini 모델 목록 조회 실패를 API 키/권한/할당량/모델 문제처럼 사용자가 확인할 수 있는 문구로 변환합니다.
        /// <paramref name="result"/>는 TranslationApiClient가 반환한 모델 목록 조회 결과입니다.
        /// </summary>
        public string DescribeGeminiModelListFailure(GeminiModelListApiResult result)
        {
            if (result == null)
            {
                return "Gemini 모델 목록 확인 실패: 응답 정보가 없습니다. API 키와 네트워크 상태를 확인해 주세요.";
            }

            string action = DescribeGeminiHttpStatus(result.StatusCode, "모델 목록");
            string detail = ExtractApiErrorDetail(result.ErrorMessage);
            return AppendDetail($"Gemini 모델 목록 확인 실패: {action}", detail);
        }

        /// <summary>
        /// Gemini 모델 목록 조회 중 HttpClient 예외가 발생했을 때 사용할 안내 문구를 생성합니다.
        /// <paramref name="exception"/>은 네트워크 오류, 타임아웃, DNS 실패 등에서 발생한 예외입니다.
        /// </summary>
        public string DescribeGeminiModelListException(Exception exception)
        {
            string detail = NormalizeDetail(exception?.Message);
            return AppendDetail(
                "Gemini 모델 목록 확인 실패: 모델 목록 조회 중 네트워크 오류가 발생했습니다. 인터넷 연결, 방화벽, Google API 접속 가능 여부를 확인해 주세요.",
                detail);
        }

        /// <summary>
        /// Gemini 번역 호출 실패를 모델명과 HTTP 상태 코드 기준으로 설명합니다.
        /// <paramref name="result"/>는 TranslationApiClient가 반환한 Gemini 번역 결과,
        /// <paramref name="modelName"/>은 호출에 사용한 모델명입니다.
        /// </summary>
        public string DescribeGeminiTranslateFailure(GeminiTranslateApiResult result, string modelName)
        {
            string displayModel = NormalizeModelName(modelName);
            if (result == null)
            {
                return $"Gemini 번역 실패: 모델({displayModel}) 응답 정보가 없습니다. API 키, 모델명, 네트워크 상태를 확인해 주세요.";
            }

            string action = DescribeGeminiHttpStatus(result.StatusCode, "번역");
            string detail = ExtractApiErrorDetail(result.ErrorMessage);
            return AppendDetail($"Gemini 번역 실패: 모델({displayModel}) {action}", detail);
        }

        /// <summary>
        /// 번역창 상단에 잠시 표시할 짧은 Gemini 번역 실패 안내입니다.
        /// HTTP 상태 코드만 보고 사용자가 바로 확인할 항목을 짧게 알려줍니다.
        /// </summary>
        public string DescribeShortGeminiTranslateFailure(GeminiTranslateApiResult result)
        {
            return result?.StatusCode switch
            {
                400 => "Gemini 요청 오류: 모델/설정 확인",
                401 => "Gemini 인증 실패: API Key 확인",
                403 => "Gemini 권한 오류: API 설정 확인",
                404 => "Gemini 모델 오류: GeminiModel 확인",
                429 => "Gemini 할당량 초과: 사용량 확인",
                500 or 502 or 503 or 504 => "Gemini 서버 오류: 잠시 후 재시도",
                _ => "Gemini 번역 실패: Google 전환"
            };
        }

        /// <summary>
        /// Gemini 호출은 성공했지만 번역문을 파싱하지 못했을 때 사용할 안내 문구를 생성합니다.
        /// <paramref name="modelName"/>은 호출에 사용한 모델명입니다.
        /// </summary>
        public string DescribeGeminiEmptyResponse(string modelName)
        {
            return $"Gemini 번역 실패: 모델({NormalizeModelName(modelName)}) 응답에서 번역문을 찾지 못했습니다. 모델명을 확인하거나 Google 번역으로 전환됩니다.";
        }

        /// <summary>
        /// Gemini 응답은 왔지만 번역문이 비어 있을 때 번역창에 표시할 짧은 안내입니다.
        /// </summary>
        public string DescribeShortGeminiEmptyResponse()
        {
            return "Gemini 응답 오류: Google 전환";
        }

        /// <summary>
        /// Gemini 호출 중 HttpClient 예외가 발생했을 때 사용할 안내 문구를 생성합니다.
        /// <paramref name="exception"/>은 네트워크 오류, 타임아웃, DNS 실패 등에서 발생한 예외입니다.
        /// <paramref name="modelName"/>은 호출에 사용한 모델명입니다.
        /// </summary>
        public string DescribeGeminiException(Exception exception, string modelName)
        {
            string detail = NormalizeDetail(exception?.Message);
            return AppendDetail(
                $"Gemini 번역 실패: 모델({NormalizeModelName(modelName)}) 호출 중 네트워크 오류가 발생했습니다. 인터넷 연결, 방화벽, Google API 접속 가능 여부를 확인해 주세요.",
                detail);
        }

        /// <summary>
        /// Gemini 호출 중 예외가 발생했을 때 번역창에 표시할 짧은 안내입니다.
        /// </summary>
        public string DescribeShortGeminiException(Exception exception)
        {
            return "Gemini 네트워크 오류: Google 전환";
        }

        /// <summary>
        /// Local LLM 번역 호출 실패를 endpoint/model/status 기준으로 설명합니다.
        /// <paramref name="result"/>는 LM Studio/OpenAI 호환 엔드포인트 호출 결과,
        /// <paramref name="endpoint"/>는 사용자가 설정한 chat completions 주소,
        /// <paramref name="modelName"/>은 호출에 사용한 로컬 모델 ID입니다.
        /// </summary>
        public string DescribeLocalLlmTranslateFailure(LocalLlmTranslateApiResult result, string endpoint, string modelName)
        {
            if (result == null)
            {
                return $"Local LLM 번역 실패: 모델({NormalizeModelName(modelName)}) 응답 정보가 없습니다. LM Studio 서버와 endpoint를 확인해 주세요.";
            }

            string action = DescribeLocalLlmHttpStatus(result.StatusCode);
            string detail = ExtractApiErrorDetail(result.ErrorMessage);
            return AppendDetail(
                $"Local LLM 번역 실패: 모델({NormalizeModelName(modelName)}), endpoint({NormalizeEndpoint(endpoint)}) {action}",
                detail);
        }

        /// <summary>
        /// 번역창 상단에 잠시 표시할 짧은 Local LLM 실패 안내입니다.
        /// </summary>
        public string DescribeShortLocalLlmTranslateFailure(LocalLlmTranslateApiResult result)
        {
            return result?.StatusCode switch
            {
                400 => "Local LLM 요청 오류: 설정 확인",
                404 => "Local LLM endpoint/model 확인",
                408 => "Local LLM 응답 지연: Google 전환",
                429 => "Local LLM 처리 제한: Google 전환",
                500 or 502 or 503 or 504 => "Local LLM 서버 오류: Google 전환",
                _ => "Local LLM 실패: Google 전환"
            };
        }

        /// <summary>
        /// Local LLM 호출은 성공했지만 번역문을 파싱하지 못했을 때 사용할 안내 문구를 생성합니다.
        /// </summary>
        public string DescribeLocalLlmEmptyResponse(string endpoint, string modelName)
        {
            return $"Local LLM 번역 실패: 모델({NormalizeModelName(modelName)}) 응답에서 번역문을 찾지 못했습니다. endpoint({NormalizeEndpoint(endpoint)})와 모델 출력 형식을 확인해 주세요.";
        }

        /// <summary>
        /// Local LLM 응답이 비어 있을 때 번역창에 표시할 짧은 안내입니다.
        /// </summary>
        public string DescribeShortLocalLlmEmptyResponse()
        {
            return "Local LLM 응답 오류: Google 전환";
        }

        /// <summary>
        /// Local LLM 호출 중 예외가 발생했을 때 사용할 안내 문구를 생성합니다.
        /// </summary>
        public string DescribeLocalLlmException(Exception exception, string endpoint, string modelName)
        {
            string detail = NormalizeDetail(exception?.Message);
            return AppendDetail(
                $"Local LLM 번역 실패: 모델({NormalizeModelName(modelName)}) 호출 중 오류가 발생했습니다. LM Studio Local Server가 켜져 있는지, endpoint({NormalizeEndpoint(endpoint)})가 맞는지 확인해 주세요.",
                detail);
        }

        /// <summary>
        /// Local LLM 호출 중 예외가 발생했을 때 번역창에 표시할 짧은 안내입니다.
        /// </summary>
        public string DescribeShortLocalLlmException(Exception exception)
        {
            return "Local LLM 연결 실패: Google 전환";
        }

        /// <summary>
        /// API 오류 JSON에서 사람이 읽을 수 있는 핵심 오류 메시지만 추출합니다.
        /// Google API의 {"error":{"message":"...","status":"..."}} 형태와 단순 문자열 오류를 모두 처리합니다.
        /// </summary>
        public string ExtractApiErrorDetail(string rawError)
        {
            string value = NormalizeDetail(rawError);
            if (string.IsNullOrWhiteSpace(value)) return "";

            try
            {
                using JsonDocument document = JsonDocument.Parse(value);
                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("error", out JsonElement errorElement))
                {
                    if (errorElement.ValueKind == JsonValueKind.String)
                    {
                        return NormalizeDetail(errorElement.GetString());
                    }

                    if (errorElement.ValueKind == JsonValueKind.Object)
                    {
                        string message = ReadStringProperty(errorElement, "message");
                        string status = ReadStringProperty(errorElement, "status");
                        string code = ReadNumberProperty(errorElement, "code");
                        return JoinDetails(code, status, message);
                    }
                }

                return Truncate(value);
            }
            catch (JsonException)
            {
                return Truncate(value);
            }
        }

        private static string DescribeGeminiHttpStatus(int? statusCode, string actionName)
        {
            return statusCode switch
            {
                400 => $"{actionName} 요청이 거부됐습니다. Gemini 모델명과 요청 형식을 확인해 주세요.",
                401 => $"{actionName} 인증에 실패했습니다. Gemini API 키가 올바른지 확인해 주세요.",
                403 => $"{actionName} 권한이 없습니다. API 키 권한, 프로젝트 사용 설정, 결제/지역 제한을 확인해 주세요.",
                404 => $"{actionName} 대상 모델을 찾지 못했습니다. GeminiModel 설정값을 확인해 주세요.",
                429 => $"{actionName} 할당량 또는 호출 제한에 걸렸습니다. 잠시 후 다시 시도하거나 API 사용량을 확인해 주세요.",
                500 or 502 or 503 or 504 => $"{actionName} 서버 오류가 발생했습니다. Gemini 서버가 혼잡하거나 일시적으로 응답하지 않습니다. 잠시 후 다시 시도하거나 Google 번역을 사용해 주세요.",
                null => $"{actionName} 실패 원인을 특정하지 못했습니다. API 키, 모델명, 네트워크 상태를 확인해 주세요.",
                _ => $"{actionName} 실패가 발생했습니다. HTTP {statusCode.Value} 응답 내용을 확인해 주세요."
            };
        }

        private static string DescribeLocalLlmHttpStatus(int? statusCode)
        {
            return statusCode switch
            {
                400 => "요청 형식이 거부됐습니다. endpoint, model, max tokens 설정을 확인해 주세요.",
                404 => "endpoint 또는 모델을 찾지 못했습니다. LM Studio 서버 주소와 모델 ID를 확인해 주세요.",
                408 => "응답 시간이 초과됐습니다. timeout 값을 늘리거나 더 가벼운 모델을 사용해 주세요.",
                429 => "로컬 서버가 현재 요청을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.",
                500 or 502 or 503 or 504 => "로컬 서버 오류가 발생했습니다. LM Studio 모델 로드 상태를 확인해 주세요.",
                null => "실패 원인을 특정하지 못했습니다. LM Studio 서버와 모델 설정을 확인해 주세요.",
                _ => $"실패가 발생했습니다. HTTP {statusCode.Value} 응답 내용을 확인해 주세요."
            };
        }

        private static string AppendDetail(string message, string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return message;
            return $"{message} 상세: {detail}";
        }

        private static string NormalizeModelName(string modelName)
        {
            return string.IsNullOrWhiteSpace(modelName) ? "(비어 있음)" : modelName.Trim();
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            return string.IsNullOrWhiteSpace(endpoint) ? "(비어 있음)" : endpoint.Trim();
        }

        private static string NormalizeDetail(string detail)
        {
            return (detail ?? "").Trim();
        }

        private static string ReadStringProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) return "";
            if (property.ValueKind != JsonValueKind.String) return "";
            return property.GetString() ?? "";
        }

        private static string ReadNumberProperty(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement property)) return "";
            if (property.ValueKind != JsonValueKind.Number) return "";
            return property.GetRawText();
        }

        private static string JoinDetails(params string[] parts)
        {
            return Truncate(string.Join(" / ", Array.FindAll(parts, part => !string.IsNullOrWhiteSpace(part))));
        }

        private static string Truncate(string value)
        {
            string normalized = NormalizeDetail(value);
            if (normalized.Length <= MaxDetailLength) return normalized;
            return normalized.Substring(0, MaxDetailLength) + "...";
        }
    }
}
