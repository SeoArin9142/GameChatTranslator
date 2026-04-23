using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameTranslator
{
    // ==========================================
    // 📌 화면 캡처 영역 지정 창 (UI 비하인드 코드)
    // 단축키(기본 Ctrl+8)를 누르면 전체 화면을 덮는 반투명 창이 뜨고,
    // 사용자가 마우스로 드래그하여 번역할 채팅창 영역을 설정합니다.
    // ==========================================
    public partial class AreaSelector : Window
    {
        // 사용자가 마우스를 처음 클릭한 시작 좌표를 저장
        private System.Windows.Point startPoint;

        // 마우스 드래그가 끝난 후 최종적으로 계산된 사각형 영역
        private Rectangle selectionArea;

        /// <summary>
        /// 캡처 영역 선택 창을 초기화합니다.
        /// InitializeComponent는 AreaSelector.xaml의 Canvas와 선택 테두리를 로드하고,
        /// ConfigureVirtualScreenBounds는 멀티모니터 전체 영역을 덮도록 창 크기를 맞춥니다.
        /// </summary>
        public AreaSelector()
        {
            InitializeComponent();
            ConfigureVirtualScreenBounds();
        }

        /// <summary>
        /// 영역 선택 창을 Windows 가상 화면 전체에 맞춥니다.
        /// SystemParameters.VirtualScreen* 값은 주 모니터뿐 아니라 좌측/상단에 배치된 보조 모니터까지 포함하므로,
        /// 멀티모니터 환경에서도 원하는 채팅 영역을 드래그할 수 있습니다.
        /// </summary>
        private void ConfigureVirtualScreenBounds()
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        // ==========================================
        // 📌 1. 마우스 클릭 시작 이벤트 (드래그 시작)
        // ==========================================
        /// <summary>
        /// 사용자가 캡처 영역 선택 창에서 마우스 버튼을 누른 순간 호출됩니다.
        /// <paramref name="sender"/>는 이벤트를 발생시킨 Window이고,
        /// <paramref name="e"/>는 클릭 위치와 버튼 상태를 담은 WPF 마우스 이벤트 정보입니다.
        /// </summary>
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            // 클릭한 순간의 마우스 좌표를 시작점으로 기록
            startPoint = e.GetPosition(this);

            // 영역 초기화
            selectionArea = Rectangle.Empty;

            // XAML에 있는 SelectionBorder(선택 영역을 보여주는 테두리)를 화면에 표시
            SelectionBorder.Visibility = Visibility.Visible;

            // 테두리의 시작 위치를 마우스 클릭 위치로 이동
            Canvas.SetLeft(SelectionBorder, startPoint.X);
            Canvas.SetTop(SelectionBorder, startPoint.Y);
        }

        // ==========================================
        // 📌 2. 마우스 이동 이벤트 (드래그 중)
        // ==========================================
        /// <summary>
        /// 마우스 왼쪽 버튼을 누른 채 이동할 때 선택 사각형의 위치와 크기를 실시간으로 갱신합니다.
        /// <paramref name="sender"/>는 이벤트를 발생시킨 Window이고,
        /// <paramref name="e"/>는 현재 마우스 좌표와 버튼 상태를 담은 이벤트 정보입니다.
        /// </summary>
        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // 마우스 왼쪽 버튼을 누른 상태로 이동할 때만 실행
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                // 현재 마우스 좌표를 가져옴
                System.Windows.Point currentPoint = e.GetPosition(this);

                // 마우스를 상하좌우 어느 방향으로 드래그하든 항상 정상적인 사각형이 나오도록
                // 시작점과 현재점 중 더 작은 값을 좌상단(Top-Left) 좌표로 설정
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);

                // 가로 넓이와 세로 높이는 두 좌표의 차이의 절댓값으로 계산
                double width = Math.Abs(startPoint.X - currentPoint.X);
                double height = Math.Abs(startPoint.Y - currentPoint.Y);

                // 화면에 그려지는 테두리 UI(SelectionBorder)의 크기와 위치를 실시간으로 갱신
                SelectionBorder.Width = width;
                SelectionBorder.Height = height;
                Canvas.SetLeft(SelectionBorder, x);
                Canvas.SetTop(SelectionBorder, y);

                // 최종적으로 메인 폼에 넘겨줄 C# 그래픽용 Rectangle 구조체 생성
                selectionArea = new Rectangle((int)x, (int)y, (int)width, (int)height);
            }
        }

        // ==========================================
        // 📌 3. 마우스 클릭 종료 이벤트 (드래그 끝)
        // ==========================================
        /// <summary>
        /// 드래그가 끝났을 때 WPF 표시 좌표와 실제 화면 픽셀 좌표를 계산해 MainWindow에 전달합니다.
        /// <paramref name="sender"/>는 이벤트를 발생시킨 Window이고,
        /// <paramref name="e"/>는 마우스 버튼을 놓은 시점의 이벤트 정보입니다.
        /// </summary>
        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            // 드래그를 해서 정상적인 영역이 만들어졌다면
            if (selectionArea != Rectangle.Empty)
            {
                // 이 창을 호출했던 부모 창(MainWindow)을 찾음
                MainWindow mainWindow = Owner as MainWindow;

                // 메인 창의 SetCaptureArea 함수를 실행하여 방금 그린 캡처 영역 데이터를 전달
                Rectangle screenArea = new Rectangle(
                    (int)(Left + selectionArea.X),
                    (int)(Top + selectionArea.Y),
                    selectionArea.Width,
                    selectionArea.Height);

                // PointToScreen은 WPF 장치 독립 좌표를 현재 DPI가 반영된 물리 픽셀 좌표로 변환합니다.
                // BitBlt 캡처는 물리 픽셀을 요구하므로 표시용 좌표(screenArea)와 캡처용 좌표(pixelArea)를 분리 저장합니다.
                System.Windows.Point topLeft = PointToScreen(new System.Windows.Point(selectionArea.X, selectionArea.Y));
                System.Windows.Point bottomRight = PointToScreen(new System.Windows.Point(selectionArea.Right, selectionArea.Bottom));
                Rectangle pixelArea = new Rectangle(
                    (int)Math.Round(Math.Min(topLeft.X, bottomRight.X)),
                    (int)Math.Round(Math.Min(topLeft.Y, bottomRight.Y)),
                    Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.X - topLeft.X))),
                    Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.Y - topLeft.Y))));

                mainWindow.SetCaptureArea(screenArea, pixelArea);

                // 영역 지정이 끝났으므로 반투명 캡처 창은 닫음
                this.Close();
            }
            else
            {
                // 그냥 클릭만 하고 드래그를 하지 않았다면 테두리만 다시 숨김
                SelectionBorder.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 영역 재지정 중 마우스 우클릭 시 현재 선택을 취소하고 오버레이를 닫습니다.
        /// 기존 캡처 영역은 변경하지 않습니다.
        /// </summary>
        private void Window_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            selectionArea = Rectangle.Empty;
            SelectionBorder.Visibility = Visibility.Collapsed;
            Close();
        }
    }
}
