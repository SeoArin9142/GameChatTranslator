using System;
using System.Windows;
using GameTranslator;
using Velopack;

namespace GameChatTranslator
{
    /// <summary>
    /// WPF 애플리케이션의 진입점을 나타내는 클래스입니다.
    /// App.xaml에서 StartupUri로 지정된 MainWindow를 생성하고,
    /// 애플리케이션 리소스와 전역 수명 주기를 WPF 런타임에 연결합니다.
    /// </summary>
    public partial class App : System.Windows.Application
    {
        /// <summary>
        /// Velopack 훅을 WPF 초기화보다 먼저 처리하는 사용자 지정 진입점입니다.
        /// 설치/업데이트 시 Velopack이 특수 인수로 메인 바이너리를 다시 실행하므로,
        /// Main 초기에 Run()을 호출해야 설치/업데이트 훅을 올바르게 처리할 수 있습니다.
        /// </summary>
        [STAThread]
        public static void Main(string[] args)
        {
            VelopackApp
                .Build()
                .SetAutoApplyOnStartup(false)
                .Run();

            App app = new App();
            app.InitializeComponent();
            app.Run();
        }

        /// <summary>
        /// StartupUri 대신 직접 MainWindow를 만들어 애플리케이션 메인 창으로 등록합니다.
        /// </summary>
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            GameTranslator.MainWindow window = new GameTranslator.MainWindow();
            MainWindow = window;
            window.Show();
        }
    }

}
