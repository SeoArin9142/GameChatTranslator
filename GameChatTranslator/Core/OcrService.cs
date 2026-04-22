using System;
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
        private const double FuzzyCharacterSimilarityThreshold = 0.6d;
        private const int ExactCharacterMatchScore = 2000;
        private const int FuzzyCharacterMatchBaseScore = 1000;

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
                OcrLine bestLine = SelectBestLineCandidate(candidateList, lineIndex, characterNames, contentMode);

                if (bestLine != null)
                {
                    mergedLines.Add(CloneLine(bestLine));
                }
            }

            return mergedLines;
        }

        /// <summary>
        /// 진단용 외부 OCR 비교에서 라벨과 본문을 분리해서 병합합니다.
        /// 파싱 가능한 채팅 줄이면 characters.txt 기준으로 가장 안정적인 라벨과 가장 읽을 만한 본문을 조합합니다.
        /// 조합에 실패하면 기존 줄 단위 선택 결과로 되돌립니다.
        /// </summary>
        public List<OcrLine> MergeBestChatLinesByComponents(
            IEnumerable<OcrLanguageCandidate> candidates,
            ISet<string> characterNames)
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
                OcrLine fallbackLine = SelectBestLineCandidate(candidateList, lineIndex, characterNames, TranslationContentMode.Strinova);
                if (TryBuildMergedChatLineByComponents(candidateList, lineIndex, characterNames, out OcrLine mergedLine))
                {
                    mergedLines.Add(mergedLine);
                    continue;
                }

                if (fallbackLine != null)
                {
                    mergedLines.Add(CloneLine(fallbackLine));
                }
            }

            return mergedLines;
        }

        /// <summary>
        /// 진단용 병합 라인에서 characters.txt 기반으로만 캐릭터 라벨을 보정합니다.
        /// 메인 번역 경로에는 영향을 주지 않으며, 퍼지 매칭에 성공한 경우에만 "[캐릭터명]: 본문" 형태로 정규화합니다.
        /// </summary>
        public List<OcrLine> NormalizeMergedLinesForSelection(
            IEnumerable<OcrLine> lines,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            var normalizedLines = new List<OcrLine>();

            foreach (OcrLine line in lines ?? Enumerable.Empty<OcrLine>())
            {
                if (line == null)
                {
                    continue;
                }

                string normalizedText = line.Text ?? "";
                if (contentMode == TranslationContentMode.Strinova &&
                    TryNormalizeMergedChatLine(normalizedText, characterNames, out string recoveredText))
                {
                    normalizedText = recoveredText;
                }

                normalizedLines.Add(new OcrLine
                {
                    Top = line.Top,
                    Bottom = line.Bottom,
                    Text = normalizedText
                });
            }

            return normalizedLines;
        }

        /// <summary>
        /// 줄 단위 병합으로 만들어진 OCR 결과를 진단 비교용 점수로 환산합니다.
        /// 메인 번역 경로의 후보 점수화와 분리해서, 외부 OCR이 깨진 라벨을 반환해도 본문 가독성을 더 공정하게 반영합니다.
        /// </summary>
        public int ScoreMergedLinesForSelection(
            IEnumerable<OcrLine> lines,
            ISet<string> characterNames,
            TranslationContentMode contentMode = TranslationContentMode.Strinova)
        {
            if (contentMode == TranslationContentMode.Etc)
            {
                return ScoreLines(lines, characterNames, TranslationContentMode.Etc);
            }

            List<OcrLine> normalizedLines = NormalizeMergedLinesForSelection(lines, characterNames, contentMode);
            int normalizedCandidateScore = ScoreLines(normalizedLines, characterNames, contentMode);
            int readableFallbackScore = normalizedLines
                .Sum(line => ScoreMergedLineForSelection(line, characterNames, contentMode));

            return Math.Max(normalizedCandidateScore, readableFallbackScore);
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

        private OcrLine SelectBestLineCandidate(
            IEnumerable<OcrLanguageCandidate> candidates,
            int lineIndex,
            ISet<string> characterNames,
            TranslationContentMode contentMode)
        {
            OcrLine bestLine = null;
            string bestLanguageCode = "";
            int bestScore = int.MinValue;

            foreach (OcrLanguageCandidate candidate in candidates ?? Enumerable.Empty<OcrLanguageCandidate>())
            {
                if (candidate == null || candidate.Lines == null || lineIndex >= candidate.Lines.Count)
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
                     string.Compare(candidate.LanguageCode, bestLanguageCode, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    bestLine = currentLine;
                    bestLanguageCode = candidate.LanguageCode ?? "";
                    bestScore = currentScore;
                }
            }

            return bestLine;
        }

        private bool TryBuildMergedChatLineByComponents(
            IEnumerable<OcrLanguageCandidate> candidates,
            int lineIndex,
            ISet<string> characterNames,
            out OcrLine mergedLine)
        {
            mergedLine = null;
            var parsedCandidates = new List<ParsedChatLineCandidate>();

            foreach (OcrLanguageCandidate candidate in candidates ?? Enumerable.Empty<OcrLanguageCandidate>())
            {
                if (candidate == null || candidate.Lines == null || lineIndex >= candidate.Lines.Count)
                {
                    continue;
                }

                OcrLine sourceLine = candidate.Lines[lineIndex];
                string text = sourceLine?.Text ?? "";
                if (!ChatTextAnalyzer.TryParseChatLine(text, out ChatTextAnalyzer.ChatLine parsedLine) ||
                    string.IsNullOrWhiteSpace(parsedLine.Message))
                {
                    continue;
                }

                string recoveredCharacterName;
                int labelScore = ScoreKnownCharacterMatch(parsedLine.CharacterName, characterNames, out recoveredCharacterName);
                int messageScore = ChatTextAnalyzer.ScoreReadableTextCandidate(new[] { parsedLine.Message });

                parsedCandidates.Add(new ParsedChatLineCandidate(
                    candidate.LanguageCode ?? "",
                    sourceLine,
                    parsedLine,
                    recoveredCharacterName,
                    labelScore,
                    messageScore));
            }

            if (parsedCandidates.Count == 0)
            {
                return false;
            }

            ParsedChatLineCandidate bestLabelCandidate = parsedCandidates
                .Where(candidate => candidate.LabelScore > 0 && !string.IsNullOrWhiteSpace(candidate.RecoveredCharacterName))
                .OrderByDescending(candidate => candidate.LabelScore)
                .ThenBy(candidate => candidate.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            ParsedChatLineCandidate bestMessageCandidate = parsedCandidates
                .Where(candidate => candidate.MessageScore > 0)
                .OrderByDescending(candidate => candidate.MessageScore)
                .ThenBy(candidate => candidate.LanguageCode, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (bestLabelCandidate == null || bestMessageCandidate == null)
            {
                return false;
            }

            mergedLine = new OcrLine
            {
                Top = bestMessageCandidate.SourceLine.Top,
                Bottom = bestMessageCandidate.SourceLine.Bottom,
                Text = $"[{bestLabelCandidate.RecoveredCharacterName}]: {bestMessageCandidate.ParsedLine.Message}"
            };
            return true;
        }

        private bool TryNormalizeMergedChatLine(string text, ISet<string> characterNames, out string normalizedText)
        {
            normalizedText = text ?? "";
            if (!ChatTextAnalyzer.TryParseChatLine(text, out ChatTextAnalyzer.ChatLine parsedLine) ||
                string.IsNullOrWhiteSpace(parsedLine.Message))
            {
                return false;
            }

            string recoveredCharacterName = RecoverClosestKnownCharacterName(parsedLine.CharacterName, characterNames);
            if (string.IsNullOrWhiteSpace(recoveredCharacterName) ||
                string.Equals(recoveredCharacterName, parsedLine.CharacterName?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            normalizedText = $"[{recoveredCharacterName}]: {parsedLine.Message}";
            return true;
        }

        private int ScoreKnownCharacterMatch(
            string candidateCharacterName,
            ISet<string> characterNames,
            out string recoveredCharacterName)
        {
            recoveredCharacterName = RecoverClosestKnownCharacterName(candidateCharacterName, characterNames);
            if (string.IsNullOrWhiteSpace(recoveredCharacterName))
            {
                return 0;
            }

            string candidateKey = BuildFuzzyCharacterKey(candidateCharacterName);
            string recoveredKey = BuildFuzzyCharacterKey(recoveredCharacterName);
            if (string.IsNullOrWhiteSpace(candidateKey) || string.IsNullOrWhiteSpace(recoveredKey))
            {
                return 0;
            }

            if (string.Equals(candidateKey, recoveredKey, StringComparison.OrdinalIgnoreCase))
            {
                return ExactCharacterMatchScore;
            }

            int distance = ComputeLevenshteinDistance(candidateKey, recoveredKey);
            int maxLength = Math.Max(candidateKey.Length, recoveredKey.Length);
            if (maxLength <= 0)
            {
                return 0;
            }

            double similarity = 1d - ((double)distance / maxLength);
            return FuzzyCharacterMatchBaseScore +
                (int)Math.Round(similarity * 200d) -
                (distance * 50);
        }

        private string RecoverClosestKnownCharacterName(string candidateCharacterName, ISet<string> characterNames)
        {
            string candidateKey = BuildFuzzyCharacterKey(candidateCharacterName);
            if (string.IsNullOrWhiteSpace(candidateKey) || candidateKey.Length < 2 || characterNames == null || characterNames.Count == 0)
            {
                return "";
            }

            string bestMatch = "";
            int bestDistance = int.MaxValue;
            double bestSimilarity = 0d;

            foreach (string rawKnownName in characterNames)
            {
                string knownName = rawKnownName?.Trim() ?? "";
                string knownKey = BuildFuzzyCharacterKey(knownName);
                if (string.IsNullOrWhiteSpace(knownKey))
                {
                    continue;
                }

                if (string.Equals(candidateKey, knownKey, StringComparison.OrdinalIgnoreCase))
                {
                    return knownName;
                }

                int distance = ComputeLevenshteinDistance(candidateKey, knownKey);
                int maxLength = Math.Max(candidateKey.Length, knownKey.Length);
                int maxAllowedDistance = maxLength <= 4 ? 1 : 2;
                if (distance > maxAllowedDistance)
                {
                    continue;
                }

                double similarity = 1d - ((double)distance / maxLength);
                bool isShortLabelMatch = maxLength <= 4 && distance <= 1;
                if (!isShortLabelMatch && similarity < FuzzyCharacterSimilarityThreshold)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(bestMatch) ||
                    distance < bestDistance ||
                    (distance == bestDistance && similarity > bestSimilarity) ||
                    (distance == bestDistance &&
                     Math.Abs(similarity - bestSimilarity) < double.Epsilon &&
                     string.Compare(knownName, bestMatch, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    bestMatch = knownName;
                    bestDistance = distance;
                    bestSimilarity = similarity;
                }
            }

            return bestMatch;
        }

        private static string BuildFuzzyCharacterKey(string value)
        {
            return new string((value ?? "")
                .Trim()
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray());
        }

        private static int ComputeLevenshteinDistance(string source, string target)
        {
            if (string.IsNullOrEmpty(source))
            {
                return target?.Length ?? 0;
            }

            if (string.IsNullOrEmpty(target))
            {
                return source.Length;
            }

            var distances = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
            {
                distances[i, 0] = i;
            }

            for (int j = 0; j <= target.Length; j++)
            {
                distances[0, j] = j;
            }

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int substitutionCost = source[i - 1] == target[j - 1] ? 0 : 1;
                    distances[i, j] = Math.Min(
                        Math.Min(
                            distances[i - 1, j] + 1,
                            distances[i, j - 1] + 1),
                        distances[i - 1, j - 1] + substitutionCost);
                }
            }

            return distances[source.Length, target.Length];
        }

        private static OcrLine CloneLine(OcrLine line)
        {
            return new OcrLine
            {
                Top = line.Top,
                Bottom = line.Bottom,
                Text = line.Text
            };
        }

        private sealed class ParsedChatLineCandidate
        {
            public ParsedChatLineCandidate(
                string languageCode,
                OcrLine sourceLine,
                ChatTextAnalyzer.ChatLine parsedLine,
                string recoveredCharacterName,
                int labelScore,
                int messageScore)
            {
                LanguageCode = languageCode ?? "";
                SourceLine = sourceLine;
                ParsedLine = parsedLine;
                RecoveredCharacterName = recoveredCharacterName ?? "";
                LabelScore = labelScore;
                MessageScore = messageScore;
            }

            public string LanguageCode { get; }
            public OcrLine SourceLine { get; }
            public ChatTextAnalyzer.ChatLine ParsedLine { get; }
            public string RecoveredCharacterName { get; }
            public int LabelScore { get; }
            public int MessageScore { get; }
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
