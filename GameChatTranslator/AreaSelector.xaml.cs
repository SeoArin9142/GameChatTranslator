// AreaSelector.xaml.cs
using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GameTranslator
{
    public partial class AreaSelector : Window
    {
        private System.Windows.Point startPoint;
        private Rectangle selectionArea;

        public AreaSelector()
        {
            InitializeComponent();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            startPoint = e.GetPosition(this);
            selectionArea = Rectangle.Empty;
            SelectionBorder.Visibility = Visibility.Visible;
            Canvas.SetLeft(SelectionBorder, startPoint.X);
            Canvas.SetTop(SelectionBorder, startPoint.Y);
        }

        private void Window_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                System.Windows.Point currentPoint = e.GetPosition(this);
                double x = Math.Min(startPoint.X, currentPoint.X);
                double y = Math.Min(startPoint.Y, currentPoint.Y);
                double width = Math.Abs(startPoint.X - currentPoint.X);
                double height = Math.Abs(startPoint.Y - currentPoint.Y);

                SelectionBorder.Width = width;
                SelectionBorder.Height = height;
                Canvas.SetLeft(SelectionBorder, x);
                Canvas.SetTop(SelectionBorder, y);

                selectionArea = new Rectangle((int)x, (int)y, (int)width, (int)height);
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (selectionArea != Rectangle.Empty)
            {
                // 선택한 영역 정보를 MainWindow에 전달
                MainWindow mainWindow = Owner as MainWindow;
                mainWindow.SetCaptureArea(selectionArea);
                this.Close(); // AreaSelector 창 닫기
            }
            else
            {
                SelectionBorder.Visibility = Visibility.Collapsed;
            }
        }
    }
}