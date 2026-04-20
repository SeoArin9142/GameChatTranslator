using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace GameTranslator
{
    /// <summary>
    /// OCR 진단 결과를 텍스트와 PNG 이미지 묶음 ZIP으로 저장합니다.
    /// WPF UI에 의존하지 않으므로 테스트에서 MemoryStream으로 검증할 수 있습니다.
    /// </summary>
    public sealed class OcrDiagnosticExporter
    {
        /// <summary>
        /// OCR 진단 결과를 ZIP 스트림에 기록합니다.
        /// <paramref name="result"/>는 OcrDiagnosticWindow에 표시된 진단 결과이고,
        /// <paramref name="outputStream"/>은 저장 대상 ZIP 파일 스트림입니다.
        /// </summary>
        public void ExportToZip(OcrDiagnosticResult result, Stream outputStream)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (outputStream == null) throw new ArgumentNullException(nameof(outputStream));

            using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, leaveOpen: true, Encoding.UTF8);
            WriteTextEntry(archive, "summary.txt", BuildSummaryText(result));
            WriteBinaryEntry(archive, "images/raw_capture.png", result.RawPng);
            WriteBinaryEntry(archive, "images/resized_ocr.png", result.ResizedPng);

            for (int i = 0; i < result.Candidates.Count; i++)
            {
                OcrDiagnosticCandidate candidate = result.Candidates[i];
                bool selected = candidate.Name == result.SelectedCandidateName;
                string folder = $"candidates/{i + 1:00}_{SanitizeEntryName(candidate.Name)}";

                WriteTextEntry(archive, $"{folder}/details.txt", BuildCandidateText(candidate, selected));
                WriteBinaryEntry(archive, $"{folder}/preprocessed.png", candidate.PreprocessedPng);
                WriteBinaryEntry(archive, $"{folder}/cropped.png", candidate.CroppedPng);
            }
        }

        /// <summary>
        /// 저장 대화상자에서 사용할 기본 ZIP 파일명을 생성합니다.
        /// <paramref name="capturedAt"/>은 진단 캡처 시각입니다.
        /// </summary>
        public string CreateDefaultFileName(DateTime capturedAt)
        {
            return $"GameChatTranslator_OCR_Diagnostic_{capturedAt:yyyyMMdd_HHmmss}.zip";
        }

        /// <summary>
        /// OCR 진단 요약 텍스트를 생성합니다.
        /// </summary>
        public string BuildSummaryText(OcrDiagnosticResult result)
        {
            var builder = new StringBuilder();
            builder.AppendLine("[OCR 진단 요약]");
            builder.AppendLine($"진단 시각: {result.CapturedAt:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"캡처 영역: X={result.CaptureArea.X}, Y={result.CaptureArea.Y}, W={result.CaptureArea.Width}, H={result.CaptureArea.Height}");
            builder.AppendLine($"Threshold: {result.Threshold}");
            builder.AppendLine($"ScaleFactor: {result.ScaleFactor}");
            AppendMetadata(builder, result.Metadata);
            builder.AppendLine();
            builder.AppendLine("[선택 결과]");
            builder.AppendLine($"선택 후보: {result.SelectedCandidateName}");
            builder.AppendLine($"선택 점수: {result.SelectedScore}");
            builder.AppendLine($"후보 수: {result.Candidates.Count}");
            builder.AppendLine($"OCR 호출 수: {result.OcrCallCount}");
            builder.AppendLine();
            builder.AppendLine("[처리 시간]");
            builder.AppendLine($"Capture: {result.CaptureMs}ms");
            builder.AppendLine($"Resize: {result.ResizeMs}ms");
            builder.AppendLine($"Preprocess: {result.PreprocessMs}ms");
            builder.AppendLine($"Crop: {result.CropMs}ms");
            builder.AppendLine($"OCR: {result.OcrMs}ms");
            builder.AppendLine($"Scoring: {result.ScoringMs}ms");
            builder.AppendLine($"Total: {result.TotalMs}ms");
            builder.AppendLine();
            builder.AppendLine("[후보 점수]");
            foreach (OcrDiagnosticCandidate candidate in result.Candidates.OrderByDescending(c => c.Score))
            {
                builder.AppendLine($"- {candidate.Name}: {candidate.Score}");
            }

            return builder.ToString();
        }

        /// <summary>
        /// 클립보드 공유용 OCR 진단 전체 텍스트를 생성합니다.
        /// summary.txt와 후보별 details.txt 내용을 한 번에 붙여넣을 수 있게 이어 붙입니다.
        /// </summary>
        public string BuildFullText(OcrDiagnosticResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            var builder = new StringBuilder();
            builder.AppendLine(BuildSummaryText(result).TrimEnd());

            foreach (OcrDiagnosticCandidate candidate in result.Candidates)
            {
                bool selected = candidate.Name == result.SelectedCandidateName;
                builder.AppendLine();
                builder.AppendLine("========================================");
                builder.AppendLine(BuildCandidateText(candidate, selected).TrimEnd());
            }

            return builder.ToString().TrimEnd();
        }

        private static void AppendMetadata(StringBuilder builder, OcrDiagnosticMetadata metadata)
        {
            if (metadata == null) return;

            builder.AppendLine();
            builder.AppendLine("[환경 설정]");
            builder.AppendLine($"앱 버전: {EmptyToDash(metadata.AppVersion)}");
            builder.AppendLine($"게임 언어: {EmptyToDash(metadata.GameLanguage)}");
            builder.AppendLine($"번역 언어: {EmptyToDash(metadata.TargetLanguage)}");
            builder.AppendLine($"현재 자동 OCR 모드: {EmptyToDash(metadata.AutoTranslateMode)}");
            builder.AppendLine($"진단 OCR 모드: {EmptyToDash(metadata.DiagnosticProcessingMode)}");
            builder.AppendLine($"디버그 이미지 저장: {EmptyToDash(metadata.SaveDebugImages)}");
            builder.AppendLine($"결과 표시 방식: {EmptyToDash(metadata.ResultDisplayMode)}");
            builder.AppendLine($"누적 표시 줄 수: {metadata.ResultHistoryLimit}");
            builder.AppendLine($"표시 좌표 CaptureX/Y/W/H: {EmptyToDash(metadata.CaptureDisplayArea)}");
            builder.AppendLine($"물리 픽셀 CapturePixelX/Y/W/H: {EmptyToDash(metadata.CapturePixelArea)}");
            builder.AppendLine();
            builder.AppendLine("[OCR 언어팩 상태]");

            if (metadata.OcrLanguageStatuses.Count == 0)
            {
                builder.AppendLine("정보 없음");
                return;
            }

            foreach (string status in metadata.OcrLanguageStatuses)
            {
                builder.AppendLine("- " + status);
            }
        }

        /// <summary>
        /// 후보별 병합 라인과 언어별 OCR 원문을 텍스트로 정리합니다.
        /// </summary>
        public string BuildCandidateText(OcrDiagnosticCandidate candidate, bool selected)
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[{candidate.Name}]");
            builder.AppendLine($"선택 여부: {(selected ? "YES" : "NO")}");
            builder.AppendLine($"점수: {candidate.Score}");
            builder.AppendLine();
            builder.AppendLine("[병합 라인]");
            AppendNumberedLines(builder, candidate.MergedLines);
            builder.AppendLine();
            builder.AppendLine("[언어별 OCR 결과]");

            foreach (OcrDiagnosticLanguageResult language in candidate.Languages)
            {
                builder.AppendLine();
                builder.AppendLine($"-- {language.LanguageTag} --");
                AppendNumberedLines(builder, language.Lines);
            }

            if (candidate.Languages.Count == 0)
            {
                builder.AppendLine("OCR 결과 없음");
            }

            return builder.ToString();
        }

        private static void AppendNumberedLines(StringBuilder builder, System.Collections.Generic.IList<string> lines)
        {
            if (lines == null || lines.Count == 0)
            {
                builder.AppendLine("없음");
                return;
            }

            for (int i = 0; i < lines.Count; i++)
            {
                builder.AppendLine($"{i + 1:00}. {lines[i]}");
            }
        }

        private static void WriteTextEntry(ZipArchive archive, string entryName, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text ?? "");
            WriteBinaryEntry(archive, entryName, bytes);
        }

        private static void WriteBinaryEntry(ZipArchive archive, string entryName, byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return;

            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            stream.Write(bytes, 0, bytes.Length);
        }

        private static string SanitizeEntryName(string name)
        {
            string value = string.IsNullOrWhiteSpace(name) ? "candidate" : name.Trim();
            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidChar, '_');
            }

            return value.Replace(' ', '_');
        }

        private static string EmptyToDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
