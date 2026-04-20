using System.Collections.Generic;
using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrServiceTests
    {
        private readonly OcrService _service = new OcrService();
        private static readonly HashSet<string> KnownCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "미셸",
            "오드리"
        };

        [Fact]
        public void CreateEvaluationPlan_Fast_UsesOnlyColorGameLanguageStep()
        {
            IReadOnlyList<OcrEvaluationStep> plan = _service.CreateEvaluationPlan(OcrProcessingMode.Fast);

            Assert.Single(plan);
            Assert.True(plan[0].IsFastPathStep);
            Assert.False(plan[0].RecognizeAllLanguages);
            Assert.Equal(new[] { OcrPreprocessKind.Color }, plan[0].PreprocessKinds);
        }

        [Fact]
        public void CreateEvaluationPlan_Auto_UsesFastPathThenFallback()
        {
            IReadOnlyList<OcrEvaluationStep> plan = _service.CreateEvaluationPlan(OcrProcessingMode.Auto);

            Assert.Equal(2, plan.Count);
            Assert.True(plan[0].IsFastPathStep);
            Assert.False(plan[0].RecognizeAllLanguages);
            Assert.Equal(new[] { OcrPreprocessKind.Color }, plan[0].PreprocessKinds);

            Assert.False(plan[1].IsFastPathStep);
            Assert.True(plan[1].RecognizeAllLanguages);
            Assert.Equal(new[] { OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive }, plan[1].PreprocessKinds);
        }

        [Fact]
        public void CreateEvaluationPlan_Accurate_UsesAllCandidatesAndLanguages()
        {
            IReadOnlyList<OcrEvaluationStep> plan = _service.CreateEvaluationPlan(OcrProcessingMode.Accurate);

            Assert.Single(plan);
            Assert.False(plan[0].IsFastPathStep);
            Assert.True(plan[0].RecognizeAllLanguages);
            Assert.Equal(new[] { OcrPreprocessKind.Color, OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive }, plan[0].PreprocessKinds);
        }

        [Fact]
        public void IsFastPathSuccess_RequiresPositiveScoreKnownCharacterAndMessage()
        {
            var validCandidate = new OcrCandidate<string>
            {
                Score = 10,
                Lines = new List<OcrLine> { new OcrLine { Text = "[미셸]: attack" } }
            };

            var unknownCharacterCandidate = new OcrCandidate<string>
            {
                Score = 10,
                Lines = new List<OcrLine> { new OcrLine { Text = "[Unknown]: attack" } }
            };

            var zeroScoreCandidate = new OcrCandidate<string>
            {
                Score = 0,
                Lines = new List<OcrLine> { new OcrLine { Text = "[미셸]: attack" } }
            };

            var emptyMessageCandidate = new OcrCandidate<string>
            {
                Score = 10,
                Lines = new List<OcrLine> { new OcrLine { Text = "[미셸]:   " } }
            };

            Assert.True(_service.IsFastPathSuccess(validCandidate, KnownCharacters));
            Assert.False(_service.IsFastPathSuccess(unknownCharacterCandidate, KnownCharacters));
            Assert.False(_service.IsFastPathSuccess(zeroScoreCandidate, KnownCharacters));
            Assert.False(_service.IsFastPathSuccess(emptyMessageCandidate, KnownCharacters));
            Assert.False(_service.IsFastPathSuccess<string>(null, KnownCharacters));
        }

        [Fact]
        public void IsFastPathSuccess_EtcMode_AllowsReadableNonChatText()
        {
            var nonChatCandidate = new OcrCandidate<string>
            {
                Score = 10,
                Lines = new List<OcrLine> { new OcrLine { Text = "hello world" } }
            };

            Assert.True(_service.IsFastPathSuccess(nonChatCandidate, KnownCharacters, TranslationContentMode.Etc));
            Assert.False(_service.IsFastPathSuccess(nonChatCandidate, KnownCharacters, TranslationContentMode.Strinova));
        }

        [Fact]
        public void SelectHigherScore_KeepsHigherScoredCandidate()
        {
            var low = new OcrCandidate<string> { PreprocessName = "Color", Score = 10 };
            var high = new OcrCandidate<string> { PreprocessName = "Adaptive", Score = 20 };

            Assert.Same(low, _service.SelectHigherScore<string>(null, low));
            Assert.Same(high, _service.SelectHigherScore(low, high));
            Assert.Same(high, _service.SelectHigherScore(high, low));
            Assert.Same(high, _service.SelectHigherScore(high, null));
        }

        [Fact]
        public void ScoreLines_DelegatesKnownCharacterScoring()
        {
            var lines = new[]
            {
                new OcrLine { Text = "[미셸]: hello" },
                new OcrLine { Text = "시스템 메시지" }
            };

            int score = _service.ScoreLines(lines, KnownCharacters);

            Assert.True(score > 10000);
        }

        [Fact]
        public void ScoreLines_EtcMode_UsesReadableTextScoring()
        {
            var lines = new[]
            {
                new OcrLine { Text = "일반 OCR 텍스트" },
                new OcrLine { Text = "hello world" }
            };

            int strinovaScore = _service.ScoreLines(lines, KnownCharacters, TranslationContentMode.Strinova);
            int etcScore = _service.ScoreLines(lines, KnownCharacters, TranslationContentMode.Etc);

            Assert.True(etcScore > 0);
            Assert.True(etcScore > strinovaScore);
        }

        [Fact]
        public void OcrService_DoesNotReferenceWinRtOrBitmapTypes()
        {
            string assemblyQualifiedNames = string.Join(
                "\n",
                typeof(OcrService).Assembly
                    .GetTypes()
                    .Where(type => type.Namespace == "GameTranslator" && type.Name.StartsWith("Ocr"))
                    .Select(type => type.AssemblyQualifiedName));

            Assert.DoesNotContain("Windows.Media.Ocr", assemblyQualifiedNames);
            Assert.DoesNotContain("SoftwareBitmap", assemblyQualifiedNames);
            Assert.DoesNotContain("System.Drawing.Bitmap", assemblyQualifiedNames);
        }
    }
}
