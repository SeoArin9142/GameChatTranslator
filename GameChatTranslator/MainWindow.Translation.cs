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
    public partial class MainWindow
    {
        private enum AutoTranslateMode
        {
            Off,
            Fast,
            Auto,
            Accurate
        }

        private enum OcrProcessingMode
        {
            Fast,
            Auto,
            Accurate
        }

        private enum OcrPreprocessKind
        {
            Color,
            ColorThick,
            Adaptive
        }

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
        private void ResetTranslationCache(string reason)
        {
            lastRawTextCombined = "";
            AppendLog($"재번역 캐시 초기화: {reason}");
        }
        private class MergedLine
        {
            public double Top;
            public double Bottom;
            public string Text;
        }
        private class PreprocessedOcrImage : IDisposable
        {
            public string Name;
            public Bitmap Bitmap;

            public void Dispose()
            {
                Bitmap?.Dispose();
            }
        }
        private class OcrCandidate
        {
            public string PreprocessName;
            public Dictionary<string, OcrResult> Results;
            public List<MergedLine> Lines;
            public int Score;
        }
        private bool IsSameLanguage(string text, string tLang)
        {
            if (tLang == "ko" && Regex.IsMatch(text, @"[가-힣]{2,}")) return true;
            if (tLang == "ru" && Regex.IsMatch(text, @"[а-яА-ЯёЁ]")) return true;
            if (tLang == "ja" && Regex.IsMatch(text, @"[ぁ-んァ-ヶ]")) return true;
            if (tLang == "zh-Hans-CN" && Regex.IsMatch(text, @"[\u4e00-\u9fa5]")) return true;
            if (tLang == "en-US" && Regex.IsMatch(text, @"[a-zA-Z]") && !Regex.IsMatch(text, @"[가-힣а-яА-ЯёЁぁ-んァ-ヶ\u4e00-\u9fa5]")) return true;
            return false;
        }
        private string GetGoogleTransLangCode(string lang)
        {
            if (lang == "zh-Hans-CN") return "zh-CN";
            if (lang == "en-US") return "en";
            return lang;
        }
        private void runTranslation()
        {
            runTranslation(OcrProcessingMode.Accurate);
        }
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
        private bool IsFastPathSuccess(OcrCandidate candidate)
        {
            if (candidate == null || candidate.Score <= 0 || candidate.Lines == null) return false;

            return candidate.Lines.Any(line =>
                TryExtractKnownCharacterChatLine(line.Text, out _, out string message) &&
                !string.IsNullOrWhiteSpace(message));
        }
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
        private OcrResult SelectMasterOcrResult(Dictionary<string, OcrResult> ocrResults)
        {
            return ocrResults.ContainsKey(gameLang) ? ocrResults[gameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
        }
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
