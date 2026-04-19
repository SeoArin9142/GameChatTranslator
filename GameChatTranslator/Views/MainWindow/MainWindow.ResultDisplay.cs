using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;

namespace GameTranslator
{
    public partial class MainWindow
    {
        private const string ResultDisplayModeHistory = "History";
        private readonly List<TranslationDisplayLine> translationDisplayHistory = new List<TranslationDisplayLine>();

        /// <summary>
        /// 번역 결과 표시 영역에 보관할 한 줄의 번역 내역입니다.
        /// CharacterName은 "[캐릭터명]: " 형식, TranslatedText는 최종 번역 결과입니다.
        /// </summary>
        private class TranslationDisplayLine
        {
            public string CharacterName { get; set; } = "";
            public string TranslatedText { get; set; } = "";
        }

        /// <summary>
        /// 새 OCR 결과를 화면에 반영하기 전 표시 모드에 맞게 결과 영역과 클립보드 버퍼를 준비합니다.
        /// 최신 결과 모드에서는 기존 내용을 지우고, 누적 모드에서는 기존 내역을 유지합니다.
        /// </summary>
        private void BeginTranslationResultUpdate()
        {
            if (!ShouldAccumulateTranslationResults())
            {
                translationDisplayHistory.Clear();
                TxtResult.Inlines.Clear();
                ResetClipboardTranslationText();
            }
        }

        /// <summary>
        /// 번역된 한 줄을 현재 표시 모드에 맞게 번역창과 클립보드 복사 대상에 추가합니다.
        /// 누적 모드에서는 최근 N줄만 유지하고 화면 전체를 다시 렌더링합니다.
        /// </summary>
        private void AddTranslationResultToDisplay(string characterName, string translatedText)
        {
            if (ShouldAccumulateTranslationResults())
            {
                translationDisplayHistory.Add(new TranslationDisplayLine
                {
                    CharacterName = characterName,
                    TranslatedText = translatedText
                });

                TrimTranslationDisplayHistory();
                RenderTranslationDisplayHistory();
                return;
            }

            AddTranslationResultRun(characterName, translatedText);
            AddClipboardTranslationLine(characterName, translatedText);
            ScrollResultToEnd();
        }

        /// <summary>
        /// 누적 표시 모드에서 오래된 번역 줄을 삭제해 UI가 끝없이 길어지는 것을 막습니다.
        /// 유지 개수는 ResultHistoryLimit 설정값이며 기본값은 5줄입니다.
        /// </summary>
        private void TrimTranslationDisplayHistory()
        {
            int limit = ReadResultHistoryLimit();
            while (translationDisplayHistory.Count > limit)
            {
                translationDisplayHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// 누적 표시 모드의 현재 내역을 TextBlock과 클립보드 버퍼에 다시 그립니다.
        /// </summary>
        private void RenderTranslationDisplayHistory()
        {
            TxtResult.Inlines.Clear();
            ResetClipboardTranslationText();

            foreach (TranslationDisplayLine line in translationDisplayHistory)
            {
                AddTranslationResultRun(line.CharacterName, line.TranslatedText);
                AddClipboardTranslationLine(line.CharacterName, line.TranslatedText);
            }

            ScrollResultToEnd();
        }

        /// <summary>
        /// 번역 결과 TextBlock에 캐릭터명과 번역문 Run을 추가합니다.
        /// </summary>
        private void AddTranslationResultRun(string characterName, string translatedText)
        {
            TxtResult.Inlines.Add(new Run(characterName) { Foreground = Brushes.Gold, FontWeight = FontWeights.Bold });
            TxtResult.Inlines.Add(new Run(translatedText) { Foreground = Brushes.White });
            TxtResult.Inlines.Add(new LineBreak());
        }

        /// <summary>
        /// 결과 영역이 스크롤 가능한 상태라면 새 번역 줄이 보이도록 맨 아래로 이동합니다.
        /// </summary>
        private void ScrollResultToEnd()
        {
            Dispatcher.BeginInvoke(new Action(() => ResultScrollViewer?.ScrollToEnd()), DispatcherPriority.Background);
        }

        /// <summary>
        /// config.ini의 ResultDisplayMode 값이 History인지 확인합니다.
        /// History이면 번역 결과를 누적 표시하고, 그 외 값은 최신 결과만 표시합니다.
        /// </summary>
        private bool ShouldAccumulateTranslationResults()
        {
            string displayMode = ini.Read("ResultDisplayMode") ?? SettingsService.DefaultResultDisplayMode;
            return displayMode.Equals(ResultDisplayModeHistory, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 누적 표시 모드에서 유지할 최대 번역 줄 수를 읽습니다.
        /// 너무 작거나 큰 값은 1~10 범위로 보정합니다.
        /// </summary>
        private int ReadResultHistoryLimit()
        {
            return SettingsValueNormalizer.NormalizeResultHistoryLimit(ini.Read("ResultHistoryLimit"));
        }
    }
}
