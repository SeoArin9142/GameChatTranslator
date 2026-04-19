using System;
using System.Text.Json;

namespace GameTranslator
{
    /// <summary>
    /// 번역 API 응답 JSON에서 최종 번역 문자열만 추출하는 순수 파서입니다.
    /// 네트워크 호출과 재시도 판단은 호출자가 담당하고, 이 클래스는 JSON 구조 해석만 수행합니다.
    /// </summary>
    public sealed class TranslationResultParser
    {
        /// <summary>
        /// Google Translate 비공식 엔드포인트 응답에서 번역 조각을 순서대로 이어 붙입니다.
        /// 파싱할 수 없는 응답이면 빈 문자열을 반환합니다.
        /// </summary>
        public string ParseGoogleTranslateResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) return "";

                JsonElement sentenceArray = root[0];
                if (sentenceArray.ValueKind != JsonValueKind.Array) return "";

                string result = "";
                foreach (JsonElement item in sentenceArray.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() == 0) continue;
                    if (item[0].ValueKind == JsonValueKind.String)
                    {
                        result += item[0].GetString();
                    }
                }

                return result.Trim();
            }
            catch
            {
                return "";
            }
        }

        /// <summary>
        /// Gemini generateContent 응답에서 첫 번째 후보의 첫 번째 text part를 추출합니다.
        /// 후보가 없거나 JSON 구조가 예상과 다르면 빈 문자열을 반환합니다.
        /// </summary>
        public string ParseGeminiTranslateResponse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "";

            try
            {
                using JsonDocument document = JsonDocument.Parse(json);
                JsonElement root = document.RootElement;
                JsonElement parts = root
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts");

                if (parts.ValueKind != JsonValueKind.Array || parts.GetArrayLength() == 0) return "";
                if (!parts[0].TryGetProperty("text", out JsonElement textElement)) return "";
                if (textElement.ValueKind != JsonValueKind.String) return "";

                return textElement.GetString()?.Trim() ?? "";
            }
            catch
            {
                return "";
            }
        }
    }
}
