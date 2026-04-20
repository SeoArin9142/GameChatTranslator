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
    /// 번역 오버레이 메인 창의 공유 상태와 생성자 초기화를 담는 partial 클래스입니다.
    /// 실제 기능은 Capture/Hotkeys/Lifecycle/Settings/Translation 등 역할별 partial 파일로 분리되어 있습니다.
    /// </summary>
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

        private uint modMove, modArea, modTrans, modAuto, modToggle, modCopy, modLog, modOcrDiag, modHotkeyGuide;
        private uint keyMove, keyArea, keyTrans, keyAuto, keyToggle, keyCopy, keyLog, keyOcrDiag, keyHotkeyGuide;

        private bool useGeminiEngine = false; // 🌟 [추가] 현재 제미나이를 사용 중인지 상태 저장

        private DispatcherTimer autoTranslateTimer;
        // 🌟 [추가] 최상단 강제 유지 타이머
        private DispatcherTimer topmostTimer;
        private DispatcherTimer apiStatusTimer;
        private DispatcherTimer autoModeStatusTimer;

        private AutoTranslateMode autoTranslateMode = AutoTranslateMode.Off;
        private bool isAutoTranslating = false;
        private bool isHotkeyGuideExpanded = false;
        private string lastRawTextCombined = "";
        private string hotkeyWarningMessage = "";

        private HttpClient httpClient = new HttpClient();
        private readonly OcrService ocrService = new OcrService();
        private readonly OcrImagePreprocessor ocrImagePreprocessor = new OcrImagePreprocessor();
        private readonly SettingsService settingsService = new SettingsService();
        private readonly TranslationPromptBuilder translationPromptBuilder = new TranslationPromptBuilder();
        private readonly TranslationResultParser translationResultParser = new TranslationResultParser();
        private readonly TranslationService translationService = new TranslationService();
        private readonly TranslationApiErrorDescriber translationApiErrorDescriber = new TranslationApiErrorDescriber();
        private readonly TranslationApiClient translationApiClient;
        private Dictionary<string, OcrEngine> ocrEngines = new Dictionary<string, OcrEngine>();
        private IntPtr _windowHandle;
        private LogViewerWindow logViewerWindow;
        private OcrDiagnosticWindow ocrDiagnosticWindow;

        private HashSet<string> characterNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string sessionLogFileName;


        // ==========================================
        // 📌 3. 초기화 (생성자)
        // ==========================================
        /// <summary>
        /// 메인 번역창을 생성하고 실행에 필요한 공통 리소스를 초기화합니다.
        /// INI 파일, OCR 엔진, HTTP 클라이언트, 자동 번역 타이머, 최상단 유지 타이머를 준비합니다.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();

            // 🌟 [추가] 켜진 시간을 기준으로 로그 파일명 고정 (예: log_20260419_1635.txt)
            // 초(ss)까지 넣고 싶으시면 yyyyMMdd_HHmmss 로 변경하시면 됩니다.
            sessionLogFileName = $"log_{DateTime.Now:yyyyMMdd_HHmm}.txt";

            LoadCharacters();

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
            translationApiClient = new TranslationApiClient(httpClient, translationPromptBuilder, translationResultParser);

            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.ini");
            ini = new IniFile(iniPath);

            EnsureDefaultSettings();

            gameLang = ini.Read("GameLanguage") ?? "ko";
            targetLang = ini.Read("TargetLanguage") ?? "ko";
            int intervalSeconds = SettingsValueNormalizer.NormalizeAutoTranslateInterval(ini.Read("AutoTranslateInterval"));

            autoTranslateTimer = new DispatcherTimer();
            autoTranslateTimer.Interval = TimeSpan.FromSeconds(intervalSeconds);
            autoTranslateTimer.Tick += (s, e) => { if (!isTranslating) runTranslation(GetCurrentOcrProcessingMode()); };

            string[] tags = { "ko", "en-US", "zh-Hans-CN", "ja", "ru" };
            foreach (var tag in tags)
            {
                // Windows OCR 언어팩이 설치된 언어만 엔진 생성에 성공합니다.
                // 실패한 언어는 실행 후 안내 메시지나 README의 LangInstall.bat 설명으로 보완합니다.
                var engine = OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language(tag));
                if (engine != null) ocrEngines.Add(tag, engine);
            }

            // 🌟 [추가] 2초마다 창을 최상단으로 강제 끌어올리는 타이머 시작
            topmostTimer = new DispatcherTimer();
            topmostTimer.Interval = TimeSpan.FromSeconds(2);
            topmostTimer.Tick += (s, e) => ForceTopmost();
            topmostTimer.Start();

            apiStatusTimer = new DispatcherTimer();
            apiStatusTimer.Interval = TimeSpan.FromSeconds(7);
            apiStatusTimer.Tick += (s, e) => HideTranslationApiStatus();

            autoModeStatusTimer = new DispatcherTimer();
            autoModeStatusTimer.Interval = TimeSpan.FromSeconds(2.5);
            autoModeStatusTimer.Tick += (s, e) => HideAutoModeStatus();
        }
    }
}
