using System.Collections.Generic;
using System.Linq;

namespace GameTranslator
{
    /// <summary>
    /// OCR 처리 모드에 따른 후보 선택 정책과 후보 점수 비교를 담당하는 순수 서비스입니다.
    /// 플랫폼 의존 타입을 참조하지 않아 단위 테스트에서 검증할 수 있습니다.
    /// </summary>
    public sealed class OcrService
    {
        private const int MergeChatLabelBonus = 300;

        /// <summary>
        /// 처리 모드별 OCR 평가 순서를 만듭니다.
        /// Fast/Auto의 첫 단계는 Color + 게임 언어 OCR만 실행하는 fast path입니다.
        /// </summary>
        public IReadOnlyList<OcrEvaluationStep> CreateEvaluationPlan(OcrProcessingMode processingMode)
        {
            return processingMode switch
            {
                OcrProcessingMode.Fast => new[]
                {
                    OcrEvaluationStep.FastPath(OcrPreprocessKind.Color)
                },
                OcrProcessingMode.Auto => new[]
                {
                    OcrEvaluationStep.FastPath(OcrPreprocessKind.Color),
                    OcrEvaluationStep.Fallback(true, OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive)
                },
                _ => new[]
                {
                    OcrEvaluationStep.Fallback(true, OcrPreprocessKind.Color, OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive)
                }
            };
        }

        /// <summary>
        /// OCR 후보가 빠른 경로에서 즉시 번역해도 될 정도로 신뢰 가능한지 판단합니다.
        /// 후보 점수가 양수이고, known character 채팅 라인이 하나 이상 있어야 true입니다.
        /// </summary>
        public bool IsFastPathSuccess<TResults>(
            OcrCandidate<TResults> candidate,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            if (candidate == null || candidate.Score <= 0 || candidate.Lines == null) return false;

            if (contentMode == TranslationContentMode.Etc)
            {
                return candidate.Lines.Any(line => ChatTextAnalyzer.ContainsReadableLetter(line.Text));
            }

            return candidate.Lines.Any(line =>
                ChatTextAnalyzer.TryParseKnownCharacterChatLine(line.Text, characterNames, out _));
        }

        /// <summary>
        /// 두 후보 중 점수가 더 높은 후보를 반환합니다.
        /// 기존 후보가 없으면 새 후보를 그대로 반환하고, 새 후보가 없으면 기존 후보를 유지합니다.
        /// </summary>
        public OcrCandidate<TResults> SelectHigherScore<TResults>(OcrCandidate<TResults> currentBest, OcrCandidate<TResults> nextCandidate)
        {
            if (nextCandidate == null) return currentBest;
            if (currentBest == null || nextCandidate.Score > currentBest.Score) return nextCandidate;
            return currentBest;
        }

        /// <summary>
        /// OCR 라인 목록을 ChatTextAnalyzer 기준으로 점수화합니다.
        /// </summary>
        public int ScoreLines(
            IEnumerable<OcrLine> lines,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            IEnumerable<string> lineTexts = lines?.Select(line => line.Text);
            return contentMode == TranslationContentMode.Etc
                ? ChatTextAnalyzer.ScoreReadableTextCandidate(lineTexts)
                : ChatTextAnalyzer.ScoreOcrCandidate(lineTexts, characterNames);
        }

        /// <summary>
        /// 언어별 OCR 병합 라인 후보 중 현재 모드에서 가장 읽기 좋은 결과를 선택합니다.
        /// ETC 모드에서는 gameLang 고정 대신 언어별 결과를 비교해 더 읽을 만한 OCR 결과를 고르기 위해 사용합니다.
        /// </summary>
        public OcrLanguageSelection SelectBestLanguageSelection(
            IEnumerable<OcrLanguageCandidate> candidates,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            OcrLanguageSelection bestSelection = null;

            foreach (OcrLanguageCandidate candidate in candidates ?? Enumerable.Empty<OcrLanguageCandidate>())
            {
                string languageCode = candidate?.LanguageCode ?? "";
                List<OcrLine> lines = candidate?.Lines ?? new List<OcrLine>();
                int score = ScoreLines(lines, characterNames, contentMode);

                if (bestSelection == null ||
                    score > bestSelection.Score ||
                    (score == bestSelection.Score &&
                     string.Compare(languageCode, bestSelection.LanguageCode, System.StringComparison.OrdinalIgnoreCase) < 0))
                {
                    bestSelection = new OcrLanguageSelection(languageCode, lines, score);
                }
            }

            return bestSelection != null && bestSelection.Score > 0 ? bestSelection : null;
        }

        /// <summary>
        /// 여러 언어 조합이 반환한 OCR 라인을 줄 인덱스별로 비교해 가장 읽기 좋은 줄만 병합합니다.
        /// 실험용 외부 OCR 비교에서 전체 언어 조합 하나를 통째로 선택하지 않고, 줄마다 더 나은 결과를 섞어 보기 위해 사용합니다.
        /// </summary>
        public List<OcrLine> MergeBestLinesByIndex(
            IEnumerable<OcrLanguageCandidate> candidates,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            List<OcrLanguageCandidate> candidateList = (candidates ?? Enumerable.Empty<OcrLanguageCandidate>())
                .Where(candidate => candidate != null && candidate.Lines != null)
                .ToList();

            if (candidateList.Count == 0)
            {
                return new List<OcrLine>();
            }

            int maxLineCount = candidateList.Max(candidate => candidate.Lines.Count);
            var mergedLines = new List<OcrLine>();

            for (int lineIndex = 0; lineIndex < maxLineCount; lineIndex++)
            {
                OcrLine bestLine = null;
                string bestLanguageCode = "";
                int bestScore = int.MinValue;

                foreach (OcrLanguageCandidate candidate in candidateList)
                {
                    if (lineIndex >= candidate.Lines.Count)
                    {
                        continue;
                    }

                    OcrLine currentLine = candidate.Lines[lineIndex];
                    string currentText = currentLine?.Text ?? "";
                    if (string.IsNullOrWhiteSpace(currentText))
                    {
                        continue;
                    }

                    int currentScore = ScoreMergedLineForSelection(currentLine, characterNames, contentMode);

                    if (bestLine == null ||
                        currentScore > bestScore ||
                        (currentScore == bestScore &&
                         string.Compare(candidate.LanguageCode, bestLanguageCode, System.StringComparison.OrdinalIgnoreCase) < 0))
                    {
                        bestLine = currentLine;
                        bestLanguageCode = candidate.LanguageCode ?? "";
                        bestScore = currentScore;
                    }
                }

                if (bestLine != null)
                {
                    mergedLines.Add(new OcrLine
                    {
                        Top = bestLine.Top,
                        Bottom = bestLine.Bottom,
                        Text = bestLine.Text
                    });
                }
            }

            return mergedLines;
        }

        private int ScoreMergedLineForSelection(
            OcrLine line,
            ISet<string> characterNames,
            TranslationContentMode contentMode)
        {
            string text = line?.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }

            if (contentMode == TranslationContentMode.Etc)
            {
                return ScoreLines(
                    new[] { new OcrLine { Top = line.Top, Bottom = line.Bottom, Text = text } },
                    characterNames,
                    TranslationContentMode.Etc);
            }

            if (ChatTextAnalyzer.TryParseChatLine(text, out ChatTextAnalyzer.ChatLine parsedLine))
            {
                return MergeChatLabelBonus +
                       ChatTextAnalyzer.ScoreReadableTextCandidate(new[] { parsedLine.Message });
            }

            return ChatTextAnalyzer.ScoreReadableTextCandidate(new[] { text });
        }
    }

    /// <summary>
    /// 자동/수동 번역에서 실제 OCR 후보 실행 범위를 결정하는 모드입니다.
    /// </summary>
    public enum OcrProcessingMode
    {
        Fast,
        Auto,
        Accurate
    }

    /// <summary>
    /// 캡처 이미지를 OCR에 넣기 전 적용할 전처리 후보 종류입니다.
    /// </summary>
    public enum OcrPreprocessKind
    {
        Color,
        ColorThick,
        Adaptive
    }

    /// <summary>
    /// 한 번의 OCR 후보 평가 단계입니다.
    /// RecognizeAllLanguages가 false이면 게임 언어 우선 OCR만 실행하고, true이면 설치된 모든 OCR 언어를 실행합니다.
    /// </summary>
    public sealed class OcrEvaluationStep
    {
        private OcrEvaluationStep(bool recognizeAllLanguages, bool isFastPathStep, params OcrPreprocessKind[] preprocessKinds)
        {
            RecognizeAllLanguages = recognizeAllLanguages;
            IsFastPathStep = isFastPathStep;
            PreprocessKinds = preprocessKinds?.ToArray() ?? new OcrPreprocessKind[0];
        }

        public bool RecognizeAllLanguages { get; }
        public bool IsFastPathStep { get; }
        public IReadOnlyList<OcrPreprocessKind> PreprocessKinds { get; }

        public static OcrEvaluationStep FastPath(params OcrPreprocessKind[] preprocessKinds)
        {
            return new OcrEvaluationStep(false, true, preprocessKinds);
        }

        public static OcrEvaluationStep Fallback(bool recognizeAllLanguages, params OcrPreprocessKind[] preprocessKinds)
        {
            return new OcrEvaluationStep(recognizeAllLanguages, false, preprocessKinds);
        }
    }

    /// <summary>
    /// OCR 결과의 한 줄을 병합한 순수 모델입니다.
    /// Top/Bottom은 화면상 세로 위치이고, Text는 병합된 OCR 문자열입니다.
    /// </summary>
    public sealed class OcrLine
    {
        public double Top { get; set; }
        public double Bottom { get; set; }
        public string Text { get; set; }
    }

    /// <summary>
    /// 하나의 OCR 언어 결과를 병합 라인 기준으로 비교하기 위한 순수 모델입니다.
    /// ETC 모드에서 언어별 OCR 결과를 읽기 점수로 비교할 때 사용합니다.
    /// </summary>
    public sealed class OcrLanguageCandidate
    {
        public string LanguageCode { get; set; }
        public List<OcrLine> Lines { get; set; }
    }

    /// <summary>
    /// 언어별 OCR 결과 비교 후 선택된 최종 언어와 점수를 담는 모델입니다.
    /// </summary>
    public sealed class OcrLanguageSelection
    {
        public OcrLanguageSelection(string languageCode, List<OcrLine> lines, int score)
        {
            LanguageCode = languageCode ?? "";
            Lines = lines ?? new List<OcrLine>();
            Score = score;
        }

        public string LanguageCode { get; }
        public List<OcrLine> Lines { get; }
        public int Score { get; }
    }

    /// <summary>
    /// 하나의 전처리 후보에 대한 OCR 결과, 병합 라인, 품질 점수를 묶는 모델입니다.
    /// TResults는 실제 앱에서는 언어별 Windows OCR 결과 딕셔너리이고, 테스트에서는 가짜 결과를 넣을 수 있습니다.
    /// </summary>
    public sealed class OcrCandidate<TResults>
    {
        public string PreprocessName { get; set; }
        public string SelectedLanguageCode { get; set; }
        public TResults Results { get; set; }
        public List<OcrLine> Lines { get; set; }
        public int Score { get; set; }
    }
}
