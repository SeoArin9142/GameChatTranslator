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
        // 마지막 번역 실행에서 화면에 출력한 번역문을 클립보드 복사용으로 누적 저장합니다.
        private string lastClipboardTranslationText = "";

        /// <summary>
        /// 새 번역 결과를 만들기 전에 클립보드 복사용 문자열을 초기화합니다.
        /// 번역창이 최신 OCR 결과만 표시하는 구조이므로 복사 대상도 최신 결과와 맞춰 리셋합니다.
        /// </summary>
        private void ResetClipboardTranslationText()
        {
            lastClipboardTranslationText = "";
        }

        /// <summary>
        /// 번역된 한 줄을 클립보드 복사용 텍스트에 추가합니다.
        /// <paramref name="characterName"/>은 "[캐릭터명]: " 형식의 말한 사람 표시이고,
        /// <paramref name="translatedText"/>는 최종 번역 결과 문자열입니다.
        /// </summary>
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

        /// <summary>
        /// 최근 번역 결과 전체를 Windows 클립보드에 복사합니다.
        /// 복사할 내용이 없거나 OS 클립보드 접근에 실패하면 로그에 사용자 안내 메시지를 남깁니다.
        /// </summary>
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
