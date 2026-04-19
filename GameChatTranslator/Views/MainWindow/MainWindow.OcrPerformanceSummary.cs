using System;
using System.Collections.Generic;
using System.Linq;

namespace GameTranslator
{
    public partial class MainWindow
    {
        private readonly Dictionary<string, OcrPerformanceAverage> ocrPerformanceAverages = new Dictionary<string, OcrPerformanceAverage>();
        private string latestOcrPerformanceSummary = "OCR 평균: 아직 번역 성능 기록 없음";

        /// <summary>
        /// OCR 처리 모드별 평균 성능을 계산하기 위한 누적 값입니다.
        /// Count는 번역 실행 횟수이고, 각 Total* 값은 밀리초 또는 OCR 호출 수 누적입니다.
        /// </summary>
        private class OcrPerformanceAverage
        {
            public int Count { get; set; }
            public long TotalElapsedMs { get; set; }
            public long TotalOcrMs { get; set; }
            public long TotalTranslateMs { get; set; }
            public int TotalOcrCalls { get; set; }
        }

        /// <summary>
        /// 한 번의 OCR 성능 로그를 모드별 평균 집계에 반영하고 로그창 요약 문구를 갱신합니다.
        /// <paramref name="stats"/>는 이번 번역 사이클의 OCR/번역 처리 시간,
        /// <paramref name="totalElapsedMs"/>는 전체 처리 시간입니다.
        /// </summary>
        private void TrackOcrPerformanceSummary(OcrPerformanceStats stats, long totalElapsedMs)
        {
            string modeLabel = GetOcrProcessingModeLabel(stats.ProcessingMode);
            if (!ocrPerformanceAverages.TryGetValue(modeLabel, out OcrPerformanceAverage average))
            {
                average = new OcrPerformanceAverage();
                ocrPerformanceAverages[modeLabel] = average;
            }

            average.Count++;
            average.TotalElapsedMs += totalElapsedMs;
            average.TotalOcrMs += stats.OcrMs;
            average.TotalTranslateMs += stats.TranslateMs;
            average.TotalOcrCalls += stats.OcrLanguageCallCount;

            latestOcrPerformanceSummary = BuildOcrPerformanceSummaryText();
            PushOcrPerformanceSummaryToLogViewer();
        }

        /// <summary>
        /// 로그창 상단에 표시할 OCR 모드별 평균 성능 요약 문자열을 만듭니다.
        /// </summary>
        private string BuildOcrPerformanceSummaryText()
        {
            if (ocrPerformanceAverages.Count == 0)
            {
                return "OCR 평균: 아직 번역 성능 기록 없음";
            }

            string[] modeOrder = { "빠름", "자동", "정확" };
            var parts = new List<string>();
            foreach (string mode in modeOrder)
            {
                if (!ocrPerformanceAverages.TryGetValue(mode, out OcrPerformanceAverage average)) continue;
                parts.Add($"{mode} n={average.Count} Total {average.TotalElapsedMs / average.Count}ms / OCR {average.TotalOcrMs / average.Count}ms / Translate {average.TotalTranslateMs / average.Count}ms / Calls {average.TotalOcrCalls / average.Count}");
            }

            foreach (var item in ocrPerformanceAverages.Where(x => !modeOrder.Contains(x.Key)))
            {
                OcrPerformanceAverage average = item.Value;
                parts.Add($"{item.Key} n={average.Count} Total {average.TotalElapsedMs / average.Count}ms / OCR {average.TotalOcrMs / average.Count}ms / Translate {average.TotalTranslateMs / average.Count}ms / Calls {average.TotalOcrCalls / average.Count}");
            }

            return "OCR 평균: " + string.Join("  |  ", parts);
        }

        /// <summary>
        /// 로그창이 열려 있으면 최신 OCR 평균 성능 요약을 전달합니다.
        /// 로그창이 아직 없으면 문자열만 보관하고, 나중에 열 때 ShowLogViewerWindow에서 다시 전달합니다.
        /// </summary>
        private void PushOcrPerformanceSummaryToLogViewer()
        {
            logViewerWindow?.UpdateOcrPerformanceSummary(latestOcrPerformanceSummary);
        }
    }
}
