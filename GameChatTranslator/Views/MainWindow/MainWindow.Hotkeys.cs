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
    /// 전역 단축키 등록, 단축키 문자열 파싱, WM_HOTKEY 메시지 분기를 담당하는 partial 파일입니다.
    /// 환경설정창에서 저장한 Key_* 값을 읽어 OS 전역 단축키로 등록합니다.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// config.ini에 저장된 모든 단축키를 읽고 Win32 RegisterHotKey로 등록합니다.
        /// 기존 등록을 먼저 해제한 뒤 다시 등록하므로 설정 변경 후 재등록해도 중복 등록을 피할 수 있습니다.
        /// </summary>
        private void RegisterAllHotkeys()
        {
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_ENGINE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_COPY_RESULT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_LOG_VIEWER);

            hotkeyWarningMessage = "";
            var failedHotkeys = new List<string>();

            string moveHotkey = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string areaHotkey = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string translateHotkey = ini.Read("Key_Translate") ?? "Ctrl+9";
            string autoHotkey = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string toggleHotkey = ini.Read("Key_ToggleEngine") ?? "Ctrl+-";
            string copyHotkey = ini.Read("Key_CopyResult") ?? "Ctrl+6";
            string logHotkey = ini.Read("Key_LogViewer") ?? "Ctrl+=";

            ParseHotkey(moveHotkey, out modMove, out keyMove);
            ParseHotkey(areaHotkey, out modArea, out keyArea);
            ParseHotkey(translateHotkey, out modTrans, out keyTrans);
            ParseHotkey(autoHotkey, out modAuto, out keyAuto);
            ParseHotkey(toggleHotkey, out modToggle, out keyToggle);
            ParseHotkey(copyHotkey, out modCopy, out keyCopy);
            ParseHotkey(logHotkey, out modLog, out keyLog);

            RegisterHotKeyOrWarn(ID_HOTKEY_MOVE_LOCK, modMove, keyMove, "이동/잠금", moveHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AREA_SELECT, modArea, keyArea, "영역 설정", areaHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TRANSLATE, modTrans, keyTrans, "수동 번역", translateHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_AUTO, modAuto, keyAuto, "자동 번역", autoHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_TOGGLE_ENGINE, modToggle, keyToggle, "엔진 전환", toggleHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_COPY_RESULT, modCopy, keyCopy, "번역 복사", copyHotkey, failedHotkeys);
            RegisterHotKeyOrWarn(ID_HOTKEY_LOG_VIEWER, modLog, keyLog, "로그창", logHotkey, failedHotkeys);

            if (failedHotkeys.Count > 0)
            {
                hotkeyWarningMessage = "⚠️ 등록 실패 단축키: " + string.Join(", ", failedHotkeys);
                AppendLog(hotkeyWarningMessage);
            }
        }

        /// <summary>
        /// 단일 전역 단축키를 등록하고 실패 시 사용자에게 보여줄 경고 목록에 추가합니다.
        /// <paramref name="id"/>는 WM_HOTKEY 메시지에서 기능을 구분하는 내부 ID,
        /// <paramref name="modifier"/>는 Ctrl/Alt/Shift 같은 Win32 modifier 비트,
        /// <paramref name="key"/>는 Virtual-Key 코드,
        /// <paramref name="label"/>은 경고 메시지에 표시할 기능 이름,
        /// <paramref name="configuredHotkey"/>는 config.ini에 저장된 원본 단축키 문자열,
        /// <paramref name="failedHotkeys"/>는 실패한 항목을 누적할 리스트입니다.
        /// </summary>
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

        /// <summary>
        /// 단축키 등록 실패가 있었을 때 번역창에 주황색 경고 문구를 출력합니다.
        /// 다른 프로그램이 같은 전역 단축키를 선점한 경우 사용자가 즉시 알아볼 수 있게 합니다.
        /// </summary>
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

        /// <summary>
        /// 번역창 상단의 노란 단축키 안내 문구를 현재 설정값과 엔진/자동번역 모드에 맞게 갱신합니다.
        /// 이 함수는 설정 변경, 엔진 전환, 자동번역 모드 변경, 캡처 영역 설정 후 호출됩니다.
        /// </summary>
        private void UpdateYellowHotkeyGuideText()
        {
            string m = ini.Read("Key_MoveLock") ?? "Ctrl+7";
            string a = ini.Read("Key_AreaSelect") ?? "Ctrl+8";
            string t = ini.Read("Key_Translate") ?? "Ctrl+9";
            string au = ini.Read("Key_AutoTranslate") ?? "Ctrl+0";
            string tg = ini.Read("Key_ToggleEngine") ?? "Ctrl+-";
            string copy = ini.Read("Key_CopyResult") ?? "Ctrl+6";
            string log = ini.Read("Key_LogViewer") ?? "Ctrl+=";

            // 🌟 안내 문구에 엔진 전환 추가
            string engineStr = useGeminiEngine ? "Gemini" : "Google";
            string newGuide = $"[{m}] 이동  [{a}] 영역  [{t}] 번역  [{copy}] 복사\n[{au}] 자동모드  [{tg}] {engineStr} 전환  [{log}] 로그";

            foreach (var tb in FindVisualChildren<TextBlock>(this))
            {
                if (tb.Text.Contains("이동") && tb.Text.Contains("영역설정") || tb.Text.Contains("자동"))
                {
                    // TextBlock 내부 Run을 직접 교체해 자동 모드 상태만 색상 강조합니다.
                    tb.Inlines.Clear();
                    tb.Inlines.Add(new Run(newGuide));
                    tb.Inlines.Add(new Run($"\n  ● 자동: {GetAutoTranslateModeLabel()}")
                    {
                        Foreground = isAutoTranslating ? Brushes.Lime : Brushes.Gray,
                        FontWeight = FontWeights.Bold
                    });
                    break;
                }
            }
        }

        /// <summary>
        /// WPF 시각 트리에서 특정 타입의 자식 컨트롤을 재귀적으로 찾습니다.
        /// <typeparam name="T"/>는 찾을 DependencyObject 타입입니다. 예: TextBlock.
        /// <paramref name="depObj"/>는 검색을 시작할 루트 컨트롤입니다.
        /// 반환값은 루트 아래에서 발견된 모든 T 타입 컨트롤입니다.
        /// </summary>
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

        /// <summary>
        /// "Ctrl+9", "Ctrl+-", "Alt+T" 같은 사용자 입력 문자열을 Win32 단축키 값으로 변환합니다.
        /// <paramref name="hotkeyStr"/>는 config.ini 또는 설정창에서 받은 단축키 문자열,
        /// <paramref name="modifier"/>는 변환된 Ctrl/Alt/Shift modifier 비트 출력값,
        /// <paramref name="vk"/>는 변환된 Virtual-Key 코드 출력값입니다.
        /// </summary>
        private void ParseHotkey(string hotkeyStr, out uint modifier, out uint vk)
        {
            modifier = 0; vk = 0;
            if (string.IsNullOrEmpty(hotkeyStr)) return;

            hotkeyStr = hotkeyStr.ToUpper().Replace(" ", "");
            if (hotkeyStr.Contains("CTRL+")) { modifier |= MOD_CONTROL; hotkeyStr = hotkeyStr.Replace("CTRL+", ""); }
            if (hotkeyStr.Contains("ALT+")) { modifier |= 0x0001; hotkeyStr = hotkeyStr.Replace("ALT+", ""); }
            if (hotkeyStr.Contains("SHIFT+")) { modifier |= 0x0004; hotkeyStr = hotkeyStr.Replace("SHIFT+", ""); }

            if (Regex.IsMatch(hotkeyStr, @"^[0-9]$")) hotkeyStr = "D" + hotkeyStr;
            if (hotkeyStr == "-" || hotkeyStr == "OEMMINUS") { vk = 0xBD; return; }
            if (hotkeyStr == "=" || hotkeyStr == "+" || hotkeyStr == "OEMPLUS") { vk = 0xBB; return; }
            if (hotkeyStr == "~" || hotkeyStr == "`" || hotkeyStr == "TILDE") { vk = 0xC0; return; }

            if (Enum.TryParse(hotkeyStr, true, out Key wpfKey)) { vk = (uint)KeyInterop.VirtualKeyFromKey(wpfKey); }
        }

        /// <summary>
        /// Windows 메시지 루프에서 WM_HOTKEY를 받아 실제 기능 함수로 분기합니다.
        /// <paramref name="hwnd"/>는 메시지를 받은 창 핸들,
        /// <paramref name="msg"/>는 Windows 메시지 번호,
        /// <paramref name="wParam"/>은 등록한 단축키 ID,
        /// <paramref name="lParam"/>은 modifier/key 조합 정보,
        /// <paramref name="handled"/>는 메시지 처리 완료 여부를 WPF에 알려주는 플래그입니다.
        /// 반환값은 Win32 메시지 후킹 규약상 IntPtr.Zero입니다.
        /// </summary>
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
                    case ID_HOTKEY_LOG_VIEWER: ToggleLogViewerWindow(); handled = true; break;
                }
            }
            return IntPtr.Zero;
        }
    }
}
