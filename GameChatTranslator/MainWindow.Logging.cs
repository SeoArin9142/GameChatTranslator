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
        private void AppendLog(string systemMessage)
        {
            try
            {
                string logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDirPath)) Directory.CreateDirectory(logDirPath);

                // 🌟 수정: 매번 새로 만들지 않고, 켜질 때 고정된 파일명 사용
                string filePath = Path.Combine(logDirPath, sessionLogFileName);

                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [System] {systemMessage}{Environment.NewLine}";
                File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { }
        }
        private void AppendLog(string original, string translated, string engineName)
        {
            try
            {
                string logDirPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                if (!Directory.Exists(logDirPath)) Directory.CreateDirectory(logDirPath);

                // 🌟 수정: 매번 새로 만들지 않고, 켜질 때 고정된 파일명 사용
                string filePath = Path.Combine(logDirPath, sessionLogFileName);

                string logEntry = $"[{DateTime.Now:HH:mm:ss}] [{engineName}] {original.Trim()} -> {translated.Trim()}{Environment.NewLine}";
                File.AppendAllText(filePath, logEntry, System.Text.Encoding.UTF8);
            }
            catch { }
        }
    }
}
