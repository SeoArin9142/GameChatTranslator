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
    /// MainWindow의 로드/종료/이동 잠금 등 창 수명 주기와 사용자 조작 상태를 관리합니다.
    /// 번역창은 게임 위에 떠 있어야 하므로 최상단 유지와 클릭 관통 상태 전환을 여기서 처리합니다.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 메인 번역창과 캡처 영역 테두리 창을 주기적으로 최상단에 고정합니다.
        /// 전체화면 또는 게임 창 포커스 전환 중에도 번역 오버레이가 뒤로 밀리지 않게 보정합니다.
        /// </summary>
        private void ForceTopmost()
        {
            // 1. 메인 번역창 최상단 강제 적용
            if (_windowHandle != IntPtr.Zero)
            {
                SetWindowPos(_windowHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }

            // 2. 캡처 영역 표시(빨간 테두리) 창 최상단 강제 적용
            if (captureBorderWindow != null && captureBorderWindow.IsVisible)
            {
                IntPtr borderHandle = new WindowInteropHelper(captureBorderWindow).Handle;
                if (borderHandle != IntPtr.Zero)
                {
                    SetWindowPos(borderHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        /// <summary>
        /// 메인 창 로드 완료 후 업데이트 확인, 환경설정창 표시, 단축키 등록, 초기 캡처 영역 배치를 순서대로 수행합니다.
        /// <paramref name="sender"/>는 로드된 MainWindow이고,
        /// <paramref name="e"/>는 WPF Loaded 이벤트 정보입니다.
        /// </summary>
        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await Task.Delay(500);

            if (!await CheckForUpdatesOnStartupAsync())
            {
                return;
            }

            OptionSelector selector = CreateSettingsDialog(isDialogMode: true);
            bool? dialogResult = selector.ShowDialog();

            if (dialogResult != true)
            {
                Application.Current.Shutdown();
                return;
            }

            OptionSelectorPostAction requestedAction = selector.RequestedActionAfterClose;

            this.Topmost = false;
            this.Topmost = true;

            _windowHandle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_windowHandle).AddHook(HwndHook);
            ApplyRuntimeSettingsFromIni();

            string geminiKey = ReadGeminiKey();

            string currentEngine = GetCurrentTranslationEngineDisplayName();

            AppendLog($"프로그램이 시작되었습니다. (적용 엔진: {currentEngine})");

            // 🌟 [추가] 프로그램 시작 시 현재 세팅값(API 키 제외)을 로그에 기록합니다.
            string log_gLang = ini.Read("GameLanguage") ?? "ko";
            string log_tLang = ini.Read("TargetLanguage") ?? "ko";
            string log_interval = SettingsValueNormalizer.NormalizeAutoTranslateInterval(ini.Read("AutoTranslateInterval")).ToString();
            string log_threshold = SettingsValueNormalizer.NormalizeThreshold(ini.Read("Threshold")).ToString();
            string log_scale = SettingsValueNormalizer.NormalizeScaleFactor(ini.Read("ScaleFactor")).ToString();
            string log_opacity = ini.Read("Opacity") ?? "100";
            string log_model = ReadGeminiModel();
            string log_local_llm_endpoint = ReadLocalLlmEndpoint();
            string log_local_llm_model = ReadLocalLlmModel();
            string log_content_mode = settingsService.GetTranslationContentModeTag(ReadTranslationContentMode());

            AppendLog($"[현재 세팅]");
            AppendLog($"\t[게임 언어\t\t\t: {log_gLang}\t]");
            AppendLog($"\t[번역 언어\t\t\t: {log_tLang}\t]");
            AppendLog($"\t[Threshold\t\t\t: {log_threshold}\t]");
            AppendLog($"\t[Scale\t\t\t: {log_scale}배\t]");
            AppendLog($"\t[번역 주기\t\t\t: {log_interval}초\t]");
            AppendLog($"\t[번역기 방식\t\t\t: {log_content_mode}\t]");
            AppendLog($"\t[투명도\t\t\t: {log_opacity}\t]");
            AppendLog($"\t[모델\t\t\t: {log_model}\t]");
            AppendLog($"\t[Local LLM Endpoint\t: {log_local_llm_endpoint}\t]");
            AppendLog($"\t[Local LLM Model\t\t: {log_local_llm_model}\t]");

            if (!string.IsNullOrEmpty(geminiKey))
            {
                await ListAvailableGeminiModels(geminiKey);
            }

            WindowUtils.SetClickThrough(this);
            UpdateYellowHotkeyGuideText();
            UpdateMoveLockToggleButton();

            string cx = ini.Read("CaptureX");
            string cy = ini.Read("CaptureY");
            string cw = ini.Read("CaptureW");
            string ch = ini.Read("CaptureH");
            string cpx = ini.Read("CapturePixelX");
            string cpy = ini.Read("CapturePixelY");
            string cpw = ini.Read("CapturePixelW");
            string cph = ini.Read("CapturePixelH");
            if (int.TryParse(cx, out int x) && int.TryParse(cy, out int y) &&
                int.TryParse(cw, out int w) && int.TryParse(ch, out int h) && w > 0 && h > 0)
            {
                gameChatArea = new Rectangle(x, y, w, h);
                if (int.TryParse(cpx, out int px) && int.TryParse(cpy, out int py) &&
                    int.TryParse(cpw, out int pw) && int.TryParse(cph, out int ph) && pw > 0 && ph > 0)
                {
                    gameChatCaptureArea = new Rectangle(px, py, pw, ph);
                }
                else
                {
                    gameChatCaptureArea = ConvertDisplayAreaToPixels(gameChatArea);
                }
                this.SizeToContent = SizeToContent.Manual;
                this.Width = w;
                this.MinWidth = w;
                this.SizeToContent = SizeToContent.Height;

                TxtResult.Text = $"📍 마지막으로 저장된 영역을 불러왔습니다.\n🤖 현재 번역 엔진: {currentEngine}";
                PositionTranslationWindowNearCaptureArea(gameChatArea, gameChatCaptureArea);
            }
            else
            {
                this.Width = 1000;
                this.Height = 130;
                gameChatArea = new Rectangle(20, (int)(SystemParameters.WorkArea.Bottom - this.Height - TranslationWindowGap - 250 - 10), 500, 250);
                gameChatCaptureArea = ConvertDisplayAreaToPixels(gameChatArea);

                string missingLangs = "";
                if (!ocrEngines.ContainsKey("ru")) missingLangs += "러시아어 ";
                if (!ocrEngines.ContainsKey("ja")) missingLangs += "일본어 ";
                if (!ocrEngines.ContainsKey("zh-Hans-CN")) missingLangs += "중국어 ";

                if (missingLangs != "")
                    TxtResult.Text = $"⚠️ [경고] {missingLangs}언어팩 누락!\n'LangInstall.bat' 실행 권장.\n🤖 현재 번역 엔진: {currentEngine}";
                else
                    TxtResult.Text = $"📍 기본 캡처 영역으로 세팅 완료.\n🤖 현재 번역 엔진: {currentEngine}";

                PositionTranslationWindowNearCaptureArea(gameChatArea, gameChatCaptureArea);
            }

            UpdateCaptureBorder(!isLocked);
            ShowHotkeyWarningIfAny();
            ExecuteSettingsDialogPostAction(requestedAction);
        }

        /// <summary>
        /// 프로그램 종료 시 보조 창과 전역 단축키를 정리하고 종료 로그를 남깁니다.
        /// <paramref name="e"/>는 WPF 종료 이벤트 정보입니다.
        /// 전역 단축키를 해제하지 않으면 다음 실행 또는 다른 프로그램 단축키에 영향을 줄 수 있습니다.
        /// </summary>
        protected override void OnClosed(EventArgs e)
        {
            autoTranslateTimer?.Stop();
            topmostTimer?.Stop();
            apiStatusTimer?.Stop();
            autoModeStatusTimer?.Stop();
            translationResultAutoClearTimer?.Stop();
            captureBorderWindow?.Close();
            if (settingsWindow != null)
            {
                settingsWindow.Closed -= SettingsWindow_Closed;
                settingsWindow.Close();
                settingsWindow = null;
            }
            logViewerWindow?.CloseForShutdown();
            ocrDiagnosticWindow?.Close();
            UnregisterHotKey(_windowHandle, ID_HOTKEY_MOVE_LOCK);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AREA_SELECT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TRANSLATE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_AUTO);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_TOGGLE_ENGINE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_COPY_RESULT);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_LOG_VIEWER);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_OCR_DIAGNOSTIC);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_HOTKEY_GUIDE_TOGGLE);
            UnregisterHotKey(_windowHandle, ID_HOTKEY_OPEN_SETTINGS);

            AppendLog("프로그램이 정상적으로 종료되었습니다.");
            Application.Current.Shutdown();
            base.OnClosed(e);
        }

        /// <summary>
        /// 런타임에서 환경설정창을 다시 열어 설정 변경을 적용합니다.
        /// 저장 후 필요한 후처리(예: 캡처 영역 다시 지정)는 창이 닫힌 뒤 실행합니다.
        /// </summary>
        public void ShowSettingsWindow()
        {
            if (settingsWindow != null)
            {
                if (settingsWindow.WindowState == WindowState.Minimized)
                {
                    settingsWindow.WindowState = WindowState.Normal;
                }

                settingsWindow.Activate();
                return;
            }

            settingsWindow = CreateSettingsDialog(isDialogMode: false);
            settingsWindow.Closed += SettingsWindow_Closed;
            settingsWindow.Show();
            settingsWindow.Activate();
        }

        /// <summary>
        /// 환경설정창에서 이동 잠금 전환을 요청할 때 사용하는 래퍼입니다.
        /// </summary>
        public bool ToggleMoveLockFromSettings()
        {
            ToggleMoveLock();
            return !isLocked;
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            if (sender is OptionSelector selector)
            {
                ExecuteSettingsDialogPostAction(selector.RequestedActionAfterClose);
                selector.Closed -= SettingsWindow_Closed;
            }

            if (ReferenceEquals(settingsWindow, sender))
            {
                settingsWindow = null;
            }
        }

        private OptionSelector CreateSettingsDialog(bool isDialogMode)
        {
            return new OptionSelector(this, ini, isDialogMode)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
        }

        private void ExecuteSettingsDialogPostAction(OptionSelectorPostAction action)
        {
            if (action == OptionSelectorPostAction.StartAreaSelection)
            {
                startAreaSelection();
            }
        }

        /// <summary>
        /// 이동/잠금 해제 상태에서 번역창을 마우스로 드래그할 수 있게 합니다.
        /// <paramref name="sender"/>는 클릭된 MainWindow,
        /// <paramref name="e"/>는 마우스 왼쪽 버튼 이벤트 정보입니다.
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { if (!isLocked) this.DragMove(); }

        private void BtnMoveLockToggle_Click(object sender, RoutedEventArgs e)
        {
            ToggleMoveLock();
        }

        private void UpdateMoveLockToggleButton()
        {
            if (BtnMoveLockToggle == null)
            {
                return;
            }

            BtnMoveLockToggle.Visibility = isLocked ? Visibility.Collapsed : Visibility.Visible;
            BtnMoveLockToggle.Content = "위치 고정";
        }

        /// <summary>
        /// Ctrl+7 단축키로 번역창 이동 가능 상태와 클릭 관통 잠금 상태를 전환합니다.
        /// 잠금 상태에서는 마우스 클릭이 게임으로 통과하고, 이동 상태에서는 창 테두리를 녹색으로 바꿔 드래그 가능함을 표시합니다.
        /// </summary>
        private void ToggleMoveLock()
        {
            isLocked = !isLocked;
            this.Topmost = true;

            if (isLocked)
            {
                WindowUtils.SetClickThrough(this);
                MainBorder.BorderBrush = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString("#55FFFFFF"));
                UpdateCaptureBorder(false);
            }
            else
            {
                WindowUtils.RemoveClickThrough(this);
                MainBorder.BorderBrush = Brushes.LimeGreen;
                UpdateCaptureBorder(true);
            }

            UpdateYellowHotkeyGuideText();
            UpdateMoveLockToggleButton();
        }
    }
}
