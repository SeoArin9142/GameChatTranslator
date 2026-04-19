using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using ColorConverter = System.Windows.Media.ColorConverter;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace GameTranslator
{
    public partial class MainWindow
    {
        /// <summary>
        /// 캡처 영역 선택용 오버레이 창을 엽니다.
        /// 이미 열린 AreaSelector가 있으면 닫고 새로 만들어 중복 선택 창이 쌓이지 않도록 합니다.
        /// </summary>
        private void startAreaSelection()
        {
            if (areaSelector != null) { areaSelector.Close(); }
            areaSelector = new AreaSelector();
            areaSelector.Owner = this;
            areaSelector.Show();
        }

        /// <summary>
        /// 표시 좌표만 받은 경우 현재 DPI 배율을 적용해 캡처용 물리 픽셀 좌표를 계산한 뒤 저장합니다.
        /// <paramref name="area"/>는 WPF 화면 표시 기준의 채팅 영역 좌표와 크기입니다.
        /// </summary>
        public void SetCaptureArea(Rectangle area)
        {
            SetCaptureArea(area, ConvertDisplayAreaToPixels(area));
        }

        /// <summary>
        /// 사용자가 선택한 채팅 캡처 영역을 메모리와 config.ini에 저장하고 번역창 위치를 재배치합니다.
        /// <paramref name="area"/>는 WPF 표시 좌표계에서의 영역이고,
        /// <paramref name="pixelArea"/>는 BitBlt 캡처에 사용할 실제 물리 픽셀 좌표계의 영역입니다.
        /// </summary>
        public void SetCaptureArea(Rectangle area, Rectangle pixelArea)
        {
            gameChatArea = area;
            gameChatCaptureArea = pixelArea;
            this.Top = area.Y + area.Height + 50;
            this.Left = area.X - 5;
            this.SizeToContent = SizeToContent.Manual;
            this.Width = area.Width;
            this.MinWidth = area.Width;
            this.SizeToContent = SizeToContent.Height;
            this.Visibility = Visibility.Visible;
            this.Topmost = true;
            UpdateYellowHotkeyGuideText();

            ini.Write("CaptureX", area.X.ToString());
            ini.Write("CaptureY", area.Y.ToString());
            ini.Write("CaptureW", area.Width.ToString());
            ini.Write("CaptureH", area.Height.ToString());
            ini.Write("CapturePixelX", pixelArea.X.ToString());
            ini.Write("CapturePixelY", pixelArea.Y.ToString());
            ini.Write("CapturePixelW", pixelArea.Width.ToString());
            ini.Write("CapturePixelH", pixelArea.Height.ToString());
            ini.Write("WindowW", this.ActualWidth.ToString());
            ini.Write("WindowH", this.ActualHeight.ToString());

            ResetTranslationCache("캡처 영역 변경");
            UpdateCaptureBorder(!isLocked);
        }

        /// <summary>
        /// WPF 장치 독립 좌표를 현재 모니터 DPI가 반영된 물리 픽셀 좌표로 변환합니다.
        /// <paramref name="area"/>는 WPF가 사용하는 표시 좌표계의 사각형입니다.
        /// 반환값은 Win32 BitBlt가 요구하는 픽셀 단위 사각형입니다.
        /// </summary>
        private Rectangle ConvertDisplayAreaToPixels(Rectangle area)
        {
            PresentationSource source = PresentationSource.FromVisual(this);
            Matrix transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

            System.Windows.Point topLeft = transform.Transform(new System.Windows.Point(area.X, area.Y));
            System.Windows.Point bottomRight = transform.Transform(new System.Windows.Point(area.Right, area.Bottom));

            return new Rectangle(
                (int)Math.Round(Math.Min(topLeft.X, bottomRight.X)),
                (int)Math.Round(Math.Min(topLeft.Y, bottomRight.Y)),
                Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.X - topLeft.X))),
                Math.Max(1, (int)Math.Round(Math.Abs(bottomRight.Y - topLeft.Y))));
        }

        /// <summary>
        /// 현재 번역에 사용할 캡처용 물리 픽셀 영역을 반환합니다.
        /// 저장된 pixelArea가 유효하면 그대로 사용하고, 구버전 설정처럼 표시 좌표만 있는 경우 DPI 변환으로 보정합니다.
        /// </summary>
        private Rectangle GetCapturePixelArea()
        {
            if (gameChatCaptureArea != Rectangle.Empty &&
                gameChatCaptureArea.Width > 0 &&
                gameChatCaptureArea.Height > 0)
            {
                return gameChatCaptureArea;
            }

            return ConvertDisplayAreaToPixels(gameChatArea);
        }

        /// <summary>
        /// 이동 모드에서 사용자가 현재 감시 영역을 볼 수 있도록 빨간 테두리 창을 표시하거나 숨깁니다.
        /// <paramref name="show"/>가 true이면 테두리를 보이고, false이면 숨깁니다.
        /// </summary>
        private void UpdateCaptureBorder(bool show)
        {
            if (gameChatArea == Rectangle.Empty) return;

            if (captureBorderWindow == null)
            {
                captureBorderWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    BorderBrush = Brushes.Red,
                    BorderThickness = new Thickness(2),
                    Opacity = 0.8
                };
                captureBorderWindow.Show();
                WindowUtils.SetClickThrough(captureBorderWindow);
            }

            captureBorderWindow.Left = gameChatArea.X;
            captureBorderWindow.Top = gameChatArea.Y;
            captureBorderWindow.Width = gameChatArea.Width;
            captureBorderWindow.Height = gameChatArea.Height;
            captureBorderWindow.Visibility = show ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>
        /// OCR 문제 분석용 캡처 이미지를 Captures 폴더에 저장합니다.
        /// <paramref name="bitmap"/>은 저장할 원본/전처리/크롭 이미지이고,
        /// <paramref name="suffix"/>는 파일명 뒤에 붙여 이미지 종류를 구분하는 문자열입니다.
        /// </summary>
        private void SaveDebugImage(Bitmap bitmap, string suffix)
        {
            try
            {
                string captureDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Captures");
                if (!Directory.Exists(captureDirPath)) Directory.CreateDirectory(captureDirPath);

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                string fileName = $"{timestamp}_{suffix}.png";
                string filePath = Path.Combine(captureDirPath, fileName);

                bitmap.Save(filePath, ImageFormat.Png);
                CleanupCaptureFolder(captureDirPath);
            }
            catch { }
        }

        /// <summary>
        /// Captures 폴더가 과도하게 커지지 않도록 오래된 PNG 파일을 삭제합니다.
        /// <paramref name="folderPath"/>는 정리할 Captures 폴더의 절대 경로입니다.
        /// 자동 번역 주기를 기준으로 최근 약 30분 분량만 유지합니다.
        /// </summary>
        private void CleanupCaptureFolder(string folderPath)
        {
            try
            {
                int interval = int.TryParse(ini.Read("AutoTranslateInterval"), out int i) ? i : 5;
                if (interval < 1) interval = 1;

                // 30분(1800초) 분량의 세트 수를 계산 (한 번에 3장 저장)
                int maxFileCount = (1800 / interval) * 3;
                if (maxFileCount < 20) maxFileCount = 20;

                var directory = new DirectoryInfo(folderPath);
                var files = directory.GetFiles("*.png").OrderByDescending(f => f.CreationTime).ToList();

                if (files.Count > maxFileCount)
                {
                    var filesToDelete = files.Skip(maxFileCount);
                    foreach (var file in filesToDelete)
                    {
                        file.Delete();
                    }
                }
            }
            catch { }
        }
    }
}
