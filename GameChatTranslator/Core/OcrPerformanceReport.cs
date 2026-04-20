using System;

namespace GameTranslator
{
    /// <summary>
    /// 한 번의 OCR/번역 실행에서 수집한 성능 진단 값을 로그 문자열로 만들기 위한 순수 모델입니다.
    /// WPF, WinRT, Bitmap에 의존하지 않아 단위 테스트에서 직접 검증할 수 있습니다.
    /// </summary>
    public sealed class OcrPerformanceReport
    {
        public string ModeLabel { get; set; } = "-";
        public long CaptureMs { get; set; }
        public long ResizeMs { get; set; }
        public long PreprocessMs { get; set; }
        public long CropMs { get; set; }
        public long OcrMs { get; set; }
        public long ScoringMs { get; set; }
        public long TranslateMs { get; set; }
        public long TotalMs { get; set; }
        public int PreprocessCandidateCount { get; set; }
        public int OcrLanguageCallCount { get; set; }
        public int MergedLineCount { get; set; }
        public int TranslatedLineCount { get; set; }
        public int SkippedLineCount { get; set; }
        public string SelectedPreprocessName { get; set; } = "-";
        public string SelectedOcrLanguages { get; set; } = "-";
        public int SelectedScore { get; set; }
        public string Outcome { get; set; } = "Started";
        public bool FastPathAttempted { get; set; }
        public bool FastPathSucceeded { get; set; }
        public bool FallbackAttempted { get; set; }
        public string FallbackReason { get; set; } = "-";
    }

    /// <summary>
    /// OCR 모드별 평균 성능 요약을 만들기 위한 순수 모델입니다.
    /// Count는 표본 수이고 Total* 값은 누적값입니다.
    /// </summary>
    public sealed class OcrPerformanceAverageReport
    {
        public string ModeLabel { get; set; } = "-";
        public int Count { get; set; }
        public long TotalElapsedMs { get; set; }
        public long TotalOcrMs { get; set; }
        public long TotalTranslateMs { get; set; }
        public int TotalOcrCalls { get; set; }
        public int FastPathAttemptCount { get; set; }
        public int FastPathSuccessCount { get; set; }
        public int FallbackCount { get; set; }
    }

    /// <summary>
    /// OCR 성능 진단 로그와 로그창 평균 요약 문자열을 생성합니다.
    /// 문자열 포맷을 한곳에 모아 로그 구조 변경 시 테스트로 회귀를 잡기 쉽게 합니다.
    /// </summary>
    public static class OcrPerformanceReportFormatter
    {
        public static string BuildLogLine(OcrPerformanceReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            return "[OCR PERF] " +
                $"Mode={EmptyToDash(report.ModeLabel)}, " +
                $"Capture={report.CaptureMs}ms, " +
                $"Resize={report.ResizeMs}ms, " +
                $"Preprocess={report.PreprocessMs}ms, " +
                $"Crop={report.CropMs}ms, " +
                $"OCR={report.OcrMs}ms, " +
                $"Scoring={report.ScoringMs}ms, " +
                $"Translate={report.TranslateMs}ms, " +
                $"Total={report.TotalMs}ms, " +
                $"Selected={EmptyToDash(report.SelectedPreprocessName)}/{EmptyToDash(report.SelectedOcrLanguages)}, " +
                $"Score={report.SelectedScore}, " +
                $"Candidates={report.PreprocessCandidateCount}, " +
                $"OcrCalls={report.OcrLanguageCallCount}, " +
                $"FastPath={BuildFastPathText(report.FastPathAttempted, report.FastPathSucceeded)}, " +
                $"Fallback={BuildFallbackText(report.FallbackAttempted, report.FallbackReason)}, " +
                $"Lines={report.MergedLineCount}, " +
                $"Translated={report.TranslatedLineCount}, " +
                $"Skipped={report.SkippedLineCount}, " +
                $"Outcome={EmptyToDash(report.Outcome)}";
        }

        public static string BuildAveragePart(OcrPerformanceAverageReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (report.Count <= 0) return $"{EmptyToDash(report.ModeLabel)} n=0";

            return $"{EmptyToDash(report.ModeLabel)} " +
                $"n={report.Count} " +
                $"Total {report.TotalElapsedMs / report.Count}ms / " +
                $"OCR {report.TotalOcrMs / report.Count}ms / " +
                $"Translate {report.TotalTranslateMs / report.Count}ms / " +
                $"Calls {report.TotalOcrCalls / report.Count} / " +
                $"FastPath {BuildFastPathAverageText(report.FastPathSuccessCount, report.FastPathAttemptCount)} / " +
                $"Fallback {report.FallbackCount}/{report.Count}";
        }

        public static string BuildFastPathText(bool attempted, bool succeeded)
        {
            if (!attempted) return "NotUsed";
            return succeeded ? "Success" : "Failed";
        }

        public static string BuildFallbackText(bool attempted, string reason)
        {
            if (!attempted) return "No";

            string value = EmptyToDash(reason);
            return value == "-" ? "Yes" : $"Yes({value})";
        }

        private static string BuildFastPathAverageText(int successCount, int attemptCount)
        {
            if (attemptCount <= 0) return "-";
            return $"{successCount}/{attemptCount}";
        }

        private static string EmptyToDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
