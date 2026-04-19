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
    /// MainWindow의 Win32 API 선언과 전역 단축키 ID를 모아 둔 partial 파일입니다.
    /// WPF만으로 처리하기 어려운 전역 단축키, 화면 캡처, 최상단 유지 기능을 user32/gdi32 호출로 연결합니다.
    /// </summary>
    public partial class MainWindow
    {
        // ==========================================
        // 📌 1. Windows API 단축키 등록 (Global Hotkey) 및 화면 캡처(BitBlt)
        // ==========================================
        // RegisterHotKey: OS 전역 단축키를 현재 창 핸들에 연결합니다. id는 WM_HOTKEY 수신 시 어떤 기능인지 구분하는 값입니다.
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vlc);
        // UnregisterHotKey: 종료 시 등록했던 전역 단축키를 해제해 다른 프로그램/다음 실행에 영향을 주지 않게 합니다.
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        // GetWindowDC/ReleaseDC: 전체 화면 DC를 얻고 반납합니다. BitBlt 캡처 전후로 반드시 짝을 맞춰야 합니다.
        [DllImport("user32.dll")]
        public static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        // BitBlt: 화면 DC의 픽셀을 Bitmap의 DC로 복사합니다. 마지막 dwRop 0x00CC0020은 SRCCOPY입니다.
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);
        // 🌟 [추가] 창을 강제로 최상단에 고정하는 API
        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010; // 창이 포커스를 뺏지 않도록 함 (게임 방해 금지)

        private const uint MOD_CONTROL = 0x0002;
        private const int WM_HOTKEY = 0x0312;

        private const int ID_HOTKEY_MOVE_LOCK = 9001;
        private const int ID_HOTKEY_AREA_SELECT = 9002;
        private const int ID_HOTKEY_TRANSLATE = 9003;
        private const int ID_HOTKEY_AUTO = 9004;
        private const int ID_HOTKEY_TOGGLE_ENGINE = 9005;
        private const int ID_HOTKEY_COPY_RESULT = 9006;
        private const int ID_HOTKEY_LOG_VIEWER = 9007;
        internal const string DefaultGeminiModel = SettingsService.DefaultGeminiModel;
    }
}
