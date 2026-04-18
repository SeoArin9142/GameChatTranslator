using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GameTranslator
{
    // ==========================================
    // 📌 윈도우 투명화(클릭 관통) 제어 유틸리티 클래스
    // WPF 기본 기능에는 '클릭 통과(Click-Through)' 기능이 없으므로,
    // Windows OS의 핵심 라이브러리인 user32.dll(Win32 API)을 직접 호출하여 제어합니다.
    // ==========================================
    public static class WindowUtils
    {
        // Windows API: 특정 윈도우의 속성값을 가져오는 함수 (메모리에서 읽어오기)
        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        // Windows API: 특정 윈도우의 속성값을 덮어쓰는 함수 (메모리에 쓰기)
        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        // 속성 인덱스: 윈도우의 "확장 스타일(Extended Style)"을 제어하겠다는 시스템 지정 번호
        private const int GWL_EXSTYLE = -20;

        // 속성 플래그: 마우스 클릭을 무시하고 윈도우 뒤쪽에 있는 프로그램으로 신호를 패스하는 기능
        private const int WS_EX_TRANSPARENT = 0x00000020;

        // ==========================================
        // 📌 1. 마우스 클릭 관통 활성화 (게임 모드)
        // 창이 화면에 보이기만 할 뿐, 마우스로 클릭하면 번역창 뒤에 있는 게임(스트리노바)이 클릭되게 만듭니다.
        // ==========================================
        public static void SetClickThrough(System.Windows.Window window)
        {
            // 현재 띄워진 WPF 창의 OS 고유 핸들(ID)을 추출합니다.
            IntPtr hWnd = new WindowInteropHelper(window).Handle;

            // 해당 창이 원래 가지고 있던 확장 스타일 값을 가져옵니다.
            int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 기존 스타일을 유지한 채로, '클릭 관통(WS_EX_TRANSPARENT)' 기능만 비트 연산자(|)를 통해 추가합니다.
            SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT);
        }

        // ==========================================
        // 📌 2. 마우스 클릭 관통 비활성화 (설정 모드)
        // 번역창을 다시 마우스로 클릭할 수 있도록 만들어, 사용자가 창을 잡고 드래그(이동)할 수 있게 합니다.
        // ==========================================
        public static void RemoveClickThrough(System.Windows.Window window)
        {
            // 현재 띄워진 WPF 창의 OS 고유 핸들(ID)을 추출합니다.
            IntPtr hWnd = new WindowInteropHelper(window).Handle;

            // 해당 창의 현재 확장 스타일 값을 가져옵니다.
            int extendedStyle = GetWindowLong(hWnd, GWL_EXSTYLE);

            // 비트 연산자(& ~)를 사용하여 기존 스타일에서 '클릭 관통' 속성만 정확히 쏙 빼냅니다.
            SetWindowLong(hWnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
        }
    }
}