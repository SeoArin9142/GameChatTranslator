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
        /// 시스템 상태 메시지를 세션 로그 파일에 기록합니다.
        /// <paramref name="systemMessage"/>는 프로그램 시작, 설정 변경, 오류 같은 사용자/개발자 확인용 메시지입니다.
        /// 로그 저장 실패가 번역 동작을 막지 않도록 예외는 내부에서 흡수합니다.
        /// </summary>
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

        /// <summary>
        /// 번역 처리 결과를 원문, 번역문, 사용 엔진과 함께 세션 로그 파일에 기록합니다.
        /// <paramref name="original"/>은 OCR에서 추출한 원문,
        /// <paramref name="translated"/>는 최종 출력 번역문,
        /// <paramref name="engineName"/>은 Google/Gemini/Skip 등 처리 경로 이름입니다.
        /// </summary>
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
