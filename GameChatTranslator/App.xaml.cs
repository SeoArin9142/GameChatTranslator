using System;
using System.Threading;
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
        private const string SingleInstanceMutexName = @"Local\GameChatTranslator.SingleInstance";
        private const string SingleInstanceActivationEventName = @"Local\GameChatTranslator.SingleInstance.Activate";

        private static Mutex singleInstanceMutex;
        private static EventWaitHandle singleInstanceActivationEvent;
        private RegisteredWaitHandle activationWaitRegistration;

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

            if (!TryAcquireSingleInstance())
            {
                return;
            }

            try
            {
                App app = new App();
                app.InitializeComponent();
                app.Run();
            }
            finally
            {
                ReleaseSingleInstance();
            }
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

            if (singleInstanceActivationEvent != null)
            {
                activationWaitRegistration = ThreadPool.RegisterWaitForSingleObject(
                    singleInstanceActivationEvent,
                    OnSingleInstanceActivationRequested,
                    null,
                    Timeout.Infinite,
                    false);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            activationWaitRegistration?.Unregister(null);
            activationWaitRegistration = null;
            base.OnExit(e);
        }

        private static bool TryAcquireSingleInstance()
        {
            bool createdNew;
            singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out createdNew);
            if (!createdNew)
            {
                try
                {
                    singleInstanceMutex.Dispose();
                }
                catch
                {
                }

                singleInstanceMutex = null;
                TrySignalExistingInstance();
                return false;
            }

            bool createdActivationEvent;
            singleInstanceActivationEvent = new EventWaitHandle(
                false,
                EventResetMode.AutoReset,
                SingleInstanceActivationEventName,
                out createdActivationEvent);

            return true;
        }

        private static void ReleaseSingleInstance()
        {
            try
            {
                singleInstanceActivationEvent?.Dispose();
            }
            catch
            {
            }
            finally
            {
                singleInstanceActivationEvent = null;
            }

            if (singleInstanceMutex == null)
            {
                return;
            }

            try
            {
                singleInstanceMutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }
            finally
            {
                singleInstanceMutex.Dispose();
                singleInstanceMutex = null;
            }
        }

        private static void TrySignalExistingInstance()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    using EventWaitHandle existingInstanceEvent = EventWaitHandle.OpenExisting(SingleInstanceActivationEventName);
                    existingInstanceEvent.Set();
                    return;
                }
                catch (WaitHandleCannotBeOpenedException)
                {
                    Thread.Sleep(150);
                }
            }
        }

        private void OnSingleInstanceActivationRequested(object state, bool timedOut)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow is GameTranslator.MainWindow gameTranslatorMainWindow)
                {
                    gameTranslatorMainWindow.ActivateFromSingleInstanceRequest();
                    return;
                }

                MainWindow?.Activate();
            }));
        }
    }

}
