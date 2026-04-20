using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using OcrResultCandidate = GameTranslator.OcrCandidate<System.Collections.Generic.Dictionary<string, Windows.Media.Ocr.OcrResult>>;

namespace GameTranslator
{
    /// <summary>
    /// 자동 번역 모드, OCR 전처리, 텍스트 추출, 번역 API 호출, 결과 출력까지 담당하는 partial 파일입니다.
    /// 캡처된 채팅 이미지를 전처리하고 Windows OCR 결과를 검증한 뒤 Google 또는 Gemini로 번역합니다.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// Ctrl+0으로 순환되는 자동 번역 상태입니다.
        /// Off는 타이머 정지, Fast/Auto/Accurate는 각각 속도/균형/정확도 우선 OCR 전략입니다.
        /// </summary>
        private enum AutoTranslateMode
        {
            Off,
            Fast,
            Auto,
            Accurate
        }

        /// <summary>
        /// 자동 번역 단축키를 눌렀을 때 모드를 빠름 → 자동 → 정확 → OFF 순서로 전환합니다.
        /// 영역이 아직 설정되지 않았다면 자동 번역을 시작하지 않습니다.
        /// </summary>
        private void ToggleAutoTranslate()
        {
            if (gameChatArea == Rectangle.Empty) return;
            autoTranslateMode = GetNextAutoTranslateMode(autoTranslateMode);
            isAutoTranslating = autoTranslateMode != AutoTranslateMode.Off;
            ResetTranslationCache($"자동 번역 모드 변경: {GetAutoTranslateModeLabel()}");
            UpdateYellowHotkeyGuideText();
            AppendLog($"자동 번역 모드: {GetAutoTranslateModeLabel()}");

            if (isAutoTranslating)
            {
                runTranslation(GetCurrentOcrProcessingMode());
                autoTranslateTimer.Start();
            }
            else
            {
                autoTranslateTimer.Stop();
            }
        }

        /// <summary>
        /// 현재 자동 번역 모드의 다음 모드를 계산합니다.
        /// <paramref name="currentMode"/>는 현재 상태이며, 반환값은 Ctrl+0을 한 번 더 눌렀을 때 적용될 상태입니다.
        /// </summary>
        private AutoTranslateMode GetNextAutoTranslateMode(AutoTranslateMode currentMode)
        {
            return currentMode switch
            {
                AutoTranslateMode.Off => AutoTranslateMode.Fast,
                AutoTranslateMode.Fast => AutoTranslateMode.Auto,
                AutoTranslateMode.Auto => AutoTranslateMode.Accurate,
                AutoTranslateMode.Accurate => AutoTranslateMode.Off,
                _ => AutoTranslateMode.Fast
            };
        }

        /// <summary>
        /// 자동 번역 상태를 실제 OCR 처리 모드로 변환합니다.
        /// Off 상태는 호출되지 않는 것이 정상이나, 방어적으로 Accurate를 반환합니다.
        /// </summary>
        private OcrProcessingMode GetCurrentOcrProcessingMode()
        {
            return autoTranslateMode switch
            {
                AutoTranslateMode.Fast => OcrProcessingMode.Fast,
                AutoTranslateMode.Auto => OcrProcessingMode.Auto,
                AutoTranslateMode.Accurate => OcrProcessingMode.Accurate,
                _ => OcrProcessingMode.Accurate
            };
        }

        /// <summary>
        /// 현재 자동 번역 모드를 사용자에게 보여줄 한국어 라벨로 변환합니다.
        /// 상단 안내 문구와 로그 메시지에서 사용됩니다.
        /// </summary>
        private string GetAutoTranslateModeLabel()
        {
            return autoTranslateMode switch
            {
                AutoTranslateMode.Fast => "빠름",
                AutoTranslateMode.Auto => "자동",
                AutoTranslateMode.Accurate => "정확",
                _ => "OFF"
            };
        }

        /// <summary>
        /// OCR 처리 모드를 로그에 남길 한국어 라벨로 변환합니다.
        /// <paramref name="processingMode"/>는 이번 번역 실행에서 실제로 사용한 OCR 전략입니다.
        /// </summary>
        private string GetOcrProcessingModeLabel(OcrProcessingMode processingMode)
        {
            return processingMode switch
            {
                OcrProcessingMode.Fast => "빠름",
                OcrProcessingMode.Auto => "자동",
                OcrProcessingMode.Accurate => "정확",
                _ => "정확"
            };
        }

        /// <summary>
        /// Ctrl+- 단축키로 Google 무료 번역과 Gemini AI 번역 엔진을 전환합니다.
        /// Gemini API 키가 없거나 형식이 너무 짧으면 전환하지 않고 사용자 경고를 표시합니다.
        /// </summary>
        private void ToggleEngine()
        {
            string geminiKey = ReadGeminiKey();
            if (string.IsNullOrWhiteSpace(geminiKey) || geminiKey.Length < 30)
            {
                TxtResult.Text = "⚠️ Gemini API 키가 올바르게 등록되지 않아 전환할 수 없습니다.";
                return;
            }

            // 엔진 상태 반전 (true <-> false)
            useGeminiEngine = !useGeminiEngine;
            ResetTranslationCache("번역 엔진 변경");
            string currentEngine = useGeminiEngine ? "Gemini AI" : "Google 무료";

            AppendLog($"번역 엔진이 '{currentEngine}'(으)로 실시간 변경되었습니다.");

            // 화면에 즉시 알림 표시 (파란색 글씨)
            TxtResult.Inlines.Clear();
            TxtResult.Inlines.Add(new Run($"🔄 번역 엔진 변경됨: [ {currentEngine} ]") { Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold });

            UpdateYellowHotkeyGuideText(); // 노란색 안내문구도 업데이트
        }

        /// <summary>
        /// 같은 OCR 원문을 중복 번역하지 않기 위해 저장해 둔 캐시를 초기화합니다.
        /// <paramref name="reason"/>은 로그에 남길 초기화 사유입니다. 예: 번역 엔진 변경, 캡처 영역 변경.
        /// </summary>
        private void ResetTranslationCache(string reason)
        {
            lastRawTextCombined = "";
            AppendLog($"재번역 캐시 초기화: {reason}");
        }

        /// <summary>
        /// 한 번의 번역 사이클에서 어느 단계가 시간을 많이 쓰는지 기록하는 진단 모델입니다.
        /// Capture/Preprocess/OCR/Translate처럼 병목이 될 수 있는 구간을 밀리초 단위로 누적합니다.
        /// </summary>
        private class OcrPerformanceStats
        {
            public OcrProcessingMode ProcessingMode;
            public long CaptureMs;
            public long ResizeMs;
            public long PreprocessMs;
            public long CropMs;
            public long OcrMs;
            public long ScoringMs;
            public long TranslateMs;
            public int PreprocessCandidateCount;
            public int OcrLanguageCallCount;
            public int MergedLineCount;
            public int TranslatedLineCount;
            public int SkippedLineCount;
            public string SelectedPreprocessName = "-";
            public string SelectedOcrLanguages = "-";
            public int SelectedScore;
            public string Outcome = "Started";
        }

        /// <summary>
        /// OCR 성능 진단 결과를 세션 로그에 한 줄로 남깁니다.
        /// <paramref name="stats"/>는 단계별 누적 시간과 OCR 후보 정보를 담고,
        /// <paramref name="totalElapsedMs"/>는 번역 사이클 전체 경과 시간입니다.
        /// </summary>
        private void AppendOcrPerformanceLog(OcrPerformanceStats stats, long totalElapsedMs)
        {
            AppendLog(
                "[OCR PERF] " +
                $"Mode={GetOcrProcessingModeLabel(stats.ProcessingMode)}, " +
                $"Capture={stats.CaptureMs}ms, " +
                $"Resize={stats.ResizeMs}ms, " +
                $"Preprocess={stats.PreprocessMs}ms, " +
                $"Crop={stats.CropMs}ms, " +
                $"OCR={stats.OcrMs}ms, " +
                $"Scoring={stats.ScoringMs}ms, " +
                $"Translate={stats.TranslateMs}ms, " +
                $"Total={totalElapsedMs}ms, " +
                $"Selected={stats.SelectedPreprocessName}/{stats.SelectedOcrLanguages}, " +
                $"Score={stats.SelectedScore}, " +
                $"Candidates={stats.PreprocessCandidateCount}, " +
                $"OcrCalls={stats.OcrLanguageCallCount}, " +
                $"Lines={stats.MergedLineCount}, " +
                $"Translated={stats.TranslatedLineCount}, " +
                $"Skipped={stats.SkippedLineCount}, " +
                $"Outcome={stats.Outcome}");

            TrackOcrPerformanceSummary(stats, totalElapsedMs);
        }

        /// <summary>
        /// 수동 번역 단축키에서 호출하는 기본 번역 실행 함수입니다.
        /// 수동 번역은 속도보다 인식률을 우선하므로 정확 모드로 실행합니다.
        /// </summary>
        private void runTranslation()
        {
            runTranslation(OcrProcessingMode.Accurate);
        }

        /// <summary>
        /// 화면 캡처부터 OCR, 번역 API 호출, 번역창 출력까지 한 번의 번역 사이클을 수행합니다.
        /// <paramref name="processingMode"/>는 이번 실행에서 사용할 OCR 전처리/언어 후보 전략입니다.
        /// </summary>
        private async void runTranslation(OcrProcessingMode processingMode)
        {
            if (isTranslating || gameChatArea == Rectangle.Empty) return;
            isTranslating = true;
            Stopwatch totalStopwatch = Stopwatch.StartNew();
            OcrPerformanceStats performanceStats = new OcrPerformanceStats
            {
                ProcessingMode = processingMode
            };

            int threshold = SettingsValueNormalizer.NormalizeThreshold(ini.Read("Threshold"));
            int scaleFactor = SettingsValueNormalizer.NormalizeScaleFactor(ini.Read("ScaleFactor"));

            try
            {
                // 1. 저장된 채팅 영역을 실제 화면 픽셀 기준으로 캡처합니다.
                Stopwatch captureStopwatch = Stopwatch.StartNew();
                Rectangle captureArea = GetCapturePixelArea();
                using Bitmap rawBitmap = new Bitmap(captureArea.Width, captureArea.Height);
                using (Graphics g = Graphics.FromImage(rawBitmap))
                {
                    IntPtr hdcSrc = GetWindowDC(IntPtr.Zero);
                    IntPtr hdcDest = g.GetHdc();
                    BitBlt(hdcDest, 0, 0, captureArea.Width, captureArea.Height, hdcSrc, captureArea.X, captureArea.Y, 0x00CC0020);
                    g.ReleaseHdc(hdcDest);
                    ReleaseDC(IntPtr.Zero, hdcSrc);
                }
                captureStopwatch.Stop();
                performanceStats.CaptureMs += captureStopwatch.ElapsedMilliseconds;

                // 2. OCR 인식률 향상을 위해 설정된 배율만큼 이미지를 확대합니다.
                Stopwatch resizeStopwatch = Stopwatch.StartNew();
                int newWidth = rawBitmap.Width * scaleFactor;
                int newHeight = rawBitmap.Height * scaleFactor;
                using Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawBitmap, 0, 0, newWidth, newHeight);
                }
                resizeStopwatch.Stop();
                performanceStats.ResizeMs += resizeStopwatch.ElapsedMilliseconds;

                if (ShouldSaveDebugImages())
                {
                    // 🌟 [프레임 방어 최적화] 메인 스레드 대기 방지를 위해 복사본을 만들어 백그라운드 저장
                    Bitmap rawClone = new Bitmap(rawBitmap);
                    _ = Task.Run(() =>
                    {
                        using (rawClone) SaveDebugImage(rawClone, "[Origin]");
                    });
                }

                // 3. 모드별 후보 전략으로 OCR을 수행하고 가장 점수가 높은 후보를 선택합니다.
                OcrResultCandidate bestCandidate = await SelectBestOcrCandidateAsync(resizedBitmap, threshold, processingMode, performanceStats);

                if (bestCandidate == null || bestCandidate.Lines.Count == 0 || bestCandidate.Score <= 0)
                {
                    performanceStats.Outcome = "NoOcrCandidate";
                    return;
                }

                var ocrResults = bestCandidate.Results;
                var mergedLines = bestCandidate.Lines;
                performanceStats.SelectedPreprocessName = bestCandidate.PreprocessName;
                performanceStats.SelectedOcrLanguages = string.Join("+", ocrResults.Keys);
                performanceStats.SelectedScore = bestCandidate.Score;
                performanceStats.MergedLineCount = mergedLines.Count;
                AppendLog("DEBUG", $"OCR 모드: {GetOcrProcessingModeLabel(processingMode)}, 전처리 선택: {bestCandidate.PreprocessName} (점수 {bestCandidate.Score})", "System");

                string currentRawTextCombined = string.Join("\n", mergedLines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined)
                {
                    performanceStats.Outcome = "DuplicateOcrText";
                    return;
                }
                lastRawTextCombined = currentRawTextCombined;

                BeginTranslationResultUpdate();

                // 4. OCR에서 검증된 채팅 줄만 번역하고 UI/클립보드/로그에 반영합니다.
                foreach (var chatLine in mergedLines)
                {
                    string krRawText = chatLine.Text.Trim();
                    string usedEngine = "None";

                    AppendLog("DEBUG", $"OCR 원본: {krRawText}", "System");

                    if (!ChatTextAnalyzer.ContainsReadableLetter(krRawText))
                    {
                        AppendLog("DEBUG", "글자 없음으로 스킵", "System");
                        performanceStats.SkippedLineCount++;
                        continue;
                    }

                    if (!ChatTextAnalyzer.TryParseChatLine(krRawText, out ChatTextAnalyzer.ChatLine parsedChatLine))
                    {
                        AppendLog("DEBUG", "정규식(strictMatch) 불일치로 스킵됨", "System");
                        performanceStats.SkippedLineCount++;
                        continue;
                    }

                    string characterNameOnly = parsedChatLine.CharacterName;

                    if (!characterNames.Contains(characterNameOnly))
                    {
                        AppendLog("DEBUG", $"리스트에 없는 이름이라 스킵됨: {characterNameOnly}", "System");
                        performanceStats.SkippedLineCount++;
                        continue;
                    }

                    string characterNameGold = parsedChatLine.CharacterLabel;
                    string bestMessage = parsedChatLine.Message;
                    int bestScore = -1;

                    double mTop = chatLine.Top - 5;
                    double mBot = chatLine.Bottom + 5;

                    foreach (var kvp in ocrResults)
                    {
                        var linesInBand = kvp.Value.Lines
                            .Where(l => l.Words.Count > 0 && l.Words.Any(w => w.BoundingRect.Bottom > mTop && w.BoundingRect.Top < mBot))
                            .OrderBy(l => l.Words.First().BoundingRect.Left);

                        if (linesInBand.Any())
                        {
                            string fullText = string.Join(" ", linesInBand.Select(l => l.Text.Trim()));
                            string msgOnly = fullText;

                            int subCIdx = fullText.LastIndexOfAny(new char[] { ':', ';', '：', '!' });
                            if (subCIdx != -1)
                            {
                                msgOnly = fullText.Substring(subCIdx + 1).Trim();
                            }
                            else
                            {
                                int brIdx = fullText.LastIndexOfAny(new char[] { ']', ')' });
                                if (brIdx != -1) msgOnly = fullText.Substring(brIdx + 1).Trim();
                                else msgOnly = "";
                            }

                            int score = msgOnly.Length;
                            if (kvp.Key == "zh-Hans-CN" && Regex.IsMatch(msgOnly, @"[\u4e00-\u9fa5]")) score += 40000;
                            else if (kvp.Key == "en-US" && Regex.IsMatch(msgOnly, @"[a-zA-Z]{2,}")) score += 30000;
                            else if (kvp.Key == "ja" && Regex.IsMatch(msgOnly, @"[ぁ-んァ-ヶ]")) score += 20000;
                            else if (kvp.Key == "ru" && Regex.IsMatch(msgOnly, @"[а-яА-ЯёЁ]")) score += 10000;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMessage = msgOnly;
                            }
                        }
                    }

                    string finalContent = bestMessage.Trim();
                    string geminiKey = ReadGeminiKey();
                    bool willUseGemini = useGeminiEngine && !string.IsNullOrWhiteSpace(geminiKey);

                    if (!willUseGemini)
                    {
                        if (Regex.IsMatch(finalContent, @"[\u4e00-\u9fa5]") && Regex.IsMatch(finalContent, @"[ぁ-んァ-ヶ]"))
                            finalContent = Regex.Replace(finalContent, @"[ぁ-んァ-ヶ]", "");

                        finalContent = Regex.Replace(finalContent, @"^[0-9\W_]+", "");
                        finalContent = Regex.Replace(finalContent, @"[イ尓カ幺哓ロト昌号i]", "").Trim();
                    }

                    if (string.IsNullOrWhiteSpace(finalContent))
                    {
                        performanceStats.SkippedLineCount++;
                        continue;
                    }

                    // 🌟 [1글자 채팅 예외처리 포함]
                    if (finalContent.Length == 1)
                    {
                        if (Regex.IsMatch(finalContent, @"^[イ尓カ幺哓ロト昌号iI0-9\W_]$"))
                        {
                            performanceStats.SkippedLineCount++;
                            continue;
                        }
                        if (!Regex.IsMatch(finalContent, @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$"))
                        {
                            performanceStats.SkippedLineCount++;
                            continue;
                        }
                    }
                    else if (finalContent.Length < 2)
                    {
                        performanceStats.SkippedLineCount++;
                        continue;
                    }

                    string modelName = ReadGeminiModel();
                    usedEngine = "Google";
                    string translated = finalContent;

                    TranslationPlan translationPlan = translationService.CreatePlan(finalContent, targetLang, willUseGemini);
                    TranslationDecisionResult translationResult = translationPlan.ImmediateResult;

                    if (!translationPlan.HasImmediateResult)
                    {
                        if (translationPlan.RequestKind == TranslationRequestKind.Gemini)
                        {
                            Stopwatch translateStopwatch = Stopwatch.StartNew();
                            string geminiTranslated = await CallGeminiAPI(finalContent, targetLang, geminiKey);
                            translateStopwatch.Stop();
                            performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;

                            if (translationService.ShouldFallbackToGoogle(geminiTranslated))
                            {
                                translateStopwatch.Restart();
                                string googleFallback = await CallGoogleAPI(finalContent, targetLang);
                                translateStopwatch.Stop();
                                performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;
                                translationResult = translationService.CreateGoogleResult(googleFallback, true);
                            }
                            else
                            {
                                translationResult = translationService.CreateGeminiResult(geminiTranslated, modelName);
                            }
                        }
                        else
                        {
                            Stopwatch translateStopwatch = Stopwatch.StartNew();
                            string googleTranslated = await CallGoogleAPI(finalContent, targetLang);
                            translateStopwatch.Stop();
                            performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;
                            translationResult = translationService.CreateGoogleResult(googleTranslated, false);
                        }
                    }

                    translated = translationResult.TranslatedText;
                    usedEngine = translationResult.EngineName;

                    AppendLog(characterNameGold + finalContent, translated, usedEngine);

                    AddTranslationResultToDisplay(characterNameGold, translated);
                    performanceStats.TranslatedLineCount++;
                }

                performanceStats.Outcome = performanceStats.TranslatedLineCount > 0 ? "Translated" : "NoTranslatableLines";
            }
            catch (Exception ex)
            {
                performanceStats.Outcome = "Error";
                TxtResult.Text = "에러: " + ex.Message;
            }
            finally
            {
                totalStopwatch.Stop();
                AppendOcrPerformanceLog(performanceStats, totalStopwatch.ElapsedMilliseconds);
                isTranslating = false;
            }
        }

        /// <summary>
        /// OCR 처리 모드에 맞춰 전처리 후보와 OCR 언어 범위를 선택하고 최종 후보를 반환합니다.
        /// <paramref name="resizedBitmap"/>은 캡처 후 설정 배율로 확대한 이미지,
        /// <paramref name="threshold"/>는 색상/밝기 기반 이진화 기준값,
        /// <paramref name="processingMode"/>는 빠름/자동/정확 중 이번 실행 전략입니다.
        /// <paramref name="performanceStats"/>는 후보 선택 과정의 단계별 시간을 누적하는 진단 객체입니다.
        /// </summary>
        private async Task<OcrResultCandidate> SelectBestOcrCandidateAsync(Bitmap resizedBitmap, int threshold, OcrProcessingMode processingMode, OcrPerformanceStats performanceStats)
        {
            foreach (OcrEvaluationStep step in ocrService.CreateEvaluationPlan(processingMode))
            {
                OcrResultCandidate candidate = await EvaluateOcrCandidatesAsync(
                    resizedBitmap,
                    threshold,
                    performanceStats,
                    step.RecognizeAllLanguages,
                    step.PreprocessKinds.ToArray());

                if (step.IsFastPathStep)
                {
                    if (ocrService.IsFastPathSuccess(candidate, characterNames)) return candidate;
                    if (processingMode == OcrProcessingMode.Fast) return null;
                    continue;
                }

                return candidate;
            }

            return null;
        }

        /// <summary>
        /// 지정된 전처리 후보들을 실제 Bitmap으로 만들고 OCR을 수행한 뒤 가장 높은 점수의 후보를 고릅니다.
        /// <paramref name="resizedBitmap"/>은 OCR 전처리 입력 이미지,
        /// <paramref name="threshold"/>는 색상 필터와 적응형 이진화의 기준값,
        /// <paramref name="performanceStats"/>는 전처리/OCR/점수화 시간을 누적하는 진단 객체입니다.
        /// <paramref name="recognizeAllLanguages"/>가 true이면 설치된 모든 OCR 언어를 실행하고 false이면 게임 언어만 실행합니다.
        /// <paramref name="preprocessKinds"/>는 평가할 전처리 후보 목록입니다.
        /// </summary>
        private async Task<OcrResultCandidate> EvaluateOcrCandidatesAsync(Bitmap resizedBitmap, int threshold, OcrPerformanceStats performanceStats, bool recognizeAllLanguages, params OcrPreprocessKind[] preprocessKinds)
        {
            OcrResultCandidate bestCandidate = null;
            Stopwatch preprocessStopwatch = Stopwatch.StartNew();
            List<PreprocessedOcrImage> preprocessedImages = ocrImagePreprocessor.CreatePreprocessedOcrImages(resizedBitmap, threshold, preprocessKinds);
            preprocessStopwatch.Stop();
            performanceStats.PreprocessMs += preprocessStopwatch.ElapsedMilliseconds;
            performanceStats.PreprocessCandidateCount += preprocessedImages.Count;

            try
            {
                foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
                {
                    Stopwatch cropStopwatch = Stopwatch.StartNew();
                    using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);
                    cropStopwatch.Stop();
                    performanceStats.CropMs += cropStopwatch.ElapsedMilliseconds;

                    if (ShouldSaveDebugImages())
                    {
                        Bitmap preprocessClone = new Bitmap(preprocessedImage.Bitmap);
                        Bitmap cropClone = new Bitmap(croppedBitmap);
                        string imageName = preprocessedImage.Name;
                        _ = Task.Run(() =>
                        {
                            using (preprocessClone) SaveDebugImage(preprocessClone, $"[Pre_{imageName}]");
                            using (cropClone) SaveDebugImage(cropClone, $"[Crop_{imageName}]");
                        });
                    }

                    // OCR 엔진 호출이 가장 큰 병목입니다. 빠름 모드에서는 게임 언어만 호출해 처리량을 줄입니다.
                    Dictionary<string, OcrResult> candidateResults = await RecognizeLanguagesAsync(croppedBitmap, recognizeAllLanguages, performanceStats);
                    Stopwatch scoringStopwatch = Stopwatch.StartNew();
                    OcrResult candidateMasterResult = SelectMasterOcrResult(candidateResults);
                    if (candidateMasterResult == null)
                    {
                        scoringStopwatch.Stop();
                        performanceStats.ScoringMs += scoringStopwatch.ElapsedMilliseconds;
                        continue;
                    }

                    List<OcrLine> candidateLines = MergeOcrLines(candidateMasterResult);
                    int candidateScore = ScoreOcrCandidate(candidateLines);
                    scoringStopwatch.Stop();
                    performanceStats.ScoringMs += scoringStopwatch.ElapsedMilliseconds;

                    bestCandidate = ocrService.SelectHigherScore(bestCandidate, new OcrResultCandidate
                    {
                        PreprocessName = preprocessedImage.Name,
                        Results = candidateResults,
                        Lines = candidateLines,
                        Score = candidateScore
                    });
                }
            }
            finally
            {
                foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
                {
                    preprocessedImage.Dispose();
                }
            }

            return bestCandidate;
        }

        /// <summary>
        /// OCR 후보 이미지의 하단 여백 일부를 잘라 채팅 외부 노이즈를 줄입니다.
        /// <paramref name="source"/>는 전처리 완료된 OCR 입력 이미지입니다.
        /// 반환값은 하단 5%가 제거된 새 Bitmap이며 호출자가 Dispose해야 합니다.
        /// </summary>
        private Bitmap CropForOcr(Bitmap source)
        {
            int cropTop = 0;
            int cropBottom = (int)(source.Height * 0.05);
            int newH = Math.Max(1, source.Height - cropTop - cropBottom);

            Bitmap croppedBitmap = new Bitmap(source.Width, newH);
            using (Graphics g = Graphics.FromImage(croppedBitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, newH),
                            new Rectangle(0, cropTop, source.Width, newH), GraphicsUnit.Pixel);
            }

            return croppedBitmap;
        }

        /// <summary>
        /// Bitmap을 SoftwareBitmap으로 변환해 Windows OCR 엔진을 실행합니다.
        /// <paramref name="bitmap"/>은 OCR에 넣을 전처리/크롭 이미지,
        /// <paramref name="recognizeAllLanguages"/>가 true이면 설치된 모든 언어, false이면 게임 언어 우선으로 OCR합니다.
        /// <paramref name="performanceStats"/>는 OCR 호출 횟수와 OCR 구간 소요 시간을 누적하는 진단 객체입니다.
        /// 반환값은 언어 코드별 OCR 결과 딕셔너리입니다.
        /// </summary>
        private async Task<Dictionary<string, OcrResult>> RecognizeLanguagesAsync(Bitmap bitmap, bool recognizeAllLanguages, OcrPerformanceStats performanceStats)
        {
            Stopwatch ocrStopwatch = Stopwatch.StartNew();
            try
            {
                using MemoryStream ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                var ocrResults = new Dictionary<string, OcrResult>();
                List<KeyValuePair<string, OcrEngine>> selectedEngines = SelectOcrEngines(recognizeAllLanguages);
                performanceStats.OcrLanguageCallCount += selectedEngines.Count;
                foreach (var kvp in selectedEngines)
                {
                    ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
                }

                return ocrResults;
            }
            finally
            {
                ocrStopwatch.Stop();
                performanceStats.OcrMs += ocrStopwatch.ElapsedMilliseconds;
            }
        }

        /// <summary>
        /// 이번 OCR 실행에 사용할 OCR 엔진 목록을 선택합니다.
        /// <paramref name="recognizeAllLanguages"/>가 true이면 설치된 모든 엔진, false이면 gameLang/ko/첫 엔진 순서로 하나만 선택합니다.
        /// </summary>
        private List<KeyValuePair<string, OcrEngine>> SelectOcrEngines(bool recognizeAllLanguages)
        {
            if (recognizeAllLanguages) return ocrEngines.ToList();

            if (ocrEngines.TryGetValue(gameLang, out OcrEngine gameEngine))
            {
                return new List<KeyValuePair<string, OcrEngine>>
                {
                    new KeyValuePair<string, OcrEngine>(gameLang, gameEngine)
                };
            }

            if (ocrEngines.TryGetValue("ko", out OcrEngine koreanEngine))
            {
                return new List<KeyValuePair<string, OcrEngine>>
                {
                    new KeyValuePair<string, OcrEngine>("ko", koreanEngine)
                };
            }

            return ocrEngines.Take(1).ToList();
        }

        /// <summary>
        /// 여러 언어 OCR 결과 중 줄 병합 기준으로 사용할 주 결과를 선택합니다.
        /// <paramref name="ocrResults"/>는 언어 코드별 OCR 결과 딕셔너리입니다.
        /// 게임 언어 결과가 있으면 우선 사용하고, 없으면 한국어 결과를 fallback으로 사용합니다.
        /// </summary>
        private OcrResult SelectMasterOcrResult(Dictionary<string, OcrResult> ocrResults)
        {
            return ocrResults.ContainsKey(gameLang) ? ocrResults[gameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
        }

        /// <summary>
        /// Windows OCR이 여러 조각으로 나눈 라인을 세로 위치 기준으로 다시 합칩니다.
        /// <paramref name="masterResult"/>는 기준 언어 OCR 결과입니다.
        /// 반환값은 채팅 한 줄 단위에 가깝게 병합된 OcrLine 목록입니다.
        /// </summary>
        private List<OcrLine> MergeOcrLines(OcrResult masterResult)
        {
            var mergedLines = new List<OcrLine>();

            foreach (var mLine in masterResult.Lines)
            {
                if (mLine.Words.Count == 0) continue;
                double top = mLine.Words.Min(w => w.BoundingRect.Top);
                double bot = mLine.Words.Max(w => w.BoundingRect.Bottom);
                string text = mLine.Text.Trim();

                var existing = mergedLines.FirstOrDefault(c => Math.Abs(c.Top - top) < 15 || Math.Abs(c.Bottom - bot) < 15);
                if (existing != null)
                {
                    existing.Text += " " + text;
                    existing.Top = Math.Min(existing.Top, top);
                    existing.Bottom = Math.Max(existing.Bottom, bot);
                }
                else mergedLines.Add(new OcrLine { Top = top, Bottom = bot, Text = text });
            }

            return mergedLines;
        }

        /// <summary>
        /// OCR 후보 라인 목록의 신뢰도를 점수화합니다.
        /// <paramref name="lines"/>는 후보 전처리에서 얻은 병합 라인 목록입니다.
        /// 채팅 포맷, 캐릭터명 일치, 본문 길이, 언어별 문자 포함 여부는 가산하고 노이즈 문자는 감산합니다.
        /// </summary>
        private int ScoreOcrCandidate(List<OcrLine> lines)
        {
            return ocrService.ScoreLines(lines, characterNames);
        }

        /// <summary>
        /// Google Translate 비공식 무료 엔드포인트를 호출해 문자열을 목표 언어로 번역합니다.
        /// <paramref name="text"/>는 번역할 OCR 후처리 문장이고,
        /// <paramref name="tLang"/>은 목표 언어 코드입니다.
        /// 반환값은 번역문이며 실패하거나 의미 없는 입력이면 빈 문자열일 수 있습니다.
        /// </summary>
        private async Task<string> CallGoogleAPI(string text, string tLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            if (!translationApiClient.CanTranslateWithGoogle(text)) return "";

            int retryCount = 3;
            string result = "";
            Exception lastException = null;

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            while (retryCount > 0)
            {
                try
                {
                    result = await translationApiClient.TranslateWithGoogleAsync(text, tLang);
                    if (string.IsNullOrEmpty(result)) throw new Exception("Google 번역 응답 파싱 실패");
                    break;
                }
                catch (Exception ex)
                {
                    lastException = ex;
                    retryCount--;
                    await Task.Delay(300);
                }
            }

            if (string.IsNullOrEmpty(result) && lastException != null)
            {
                AppendLog(translationApiErrorDescriber.DescribeGoogleFailure(lastException));
                ShowTranslationApiStatus(translationApiErrorDescriber.DescribeShortGoogleFailure(lastException));
            }

            return result;
        }

        /// <summary>
        /// 현재 API 키로 사용 가능한 Gemini 모델 목록을 조회해 로그에 남깁니다.
        /// <paramref name="apiKey"/>는 Google AI Studio에서 발급받은 Gemini API 키입니다.
        /// 시작 시 키가 있을 때만 호출되며, 실패해도 프로그램 실행은 계속됩니다.
        /// </summary>
        private async Task ListAvailableGeminiModels(string apiKey)
        {
            try
            {
                GeminiModelListApiResult result = await translationApiClient.ListGeminiModelsAsync(apiKey);
                if (result.IsSuccess)
                {
                    string modelList = string.Join(", ", result.Models);
                    AppendLog($"사용 가능한 제미나이 모델 목록: {modelList}");
                }
                else if (result.StatusCode.HasValue)
                {
                    AppendLog(translationApiErrorDescriber.DescribeGeminiModelListFailure(result));
                }
                else
                {
                    AppendLog(translationApiErrorDescriber.DescribeGeminiModelListFailure(result));
                }
            }
            catch (Exception ex) { AppendLog(translationApiErrorDescriber.DescribeGeminiModelListException(ex)); }
        }

        /// <summary>
        /// Gemini API를 호출해 OCR 오타가 포함된 게임 채팅 문장을 문맥 기반으로 번역합니다.
        /// <paramref name="text"/>는 번역할 OCR 후처리 문장,
        /// <paramref name="tLang"/>은 목표 언어 코드,
        /// <paramref name="apiKey"/>는 Gemini API 키입니다.
        /// 반환값은 Gemini가 생성한 번역문이며, 실패하면 빈 문자열을 반환해 Google fallback이 가능하게 합니다.
        /// </summary>
        private async Task<string> CallGeminiAPI(string text, string tLang, string apiKey)
        {
            string modelName = ReadGeminiModel();

            int retryCount = 2;
            string lastFailureMessage = "";
            string lastShortFailureMessage = "";
            while (retryCount > 0)
            {
                try
                {
                    GeminiTranslateApiResult result = await translationApiClient.TranslateWithGeminiAsync(text, tLang, apiKey, modelName);
                    if (!result.IsSuccess)
                    {
                        lastFailureMessage = translationApiErrorDescriber.DescribeGeminiTranslateFailure(result, modelName);
                        lastShortFailureMessage = translationApiErrorDescriber.DescribeShortGeminiTranslateFailure(result);
                        throw new Exception(lastFailureMessage);
                    }

                    string parsedText = result.Text;
                    if (string.IsNullOrWhiteSpace(parsedText))
                    {
                        lastFailureMessage = translationApiErrorDescriber.DescribeGeminiEmptyResponse(modelName);
                        lastShortFailureMessage = translationApiErrorDescriber.DescribeShortGeminiEmptyResponse();
                        throw new Exception(lastFailureMessage);
                    }
                    return parsedText;
                }
                catch (Exception ex)
                {
                    if (string.IsNullOrWhiteSpace(lastFailureMessage))
                    {
                        lastFailureMessage = translationApiErrorDescriber.DescribeGeminiException(ex, modelName);
                    }
                    if (string.IsNullOrWhiteSpace(lastShortFailureMessage))
                    {
                        lastShortFailureMessage = translationApiErrorDescriber.DescribeShortGeminiException(ex);
                    }

                    retryCount--;
                    await Task.Delay(300);
                }
            }

            if (!string.IsNullOrWhiteSpace(lastFailureMessage))
            {
                AppendLog(lastFailureMessage + " Google 무료 번역으로 전환합니다.");
                ShowTranslationApiStatus(lastShortFailureMessage);
            }

            return "";
        }
    }
}
