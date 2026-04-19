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
    public partial class MainWindow
    {
        private void RegisterAllHotkeys()
        {
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_ENGINE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_COPY_RESULT);

            hotkeyWarningMessage = "";
            var failedHotkeys = new List<string>();

            string moveHotkey = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string areaHotkey = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string translateHotkey = ini.Read("Key_Translate") ?? "Ctrl+9";
            string autoHotkey = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string toggleHotkey = ini.Read("Key_ToggleEngine") ?? "Ctrl+-";
            string copyHotkey = ini.Read("Key_CopyResult") ?? "Ctrl+6";

            ParseHotkey(moveHotkey, out modMove, out keyMove);
            ParseHotkey(areaHotkey, out modArea, out keyArea);
            ParseHotkey(translateHotkey, out modTrans, out keyTrans);
            ParseHotkey(autoHotkey, out modAuto, out keyAuto);
            ParseHotkey(toggleHotkey, out modToggle, out keyToggle);
            ParseHotkey(copyHotkey, out modCopy, out keyCopy);

            RegisterHotKeyOrWarn(ID_HOTKEY_MOVE_LOCK, modMove, keyMove, "이동/잠금", moveHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AREA_SELECT, modArea, keyArea, "영역 설정", areaHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TRANSLATE, modTrans, keyTrans, "수동 번역", translateHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AUTO, modAuto, keyAuto, "자동 번역", autoHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TOGGLE_ENGINE, modToggle, keyToggle, "엔진 전환", toggleHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_COPY_RESULT, modCopy, keyCopy, "번역 복사", copyHotkey, failedHotkeys);

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
            string copy = ini.Read("Key_CopyResult") ?? "Ctrl+6";

            // 🌟 안내 문구에 엔진 전환 추가
            string engineStr = useGeminiEngine ? "Gemini" : "Google";
            string newGuide = $"[{m}] 이동  [{a}] 영역  [{t}] 번역  [{copy}] 복사\n[{au}] 자동  [{tg}] {engineStr} 전환";

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
                    case ID_HOTKEY_COPY_RESULT: CopyLastTranslationToClipboard(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }
    }
}
