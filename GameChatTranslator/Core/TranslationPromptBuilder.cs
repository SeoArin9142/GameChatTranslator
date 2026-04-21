using System;
using System.Linq;
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
        private const string NonAsciiReadablePattern = @"[가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]";
        private const string SingleCharacterAllowPattern = @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$";
        private const string EastAsianNoSpacePattern = @"(?<=[ぁ-んァ-ヶ一-龥])\s+(?=[ぁ-んァ-ヶ一-龥])";

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
            cleaned = Regex.Replace(cleaned, EastAsianNoSpacePattern, "");
            return cleaned;
        }

        /// <summary>
        /// ETC 모드에서 OCR 한 줄을 번역기에 넘기기 전 정리합니다.
        /// 동아시아 문자와 ASCII/숫자가 섞인 토큰은 ASCII/숫자 노이즈를 제거하고,
        /// 순수 영어 문장은 가능한 한 유지합니다.
        /// 의미 있는 문자 구성이 남지 않으면 빈 문자열을 반환합니다.
        /// </summary>
        public string CleanEtcOcrLine(string text)
        {
            string source = text ?? "";
            string cleaned = source;
            cleaned = Regex.Replace(cleaned, @"\d{1,2}:\d{2}", " ");
            cleaned = Regex.Replace(cleaned, @"[\[\]\(\)\{\}\<\>]", " ");
            cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\.,!\?\-]", " ");
            cleaned = Regex.Replace(cleaned, @"([\-\=\.\/_])\1+", "$1");

            bool containsNonAsciiReadable = Regex.IsMatch(cleaned, NonAsciiReadablePattern);

            string[] normalizedTokens = Regex
                .Split(cleaned, @"\s+")
                .Select(token => NormalizeEtcToken(token, containsNonAsciiReadable))
                .Where(token => !string.IsNullOrWhiteSpace(token))
                .ToArray();

            cleaned = string.Join(" ", normalizedTokens);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
            cleaned = Regex.Replace(cleaned, EastAsianNoSpacePattern, "");

            if (ShouldDiscardEtcLineAsTooNoisy(source))
            {
                return "";
            }

            return HasMeaningfulEtcContent(cleaned) ? cleaned : "";
        }

        /// <summary>
        /// ETC 정리 후 남은 문자열이 실제 번역 대상으로 볼 만한지 확인합니다.
        /// 동아시아/키릴 문자가 섞인 줄은 한 글자만 남으면 노이즈로 보고 제외합니다.
        /// </summary>
        public bool HasMeaningfulEtcContent(string cleanedText)
        {
            string text = (cleanedText ?? "").Trim();
            if (!HasTranslatableContent(text))
            {
                return false;
            }

            if (Regex.IsMatch(text, NonAsciiReadablePattern))
            {
                return Regex.Matches(text, TranslatableLetterPattern).Count >= 2;
            }

            return true;
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
        /// 앱 내부 게임 언어 코드를 Google Translate API의 sl 파라미터 언어 코드로 변환합니다.
        /// <paramref name="sourceLanguageCode"/>는 OCR에 사용한 게임 원문 언어 코드입니다.
        /// 알 수 없는 값은 Google 자동 감지로 되돌려 잘못된 sl 값 전송을 피합니다.
        /// </summary>
        public string GetGoogleTranslateSourceLanguageCode(string sourceLanguageCode)
        {
            string normalized = GetGoogleTranslateLanguageCode(sourceLanguageCode ?? "");
            return normalized switch
            {
                "ko" => "ko",
                "en" => "en",
                "ja" => "ja",
                "ru" => "ru",
                "zh-CN" => "zh-CN",
                _ => "auto"
            };
        }

        /// <summary>
        /// Google Translate 비공식 엔드포인트 호출 URL을 생성합니다.
        /// <paramref name="cleanedText"/>는 CleanGoogleTranslateInput을 통과한 문자열,
        /// <paramref name="sourceLanguageCode"/>는 앱 내부 게임 원문 언어 코드,
        /// <paramref name="targetLanguageCode"/>는 앱 내부 목표 언어 코드입니다.
        /// </summary>
        public string BuildGoogleTranslateUrl(string cleanedText, string sourceLanguageCode, string targetLanguageCode)
        {
            string sourceApiLang = GetGoogleTranslateSourceLanguageCode(sourceLanguageCode);
            string targetApiLang = GetGoogleTranslateLanguageCode(targetLanguageCode);
            return $"https://translate.googleapis.com/translate_a/single?client=gtx&sl={sourceApiLang}&tl={targetApiLang}&dt=t&q={Uri.EscapeDataString(cleanedText ?? "")}";
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

        /// <summary>
        /// LM Studio/OpenAI 호환 로컬 LLM에 전달할 system 프롬프트를 생성합니다.
        /// Qwen 계열 reasoning 모델이 내부 추론 토큰을 소모하지 않도록 /no_think를 포함합니다.
        /// </summary>
        public string BuildLocalLlmSystemPrompt(string targetLanguageCode)
        {
            string targetLanguage = GetGeminiTargetLanguageName(targetLanguageCode);
            return "/no_think You are a game chat translator. " +
                   "The input text may contain OCR mistakes from a game chat overlay. " +
                   "Restore the intended sentence and translate it naturally into " + targetLanguage + ". " +
                   "Return only the translated sentence. Do not explain. Do not think step by step.";
        }

        /// <summary>
        /// 로컬 LLM에 전달할 user 프롬프트를 생성합니다.
        /// Google 입력 정리와 같은 OCR 노이즈 정리를 적용해 한중일 문자 사이 불필요한 공백을 줄입니다.
        /// </summary>
        public string BuildLocalLlmUserPrompt(string text)
        {
            string cleaned = CleanGoogleTranslateInput(text);
            return "/no_think Input: " + cleaned;
        }

        private string NormalizeEtcToken(string token, bool containsNonAsciiReadable)
        {
            string normalized = (token ?? "").Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            if (containsNonAsciiReadable &&
                Regex.IsMatch(normalized, NonAsciiReadablePattern) &&
                Regex.IsMatch(normalized, @"[a-zA-Z0-9]"))
            {
                normalized = Regex.Replace(normalized, @"[a-zA-Z0-9]", "");
            }

            normalized = Regex.Replace(normalized, @"^[\.\,\!\?\-]+|[\.\,\!\?\-]+$", "");

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return "";
            }

            if (containsNonAsciiReadable)
            {
                if (Regex.IsMatch(normalized, @"^[a-zA-Z]$")) return "";
                if (Regex.IsMatch(normalized, @"^\d$")) return "";
            }

            if (!Regex.IsMatch(normalized, TranslatableLetterPattern) &&
                !Regex.IsMatch(normalized, @"^\d{2,}$"))
            {
                return "";
            }

            return normalized;
        }

        private bool ShouldDiscardEtcLineAsTooNoisy(string source)
        {
            string text = source ?? "";
            int nonAsciiReadableCount = Regex.Matches(text, NonAsciiReadablePattern).Count;
            if (nonAsciiReadableCount == 0)
            {
                return false;
            }

            int asciiLetterCount = Regex.Matches(text, @"[a-zA-Z]").Count;
            int noiseCount = Regex.Matches(text, @"[^a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\.,!\?\-]").Count;
            return asciiLetterCount > nonAsciiReadableCount && noiseCount >= 3;
        }
    }
}
