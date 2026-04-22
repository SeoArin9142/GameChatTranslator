using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Ocr;

namespace GameTranslator
{
    public partial class MainWindow
    {
        private const int TesseractDiagnosticTimeoutMs = 2500;
        private const int EasyOcrDiagnosticTimeoutMs = 120000;
        private const int PaddleOcrDiagnosticTimeoutMs = 120000;

        /// <summary>
        /// OCR 테스트/진단 창을 열거나 이미 열린 창을 앞으로 가져옵니다.
        /// 진단 창은 현재 캡처 영역을 실제 번역 로직과 같은 전처리/OCR 기준으로 분석합니다.
        /// </summary>
        public void ShowOcrDiagnosticWindow()
        {
            if (ocrDiagnosticWindow == null)
            {
                ocrDiagnosticWindow = new OcrDiagnosticWindow(this, appDataPaths?.OcrDiagnosticsDirectory);
                ocrDiagnosticWindow.Closed += (s, e) => ocrDiagnosticWindow = null;
            }

            if (!ocrDiagnosticWindow.IsVisible)
            {
                ocrDiagnosticWindow.Show();
            }

            ocrDiagnosticWindow.Activate();
        }

        /// <summary>
        /// 현재 저장된 캡처 영역을 촬영하고 Color/ColorThick/Adaptive 전처리 후보별 OCR 결과를 생성합니다.
        /// 설정창의 OCR 진단 창에서 호출하며, 번역 API는 호출하지 않고 Windows OCR 결과와 점수만 계산합니다.
        /// </summary>
        public async Task<OcrDiagnosticResult> RunOcrDiagnosticAsync()
        {
            EnsureCaptureAreaLoadedForDiagnostics();

            if (gameChatArea == Rectangle.Empty)
            {
                throw new InvalidOperationException("캡처 영역이 설정되어 있지 않습니다. Ctrl+8로 채팅 영역을 먼저 지정해 주세요.");
            }

            if (ocrEngines.Count == 0)
            {
                throw new InvalidOperationException("사용 가능한 Windows OCR 언어 엔진이 없습니다. LangInstall.bat 실행 후 재부팅하고 다시 확인해 주세요.");
            }

            int threshold = SettingsValueNormalizer.NormalizeThreshold(ini.Read("Threshold"));
            int scaleFactor = SettingsValueNormalizer.NormalizeScaleFactor(ini.Read("ScaleFactor"));

            var result = new OcrDiagnosticResult
            {
                CapturedAt = DateTime.Now,
                Threshold = threshold,
                ScaleFactor = scaleFactor,
                CaptureArea = GetCapturePixelArea(),
                Metadata = BuildOcrDiagnosticMetadata()
            };

            Stopwatch totalStopwatch = Stopwatch.StartNew();
            var stats = new OcrPerformanceStats
            {
                ProcessingMode = OcrProcessingMode.Accurate
            };

            Stopwatch captureStopwatch = Stopwatch.StartNew();
            using Bitmap rawBitmap = CaptureBitmap(result.CaptureArea);
            captureStopwatch.Stop();
            stats.CaptureMs += captureStopwatch.ElapsedMilliseconds;
            result.RawPng = BitmapToPngBytes(rawBitmap);
            result.CaptureMs = stats.CaptureMs;

            Stopwatch resizeStopwatch = Stopwatch.StartNew();
            using Bitmap resizedBitmap = ResizeBitmapForOcr(rawBitmap, scaleFactor);
            resizeStopwatch.Stop();
            stats.ResizeMs += resizeStopwatch.ElapsedMilliseconds;
            result.ResizedPng = BitmapToPngBytes(resizedBitmap);
            result.ResizeMs = stats.ResizeMs;

            Stopwatch preprocessStopwatch = Stopwatch.StartNew();
            List<PreprocessedOcrImage> preprocessedImages = ocrImagePreprocessor.CreatePreprocessedOcrImages(
                resizedBitmap,
                threshold,
                OcrPreprocessKind.Color,
                OcrPreprocessKind.ColorThick,
                OcrPreprocessKind.Adaptive);
            preprocessStopwatch.Stop();
            stats.PreprocessMs += preprocessStopwatch.ElapsedMilliseconds;

            try
            {
                OcrDiagnosticCandidate bestCandidate = null;

                foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
                {
                    var diagnosticCandidate = new OcrDiagnosticCandidate
                    {
                        Name = preprocessedImage.Name,
                        PreprocessedPng = BitmapToPngBytes(preprocessedImage.Bitmap)
                    };

                    Stopwatch cropStopwatch = Stopwatch.StartNew();
                    using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);
                    cropStopwatch.Stop();
                    stats.CropMs += cropStopwatch.ElapsedMilliseconds;
                    diagnosticCandidate.CroppedPng = BitmapToPngBytes(croppedBitmap);

                    Dictionary<string, OcrResult> ocrResults = await RecognizeLanguagesAsync(croppedBitmap, true, stats);
                    foreach (var kvp in ocrResults.OrderBy(x => x.Key))
                    {
                        var languageResult = new OcrDiagnosticLanguageResult
                        {
                            LanguageTag = kvp.Key
                        };
                        languageResult.Lines.AddRange(kvp.Value.Lines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                        diagnosticCandidate.Languages.Add(languageResult);
                    }

                    Stopwatch scoringStopwatch = Stopwatch.StartNew();
                    OcrResult masterResult = SelectMasterOcrResult(ocrResults);
                    if (masterResult != null)
                    {
                        List<OcrLine> mergedLines = MergeOcrLines(masterResult);
                        diagnosticCandidate.MergedLines.AddRange(mergedLines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                        diagnosticCandidate.Score = ScoreOcrCandidate(mergedLines, ReadTranslationContentMode());
                    }
                    scoringStopwatch.Stop();
                    stats.ScoringMs += scoringStopwatch.ElapsedMilliseconds;

                    result.Candidates.Add(diagnosticCandidate);
                    if (bestCandidate == null || diagnosticCandidate.Score > bestCandidate.Score)
                    {
                        bestCandidate = diagnosticCandidate;
                    }
                }

                var externalSummaries = new List<ExternalOcrDiagnosticSummary>
                {
                    await TryBuildTesseractDiagnosticCandidatesAsync(preprocessedImages, ReadTranslationContentMode()),
                    await TryBuildEasyOcrDiagnosticCandidatesAsync(preprocessedImages, ReadTranslationContentMode()),
                    await TryBuildPaddleOcrDiagnosticCandidatesAsync(preprocessedImages, ReadTranslationContentMode())
                };
                result.ExternalOcrStatus = BuildExternalOcrStatusText(externalSummaries);

                foreach (ExternalOcrDiagnosticSummary externalSummary in externalSummaries)
                {
                    stats.OcrMs += externalSummary.ElapsedMs;
                    stats.OcrLanguageCallCount += externalSummary.CallCount;

                    foreach (OcrDiagnosticCandidate externalCandidate in externalSummary.Candidates)
                    {
                        result.Candidates.Add(externalCandidate);
                        if (bestCandidate == null || externalCandidate.Score > bestCandidate.Score)
                        {
                            bestCandidate = externalCandidate;
                        }
                    }
                }

                if (bestCandidate != null)
                {
                    result.SelectedCandidateName = bestCandidate.Name;
                    result.SelectedScore = bestCandidate.Score;
                }
            }
            finally
            {
                foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
                {
                    preprocessedImage.Dispose();
                }
            }

            totalStopwatch.Stop();
            result.CaptureMs = stats.CaptureMs;
            result.ResizeMs = stats.ResizeMs;
            result.PreprocessMs = stats.PreprocessMs;
            result.CropMs = stats.CropMs;
            result.OcrMs = stats.OcrMs;
            result.ScoringMs = stats.ScoringMs;
            result.OcrCallCount = stats.OcrLanguageCallCount;
            result.TotalMs = totalStopwatch.ElapsedMilliseconds;

            AppendLog(
                "[OCR DIAG] " +
                $"Selected={result.SelectedCandidateName}, " +
                $"Score={result.SelectedScore}, " +
                $"Candidates={result.Candidates.Count}, " +
                $"OcrCalls={result.OcrCallCount}, " +
                $"Total={result.TotalMs}ms, " +
                $"External={EmptyToDash(result.ExternalOcrStatus)}");

            return result;
        }

        private async Task<ExternalOcrDiagnosticSummary> TryBuildTesseractDiagnosticCandidatesAsync(IEnumerable<PreprocessedOcrImage> preprocessedImages, TranslationContentMode contentMode)
        {
            string configuredExecutablePath = ReadTesseractExecutablePath();
            string configuredLanguageCodes = ReadTesseractLanguageCodes();
            IReadOnlyList<string> languageCombinations = tesseractCliOcrAdapter.BuildLanguageCombinations(configuredLanguageCodes, gameLang);
            IReadOnlyList<TesseractCliOcrAdapter.TesseractCliProfile> diagnosticProfiles = tesseractCliOcrAdapter.BuildDiagnosticProfiles();

            int totalCallCount = 0;
            long totalElapsedMs = 0;
            int successCount = 0;
            int failureCount = 0;
            bool executableMissing = false;
            string firstErrorMessage = "";
            var candidates = new List<OcrDiagnosticCandidate>();

            foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
            {
                using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);
                byte[] croppedPng = BitmapToPngBytes(croppedBitmap);
                byte[] preprocessedPng = BitmapToPngBytes(preprocessedImage.Bitmap);

                foreach (TesseractCliOcrAdapter.TesseractCliProfile profile in diagnosticProfiles)
                {
                    var candidate = new OcrDiagnosticCandidate
                    {
                        Name = $"Tesseract-{preprocessedImage.Name}-{profile.CandidateSuffix}",
                        PreprocessedPng = preprocessedPng,
                        CroppedPng = croppedPng
                    };
                    var languageCandidates = new List<OcrLanguageCandidate>();

                    foreach (string languageCombination in languageCombinations)
                    {
                        totalCallCount++;

                        Stopwatch stopwatch = Stopwatch.StartNew();
                        using Bitmap tesseractInputBitmap = (Bitmap)croppedBitmap.Clone();
                        TesseractCliOcrResult runResult = await Task.Run(() =>
                            tesseractCliOcrAdapter.Recognize(
                                tesseractInputBitmap,
                                configuredExecutablePath,
                                languageCombination,
                                profile,
                                TesseractDiagnosticTimeoutMs));
                        stopwatch.Stop();
                        totalElapsedMs += stopwatch.ElapsedMilliseconds;

                        if (!runResult.Success)
                        {
                            failureCount++;
                            if (string.IsNullOrWhiteSpace(firstErrorMessage))
                            {
                                firstErrorMessage = runResult.ErrorMessage;
                            }

                            if (runResult.IsExecutableMissing)
                            {
                                executableMissing = true;
                                break;
                            }

                            continue;
                        }

                        successCount++;

                        var languageResult = new OcrDiagnosticLanguageResult
                        {
                            LanguageTag = $"tesseract:{profile.CandidateSuffix}:{runResult.LanguageCodes}"
                        };
                        languageResult.Lines.AddRange(runResult.Lines);
                        candidate.Languages.Add(languageResult);

                        List<OcrLine> lines = runResult.Lines
                            .Select((text, index) => new OcrLine
                            {
                                Top = index * 20,
                                Bottom = index * 20 + 12,
                                Text = text
                            })
                            .ToList();
                        languageCandidates.Add(new OcrLanguageCandidate
                        {
                            LanguageCode = languageCombination,
                            Lines = lines
                        });
                    }

                    if (executableMissing)
                    {
                        break;
                    }

                    List<OcrLine> mergedLines = contentMode == TranslationContentMode.Strinova
                        ? ocrService.MergeBestChatLinesByComponents(languageCandidates, characterNames)
                        : ocrService.MergeBestLinesByIndex(languageCandidates, characterNames, contentMode);
                    List<OcrLine> filteredMergedLines = ocrTranslationHarnessService.FilterMergedLinesForDiagnostics(mergedLines, characterNames);
                    List<OcrLine> normalizedMergedLines = ocrService.NormalizeMergedLinesForSelection(filteredMergedLines, characterNames, contentMode);
                    if (normalizedMergedLines.Count > 0 && candidate.Languages.Count > 0)
                    {
                        candidate.MergedLines.AddRange(normalizedMergedLines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                        candidate.Score = ocrService.ScoreMergedLinesForSelection(normalizedMergedLines, characterNames, contentMode);
                        candidates.Add(candidate);
                    }
                }
            }

            if (executableMissing)
            {
                string failedStatus = $"Tesseract 미사용: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            if (successCount == 0)
            {
                string failedStatus = $"Tesseract 실패: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            string successStatus =
                $"Tesseract 후보 {candidates.Count}개 추가 / {successCount}회 성공 / {failureCount}회 실패 / timeout {TesseractDiagnosticTimeoutMs}ms";
            AppendLog($"[OCR DIAG] {successStatus}");
            return new ExternalOcrDiagnosticSummary(candidates, successStatus, totalElapsedMs, totalCallCount);
        }

        private async Task<ExternalOcrDiagnosticSummary> TryBuildEasyOcrDiagnosticCandidatesAsync(IEnumerable<PreprocessedOcrImage> preprocessedImages, TranslationContentMode contentMode)
        {
            string configuredPythonPath = ReadEasyOcrPythonPath();
            string configuredLanguageCodes = ReadEasyOcrLanguageCodes();

            int totalCallCount = 0;
            long totalElapsedMs = 0;
            int successCount = 0;
            int failureCount = 0;
            bool pythonMissing = false;
            bool moduleMissing = false;
            string firstErrorMessage = "";
            var candidates = new List<OcrDiagnosticCandidate>();

            foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
            {
                using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);
                byte[] croppedPng = BitmapToPngBytes(croppedBitmap);
                byte[] preprocessedPng = BitmapToPngBytes(preprocessedImage.Bitmap);

                Stopwatch stopwatch = Stopwatch.StartNew();
                using Bitmap easyOcrInputBitmap = (Bitmap)croppedBitmap.Clone();
                EasyOcrCliBatchResult batchResult = await Task.Run(() =>
                    easyOcrCliAdapter.Recognize(
                        easyOcrInputBitmap,
                        configuredPythonPath,
                        configuredLanguageCodes,
                        gameLang,
                        EasyOcrDiagnosticTimeoutMs));
                stopwatch.Stop();
                totalElapsedMs += stopwatch.ElapsedMilliseconds;
                totalCallCount += batchResult.LanguageCombinations.Count;

                if (!batchResult.Success)
                {
                    failureCount += Math.Max(1, batchResult.LanguageCombinations.Count);
                    if (string.IsNullOrWhiteSpace(firstErrorMessage))
                    {
                        firstErrorMessage = batchResult.ErrorMessage;
                    }

                    if (batchResult.IsPythonMissing)
                    {
                        pythonMissing = true;
                        break;
                    }

                    if (batchResult.IsModuleMissing)
                    {
                        moduleMissing = true;
                        break;
                    }

                    continue;
                }

                successCount += batchResult.GroupResults.Count(group => group.Success);
                failureCount += batchResult.GroupResults.Count(group => !group.Success);

                var candidate = new OcrDiagnosticCandidate
                {
                    Name = $"EasyOCR-{preprocessedImage.Name}",
                    PreprocessedPng = preprocessedPng,
                    CroppedPng = croppedPng
                };
                var languageCandidates = new List<OcrLanguageCandidate>();

                foreach (EasyOcrCliGroupResult groupResult in batchResult.GroupResults.Where(group => group.Success))
                {
                    var languageResult = new OcrDiagnosticLanguageResult
                    {
                        LanguageTag = $"easyocr:{groupResult.LanguageCodes}"
                    };
                    languageResult.Lines.AddRange(groupResult.Lines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                    candidate.Languages.Add(languageResult);

                    languageCandidates.Add(new OcrLanguageCandidate
                    {
                        LanguageCode = groupResult.LanguageCodes,
                        Lines = groupResult.Lines
                            .Select(line => new OcrLine
                            {
                                Top = line.Top,
                                Bottom = line.Bottom,
                                Text = line.Text
                            })
                            .ToList()
                    });
                }

                List<OcrLine> mergedLines = contentMode == TranslationContentMode.Strinova
                    ? ocrService.MergeBestChatLinesByComponents(languageCandidates, characterNames)
                    : ocrService.MergeBestLinesByIndex(languageCandidates, characterNames, contentMode);
                List<OcrLine> filteredMergedLines = ocrTranslationHarnessService.FilterMergedLinesForDiagnostics(mergedLines, characterNames);
                List<OcrLine> normalizedMergedLines = ocrService.NormalizeMergedLinesForSelection(filteredMergedLines, characterNames, contentMode);
                if (normalizedMergedLines.Count > 0 && candidate.Languages.Count > 0)
                {
                    candidate.MergedLines.AddRange(normalizedMergedLines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                    candidate.Score = ocrService.ScoreMergedLinesForSelection(normalizedMergedLines, characterNames, contentMode);
                    candidates.Add(candidate);
                }
            }

            if (pythonMissing || moduleMissing)
            {
                string failedStatus = $"EasyOCR 미사용: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            if (successCount == 0)
            {
                string failedStatus = $"EasyOCR 실패: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            string successStatus =
                $"EasyOCR 후보 {candidates.Count}개 추가 / {successCount}개 그룹 성공 / {failureCount}개 그룹 실패 / timeout {EasyOcrDiagnosticTimeoutMs}ms";
            AppendLog($"[OCR DIAG] {successStatus}");
            return new ExternalOcrDiagnosticSummary(candidates, successStatus, totalElapsedMs, totalCallCount);
        }

        private async Task<ExternalOcrDiagnosticSummary> TryBuildPaddleOcrDiagnosticCandidatesAsync(IEnumerable<PreprocessedOcrImage> preprocessedImages, TranslationContentMode contentMode)
        {
            string configuredPythonPath = ReadPaddleOcrPythonPath();
            string configuredLanguageCodes = ReadPaddleOcrLanguageCodes();

            int totalCallCount = 0;
            long totalElapsedMs = 0;
            int successCount = 0;
            int failureCount = 0;
            bool pythonMissing = false;
            bool moduleMissing = false;
            string firstErrorMessage = "";
            var candidates = new List<OcrDiagnosticCandidate>();

            foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
            {
                using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);
                byte[] croppedPng = BitmapToPngBytes(croppedBitmap);
                byte[] preprocessedPng = BitmapToPngBytes(preprocessedImage.Bitmap);

                Stopwatch stopwatch = Stopwatch.StartNew();
                using Bitmap paddleOcrInputBitmap = (Bitmap)croppedBitmap.Clone();
                PaddleOcrCliBatchResult batchResult = await Task.Run(() =>
                    paddleOcrCliAdapter.Recognize(
                        paddleOcrInputBitmap,
                        configuredPythonPath,
                        configuredLanguageCodes,
                        gameLang,
                        PaddleOcrDiagnosticTimeoutMs));
                stopwatch.Stop();
                totalElapsedMs += stopwatch.ElapsedMilliseconds;
                totalCallCount += batchResult.LanguageCandidates.Count;

                if (!batchResult.Success)
                {
                    failureCount += Math.Max(1, batchResult.LanguageCandidates.Count);
                    if (string.IsNullOrWhiteSpace(firstErrorMessage))
                    {
                        firstErrorMessage = batchResult.ErrorMessage;
                    }

                    if (batchResult.IsPythonMissing)
                    {
                        pythonMissing = true;
                        break;
                    }

                    if (batchResult.IsModuleMissing)
                    {
                        moduleMissing = true;
                        break;
                    }

                    continue;
                }

                successCount += batchResult.GroupResults.Count(group => group.Success);
                failureCount += batchResult.GroupResults.Count(group => !group.Success);

                var candidate = new OcrDiagnosticCandidate
                {
                    Name = $"PaddleOCR-{preprocessedImage.Name}",
                    PreprocessedPng = preprocessedPng,
                    CroppedPng = croppedPng
                };
                var languageCandidates = new List<OcrLanguageCandidate>();

                foreach (PaddleOcrCliGroupResult groupResult in batchResult.GroupResults.Where(group => group.Success))
                {
                    var languageResult = new OcrDiagnosticLanguageResult
                    {
                        LanguageTag = $"paddleocr:{groupResult.LanguageCodes}"
                    };
                    languageResult.Lines.AddRange(groupResult.Lines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                    candidate.Languages.Add(languageResult);

                    languageCandidates.Add(new OcrLanguageCandidate
                    {
                        LanguageCode = groupResult.LanguageCodes,
                        Lines = groupResult.Lines
                            .Select(line => new OcrLine
                            {
                                Top = line.Top,
                                Bottom = line.Bottom,
                                Text = line.Text
                            })
                            .ToList()
                    });
                }

                List<OcrLine> mergedLines = contentMode == TranslationContentMode.Strinova
                    ? ocrService.MergeBestChatLinesByComponents(languageCandidates, characterNames)
                    : ocrService.MergeBestLinesByIndex(languageCandidates, characterNames, contentMode);
                List<OcrLine> filteredMergedLines = ocrTranslationHarnessService.FilterMergedLinesForDiagnostics(mergedLines, characterNames);
                List<OcrLine> normalizedMergedLines = ocrService.NormalizeMergedLinesForSelection(filteredMergedLines, characterNames, contentMode);
                if (normalizedMergedLines.Count > 0 && candidate.Languages.Count > 0)
                {
                    candidate.MergedLines.AddRange(normalizedMergedLines.Select(line => line.Text.Trim()).Where(text => !string.IsNullOrWhiteSpace(text)));
                    candidate.Score = ocrService.ScoreMergedLinesForSelection(normalizedMergedLines, characterNames, contentMode);
                    candidates.Add(candidate);
                }
            }

            if (pythonMissing || moduleMissing)
            {
                string failedStatus = $"PaddleOCR 미사용: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            if (successCount == 0)
            {
                string failedStatus = $"PaddleOCR 실패: {firstErrorMessage}";
                AppendLog($"[OCR DIAG] {failedStatus}");
                return new ExternalOcrDiagnosticSummary(candidates, failedStatus, totalElapsedMs, totalCallCount);
            }

            string successStatus =
                $"PaddleOCR 후보 {candidates.Count}개 추가 / {successCount}개 그룹 성공 / {failureCount}개 그룹 실패 / timeout {PaddleOcrDiagnosticTimeoutMs}ms";
            AppendLog($"[OCR DIAG] {successStatus}");
            return new ExternalOcrDiagnosticSummary(candidates, successStatus, totalElapsedMs, totalCallCount);
        }

        private static string BuildExternalOcrStatusText(IEnumerable<ExternalOcrDiagnosticSummary> summaries)
        {
            return string.Join(
                Environment.NewLine,
                (summaries ?? Enumerable.Empty<ExternalOcrDiagnosticSummary>())
                    .Select(summary => summary?.Status?.Trim())
                    .Where(status => !string.IsNullOrWhiteSpace(status)));
        }

        /// <summary>
        /// 환경설정창이 열린 초기 시점처럼 MainWindow가 아직 캡처 영역을 메모리에 복원하지 못한 경우 config.ini에서 좌표를 읽습니다.
        /// 저장된 좌표가 없으면 아무 것도 하지 않으며, 호출자는 gameChatArea가 Empty인지 다시 확인해야 합니다.
        /// </summary>
        private void EnsureCaptureAreaLoadedForDiagnostics()
        {
            if (gameChatArea != Rectangle.Empty && gameChatArea.Width > 0 && gameChatArea.Height > 0)
            {
                return;
            }

            string cx = ini.Read("CaptureX");
            string cy = ini.Read("CaptureY");
            string cw = ini.Read("CaptureW");
            string ch = ini.Read("CaptureH");

            if (!int.TryParse(cx, out int x) ||
                !int.TryParse(cy, out int y) ||
                !int.TryParse(cw, out int w) ||
                !int.TryParse(ch, out int h) ||
                w <= 0 ||
                h <= 0)
            {
                return;
            }

            gameChatArea = new Rectangle(x, y, w, h);

            string cpx = ini.Read("CapturePixelX");
            string cpy = ini.Read("CapturePixelY");
            string cpw = ini.Read("CapturePixelW");
            string cph = ini.Read("CapturePixelH");
            if (int.TryParse(cpx, out int px) &&
                int.TryParse(cpy, out int py) &&
                int.TryParse(cpw, out int pw) &&
                int.TryParse(cph, out int ph) &&
                pw > 0 &&
                ph > 0)
            {
                gameChatCaptureArea = new Rectangle(px, py, pw, ph);
            }
            else
            {
                gameChatCaptureArea = ConvertDisplayAreaToPixels(gameChatArea);
            }
        }

        /// <summary>
        /// OCR 진단 ZIP에 포함할 앱 버전, 언어 설정, OCR 모드, 언어팩 상태를 문자열 모델로 만듭니다.
        /// Exporter가 WinRT/Assembly/IniFile에 의존하지 않도록 MainWindow에서 현재 런타임 값을 채워 전달합니다.
        /// </summary>
        private OcrDiagnosticMetadata BuildOcrDiagnosticMetadata()
        {
            var metadata = new OcrDiagnosticMetadata
            {
                AppVersion = CurrentAppVersion,
                GameLanguage = gameLang,
                TargetLanguage = targetLang,
                AutoTranslateMode = GetAutoTranslateModeLabel(),
                DiagnosticProcessingMode = GetOcrProcessingModeLabel(OcrProcessingMode.Accurate),
                SaveDebugImages = settingsService.IsEnabled(ini.Read("SaveDebugImages")) ? "true" : "false",
                ResultDisplayMode = ini.Read("ResultDisplayMode") ?? SettingsService.DefaultResultDisplayMode,
                ResultHistoryLimit = SettingsValueNormalizer.NormalizeResultHistoryLimit(ini.Read("ResultHistoryLimit")),
                CaptureDisplayArea = FormatRectangle(gameChatArea),
                CapturePixelArea = FormatRectangle(GetCapturePixelArea())
            };

            foreach (string status in BuildOcrLanguageStatusLines())
            {
                metadata.OcrLanguageStatuses.Add(status);
            }

            return metadata;
        }

        /// <summary>
        /// 시작 시 생성된 OCR 엔진 목록을 기준으로 언어팩 설치 상태 문자열을 만듭니다.
        /// 여기서는 WinRT 엔진 생성을 다시 시도하지 않아 OCR 진단 저장 과정의 부작용을 피합니다.
        /// </summary>
        private IEnumerable<string> BuildOcrLanguageStatusLines()
        {
            var targets = new (string Label, string Tag)[]
            {
                ("한국어", "ko"),
                ("영어", "en-US"),
                ("중국어 간체", "zh-Hans-CN"),
                ("일본어", "ja"),
                ("러시아어", "ru")
            };

            foreach ((string label, string tag) in targets)
            {
                string status = ocrEngines.ContainsKey(tag) ? "설치됨" : "미설치";
                yield return $"{label} ({tag}) : {status}";
            }
        }

        private string FormatRectangle(Rectangle rectangle)
        {
            if (rectangle == Rectangle.Empty || rectangle.Width <= 0 || rectangle.Height <= 0)
            {
                return "없음";
            }

            return $"X={rectangle.X}, Y={rectangle.Y}, W={rectangle.Width}, H={rectangle.Height}";
        }

        /// <summary>
        /// 지정한 물리 픽셀 영역을 BitBlt로 캡처해 Bitmap으로 반환합니다.
        /// <paramref name="captureArea"/>는 GetCapturePixelArea에서 얻은 실제 화면 픽셀 좌표입니다.
        /// </summary>
        private Bitmap CaptureBitmap(Rectangle captureArea)
        {
            Bitmap bitmap = new Bitmap(captureArea.Width, captureArea.Height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                IntPtr hdcSrc = GetWindowDC(IntPtr.Zero);
                IntPtr hdcDest = g.GetHdc();
                BitBlt(hdcDest, 0, 0, captureArea.Width, captureArea.Height, hdcSrc, captureArea.X, captureArea.Y, 0x00CC0020);
                g.ReleaseHdc(hdcDest);
                ReleaseDC(IntPtr.Zero, hdcSrc);
            }

            return bitmap;
        }

        /// <summary>
        /// OCR 설정 배율에 맞춰 원본 캡처 이미지를 확대합니다.
        /// <paramref name="source"/>는 원본 캡처 이미지이고, <paramref name="scaleFactor"/>는 1~4 사이 확대 배율입니다.
        /// </summary>
        private Bitmap ResizeBitmapForOcr(Bitmap source, int scaleFactor)
        {
            int newWidth = source.Width * scaleFactor;
            int newHeight = source.Height * scaleFactor;
            Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
            using (Graphics g = Graphics.FromImage(resizedBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, newWidth, newHeight);
            }

            return resizedBitmap;
        }

        /// <summary>
        /// WPF Image 컨트롤에서 표시할 수 있도록 Bitmap을 PNG byte 배열로 변환합니다.
        /// </summary>
        private byte[] BitmapToPngBytes(Bitmap bitmap)
        {
            using MemoryStream ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }

        private static string EmptyToDash(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private sealed class ExternalOcrDiagnosticSummary
        {
            public ExternalOcrDiagnosticSummary(List<OcrDiagnosticCandidate> candidates, string status, long elapsedMs, int callCount)
            {
                Candidates = candidates ?? new List<OcrDiagnosticCandidate>();
                Status = status ?? "";
                ElapsedMs = elapsedMs;
                CallCount = callCount;
            }

            public List<OcrDiagnosticCandidate> Candidates { get; }
            public string Status { get; }
            public long ElapsedMs { get; }
            public int CallCount { get; }
        }
    }
}
