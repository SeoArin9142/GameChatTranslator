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
        // 📌 2. 전역 변수 (UI, 캡처, OCR, API 관련)
        // ==========================================
        private AreaSelector areaSelector;
        private Rectangle gameChatArea;
        private Rectangle gameChatCaptureArea;
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
    }
}
