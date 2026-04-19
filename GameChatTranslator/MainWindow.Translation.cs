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
        private void ToggleAutoTranslate()
        {
            if (gameChatArea == Rectangle.Empty) return;
            isAutoTranslating = !isAutoTranslating;
            UpdateYellowHotkeyGuideText();

            if (isAutoTranslating)
            {
                runTranslation();
                autoTranslateTimer.Start();
            }
            else autoTranslateTimer.Stop();
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
        private async void runTranslation()
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

                BitmapData bmpData = resizedBitmap.LockBits(new Rectangle(0, 0, resizedBitmap.Width, resizedBitmap.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * resizedBitmap.Height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    byte b = rgbValues[i], g = rgbValues[i + 1], r = rgbValues[i + 2];

                    // 1. 하얀색 채팅 텍스트: 단순히 밝은 게 아니라 R/G/B 값의 차이가 거의 없는 '순백색' 필터링
                    // -> 배경에 파란 하늘이나 붉은 이펙트가 비치면 R,G,B 차이가 벌어져서 가차 없이 버림
                    bool isWhite = (r > threshold && g > threshold && b > threshold) &&
                                   (Math.Abs(r - g) < 35 && Math.Abs(g - b) < 35 && Math.Abs(r - b) < 35);

                    // 2. 노란색/금색 닉네임: R과 G는 높고, B는 낮아야 함 (G에 약간의 여유를 주어 번짐 방어)
                    bool isYellow = (r > threshold && g > (threshold - 40) && b < 100);

                    // 둘 중 하나라도 만족하면 완벽한 글자(흰색)로, 아니면 전부 배경(검은색)으로 밀어버립니다.
                    byte finalColor = (isWhite || isYellow) ? (byte)255 : (byte)0;

                    rgbValues[i] = finalColor;     // B
                    rgbValues[i + 1] = finalColor; // G
                    rgbValues[i + 2] = finalColor; // R
                    rgbValues[i + 3] = 255;        // A
                }

                Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                resizedBitmap.UnlockBits(bmpData);

                if (ShouldSaveDebugImages())
                {
                    // 🌟 [프레임 방어 최적화] 메인 스레드 대기 방지를 위해 복사본을 만들어 백그라운드 저장
                    Bitmap rawClone = new Bitmap(rawBitmap);
                    Bitmap resizeClone = new Bitmap(resizedBitmap);
                    _ = Task.Run(() =>
                    {
                        using (rawClone) SaveDebugImage(rawClone, "[Origin]");
                        using (resizeClone) SaveDebugImage(resizeClone, "[Resize]");
                    });
                }

                int cropTop = 0;
                int cropBottom = (int)(resizedBitmap.Height * 0.05);
                int newH = resizedBitmap.Height - cropTop - cropBottom;

                using Bitmap croppedBitmap = new Bitmap(resizedBitmap.Width, newH);
                using (Graphics g = Graphics.FromImage(croppedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    g.DrawImage(resizedBitmap, new Rectangle(0, 0, resizedBitmap.Width, newH),
                                new Rectangle(0, cropTop, resizedBitmap.Width, newH), GraphicsUnit.Pixel);
                }

                if (ShouldSaveDebugImages())
                {
                    // 🌟 [프레임 방어 최적화] 백그라운드 저장
                    Bitmap cropClone = new Bitmap(croppedBitmap);
                    _ = Task.Run(() => { using (cropClone) SaveDebugImage(cropClone, "[Cropped]"); });
                }

                using MemoryStream ms = new MemoryStream();
                croppedBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                using SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                var ocrResults = new Dictionary<string, OcrResult>();
                foreach (var kvp in ocrEngines)
                {
                    ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
                }

                var masterResult = ocrResults.ContainsKey(gameLang) ? ocrResults[gameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
                if (masterResult == null) return;

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
