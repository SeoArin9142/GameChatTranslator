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
    public partial class MainWindow : Window
    {
        // ==========================================
        // 📌 1. Windows API 단축키 등록 (Global Hotkey) 및 화면 캡처(BitBlt)
        // ==========================================
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
        // 🌟 [추가] 창을 강제로 최상단에 고정하는 API
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010; // 창이 포커스를 뺏지 않도록 함 (게임 방해 금지)

        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        private const int ID_HOTKEY_MOVE_LOCK = 9001;
        private const int ID_HOTKEY_AREA_SELECT = 9002;
        private const int ID_HOTKEY_TRANSLATE = 9003;
        private const int ID_HOTKEY_AUTO = 9004;
        private const int ID_HOTKEY_TOGGLE_ENGINE = 9005;
        private const string DefaultGeminiModel = "gemini-2.5-flash";

        // ==========================================
        // 📌 2. 전역 변수 (UI, 캡처, OCR, API 관련)
        // ==========================================
        private AreaSelector areaSelector;
        private Rectangle gameChatArea;
        private Window captureBorderWindow;

        private bool isTranslating = false;
        private bool isLocked = true;

        public IniFile ini;
        private string gameLang = "ko";
        private string targetLang = "ko";

        private uint modMove, modArea, modTrans, modAuto, modToggle;
        private uint keyMove, keyArea, keyTrans, keyAuto, keyToggle;

        private bool useGeminiEngine = false; // 🌟 [추가] 현재 제미나이를 사용 중인지 상태 저장

        private DispatcherTimer autoTranslateTimer;
        // 🌟 [추가] 최상단 강제 유지 타이머
        private DispatcherTimer topmostTimer;

        private bool isAutoTranslating = false;
        private string lastRawTextCombined = "";
        private string hotkeyWarningMessage = "";

        private HttpClient httpClient = new HttpClient();
        private Dictionary<string, OcrEngine> ocrEngines = new Dictionary<string, OcrEngine>();
        private IntPtr _windowHandle;

        private HashSet<string> characterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string sessionLogFileName;

        private void LoadCharacters()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "characters.txt");
                if (File.Exists(path))
                {
                    var lines = File.ReadAllLines(path);
                    foreach (var line in lines)
                    {
                        string name = line.Trim();
                        if (!string.IsNullOrEmpty(name) && !name.StartsWith("#"))
                        {
                            characterNames.Add(name);
                        }
                    }
                    AppendLog($"캐릭터 {characterNames.Count}명 로드 완료.");
                }
            }
            catch (Exception ex) { AppendLog($"파일 로드 중 오류: {ex.Message}"); }
        }

        // ==========================================
        // 📌 3. 초기화 (생성자)
        // ==========================================
        public MainWindow()
        {
            InitializeComponent();

            // 🌟 [추가] 켜진 시간을 기준으로 로그 파일명 고정 (예: log_20260419_1635.txt)
            // 초(ss)까지 넣고 싶으시면 yyyyMMdd_HHmmss 로 변경하시면 됩니다.
            sessionLogFileName = $"log_{DateTime.Now:yyyyMMdd_HHmm}.txt";

            LoadCharacters();

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            ini = new IniFile(iniPath);

            EnsureDefaultSettings();

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

            // 🌟 [추가] 2초마다 창을 최상단으로 강제 끌어올리는 타이머 시작
            topmostTimer = new DispatcherTimer();
            topmostTimer.Interval = TimeSpan.FromSeconds(2);
            topmostTimer.Tick += (s, e) => ForceTopmost();
            topmostTimer.Start();
        }

        private void EnsureDefaultSettings()
        {
            if (string.IsNullOrWhiteSpace(ini.Read("GeminiKey")) && string.IsNullOrWhiteSpace(ini.Read("GeminiKey", "GeminiKey")))
            {
                ini.Write("GeminiKey", "");
            }

            if (string.IsNullOrWhiteSpace(ini.Read("GeminiModel")))
            {
                ini.Write("GeminiModel", DefaultGeminiModel);
            }
        }

        private string ReadGeminiKey()
        {
            string settingsKey = ini.Read("GeminiKey");
            if (!string.IsNullOrWhiteSpace(settingsKey))
            {
                return settingsKey.Trim();
            }

            string legacySectionKey = ini.Read("GeminiKey", "GeminiKey");
            if (!string.IsNullOrWhiteSpace(legacySectionKey))
            {
                string trimmedKey = legacySectionKey.Trim();
                ini.Write("GeminiKey", trimmedKey);
                AppendLog("기존 [GeminiKey] 섹션의 API 키를 [Settings] 섹션으로 이전했습니다.");
                return trimmedKey;
            }

            return "";
        }

        private string ReadGeminiModel()
        {
            string modelName = ini.Read("GeminiModel");
            return string.IsNullOrWhiteSpace(modelName) ? DefaultGeminiModel : modelName.Trim();
        }

        // ==========================================
        // 📌 창 강제 최상단 유지 로직
        // ==========================================
        private void ForceTopmost()
        {
            // 1. 메인 번역창 최상단 강제 적용
            if (_windowHandle != IntPtr.Zero)
            {
                SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            // 2. 캡처 영역 표시(빨간 테두리) 창 최상단 강제 적용
            if (captureBorderWindow != null && captureBorderWindow.IsVisible)
            {
                IntPtr borderHandle = new WindowInteropHelper(captureBorderWindow).Handle;
                if (borderHandle != IntPtr.Zero)
                {
                    SetWindowPos(borderHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        // ==========================================
        // 📌 4. 창 로드 이벤트
        // ==========================================
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);

            OptionSelector selector = new OptionSelector(this, ini);
            selector.Owner = this;
            selector.WindowStartupLocation = WindowStartupLocation.CenterOwner;

            bool? dialogResult = selector.ShowDialog();

            if (dialogResult != true)
            {
                Application.Current.Shutdown();
                return;
            }

            this.Topmost = false;
            this.Topmost = true;

            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";

            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_windowHandle).AddHook(HwndHook);
            RegisterAllHotkeys();

            string geminiKey = ReadGeminiKey();

            useGeminiEngine = !string.IsNullOrEmpty(geminiKey);

            string currentEngine = string.IsNullOrEmpty(geminiKey) ? "Google 무료 번역" : "Gemini AI 번역";

            AppendLog($"프로그램이 시작되었습니다. (적용 엔진: {currentEngine})");

            // 🌟 [추가] 프로그램 시작 시 현재 세팅값(API 키 제외)을 로그에 기록합니다.
            string log_gLang = ini.Read("GameLanguage") ?? "ko";
            string log_tLang = ini.Read("TargetLanguage") ?? "ko";
            string log_interval = ini.Read("AutoTranslateInterval") ?? "5";
            string log_threshold = ini.Read("Threshold") ?? "120";
            string log_scale = ini.Read("ScaleFactor") ?? "3";
            string log_opacity = ini.Read("Opacity") ?? "100";
            string log_model = ReadGeminiModel();

            AppendLog($"[현재 세팅]");
            AppendLog($"\t[게임 언어\t\t\t: {log_gLang}\t]");
            AppendLog($"\t[번역 언어\t\t\t: {log_tLang}\t]");
            AppendLog($"\t[Threshold\t\t\t: {log_threshold}\t]");
            AppendLog($"\t[Scale\t\t\t: {log_scale}배\t]");
            AppendLog($"\t[번역 주기\t\t\t: {log_interval}초\t]");
            AppendLog($"\t[투명도\t\t\t: {log_opacity}\t]");
            AppendLog($"\t[모델\t\t\t: {log_model}\t]");

            if (!string.IsNullOrEmpty(geminiKey))
            {
                await ListAvailableGeminiModels(geminiKey);
            }

            WindowUtils.SetClickThrough(this);
            UpdateYellowHotkeyGuideText();

            string cx = ini.Read("CaptureX");
            string cy = ini.Read("CaptureY");
            string cw = ini.Read("CaptureW");
            string ch = ini.Read("CaptureH");
            double screenH = SystemParameters.PrimaryScreenHeight;

            if (int.TryParse(cx, out int x) && int.TryParse(cy, out int y) &&
                int.TryParse(cw, out int w) && int.TryParse(ch, out int h) && w > 0 && h > 0)
            {
                gameChatArea = new Rectangle(x, y, w, h);
                this.SizeToContent = SizeToContent.Manual;
                this.Width = w;
                this.MinWidth = w;
                this.SizeToContent = SizeToContent.Height;
                this.Left = x - 5;
                this.Top = y + h + 50;

                TxtResult.Text = $"📍 마지막으로 저장된 영역을 불러왔습니다.\n🤖 현재 번역 엔진: {currentEngine}";
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
                    TxtResult.Text = $"⚠️ [경고] {missingLangs}언어팩 누락!\n'LangInstall.bat' 실행 권장.\n🤖 현재 번역 엔진: {currentEngine}";
                else
                    TxtResult.Text = $"📍 기본 캡처 영역으로 세팅 완료.\n🤖 현재 번역 엔진: {currentEngine}";
            }

            UpdateCaptureBorder(!isLocked);
            ShowHotkeyWarningIfAny();
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
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_ENGINE);

            hotkeyWarningMessage = "";
            var failedHotkeys = new List<string>();

            string moveHotkey = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string areaHotkey = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string translateHotkey = ini.Read("Key_Translate") ?? "Ctrl+9";
            string autoHotkey = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string toggleHotkey = ini.Read("Key_ToggleEngine") ?? "Ctrl+-";

            ParseHotkey(moveHotkey, out modMove, out keyMove);
            ParseHotkey(areaHotkey, out modArea, out keyArea);
            ParseHotkey(translateHotkey, out modTrans, out keyTrans);
            ParseHotkey(autoHotkey, out modAuto, out keyAuto);
            ParseHotkey(toggleHotkey, out modToggle, out keyToggle);

            RegisterHotKeyOrWarn(ID_HOTKEY_MOVE_LOCK, modMove, keyMove, "이동/잠금", moveHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AREA_SELECT, modArea, keyArea, "영역 설정", areaHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TRANSLATE, modTrans, keyTrans, "수동 번역", translateHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AUTO, modAuto, keyAuto, "자동 번역", autoHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TOGGLE_ENGINE, modToggle, keyToggle, "엔진 전환", toggleHotkey, failedHotkeys);

            if (failedHotkeys.Count > 0)
            {
                hotkeyWarningMessage = "⚠️ 등록 실패 단축키: " + string.Join(", ", failedHotkeys);
                AppendLog(hotkeyWarningMessage);
            }
        }

        private void RegisterHotKeyOrWarn(int id, uint modifier, uint key, string label, string configuredHotkey, List<string> failedHotkeys)
        {
            if (key == 0)
            {
                failedHotkeys.Add($"{label}({configuredHotkey}: 키 해석 실패)");
                return;
            }

            if (!RegisterHotKey(_windowHandle, id, modifier, key))
            {
                failedHotkeys.Add($"{label}({configuredHotkey})");
            }
        }

        private void ShowHotkeyWarningIfAny()
        {
            if (string.IsNullOrWhiteSpace(hotkeyWarningMessage)) return;

            TxtResult.Inlines.Add(new LineBreak());
            TxtResult.Inlines.Add(new Run(hotkeyWarningMessage)
            {
                Foreground = Brushes.OrangeRed,
                FontWeight = FontWeights.Bold
            });
        }

        private void UpdateYellowHotkeyGuideText()
        {
            string m = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string a = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string t = ini.Read("Key_Translate") ?? "Ctrl+9";
            string au = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string tg = ini.Read("Key_ToggleEngine") ?? "Ctrl+-";

            // 🌟 안내 문구에 엔진 전환 추가
            string engineStr = useGeminiEngine ? "Gemini" : "Google";
            string newGuide = $"[{m}] 이동  [{a}] 영역  [{t}] 번역  \n[{au}] 자동  [{tg}] {engineStr} 전환";

            foreach (var tb in FindVisualChildren<TextBlock>(this))
            {
                if (tb.Text.Contains("이동") && tb.Text.Contains("영역설정") || tb.Text.Contains("자동"))
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
                    case ID_HOTKEY_TOGGLE_ENGINE: ToggleEngine(); handled = true; break;
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
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_ENGINE);

            AppendLog("프로그램이 정상적으로 종료되었습니다.");
            Application.Current.Shutdown();
            base.OnClosed(e);
        }

        // ==========================================
        // 📌 6. UI 제어 및 기능 토글 로직
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!isLocked) this.DragMove(); }

        private void ToggleMoveLock()
        {
            isLocked = !isLocked;
            this.Topmost = true;

            if (isLocked)
            {
                WindowUtils.SetClickThrough(this);
                MainBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#55FFFFFF"));
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
            string currentEngine = useGeminiEngine ? "Gemini AI" : "Google 무료";

            AppendLog($"번역 엔진이 '{currentEngine}'(으)로 실시간 변경되었습니다.");

            // 화면에 즉시 알림 표시 (파란색 글씨)
            TxtResult.Inlines.Clear();
            TxtResult.Inlines.Add(new Run($"🔄 번역 엔진 변경됨: [ {currentEngine} ]") { Foreground = Brushes.Cyan, FontWeight = FontWeights.Bold });

            UpdateYellowHotkeyGuideText(); // 노란색 안내문구도 업데이트
        }

        // ==========================================
        // 📌 7. 유틸리티 (이미지 저장, 파일 정리, 로깅)
        // ==========================================
        private class MergedLine
        {
            public double Top;
            public double Bottom;
            public string Text;
        }

        private void SaveDebugImage(Bitmap bitmap, string suffix)
        {
            try
            {
                string captureDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
                if (!Directory.Exists(captureDirPath)) Directory.CreateDirectory(captureDirPath);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = $"{timestamp}_{suffix}.png";
                string filePath = Path.Combine(captureDirPath, fileName);

                bitmap.Save(filePath, ImageFormat.Png);
                CleanupCaptureFolder(captureDirPath);
            }
            catch { }
        }

        private void CleanupCaptureFolder(string folderPath)
        {
            try
            {
                int interval = int.TryParse(ini.Read("AutoTranslateInterval"), out int i) ? i : 5;
                if (interval < 1) interval = 1;

                // 30분(1800초) 분량의 세트 수를 계산 (한 번에 3장 저장)
                int maxFileCount = (1800 / interval) * 3;
                if (maxFileCount < 20) maxFileCount = 20;

                var directory = new DirectoryInfo(folderPath);
                var files = directory.GetFiles("*.png").OrderByDescending(f => f.CreationTime).ToList();

                if (files.Count > maxFileCount)
                {
                    var filesToDelete = files.Skip(maxFileCount);
                    foreach (var file in filesToDelete)
                    {
                        file.Delete();
                    }
                }
            }
            catch { }
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

        // 🌟 버전 1: 시스템 시작/종료 로그용 (인자 1개)
        private void AppendLog(string systemMessage)
        {
            try
            {
                string logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDirPath)) Directory.CreateDirectory(logDirPath);

                // 🌟 수정: 매번 새로 만들지 않고, 켜질 때 고정된 파일명 사용
                string filePath = Path.Combine(logDirPath, sessionLogFileName);

                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [System] {systemMessage}{Environment.NewLine}";
                File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // 🌟 버전 2: 번역 결과 로그용 (인자 3개)
        private void AppendLog(string original, string translated, string engineName)
        {
            try
            {
                string logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDirPath)) Directory.CreateDirectory(logDirPath);

                // 🌟 수정: 매번 새로 만들지 않고, 켜질 때 고정된 파일명 사용
                string filePath = Path.Combine(logDirPath, sessionLogFileName);

                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{engineName}] {original.Trim()} -> {translated.Trim()}{Environment.NewLine}";
                File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { }
        }

        // ==========================================
        // 📌 8. 메인 번역 로직 (OCR -> 필터링 -> API -> UI)
        // ==========================================
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
                using Bitmap rawBitmap = new Bitmap(gameChatArea.Width, gameChatArea.Height);
                using (Graphics g = Graphics.FromImage(rawBitmap))
                {
                    IntPtr hdcSrc = GetWindowDC(IntPtr.Zero);
                    IntPtr hdcDest = g.GetHdc();
                    BitBlt(hdcDest, 0, 0, gameChatArea.Width, gameChatArea.Height, hdcSrc, gameChatArea.X, gameChatArea.Y, 0x00CC0020);
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

                // 🌟 [프레임 방어 최적화] 메인 스레드 대기 방지를 위해 복사본을 만들어 백그라운드 저장
                Bitmap rawClone = new Bitmap(rawBitmap);
                Bitmap resizeClone = new Bitmap(resizedBitmap);
                _ = Task.Run(() =>
                {
                    using (rawClone) SaveDebugImage(rawClone, "[Origin]");
                    using (resizeClone) SaveDebugImage(resizeClone, "[Resize]");
                });

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

                // 🌟 [프레임 방어 최적화] 백그라운드 저장
                Bitmap cropClone = new Bitmap(croppedBitmap);
                _ = Task.Run(() => { using (cropClone) SaveDebugImage(cropClone, "[Cropped]"); });

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
                }
            }
            catch (Exception ex) { TxtResult.Text = "에러: " + ex.Message; }
            finally { isTranslating = false; }
        }

        // ==========================================
        // 📌 9. API 통신 로직
        // ==========================================
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
