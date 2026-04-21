using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
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
            ShowAutoModeStatus();
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
        /// 현재 번역 엔진의 짧은 표시명을 반환합니다.
        /// 단축키 안내처럼 공간이 좁은 UI에서 사용합니다.
        /// </summary>
        private string GetCurrentTranslationEngineShortName()
        {
            return currentTranslationEngineMode switch
            {
                TranslationEngineMode.Gemini => "Gemini",
                TranslationEngineMode.LocalLlm => "Local LLM",
                _ => "Google"
            };
        }

        /// <summary>
        /// 현재 번역 엔진의 사용자 표시명을 반환합니다.
        /// 로그와 상태 문구처럼 조금 더 설명적인 위치에서 사용합니다.
        /// </summary>
        private string GetCurrentTranslationEngineDisplayName()
        {
            return currentTranslationEngineMode switch
            {
                TranslationEngineMode.Gemini => "Gemini AI 번역",
                TranslationEngineMode.LocalLlm => "Local LLM 번역",
                _ => "Google 무료 번역"
            };
        }

        /// <summary>
        /// Gemini API 키가 실제 호출을 시도할 만큼 설정되어 있는지 확인합니다.
        /// </summary>
        private bool HasUsableGeminiKey()
        {
            string geminiKey = ReadGeminiKey();
            return !string.IsNullOrWhiteSpace(geminiKey) && geminiKey.Length >= 30;
        }

        /// <summary>
        /// Ctrl+- 단축키로 Google, Gemini, Local LLM 번역 엔진을 순환 전환합니다.
        /// Gemini API 키가 없으면 Gemini 단계를 건너뛰고 Local LLM으로 넘어갑니다.
        /// </summary>
        private void ToggleEngine()
        {
            bool canUseGemini = HasUsableGeminiKey();
            currentTranslationEngineMode = currentTranslationEngineMode switch
            {
                TranslationEngineMode.Google when canUseGemini => TranslationEngineMode.Gemini,
                TranslationEngineMode.Google => TranslationEngineMode.LocalLlm,
                TranslationEngineMode.Gemini => TranslationEngineMode.LocalLlm,
                _ => TranslationEngineMode.Google
            };
            SaveTranslationEngineMode();

            ResetTranslationCache("번역 엔진 변경");
            string currentEngine = GetCurrentTranslationEngineDisplayName();

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
            public bool FastPathAttempted;
            public bool FastPathSucceeded;
            public bool FallbackAttempted;
            public string FallbackReason = "-";
        }

        /// <summary>
        /// OCR 성능 진단 결과를 세션 로그에 한 줄로 남깁니다.
        /// <paramref name="stats"/>는 단계별 누적 시간과 OCR 후보 정보를 담고,
        /// <paramref name="totalElapsedMs"/>는 번역 사이클 전체 경과 시간입니다.
        /// </summary>
        private void AppendOcrPerformanceLog(OcrPerformanceStats stats, long totalElapsedMs)
        {
            AppendLog(OcrPerformanceReportFormatter.BuildLogLine(CreateOcrPerformanceReport(stats, totalElapsedMs)));

            TrackOcrPerformanceSummary(stats, totalElapsedMs);
        }

        /// <summary>
        /// MainWindow 내부 진단 객체를 테스트 가능한 순수 성능 리포트 모델로 변환합니다.
        /// <paramref name="stats"/>는 번역 실행 중 누적한 시간/후보 선택 정보이고,
        /// <paramref name="totalElapsedMs"/>는 캡처부터 화면 출력까지의 전체 소요 시간입니다.
        /// </summary>
        private OcrPerformanceReport CreateOcrPerformanceReport(OcrPerformanceStats stats, long totalElapsedMs)
        {
            return new OcrPerformanceReport
            {
                ModeLabel = GetOcrProcessingModeLabel(stats.ProcessingMode),
                CaptureMs = stats.CaptureMs,
                ResizeMs = stats.ResizeMs,
                PreprocessMs = stats.PreprocessMs,
                CropMs = stats.CropMs,
                OcrMs = stats.OcrMs,
                ScoringMs = stats.ScoringMs,
                TranslateMs = stats.TranslateMs,
                TotalMs = totalElapsedMs,
                PreprocessCandidateCount = stats.PreprocessCandidateCount,
                OcrLanguageCallCount = stats.OcrLanguageCallCount,
                MergedLineCount = stats.MergedLineCount,
                TranslatedLineCount = stats.TranslatedLineCount,
                SkippedLineCount = stats.SkippedLineCount,
                SelectedPreprocessName = stats.SelectedPreprocessName,
                SelectedOcrLanguages = stats.SelectedOcrLanguages,
                SelectedScore = stats.SelectedScore,
                Outcome = stats.Outcome,
                FastPathAttempted = stats.FastPathAttempted,
                FastPathSucceeded = stats.FastPathSucceeded,
                FallbackAttempted = stats.FallbackAttempted,
                FallbackReason = stats.FallbackReason
            };
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
            TranslationContentMode contentMode = ReadTranslationContentMode();

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
                OcrResultCandidate bestCandidate = await SelectBestOcrCandidateAsync(resizedBitmap, threshold, processingMode, contentMode, performanceStats);

                if (bestCandidate == null || bestCandidate.Lines.Count == 0 || bestCandidate.Score <= 0)
                {
                    performanceStats.Outcome = "NoOcrCandidate";
                    return;
                }

                var ocrResults = bestCandidate.Results;
                var mergedLines = bestCandidate.Lines;
                performanceStats.SelectedPreprocessName = bestCandidate.PreprocessName;
                performanceStats.SelectedOcrLanguages = bestCandidate.SelectedLanguageCode;
                performanceStats.SelectedScore = bestCandidate.Score;
                performanceStats.MergedLineCount = mergedLines.Count;
                AppendLog("DEBUG", $"OCR 모드: {GetOcrProcessingModeLabel(processingMode)}, 전처리 선택: {bestCandidate.PreprocessName}, 언어 선택: {bestCandidate.SelectedLanguageCode} (점수 {bestCandidate.Score})", "System");

                string currentRawTextCombined = string.Join("\n", mergedLines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined)
                {
                    performanceStats.Outcome = "DuplicateOcrText";
                    return;
                }
                lastRawTextCombined = currentRawTextCombined;

                BeginTranslationResultUpdate();

                // 4. 설정한 번역 대상 방식에 맞게 Strinova 채팅 또는 OCR 전체 텍스트를 번역합니다.
                if (contentMode == TranslationContentMode.Etc)
                {
                    await TranslateEtcOcrContentAsync(mergedLines, performanceStats);
                }
                else
                {
                    foreach (var chatLine in mergedLines)
                    {
                        string krRawText = chatLine.Text.Trim();

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

                        string finalContent = PrepareFinalTranslationContent(bestMessage);
                        if (!ShouldTranslateFinalContent(finalContent))
                        {
                            performanceStats.SkippedLineCount++;
                            continue;
                        }

                        TranslationDecisionResult translationResult = await TranslateFinalContentAsync(finalContent, performanceStats, contentMode);

                        AppendLog(characterNameGold + finalContent, translationResult.TranslatedText, translationResult.EngineName);

                        AddTranslationResultToDisplay(characterNameGold, translationResult.TranslatedText);
                        performanceStats.TranslatedLineCount++;
                    }
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
        /// ETC 모드에서 OCR 병합 라인 전체를 하나의 번역 대상으로 만들어 번역창에 표시합니다.
        /// Strinova 채팅 포맷이나 characters.txt 검증을 사용하지 않습니다.
        /// </summary>
        private async Task TranslateEtcOcrContentAsync(List<OcrLine> mergedLines, OcrPerformanceStats performanceStats)
        {
            string finalContent = PrepareFinalTranslationContent(BuildEtcTranslationContent(mergedLines));
            AppendLog("DEBUG", $"OCR 원본(ETC): {finalContent}", "System");

            if (!ShouldTranslateFinalContent(finalContent))
            {
                performanceStats.SkippedLineCount += Math.Max(1, mergedLines?.Count ?? 0);
                return;
            }

            TranslationDecisionResult translationResult = await TranslateFinalContentAsync(finalContent, performanceStats, TranslationContentMode.Etc);

            AppendLog("[ETC]: " + finalContent, translationResult.TranslatedText, translationResult.EngineName);
            AddTranslationResultToDisplay("[ETC]: ", translationResult.TranslatedText);
            performanceStats.TranslatedLineCount++;
        }

        /// <summary>
        /// ETC 모드에서 OCR로 읽은 전체 라인을 빈 줄 없이 합칩니다.
        /// 숫자/기호만 있는 라인은 번역 대상에서 제외해 불필요한 API 호출을 줄입니다.
        /// </summary>
        private string BuildEtcTranslationContent(IEnumerable<OcrLine> mergedLines)
        {
            var lines = (mergedLines ?? Enumerable.Empty<OcrLine>())
                .Select(line => line?.Text?.Trim() ?? "")
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => translationPromptBuilder.CleanEtcOcrLine(text))
                .Where(translationPromptBuilder.HasMeaningfulEtcContent);

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// 선택된 번역 엔진에 맞춰 OCR 후처리 문자열을 정리합니다.
        /// Google 모드에서는 기존 노이즈 제거 규칙을 유지합니다.
        /// </summary>
        private string PrepareFinalTranslationContent(string content)
        {
            string finalContent = (content ?? "").Trim();
            if (currentTranslationEngineMode != TranslationEngineMode.Google)
            {
                return finalContent;
            }

            if (Regex.IsMatch(finalContent, @"[\u4e00-\u9fa5]") && Regex.IsMatch(finalContent, @"[ぁ-んァ-ヶ]"))
                finalContent = Regex.Replace(finalContent, @"[ぁ-んァ-ヶ]", "");

            finalContent = Regex.Replace(finalContent, @"^[0-9\W_]+", "");
            finalContent = Regex.Replace(finalContent, @"[イ尓カ幺哓ロト昌号i]", "").Trim();
            return finalContent;
        }

        /// <summary>
        /// 최종 번역 입력으로 사용할 수 있는 길이와 문자 구성을 만족하는지 확인합니다.
        /// 한 글자 입력은 한중일 문자일 때만 허용합니다.
        /// </summary>
        private bool ShouldTranslateFinalContent(string finalContent)
        {
            if (string.IsNullOrWhiteSpace(finalContent)) return false;

            if (finalContent.Length == 1)
            {
                if (Regex.IsMatch(finalContent, @"^[イ尓カ幺哓ロト昌号iI0-9\W_]$")) return false;
                return Regex.IsMatch(finalContent, @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$");
            }

            return finalContent.Length >= 2;
        }

        /// <summary>
        /// 현재 선택된 번역 엔진 정책에 따라 Google/Gemini/Local LLM 호출을 수행하고 최종 결과를 반환합니다.
        /// API 호출 시간은 OCR 성능 로그의 Translate 구간에 누적합니다.
        /// </summary>
        private async Task<TranslationDecisionResult> TranslateFinalContentAsync(string finalContent, OcrPerformanceStats performanceStats, TranslationContentMode contentMode)
        {
            string geminiKey = ReadGeminiKey();
            bool willUseGemini = currentTranslationEngineMode == TranslationEngineMode.Gemini &&
                !string.IsNullOrWhiteSpace(geminiKey);
            bool willUseLocalLlm = currentTranslationEngineMode == TranslationEngineMode.LocalLlm;
            string googleSourceLanguage = contentMode == TranslationContentMode.Etc ? "auto" : gameLang;

            TranslationPlan translationPlan = translationService.CreatePlan(
                finalContent,
                targetLang,
                currentTranslationEngineMode,
                willUseGemini,
                willUseLocalLlm);

            if (translationPlan.HasImmediateResult)
            {
                return translationPlan.ImmediateResult;
            }

            if (translationPlan.RequestKind == TranslationRequestKind.Gemini)
            {
                string modelName = ReadGeminiModel();
                Stopwatch translateStopwatch = Stopwatch.StartNew();
                string geminiTranslated = await CallGeminiAPI(finalContent, targetLang, geminiKey);
                translateStopwatch.Stop();
                performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;

                TranslationAttemptResolution geminiResolution = translationService.ResolveGeminiAttempt(geminiTranslated, modelName);
                if (!geminiResolution.RequiresGoogleFallback)
                {
                    return geminiResolution.FinalResult;
                }

                translateStopwatch.Restart();
                string googleFallback = await CallGoogleAPI(finalContent, googleSourceLanguage, targetLang);
                translateStopwatch.Stop();
                performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;
                return translationService.ResolveGoogleAttempt(googleFallback, true).FinalResult;
            }

            if (translationPlan.RequestKind == TranslationRequestKind.LocalLlm)
            {
                string localLlmModel = ReadLocalLlmModel();
                Stopwatch translateStopwatch = Stopwatch.StartNew();
                string localLlmTranslated = await CallLocalLlmAPI(finalContent, targetLang);
                translateStopwatch.Stop();
                performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;

                TranslationAttemptResolution localLlmResolution = translationService.ResolveLocalLlmAttempt(localLlmTranslated, localLlmModel);
                if (!localLlmResolution.RequiresGoogleFallback)
                {
                    return localLlmResolution.FinalResult;
                }

                translateStopwatch.Restart();
                string googleFallback = await CallGoogleAPI(finalContent, googleSourceLanguage, targetLang);
                translateStopwatch.Stop();
                performanceStats.TranslateMs += translateStopwatch.ElapsedMilliseconds;
                return translationService.ResolveGoogleAttempt(googleFallback, true, "Local LLM").FinalResult;
            }

            Stopwatch googleStopwatch = Stopwatch.StartNew();
            string googleTranslated = await CallGoogleAPI(finalContent, googleSourceLanguage, targetLang);
            googleStopwatch.Stop();
            performanceStats.TranslateMs += googleStopwatch.ElapsedMilliseconds;
            return translationService.ResolveGoogleAttempt(googleTranslated, false).FinalResult;
        }

        /// <summary>
        /// OCR 처리 모드에 맞춰 전처리 후보와 OCR 언어 범위를 선택하고 최종 후보를 반환합니다.
        /// <paramref name="resizedBitmap"/>은 캡처 후 설정 배율로 확대한 이미지,
        /// <paramref name="threshold"/>는 색상/밝기 기반 이진화 기준값,
        /// <paramref name="processingMode"/>는 빠름/자동/정확 중 이번 실행 전략입니다.
        /// <paramref name="contentMode"/>는 Strinova 채팅 검증 또는 OCR 전체 번역 여부입니다.
        /// <paramref name="performanceStats"/>는 후보 선택 과정의 단계별 시간을 누적하는 진단 객체입니다.
        /// </summary>
        private async Task<OcrResultCandidate> SelectBestOcrCandidateAsync(Bitmap resizedBitmap, int threshold, OcrProcessingMode processingMode, TranslationContentMode contentMode, OcrPerformanceStats performanceStats)
        {
            foreach (OcrEvaluationStep step in ocrService.CreateEvaluationPlan(processingMode))
            {
                OcrResultCandidate candidate = await EvaluateOcrCandidatesAsync(
                    resizedBitmap,
                    threshold,
                    contentMode,
                    performanceStats,
                    step.RecognizeAllLanguages,
                    step.PreprocessKinds.ToArray());

                if (step.IsFastPathStep)
                {
                    performanceStats.FastPathAttempted = true;

                    if (ocrService.IsFastPathSuccess(candidate, characterNames, contentMode))
                    {
                        performanceStats.FastPathSucceeded = true;
                        return candidate;
                    }

                    if (processingMode == OcrProcessingMode.Fast)
                    {
                        performanceStats.FallbackReason = "FastPathFailedNoFallback";
                        return null;
                    }

                    performanceStats.FallbackAttempted = true;
                    performanceStats.FallbackReason = "FastPathFailed";
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
        /// <paramref name="contentMode"/>는 OCR 후보 점수화에서 채팅 포맷을 강제할지 여부입니다.
        /// <paramref name="performanceStats"/>는 전처리/OCR/점수화 시간을 누적하는 진단 객체입니다.
        /// <paramref name="recognizeAllLanguages"/>가 true이면 설치된 모든 OCR 언어를 실행하고 false이면 게임 언어만 실행합니다.
        /// <paramref name="preprocessKinds"/>는 평가할 전처리 후보 목록입니다.
        /// </summary>
        private async Task<OcrResultCandidate> EvaluateOcrCandidatesAsync(Bitmap resizedBitmap, int threshold, TranslationContentMode contentMode, OcrPerformanceStats performanceStats, bool recognizeAllLanguages, params OcrPreprocessKind[] preprocessKinds)
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
                    Dictionary<string, List<OcrLine>> mergedLinesByLanguage = candidateResults
                        .Where(kvp => kvp.Value != null)
                        .ToDictionary(kvp => kvp.Key, kvp => MergeOcrLines(kvp.Value));

                    OcrLanguageSelection selectedLanguage = SelectOcrLanguageSelection(
                        candidateResults,
                        mergedLinesByLanguage,
                        contentMode);

                    if (selectedLanguage == null || selectedLanguage.Lines.Count == 0)
                    {
                        scoringStopwatch.Stop();
                        performanceStats.ScoringMs += scoringStopwatch.ElapsedMilliseconds;
                        continue;
                    }

                    List<OcrLine> candidateLines = selectedLanguage.Lines;
                    int candidateScore = selectedLanguage.Score;
                    scoringStopwatch.Stop();
                    performanceStats.ScoringMs += scoringStopwatch.ElapsedMilliseconds;

                    bestCandidate = ocrService.SelectHigherScore(bestCandidate, new OcrResultCandidate
                    {
                        PreprocessName = preprocessedImage.Name,
                        SelectedLanguageCode = selectedLanguage.LanguageCode,
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
        /// 이번 OCR 후보에서 실제 번역 대상으로 삼을 기준 언어 결과를 선택합니다.
        /// Strinova는 기존처럼 게임 언어/한국어 우선 전략을 유지하고, ETC는 언어별 OCR 결과를 읽기 점수로 비교합니다.
        /// </summary>
        private OcrLanguageSelection SelectOcrLanguageSelection(
            Dictionary<string, OcrResult> candidateResults,
            Dictionary<string, List<OcrLine>> mergedLinesByLanguage,
            TranslationContentMode contentMode)
        {
            if (contentMode == TranslationContentMode.Etc)
            {
                // ETC는 "선택 점수"와 "실제 번역 입력"이 어긋나지 않게,
                // 언어 선택에 사용한 cleaned lines 자체를 이후 번역 입력으로 그대로 넘깁니다.
                OcrLanguageSelection cleanedSelection = ocrService.SelectBestLanguageSelection(
                    (mergedLinesByLanguage ?? new Dictionary<string, List<OcrLine>>())
                        .Select(kvp => new OcrLanguageCandidate
                        {
                            LanguageCode = kvp.Key,
                            Lines = BuildCleanedEtcLanguageLines(kvp.Value)
                        }),
                    characterNames,
                    TranslationContentMode.Etc);

                if (cleanedSelection == null ||
                    string.IsNullOrWhiteSpace(cleanedSelection.LanguageCode))
                {
                    return null;
                }

                return cleanedSelection;
            }

            string selectedLanguageCode = GetPreferredMasterLanguageCode(candidateResults);
            if (string.IsNullOrWhiteSpace(selectedLanguageCode) ||
                mergedLinesByLanguage == null ||
                !mergedLinesByLanguage.TryGetValue(selectedLanguageCode, out List<OcrLine> lines))
            {
                return null;
            }

            return new OcrLanguageSelection(
                selectedLanguageCode,
                lines,
                ScoreOcrCandidate(lines, contentMode));
        }

        /// <summary>
        /// ETC 모드 언어 선택 점수 비교 전에 각 언어별 OCR 라인을 전처리 결과 기준으로 정리합니다.
        /// 실제 번역 입력과 같은 필터를 써서, 노이즈가 심한 언어 결과가 선택 점수에서 과대평가되지 않게 합니다.
        /// </summary>
        private List<OcrLine> BuildCleanedEtcLanguageLines(IEnumerable<OcrLine> lines)
        {
            return (lines ?? Enumerable.Empty<OcrLine>())
                .Select(line => new OcrLine
                {
                    Top = line?.Top ?? 0,
                    Bottom = line?.Bottom ?? 0,
                    Text = translationPromptBuilder.CleanEtcOcrLine(line?.Text?.Trim() ?? "")
                })
                .Where(line => translationPromptBuilder.HasMeaningfulEtcContent(line.Text))
                .ToList();
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
        /// Strinova 모드에서 우선 사용할 기준 OCR 언어 코드를 반환합니다.
        /// 게임 언어를 우선하고, 없으면 한국어를 fallback으로 사용합니다.
        /// </summary>
        private string GetPreferredMasterLanguageCode(Dictionary<string, OcrResult> ocrResults)
        {
            if (ocrResults == null) return "";
            if (ocrResults.ContainsKey(gameLang)) return gameLang;
            if (ocrResults.ContainsKey("ko")) return "ko";
            return "";
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
        /// <paramref name="contentMode"/>가 ETC이면 채팅 포맷 대신 일반 텍스트 가독성을 기준으로 점수화합니다.
        /// 채팅 포맷, 캐릭터명 일치, 본문 길이, 언어별 문자 포함 여부는 가산하고 노이즈 문자는 감산합니다.
        /// </summary>
        private int ScoreOcrCandidate(List<OcrLine> lines, TranslationContentMode contentMode)
        {
            return ocrService.ScoreLines(lines, characterNames, contentMode);
        }

        /// <summary>
        /// Google Translate 비공식 무료 엔드포인트를 호출해 문자열을 목표 언어로 번역합니다.
        /// <paramref name="text"/>는 번역할 OCR 후처리 문장이고,
        /// <paramref name="sourceLang"/>은 게임 원문 언어 코드입니다.
        /// <paramref name="tLang"/>은 목표 언어 코드입니다.
        /// 반환값은 번역문이며 실패하거나 의미 없는 입력이면 빈 문자열일 수 있습니다.
        /// </summary>
        private async Task<string> CallGoogleAPI(string text, string sourceLang, string tLang)
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
                    result = await translationApiClient.TranslateWithGoogleAsync(text, sourceLang, tLang);
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
        /// LM Studio/OpenAI 호환 로컬 LLM 서버를 호출해 OCR 오타가 포함된 게임 채팅을 번역합니다.
        /// endpoint/model/timeout/max tokens는 config.ini 설정값을 사용하며, 실패하면 빈 문자열을 반환해 Google fallback을 유도합니다.
        /// </summary>
        private async Task<string> CallLocalLlmAPI(string text, string tLang)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            string endpoint = ReadLocalLlmEndpoint();
            string modelName = ReadLocalLlmModel();
            int timeoutSeconds = ReadLocalLlmTimeoutSeconds();
            int maxTokens = ReadLocalLlmMaxTokens();

            string lastFailureMessage = "";
            string lastShortFailureMessage = "";

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                LocalLlmTranslateApiResult result = await translationApiClient.TranslateWithLocalLlmAsync(
                    text,
                    tLang,
                    endpoint,
                    modelName,
                    0.1,
                    maxTokens,
                    cts.Token);

                if (!result.IsSuccess)
                {
                    lastFailureMessage = translationApiErrorDescriber.DescribeLocalLlmTranslateFailure(result, endpoint, modelName);
                    lastShortFailureMessage = translationApiErrorDescriber.DescribeShortLocalLlmTranslateFailure(result);
                    return "";
                }

                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    lastFailureMessage = translationApiErrorDescriber.DescribeLocalLlmEmptyResponse(endpoint, modelName);
                    lastShortFailureMessage = translationApiErrorDescriber.DescribeShortLocalLlmEmptyResponse();
                    return "";
                }

                return result.Text;
            }
            catch (OperationCanceledException ex)
            {
                lastFailureMessage = translationApiErrorDescriber.DescribeLocalLlmException(
                    new TimeoutException($"{timeoutSeconds}초 안에 응답하지 않았습니다.", ex),
                    endpoint,
                    modelName);
                lastShortFailureMessage = "Local LLM 시간 초과: Google 전환";
                return "";
            }
            catch (Exception ex)
            {
                lastFailureMessage = translationApiErrorDescriber.DescribeLocalLlmException(ex, endpoint, modelName);
                lastShortFailureMessage = translationApiErrorDescriber.DescribeShortLocalLlmException(ex);
                return "";
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(lastFailureMessage))
                {
                    AppendLog(lastFailureMessage + " Google 무료 번역으로 전환합니다.");
                    ShowTranslationApiStatus(lastShortFailureMessage);
                }
            }
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
