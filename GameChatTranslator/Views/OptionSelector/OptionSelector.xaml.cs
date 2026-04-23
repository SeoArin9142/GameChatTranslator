using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
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
        private readonly SettingsService _settingsService = new SettingsService();
        private readonly OcrLanguageStatusFormatter _ocrLanguageStatusFormatter = new OcrLanguageStatusFormatter();
        private readonly DispatcherTimer _updateStatusResetTimer;
        internal OptionSelectorPostAction RequestedActionAfterClose { get; private set; } = OptionSelectorPostAction.None;

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
            _updateStatusResetTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _updateStatusResetTimer.Tick += UpdateStatusResetTimer_Tick;

            RegisterNumericSettingInputGuards();
            LoadCurrentSettings(); // 창이 켜지자마자 INI 파일에서 기존 설정값을 불러옴
            LoadPresetList();
            RefreshInstallLocationStatus();
            RefreshOcrLanguageStatus();
            RefreshAdvancedSettingValidationStatus();
        }

        /// <summary>
        /// 설정창이 닫힐 때 상태 문구 자동 초기화 타이머를 함께 정지합니다.
        /// 닫힌 창에 늦게 Tick이 도착하는 경우를 막아 상태 갱신 흐름을 정리합니다.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            _updateStatusResetTimer.Stop();
            base.OnClosed(e);
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
            DefaultHotkeys defaults = _settingsService.GetDefaultHotkeys();
            TxtKeySettings.Text = _settingsService.NormalizeHotkey(_ini.Read("Key_OpenSettings"), defaults.OpenSettings);
            TxtKeyTrans.Text = _settingsService.NormalizeHotkey(_ini.Read("Key_Translate"), defaults.Translate);
            TxtKeyAuto.Text = _settingsService.NormalizeHotkey(_ini.Read("Key_AutoTranslate"), defaults.AutoTranslate);

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

            // [배율 세팅] config.ini에 잘못된 값이 들어 있어도 UI에는 1~4 범위의 정상값만 표시합니다.
            int scale = SettingsValueNormalizer.NormalizeScaleFactor(_ini.Read("ScaleFactor"));
            SetComboByTag(ComboScale, scale.ToString());

            // [OCR/자동 번역 세팅] 상세 설정 UI에서 바로 수정할 수 있도록 보정된 현재값을 표시합니다.
            if (TxtThreshold != null) TxtThreshold.Text = SettingsValueNormalizer.NormalizeThreshold(_ini.Read("Threshold")).ToString();
            if (TxtInterval != null) TxtInterval.Text = SettingsValueNormalizer.NormalizeAutoTranslateInterval(_ini.Read("AutoTranslateInterval")).ToString();
            if (ComboResultDisplayMode != null) SetComboByTag(ComboResultDisplayMode, _ini.Read("ResultDisplayMode") ?? SettingsService.DefaultResultDisplayMode);
            ApplyTranslationContentMode(_settingsService.NormalizeTranslationContentMode(_ini.Read("TranslationContentMode")));
            if (ComboTranslationEngine != null)
            {
                TranslationEngineMode engineMode = _settingsService.NormalizeTranslationEngineMode(_ini.Read("TranslationEngine"));
                SetComboByTag(ComboTranslationEngine, _settingsService.GetTranslationEngineTag(engineMode));
            }
            if (ComboConfiguredOcrEngine != null)
            {
                ConfiguredOcrEngine configuredOcrEngine = _settingsService.NormalizeConfiguredOcrEngine(_ini.Read("OcrEngineSelection"));
                SetComboByTag(ComboConfiguredOcrEngine, _settingsService.GetConfiguredOcrEngineTag(configuredOcrEngine));
            }
            if (TxtResultHistoryLimit != null)
            {
                TxtResultHistoryLimit.Text = SettingsValueNormalizer.NormalizeResultHistoryLimit(_ini.Read("ResultHistoryLimit")).ToString();
            }
            if (TxtTranslationResultAutoClearSeconds != null)
            {
                TxtTranslationResultAutoClearSeconds.Text = SettingsValueNormalizer.NormalizeTranslationResultAutoClearSeconds(_ini.Read("TranslationResultAutoClearSeconds")).ToString();
            }
            if (CheckAutoCopyTranslationResult != null)
            {
                CheckAutoCopyTranslationResult.IsChecked = _settingsService.IsEnabledOrDefault(
                    _ini.Read("AutoCopyTranslationResult"),
                    _settingsService.IsEnabled(SettingsService.DefaultAutoCopyTranslationResult));
            }

            GeminiKeySelection geminiKey = _settingsService.SelectGeminiKey(
                _ini.Read("GeminiKey"),
                _ini.Read("GeminiKey", "GeminiKey"));

            if (PasswordGeminiKey != null) PasswordGeminiKey.Password = geminiKey.Key;
            if (TxtGeminiModel != null) TxtGeminiModel.Text = _settingsService.NormalizeGeminiModel(_ini.Read("GeminiModel"));

            if (TxtLocalLlmEndpoint != null) TxtLocalLlmEndpoint.Text = _settingsService.NormalizeLocalLlmEndpoint(_ini.Read("LocalLlmEndpoint"));
            if (TxtLocalLlmModel != null) TxtLocalLlmModel.Text = _settingsService.NormalizeLocalLlmModel(_ini.Read("LocalLlmModel"));
            if (TxtLocalLlmTimeout != null) TxtLocalLlmTimeout.Text = _settingsService.NormalizeLocalLlmTimeoutSeconds(_ini.Read("LocalLlmTimeoutSeconds")).ToString();
            if (TxtLocalLlmMaxTokens != null) TxtLocalLlmMaxTokens.Text = _settingsService.NormalizeLocalLlmMaxTokens(_ini.Read("LocalLlmMaxTokens")).ToString();

            if (CheckSaveDebugImages != null)
            {
                CheckSaveDebugImages.IsChecked = _settingsService.IsEnabled(_ini.Read("SaveDebugImages"));
            }

            if (CheckUpdatesOnStartup != null)
            {
                CheckUpdatesOnStartup.IsChecked = _settingsService.IsEnabledOrDefault(_ini.Read("CheckUpdatesOnStartup"), true);
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
        /// 저장된 번역기 방식 설정을 라디오 버튼 상태에 반영합니다.
        /// Strinova는 캐릭터명 검증 기반, ETC는 OCR 전체 번역 기반입니다.
        /// </summary>
        private void ApplyTranslationContentMode(TranslationContentMode contentMode)
        {
            if (RadioTranslationContentEtc == null || RadioTranslationContentStrinova == null) return;

            RadioTranslationContentEtc.IsChecked = contentMode == TranslationContentMode.Etc;
            RadioTranslationContentStrinova.IsChecked = contentMode != TranslationContentMode.Etc;
            UpdateGameLanguageControlState(contentMode);
        }

        /// <summary>
        /// 라디오 버튼에서 선택한 번역기 방식을 config.ini에 저장할 문자열로 반환합니다.
        /// </summary>
        private string GetSelectedTranslationContentModeTag()
        {
            return _settingsService.GetTranslationContentModeTag(
                RadioTranslationContentEtc?.IsChecked == true
                    ? TranslationContentMode.Etc
                    : TranslationContentMode.Strinova);
        }

        /// <summary>
        /// ETC 모드에서는 번역 source 언어를 자동 감지하므로 게임 언어 선택을 비활성화해
        /// 현재 설정이 실제로 사용되지 않는다는 점을 UI에서 명확히 보여줍니다.
        /// </summary>
        private void UpdateGameLanguageControlState(TranslationContentMode contentMode)
        {
            bool isEtcMode = contentMode == TranslationContentMode.Etc;

            if (ComboGameLang != null)
            {
                ComboGameLang.IsEnabled = !isEtcMode;
                ComboGameLang.Opacity = isEtcMode ? 0.6 : 1.0;
                ComboGameLang.ToolTip = isEtcMode
                    ? "ETC 모드에서는 원문 언어를 자동 감지하므로 이 설정을 사용하지 않습니다."
                    : "게임 채팅 인식 기준 언어입니다.";
            }

            if (TxtGameLangLabel != null)
            {
                TxtGameLangLabel.Foreground = isEtcMode
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(150, 150, 150))
                    : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 204, 204));
            }

            if (TxtGameLangModeHint != null)
            {
                TxtGameLangModeHint.Text = isEtcMode
                    ? "ETC 모드에서는 게임 언어를 source로 강제하지 않고 자동 감지합니다. 번역 결과 언어만 사용됩니다."
                    : "Strinova 모드에서는 게임 언어를 source 언어로 사용합니다.";
            }
        }

        private void TranslationContentMode_Checked(object sender, RoutedEventArgs e)
        {
            UpdateGameLanguageControlState(
                RadioTranslationContentEtc?.IsChecked == true
                    ? TranslationContentMode.Etc
                    : TranslationContentMode.Strinova);
        }

        /// <summary>
        /// Windows OCR capability 상태를 내부적으로 함께 확인하되,
        /// 설정창에는 OCR 엔진 사용 가능 여부 중심으로 간결하게 표시합니다.
        /// capability는 설치됐는데 엔진이 안 만들어지면 재부팅 필요 가능성만 보조 문구로 안내합니다.
        /// </summary>
        private void RefreshOcrLanguageStatus()
        {
            if (TxtOcrLanguageStatus == null) return;

            Dictionary<string, string> capabilityStates = GetOcrCapabilityStates();
            var entries = new List<OcrLanguageStatusEntry>();
            foreach ((string label, string tag) in OcrLanguageStatusTargets)
            {
                capabilityStates.TryGetValue(tag, out string capabilityState);
                bool engineAvailable = IsOcrLanguageEngineAvailable(tag);
                entries.Add(_ocrLanguageStatusFormatter.CreateEntry(label, tag, capabilityState, engineAvailable));
            }

            TxtOcrLanguageStatus.Text = _ocrLanguageStatusFormatter.BuildDisplayText(entries);
        }

        /// <summary>
        /// 현재 실행 중인 EXE 경로와 설치 방식을 업데이트 영역에 표시합니다.
        /// 설치형이면 Velopack 설치형 경로로, ZIP 실행이면 현재 실행 폴더로 안내합니다.
        /// </summary>
        private void RefreshInstallLocationStatus()
        {
            if (TxtInstallLocation == null) return;

            string locationText = _mainWindow?.GetInstallLocationDisplayText();
            TxtInstallLocation.Text = string.IsNullOrWhiteSpace(locationText)
                ? "경로 확인 실패"
                : locationText;
        }

        /// <summary>
        /// 업데이트 영역 상태 문구를 지정한 색상과 함께 갱신합니다.
        /// 경로 복사/폴더 열기/업데이트 확인 버튼이 같은 하단 상태 줄을 공유합니다.
        /// </summary>
        private void SetUpdateStatusText(string text, System.Windows.Media.Brush brush, bool autoReset = false)
        {
            if (TxtUpdateStatus == null) return;

            _updateStatusResetTimer.Stop();
            TxtUpdateStatus.Foreground = brush;
            TxtUpdateStatus.Text = text ?? "";

            if (autoReset && !string.IsNullOrWhiteSpace(TxtUpdateStatus.Text))
            {
                _updateStatusResetTimer.Start();
            }
        }

        /// <summary>
        /// 경로 복사/폴더 열기 같은 일회성 상태 문구를 일정 시간 후 지웁니다.
        /// 수동 업데이트 확인처럼 유지가 필요한 상태는 autoReset=false로 남깁니다.
        /// </summary>
        private void UpdateStatusResetTimer_Tick(object sender, EventArgs e)
        {
            _updateStatusResetTimer.Stop();
            if (TxtUpdateStatus == null) return;

            TxtUpdateStatus.Foreground = System.Windows.Media.Brushes.LightGray;
            TxtUpdateStatus.Text = "";
        }

        /// <summary>
        /// PowerShell의 Get-WindowsCapability 결과를 이용해 OCR capability 설치 상태를 읽습니다.
        /// 반환 키는 앱 언어 태그(ko, en-US, zh-Hans-CN, ja, ru)이며 값은 Installed/NotPresent/Unknown 같은 상태 문자열입니다.
        /// </summary>
        private Dictionary<string, string> GetOcrCapabilityStates()
        {
            var states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                StringBuilder script = new StringBuilder();
                script.AppendLine("$targets = @{}");
                foreach ((string _, string tag) in OcrLanguageStatusTargets)
                {
                    string escapedTag = EscapePowerShellSingleQuotedString(tag);
                    string capabilityTag = EscapePowerShellSingleQuotedString(_ocrLanguageStatusFormatter.GetCapabilityLanguageTag(tag));
                    script.AppendLine($"$targets['{escapedTag}'] = 'Language.OCR~~~{capabilityTag}~0.0.1.0'");
                }

                script.AppendLine("$result = @{}");
                script.AppendLine("try {");
                script.AppendLine("  $capabilities = Get-WindowsCapability -Online");
                script.AppendLine("  foreach ($key in $targets.Keys) {");
                script.AppendLine("    $cap = $capabilities | Where-Object { $_.Name -eq $targets[$key] } | Select-Object -First 1");
                script.AppendLine("    if ($null -eq $cap) { $result[$key] = 'Unknown' } else { $result[$key] = [string]$cap.State }");
                script.AppendLine("  }");
                script.AppendLine("  $result | ConvertTo-Json -Compress");
                script.AppendLine("} catch {");
                script.AppendLine("  '{}' ");
                script.AppendLine("}");

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "powershell",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-NonInteractive");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-Command");
                startInfo.ArgumentList.Add(script.ToString());

                using (Process process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return states;
                    }

                    if (!process.WaitForExit(8000))
                    {
                        try { process.Kill(); } catch { }
                        return states;
                    }

                    string output = process.StandardOutput.ReadToEnd().Trim();
                    if (string.IsNullOrWhiteSpace(output))
                    {
                        return states;
                    }

                    using (JsonDocument document = JsonDocument.Parse(output))
                    {
                        if (document.RootElement.ValueKind != JsonValueKind.Object)
                        {
                            return states;
                        }

                        foreach (JsonProperty property in document.RootElement.EnumerateObject())
                        {
                            states[property.Name] = property.Value.GetString();
                        }
                    }
                }
            }
            catch
            {
            }

            return states;
        }

        /// <summary>
        /// 지정한 Windows OCR 언어로 실제 OcrEngine이 생성 가능한지 확인합니다.
        /// capability 설치 여부와 별개로 현재 세션에서 엔진 생성이 가능한지를 보여주기 위해 따로 확인합니다.
        /// </summary>
        private bool IsOcrLanguageEngineAvailable(string languageTag)
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

        private static string EscapePowerShellSingleQuotedString(string value)
        {
            return (value ?? "").Replace("'", "''");
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
        /// 실제 저장은 사용자가 [저장] 버튼을 눌렀을 때 BtnSaveAndStart_Click에서 처리됩니다.
        /// </summary>
        private void ApplyDefaultHotkeyValues()
        {
            DefaultHotkeys defaults = _settingsService.GetDefaultHotkeys();
            TxtKeySettings.Text = defaults.OpenSettings;
            TxtKeyTrans.Text = defaults.Translate;
            TxtKeyAuto.Text = defaults.AutoTranslate;
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
                "단축키 입력칸을 기본값으로 되돌렸습니다.\n저장하려면 [저장] 버튼을 눌러주세요.",
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
        /// 설정을 저장한 뒤 환경설정창을 닫고 캡처 영역 선택 오버레이를 엽니다.
        /// 모달 설정창이 열린 상태에서는 영역 선택이 어려우므로 닫힌 뒤 MainWindow가 후처리합니다.
        /// </summary>
        private void BtnSelectArea_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsToIni();
            _mainWindow?.ApplyRuntimeSettingsFromIni();
            RequestedActionAfterClose = OptionSelectorPostAction.StartAreaSelection;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 설정창에서 번역 오버레이의 이동 잠금 상태를 즉시 토글합니다.
        /// </summary>
        private void BtnToggleMoveLock_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.ToggleMoveLockFromSettings();
        }

        /// <summary>
        /// 환경설정창의 현재 입력값을 config.ini에 저장하고 메인 번역창에 즉시 반영합니다.
        /// <paramref name="sender"/>는 저장 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnSaveAndStart_Click(object sender, RoutedEventArgs e)
        {
            SaveSettingsToIni();
            _mainWindow?.ApplyRuntimeSettingsFromIni();
            this.DialogResult = true;
            this.Close();
        }

        private void SaveSettingsToIni()
        {
            RequestedActionAfterClose = OptionSelectorPostAction.None;

            _ini.Write("GameLanguage", GetSelectedTag(ComboGameLang, "ko"));
            _ini.Write("TargetLanguage", GetSelectedTag(ComboTargetLang, "ko"));
            _ini.Write("Opacity", ((int)SliderOpacity.Value).ToString());

            DefaultHotkeys defaults = _settingsService.GetDefaultHotkeys();
            _ini.Write("Key_OpenSettings", _settingsService.NormalizeHotkey(TxtKeySettings?.Text, defaults.OpenSettings));
            _ini.Write("Key_Translate", _settingsService.NormalizeHotkey(TxtKeyTrans?.Text, defaults.Translate));
            _ini.Write("Key_AutoTranslate", _settingsService.NormalizeHotkey(TxtKeyAuto?.Text, defaults.AutoTranslate));

            int scaleFactor = SettingsValueNormalizer.NormalizeScaleFactor(GetSelectedTag(ComboScale, SettingsValueNormalizer.DefaultScaleFactor.ToString()));
            _ini.Write("ScaleFactor", scaleFactor.ToString());
            SetComboByTag(ComboScale, scaleFactor.ToString());

            int threshold = SettingsValueNormalizer.NormalizeThreshold(TxtThreshold?.Text);
            _ini.Write("Threshold", threshold.ToString());
            if (TxtThreshold != null) TxtThreshold.Text = threshold.ToString();

            int interval = SettingsValueNormalizer.NormalizeAutoTranslateInterval(TxtInterval?.Text);
            _ini.Write("AutoTranslateInterval", interval.ToString());
            if (TxtInterval != null) TxtInterval.Text = interval.ToString();

            _ini.Write("ResultDisplayMode", GetSelectedTag(ComboResultDisplayMode, SettingsService.DefaultResultDisplayMode));
            int historyLimit = SettingsValueNormalizer.NormalizeResultHistoryLimit(TxtResultHistoryLimit?.Text);
            _ini.Write("ResultHistoryLimit", historyLimit.ToString());
            if (TxtResultHistoryLimit != null) TxtResultHistoryLimit.Text = historyLimit.ToString();
            int autoClearSeconds = SettingsValueNormalizer.NormalizeTranslationResultAutoClearSeconds(TxtTranslationResultAutoClearSeconds?.Text);
            _ini.Write("TranslationResultAutoClearSeconds", autoClearSeconds.ToString());
            if (TxtTranslationResultAutoClearSeconds != null) TxtTranslationResultAutoClearSeconds.Text = autoClearSeconds.ToString();

            _ini.Write("SaveDebugImages", CheckSaveDebugImages?.IsChecked == true ? "true" : "false");
            _ini.Write("CheckUpdatesOnStartup", CheckUpdatesOnStartup?.IsChecked == true ? "true" : "false");
            _ini.Write("AutoCopyTranslationResult", CheckAutoCopyTranslationResult?.IsChecked == true ? "true" : "false");
            _ini.Write("TranslationContentMode", GetSelectedTranslationContentModeTag());
            _ini.Write("TranslationEngine", GetSelectedTag(ComboTranslationEngine, SettingsService.DefaultTranslationEngine));
            _ini.Write("OcrEngineSelection", GetSelectedTag(ComboConfiguredOcrEngine, SettingsService.DefaultOcrEngineSelection));
            _ini.Write("GeminiKey", PasswordGeminiKey?.Password?.Trim() ?? "");
            _ini.Write("GeminiModel", _settingsService.NormalizeGeminiModel(TxtGeminiModel?.Text));

            _ini.Write("LocalLlmEndpoint", _settingsService.NormalizeLocalLlmEndpoint(TxtLocalLlmEndpoint?.Text));
            _ini.Write("LocalLlmModel", _settingsService.NormalizeLocalLlmModel(TxtLocalLlmModel?.Text));
            int localLlmTimeout = _settingsService.NormalizeLocalLlmTimeoutSeconds(TxtLocalLlmTimeout?.Text);
            _ini.Write("LocalLlmTimeoutSeconds", localLlmTimeout.ToString());
            if (TxtLocalLlmTimeout != null) TxtLocalLlmTimeout.Text = localLlmTimeout.ToString();

            int localLlmMaxTokens = _settingsService.NormalizeLocalLlmMaxTokens(TxtLocalLlmMaxTokens?.Text);
            _ini.Write("LocalLlmMaxTokens", localLlmMaxTokens.ToString());
            if (TxtLocalLlmMaxTokens != null) TxtLocalLlmMaxTokens.Text = localLlmMaxTokens.ToString();
        }

        /// <summary>
        /// 환경설정창의 업데이트 확인 버튼을 눌렀을 때 GitHub 릴리즈 최신 버전을 수동 확인합니다.
        /// <paramref name="sender"/>는 업데이트 확인 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private async void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnCheckUpdate.IsEnabled = false;
            SetUpdateStatusText("확인 중...", System.Windows.Media.Brushes.LightGray);

            try
            {
                await _mainWindow.RunManualUpdateCheckAsync(this, status => SetUpdateStatusText(status, System.Windows.Media.Brushes.LightGray));
            }
            finally
            {
            BtnCheckUpdate.IsEnabled = true;
            }
        }

        /// <summary>
        /// 현재 실행 중인 EXE 폴더를 탐색기로 엽니다.
        /// 설치형이면 설치 폴더, ZIP 실행이면 압축을 푼 현재 실행 폴더가 열립니다.
        /// </summary>
        private void BtnOpenInstallFolder_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = _mainWindow?.GetInstallLocationPath();
            if (string.IsNullOrWhiteSpace(folderPath) || !System.IO.Directory.Exists(folderPath))
            {
                SetUpdateStatusText("폴더를 찾지 못했습니다.", System.Windows.Media.Brushes.OrangeRed, autoReset: true);
                System.Windows.MessageBox.Show("현재 실행 폴더를 찾지 못했습니다.", "현재 폴더 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add(folderPath);
                Process.Start(startInfo);
                SetUpdateStatusText("현재 실행 폴더를 열었습니다.", System.Windows.Media.Brushes.LightGreen, autoReset: true);
            }
            catch (Exception ex)
            {
                SetUpdateStatusText("폴더 열기 실패", System.Windows.Media.Brushes.OrangeRed, autoReset: true);
                System.Windows.MessageBox.Show($"현재 폴더를 열지 못했습니다.\n{ex.Message}", "현재 폴더 열기", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 현재 실행 중인 EXE 폴더 경로를 클립보드에 복사합니다.
        /// 사용자는 탐색기 주소창이나 Setup.exe --installto 경로 입력에 그대로 붙여넣을 수 있습니다.
        /// </summary>
        private void BtnCopyInstallPath_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = _mainWindow?.GetInstallLocationPath();
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                SetUpdateStatusText("경로 확인 실패", System.Windows.Media.Brushes.OrangeRed, autoReset: true);
                System.Windows.MessageBox.Show("현재 실행 경로를 확인하지 못했습니다.", "경로 복사", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(folderPath);
                SetUpdateStatusText("실행 경로를 복사했습니다.", System.Windows.Media.Brushes.LightGreen, autoReset: true);
            }
            catch (Exception ex)
            {
                SetUpdateStatusText("경로 복사 실패", System.Windows.Media.Brushes.OrangeRed, autoReset: true);
                System.Windows.MessageBox.Show($"실행 경로를 복사하지 못했습니다.\n{ex.Message}", "경로 복사", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Local LLM endpoint/model 설정이 LM Studio Local Server에 연결 가능한지 확인합니다.
        /// /v1/models를 조회해 서버 응답과 모델 ID 존재 여부를 함께 검사합니다.
        /// </summary>
        private async void BtnTestLocalLlm_Click(object sender, RoutedEventArgs e)
        {
            if (BtnTestLocalLlm == null || TxtLocalLlmTestStatus == null) return;

            string endpoint = _settingsService.NormalizeLocalLlmEndpoint(TxtLocalLlmEndpoint?.Text);
            string modelName = _settingsService.NormalizeLocalLlmModel(TxtLocalLlmModel?.Text);
            int timeoutSeconds = _settingsService.NormalizeLocalLlmTimeoutSeconds(TxtLocalLlmTimeout?.Text);

            BtnTestLocalLlm.IsEnabled = false;
            TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.LightGray;
            TxtLocalLlmTestStatus.Text = "Local LLM 서버 확인 중...";

            try
            {
                using var httpClient = new HttpClient();
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
                var apiClient = new TranslationApiClient(httpClient, new TranslationPromptBuilder(), new TranslationResultParser());

                LocalLlmConnectionTestResult result = await apiClient.TestLocalLlmConnectionAsync(endpoint, modelName, cts.Token);
                if (!result.IsSuccess)
                {
                    TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                    string detail = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "" : $" ({result.ErrorMessage})";
                    TxtLocalLlmTestStatus.Text = $"연결 실패: LM Studio 서버와 endpoint를 확인하세요.{detail}";
                    return;
                }

                if (!result.ModelFound)
                {
                    TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.Gold;
                    string models = result.Models.Count == 0 ? "모델 목록 없음" : string.Join(", ", result.Models);
                    TxtLocalLlmTestStatus.Text = $"서버 연결됨. 설정한 모델을 찾지 못했습니다. 모델: {models}";
                    return;
                }

                TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.LimeGreen;
                TxtLocalLlmTestStatus.Text = $"연결 성공: {modelName}";
            }
            catch (OperationCanceledException)
            {
                TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                TxtLocalLlmTestStatus.Text = $"연결 시간 초과: {timeoutSeconds}초 안에 응답하지 않았습니다.";
            }
            catch (Exception ex)
            {
                TxtLocalLlmTestStatus.Foreground = System.Windows.Media.Brushes.OrangeRed;
                TxtLocalLlmTestStatus.Text = $"연결 실패: {ex.Message}";
            }
            finally
            {
                BtnTestLocalLlm.IsEnabled = true;
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

        /// <summary>
        /// [OCR 진단 화면 열기] 버튼 클릭 시 현재 캡처 영역의 원본/전처리/OCR 결과를 별도 창으로 표시합니다.
        /// <paramref name="sender"/>는 OCR 진단 화면 열기 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnOpenOcrDiagnostic_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow?.ShowOcrDiagnosticWindow();
        }
    }

    internal enum OptionSelectorPostAction
    {
        None,
        StartAreaSelection
    }
}
