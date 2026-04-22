using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace GameTranslator
{
    public partial class MainWindow
    {
        /// <summary>
        /// OCR 진단 창에서 선택한 병합 라인 목록을 현재 번역 엔진으로 시험 번역합니다.
        /// "[캐릭터명]: 내용" 형식은 캐릭터 라벨을 유지하고 내용만 번역하며,
        /// 일반 텍스트는 RAW 프리픽스로 ETC 규칙으로 번역합니다.
        /// </summary>
        public async Task<OcrTranslationHarnessPreviewResult> RunOcrTranslationHarnessAsync(string candidateName, IEnumerable<string> mergedLines)
        {
            var result = new OcrTranslationHarnessPreviewResult
            {
                CandidateName = candidateName ?? ""
            };

            IReadOnlyList<OcrTranslationHarnessRequest> requests = ocrTranslationHarnessService.BuildRequests(mergedLines, characterNames);
            result.TotalLineCount = requests.Count;

            var performanceStats = new OcrPerformanceStats();
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < requests.Count; i++)
            {
                OcrTranslationHarnessRequest request = requests[i];
                var previewLine = new OcrTranslationHarnessPreviewLine
                {
                    Index = i + 1,
                    RawText = request.RawText,
                    Prefix = request.Prefix,
                    OriginalContent = request.ContentToTranslate
                };

                if (request.Skipped)
                {
                    previewLine.Status = request.SkipReason;
                    result.SkippedLineCount++;
                    result.Lines.Add(previewLine);
                    continue;
                }

                string preparedContent = PrepareFinalTranslationContent(request.ContentToTranslate);
                previewLine.PreparedContent = preparedContent;

                if (!ShouldTranslateFinalContent(preparedContent))
                {
                    previewLine.Status = "번역 조건 미충족";
                    result.SkippedLineCount++;
                    result.Lines.Add(previewLine);
                    continue;
                }

                TranslationDecisionResult translationResult = await TranslateFinalContentAsync(preparedContent, performanceStats, request.ContentMode);
                previewLine.TranslatedText = translationResult.TranslatedText;
                previewLine.EngineName = translationResult.EngineName;
                previewLine.Status = "OK";
                previewLine.Translated = true;
                result.TranslatedLineCount++;
                result.Lines.Add(previewLine);
            }

            stopwatch.Stop();
            result.TranslateMs = stopwatch.ElapsedMilliseconds;
            return result;
        }
    }
}
