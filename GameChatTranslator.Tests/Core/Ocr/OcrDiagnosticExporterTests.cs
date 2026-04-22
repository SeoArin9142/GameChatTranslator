using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrDiagnosticExporterTests
    {
        private readonly OcrDiagnosticExporter _exporter = new OcrDiagnosticExporter();

        [Fact]
        public void BuildSummaryText_IncludesSelectedCandidateAndTimings()
        {
            OcrDiagnosticResult result = CreateSampleResult();

            string summary = _exporter.BuildSummaryText(result);

            Assert.Contains("선택 후보: Color", summary);
            Assert.Contains("Threshold: 120", summary);
            Assert.Contains("Total: 70ms", summary);
            Assert.Contains("- Color: 123", summary);
            Assert.Contains("외부 OCR 상태: Tesseract 후보 추가 (jpn+eng+kor+chi_sim)", summary);
            Assert.Contains("앱 버전: v.test", summary);
            Assert.Contains("게임 언어: ko", summary);
            Assert.Contains("현재 자동 OCR 모드: 자동", summary);
            Assert.Contains("표시 좌표 CaptureX/Y/W/H: X=1, Y=2, W=30, H=40", summary);
            Assert.Contains("물리 픽셀 CapturePixelX/Y/W/H: X=10, Y=20, W=300, H=120", summary);
            Assert.Contains("- 영어 (en-US) : 설치됨", summary);
        }

        [Fact]
        public void BuildCandidateText_IncludesMergedLinesAndLanguageResults()
        {
            OcrDiagnosticCandidate candidate = CreateSampleResult().Candidates[0];

            string text = _exporter.BuildCandidateText(candidate, true);

            Assert.Contains("선택 여부: YES", text);
            Assert.Contains("01. [미셸]: hello", text);
            Assert.Contains("-- en-US --", text);
            Assert.Contains("01. [Michelle]: hello", text);
        }

        [Fact]
        public void BuildFullText_IncludesSummaryAndEveryCandidateDetail()
        {
            OcrDiagnosticResult result = CreateSampleResult();
            result.Candidates.Add(new OcrDiagnosticCandidate
            {
                Name = "Adaptive",
                Score = 11,
                PreprocessedPng = new byte[] { 1 },
                CroppedPng = new byte[] { 2 }
            });

            string text = _exporter.BuildFullText(result);

            Assert.Contains("[OCR 진단 요약]", text);
            Assert.Contains("선택 후보: Color", text);
            Assert.Contains("[Color]", text);
            Assert.Contains("선택 여부: YES", text);
            Assert.Contains("[Adaptive]", text);
            Assert.Contains("선택 여부: NO", text);
            Assert.Contains("========================================", text);
        }

        [Fact]
        public void BuildFullText_ThrowsForNullResult()
        {
            Assert.Throws<ArgumentNullException>(() => _exporter.BuildFullText(null));
        }

        [Fact]
        public void CreateDefaultFileName_UsesCaptureTimestamp()
        {
            string fileName = _exporter.CreateDefaultFileName(new DateTime(2026, 4, 20, 15, 30, 40));

            Assert.Equal("GameChatTranslator_OCR_Diagnostic_20260420_153040.zip", fileName);
        }

        [Fact]
        public void ExportToZip_WritesSummaryImagesAndCandidateDetails()
        {
            OcrDiagnosticResult result = CreateSampleResult();

            using var stream = new MemoryStream();
            _exporter.ExportToZip(result, stream);

            stream.Position = 0;
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            AssertEntryExists(archive, "summary.txt");
            AssertEntryExists(archive, "images/raw_capture.png");
            AssertEntryExists(archive, "images/resized_ocr.png");
            AssertEntryExists(archive, "candidates/01_Color/details.txt");
            AssertEntryExists(archive, "candidates/01_Color/preprocessed.png");
            AssertEntryExists(archive, "candidates/01_Color/cropped.png");

            string summary = ReadEntryText(archive, "summary.txt");
            string candidateDetails = ReadEntryText(archive, "candidates/01_Color/details.txt");
            Assert.Contains("선택 후보: Color", summary);
            Assert.Contains("[언어별 OCR 결과]", candidateDetails);
        }

        private static OcrDiagnosticResult CreateSampleResult()
        {
            var result = new OcrDiagnosticResult
            {
                CapturedAt = new DateTime(2026, 4, 20, 15, 30, 40),
                CaptureArea = new System.Drawing.Rectangle(10, 20, 300, 120),
                Threshold = 120,
                ScaleFactor = 3,
                RawPng = new byte[] { 1, 2, 3 },
                ResizedPng = new byte[] { 4, 5, 6 },
                SelectedCandidateName = "Color",
                SelectedScore = 123,
                CaptureMs = 10,
                ResizeMs = 11,
                PreprocessMs = 12,
                CropMs = 13,
                OcrMs = 14,
                ScoringMs = 15,
                TotalMs = 70,
                OcrCallCount = 2,
                ExternalOcrStatus = "Tesseract 후보 추가 (jpn+eng+kor+chi_sim)",
                Metadata = new OcrDiagnosticMetadata
                {
                    AppVersion = "v.test",
                    GameLanguage = "ko",
                    TargetLanguage = "en-US",
                    AutoTranslateMode = "자동",
                    DiagnosticProcessingMode = "정확",
                    SaveDebugImages = "false",
                    ResultDisplayMode = "History",
                    ResultHistoryLimit = 5,
                    CaptureDisplayArea = "X=1, Y=2, W=30, H=40",
                    CapturePixelArea = "X=10, Y=20, W=300, H=120"
                }
            };
            result.Metadata.OcrLanguageStatuses.Add("한국어 (ko) : 설치됨");
            result.Metadata.OcrLanguageStatuses.Add("영어 (en-US) : 설치됨");
            result.Metadata.OcrLanguageStatuses.Add("일본어 (ja) : 미설치");

            var candidate = new OcrDiagnosticCandidate
            {
                Name = "Color",
                Score = 123,
                PreprocessedPng = new byte[] { 7, 8, 9 },
                CroppedPng = new byte[] { 10, 11, 12 }
            };
            candidate.MergedLines.Add("[미셸]: hello");

            var language = new OcrDiagnosticLanguageResult
            {
                LanguageTag = "en-US"
            };
            language.Lines.Add("[Michelle]: hello");
            candidate.Languages.Add(language);
            result.Candidates.Add(candidate);

            return result;
        }

        private static void AssertEntryExists(ZipArchive archive, string entryName)
        {
            Assert.Contains(archive.Entries, entry => entry.FullName == entryName);
        }

        private static string ReadEntryText(ZipArchive archive, string entryName)
        {
            ZipArchiveEntry entry = archive.Entries.Single(item => item.FullName == entryName);
            using Stream stream = entry.Open();
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
