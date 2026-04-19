using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        // ==========================================
        // 📌 1. 생성자 (초기화)
        // 메인 창에서 이 설정창을 띄울 때 MainWindow 자기 자신과 ini 객체를 넘겨줍니다.
        // ==========================================
        public OptionSelector(MainWindow mainWindow, IniFile ini)
        {
            InitializeComponent(); // XAML 디자인 UI 요소들을 메모리에 로드
            _mainWindow = mainWindow;
            _ini = ini;

            LoadCurrentSettings(); // 창이 켜지자마자 INI 파일에서 기존 설정값을 불러옴
        }

        // ==========================================
        // 📌 2. 기존 설정 불러오기 (UI 초기 세팅)
        // ==========================================
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
            TxtKeyMove.Text = _ini.Read("Key_MoveLock") ?? "Ctrl+7";
            TxtKeyArea.Text = _ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            TxtKeyTrans.Text = _ini.Read("Key_Translate") ?? "Ctrl+9";
            TxtKeyAuto.Text = _ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            TxtKeyToggle.Text = _ini.Read("Key_ToggleEngine") ?? "Ctrl+-"; // 🌟 추가

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
        }

        // ==========================================
        // 📌 3. 콤보박스 아이템 자동 선택 도우미 함수
        // ==========================================
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

        // ==========================================
        // 📌 4. 투명도 슬라이더 변경 이벤트 (실시간 적용)
        // ==========================================
        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtOpacityInfo != null)
            {
                TxtOpacityInfo.Text = $"번역창 불투명도: {(int)e.NewValue}%";
                if (_mainWindow != null) _mainWindow.Opacity = e.NewValue / 100.0;
            }
        }

        // ==========================================
        // 📌 5. 단축키 자동 입력기 (스마트 키보드 캡처)
        // ==========================================
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
            tb.Text = modifierStr + key.ToString();
        }

        // ==========================================
        // 📌 6. 캡처 영역 초기화 버튼 이벤트
        // ==========================================
        private void BtnResetArea_Click(object sender, RoutedEventArgs e)
        {
            _ini.Write("CaptureX", "");
            _ini.Write("CaptureY", "");
            _ini.Write("CaptureW", "");
            _ini.Write("CaptureH", "");

            TxtAreaInfo.Text = "저장된 영역: 없음 (좌측 하단 기본값)";
            System.Windows.MessageBox.Show("영역 좌표가 초기화되었습니다.\n게임 실행 시 기본 위치(좌측 하단)로 배치됩니다.", "초기화 완료", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================================
        // 📌 7. [저장 및 게임 시작] 버튼 이벤트
        // ==========================================
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
            _ini.Write("GeminiKey", PasswordGeminiKey?.Password?.Trim() ?? "");

            string geminiModel = TxtGeminiModel?.Text?.Trim();
            _ini.Write("GeminiModel", string.IsNullOrWhiteSpace(geminiModel) ? MainWindow.DefaultGeminiModel : geminiModel);

            // DialogResult를 true로 설정하여 메인 창(MainWindow)에 정상 종료되었음을 알리고 창을 닫습니다.
            this.DialogResult = true;
            this.Close();
        }
    }
}
