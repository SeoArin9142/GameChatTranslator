using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
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
using PixelFormat = System.Drawing.Imaging.PixelFormat;

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
        /// 실제 OCR 후보 실행 범위를 결정하는 내부 처리 모드입니다.
        /// 수동 번역은 항상 Accurate를 사용하고, 자동 번역은 AutoTranslateMode에서 변환됩니다.
        /// </summary>
        private enum OcrProcessingMode
        {
            Fast,
            Auto,
            Accurate
        }

        /// <summary>
        /// 캡처 이미지를 OCR에 넣기 전 적용할 전처리 후보 종류입니다.
        /// Color는 기본 색상 필터, ColorThick은 글자 굵기 보정, Adaptive는 로컬 밝기 기반 이진화입니다.
        /// </summary>
        private enum OcrPreprocessKind
        {
            Color,
            ColorThick,
            Adaptive
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
        /// OCR 결과의 한 줄을 병합한 내부 모델입니다.
        /// Top/Bottom은 화면상 세로 위치이고, Text는 병합된 OCR 문자열입니다.
        /// </summary>
        private class MergedLine
        {
            public double Top;
            public double Bottom;
            public string Text;
        }

        /// <summary>
        /// 전처리된 OCR 입력 이미지를 이름과 함께 관리하는 disposable 모델입니다.
        /// Bitmap은 unmanaged 리소스를 포함하므로 사용 후 Dispose로 해제합니다.
        /// </summary>
        private class PreprocessedOcrImage : IDisposable
        {
            public string Name;
            public Bitmap Bitmap;

            /// <summary>
            /// 전처리 이미지 Bitmap 리소스를 해제합니다.
            /// </summary>
            public void Dispose()
            {
                Bitmap?.Dispose();
            }
        }

        /// <summary>
        /// 하나의 전처리 후보에 대해 OCR 결과, 병합 라인, 품질 점수를 묶어 보관합니다.
        /// 후보 간 Score를 비교해 최종 번역에 사용할 OCR 결과를 선택합니다.
        /// </summary>
        private class OcrCandidate
        {
            public string PreprocessName;
            public Dictionary<string, OcrResult> Results;
            public List<MergedLine> Lines;
            public int Score;
        }

        /// <summary>
        /// 번역 대상 문장이 이미 목표 언어인지 간단한 문자 범위 검사로 판단합니다.
        /// <paramref name="text"/>는 번역 전 원문 또는 OCR 후처리된 문자열,
        /// <paramref name="tLang"/>은 목표 언어 코드입니다. 예: ko, en-US, ja.
        /// 반환값이 true이면 API 번역 호출을 생략할 수 있습니다.
        /// </summary>
        private bool IsSameLanguage(string text, string tLang)
        {
            if (tLang == "ko" && Regex.IsMatch(text, @"[가-힣]{2,}")) return true;
            if (tLang == "ru" && Regex.IsMatch(text, @"[а-яА-ЯёЁ]")) return true;
            if (tLang == "ja" && Regex.IsMatch(text, @"[ぁ-んァ-ヶ]")) return true;
            if (tLang == "zh-Hans-CN" && Regex.IsMatch(text, @"[\u4e00-\u9fa5]")) return true;
            if (tLang == "en-US" && Regex.IsMatch(text, @"[a-zA-Z]") && !Regex.IsMatch(text, @"[가-힣а-яА-ЯёЁぁ-んァ-ヶ\u4e00-\u9fa5]")) return true;
            return false;
        }

        /// <summary>
        /// 내부 언어 코드를 Google Translate API가 요구하는 언어 코드로 변환합니다.
        /// <paramref name="lang"/>은 앱 내부에서 쓰는 언어 코드입니다.
        /// 반환값은 Google API의 tl 파라미터에 넣을 코드입니다.
        /// </summary>
        private string GetGoogleTransLangCode(string lang)
        {
            if (lang == "zh-Hans-CN") return "zh-CN";
            if (lang == "en-US") return "en";
            return lang;
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

            int threshold = int.TryParse(ini.Read("Threshold"), out int t) ? t : 120;
            int scaleFactor = int.TryParse(ini.Read("ScaleFactor"), out int s) ? s : 3;
            if (scaleFactor < 1) scaleFactor = 1;
            if (scaleFactor > 4) scaleFactor = 4;

            try
            {
                // 1. 저장된 채팅 영역을 실제 화면 픽셀 기준으로 캡처합니다.
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

                // 2. OCR 인식률 향상을 위해 설정된 배율만큼 이미지를 확대합니다.
                int newWidth = rawBitmap.Width * scaleFactor;
                int newHeight = rawBitmap.Height * scaleFactor;
                using Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawBitmap, 0, 0, newWidth, newHeight);
                }

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
                OcrCandidate bestCandidate = await SelectBestOcrCandidateAsync(resizedBitmap, threshold, processingMode);

                if (bestCandidate == null || bestCandidate.Lines.Count == 0 || bestCandidate.Score <= 0) return;

                var ocrResults = bestCandidate.Results;
                var mergedLines = bestCandidate.Lines;
                AppendLog("DEBUG", $"OCR 모드: {GetOcrProcessingModeLabel(processingMode)}, 전처리 선택: {bestCandidate.PreprocessName} (점수 {bestCandidate.Score})", "System");

                string currentRawTextCombined = string.Join("\n", mergedLines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined) return;
                lastRawTextCombined = currentRawTextCombined;

                TxtResult.Inlines.Clear();
                ResetClipboardTranslationText();

                // 4. OCR에서 검증된 채팅 줄만 번역하고 UI/클립보드/로그에 반영합니다.
                foreach (var chatLine in mergedLines)
                {
                    string krRawText = chatLine.Text.Trim();
                    string usedEngine = "None";

                    AppendLog("DEBUG", $"OCR 원본: {krRawText}", "System");

                    if (!Regex.IsMatch(krRawText, @"[a-zA-Z가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]"))
                    {
                        AppendLog("DEBUG", "글자 없음으로 스킵", "System");
                        continue;
                    }

                    var strictMatch = Regex.Match(krRawText, @"^(.*[\[\(]([^\]\)]+)[\]\)]\s*[:;：!])\s*(.*)$");
                    if (!strictMatch.Success)
                    {
                        AppendLog("DEBUG", "정규식(strictMatch) 불일치로 스킵됨", "System");
                        continue;
                    }

                    string characterNameOnly = strictMatch.Groups[2].Value.Trim();

                    if (!characterNames.Contains(characterNameOnly))
                    {
                        AppendLog("DEBUG", $"리스트에 없는 이름이라 스킵됨: {characterNameOnly}", "System");
                        continue;
                    }

                    string characterNameGold = $"[{characterNameOnly}]: ";
                    string bestMessage = strictMatch.Groups[3].Value.Trim();
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

                    if (string.IsNullOrWhiteSpace(finalContent)) continue;

                    // 🌟 [1글자 채팅 예외처리 포함]
                    if (finalContent.Length == 1)
                    {
                        if (Regex.IsMatch(finalContent, @"^[イ尓カ幺哓ロト昌号iI0-9\W_]$")) continue;
                        if (!Regex.IsMatch(finalContent, @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$")) continue;
                    }
                    else if (finalContent.Length < 2) continue;

                    string modelName = ReadGeminiModel();
                    usedEngine = "Google";
                    string translated = finalContent;

                    if (!Regex.IsMatch(finalContent, @"^[0-9\W]+$"))
                    {
                        if (IsSameLanguage(finalContent, targetLang))
                        {
                            translated = finalContent;
                            usedEngine = "Skip";
                        }
                        else
                        {
                            // 🌟 이제 제미나이 키가 있어도, 상태(useGeminiEngine)가 켜져 있을 때만 사용!
                            if (willUseGemini)
                            {
                                translated = await CallGeminiAPI(finalContent, targetLang, geminiKey);
                                usedEngine = $"Gemini {modelName}";

                                if (string.IsNullOrEmpty(translated))
                                {
                                    translated = await CallGoogleAPI(finalContent, targetLang);
                                    translated = "[Gemini 에러 - 구글 전환됨] " + translated;
                                    usedEngine = "Google (Fallback)";
                                }
                            }
                            else
                            {
                                // 스위치를 꺼두면 무조건 구글님 호출
                                translated = await CallGoogleAPI(finalContent, targetLang);
                                usedEngine = "Google";
                            }
                        }
                    }

                    AppendLog(characterNameGold + finalContent, translated, usedEngine);

                    TxtResult.Inlines.Add(new Run(characterNameGold) { Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                    TxtResult.Inlines.Add(new Run(translated) { Foreground = Brushes.White });
                    TxtResult.Inlines.Add(new LineBreak());
                    AddClipboardTranslationLine(characterNameGold, translated);
                }
            }
            catch (Exception ex) { TxtResult.Text = "에러: " + ex.Message; }
            finally { isTranslating = false; }
        }

        /// <summary>
        /// OCR 처리 모드에 맞춰 전처리 후보와 OCR 언어 범위를 선택하고 최종 후보를 반환합니다.
        /// <paramref name="resizedBitmap"/>은 캡처 후 설정 배율로 확대한 이미지,
        /// <paramref name="threshold"/>는 색상/밝기 기반 이진화 기준값,
        /// <paramref name="processingMode"/>는 빠름/자동/정확 중 이번 실행 전략입니다.
        /// </summary>
        private async Task<OcrCandidate> SelectBestOcrCandidateAsync(Bitmap resizedBitmap, int threshold, OcrProcessingMode processingMode)
        {
            if (processingMode == OcrProcessingMode.Fast)
            {
                OcrCandidate fastCandidate = await EvaluateOcrCandidatesAsync(
                    resizedBitmap,
                    threshold,
                    recognizeAllLanguages: false,
                    OcrPreprocessKind.Color);

                return IsFastPathSuccess(fastCandidate) ? fastCandidate : null;
            }

            if (processingMode == OcrProcessingMode.Auto)
            {
                OcrCandidate fastCandidate = await EvaluateOcrCandidatesAsync(
                    resizedBitmap,
                    threshold,
                    recognizeAllLanguages: false,
                    OcrPreprocessKind.Color);

                if (IsFastPathSuccess(fastCandidate))
                {
                    return fastCandidate;
                }

                OcrCandidate fallbackCandidate = await EvaluateOcrCandidatesAsync(
                    resizedBitmap,
                    threshold,
                    recognizeAllLanguages: true,
                    OcrPreprocessKind.ColorThick,
                    OcrPreprocessKind.Adaptive);

                return fallbackCandidate;
            }

            return await EvaluateOcrCandidatesAsync(
                resizedBitmap,
                threshold,
                recognizeAllLanguages: true,
                OcrPreprocessKind.Color,
                OcrPreprocessKind.ColorThick,
                OcrPreprocessKind.Adaptive);
        }

        /// <summary>
        /// 지정된 전처리 후보들을 실제 Bitmap으로 만들고 OCR을 수행한 뒤 가장 높은 점수의 후보를 고릅니다.
        /// <paramref name="resizedBitmap"/>은 OCR 전처리 입력 이미지,
        /// <paramref name="threshold"/>는 색상 필터와 적응형 이진화의 기준값,
        /// <paramref name="recognizeAllLanguages"/>가 true이면 설치된 모든 OCR 언어를 실행하고 false이면 게임 언어만 실행합니다.
        /// <paramref name="preprocessKinds"/>는 평가할 전처리 후보 목록입니다.
        /// </summary>
        private async Task<OcrCandidate> EvaluateOcrCandidatesAsync(Bitmap resizedBitmap, int threshold, bool recognizeAllLanguages, params OcrPreprocessKind[] preprocessKinds)
        {
            OcrCandidate bestCandidate = null;
            List<PreprocessedOcrImage> preprocessedImages = CreatePreprocessedOcrImages(resizedBitmap, threshold, preprocessKinds);

            try
            {
                foreach (PreprocessedOcrImage preprocessedImage in preprocessedImages)
                {
                    using Bitmap croppedBitmap = CropForOcr(preprocessedImage.Bitmap);

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
                    Dictionary<string, OcrResult> candidateResults = await RecognizeLanguagesAsync(croppedBitmap, recognizeAllLanguages);
                    OcrResult candidateMasterResult = SelectMasterOcrResult(candidateResults);
                    if (candidateMasterResult == null) continue;

                    List<MergedLine> candidateLines = MergeOcrLines(candidateMasterResult);
                    int candidateScore = ScoreOcrCandidate(candidateLines);

                    if (bestCandidate == null || candidateScore > bestCandidate.Score)
                    {
                        bestCandidate = new OcrCandidate
                        {
                            PreprocessName = preprocessedImage.Name,
                            Results = candidateResults,
                            Lines = candidateLines,
                            Score = candidateScore
                        };
                    }
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
        /// 빠른 경로 결과가 바로 번역해도 될 정도로 신뢰 가능한지 판단합니다.
        /// <paramref name="candidate"/>는 Color 전처리 + 게임 언어 OCR만 수행한 후보입니다.
        /// 반환값은 캐릭터명과 채팅 포맷이 모두 확인된 경우에만 true입니다.
        /// </summary>
        private bool IsFastPathSuccess(OcrCandidate candidate)
        {
            if (candidate == null || candidate.Score <= 0 || candidate.Lines == null) return false;

            return candidate.Lines.Any(line =>
                TryExtractKnownCharacterChatLine(line.Text, out _, out string message) &&
                !string.IsNullOrWhiteSpace(message));
        }

        /// <summary>
        /// OCR 문자열이 "[캐릭터명]: 내용" 형식이고 캐릭터 목록에 있는 이름인지 검사합니다.
        /// <paramref name="rawText"/>는 OCR에서 얻은 한 줄 원문,
        /// <paramref name="characterNameOnly"/>는 성공 시 추출된 캐릭터명,
        /// <paramref name="message"/>는 성공 시 추출된 채팅 본문입니다.
        /// </summary>
        private bool TryExtractKnownCharacterChatLine(string rawText, out string characterNameOnly, out string message)
        {
            characterNameOnly = "";
            message = "";

            string text = rawText?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return false;

            Match strictMatch = Regex.Match(text, @"^(.*[\[\(]([^\]\)]+)[\]\)]\s*[:;：!])\s*(.*)$");
            if (!strictMatch.Success) return false;

            characterNameOnly = strictMatch.Groups[2].Value.Trim();
            message = strictMatch.Groups[3].Value.Trim();

            return characterNames.Contains(characterNameOnly) && !string.IsNullOrWhiteSpace(message);
        }

        /// <summary>
        /// 요청된 전처리 후보 종류만 생성해 OCR 입력 Bitmap 목록을 만듭니다.
        /// <paramref name="source"/>는 확대된 원본 캡처 이미지,
        /// <paramref name="threshold"/>는 색상/밝기 판단 기준값,
        /// <paramref name="preprocessKinds"/>는 만들 전처리 후보 종류 목록입니다.
        /// 반환값의 Bitmap은 호출자가 Dispose해야 합니다.
        /// </summary>
        private List<PreprocessedOcrImage> CreatePreprocessedOcrImages(Bitmap source, int threshold, params OcrPreprocessKind[] preprocessKinds)
        {
            int width = source.Width;
            int height = source.Height;
            byte[] pixels = ReadBitmapPixels(source, out int stride);

            if (preprocessKinds == null || preprocessKinds.Length == 0)
            {
                preprocessKinds = new[] { OcrPreprocessKind.Color, OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive };
            }

            byte[] colorMask = null;
            var images = new List<PreprocessedOcrImage>();

            foreach (OcrPreprocessKind kind in preprocessKinds.Distinct())
            {
                if (kind == OcrPreprocessKind.Color)
                {
                    colorMask ??= CreateColorMask(pixels, stride, width, height, threshold);
                    images.Add(new PreprocessedOcrImage { Name = "Color", Bitmap = CreateBitmapFromMask(colorMask, width, height) });
                }
                else if (kind == OcrPreprocessKind.ColorThick)
                {
                    colorMask ??= CreateColorMask(pixels, stride, width, height, threshold);
                    byte[] colorThickMask = DilateMask(colorMask, width, height);
                    images.Add(new PreprocessedOcrImage { Name = "ColorThick", Bitmap = CreateBitmapFromMask(colorThickMask, width, height) });
                }
                else if (kind == OcrPreprocessKind.Adaptive)
                {
                    byte[] adaptiveMask = CreateAdaptiveThresholdMask(pixels, stride, width, height, threshold);
                    byte[] adaptiveCleanMask = DilateMask(RemoveIsolatedWhitePixels(adaptiveMask, width, height), width, height);
                    images.Add(new PreprocessedOcrImage { Name = "Adaptive", Bitmap = CreateBitmapFromMask(adaptiveCleanMask, width, height) });
                }
            }

            return images;
        }

        /// <summary>
        /// Bitmap의 픽셀 데이터를 32bpp ARGB byte 배열로 복사합니다.
        /// <paramref name="source"/>는 읽을 Bitmap,
        /// <paramref name="stride"/>는 반환되는 한 행의 byte 길이입니다.
        /// 반환 배열은 B,G,R,A 순서의 픽셀 데이터를 담습니다.
        /// </summary>
        private byte[] ReadBitmapPixels(Bitmap source, out int stride)
        {
            BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * source.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        /// <summary>
        /// 흰색 채팅 글자와 노란색 캐릭터명을 보존하는 기본 색상 마스크를 생성합니다.
        /// <paramref name="pixels"/>는 ReadBitmapPixels로 읽은 원본 픽셀 배열,
        /// <paramref name="stride"/>는 한 행의 byte 길이,
        /// <paramref name="width"/>와 <paramref name="height"/>는 이미지 크기,
        /// <paramref name="threshold"/>는 밝기 판단 기준입니다.
        /// 반환값은 흰 픽셀 255, 배경 0으로 구성된 1채널 마스크입니다.
        /// </summary>
        private byte[] CreateColorMask(byte[] pixels, int stride, int width, int height, int threshold)
        {
            byte[] mask = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int maskOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x * 4;
                    byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    int diff = max - min;

                    // 흰 채팅 글자: 밝고 채도가 낮은 픽셀을 우선 보존합니다.
                    bool isWhite = max > threshold &&
                                   min > Math.Max(0, threshold - 18) &&
                                   (diff < 42 || (max > 0 && diff * 100 / max < 24));

                    // 노란 닉네임: RGB 조건에 채도 조건을 추가해 밝은 배경과 구분합니다.
                    bool isYellow = r > threshold &&
                                    g > Math.Max(0, threshold - 45) &&
                                    b < 125 &&
                                    r + g > b * 3 &&
                                    Math.Abs(r - g) < 95;

                    mask[maskOffset + x] = (isWhite || isYellow) ? (byte)255 : (byte)0;
                }
            }

            return mask;
        }

        /// <summary>
        /// 주변 밝기 평균과 현재 픽셀 밝기를 비교해 배경 변화에 강한 적응형 이진화 마스크를 만듭니다.
        /// <paramref name="pixels"/>는 원본 픽셀 배열,
        /// <paramref name="stride"/>는 한 행의 byte 길이,
        /// <paramref name="width"/>와 <paramref name="height"/>는 이미지 크기,
        /// <paramref name="threshold"/>는 최소 밝기 기준입니다.
        /// </summary>
        private byte[] CreateAdaptiveThresholdMask(byte[] pixels, int stride, int width, int height, int threshold)
        {
            byte[] gray = new byte[width * height];
            long[] integral = new long[(width + 1) * (height + 1)];

            for (int y = 0; y < height; y++)
            {
                long rowSum = 0;
                int rowOffset = y * stride;

                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x * 4;
                    int value = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
                    gray[y * width + x] = (byte)value;
                    rowSum += value;
                    integral[(y + 1) * (width + 1) + x + 1] = integral[y * (width + 1) + x + 1] + rowSum;
                }
            }

            byte[] mask = new byte[width * height];
            int radius = Math.Max(10, Math.Min(28, Math.Min(width, height) / 24));
            int offset = 16;
            int minAbsolute = Math.Max(70, threshold - 42);

            for (int y = 0; y < height; y++)
            {
                int y1 = Math.Max(0, y - radius);
                int y2 = Math.Min(height - 1, y + radius);

                for (int x = 0; x < width; x++)
                {
                    int x1 = Math.Max(0, x - radius);
                    int x2 = Math.Min(width - 1, x + radius);

                    int area = (x2 - x1 + 1) * (y2 - y1 + 1);
                    long sum = integral[(y2 + 1) * (width + 1) + x2 + 1]
                               - integral[y1 * (width + 1) + x2 + 1]
                               - integral[(y2 + 1) * (width + 1) + x1]
                               + integral[y1 * (width + 1) + x1];

                    int localAverage = (int)(sum / area);
                    int current = gray[y * width + x];

                    mask[y * width + x] = current >= minAbsolute && current > localAverage + offset ? (byte)255 : (byte)0;
                }
            }

            return mask;
        }

        /// <summary>
        /// 흰색 픽셀 주변 1픽셀 영역을 확장해 얇은 글자를 굵게 보정합니다.
        /// <paramref name="mask"/>는 0/255로 구성된 입력 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 마스크 크기입니다.
        /// 반환값은 글자가 한 픽셀 정도 두꺼워진 새 마스크입니다.
        /// </summary>
        private byte[] DilateMask(byte[] mask, int width, int height)
        {
            byte[] result = new byte[mask.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool hasWhite = false;

                    for (int dy = -1; dy <= 1 && !hasWhite; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= width) continue;

                            if (mask[yy * width + xx] == 255)
                            {
                                hasWhite = true;
                                break;
                            }
                        }
                    }

                    result[y * width + x] = hasWhite ? (byte)255 : (byte)0;
                }
            }

            return result;
        }

        /// <summary>
        /// 주변 흰색 픽셀이 거의 없는 고립 노이즈 픽셀을 제거합니다.
        /// <paramref name="mask"/>는 0/255로 구성된 입력 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 마스크 크기입니다.
        /// </summary>
        private byte[] RemoveIsolatedWhitePixels(byte[] mask, int width, int height)
        {
            byte[] result = new byte[mask.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (mask[index] == 0) continue;

                    int neighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int xx = x + dx;
                            if (xx < 0 || xx >= width) continue;
                            if (mask[yy * width + xx] == 255) neighbors++;
                        }
                    }

                    result[index] = neighbors >= 2 ? (byte)255 : (byte)0;
                }
            }

            return result;
        }

        /// <summary>
        /// 0/255 마스크를 Windows OCR이 읽을 수 있는 32bpp ARGB Bitmap으로 변환합니다.
        /// <paramref name="mask"/>는 width*height 길이의 1채널 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 생성할 Bitmap 크기입니다.
        /// </summary>
        private Bitmap CreateBitmapFromMask(byte[] mask, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * height];

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    int maskOffset = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        byte value = mask[maskOffset + x];
                        int i = rowOffset + x * 4;
                        pixels[i] = value;
                        pixels[i + 1] = value;
                        pixels[i + 2] = value;
                        pixels[i + 3] = 255;
                    }
                }

                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
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
        /// 반환값은 언어 코드별 OCR 결과 딕셔너리입니다.
        /// </summary>
        private async Task<Dictionary<string, OcrResult>> RecognizeLanguagesAsync(Bitmap bitmap, bool recognizeAllLanguages)
        {
            using MemoryStream ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            ms.Position = 0;
            var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
            using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

            var ocrResults = new Dictionary<string, OcrResult>();
            foreach (var kvp in SelectOcrEngines(recognizeAllLanguages))
            {
                ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
            }

            return ocrResults;
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
        /// 반환값은 채팅 한 줄 단위에 가깝게 병합된 MergedLine 목록입니다.
        /// </summary>
        private List<MergedLine> MergeOcrLines(OcrResult masterResult)
        {
            var mergedLines = new List<MergedLine>();

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
                else mergedLines.Add(new MergedLine { Top = top, Bottom = bot, Text = text });
            }

            return mergedLines;
        }

        /// <summary>
        /// OCR 후보 라인 목록의 신뢰도를 점수화합니다.
        /// <paramref name="lines"/>는 후보 전처리에서 얻은 병합 라인 목록입니다.
        /// 채팅 포맷, 캐릭터명 일치, 본문 길이, 언어별 문자 포함 여부는 가산하고 노이즈 문자는 감산합니다.
        /// </summary>
        private int ScoreOcrCandidate(List<MergedLine> lines)
        {
            int score = 0;

            foreach (MergedLine line in lines)
            {
                string text = line.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                int letterCount = Regex.Matches(text, @"[a-zA-Z가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]").Count;
                int noiseCount = Regex.Matches(text, @"[^a-zA-Z0-9가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\[\]\(\):;：!\.,\?\-]").Count;
                score += letterCount * 2;
                score -= noiseCount * 18;

                Match strictMatch = Regex.Match(text, @"^(.*[\[\(]([^\]\)]+)[\]\)]\s*[:;：!])\s*(.*)$");
                if (!strictMatch.Success)
                {
                    score -= 80;
                    continue;
                }

                string characterNameOnly = strictMatch.Groups[2].Value.Trim();
                string message = strictMatch.Groups[3].Value.Trim();

                if (characterNames.Contains(characterNameOnly))
                {
                    score += 10000;
                    score += Math.Min(message.Length, 80) * 40;
                }
                else
                {
                    score -= 200;
                }

                if (Regex.IsMatch(message, @"[\u4e00-\u9fa5]")) score += 400;
                if (Regex.IsMatch(message, @"[a-zA-Z]{2,}")) score += 300;
                if (Regex.IsMatch(message, @"[ぁ-んァ-ヶ]")) score += 200;
                if (Regex.IsMatch(message, @"[а-яА-ЯёЁ]")) score += 100;
            }

            return score;
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

            string cleaned = Regex.Replace(text, @"\d{1,2}:\d{2}", "");
            cleaned = Regex.Replace(cleaned, @"[\[\]\(\)\{\}\<\>]", " ");
            cleaned = Regex.Replace(cleaned, @"[^a-zA-Z0-9가-힣ㄱ-ㅎㅏ-ㅣぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\.,!\?\-]", "");
            cleaned = Regex.Replace(cleaned, @"([\-\=\.\/_])\1+", "$1");
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            if (cleaned.Length < 2 || !Regex.IsMatch(cleaned, @"[a-zA-Z가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]"))
            {
                // 여기에도 1글자 예외 처리 허용
                if (cleaned.Length == 1 && Regex.IsMatch(cleaned, @"^[가-힣ぁ-んァ-ヶ\u4e00-\u9fa5]$")) { /* 통과 */ }
                else return "";
            }

            int retryCount = 3;
            string targetApiLang = GetGoogleTransLangCode(tLang);
            string result = "";

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            while (retryCount > 0)
            {
                try
                {
                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetApiLang}&dt=t&q={Uri.EscapeDataString(cleaned)}";
                    var res = await httpClient.GetStringAsync(url);
                    using var doc = System.Text.Json.JsonDocument.Parse(res);

                    foreach (var item in doc.RootElement[0].EnumerateArray())
                    {
                        result += item[0].GetString();
                    }
                    break;
                }
                catch
                {
                    retryCount--;
                    await Task.Delay(300);
                }
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
            string url = $"https://generativelanguage.googleapis.com/v1beta/models?key={apiKey}";
            try
            {
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string resJson = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(resJson);

                    var models = doc.RootElement.GetProperty("models")
                                    .EnumerateArray()
                                    .Select(m => m.GetProperty("name").GetString().Replace("models/", ""))
                                    .ToList();

                    string modelList = string.Join(", ", models);
                    AppendLog($"사용 가능한 제미나이 모델 목록: {modelList}");
                }
                else AppendLog($"제미나이 모델 목록 가져오기 실패 (HTTP {(int)response.StatusCode})");
            }
            catch (Exception ex) { AppendLog($"모델 목록 확인 중 오류 발생: {ex.Message}"); }
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
            string url = $"https://generativelanguage.googleapis.com/v1/models/{modelName}:generateContent?key={apiKey}";
            string targetLanguage = tLang == "ko" ? "Korean" : (tLang == "en-US" ? "English" : tLang);

            string prompt = "You are an expert game translator. " +
                            "The input text is from OCR and has many typos (e.g., '伽' instead of '你', 'カ' instead of '为'). " +
                            "Your job: 1. Guess the original intended sentence by ignoring OCR noise. " +
                            "2. Translate it naturally into " + targetLanguage + ". " +
                            "3. If the text is just a name or nonsense, return an empty string. " +
                            "Output ONLY the translation: \n\n" + text;

            var requestBody = new { contents = new[] { new { parts = new[] { new { text = prompt } } } } };
            string jsonPayload = System.Text.Json.JsonSerializer.Serialize(requestBody);

            int retryCount = 2;
            while (retryCount > 0)
            {
                try
                {
                    using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                    using var response = await httpClient.PostAsync(url, content);
                    if (!response.IsSuccessStatusCode)
                    {
                        string errorDetail = await response.Content.ReadAsStringAsync();
                        AppendLog($"[Gemini API 오류] 모델({modelName}) 호출 실패: {errorDetail}");
                        throw new Exception("Gemini 호출 실패");
                    }

                    string resJson = await response.Content.ReadAsStringAsync();
                    using var doc = System.Text.Json.JsonDocument.Parse(resJson);
                    return doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString().Trim();
                }
                catch
                {
                    retryCount--;
                    await Task.Delay(300);
                }
            }
            return "";
        }
    }
}
