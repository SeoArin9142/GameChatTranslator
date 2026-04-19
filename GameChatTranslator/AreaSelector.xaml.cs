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

        public AreaSelector()
        {
            InitializeComponent();
        }

        // ==========================================
        // 📌 1. 마우스 클릭 시작 이벤트 (드래그 시작)
        // ==========================================
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
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
        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            // 드래그를 해서 정상적인 영역이 만들어졌다면
            if (selectionArea != Rectangle.Empty)
            {
                // 이 창을 호출했던 부모 창(MainWindow)을 찾음
                MainWindow mainWindow = Owner as MainWindow;

                // 메인 창의 SetCaptureArea 함수를 실행하여 방금 그린 캡처 영역 데이터를 전달
                mainWindow.SetCaptureArea(selectionArea);

                // 영역 지정이 끝났으므로 반투명 캡처 창은 닫음
                this.Close();
            }
            else
            {
                // 그냥 클릭만 하고 드래그를 하지 않았다면 테두리만 다시 숨김
                SelectionBorder.Visibility = Visibility.Collapsed;
            }
        }
    }
}
