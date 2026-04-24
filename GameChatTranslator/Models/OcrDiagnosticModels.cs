using System;
using System.Collections.Generic;

namespace GameTranslator
{
    /// <summary>
    /// OCR 테스트/진단 창에 표시할 한 번의 캡처 진단 결과입니다.
    /// 캡처 영역, 원본/확대 이미지, 후보별 OCR 결과와 처리 시간을 함께 담습니다.
    /// </summary>
    public class OcrDiagnosticResult
    {
        public DateTime CapturedAt { get; set; }
        public System.Drawing.Rectangle CaptureArea { get; set; }
        public int Threshold { get; set; }
        public int ScaleFactor { get; set; }
        public byte[] RawPng { get; set; }
        public byte[] ResizedPng { get; set; }
        public string SelectedCandidateName { get; set; } = "-";
        public int SelectedScore { get; set; }
        public long CaptureMs { get; set; }
        public long ResizeMs { get; set; }
        public long PreprocessMs { get; set; }
        public long CropMs { get; set; }
        public long OcrMs { get; set; }
        public long ScoringMs { get; set; }
        public long TotalMs { get; set; }
        public int OcrCallCount { get; set; }
        public string ExternalOcrStatus { get; set; } = "";
        public OcrDiagnosticMetadata Metadata { get; set; } = new OcrDiagnosticMetadata();
        public List<OcrDiagnosticCandidate> Candidates { get; } = new List<OcrDiagnosticCandidate>();
    }

    /// <summary>
    /// OCR 진단 ZIP을 공유했을 때 재현 환경을 파악하기 위한 설정/환경 요약입니다.
    /// WinRT, Assembly, IniFile 같은 런타임 의존 객체는 직접 참조하지 않고 호출부에서 문자열 값으로 채워 전달합니다.
    /// </summary>
    public class OcrDiagnosticMetadata
    {
        public string AppVersion { get; set; } = "";
        public string BuildInformationalVersion { get; set; } = "";
        public string BuildCommit { get; set; } = "";
        public string GameLanguage { get; set; } = "";
        public string TargetLanguage { get; set; } = "";
        public string ConfiguredOcrEngine { get; set; } = "";
        public string AutoTranslateMode { get; set; } = "";
        public string DiagnosticProcessingMode { get; set; } = "";
        public string SaveDebugImages { get; set; } = "";
        public string ResultDisplayMode { get; set; } = "";
        public int ResultHistoryLimit { get; set; }
        public string CaptureDisplayArea { get; set; } = "";
        public string CapturePixelArea { get; set; } = "";
        public List<string> OcrLanguageStatuses { get; } = new List<string>();
    }

    /// <summary>
    /// Color, ColorThick, Adaptive 같은 전처리 후보 하나의 진단 결과입니다.
    /// 후보 이미지, OCR 언어별 원문, 병합 라인과 점수를 표시하는 데 사용합니다.
    /// </summary>
    public class OcrDiagnosticCandidate
    {
        public string Name { get; set; } = "";
        public byte[] PreprocessedPng { get; set; }
        public byte[] CroppedPng { get; set; }
        public int Score { get; set; }
        public List<string> MergedLines { get; } = new List<string>();
        public List<OcrDiagnosticLanguageResult> Languages { get; } = new List<OcrDiagnosticLanguageResult>();
    }

    /// <summary>
    /// 특정 Windows OCR 언어 엔진이 반환한 원문 라인 목록입니다.
    /// </summary>
    public class OcrDiagnosticLanguageResult
    {
        public string LanguageTag { get; set; } = "";
        public List<string> Lines { get; } = new List<string>();
    }
}
