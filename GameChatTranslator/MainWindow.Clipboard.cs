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
        private string lastClipboardTranslationText = "";

        private void ResetClipboardTranslationText()
        {
            lastClipboardTranslationText = "";
        }

        private void AddClipboardTranslationLine(string characterName, string translatedText)
        {
            string line = $"{characterName}{translatedText}".Trim();
            if (string.IsNullOrWhiteSpace(line)) return;

            if (string.IsNullOrWhiteSpace(lastClipboardTranslationText))
            {
                lastClipboardTranslationText = line;
            }
            else
            {
                lastClipboardTranslationText += Environment.NewLine + line;
            }
        }

        private void CopyLastTranslationToClipboard()
        {
            if (string.IsNullOrWhiteSpace(lastClipboardTranslationText))
            {
                AppendLog("복사할 번역 결과가 없습니다.");
                return;
            }

            try
            {
                System.Windows.Clipboard.SetText(lastClipboardTranslationText.Trim());
                AppendLog("번역 결과를 클립보드에 복사했습니다.");
            }
            catch (Exception ex)
            {
                AppendLog($"클립보드 복사 실패: {ex.Message}");
            }
        }
    }
}
