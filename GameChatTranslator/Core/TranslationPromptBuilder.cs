using System;
using System.Text.RegularExpressions;

namespace GameTranslator
{
    /// <summary>
    /// 번역 API 호출 전에 필요한 문자열 정리, 언어 코드 변환, Gemini 프롬프트 생성을 담당하는 순수 서비스입니다.
    /// HTTP 호출이나 UI 상태에는 의존하지 않아 단위 테스트에서 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class TranslationPromptBuilder
    {
        private const string TranslatableLetterPattern = @"[a-zA-Z가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]";
        private const string SingleCharacterAllowPattern = @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$";

        /// <summary>
        /// Google Translate API에 보내기 전 OCR 노이즈와 불필요한 기호를 제거합니다.
        /// <paramref name="text"/>는 OCR 후처리 결과 또는 채팅 본문입니다.
        /// 반환값은 API 요청에 사용할 정리된 문자열이며, 입력이 비어 있으면 빈 문자열입니다.
        /// </summary>
        public string CleanGoogleTranslateInput(string text)
        {
            string cleaned = text ?? "";
            cleaned = Regex.Replace(cleaned, @"\d{1,2}:\d{2}", "");
            cleaned = Regex.Replace(cleaned, @"[\[\]\(\)\{\}\<\>]", " ");
            cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\.,!\?\-]", "");
            cleaned = Regex.Replace(cleaned, @"([\-\=\.\/_])\1+", "$1");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            return cleaned;
        }

        /// <summary>
        /// 정리된 문자열이 번역 API를 호출할 만한 실제 문자인지 판단합니다.
        /// 숫자/기호만 있으면 false이며, 한 글자 채팅은 한중일 문자일 때만 허용합니다.
        /// </summary>
        public bool HasTranslatableContent(string cleanedText)
        {
            string text = cleanedText ?? "";
            if (text.Length == 1)
            {
                return Regex.IsMatch(text, SingleCharacterAllowPattern);
            }

            return text.Length >= 2 && Regex.IsMatch(text, TranslatableLetterPattern);
        }

        /// <summary>
        /// 앱 내부 언어 코드를 Google Translate API의 tl 파라미터 언어 코드로 변환합니다.
        /// <paramref name="targetLanguageCode"/>는 ko, en-US, zh-Hans-CN 같은 내부 언어 코드입니다.
        /// </summary>
        public string GetGoogleTranslateLanguageCode(string targetLanguageCode)
        {
            if (targetLanguageCode == "zh-Hans-CN") return "zh-CN";
            if (targetLanguageCode == "en-US") return "en";
            return targetLanguageCode;
        }

        /// <summary>
        /// Google Translate 비공식 엔드포인트 호출 URL을 생성합니다.
        /// <paramref name="cleanedText"/>는 CleanGoogleTranslateInput을 통과한 문자열,
        /// <paramref name="targetLanguageCode"/>는 앱 내부 목표 언어 코드입니다.
        /// </summary>
        public string BuildGoogleTranslateUrl(string cleanedText, string targetLanguageCode)
        {
            string targetApiLang = GetGoogleTranslateLanguageCode(targetLanguageCode);
            return $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetApiLang}&dt=t&q={Uri.EscapeDataString(cleanedText ?? "")}";
        }

        /// <summary>
        /// Gemini 프롬프트에 표시할 목표 언어명을 반환합니다.
        /// 한국어/영어는 자연어 이름으로 바꾸고, 그 외 언어는 내부 코드를 그대로 사용합니다.
        /// </summary>
        public string GetGeminiTargetLanguageName(string targetLanguageCode)
        {
            if (targetLanguageCode == "ko") return "Korean";
            if (targetLanguageCode == "en-US") return "English";
            return targetLanguageCode;
        }

        /// <summary>
        /// OCR 오타가 섞인 게임 채팅을 Gemini가 자연스럽게 복원/번역하도록 요청하는 프롬프트를 생성합니다.
        /// <paramref name="text"/>는 번역할 채팅 본문,
        /// <paramref name="targetLanguageCode"/>는 앱 내부 목표 언어 코드입니다.
        /// </summary>
        public string BuildGeminiPrompt(string text, string targetLanguageCode)
        {
            string targetLanguage = GetGeminiTargetLanguageName(targetLanguageCode);
            return "You are an expert game translator. " +
                   "The input text is from OCR and has many typos (e.g., '伽' instead of '你', 'カ' instead of '为'). " +
                   "Your job: 1. Guess the original intended sentence by ignoring OCR noise. " +
                   "2. Translate it naturally into " + targetLanguage + ". " +
                   "3. If the text is just a name or nonsense, return an empty string. " +
                   "Output ONLY the translation: \n\n" + (text ?? "");
        }
    }
}
