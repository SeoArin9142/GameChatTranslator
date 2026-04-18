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
        // 프로그램이 백그라운드에 있거나 게임 중이어도 단축키를 인식하기 위해 user32.dll을 사용합니다.
        // ==========================================
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vlc);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const uint MOD_CONTROL = 0x0002;  // Ctrl 키 플래그
        private const int WM_HOTKEY = 0x0312;     // 핫키 메시지 ID

        // 각 단축키 기능별 고유 ID
        private const int ID_HOTKEY_MOVE_LOCK = 9001;
        private const int ID_HOTKEY_AREA_SELECT = 9002;
        private const int ID_HOTKEY_TRANSLATE = 9003;
        private const int ID_HOTKEY_AUTO = 9004;

        // ==========================================
        // 📌 2. 전역 변수 선언
        // ==========================================
        private AreaSelector areaSelector;           // 화면 캡처 영역을 지정하는 반투명 창
        private Rectangle gameChatArea;              // 실제 캡처가 진행될 좌표 및 크기 (X, Y, Width, Height)
        private Window captureBorderWindow;          // 설정 모드일 때 캡처 영역을 붉은 테두리로 보여주는 창
        private bool isTranslating = false;          // 번역 작업 중복 실행 방지 플래그
        private bool isLocked = true;                // 창 잠금(마우스 클릭 무시) 상태 플래그

        public IniFile ini;                          // 설정 파일(config.ini) 읽기/쓰기 객체
        private string gameLang = "ko";              // 기준 언어 (채팅 닉네임과 줄바꿈을 인식하는 기준)
        private string targetLang = "ko";            // 최종 번역될 목표 언어

        // 단축키의 조합 키(Ctrl, Alt 등)와 실제 키 코드를 저장
        private uint modMove, modArea, modTrans, modAuto;
        private uint keyMove, keyArea, keyTrans, keyAuto;

        private DispatcherTimer autoTranslateTimer;  // 주기적으로 자동 번역을 실행하는 타이머
        private bool isAutoTranslating = false;      // 자동 번역 실행 상태 플래그
        private string lastRawTextCombined = "";     // 이전 번역 텍스트 (동일 내용이면 번역 스킵용)

        private HttpClient httpClient = new HttpClient(); // Google 번역 API 호출용 HTTP 클라이언트
        private Dictionary<string, OcrEngine> ocrEngines = new Dictionary<string, OcrEngine>(); // 언어별 OCR(광학 문자 인식) 엔진 모음
        private IntPtr _windowHandle;                // 현재 메인 윈도우의 OS 핸들

        // ==========================================
        // 📌 3. 초기화 (생성자)
        // ==========================================
        public MainWindow()
        {
            InitializeComponent();

            // HTTP 요청 시 봇으로 차단당하지 않도록 일반 브라우저(Chrome)처럼 User-Agent 위장
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            // config.ini 파일 경로 지정 및 로드
            string iniPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            ini = new IniFile(iniPath);

            // INI에서 초기 언어 및 자동 번역 간격(초) 설정 로드 (없으면 기본값 사용)
            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";
            int intervalSeconds = int.TryParse(ini.Read("AutoTranslateInterval"), out int i) ? i : 5;

            // 자동 번역 타이머 세팅
            autoTranslateTimer = new DispatcherTimer();
            autoTranslateTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            autoTranslateTimer.Tick += (s, e) => { if (!isTranslating) runTranslation(); }; // 번역 중이 아닐 때만 실행

            // 다중 언어 OCR 엔진 초기화 (한국어, 영어, 중국어, 일본어, 러시아어)
            // 윈도우에 해당 언어팩이 설치되어 있어야만 정상 로드됨
            string[] tags = { "ko", "en-US", "zh-Hans-CN", "ja", "ru" };
            foreach (var tag in tags)
            {
                var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                if (engine != null) ocrEngines.Add(tag, engine);
            }
        }

        // ==========================================
        // 📌 4. 창 로드 이벤트 (프로그램 실행 직후)
        // ==========================================
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 1. 실행 시 통합 설정창(OptionSelector)을 모달 창으로 띄워 사용자 설정 확인
            OptionSelector selector = new OptionSelector(this, ini);
            selector.ShowDialog();

            // 설정창에서 변경된 최신 언어 설정을 다시 읽어옴
            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";

            // 2. 윈도우 핸들을 가져와 글로벌 단축키 후킹 이벤트 연결
            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_windowHandle).AddHook(HwndHook);
            RegisterAllHotkeys(); // 단축키 등록

            // 3. 기본 UI 세팅
            WindowUtils.SetClickThrough(this); // 기본적으로 마우스 클릭 통과 모드로 시작
            UpdateYellowHotkeyGuideText();     // 상단 단축키 안내 텍스트 갱신

            // 4. INI 파일에서 이전에 저장된 캡처 영역(X, Y, W, H) 읽어오기
            string cx = ini.Read("CaptureX");
            string cy = ini.Read("CaptureY");
            string cw = ini.Read("CaptureW");
            string ch = ini.Read("CaptureH");
            string ww = ini.Read("WindowW"); // 번역창 너비
            string wh = ini.Read("WindowH"); // 번역창 높이

            double screenH = SystemParameters.PrimaryScreenHeight;

            // 저장된 캡처 영역 데이터가 정상적으로 존재한다면?
            if (int.TryParse(cx, out int x) && int.TryParse(cy, out int y) &&
                int.TryParse(cw, out int w) && int.TryParse(ch, out int h) && w > 0 && h > 0)
            {
                gameChatArea = new Rectangle(x, y, w, h); // 캡처 영역 복구

                // WPF 창 크기 자동 조절 꼼수 방지: 강제로 캡처 넓이(w)에 맞추고 세로만 자동 조절
                this.SizeToContent = SizeToContent.Height;
                this.SizeToContent = SizeToContent.Manual;
                this.Width = w;
                this.MinWidth = w;    // 최소 넓이를 고정하여 창이 임의로 쪼그라드는 현상 방지
                this.SizeToContent = SizeToContent.Height;

                // 번역창을 캡처 영역의 바로 아래쪽(y + h + 50)에 배치
                this.Left = x - 5;
                this.Top = y + h + 50;
                TxtResult.Text = "📍 마지막으로 저장된 영역과 창 크기를 불러왔습니다.";
            }
            else
            {
                // 저장된 값이 없다면 스트리노바 맞춤형 기본값(좌측 하단)으로 초기화
                this.Width = 1000;
                this.Height = 130;
                this.Left = 20;
                this.Top = screenH - this.Height - 10;
                gameChatArea = new Rectangle((int)this.Left, (int)(this.Top - 250 - 10), 500, 250);

                // 언어팩 누락 여부 검사 및 경고 메시지 출력
                string missingLangs = "";
                if (!ocrEngines.ContainsKey("ru")) missingLangs += "러시아어 ";
                if (!ocrEngines.ContainsKey("ja")) missingLangs += "일본어 ";
                if (!ocrEngines.ContainsKey("zh-Hans-CN")) missingLangs += "중국어 ";

                if (missingLangs != "")
                    TxtResult.Text = $"⚠️ [경고] {missingLangs}언어팩 누락!\n'LangInstall.bat' 실행 권장.\n📍 기본 영역 자동 세팅 완료.";
                else
                    TxtResult.Text = "📍 좌측 하단 기본 영역으로 세팅 완료.\n채팅창 인식 영역이 다르면 단축키로 잡아주세요.";
            }

            // 잠금 상태에 따라 캡처 영역 테두리 표시/숨김 갱신
            UpdateCaptureBorder(!isLocked);
        }

        // ==========================================
        // 📌 5. 단축키 시스템 (등록, 해제, 파싱)
        // ==========================================

        // INI에 저장된 문자열을 기반으로 단축키를 OS에 등록
        private void RegisterAllHotkeys()
        {
            // 꼬임을 방지하기 위해 등록 전 모두 해제
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);

            // 문자열(예: "Ctrl+7")을 OS가 이해하는 코드(modifier, vk)로 변환
            ParseHotkey(ini.Read("Key_MoveLock") ?? "Ctrl+7", out modMove, out keyMove);
            ParseHotkey(ini.Read("Key_AreaSelect") ?? "Ctrl+8", out modArea, out keyArea);
            ParseHotkey(ini.Read("Key_Translate") ?? "Ctrl+9", out modTrans, out keyTrans);
            ParseHotkey(ini.Read("Key_AutoTranslate") ?? "Ctrl+0", out modAuto, out keyAuto);

            // OS에 글로벌 단축키 등록
            RegisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK, modMove, keyMove);
            RegisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT, modArea, keyArea);
            RegisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE, modTrans, keyTrans);
            RegisterHotKey(_windowHandle, ID_HOTKEY_AUTO, modAuto, keyAuto);
        }

        // 상단 가이드 텍스트 업데이트 (자동 번역 중일 때 Lime 색상 표시 포함)
        private void UpdateYellowHotkeyGuideText()
        {
            string m = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string a = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string t = ini.Read("Key_Translate") ?? "Ctrl+9";
            string au = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";

            string newGuide = $"[{m}] 이동/잠금  [{a}] 영역설정  [{t}] 번역  [{au}] 자동번역";

            // 시각적 트리에서 TextBlock을 찾아 내용을 갱신
            foreach (var tb in FindVisualChildren<TextBlock>(this))
            {
                if (tb.Text.Contains("이동") && tb.Text.Contains("영역설정") || tb.Text.Contains("자동번역"))
                {
                    tb.Inlines.Clear();
                    tb.Inlines.Add(new Run(newGuide));
                    if (isAutoTranslating)
                    {
                        // 자동 번역 활성화 시 강조 표시
                        tb.Inlines.Add(new Run("  ● 자동 번역 중...") { Foreground = Brushes.Lime, FontWeight = FontWeights.Bold });
                    }
                    break;
                }
            }
        }

        // UI 계층 구조(Visual Tree)에서 특정 타입의 자식 요소를 재귀적으로 찾는 유틸리티 함수
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

        // "Ctrl+Shift+9" 같은 문자열을 파싱하여 조합키(modifier)와 가상키(vk)로 분리하는 함수
        private void ParseHotkey(string hotkeyStr, out uint modifier, out uint vk)
        {
            modifier = 0; vk = 0;
            if (string.IsNullOrEmpty(hotkeyStr)) return;

            hotkeyStr = hotkeyStr.ToUpper().Replace(" ", "");
            if (hotkeyStr.Contains("CTRL+")) { modifier |= MOD_CONTROL; hotkeyStr = hotkeyStr.Replace("CTRL+", ""); }
            if (hotkeyStr.Contains("ALT+")) { modifier |= 0x0001; hotkeyStr = hotkeyStr.Replace("ALT+", ""); }
            if (hotkeyStr.Contains("SHIFT+")) { modifier |= 0x0004; hotkeyStr = hotkeyStr.Replace("SHIFT+", ""); }

            // 숫자키는 WPF Key Enum 규격에 맞게 앞에 'D'를 붙임 (예: 1 -> D1)
            if (Regex.IsMatch(hotkeyStr, @"^[0-9]$")) hotkeyStr = "D" + hotkeyStr;
            if (hotkeyStr == "~" || hotkeyStr == "`" || hotkeyStr == "TILDE") { vk = 0xC0; return; }

            // 문자열을 가상키 코드로 변환
            if (Enum.TryParse(hotkeyStr, true, out Key wpfKey)) { vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey); }
        }

        // ==========================================
        // 📌 6. 윈도우 메시지 후킹 (OS 신호 가로채기)
        // ==========================================
        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // OS에서 단축키가 눌렸다는 신호(WM_HOTKEY)가 오면 캐치
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

        // 프로그램 종료 시 자원 정리 (단축키 해제 및 테두리 창 닫기)
        protected override void OnClosed(EventArgs e)
        {
            captureBorderWindow?.Close(); // 캡처 테두리 창 메모리 해제

            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);
            base.OnClosed(e);
        }

        // ==========================================
        // 📌 7. UI 제어 및 기능 토글 로직
        // ==========================================

        // 창 상단을 드래그하여 이동 (잠금 해제 상태일 때만)
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!isLocked) this.DragMove(); }

        // [이동/잠금] 단축키 로직: 클릭 관통 모드와 테두리 표시 전환
        private void ToggleMoveLock()
        {
            isLocked = !isLocked;
            if (isLocked)
            {
                // 게임 모드: 클릭 통과, 테두리 숨김
                WindowUtils.SetClickThrough(this);
                MainBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#55FFFFFF"));
                UpdateCaptureBorder(false);
            }
            else
            {
                // 설정 모드: 클릭 가능, 테두리 초록/빨강 표시
                WindowUtils.RemoveClickThrough(this);
                MainBorder.BorderBrush = Brushes.LimeGreen;
                UpdateCaptureBorder(true);
            }
        }

        // [자동 번역] 단축키 로직: 타이머 ON/OFF
        private void ToggleAutoTranslate()
        {
            if (gameChatArea == Rectangle.Empty) return; // 캡처 영역 없으면 무시
            isAutoTranslating = !isAutoTranslating;
            UpdateYellowHotkeyGuideText(); // UI 텍스트 갱신

            if (isAutoTranslating)
            {
                runTranslation();          // 즉시 한 번 실행하고
                autoTranslateTimer.Start();// 타이머 시작
            }
            else
            {
                autoTranslateTimer.Stop(); // 타이머 중지
            }
        }

        // [영역 설정] 단축키 로직: 반투명 드래그 창 띄우기
        private void startAreaSelection()
        {
            if (areaSelector != null) { areaSelector.Close(); }
            areaSelector = new AreaSelector();
            areaSelector.Owner = this;
            areaSelector.Show();
        }

        // AreaSelector에서 드래그가 끝난 후 호출되어 캡처 영역을 적용하는 함수
        public void SetCaptureArea(Rectangle area)
        {
            gameChatArea = area;

            // 번역창을 캡처 영역의 아래쪽에 배치
            this.Top = area.Y + area.Height + 50;
            this.Left = area.X - 5;

            // 창 크기 동기화 및 쪼그라듦 방지 강제 적용
            this.SizeToContent = SizeToContent.Manual;
            this.Width = area.Width;
            this.MinWidth = area.Width;
            this.SizeToContent = SizeToContent.Height;

            this.Visibility = Visibility.Visible;
            this.Topmost = true;
            UpdateYellowHotkeyGuideText();

            // 설정된 좌표와 크기를 INI 파일에 즉시 저장
            ini.Write("CaptureX", area.X.ToString());
            ini.Write("CaptureY", area.Y.ToString());
            ini.Write("CaptureW", area.Width.ToString());
            ini.Write("CaptureH", area.Height.ToString());
            ini.Write("WindowW", this.ActualWidth.ToString());
            ini.Write("WindowH", this.ActualHeight.ToString());

            // 붉은 테두리 창 위치도 갱신
            UpdateCaptureBorder(!isLocked);
        }

        // 쪼개진 글자(Word)들을 묶어서 한 줄(Line)로 판별하기 위한 데이터 구조체
        private class MergedLine
        {
            public double Top;
            public double Bottom;
            public string Text;
        }

        // 구글 번역 API 호출 전, 이미 목표 언어인지 체크하여 번역을 스킵(최적화)하는 함수
        private bool IsSameLanguage(string text, string tLang)
        {
            // 한국어는 연속된 2글자 이상 한글일 때만 통과 (특수문자 오인 방지)
            if (tLang == "ko" && Regex.IsMatch(text, @"[가-힣]{2,}")) return true;

            if (tLang == "ru" && Regex.IsMatch(text, @"[а-яА-ЯёЁ]")) return true;
            if (tLang == "ja" && Regex.IsMatch(text, @"[ぁ-んァ-ヶ]")) return true;
            if (tLang == "zh-Hans-CN" && Regex.IsMatch(text, @"[\u4e00-\u9fa5]")) return true;

            // 영어는 알파벳이 3글자 이상이면서 다른 아시아/러시아 문자가 없을 때만 인정
            if (tLang == "en-US" && Regex.IsMatch(text, @"[a-zA-Z]") && !Regex.IsMatch(text, @"[가-힣а-яА-ЯёЁぁ-んァ-ヶ\u4e00-\u9fa5]")) return true;

            return false;
        }

        // 캡처 영역을 가시적으로 보여주는 붉은 테두리 윈도우 생성/조작 함수
        private void UpdateCaptureBorder(bool show)
        {
            if (gameChatArea == Rectangle.Empty) return;

            if (captureBorderWindow == null)
            {
                // UI XAML 없이 코드만으로 뼈대뿐인 투명 윈도우 생성
                captureBorderWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    BorderBrush = Brushes.Red, // 붉은색 식별 테두리
                    BorderThickness = new Thickness(2),
                    Opacity = 0.8
                };
                captureBorderWindow.Show();
                WindowUtils.SetClickThrough(captureBorderWindow); // 테두리를 클릭해도 게임으로 관통되게 설정
            }

            // 테두리 창을 캡처 영역에 딱 맞게 배치
            captureBorderWindow.Left = gameChatArea.X;
            captureBorderWindow.Top = gameChatArea.Y;
            captureBorderWindow.Width = gameChatArea.Width;
            captureBorderWindow.Height = gameChatArea.Height;

            captureBorderWindow.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }

        // Google Translate API 규격에 맞게 언어 코드를 변환 (zh-Hans-CN -> zh-CN)
        private string GetGoogleTransLangCode(string lang)
        {
            if (lang == "zh-Hans-CN") return "zh-CN";
            if (lang == "en-US") return "en";
            return lang; // ko, ja, ru는 그대로 사용 가능
        }

        // 🌟 [추가] 번역 내역을 텍스트 파일로 기록하는 함수
        private void AppendLog(string original, string translated)
        {
            try
            {
                // 1. 실행 폴더 내 'logs' 폴더 경로 설정 및 생성
                string logDirPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!System.IO.Directory.Exists(logDirPath))
                    System.IO.Directory.CreateDirectory(logDirPath);

                // 2. 파일명은 날짜별로 생성 (예: log_20260419.txt)
                string fileName = $"log_{DateTime.Now:yyyyMMdd}.txt";
                string filePath = System.IO.Path.Combine(logDirPath, fileName);

                // 3. 기록할 포맷 설정: [20:30:05] 원문 -> 번역문
                string logEntry = $"[{DateTime.Now:HH:mm:ss}] {original.Trim()} -> {translated.Trim()}{Environment.NewLine}";

                // 4. 파일 끝에 내용 이어쓰기 (파일이 없으면 생성함)
                System.IO.File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { /* 로그 기록 실패 시 게임 방해 방지를 위해 무시 */ }
        }

        // ==========================================
        // 📌 8. 메인 번역 로직 (OCR -> 병합 -> 필터링 -> 번역 -> 출력)
        // ==========================================
        private async void runTranslation()
        {
            // 중복 실행 방지 및 영역 설정 여부 확인
            if (isTranslating || gameChatArea == Rectangle.Empty) return;
            isTranslating = true;

            // 배경(검은색)과 글씨(흰색)를 구분하는 이진화 기준값 (스트리노바 황금비율: 80)
            int threshold = int.TryParse(ini.Read("Threshold"), out int t) ? t : 80;

            // 🌟 [추가] 배율 설정 읽기 (기본값 3, 1~4배율 사이로 안전하게 제한)
            int scaleFactor = int.TryParse(ini.Read("ScaleFactor"), out int s) ? s : 3;
            if (scaleFactor < 1) scaleFactor = 1;
            if (scaleFactor > 4) scaleFactor = 4;

            try
            {
                // 1. 화면 지정 영역 캡처
                using Bitmap rawBitmap = new Bitmap(gameChatArea.Width, gameChatArea.Height);
                using (Graphics g = Graphics.FromImage(rawBitmap))
                {
                    g.CopyFromScreen(gameChatArea.Location, System.Drawing.Point.Empty, gameChatArea.Size);
                }

                // 2. OCR 인식률 향상을 위해 해상도 뻥튀기 (사용자가 설정한 scaleFactor 적용)
                int newWidth = rawBitmap.Width * scaleFactor;
                int newHeight = rawBitmap.Height * scaleFactor;
                using Bitmap resizedBitmap = new Bitmap(newWidth, newHeight);
                using (Graphics g = Graphics.FromImage(resizedBitmap))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(rawBitmap, 0, 0, newWidth, newHeight);
                }

                // 3. 흑백 이진화 처리 (Threshold 적용)
                BitmapData bmpData = resizedBitmap.LockBits(new Rectangle(0, 0, resizedBitmap.Width, resizedBitmap.Height), ImageLockMode.ReadWrite, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                int bytes = Math.Abs(bmpData.Stride) * resizedBitmap.Height;
                byte[] rgbValues = new byte[bytes];
                Marshal.Copy(bmpData.Scan0, rgbValues, 0, bytes);

                for (int i = 0; i < rgbValues.Length; i += 4)
                {
                    byte b = rgbValues[i]; byte g = rgbValues[i + 1]; byte r = rgbValues[i + 2];
                    int maxRGB = Math.Max(r, Math.Max(g, b));
                    // 가장 밝은 채널이 임계값(80)보다 크면 흰색(255), 작으면 검은색(0)
                    byte color = maxRGB > threshold ? (byte)255 : (byte)0;
                    rgbValues[i] = color; rgbValues[i + 1] = color; rgbValues[i + 2] = color; rgbValues[i + 3] = 255;
                }
                Marshal.Copy(rgbValues, 0, bmpData.Scan0, bytes);
                resizedBitmap.UnlockBits(bmpData);

                // 4. (디버깅용) 변환된 최종 이미지를 파일로 저장
                try
                {
                    string debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug");
                    if (!Directory.Exists(debugPath)) Directory.CreateDirectory(debugPath);
                    string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_debug.png";
                    //resizedBitmap.Save(System.IO.Path.Combine(debugPath, fileName), ImageFormat.Png);
                }
                catch { /* 권한 문제 등으로 실패 시 무시 */ }

                // 5. Bitmap을 UWP OCR 엔진이 읽을 수 있는 SoftwareBitmap 포맷으로 변환
                using MemoryStream ms = new MemoryStream();
                resizedBitmap.Save(ms, ImageFormat.Png);
                ms.Position = 0;
                var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
                SoftwareBitmap softwareBitmap = await decoder.GetSoftwareBitmapAsync();

                // 6. 비동기로 다중 OCR 엔진 병렬 판독 수행
                var ocrResults = new Dictionary<string, OcrResult>();
                foreach (var kvp in ocrEngines)
                {
                    ocrResults.Add(kvp.Key, await kvp.Value.RecognizeAsync(softwareBitmap));
                }

                // 7. 게임 기준 언어(마스터 엔진) 결과 추출
                var masterResult = ocrResults.ContainsKey(gameLang) ? ocrResults[gameLang] : (ocrResults.ContainsKey("ko") ? ocrResults["ko"] : null);
                if (masterResult == null) return;

                // 8. 줄(Line) 병합 (본드) 알고리즘
                // 넓은 띄어쓰기로 인해 파편화된 조각들을 Y좌표(높이) 기준으로 한 줄로 결합
                var mergedLines = new List<MergedLine>();
                foreach (var mLine in masterResult.Lines)
                {
                    if (mLine.Words.Count == 0) continue;
                    double top = mLine.Words.Min(w => w.BoundingRect.Top);
                    double bot = mLine.Words.Max(w => w.BoundingRect.Bottom);
                    string text = mLine.Text.Trim();

                    // Y축 오차가 15픽셀 이내면 같은 줄로 판별하여 이어붙임
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

                // 9. 화면 갱신 최적화 (이전 캡처와 텍스트가 완전히 똑같으면 번역 스킵)
                string currentRawTextCombined = string.Join("\n", mergedLines.Select(l => l.Text.Trim()));
                if (currentRawTextCombined == lastRawTextCombined) return;
                lastRawTextCombined = currentRawTextCombined;

                TxtResult.Inlines.Clear(); // 화면 초기화

                // 10. 각 채팅 라인별 분석 및 번역 진행
                foreach (var chatLine in mergedLines)
                {
                    string krRawText = chatLine.Text.Trim();

                    // 쓰레기 데이터 필터링: [닉네임]: 형식이 아니면(시스템 메시지 등) 번역 패스
                    var strictMatch = Regex.Match(krRawText, @"^([^:]*\[[^\]]+\]\s*[:;：!])(.*)$");
                    if (!strictMatch.Success) continue;

                    string characterNameGold = strictMatch.Groups[1].Value + " "; // [캐릭터]: (원문 유지 부분)
                    string bestMessage = strictMatch.Groups[2].Value.Trim();      // 번역할 채팅 내용 부분
                    int bestScore = -1;

                    // 서브 엔진 매칭을 위한 해당 라인의 높이 패딩 (+-5 오차 허용)
                    double mTop = chatLine.Top - 5;
                    double mBot = chatLine.Bottom + 5;

                    // 11. 다중 엔진 점수제 로직 (러시아어/일본어/중국어 특화 판독)
                    foreach (var kvp in ocrResults)
                    {
                        // 서브 엔진들도 해당 높이에 위치한 모든 단어 조각들을 X좌표 순서대로 정렬하여 긁어모음
                        var linesInBand = kvp.Value.Lines
                            .Where(l => l.Words.Count > 0 && l.Words.Any(w => w.BoundingRect.Bottom > mTop && w.BoundingRect.Top < mBot))
                            .OrderBy(l => l.Words.First().BoundingRect.Left);

                        if (linesInBand.Any())
                        {
                            string fullText = string.Join(" ", linesInBand.Select(l => l.Text.Trim()));
                            string msgOnly = fullText;

                            // 닉네임 제거하고 순수 채팅 내용만 추출
                            int subCIdx = fullText.IndexOfAny(new char[] { ':', ';', '：' });
                            if (subCIdx != -1) msgOnly = fullText.Substring(subCIdx + 1).Trim();
                            else
                            {
                                int brIdx = fullText.LastIndexOf(']');
                                if (brIdx != -1) msgOnly = fullText.Substring(brIdx + 1).Trim();
                            }

                            // 언어별 가중치 점수 부여
                            int score = msgOnly.Length;
                            if (kvp.Key == "ru" && Regex.Matches(msgOnly, @"[а-яА-ЯёЁ]").Count >= 1) score += 20000;
                            else if (kvp.Key == "ja" && Regex.Matches(msgOnly, @"[ぁ-んァ-ヶ]").Count >= 1) score += 10000;
                            else if (kvp.Key == "zh-Hans-CN" && Regex.Matches(msgOnly, @"[\u4e00-\u9fa5]").Count >= 1) score += 5000;
                            else if (kvp.Key == "en-US" && Regex.Matches(msgOnly, @"[a-zA-Z]").Count >= 3) score += 1000;

                            // 최고 점수를 받은 엔진의 텍스트를 채택
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestMessage = msgOnly;
                            }
                        }
                    }

                    // 예외 처리 (만약 서브 엔진에서 내용을 못 찾으면 기본 마스터 엔진 텍스트 사용)
                    if (string.IsNullOrEmpty(bestMessage)) bestMessage = strictMatch.Groups[2].Value.Trim();
                    string translated = bestMessage;

                    // 12. 구글 번역 API 연동 (숫자나 특수기호만 있으면 번역 스킵)
                    if (!string.IsNullOrEmpty(bestMessage) && !Regex.IsMatch(bestMessage, @"^[0-9\W]+$"))
                    {
                        // 스마트 번역 스킵: 이미 목표 언어로 작성된 채팅이면 API 낭비 방지
                        if (IsSameLanguage(bestMessage, targetLang))
                        {
                            translated = bestMessage;
                        }
                        else
                        {
                            int retryCount = 3;
                            string targetApiLang = GetGoogleTransLangCode(targetLang);

                            // API 호출 실패 시 최대 3번 재시도 (안정성)
                            while (retryCount > 0)
                            {
                                try
                                {
                                    string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={targetApiLang}&dt=t&q={Uri.EscapeDataString(bestMessage)}";
                                    var res = await httpClient.GetStringAsync(url);

                                    using var doc = System.Text.Json.JsonDocument.Parse(res);
                                    translated = "";

                                    // JSON 응답에서 번역된 텍스트 조각들을 모두 합침
                                    foreach (var item in doc.RootElement[0].EnumerateArray()) { translated += item[0].GetString(); }
                                    break; // 통신 성공 시 루프 탈출
                                }
                                catch
                                {
                                    retryCount--;
                                    await Task.Delay(300); // 실패 시 0.3초 대기
                                }
                            }
                        }
                    }

                    // 🌟 로그 파일에 기록 (닉네임+원문, 번역문)
                    AppendLog(characterNameGold + bestMessage, translated);

                    // 13. UI 출력 (닉네임은 금색, 번역된 채팅은 흰색)
                    TxtResult.Inlines.Add(new Run(characterNameGold) { Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
                    TxtResult.Inlines.Add(new Run(translated) { Foreground = Brushes.White });
                    TxtResult.Inlines.Add(new LineBreak());
                }
            }
            catch (Exception ex)
            {
                // 치명적인 오류 발생 시 에러 메시지 표출
                TxtResult.Text = "에러: " + ex.Message;
            }
            finally
            {
                // 번역 완료 후 잠금 해제 (다음 번역 허용)
                isTranslating = false;
            }
        }
    }
}