using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace GameTranslator
{
    /// <summary>
    /// 환경설정창의 프리셋 저장/불러오기/삭제 기능을 담당하는 partial 파일입니다.
    /// 프리셋은 config.ini의 [Presets] 목록과 Preset.이름 섹션들에 저장됩니다.
    /// </summary>
    public partial class OptionSelector
    {
        private const string PresetListSection = "Presets";
        private const string PresetListKey = "Names";
        // UI에 직접 입력하는 값이 아니라 MainWindow가 저장한 좌표/창 크기 값만 별도 배열로 관리합니다.
        private static readonly string[] PresetSettingKeys =
        {
            "CaptureX",
            "CaptureY",
            "CaptureW",
            "CaptureH",
            "CapturePixelX",
            "CapturePixelY",
            "CapturePixelW",
            "CapturePixelH",
            "WindowW",
            "WindowH"
        };

        /// <summary>
        /// config.ini에 저장된 프리셋 이름 목록을 ComboPreset 콤보박스에 다시 채웁니다.
        /// 기존 선택값이 아직 존재하면 유지하고, 없으면 첫 번째 프리셋을 선택합니다.
        /// </summary>
        private void LoadPresetList()
        {
            if (ComboPreset == null) return;

            string selected = ComboPreset.SelectedItem as string;
            ComboPreset.Items.Clear();

            foreach (string presetName in ReadPresetNames())
            {
                ComboPreset.Items.Add(presetName);
            }

            if (!string.IsNullOrWhiteSpace(selected) && ComboPreset.Items.Contains(selected))
            {
                ComboPreset.SelectedItem = selected;
            }
            else if (ComboPreset.Items.Count > 0)
            {
                ComboPreset.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// [Presets] 섹션의 Names 값을 읽어 프리셋 이름 리스트로 변환합니다.
        /// 반환값은 빈 이름과 중복 이름을 제거한 프리셋 이름 목록입니다.
        /// </summary>
        private List<string> ReadPresetNames()
        {
            string rawNames = _ini.Read(PresetListKey, PresetListSection) ?? "";
            return rawNames
                .Split('|', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePresetName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 프리셋 이름 목록을 [Presets] 섹션의 Names 키에 저장합니다.
        /// <paramref name="names"/>는 저장할 프리셋 이름 컬렉션이며, 내부적으로 정규화와 중복 제거를 수행합니다.
        /// </summary>
        private void WritePresetNames(IEnumerable<string> names)
        {
            string joined = string.Join("|", names
                .Select(NormalizePresetName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            _ini.Write(PresetListKey, joined, PresetListSection);
        }

        /// <summary>
        /// 프리셋 이름에 사용할 수 없는 문자를 제거하고 길이를 제한합니다.
        /// <paramref name="name"/>은 사용자가 입력하거나 콤보박스에서 선택한 원본 이름입니다.
        /// 반환값은 config.ini 섹션명으로 안전하게 사용할 수 있는 이름입니다.
        /// </summary>
        private string NormalizePresetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            string normalized = Regex.Replace(name.Trim(), @"[\[\]\|;\r\n]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Length > 40 ? normalized.Substring(0, 40).Trim() : normalized;
        }

        /// <summary>
        /// 프리셋 이름을 실제 INI 섹션명으로 변환합니다.
        /// <paramref name="presetName"/>은 정규화된 프리셋 이름입니다.
        /// 반환값은 "Preset.이름" 형식의 섹션명입니다.
        /// </summary>
        private string GetPresetSectionName(string presetName)
        {
            return $"Preset.{presetName}";
        }

        /// <summary>
        /// 텍스트박스 입력값을 우선하고, 비어 있으면 콤보박스 선택값을 프리셋 이름으로 사용합니다.
        /// 반환값은 저장/불러오기/삭제 대상 프리셋 이름입니다.
        /// </summary>
        private string GetSelectedPresetName()
        {
            string textName = NormalizePresetName(TxtPresetName?.Text);
            if (!string.IsNullOrWhiteSpace(textName)) return textName;

            return NormalizePresetName(ComboPreset?.SelectedItem as string);
        }

        /// <summary>
        /// 프리셋 콤보박스 선택이 바뀌면 선택한 이름을 텍스트박스에도 채워 사용자가 수정할 수 있게 합니다.
        /// <paramref name="sender"/>는 ComboPreset이고,
        /// <paramref name="e"/>는 선택 변경 이벤트 정보입니다.
        /// </summary>
        private void ComboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboPreset?.SelectedItem is string presetName && TxtPresetName != null)
            {
                TxtPresetName.Text = presetName;
            }
        }

        /// <summary>
        /// 현재 환경설정 UI 값을 지정한 프리셋 이름으로 저장합니다.
        /// <paramref name="sender"/>는 프리셋 저장 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnSavePreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = GetSelectedPresetName();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show("저장할 프리셋 이름을 입력해 주세요.", "프리셋 저장", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SavePreset(presetName);

            List<string> names = ReadPresetNames();
            if (!names.Contains(presetName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(presetName);
            }

            WritePresetNames(names);
            LoadPresetList();
            ComboPreset.SelectedItem = presetName;
            TxtPresetName.Text = presetName;

            MessageBox.Show($"프리셋 '{presetName}'을 저장했습니다.", "프리셋 저장", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 선택한 프리셋 값을 환경설정 UI에 불러옵니다.
        /// <paramref name="sender"/>는 불러오기 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// 저장 및 게임 시작을 눌러야 실제 실행 설정으로 확정됩니다.
        /// </summary>
        private void BtnLoadPreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = GetSelectedPresetName();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show("불러올 프리셋을 선택해 주세요.", "프리셋 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            LoadPreset(presetName);
            MessageBox.Show($"프리셋 '{presetName}'을 불러왔습니다.\n저장 및 게임 시작을 누르면 적용됩니다.", "프리셋 불러오기", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 선택한 프리셋 이름을 목록에서 제거합니다.
        /// <paramref name="sender"/>는 삭제 버튼이고,
        /// <paramref name="e"/>는 버튼 클릭 이벤트 정보입니다.
        /// </summary>
        private void BtnDeletePreset_Click(object sender, RoutedEventArgs e)
        {
            string presetName = GetSelectedPresetName();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show("삭제할 프리셋을 선택해 주세요.", "프리셋 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            MessageBoxResult result = MessageBox.Show($"프리셋 '{presetName}'을 삭제할까요?", "프리셋 삭제", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            List<string> names = ReadPresetNames()
                .Where(name => !name.Equals(presetName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            WritePresetNames(names);
            LoadPresetList();
            TxtPresetName.Text = "";

            MessageBox.Show($"프리셋 '{presetName}'을 삭제했습니다.", "프리셋 삭제", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// 현재 UI와 저장된 캡처/창 좌표를 Preset.이름 섹션에 기록합니다.
        /// <paramref name="presetName"/>은 저장할 프리셋 이름입니다.
        /// Gemini API Key는 보안상 프리셋에 포함하지 않습니다.
        /// </summary>
        private void SavePreset(string presetName)
        {
            string section = GetPresetSectionName(presetName);

            _ini.Write("GameLanguage", GetSelectedTag(ComboGameLang, "ko"), section);
            _ini.Write("TargetLanguage", GetSelectedTag(ComboTargetLang, "ko"), section);
            _ini.Write("Opacity", ((int)SliderOpacity.Value).ToString(), section);
            _ini.Write("ScaleFactor", SettingsValueNormalizer.NormalizeScaleFactor(GetSelectedTag(ComboScale, SettingsValueNormalizer.DefaultScaleFactor.ToString())).ToString(), section);
            _ini.Write("Threshold", SettingsValueNormalizer.NormalizeThreshold(TxtThreshold?.Text).ToString(), section);
            _ini.Write("AutoTranslateInterval", SettingsValueNormalizer.NormalizeAutoTranslateInterval(TxtInterval?.Text).ToString(), section);
            _ini.Write("ResultDisplayMode", GetSelectedTag(ComboResultDisplayMode, SettingsService.DefaultResultDisplayMode), section);
            _ini.Write("ResultHistoryLimit", SettingsValueNormalizer.NormalizeResultHistoryLimit(TxtResultHistoryLimit?.Text).ToString(), section);
            _ini.Write("SaveDebugImages", CheckSaveDebugImages?.IsChecked == true ? "true" : "false", section);
            _ini.Write("GeminiModel", _settingsService.NormalizeGeminiModel(TxtGeminiModel?.Text), section);
            _ini.Write("Key_MoveLock", TxtKeyMove.Text, section);
            _ini.Write("Key_AreaSelect", TxtKeyArea.Text, section);
            _ini.Write("Key_Translate", TxtKeyTrans.Text, section);
            _ini.Write("Key_AutoTranslate", TxtKeyAuto.Text, section);
            _ini.Write("Key_ToggleEngine", TxtKeyToggle.Text, section);
            _ini.Write("Key_CopyResult", TxtKeyCopy.Text, section);
            _ini.Write("Key_LogViewer", TxtKeyLog.Text, section);
            _ini.Write("Key_OcrDiagnostic", TxtKeyOcrDiagnostic.Text, section);

            foreach (string key in PresetSettingKeys)
            {
                _ini.Write(key, _ini.Read(key) ?? "", section);
            }
        }

        /// <summary>
        /// Preset.이름 섹션의 값을 읽어 환경설정 UI와 캡처 좌표 설정에 반영합니다.
        /// <paramref name="presetName"/>은 불러올 프리셋 이름입니다.
        /// </summary>
        private void LoadPreset(string presetName)
        {
            string section = GetPresetSectionName(presetName);

            SetComboByTag(ComboGameLang, ReadPresetValue(section, "GameLanguage", "ko"));
            SetComboByTag(ComboTargetLang, ReadPresetValue(section, "TargetLanguage", "ko"));
            SetComboByTag(ComboScale, SettingsValueNormalizer.NormalizeScaleFactor(ReadPresetValue(section, "ScaleFactor", "3")).ToString());

            if (int.TryParse(ReadPresetValue(section, "Opacity", "70"), out int opacity))
            {
                SliderOpacity.Value = Math.Max((int)SliderOpacity.Minimum, Math.Min((int)SliderOpacity.Maximum, opacity));
            }

            TxtThreshold.Text = SettingsValueNormalizer.NormalizeThreshold(ReadPresetValue(section, "Threshold", "120")).ToString();
            TxtInterval.Text = SettingsValueNormalizer.NormalizeAutoTranslateInterval(ReadPresetValue(section, "AutoTranslateInterval", "5")).ToString();
            SetComboByTag(ComboResultDisplayMode, ReadPresetValue(section, "ResultDisplayMode", SettingsService.DefaultResultDisplayMode));
            TxtResultHistoryLimit.Text = SettingsValueNormalizer.NormalizeResultHistoryLimit(ReadPresetValue(section, "ResultHistoryLimit", "5")).ToString();
            CheckSaveDebugImages.IsChecked = _settingsService.IsEnabled(ReadPresetValue(section, "SaveDebugImages", "false"));
            TxtGeminiModel.Text = _settingsService.NormalizeGeminiModel(ReadPresetValue(section, "GeminiModel", SettingsService.DefaultGeminiModel));
            DefaultHotkeys defaults = _settingsService.GetDefaultHotkeys();
            TxtKeyMove.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_MoveLock", defaults.MoveLock), defaults.MoveLock);
            TxtKeyArea.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_AreaSelect", defaults.AreaSelect), defaults.AreaSelect);
            TxtKeyTrans.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_Translate", defaults.Translate), defaults.Translate);
            TxtKeyAuto.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_AutoTranslate", defaults.AutoTranslate), defaults.AutoTranslate);
            TxtKeyToggle.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_ToggleEngine", defaults.ToggleEngine), defaults.ToggleEngine);
            TxtKeyCopy.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_CopyResult", defaults.CopyResult), defaults.CopyResult);
            TxtKeyLog.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_LogViewer", defaults.LogViewer), defaults.LogViewer);
            TxtKeyOcrDiagnostic.Text = _settingsService.NormalizeHotkey(ReadPresetValue(section, "Key_OcrDiagnostic", defaults.OcrDiagnostic), defaults.OcrDiagnostic);

            foreach (string key in PresetSettingKeys)
            {
                _ini.Write(key, _ini.Read(key, section) ?? "");
            }

            UpdateCaptureAreaTextFromIni();
        }

        /// <summary>
        /// 프리셋 섹션에서 값을 읽되, 키가 없으면 지정한 기본값을 반환합니다.
        /// <paramref name="section"/>은 Preset.이름 섹션,
        /// <paramref name="key"/>는 읽을 설정 키,
        /// <paramref name="defaultValue"/>는 값이 없을 때 사용할 기본값입니다.
        /// </summary>
        private string ReadPresetValue(string section, string key, string defaultValue)
        {
            return _ini.Read(key, section) ?? defaultValue;
        }

        /// <summary>
        /// 콤보박스 선택 항목의 Tag 값을 읽습니다.
        /// <paramref name="combo"/>는 값을 읽을 콤보박스이고,
        /// <paramref name="defaultValue"/>는 선택 항목이 없을 때 반환할 기본값입니다.
        /// </summary>
        private string GetSelectedTag(System.Windows.Controls.ComboBox combo, string defaultValue)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? defaultValue;
        }

        /// <summary>
        /// config.ini의 CaptureX/Y/W/H 값을 읽어 환경설정창의 캡처 영역 설명 문구를 갱신합니다.
        /// 프리셋 불러오기 직후 사용자가 어떤 영역이 적용될지 확인할 수 있게 합니다.
        /// </summary>
        private void UpdateCaptureAreaTextFromIni()
        {
            string cX = _ini.Read("CaptureX");
            string cY = _ini.Read("CaptureY");
            string cW = _ini.Read("CaptureW");
            string cH = _ini.Read("CaptureH");

            if (!string.IsNullOrEmpty(cW) && !string.IsNullOrEmpty(cH))
            {
                TxtAreaInfo.Text = $"저장된 영역: X:{cX}, Y:{cY} (크기: {cW}x{cH})";
            }
            else
            {
                TxtAreaInfo.Text = "저장된 영역: 없음 (좌측 하단 기본값)";
            }
        }
    }
}
