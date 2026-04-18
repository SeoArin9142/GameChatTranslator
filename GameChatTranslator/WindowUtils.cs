using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GameTranslator
{
    public static class WindowUtils
    {
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;

        // 마우스 클릭 통과 ON (게임 모드)
        public static void SetClickThrough(System.Windows.Window window)
        {
            IntPtr hWnd = new WindowInteropHelper(window).Handle;
            int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }

        // 마우스 클릭 통과 OFF (드래그 이동 모드)
        public static void RemoveClickThrough(System.Windows.Window window)
        {
            IntPtr hWnd = new WindowInteropHelper(window).Handle;
            int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }
}