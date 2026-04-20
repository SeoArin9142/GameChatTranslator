using System.Text.RegularExpressions;

namespace GameTranslator
{
    /// <summary>
    /// 번역 엔진 선택, 동일 언어 스킵, Gemini 실패 시 Google fallback 같은 번역 의사결정을 담당하는 순수 서비스입니다.
    /// 실제 HTTP 호출과 UI 출력은 호출자가 담당하고, 이 클래스는 어떤 경로를 사용할지와 결과 표기만 결정합니다.
    /// </summary>
    public sealed class TranslationService
    {
        /// <summary>
        /// 번역할 문장과 현재 엔진 상태를 바탕으로 첫 실행 경로를 결정합니다.
        /// <paramref name="text"/>는 최종 OCR 후처리 문장,
        /// <paramref name="targetLanguageCode"/>는 ko, en-US 같은 목표 언어 코드,
        /// <paramref name="engineMode"/>는 사용자가 선택한 번역 엔진,
        /// <paramref name="canUseGemini"/>는 Gemini 엔진이 켜져 있고 API 키도 있는 상태인지 여부,
        /// <paramref name="canUseLocalLlm"/>은 Local LLM 설정값이 호출 가능한 상태인지 여부입니다.
        /// </summary>
        public TranslationPlan CreatePlan(
            string text,
            string targetLanguageCode,
            TranslationEngineMode engineMode,
            bool canUseGemini,
            bool canUseLocalLlm)
        {
            string normalizedText = text ?? "";

            if (IsSymbolOnly(normalizedText))
            {
                return TranslationPlan.Immediate(
                    new TranslationDecisionResult(normalizedText, "Google", false, false));
            }

            if (IsSameLanguage(normalizedText, targetLanguageCode))
            {
                return TranslationPlan.Immediate(
                    new TranslationDecisionResult(normalizedText, "Skip", true, false));
            }

            TranslationRequestKind requestKind = engineMode switch
            {
                TranslationEngineMode.Gemini when canUseGemini => TranslationRequestKind.Gemini,
                TranslationEngineMode.LocalLlm when canUseLocalLlm => TranslationRequestKind.LocalLlm,
                _ => TranslationRequestKind.Google
            };

            return new TranslationPlan(requestKind, null);
        }

        /// <summary>
        /// 구버전 호출부 호환용 래퍼입니다. Gemini 사용 여부만 받으면 기존 Google/Gemini 2단계 정책으로 동작합니다.
        /// </summary>
        public TranslationPlan CreatePlan(string text, string targetLanguageCode, bool canUseGemini)
        {
            return CreatePlan(
                text,
                targetLanguageCode,
                canUseGemini ? TranslationEngineMode.Gemini : TranslationEngineMode.Google,
                canUseGemini,
                false);
        }

        /// <summary>
        /// Gemini 결과가 비어 있어 Google fallback이 필요한지 판단합니다.
        /// </summary>
        public bool ShouldFallbackToGoogle(string geminiResult)
        {
            return string.IsNullOrEmpty(geminiResult);
        }

        /// <summary>
        /// Gemini API 호출 결과를 최종 번역 결과 또는 Google fallback 요청으로 해석합니다.
        /// <paramref name="geminiResult"/>가 비어 있으면 호출자가 Google을 한 번 더 호출해야 하며,
        /// 값이 있으면 Gemini 최종 결과로 확정합니다.
        /// </summary>
        public TranslationAttemptResolution ResolveGeminiAttempt(string geminiResult, string modelName)
        {
            if (ShouldFallbackToGoogle(geminiResult))
            {
                return TranslationAttemptResolution.RequestGoogleFallback();
            }

            return TranslationAttemptResolution.Final(CreateGeminiResult(geminiResult, modelName));
        }

        /// <summary>
        /// Local LLM 호출 결과를 최종 번역 결과 또는 Google fallback 요청으로 해석합니다.
        /// 로컬 서버가 꺼져 있거나 응답이 비어 있으면 Google fallback을 요청합니다.
        /// </summary>
        public TranslationAttemptResolution ResolveLocalLlmAttempt(string localLlmResult, string modelName)
        {
            if (ShouldFallbackToGoogle(localLlmResult))
            {
                return TranslationAttemptResolution.RequestGoogleFallback();
            }

            return TranslationAttemptResolution.Final(CreateLocalLlmResult(localLlmResult, modelName));
        }

        /// <summary>
        /// Google API 호출 결과를 최종 번역 결과로 해석합니다.
        /// <paramref name="isFallback"/>이 true이면 Gemini 실패 후 Google로 전환된 결과임을 출력문과 엔진명에 반영합니다.
        /// </summary>
        public TranslationAttemptResolution ResolveGoogleAttempt(string googleResult, bool isFallback)
        {
            return ResolveGoogleAttempt(googleResult, isFallback, "Gemini");
        }

        /// <summary>
        /// Google API 호출 결과를 최종 번역 결과로 해석합니다.
        /// <paramref name="fallbackSourceEngine"/>은 fallback이 발생한 원래 엔진명입니다. 예: Gemini, Local LLM.
        /// </summary>
        public TranslationAttemptResolution ResolveGoogleAttempt(string googleResult, bool isFallback, string fallbackSourceEngine)
        {
            return TranslationAttemptResolution.Final(CreateGoogleResult(googleResult, isFallback, fallbackSourceEngine));
        }

        /// <summary>
        /// 성공한 Gemini 번역 결과를 UI/로그에 사용할 공통 결과 모델로 변환합니다.
        /// <paramref name="geminiResult"/>는 Gemini 응답에서 추출한 번역문,
        /// <paramref name="modelName"/>은 사용한 Gemini 모델명입니다.
        /// </summary>
        public TranslationDecisionResult CreateGeminiResult(string geminiResult, string modelName)
        {
            return new TranslationDecisionResult(geminiResult ?? "", $"Gemini {modelName}", false, false);
        }

        /// <summary>
        /// 성공한 Local LLM 번역 결과를 UI/로그에 사용할 공통 결과 모델로 변환합니다.
        /// <paramref name="localLlmResult"/>는 로컬 OpenAI 호환 엔드포인트 응답에서 추출한 번역문,
        /// <paramref name="modelName"/>은 LM Studio에서 로드된 모델명입니다.
        /// </summary>
        public TranslationDecisionResult CreateLocalLlmResult(string localLlmResult, string modelName)
        {
            return new TranslationDecisionResult(localLlmResult ?? "", $"Local LLM {modelName}", false, false);
        }

        /// <summary>
        /// Google 번역 결과를 UI/로그에 사용할 공통 결과 모델로 변환합니다.
        /// <paramref name="googleResult"/>는 Google 응답에서 추출한 번역문,
        /// <paramref name="isFallback"/>은 Gemini 실패 후 fallback으로 호출한 결과인지 여부입니다.
        /// </summary>
        public TranslationDecisionResult CreateGoogleResult(string googleResult, bool isFallback)
        {
            return CreateGoogleResult(googleResult, isFallback, "Gemini");
        }

        /// <summary>
        /// Google 번역 결과를 UI/로그에 사용할 공통 결과 모델로 변환합니다.
        /// fallback이면 원래 실패한 엔진명을 접두어에 포함합니다.
        /// </summary>
        public TranslationDecisionResult CreateGoogleResult(string googleResult, bool isFallback, string fallbackSourceEngine)
        {
            string translatedText = googleResult ?? "";
            if (isFallback)
            {
                string source = string.IsNullOrWhiteSpace(fallbackSourceEngine) ? "이전 엔진" : fallbackSourceEngine.Trim();
                translatedText = $"[{source} 에러 - 구글 전환됨] " + translatedText;
            }

            return new TranslationDecisionResult(
                translatedText,
                isFallback ? "Google (Fallback)" : "Google",
                false,
                isFallback);
        }

        /// <summary>
        /// 번역 대상 문장이 이미 목표 언어인지 문자 범위 기준으로 판단합니다.
        /// true이면 API 호출 없이 원문을 그대로 표시합니다.
        /// </summary>
        public bool IsSameLanguage(string text, string targetLanguageCode)
        {
            string value = text ?? "";
            if (targetLanguageCode == "ko" && Regex.IsMatch(value, @"[가-힣]{2,}")) return true;
            if (targetLanguageCode == "ru" && Regex.IsMatch(value, @"[а-яА-ЯёЁ]")) return true;
            if (targetLanguageCode == "ja" && Regex.IsMatch(value, @"[ぁ-んァ-ヶ]")) return true;
            if (targetLanguageCode == "zh-Hans-CN" && Regex.IsMatch(value, @"[\u4e00-\u9fa5]")) return true;
            if (targetLanguageCode == "en-US" &&
                Regex.IsMatch(value, @"[a-zA-Z]") &&
                !Regex.IsMatch(value, @"[가-힣а-яА-ЯёЁぁ-んァ-ヶ\u4e00-\u9fa5]"))
            {
                return true;
            }

            return false;
        }

        private bool IsSymbolOnly(string text)
        {
            return Regex.IsMatch(text ?? "", @"^[0-9\W]+$");
        }
    }

    /// <summary>
    /// 이번 번역에서 첫 번째로 호출해야 하는 API 경로입니다.
    /// None은 API 호출 없이 ImmediateResult를 그대로 사용한다는 뜻입니다.
    /// </summary>
    public enum TranslationRequestKind
    {
        None,
        Google,
        Gemini,
        LocalLlm
    }

    /// <summary>
    /// 사용자가 선택한 번역 엔진입니다.
    /// Google은 무료 웹 번역, Gemini는 Google AI Studio API, LocalLlm은 LM Studio/OpenAI 호환 로컬 서버를 의미합니다.
    /// </summary>
    public enum TranslationEngineMode
    {
        Google,
        Gemini,
        LocalLlm
    }

    /// <summary>
    /// TranslationService가 결정한 번역 실행 계획입니다.
    /// ImmediateResult가 있으면 API 호출 없이 바로 표시할 수 있습니다.
    /// </summary>
    public sealed class TranslationPlan
    {
        public TranslationPlan(TranslationRequestKind requestKind, TranslationDecisionResult immediateResult)
        {
            RequestKind = requestKind;
            ImmediateResult = immediateResult;
        }

        public TranslationRequestKind RequestKind { get; }
        public TranslationDecisionResult ImmediateResult { get; }
        public bool HasImmediateResult => ImmediateResult != null;

        public static TranslationPlan Immediate(TranslationDecisionResult result)
        {
            return new TranslationPlan(TranslationRequestKind.None, result);
        }
    }

    /// <summary>
    /// 최종 번역 출력과 로그에 사용할 엔진 정보를 묶은 결과 모델입니다.
    /// </summary>
    public sealed class TranslationDecisionResult
    {
        public TranslationDecisionResult(string translatedText, string engineName, bool skipped, bool fallbackUsed)
        {
            TranslatedText = translatedText ?? "";
            EngineName = engineName ?? "";
            Skipped = skipped;
            FallbackUsed = fallbackUsed;
        }

        public string TranslatedText { get; }
        public string EngineName { get; }
        public bool Skipped { get; }
        public bool FallbackUsed { get; }
    }

    /// <summary>
    /// 한 번의 API 호출 결과를 해석한 상태입니다.
    /// FinalResult가 있으면 화면에 출력할 수 있고, NextRequestKind가 Google이면 Google fallback 호출이 필요합니다.
    /// </summary>
    public sealed class TranslationAttemptResolution
    {
        private TranslationAttemptResolution(TranslationDecisionResult finalResult, TranslationRequestKind nextRequestKind)
        {
            FinalResult = finalResult;
            NextRequestKind = nextRequestKind;
        }

        public TranslationDecisionResult FinalResult { get; }
        public TranslationRequestKind NextRequestKind { get; }
        public bool HasFinalResult => FinalResult != null;
        public bool RequiresGoogleFallback => !HasFinalResult && NextRequestKind == TranslationRequestKind.Google;

        public static TranslationAttemptResolution Final(TranslationDecisionResult result)
        {
            return new TranslationAttemptResolution(result, TranslationRequestKind.None);
        }

        public static TranslationAttemptResolution RequestGoogleFallback()
        {
            return new TranslationAttemptResolution(null, TranslationRequestKind.Google);
        }
    }
}
