using System.Windows;

namespace GameTranslator
{
    public partial class MainWindow
    {
        private const string TranslationApiStatusDetailHint = "상세 원인: Ctrl+= 로그창";

        /// <summary>
        /// 번역 API 실패를 번역창 상단에 짧게 표시합니다.
        /// 자세한 원인은 기존처럼 로그창에 남기고, 오버레이에는 게임을 가리지 않도록 핵심 상태만 보여줍니다.
        /// </summary>
        private void ShowTranslationApiStatus(string message)
        {
            if (TxtApiStatus == null || string.IsNullOrWhiteSpace(message)) return;

            string displayMessage = message.Trim();
            if (!displayMessage.Contains("Ctrl+="))
            {
                displayMessage += "\n" + TranslationApiStatusDetailHint;
            }

            TxtApiStatus.Text = displayMessage;
            TxtApiStatus.Visibility = Visibility.Visible;
            apiStatusTimer?.Stop();
            apiStatusTimer?.Start();
        }

        /// <summary>
        /// 번역 API 상태 안내를 숨깁니다.
        /// DispatcherTimer에서 자동 호출되며, 다음 오류가 오기 전까지 오버레이 높이를 줄입니다.
        /// </summary>
        private void HideTranslationApiStatus()
        {
            apiStatusTimer?.Stop();

            if (TxtApiStatus == null) return;
            TxtApiStatus.Text = "";
            TxtApiStatus.Visibility = Visibility.Collapsed;
        }
    }
}
