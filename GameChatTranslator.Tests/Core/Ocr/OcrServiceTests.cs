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
        public void SelectBestLanguageSelection_EtcMode_PicksMostReadableLanguage()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "猫は可愛い" }
                    }
                }
            };

            OcrLanguageSelection selected = _service.SelectBestLanguageSelection(
                candidates,
                KnownCharacters,
                TranslationContentMode.Etc);

            Assert.NotNull(selected);
            Assert.Equal("ja", selected.LanguageCode);
            Assert.Single(selected.Lines);
            Assert.Equal("猫は可愛い", selected.Lines[0].Text);
            Assert.True(selected.Score > 0);
        }

        [Fact]
        public void SelectBestLanguageSelection_ReturnsNullWhenNoCandidates()
        {
            OcrLanguageSelection selected = _service.SelectBestLanguageSelection(
                null,
                KnownCharacters,
                TranslationContentMode.Etc);

            Assert.Null(selected);
        }

        [Fact]
        public void SelectBestLanguageSelection_ReturnsNullWhenAllCandidatesScoreZero()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine> { new OcrLine { Text = "" } }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine> { new OcrLine { Text = "!!!" } }
                }
            };

            OcrLanguageSelection selected = _service.SelectBestLanguageSelection(
                candidates,
                KnownCharacters,
                TranslationContentMode.Etc);

            Assert.Null(selected);
        }

        [Fact]
        public void SelectBestLanguageSelection_TieUsesDeterministicLanguageOrder()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine> { new OcrLine { Text = "hello world" } }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine> { new OcrLine { Text = "hello world" } }
                }
            };

            OcrLanguageSelection selected = _service.SelectBestLanguageSelection(
                candidates,
                KnownCharacters,
                TranslationContentMode.Etc);

            Assert.NotNull(selected);
            Assert.Equal("ja", selected.LanguageCode);
        }

        [Fact]
        public void MergeBestLinesByIndex_StrinovaMode_PicksBestLinePerIndex()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "[미셸]: hello" },
                        new OcrLine { Text = "[미셸]: !!!" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "[미셸]: !!!" },
                        new OcrLine { Text = "[미셸]: world" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestLinesByIndex(
                candidates,
                KnownCharacters,
                TranslationContentMode.Strinova);

            Assert.Equal(2, merged.Count);
            Assert.Equal("[미셸]: hello", merged[0].Text);
            Assert.Equal("[미셸]: world", merged[1].Text);
        }

        [Fact]
        public void MergeBestLinesByIndex_TieUsesDeterministicLanguageOrder()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Top = 10, Bottom = 20, Text = "[미셸]: hello" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Top = 30, Bottom = 40, Text = "[미셸]: hello" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestLinesByIndex(
                candidates,
                KnownCharacters,
                TranslationContentMode.Strinova);

            Assert.Single(merged);
            Assert.Equal("[미셸]: hello", merged[0].Text);
            Assert.Equal(30, merged[0].Top);
            Assert.Equal(40, merged[0].Bottom);
        }

        [Fact]
        public void MergeBestLinesByIndex_EtcMode_PicksMostReadableLinePerIndex()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "!!!" },
                        new OcrLine { Text = "猫 は 可 愛 い" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "猫很可爱!" },
                        new OcrLine { Text = "..." }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestLinesByIndex(
                candidates,
                KnownCharacters,
                TranslationContentMode.Etc);

            Assert.Equal(2, merged.Count);
            Assert.Equal("猫很可爱!", merged[0].Text);
            Assert.Equal("猫 は 可 愛 い", merged[1].Text);
        }

        [Fact]
        public void MergeBestLinesByIndex_StrinovaMode_PrefersReadableMessageOverWeakLabeledLine()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "SeoArin [치요]: ROE! |" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "SeoArin [치요]: 猫は可愛い" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestLinesByIndex(
                candidates,
                KnownCharacters,
                TranslationContentMode.Strinova);

            Assert.Single(merged);
            Assert.Equal("SeoArin [치요]: 猫は可愛い", merged[0].Text);
        }

        [Fact]
        public void MergeBestLinesByIndex_StrinovaMode_PrefersLabeledLineWhenMessageQualityMatches()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "猫は可愛い" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "SeoArin [치요]: 猫は可愛い" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestLinesByIndex(
                candidates,
                KnownCharacters,
                TranslationContentMode.Strinova);

            Assert.Single(merged);
            Assert.Equal("SeoArin [치요]: 猫は可愛い", merged[0].Text);
        }

        [Fact]
        public void MergeBestChatLinesByComponents_StrinovaMode_CombinesBestLabelAndBestMessage()
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "치요"
            };
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Top = 10, Bottom = 20, Text = "SeoArin [치요]: HRT!" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Top = 30, Bottom = 40, Text = "SeoArin [AQ]: 猫は可愛い" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestChatLinesByComponents(candidates, characters);

            Assert.Single(merged);
            Assert.Equal(30, merged[0].Top);
            Assert.Equal(40, merged[0].Bottom);
            Assert.Equal("[치요]: 猫は可愛い", merged[0].Text);
        }

        [Fact]
        public void MergeBestChatLinesByComponents_StrinovaMode_FallsBackWhenNoRecoverableLabelExists()
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "치요"
            };
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "SeoArin [AQ]: hello world" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "SeoArin [BQ]: 猫は可愛い" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestChatLinesByComponents(candidates, characters);

            Assert.Single(merged);
            Assert.Equal("SeoArin [BQ]: 猫は可愛い", merged[0].Text);
        }

        [Fact]
        public void MergeBestChatLinesByComponents_StrinovaMode_FallsBackWhenParsingFails()
        {
            var candidates = new[]
            {
                new OcrLanguageCandidate
                {
                    LanguageCode = "ko",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "!!!" }
                    }
                },
                new OcrLanguageCandidate
                {
                    LanguageCode = "ja",
                    Lines = new List<OcrLine>
                    {
                        new OcrLine { Text = "猫は可愛い" }
                    }
                }
            };

            List<OcrLine> merged = _service.MergeBestChatLinesByComponents(candidates, KnownCharacters);

            Assert.Single(merged);
            Assert.Equal("猫は可愛い", merged[0].Text);
        }

        [Fact]
        public void ScoreMergedLinesForSelection_StrinovaMode_RewardsReadableMessageEvenWithBrokenLabel()
        {
            var lines = new[]
            {
                new OcrLine { Text = "SeoArin [AQ]: 猫很可爱!" },
                new OcrLine { Text = "SeoArin [AQ]: 猫は可愛い" }
            };

            int legacyScore = _service.ScoreLines(lines, KnownCharacters, TranslationContentMode.Strinova);
            int mergedScore = _service.ScoreMergedLinesForSelection(lines, KnownCharacters, TranslationContentMode.Strinova);

            Assert.True(mergedScore > 0);
            Assert.True(mergedScore > legacyScore);
        }

        [Fact]
        public void NormalizeMergedLinesForSelection_StrinovaMode_RecoversCloseKnownLabel()
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "치요"
            };
            var lines = new[]
            {
                new OcrLine { Top = 10, Bottom = 20, Text = "SeoArin [지요]: 猫は可愛い" }
            };

            List<OcrLine> normalized = _service.NormalizeMergedLinesForSelection(lines, characters, TranslationContentMode.Strinova);

            Assert.Single(normalized);
            Assert.Equal(10, normalized[0].Top);
            Assert.Equal(20, normalized[0].Bottom);
            Assert.Equal("[치요]: 猫は可愛い", normalized[0].Text);
        }

        [Fact]
        public void NormalizeMergedLinesForSelection_StrinovaMode_DoesNotGuessAsciiNoiseLabel()
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "치요"
            };
            var lines = new[]
            {
                new OcrLine { Text = "SeoArin [AQ]: 猫は可愛い" }
            };

            List<OcrLine> normalized = _service.NormalizeMergedLinesForSelection(lines, characters, TranslationContentMode.Strinova);

            Assert.Single(normalized);
            Assert.Equal("SeoArin [AQ]: 猫は可愛い", normalized[0].Text);
        }

        [Fact]
        public void ScoreMergedLinesForSelection_StrinovaMode_UsesKnownCharacterScaleWhenRecoveredLabelExists()
        {
            var characters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "치요"
            };
            var lines = new[]
            {
                new OcrLine { Text = "SeoArin [지요]: 猫は可愛い" },
                new OcrLine { Text = "SeoArin [치요]: KowKu munpie.!!!" }
            };

            int mergedScore = _service.ScoreMergedLinesForSelection(lines, characters, TranslationContentMode.Strinova);

            Assert.True(mergedScore > 20000);
        }

        [Fact]
        public void ScoreMergedLinesForSelection_EtcMode_UsesSameScoreAsScoreLines()
        {
            var lines = new[]
            {
                new OcrLine { Text = "猫很可爱!" },
                new OcrLine { Text = "hello world" }
            };

            int baseScore = _service.ScoreLines(lines, KnownCharacters, TranslationContentMode.Etc);
            int mergedScore = _service.ScoreMergedLinesForSelection(lines, KnownCharacters, TranslationContentMode.Etc);

            Assert.Equal(baseScore, mergedScore);
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
