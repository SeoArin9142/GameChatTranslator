using System.Collections.Generic;
using System.Linq;

namespace GameTranslator
{
    /// <summary>
    /// OCR 비교 하네스에서 번역 대상으로 넘길 라인을 추출합니다.
    /// "[캐릭터명]: 내용" 형식은 캐릭터 라벨과 본문을 분리하고,
    /// 일반 텍스트는 ETC 정리 규칙으로 정제합니다.
    /// </summary>
    public sealed class OcrTranslationHarnessService
    {
        private static readonly HashSet<string> SystemLabelCandidates = new HashSet<string>(new[]
        {
            "팀",
            "공지",
            "시스템"
        });

        private static readonly string[] UiKeywords =
        {
            "클릭",
            "채널 변경",
            "선택",
            "설정",
            "닫기",
            "확인"
        };

        private readonly TranslationPromptBuilder translationPromptBuilder;

        public OcrTranslationHarnessService(TranslationPromptBuilder translationPromptBuilder = null)
        {
            this.translationPromptBuilder = translationPromptBuilder ?? new TranslationPromptBuilder();
        }

        public IReadOnlyList<OcrTranslationHarnessRequest> BuildRequests(IEnumerable<string> mergedLines)
        {
            var requests = new List<OcrTranslationHarnessRequest>();

            foreach (string rawLine in mergedLines ?? Enumerable.Empty<string>())
            {
                string text = (rawLine ?? "").Trim();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                if (!ChatTextAnalyzer.ContainsReadableLetter(text))
                {
                    requests.Add(OcrTranslationHarnessRequest.Skip(text, "글자 없음"));
                    continue;
                }

                if (ChatTextAnalyzer.TryParseChatLine(text, out ChatTextAnalyzer.ChatLine chatLine) &&
                    !string.IsNullOrWhiteSpace(chatLine.Message))
                {
                    if (ShouldSkipSystemUiLine(chatLine))
                    {
                        requests.Add(OcrTranslationHarnessRequest.Skip(text, "시스템/UI 문구"));
                        continue;
                    }

                    requests.Add(OcrTranslationHarnessRequest.Chat(text, chatLine.CharacterLabel, chatLine.Message));
                    continue;
                }

                string cleaned = translationPromptBuilder.CleanEtcOcrLine(text);
                if (!translationPromptBuilder.HasMeaningfulEtcContent(cleaned))
                {
                    requests.Add(OcrTranslationHarnessRequest.Skip(text, "파싱 실패 / 노이즈"));
                    continue;
                }

                requests.Add(OcrTranslationHarnessRequest.Raw(text, "[RAW]: ", cleaned));
            }

            return requests;
        }

        private static bool ShouldSkipSystemUiLine(ChatTextAnalyzer.ChatLine chatLine)
        {
            if (chatLine == null)
            {
                return false;
            }

            if (!SystemLabelCandidates.Contains((chatLine.CharacterName ?? "").Trim()))
            {
                return false;
            }

            string message = chatLine.Message ?? "";
            return UiKeywords.Any(keyword => message.Contains(keyword));
        }
    }

    /// <summary>
    /// OCR 비교 하네스가 번역 API에 넘길 단일 요청입니다.
    /// </summary>
    public sealed class OcrTranslationHarnessRequest
    {
        private OcrTranslationHarnessRequest(
            string rawText,
            string prefix,
            string contentToTranslate,
            TranslationContentMode contentMode,
            bool skipped,
            string skipReason)
        {
            RawText = rawText ?? "";
            Prefix = prefix ?? "";
            ContentToTranslate = contentToTranslate ?? "";
            ContentMode = contentMode;
            Skipped = skipped;
            SkipReason = skipReason ?? "";
        }

        public string RawText { get; }
        public string Prefix { get; }
        public string ContentToTranslate { get; }
        public TranslationContentMode ContentMode { get; }
        public bool Skipped { get; }
        public string SkipReason { get; }

        public static OcrTranslationHarnessRequest Chat(string rawText, string prefix, string contentToTranslate)
        {
            return new OcrTranslationHarnessRequest(rawText, prefix, contentToTranslate, TranslationContentMode.Strinova, false, "");
        }

        public static OcrTranslationHarnessRequest Raw(string rawText, string prefix, string contentToTranslate)
        {
            return new OcrTranslationHarnessRequest(rawText, prefix, contentToTranslate, TranslationContentMode.Etc, false, "");
        }

        public static OcrTranslationHarnessRequest Skip(string rawText, string skipReason)
        {
            return new OcrTranslationHarnessRequest(rawText, "", "", TranslationContentMode.Etc, true, skipReason);
        }
    }
}
