using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GameTranslator
{
    public partial class OptionSelector
    {
        /// <summary>
        /// 상세 설정의 숫자 입력칸에 붙여넣기 검증을 연결합니다.
        /// XAML의 PreviewTextInput은 키보드 입력만 막기 때문에 붙여넣기는 별도 핸들러가 필요합니다.
        /// </summary>
        private void RegisterNumericSettingInputGuards()
        {
            RegisterNumericSettingInputGuard(TxtThreshold);
            RegisterNumericSettingInputGuard(TxtInterval);
            RegisterNumericSettingInputGuard(TxtResultHistoryLimit);
            RegisterNumericSettingInputGuard(TxtLocalLlmTimeout);
            RegisterNumericSettingInputGuard(TxtLocalLlmMaxTokens);
        }

        /// <summary>
        /// 지정한 TextBox에 숫자 전용 붙여넣기 검증을 추가합니다.
        /// <paramref name="textBox"/>는 OCR 이진화 기준, 자동 번역 주기, 누적 표시 줄 수 같은 숫자 설정 입력칸입니다.
        /// </summary>
        private void RegisterNumericSettingInputGuard(System.Windows.Controls.TextBox textBox)
        {
            if (textBox == null) return;
            System.Windows.DataObject.AddPastingHandler(textBox, NumericSetting_Pasting);
        }

        /// <summary>
        /// 상세 설정 숫자 입력칸에서 숫자가 아닌 키보드 입력을 막습니다.
        /// <paramref name="sender"/>는 입력 중인 TextBox이고,
        /// <paramref name="e"/>는 새로 입력하려는 문자 조각입니다.
        /// </summary>
        private void NumericSetting_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !IsDigitsOnly(e.Text);
        }

        /// <summary>
        /// 상세 설정 숫자 입력칸에 붙여넣는 값이 숫자로만 구성됐는지 검사합니다.
        /// <paramref name="sender"/>는 붙여넣기 대상 TextBox이고,
        /// <paramref name="e"/>는 클립보드 데이터가 들어 있는 붙여넣기 이벤트입니다.
        /// </summary>
        private void NumericSetting_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(System.Windows.DataFormats.Text))
            {
                string text = e.DataObject.GetData(System.Windows.DataFormats.Text) as string;
                if (IsDigitsOnly(text))
                {
                    return;
                }
            }

            e.CancelCommand();
        }

        /// <summary>
        /// 상세 설정 숫자 입력값이 바뀔 때 현재 범위 상태를 즉시 갱신합니다.
        /// <paramref name="sender"/>는 값이 바뀐 TextBox이고,
        /// <paramref name="e"/>는 변경 이벤트 정보입니다.
        /// </summary>
        private void NumericSetting_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshAdvancedSettingValidationStatus();
        }

        /// <summary>
        /// 상세 설정 숫자 입력값이 허용 범위 안인지 검사하고 사용자에게 저장 시 보정 결과를 안내합니다.
        /// </summary>
        private void RefreshAdvancedSettingValidationStatus()
        {
            if (TxtAdvancedValidationStatus == null) return;

            var messages = new List<string>();
            AddNumericSettingStatus(
                messages,
                "OCR 이진화 기준",
                TxtThreshold?.Text,
                SettingsValueNormalizer.MinThreshold,
                SettingsValueNormalizer.MaxThreshold,
                SettingsValueNormalizer.NormalizeThreshold);
            AddNumericSettingStatus(
                messages,
                "자동 번역 주기",
                TxtInterval?.Text,
                SettingsValueNormalizer.MinAutoTranslateInterval,
                SettingsValueNormalizer.MaxAutoTranslateInterval,
                SettingsValueNormalizer.NormalizeAutoTranslateInterval);
            AddNumericSettingStatus(
                messages,
                "누적 표시 줄 수",
                TxtResultHistoryLimit?.Text,
                SettingsValueNormalizer.MinResultHistoryLimit,
                SettingsValueNormalizer.MaxResultHistoryLimit,
                SettingsValueNormalizer.NormalizeResultHistoryLimit);
            AddNumericSettingStatus(
                messages,
                "Local LLM Timeout",
                TxtLocalLlmTimeout?.Text,
                SettingsService.MinLocalLlmTimeoutSeconds,
                SettingsService.MaxLocalLlmTimeoutSeconds,
                _settingsService.NormalizeLocalLlmTimeoutSeconds);
            AddNumericSettingStatus(
                messages,
                "Local LLM Max Tokens",
                TxtLocalLlmMaxTokens?.Text,
                SettingsService.MinLocalLlmMaxTokens,
                SettingsService.MaxLocalLlmMaxTokens,
                _settingsService.NormalizeLocalLlmMaxTokens);

            if (messages.Count == 0)
            {
                TxtAdvancedValidationStatus.Foreground = System.Windows.Media.Brushes.LightGreen;
                TxtAdvancedValidationStatus.Text = "상세 설정 숫자값이 모두 허용 범위 안에 있습니다.";
                return;
            }

            TxtAdvancedValidationStatus.Foreground = System.Windows.Media.Brushes.Khaki;
            TxtAdvancedValidationStatus.Text = "저장 시 자동 보정: " + string.Join(" / ", messages);
        }

        /// <summary>
        /// 숫자 설정값 하나의 현재 입력 상태를 검사해 경고 목록에 추가합니다.
        /// <paramref name="messages"/>는 UI에 표시할 보정 안내 목록,
        /// <paramref name="label"/>은 사용자에게 보여줄 설정 이름,
        /// <paramref name="rawValue"/>는 TextBox에 입력된 원문,
        /// <paramref name="minValue"/>와 <paramref name="maxValue"/>는 허용 범위,
        /// <paramref name="normalizeValue"/>는 저장 시 실제 적용되는 보정 함수입니다.
        /// </summary>
        private void AddNumericSettingStatus(
            List<string> messages,
            string label,
            string rawValue,
            int minValue,
            int maxValue,
            Func<string, int> normalizeValue)
        {
            int normalizedValue = normalizeValue(rawValue);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                messages.Add($"{label} 빈 값 -> {normalizedValue}");
                return;
            }

            if (!int.TryParse(rawValue, out int parsedValue))
            {
                messages.Add($"{label} 숫자만 입력 가능 -> {normalizedValue}");
                return;
            }

            if (parsedValue < minValue || parsedValue > maxValue)
            {
                messages.Add($"{label} {minValue}~{maxValue} 범위 밖 -> {normalizedValue}");
            }
        }

        /// <summary>
        /// 문자열이 비어 있지 않고 모든 문자가 숫자인지 확인합니다.
        /// <paramref name="text"/>는 키보드 입력 또는 붙여넣기 문자열입니다.
        /// </summary>
        private bool IsDigitsOnly(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            foreach (char ch in text)
            {
                if (!char.IsDigit(ch))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
