using System.Collections.Generic;

namespace GameTranslator
{
    /// <summary>
    /// OCR 진단 창의 번역 테스트 하네스 결과입니다.
    /// 선택된 OCR 후보 라인을 현재 번역 엔진으로 흘려보낸 결과를 요약합니다.
    /// </summary>
    public sealed class OcrTranslationHarnessPreviewResult
    {
        public string CandidateName { get; set; } = "";
        public long TranslateMs { get; set; }
        public int TotalLineCount { get; set; }
        public int TranslatedLineCount { get; set; }
        public int SkippedLineCount { get; set; }
        public List<OcrTranslationHarnessPreviewLine> Lines { get; } = new List<OcrTranslationHarnessPreviewLine>();
    }

    public sealed class OcrTranslationHarnessPreviewLine
    {
        public int Index { get; set; }
        public string RawText { get; set; } = "";
        public string Prefix { get; set; } = "";
        public string OriginalContent { get; set; } = "";
        public string PreparedContent { get; set; } = "";
        public string TranslatedText { get; set; } = "";
        public string EngineName { get; set; } = "";
        public string Status { get; set; } = "";
        public bool Translated { get; set; }
    }
}
