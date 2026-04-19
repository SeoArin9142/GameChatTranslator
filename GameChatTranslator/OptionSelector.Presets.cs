using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace GameTranslator
{
    public partial class OptionSelector
    {
        private const string PresetListSection = "Presets";
        private const string PresetListKey = "Names";
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

        private void WritePresetNames(IEnumerable<string> names)
        {
            string joined = string.Join("|", names
                .Select(NormalizePresetName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase));

            _ini.Write(PresetListKey, joined, PresetListSection);
        }

        private string NormalizePresetName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";

            string normalized = Regex.Replace(name.Trim(), @"[\[\]\|;\r\n]", "");
            normalized = Regex.Replace(normalized, @"\s+", " ");
            return normalized.Length > 40 ? normalized.Substring(0, 40).Trim() : normalized;
        }

        private string GetPresetSectionName(string presetName)
        {
            return $"Preset.{presetName}";
        }

        private string GetSelectedPresetName()
        {
            string textName = NormalizePresetName(TxtPresetName?.Text);
            if (!string.IsNullOrWhiteSpace(textName)) return textName;

            return NormalizePresetName(ComboPreset?.SelectedItem as string);
        }

        private void ComboPreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboPreset?.SelectedItem is string presetName && TxtPresetName != null)
            {
                TxtPresetName.Text = presetName;
            }
        }

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

        private void SavePreset(string presetName)
        {
            string section = GetPresetSectionName(presetName);

            _ini.Write("GameLanguage", GetSelectedTag(ComboGameLang, "ko"), section);
            _ini.Write("TargetLanguage", GetSelectedTag(ComboTargetLang, "ko"), section);
            _ini.Write("Opacity", ((int)SliderOpacity.Value).ToString(), section);
            _ini.Write("ScaleFactor", GetSelectedTag(ComboScale, "3"), section);
            _ini.Write("Threshold", string.IsNullOrWhiteSpace(TxtThreshold?.Text) ? "120" : TxtThreshold.Text.Trim(), section);
            _ini.Write("AutoTranslateInterval", string.IsNullOrWhiteSpace(TxtInterval?.Text) ? "5" : TxtInterval.Text.Trim(), section);
            _ini.Write("SaveDebugImages", CheckSaveDebugImages?.IsChecked == true ? "true" : "false", section);
            _ini.Write("GeminiModel", string.IsNullOrWhiteSpace(TxtGeminiModel?.Text) ? MainWindow.DefaultGeminiModel : TxtGeminiModel.Text.Trim(), section);
            _ini.Write("Key_MoveLock", TxtKeyMove.Text, section);
            _ini.Write("Key_AreaSelect", TxtKeyArea.Text, section);
            _ini.Write("Key_Translate", TxtKeyTrans.Text, section);
            _ini.Write("Key_AutoTranslate", TxtKeyAuto.Text, section);
            _ini.Write("Key_ToggleEngine", TxtKeyToggle.Text, section);
            _ini.Write("Key_CopyResult", TxtKeyCopy.Text, section);

            foreach (string key in PresetSettingKeys)
            {
                _ini.Write(key, _ini.Read(key) ?? "", section);
            }
        }

        private void LoadPreset(string presetName)
        {
            string section = GetPresetSectionName(presetName);

            SetComboByTag(ComboGameLang, ReadPresetValue(section, "GameLanguage", "ko"));
            SetComboByTag(ComboTargetLang, ReadPresetValue(section, "TargetLanguage", "ko"));
            SetComboByTag(ComboScale, ReadPresetValue(section, "ScaleFactor", "3"));

            if (int.TryParse(ReadPresetValue(section, "Opacity", "70"), out int opacity))
            {
                SliderOpacity.Value = Math.Max((int)SliderOpacity.Minimum, Math.Min((int)SliderOpacity.Maximum, opacity));
            }

            TxtThreshold.Text = ReadPresetValue(section, "Threshold", "120");
            TxtInterval.Text = ReadPresetValue(section, "AutoTranslateInterval", "5");
            CheckSaveDebugImages.IsChecked = IsTruthy(ReadPresetValue(section, "SaveDebugImages", "false"));
            TxtGeminiModel.Text = ReadPresetValue(section, "GeminiModel", MainWindow.DefaultGeminiModel);
            TxtKeyMove.Text = ReadPresetValue(section, "Key_MoveLock", "Ctrl+7");
            TxtKeyArea.Text = ReadPresetValue(section, "Key_AreaSelect", "Ctrl+8");
            TxtKeyTrans.Text = ReadPresetValue(section, "Key_Translate", "Ctrl+9");
            TxtKeyAuto.Text = ReadPresetValue(section, "Key_AutoTranslate", "Ctrl+0");
            TxtKeyToggle.Text = ReadPresetValue(section, "Key_ToggleEngine", "Ctrl+-");
            TxtKeyCopy.Text = ReadPresetValue(section, "Key_CopyResult", "Ctrl+6");

            foreach (string key in PresetSettingKeys)
            {
                _ini.Write(key, _ini.Read(key, section) ?? "");
            }

            UpdateCaptureAreaTextFromIni();
        }

        private string ReadPresetValue(string section, string key, string defaultValue)
        {
            return _ini.Read(key, section) ?? defaultValue;
        }

        private string GetSelectedTag(System.Windows.Controls.ComboBox combo, string defaultValue)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? defaultValue;
        }

        private bool IsTruthy(string value)
        {
            return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("y", StringComparison.OrdinalIgnoreCase);
        }

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
