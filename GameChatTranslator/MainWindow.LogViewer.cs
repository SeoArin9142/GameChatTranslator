using System;
using System.IO;
using System.Windows;

namespace GameTranslator
{
    /// <summary>
    /// 세션 로그를 별도 창으로 표시하는 로그 뷰어 연동 기능을 담당합니다.
    /// Ctrl+= 전역 단축키나 환경설정창 버튼에서 호출되어 로그창을 열고 숨깁니다.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>
        /// 현재 실행 세션의 로그 파일 전체 경로를 반환합니다.
        /// <see cref="sessionLogFileName"/>은 MainWindow 생성 시 고정되므로 로그창은 같은 파일을 읽습니다.
        /// </summary>
        private string GetCurrentSessionLogFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", sessionLogFileName);
        }

        /// <summary>
        /// 로그창이 없으면 생성하고, 있으면 기존 창 인스턴스를 재사용합니다.
        /// 창 위치와 크기를 사용자가 옮긴 상태 그대로 유지하기 위해 매번 새로 만들지 않습니다.
        /// </summary>
        private LogViewerWindow EnsureLogViewerWindow()
        {
            if (logViewerWindow == null)
            {
                logViewerWindow = new LogViewerWindow(GetCurrentSessionLogFilePath());
            }

            return logViewerWindow;
        }

        /// <summary>
        /// 로그창을 표시하고 앞으로 가져옵니다.
        /// 환경설정창의 버튼처럼 "열기" 동작만 필요한 곳에서 사용합니다.
        /// </summary>
        public void ShowLogViewerWindow()
        {
            LogViewerWindow viewer = EnsureLogViewerWindow();
            if (!viewer.IsVisible)
            {
                viewer.Show();
                AppendLog("로그창을 열었습니다.");
            }

            if (viewer.WindowState == WindowState.Minimized)
            {
                viewer.WindowState = WindowState.Normal;
            }

            viewer.Topmost = true;
            viewer.Activate();
            viewer.Topmost = false;
            PushOcrPerformanceSummaryToLogViewer();
        }

        /// <summary>
        /// Ctrl+= 단축키에서 로그창 표시 상태를 토글합니다.
        /// 닫는 동작은 실제 종료가 아니라 숨김 처리이므로 다시 열면 기존 위치와 크기가 유지됩니다.
        /// </summary>
        private void ToggleLogViewerWindow()
        {
            LogViewerWindow viewer = EnsureLogViewerWindow();
            if (viewer.IsVisible)
            {
                viewer.Hide();
                AppendLog("로그창을 숨겼습니다.");
                return;
            }

            ShowLogViewerWindow();
        }

        /// <summary>
        /// AppendLog에서 생성한 새 로그 문자열을 열려 있는 로그창에 전달합니다.
        /// 로그창이 아직 생성되지 않았다면 파일에만 저장하고, 나중에 열 때 파일 전체를 읽어 표시합니다.
        /// </summary>
        private void PushLogEntryToLogViewer(string logEntry)
        {
            logViewerWindow?.AppendLogEntry(logEntry);
        }
    }
}
