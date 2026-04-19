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
        private void startAreaSelection()
        {
            if (areaSelector != null) { areaSelector.Close(); }
            areaSelector = new AreaSelector();
            areaSelector.Owner = this;
            areaSelector.Show();
        }
        public void SetCaptureArea(Rectangle area)
        {
            gameChatArea = area;
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
            ini.Write("WindowW", this.ActualWidth.ToString());
            ini.Write("WindowH", this.ActualHeight.ToString());

            UpdateCaptureBorder(!isLocked);
        }
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
