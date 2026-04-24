using System.Linq;
using System.Collections.Generic;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrTranslationHarnessServiceTests
    {
        private readonly OcrTranslationHarnessService _service = new OcrTranslationHarnessService(new TranslationPromptBuilder());
        private static readonly ISet<string> KnownCharacters = new HashSet<string> { "치요" };

        [Fact]
        public void BuildRequests_ParsesNicknamePrefixAndKeepsCharacterLabel()
        {
            var request = _service.BuildRequests(new[] { "SeoArin [치요]: 猫は可愛い" }, KnownCharacters).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[치요]: ", request.Prefix);
            Assert.Equal("猫は可愛い", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Strinova, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_FallsBackToRawWhenChatParseFailsButMeaningfulTextRemains()
        {
            var request = _service.BuildRequests(new[] { "猫 可 愛" }, KnownCharacters).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[RAW]: ", request.Prefix);
            Assert.Equal("猫可愛", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Etc, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_SkipsNoiseOnlyText()
        {
            var request = _service.BuildRequests(new[] { "###@@@%%%" }, KnownCharacters).Single();

            Assert.True(request.Skipped);
            Assert.Equal("글자 없음", request.SkipReason);
        }

        [Fact]
        public void BuildRequests_SkipsBrokenNoiseAfterEtcCleaning()
        {
            var request = _service.BuildRequests(new[] { "乞5U(私(z構ote.ntOØ?" }, KnownCharacters).Single();

            Assert.True(request.Skipped);
            Assert.Equal("파싱 실패 / 노이즈", request.SkipReason);
        }

        [Fact]
        public void BuildRequests_SkipsSystemUiLineWhenSystemLabelAndUiKeywordAreCombined()
        {
            var request = _service.BuildRequests(new[] { "[팀]! 빔을 클릭해 채널 변경" }, KnownCharacters).Single();

            Assert.True(request.Skipped);
            Assert.Equal("시스템/UI 문구", request.SkipReason);
        }

        [Fact]
        public void BuildRequests_KeepsExclamationSeparatedChatLineWhenItIsNotSystemUiText()
        {
            var request = _service.BuildRequests(new[] { "[치요]! 공격 시작" }, KnownCharacters).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[치요]: ", request.Prefix);
            Assert.Equal("공격 시작", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Strinova, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_FallsBackToRawWhenBangSeparatedLabelIsNotKnownCharacter()
        {
            var request = _service.BuildRequests(new[] { "[팀]! 공격 집결" }, KnownCharacters).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[RAW]: ", request.Prefix);
            Assert.Equal("팀 공격 집결", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Etc, request.ContentMode);
        }

        [Fact]
        public void BuildRequests_FallsBackToRawWhenUnknownBangLabelLooksLikeChat()
        {
            var request = _service.BuildRequests(new[] { "[el]! FAS Selon At BIZ @»" }, KnownCharacters).Single();

            Assert.False(request.Skipped);
            Assert.Equal("[RAW]: ", request.Prefix);
            Assert.Equal("el FAS Selon At BIZ", request.ContentToTranslate);
            Assert.Equal(TranslationContentMode.Etc, request.ContentMode);
        }

        [Fact]
        public void FilterMergedLinesForDiagnostics_RemovesUnknownBangLabelLine()
        {
            List<OcrLine> lines = _service.FilterMergedLinesForDiagnostics(
                new[]
                {
                    new OcrLine { Top = 0, Bottom = 10, Text = "[치요]: 猫は可愛い" },
                    new OcrLine { Top = 20, Bottom = 30, Text = "[el]! FAS Selon At BIZ @»" }
                },
                KnownCharacters);

            Assert.Single(lines);
            Assert.Equal("[치요]: 猫は可愛い", lines[0].Text);
        }

        [Fact]
        public void FilterMergedLinesForDiagnostics_KeepsKnownBangLabelLine()
        {
            List<OcrLine> lines = _service.FilterMergedLinesForDiagnostics(
                new[]
                {
                    new OcrLine { Top = 0, Bottom = 10, Text = "[치요]! 공격 시작" }
                },
                KnownCharacters);

            Assert.Single(lines);
            Assert.Equal("[치요]! 공격 시작", lines[0].Text);
        }
    }
}
