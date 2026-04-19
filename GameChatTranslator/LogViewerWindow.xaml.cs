using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GameTranslator
{
    /// <summary>
    /// 현재 세션 로그 파일을 별도 창에서 실시간으로 보여주는 창입니다.
    /// 타이머로 로그 파일 끝부분을 주기적으로 읽으며, 창을 닫으면 종료하지 않고 숨김 처리합니다.
    /// </summary>
    public partial class LogViewerWindow : Window
    {
        private readonly DispatcherTimer refreshTimer;
        private readonly string logFilePath;
        private long lastReadPosition;
        private bool waitingMessageShown;
        private bool allowClose;

        /// <summary>
        /// 로그 뷰어 창을 생성합니다.
        /// <paramref name="logFilePath"/>는 MainWindow가 이번 실행에서 사용하는 logs/log_*.txt 파일 경로입니다.
        /// </summary>
        public LogViewerWindow(string logFilePath)
        {
            InitializeComponent();

            this.logFilePath = logFilePath;
            refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            refreshTimer.Tick += (s, e) => ReadNewLogContent();

            Loaded += (s, e) =>
            {
                ReloadFromStart();
                refreshTimer.Start();
            };
            IsVisibleChanged += (s, e) =>
            {
                if (IsVisible) refreshTimer.Start();
                else refreshTimer.Stop();
            };
        }

        /// <summary>
        /// 창 닫기 버튼을 눌렀을 때 실제로 닫지 않고 숨깁니다.
        /// 다시 Ctrl+= 또는 환경설정창 버튼으로 열면 기존 위치와 크기를 유지합니다.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (allowClose)
            {
                refreshTimer.Stop();
                base.OnClosing(e);
                return;
            }

            e.Cancel = true;
            Hide();
        }

        /// <summary>
        /// 메인 프로그램 종료 시 로그창을 실제로 닫습니다.
        /// 일반 사용자의 X 버튼은 숨김이지만, 애플리케이션 종료 중에는 창을 남기지 않아야 합니다.
        /// </summary>
        public void CloseForShutdown()
        {
            allowClose = true;
            Close();
        }

        /// <summary>
        /// 로그 파일을 처음부터 다시 읽어 화면에 표시합니다.
        /// 실제 로그 파일은 수정하지 않고 표시 내용과 읽기 위치만 갱신합니다.
        /// </summary>
        private void ReloadFromStart()
        {
            TxtLog.Clear();
            lastReadPosition = 0;
            waitingMessageShown = false;
            ReadNewLogContent();
        }

        /// <summary>
        /// 마지막으로 읽은 위치 이후에 추가된 로그만 읽어 TextBox에 붙입니다.
        /// 로그 파일이 아직 없거나 외부에서 잘렸다면 안전하게 처음부터 다시 읽습니다.
        /// </summary>
        private void ReadNewLogContent()
        {
            try
            {
                TxtStatus.Text = logFilePath;

                if (!File.Exists(logFilePath))
                {
                    if (!waitingMessageShown)
                    {
                        TxtLog.Text = "로그 파일 생성 대기 중...";
                        waitingMessageShown = true;
                    }
                    return;
                }

                FileInfo fileInfo = new FileInfo(logFilePath);
                if (fileInfo.Length < lastReadPosition)
                {
                    TxtLog.Clear();
                    lastReadPosition = 0;
                }

                if (fileInfo.Length == lastReadPosition) return;

                using FileStream stream = new FileStream(logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(lastReadPosition, SeekOrigin.Begin);

                long unreadLength = stream.Length - lastReadPosition;
                if (unreadLength > int.MaxValue)
                {
                    unreadLength = int.MaxValue;
                }

                byte[] buffer = new byte[(int)unreadLength];
                int readBytes = stream.Read(buffer, 0, buffer.Length);
                lastReadPosition = stream.Position;

                if (readBytes <= 0) return;

                string appendedText = Encoding.UTF8.GetString(buffer, 0, readBytes);
                if (waitingMessageShown)
                {
                    TxtLog.Clear();
                    waitingMessageShown = false;
                }

                TxtLog.AppendText(appendedText);
                if (CheckAutoScroll.IsChecked == true)
                {
                    TxtLog.ScrollToEnd();
                }
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"로그 읽기 실패: {ex.Message}";
            }
        }

        /// <summary>
        /// [새로고침] 버튼 클릭 시 로그 파일 전체를 다시 읽습니다.
        /// </summary>
        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            ReloadFromStart();
        }

        /// <summary>
        /// [화면 지우기] 버튼 클릭 시 TextBox만 비우고 실제 로그 파일은 보존합니다.
        /// 이후 새로 추가되는 로그부터 다시 화면에 표시됩니다.
        /// </summary>
        private void BtnClearView_Click(object sender, RoutedEventArgs e)
        {
            TxtLog.Clear();
            waitingMessageShown = false;

            if (File.Exists(logFilePath))
            {
                lastReadPosition = new FileInfo(logFilePath).Length;
            }
        }
    }
}
