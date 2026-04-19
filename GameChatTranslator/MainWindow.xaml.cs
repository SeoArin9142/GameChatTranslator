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
using Brushes = System.Windows.Media.Brushes;

namespace GameTranslator
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // 📌 1. Windows API 단축키 등록 (Global Hotkey)
        // 백그라운드 캡처 및 제어를 위한 Win32 API 호출부
        // ==========================================
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        private const int ID_HOTKEY_MOVE_LOCK = 9001;
        private const int ID_HOTKEY_AREA_SELECT = 9002;
        private const int ID_HOTKEY_TRANSLATE = 9003;
        private const int ID_HOTKEY_AUTO = 9004;

        // ==========================================
        // 📌 2. 전역 변수 (UI, 캡처, OCR, API 관련)
        // ==========================================
        private AreaSelector areaSelector;           // 화면 캡처 영역 지정창
        private Rectangle gameChatArea;              // 실제 캡처 영역 좌표/크기
        private Window captureBorderWindow;          // 캡처 테두리(Red) 표시용 창

        private bool isTranslating = false;          // 번역 중복 방지 플래그
        private bool isLocked = true;                // 창 잠금(마우스 통과) 플래그

        public IniFile ini;                          // 설정 파일(config.ini) I/O
        private string gameLang = "ko";              // 원본 게임 언어
        private string targetLang = "ko";            // 최종 번역 목표 언어

        // 단축키 매핑용 변수
        private uint modMove, modArea, modTrans, modAuto;
        private uint keyMove, keyArea, keyTrans, keyAuto;

        private DispatcherTimer autoTranslateTimer;  // 자동 번역 루프 타이머
        private bool isAutoTranslating = false;
        private string lastRawTextCombined = "";     // 중복 캡처 방지용 이전 텍스트

        private HttpClient httpClient = new HttpClient();
        private Dictionary<string, OcrEngine> ocrEngines = new Dictionary<string, OcrEngine>();
        private IntPtr _windowHandle;

        // ==========================================
        // 📌 3. 초기화 (생성자)
        // ==========================================
        public MainWindow()
        {
            InitializeComponent();

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            ini = new IniFile(iniPath);

            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";
            int intervalSeconds = int.TryParse(ini.Read("AutoTranslateInterval"), out int i) ? i : 5;

            autoTranslateTimer = new DispatcherTimer();
            autoTranslateTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            autoTranslateTimer.Tick += (s, e) => { if (!isTranslating) runTranslation(); };

            string[] tags = { "ko", "en-US", "zh-Hans-CN", "ja", "ru" };
            foreach (var tag in tags)
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                if (engine != null) ocrEngines.Add(tag, engine);
            }
        }

        // ==========================================
        // 📌 4. 창 로드 이벤트 (렌더링 직후 실행)
        // ==========================================
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 🌟 버그 픽스: 투명 윈도우(AllowsTransparency)의 자식 창 팝업 위치가 어긋나는 버그 방지
            // 메인 UI가 완벽하게 그려지도록 0.5초 대기 후 설정창을 호출합니다.
            await Task.Delay(500);

            // [초기 설정창 호출]
            OptionSelector selector = new OptionSelector(this, ini);
            selector.Owner = this;
            selector.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            bool? dialogResult = selector.ShowDialog();

            // [UX 예외 처리] 설정창을 저장하지 않고 X 버튼으로 닫았을 경우 프로그램 완전 종료
            if (dialogResult != true)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            // 설정창에서 저장된 최신 언어값을 갱신
            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";

            // 단축키 후킹 시작
            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_windowHandle).AddHook(HwndHook);
            RegisterAllHotkeys();

            WindowUtils.SetClickThrough(this);
            UpdateYellowHotkeyGuideText();

            // [캡처 영역 및 창 크기 복구 로직]
            string cx = ini.Read("CaptureX");
            string cy = ini.Read("CaptureY");
            string cw = ini.Read("CaptureW");
            string ch = ini.Read("CaptureH");
            double screenH = SystemParameters.PrimaryScreenHeight;

            if (int.TryParse(cx, out int x) && int.TryParse(cy, out int y) &&
                int.TryParse(cw, out int w) && int.TryParse(ch, out int h) && w > 0 && h > 0)
            {
                gameChatArea = new Rectangle(x, y, w, h);

                // WPF 창 자동 조절 강제 고정 꼼수 (너비 고정, 높이 가변)
                this.SizeToContent = SizeToContent.Height;
                this.SizeToContent = SizeToContent.Manual;
                this.Width = w;
                this.MinWidth = w;
                this.SizeToContent = SizeToContent.Height;

                this.Left = x - 5;
                this.Top = y + h + 50;
                TxtResult.Text = "📍 마지막으로 저장된 영역과 창 크기를 불러왔습니다.";
            }
            else
            {
                this.Width = 1000;
                this.Height = 130;
                this.Left = 20;
                this.Top = screenH - this.Height - 10;
                gameChatArea = new Rectangle((int)this.Left, (int)(this.Top - 250 - 10), 500, 250);

                string missingLangs = "";
                if (!ocrEngines.ContainsKey("ru")) missingLangs += "러시아어 ";
                if (!ocrEngines.ContainsKey("ja")) missingLangs += "일본어 ";
                if (!ocrEngines.ContainsKey("zh-Hans-CN")) missingLangs += "중국어 ";

                if (missingLangs != "")
                    TxtResult.Text = $"⚠️ [경고] {missingLangs}언어팩 누락!\n'LangInstall.bat' 실행 권장.\n📍 기본 영역 자동 세팅 완료.";
                else
                    TxtResult.Text = "📍 좌측 하단 기본 영역으로 세팅 완료.\n채팅창 인식 영역이 다르면 단축키로 잡아주세요.";
            }

            UpdateCaptureBorder(!isLocked);
        }

        // ==========================================
        // 📌 5. 단축키 시스템 로직
        // ==========================================
        private void RegisterAllHotkeys()
        {
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);

            ParseHotkey(ini.Read("Key_MoveLock") ?? "Ctrl+7", out modMove, out keyMove);
            ParseHotkey(ini.Read("Key_AreaSelect") ?? "Ctrl+8", out modArea, out keyArea);
            ParseHotkey(ini.Read("Key_Translate") ?? "Ctrl+9", out modTrans, out keyTrans);
            ParseHotkey(ini.Read("Key_AutoTranslate") ?? "Ctrl+0", out modAuto, out keyAuto);

            RegisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK, modMove, keyMove);
            RegisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT, modArea, keyArea);
            RegisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE, modTrans, keyTrans);
            RegisterHotKey(_windowHandle, ID_HOTKEY_AUTO, modAuto, keyAuto);
        }

        private void UpdateYellowHotkeyGuideText()
        {
            string m = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string a = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string t = ini.Read("Key_Translate") ?? "Ctrl+9";
            string au = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string newGuide = $"[{m}] 이동/잠금  [{a}] 영역설정  [{t}] 번역  [{au}] 자동번역";

            foreach (var tb in FindVisualChildren<TextBlock>(this))
            {
                if (tb.Text.Contains("이동") && tb.Text.Contains("영역설정") || tb.Text.Contains("자동번역"))
                {
                    tb.Inlines.Clear();
                    tb.Inlines.Add(new Run(newGuide));
                    if (isAutoTranslating)
                    {
                        tb.Inlines.Add(new Run("  ● 자동 번역 중...") { Foreground = Brushes.Lime, FontWeight = FontWeights.Bold });
                    }
                    break;
                }
            }
        }

        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T) yield return (T)child;
                    foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild;
                }
            }
        }

        private void ParseHotkey(string hotkeyStr, out uint modifier, out uint vk)
        {
            modifier = 0; vk = 0;
            if (string.IsNullOrEmpty(hotkeyStr)) return;

            hotkeyStr = hotkeyStr.ToUpper().Replace(" ", "");
            if (hotkeyStr.Contains("CTRL+")) { modifier |= MOD_CONTROL; hotkeyStr = hotkeyStr.Replace("CTRL+", ""); }
            if (hotkeyStr.Contains("ALT+")) { modifier |= 0x0001; hotkeyStr = hotkeyStr.Replace("ALT+", ""); }
            if (hotkeyStr.Contains("SHIFT+")) { modifier |= 0x0004; hotkeyStr = hotkeyStr.Replace("SHIFT+", ""); }

            if (Regex.IsMatch(hotkeyStr, @"^[0-9]$")) hotkeyStr = "D" + hotkeyStr;
            if (hotkeyStr == "~" || hotkeyStr == "`" || hotkeyStr == "TILDE") { vk = 0xC0; return; }

            if (Enum.TryParse(hotkeyStr, true, out Key wpfKey)) { vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey); }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case ID_HOTKEY_MOVE_LOCK: ToggleMoveLock(); handled = true; break;
                    case ID_HOTKEY_AREA_SELECT: startAreaSelection(); handled = true; break;
                    case ID_HOTKEY_TRANSLATE: runTranslation(); handled = true; break;
                    case ID_HOTKEY_AUTO: ToggleAutoTranslate(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            captureBorderWindow?.Close();
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);
            base.OnClosed(e);
        }

        // ==========================================
        // 📌 6. UI 제어 및 기능 토글 로직
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!isLocked) this.DragMove(); }

        private void ToggleMoveLock()
        {
            isLocked = !isLocked;
            if (isLocked)
            {
                WindowUtils.SetClickThrough(this);
                MainBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#55FFFFFF"));
                UpdateCaptureBorder(false);
            }
            else
            {
                WindowUtils.RemoveClickThrough(this);
                MainBorder.BorderBrush = Brushes.LimeGreen;
                UpdateCaptureBorder(true);
            }
        }

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

        private void startAreaSelection()
        {
            if (areaSelector != null) { areaSelector.Close(); }
            areaSelector = new AreaSelector();
            areaSelector.Owner = this;
            areaSelector.Show();
        }

        public void SetCaptureArea(Rectangle area)
        {
            gameChatArea = area;

            this.Top = area.Y + area.Height + 50;
            this.Left = area.X - 5;

            this.SizeToContent = SizeToContent.Manual;
            this.Width = area.Width;
            this.MinWidth = area.Width;
            this.SizeToContent = SizeToContent.Height;

            this.Visibility = Visibility.Visible;
            this.Topmost = true;
            UpdateYellowHotkeyGuideText();

            ini.Write("CaptureX", area.X.ToString());
            ini.Write("CaptureY", area.Y.ToString());
            ini.Write("CaptureW", area.Width.ToString());
            ini.Write("CaptureH", area.Height.ToString());
            ini.Write("WindowW", this.ActualWidth.ToString());
            ini.Write("WindowH", this.ActualHeight.ToString());

            UpdateCaptureBorder(!isLocked);
        }

        private void UpdateCaptureBorder(bool show)
        {
            if (gameChatArea == Rectangle.Empty) return;

            if (captureBorderWindow == null)
            {
                captureBorderWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    BorderBrush = Brushes.Red,
                    BorderThickness = new Thickness(2),
                    Opacity = 0.8
                };
                captureBorderWindow.Show();
                WindowUtils.SetClickThrough(captureBorderWindow);
            }

            captureBorderWindow.Left = gameChatArea.X;
            captureBorderWindow.Top = gameChatArea.Y;
            captureBorderWindow.Width = gameChatArea.Width;
            captureBorderWindow.Height = gameChatArea.Height;

            captureBorderWindow.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }

        // ==========================================
        // 📌 7. 유틸리티 (언어 검증, 로깅)
        // ==========================================
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

        private void AppendLog(string original, string translated)
        {
            try
            {
                string logDirPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!System.IO.Directory.Exists(logDirPath)) System.IO.Directory.CreateDirectory(logDirPath);

                string fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string filePath = System.IO.Path.Combine(logDirPath, fileName);
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] {original.Trim()} -> {translated.Trim()}{Environment.NewLine}";

                System.IO.File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { /* 로그 기록 실패 시 무시 */ }
        }

        // ==========================================
        // 📌 8. 메인 번역 로직 (OCR -> 필터링 -> API -> UI)
        // ==========================================
        private async void runTranslation()
        {
            if (isTranslating || gameChatArea == Rectangle.Empty) return;
            isTranslating = true;

            int threshold = int.TryParse(ini.Read("Threshold"), out int t) ? t : 80;

            int scaleFactor = int.TryParse(ini.Read("ScaleFactor"), out int s) ? s : 3;
            if (scaleFactor < 1) scaleFactor = 1;
            if (scaleFactor > 4) scaleFactor = 4;

            try
            {
                // [화면 캡처]
                using Bitmap rawBitmap = new Bitmap(gameChatArea.Width, gameChatArea.Height);
                using (Graphics g = Graphics.FromImage(rawBitmap))
                {
                    g.CopyFromScreen(gameChatArea.Location, System.Drawing.Point.Empty, gameChatArea.Size);
                }

                // [해상도 배율 뻥튀기]
                int newWidth = rawBitmap.Width * scaleFactor;
                int newHeight = rawBitmap.Height * scaleFactor;
                using Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawBitmap, 0, 0, newWidth, newHeight);
                }

                // [색상 이진화 필터링] 배경 노이즈 제거 및 아군(노랑), 적군(빨강) 닉네임 보존 로직
                BitmapData bmpData = resizedBitmap.LockBits(new Rectangle(0, 0, resizedBitmap.Width, resizedBitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * resizedBitmap.Height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    byte b = rgbValues[i]; byte g = rgbValues[i + 1]; byte r = rgbValues[i + 2];

                    bool isWhite = (r > threshold && g > threshold && b > threshold);
                    bool isNicknameColor = (r > threshold && b < 120);

                    byte color = (isWhite || isNicknameColor) ? (byte)255 : (byte)0;
                    rgbValues[i] = color; rgbValues[i + 1] = color; rgbValues[i + 2] = color; rgbValues[i + 3] = 255;
                }
                Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                resizedBitmap.UnlockBits(bmpData);

                // [SoftwareBitmap 변환 및 다중 OCR 병렬 스캔]
                using MemoryStream ms = new MemoryStream();
                resizedBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                var ocrResults = new Dictionary<string, OcrResult>();
                foreach (var kvp in ocrEngines)
                {
                    ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
                }

                var masterResult = ocrResults.ContainsKey(gameLang) ? ocrResults[gameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
                if (masterResult == null) return;

                // [줄 병합 (Line Merge)] Y축 +-15 픽셀 오차 허용
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
                    else
                    {
                        mergedLines.Add(new MergedLine { Top = top, Bottom = bot, Text = text });
                    }
                }

                // [중복 스킵] 완전히 동일한 화면이면 API 호출 방지
                string currentRawTextCombined = string.Join("\n", mergedLines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined) return;
                lastRawTextCombined = currentRawTextCombined;

                TxtResult.Inlines.Clear();

                // [텍스트 치환 및 번역 엔진 호출]
                foreach (var chatLine in mergedLines)
                {
                    string krRawText = chatLine.Text.Trim();

                    // 정규식을 통한 닉네임과 채팅 내용 완벽 분리
                    var strictMatch = Regex.Match(krRawText, @"^([^:]*\[[^\]]+\]\s*[:;：!])(.*)$");
                    if (!strictMatch.Success) continue;

                    string characterNameGold = strictMatch.Groups[1].Value + " ";
                    string bestMessage = strictMatch.Groups[2].Value.Trim();
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

                            int subCIdx = fullText.IndexOfAny(new char[] { ':', ';', '：' });
                            if (subCIdx != -1) msgOnly = fullText.Substring(subCIdx + 1).Trim();
                            else
                            {
                                int brIdx = fullText.LastIndexOf(']');
                                if (brIdx != -1) msgOnly = fullText.Substring(brIdx + 1).Trim();
                            }

                            int score = msgOnly.Length;
                            if (kvp.Key == "ru" && Regex.Matches(msgOnly, @"[а-яА-ЯёЁ]").Count >= 1) score += 20000;
                            else if (kvp.Key == "ja" && Regex.Matches(msgOnly, @"[ぁ-んァ-ヶ]").Count >= 1) score += 10000;
                            else if (kvp.Key == "zh-Hans-CN" && Regex.Matches(msgOnly, @"[\u4e00-\u9fa5]").Count >= 1) score += 5000;
                            else if (kvp.Key == "en-US" && Regex.Matches(msgOnly, @"[a-zA-Z]").Count >= 3) score += 1000;

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMessage = msgOnly;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(bestMessage)) bestMessage = strictMatch.Groups[2].Value.Trim();
                    string translated = bestMessage;

                    // 번역 API 호출 전 동일 언어 검증
                    if (!string.IsNullOrEmpty(bestMessage) && !Regex.IsMatch(bestMessage, @"^[0-9\W]+$"))
                    {
                        if (IsSameLanguage(bestMessage, targetLang))
                        {
                            translated = bestMessage;
                        }
                        else
                        {
                            int retryCount = 3;
                            string targetApiLang = GetGoogleTransLangCode(targetLang);

                            while (retryCount > 0)
                            {
                                try
                                {
                                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetApiLang}&dt=t&q={Uri.EscapeDataString(bestMessage)}";
                                    var res = await httpClient.GetStringAsync(url);

                                    using var doc = System.Text.Json.JsonDocument.Parse(res);
                                    translated = "";

                                    foreach (var item in doc.RootElement[0].EnumerateArray()) { translated += item[0].GetString(); }
                                    break;
                                }
                                catch
                                {
                                    retryCount--;
                                    await Task.Delay(300);
                                }
                            }
                        }
                    }

                    AppendLog(characterNameGold + bestMessage, translated);

                    TxtResult.Inlines.Add(new Run(characterNameGold) { Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                    TxtResult.Inlines.Add(new Run(translated) { Foreground = Brushes.White });
                    TxtResult.Inlines.Add(new LineBreak());
                }
            }
            catch (Exception ex)
            {
                TxtResult.Text = "에러: " + ex.Message;
            }
            finally
            {
                isTranslating = false;
            }
        }
    }
}