using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace GameTranslator
{
    // ==========================================
    // 📌 프로그램 실행 시 가장 먼저 표시되는 "환경 설정" 창 (UI 비하인드 코드)
    // 사용자의 설정(언어, 투명도, 단축키)을 입력받아 INI 파일에 저장하거나 불러옵니다.
    // ==========================================
    public partial class OptionSelector : Window
    {
        // 메인 윈도우의 인스턴스를 보관 (투명도 조절 시 메인 윈도우에 실시간 반영하기 위함)
        private MainWindow _mainWindow;
        // 환경설정 파일(config.ini)을 읽고 쓰기 위한 객체
        private IniFile _ini;

        private const string DefaultKeyMoveLock = "Ctrl+7";
        private const string DefaultKeyAreaSelect = "Ctrl+8";
        private const string DefaultKeyTranslate = "Ctrl+9";
        private const string DefaultKeyAutoTranslate = "Ctrl+0";
        private const string DefaultKeyToggleEngine = "Ctrl+-";
        private const string DefaultKeyCopyResult = "Ctrl+6";
        private const string DefaultKeyLogViewer = "Ctrl+=";
        private static readonly (string Label, string Tag)[] OcrLanguageStatusTargets =
        {
            ("한국어", "ko"),
            ("영어", "en-US"),
            ("중국어 간체", "zh-Hans-CN"),
            ("일본어", "ja"),
            ("러시아어", "ru")
        };

        /// <summary>
        /// 환경설정 창을 생성하고 현재 설정값과 프리셋 목록을 UI에 반영합니다.
        /// <paramref name="mainWindow"/>는 투명도 미리보기와 업데이트 확인을 호출할 메인 창 인스턴스이고,
        /// <paramref name="ini"/>는 config.ini 읽기/쓰기를 담당하는 설정 파일 객체입니다.
        /// </summary>
        public OptionSelector(MainWindow mainWindow, IniFile ini)
        {
            InitializeComponent(); // XAML 디자인 UI 요소들을 메모리에 로드
            _mainWindow = mainWindow;
            _ini = ini;

            LoadCurrentSettings(); // 창이 켜지자마자 INI 파일에서 기존 설정값을 불러옴
            LoadPresetList();
            RefreshOcrLanguageStatus();
        }

        /// <summary>
        /// config.ini에 저장된 값을 읽어 환경설정창의 모든 입력 컨트롤에 채웁니다.
        /// 언어, 투명도, 단축키, 캡처 영역, OCR 옵션, 업데이트 확인, Gemini 설정을 한 번에 초기화합니다.
        /// </summary>
        private void LoadCurrentSettings()
        {
            // [언어 콤보박스 세팅]
            // INI에서 값을 읽어오되, 만약 값이 비어있다면(??) 기본값으로 "ko"(한국어)를 사용합니다.
            string gLang = _ini.Read("GameLanguage") ?? "ko";
            string tLang = _ini.Read("TargetLanguage") ?? "ko";

            // 읽어온 태그(ko, en-US 등)를 바탕으로 콤보박스의 항목을 선택 상태로 만듭니다.
            SetComboByTag(ComboGameLang, gLang);
            SetComboByTag(ComboTargetLang, tLang);

            // [투명도 세팅]
            // INI에서 숫자로 된 투명도 값을 안전하게 파싱하여 슬라이더바(Slider)의 현재 위치에 적용합니다.
            if (int.TryParse(_ini.Read("Opacity"), out int op))
            {
                SliderOpacity.Value = op;
            }

            // [단축키 세팅]
            // 각 텍스트박스에 기존에 저장된 단축키 문자열을 넣어줍니다. (없으면 기본값 적용)
            TxtKeyMove.Text = _ini.Read("Key_MoveLock") ?? DefaultKeyMoveLock;
            TxtKeyArea.Text = _ini.Read("Key_AreaSelect") ?? DefaultKeyAreaSelect;
            TxtKeyTrans.Text = _ini.Read("Key_Translate") ?? DefaultKeyTranslate;
            TxtKeyAuto.Text = _ini.Read("Key_AutoTranslate") ?? DefaultKeyAutoTranslate;
            TxtKeyToggle.Text = _ini.Read("Key_ToggleEngine") ?? DefaultKeyToggleEngine;
            TxtKeyCopy.Text = _ini.Read("Key_CopyResult") ?? DefaultKeyCopyResult;
            TxtKeyLog.Text = _ini.Read("Key_LogViewer") ?? DefaultKeyLogViewer;

            // [캡처 영역 세팅]
            // 메인 폼에서 사용자가 드래그하여 저장했던 X, Y 좌표와 넓이, 높이를 읽어옵니다.
            string cX = _ini.Read("CaptureX");
            string cY = _ini.Read("CaptureY");
            string cW = _ini.Read("CaptureW");
            string cH = _ini.Read("CaptureH");

            // 가로(W)와 세로(H) 값이 정상적으로 존재한다면 UI 텍스트에 해당 좌표를 표시합니다.
            if (!string.IsNullOrEmpty(cW) && !string.IsNullOrEmpty(cH))
            {
                TxtAreaInfo.Text = $"저장된 영역: X:{cX}, Y:{cY} (크기: {cW}x{cH})";
            }
            else
            {
                TxtAreaInfo.Text = "저장된 영역: 없음 (좌측 하단 기본값)";
            }

            // 🌟 [수정] 배율 세팅 불러오기 (빈칸 및 위치 날아감 버그 완벽 해결)
            string scale = _ini.Read("ScaleFactor") ?? "3";
            SetComboByTag(ComboScale, scale); // 기존의 꼬이던 foreach 문을 지우고 안전한 함수로 교체

            // [추가] 임계값(Threshold) 및 자동 번역 주기 불러오기
            // UI에 TxtThreshold, TxtInterval 텍스트박스가 있다고 가정합니다.
            if (TxtThreshold != null) TxtThreshold.Text = _ini.Read("Threshold") ?? "120";
            if (TxtInterval != null) TxtInterval.Text = _ini.Read("AutoTranslateInterval") ?? "5";

            string geminiKey = _ini.Read("GeminiKey") ?? "";
            if (string.IsNullOrWhiteSpace(geminiKey))
            {
                geminiKey = _ini.Read("GeminiKey", "GeminiKey") ?? "";
            }

            if (PasswordGeminiKey != null) PasswordGeminiKey.Password = geminiKey.Trim();
            if (TxtGeminiModel != null) TxtGeminiModel.Text = _ini.Read("GeminiModel") ?? MainWindow.DefaultGeminiModel;

            string saveDebugImages = _ini.Read("SaveDebugImages") ?? "false";
            if (CheckSaveDebugImages != null)
            {
                CheckSaveDebugImages.IsChecked =
                    saveDebugImages.Equals("true", System.StringComparison.OrdinalIgnoreCase) ||
                    saveDebugImages == "1";
            }

            string checkUpdatesOnStartup = _ini.Read("CheckUpdatesOnStartup") ?? "true";
            if (CheckUpdatesOnStartup != null)
            {
                CheckUpdatesOnStartup.IsChecked =
                    !checkUpdatesOnStartup.Equals("false", System.StringComparison.OrdinalIgnoreCase) &&
                    checkUpdatesOnStartup != "0";
            }
        }

        /// <summary>
        /// ComboBoxItem.Tag 값과 일치하는 항목을 선택합니다.
        /// <paramref name="combo"/>는 선택을 적용할 콤보박스이고,
        /// <paramref name="tag"/>는 찾을 언어 코드 또는 설정값입니다.
        /// 값이 없으면 첫 번째 항목을 선택해 UI가 빈 상태로 남지 않게 합니다.
        /// </summary>
        private void SetComboByTag(System.Windows.Controls.ComboBox combo, string tag)
        {
            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag.ToString() == tag)
                {
                    combo.SelectedItem = item;
                    break;
                }
            }
            if (combo.SelectedItem == null && combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        /// <summary>
        /// Windows OCR 엔진을 언어별로 생성해 설치 상태를 UI에 표시합니다.
        /// 설치된 언어는 OcrEngine.TryCreateFromLanguage가 null이 아닌 값을 반환합니다.
        /// </summary>
        private void RefreshOcrLanguageStatus()
        {
            if (TxtOcrLanguageStatus == null) return;

            var lines = new System.Collections.Generic.List<string>();
            foreach ((string label, string tag) in OcrLanguageStatusTargets)
            {
                bool installed = IsOcrLanguageInstalled(tag);
                string marker = installed ? "OK" : "NO";
                string status = installed ? "설치됨" : "미설치";
                lines.Add($"{marker}  {label,-8} ({tag}) : {status}");
            }

            TxtOcrLanguageStatus.Text = string.Join(System.Environment.NewLine, lines);
        }

        /// <summary>
        /// 지정한 Windows OCR 언어팩이 현재 OS에서 사용 가능한지 확인합니다.
        /// <paramref name="languageTag"/>는 ko, en-US, zh-Hans-CN, ja, ru 같은 Windows 언어 태그입니다.
        /// </summary>
        private bool IsOcrLanguageInstalled(string languageTag)
        {
            try
            {
                return OcrEngine.TryCreateFromLanguage(new Language(languageTag)) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 번역창 불투명도 슬라이더 값이 바뀔 때 메인 창에 즉시 반영합니다.
        /// <paramref name="sender"/>는 Slider 컨트롤이고,
        /// <paramref name="e"/>는 이전/새 Slider 값이 들어 있는 변경 이벤트 정보입니다.
        /// </summary>
        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacityInfo != null)
            {
                TxtOpacityInfo.Text = $"번역창 불투명도: {(int)e.NewValue}%";
                if (_mainWindow != null) _mainWindow.Opacity = e.NewValue / 100.0;
            }
        }

        /// <summary>
        /// 단축키 입력용 TextBox가 포커스를 가진 상태에서 누른 키 조합을 "Ctrl+9" 같은 문자열로 변환합니다.
        /// <paramref name="sender"/>는 단축키를 입력받는 TextBox이고,
        /// <paramref name="e"/>는 사용자가 누른 키와 modifier 상태를 담은 키보드 이벤트입니다.
        /// </summary>
        private void Hotkey_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            Key key = (e.Key == Key.System ? e.SystemKey : e.Key);

            if (key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt || key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin)
                return;

            string modifierStr = "";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifierStr += "Ctrl+";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifierStr += "Alt+";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifierStr += "Shift+";

            System.Windows.Controls.TextBox tb = sender as System.Windows.Controls.TextBox;
            tb.Text = modifierStr + GetHotkeyDisplayKey(key);
        }

        /// <summary>
        /// WPF Key 값을 config.ini에 저장할 사람이 읽기 쉬운 키 이름으로 변환합니다.
        /// <paramref name="key"/>는 사용자가 누른 실제 키입니다.
        /// </summary>
        private string GetHotkeyDisplayKey(Key key)
        {
            return key switch
            {
                Key.OemPlus => "=",
                Key.OemMinus => "-",
                _ => key.ToString()
            };
        }

        /// <summary>
        /// 단축키 입력칸을 최초 기본 단축키 값으로 되돌립니다.
        /// 이 함수는 UI TextBox 값만 바꾸며 config.ini에는 쓰지 않습니다.
        /// 실제 저장은 사용자가 [저장 및 게임 시작] 버튼을 눌렀을 때 BtnSaveAndStart_Click에서 처리됩니다.
        /// </summary>
        private void ApplyDefaultHotkeyValues()
        {
            TxtKeyMove.Text = DefaultKeyMoveLock;
            TxtKeyArea.Text = DefaultKeyAreaSelect;
            TxtKeyTrans.Text = DefaultKeyTranslate;
            TxtKeyAuto.Text = DefaultKeyAutoTranslate;
            TxtKeyToggle.Text = DefaultKeyToggleEngine;
            TxtKeyCopy.Text = DefaultKeyCopyResult;
            TxtKeyLog.Text = DefaultKeyLogViewer;
        }

        /// <summary>
        /// [단축키 초기화] 버튼 클릭 시 모든 단축키 입력칸을 기본값으로 되돌립니다.
        /// <paramref name="sender"/>는 단축키 초기화 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnResetHotkeys_Click(object sender, RoutedEventArgs e)
        {
            ApplyDefaultHotkeyValues();
            System.Windows.MessageBox.Show(
                "단축키 입력칸을 기본값으로 되돌렸습니다.\n저장하려면 [저장 및 게임 시작] 버튼을 눌러주세요.",
                "단축키 초기화",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        /// <summary>
        /// 저장된 캡처 영역 좌표를 config.ini에서 제거해 다음 실행 시 기본 위치를 사용하게 합니다.
        /// <paramref name="sender"/>는 영역 초기화 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnResetArea_Click(object sender, RoutedEventArgs e)
        {
            _ini.Write("CaptureX", "");
            _ini.Write("CaptureY", "");
            _ini.Write("CaptureW", "");
            _ini.Write("CaptureH", "");

            TxtAreaInfo.Text = "저장된 영역: 없음 (좌측 하단 기본값)";
            System.Windows.MessageBox.Show("영역 좌표가 초기화되었습니다.\n게임 실행 시 기본 위치(좌측 하단)로 배치됩니다.", "초기화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 환경설정창의 현재 입력값을 config.ini에 저장하고 메인 번역창 실행을 허용합니다.
        /// <paramref name="sender"/>는 저장 및 게임 시작 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnSaveAndStart_Click(object sender, RoutedEventArgs e)
        {
            // 콤보박스에서 선택된 항목의 Tag 값(언어 코드)을 저장
            _ini.Write("GameLanguage", ((ComboBoxItem)ComboGameLang.SelectedItem).Tag.ToString());
            _ini.Write("TargetLanguage", ((ComboBoxItem)ComboTargetLang.SelectedItem).Tag.ToString());

            // 슬라이더의 투명도 값을 정수로 변환하여 저장
            _ini.Write("Opacity", ((int)SliderOpacity.Value).ToString());

            // 지정된 단축키 문자열 저장
            _ini.Write("Key_MoveLock", TxtKeyMove.Text);
            _ini.Write("Key_AreaSelect", TxtKeyArea.Text);
            _ini.Write("Key_Translate", TxtKeyTrans.Text);
            _ini.Write("Key_AutoTranslate", TxtKeyAuto.Text);
            _ini.Write("Key_ToggleEngine", TxtKeyToggle.Text); // 🌟 추가
            _ini.Write("Key_CopyResult", TxtKeyCopy.Text);
            _ini.Write("Key_LogViewer", TxtKeyLog.Text);

            // [배율 설정 저장]
            if (ComboScale.SelectedItem is ComboBoxItem scaleItem)
            {
                _ini.Write("ScaleFactor", scaleItem.Tag.ToString());
            }

            // [추가] 임계값(Threshold) 저장 (안전하게 숫자인지 검사 후 저장)
            if (TxtThreshold != null && int.TryParse(TxtThreshold.Text, out int threshold))
            {
                _ini.Write("Threshold", threshold.ToString());
            }
            else
            {
                _ini.Write("Threshold", "120"); // 숫자가 아니면 기본값 120 강제 지정
            }

            // [추가] 자동 번역 주기(Interval) 저장 (안전하게 숫자인지 검사 후 저장)
            if (TxtInterval != null && int.TryParse(TxtInterval.Text, out int interval))
            {
                _ini.Write("AutoTranslateInterval", interval.ToString());
            }
            else
            {
                _ini.Write("AutoTranslateInterval", "5"); // 숫자가 아니면 기본값 5 강제 지정
            }

            _ini.Write("SaveDebugImages", CheckSaveDebugImages?.IsChecked == true ? "true" : "false");
            _ini.Write("CheckUpdatesOnStartup", CheckUpdatesOnStartup?.IsChecked == true ? "true" : "false");
            _ini.Write("GeminiKey", PasswordGeminiKey?.Password?.Trim() ?? "");

            string geminiModel = TxtGeminiModel?.Text?.Trim();
            _ini.Write("GeminiModel", string.IsNullOrWhiteSpace(geminiModel) ? MainWindow.DefaultGeminiModel : geminiModel);

            // DialogResult를 true로 설정하여 메인 창(MainWindow)에 정상 종료되었음을 알리고 창을 닫습니다.
            this.DialogResult = true;
            this.Close();
        }

        /// <summary>
        /// 환경설정창의 업데이트 확인 버튼을 눌렀을 때 GitHub 릴리즈 최신 버전을 수동 확인합니다.
        /// <paramref name="sender"/>는 업데이트 확인 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            TxtUpdateStatus.Text = "확인 중...";

            try
            {
                await _mainWindow.RunManualUpdateCheckAsync(this, status => TxtUpdateStatus.Text = status);
            }
            finally
            {
                BtnCheckUpdate.IsEnabled = true;
            }
        }

        /// <summary>
        /// 환경설정창에서 로그창을 즉시 열어 현재 세션 로그를 확인합니다.
        /// <paramref name="sender"/>는 로그창 열기 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnOpenLogViewer_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.ShowLogViewerWindow();
        }

        /// <summary>
        /// [상태 새로고침] 버튼 클릭 시 Windows OCR 언어팩 설치 상태를 다시 확인합니다.
        /// <paramref name="sender"/>는 새로고침 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnRefreshOcrLanguages_Click(object sender, RoutedEventArgs e)
        {
            RefreshOcrLanguageStatus();
        }
    }
}
