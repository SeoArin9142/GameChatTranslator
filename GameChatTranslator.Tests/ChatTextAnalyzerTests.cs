using System.Collections.Generic;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class ChatTextAnalyzerTests
    {
        private static readonly HashSet<string> KnownCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "미셸",
            "오드리",
            "Maddelena"
        };

        [Theory]
        [InlineData("[미셸]: hello", "미셸", "hello")]
        [InlineData("(오드리)：你好", "오드리", "你好")]
        [InlineData("prefix [Maddelena]! attack now", "Maddelena", "attack now")]
        public void TryParseChatLine_ExtractsCharacterAndMessage(string rawText, string expectedName, string expectedMessage)
        {
            bool parsed = ChatTextAnalyzer.TryParseChatLine(rawText, out ChatTextAnalyzer.ChatLine line);

            Assert.True(parsed);
            Assert.Equal(expectedName, line.CharacterName);
            Assert.Equal(expectedMessage, line.Message);
            Assert.Equal($"[{expectedName}]: ", line.CharacterLabel);
        }

        [Theory]
        [InlineData("시스템: 전투가 시작됩니다")]
        [InlineData("[미셸] hello")]
        [InlineData("")]
        [InlineData(null)]
        public void TryParseChatLine_RejectsNonChatFormat(string rawText)
        {
            bool parsed = ChatTextAnalyzer.TryParseChatLine(rawText, out ChatTextAnalyzer.ChatLine line);

            Assert.False(parsed);
            Assert.Null(line);
        }

        [Fact]
        public void TryParseKnownCharacterChatLine_RequiresKnownCharacterAndMessage()
        {
            Assert.True(ChatTextAnalyzer.TryParseKnownCharacterChatLine("[미셸]: push", KnownCharacters, out ChatTextAnalyzer.ChatLine knownLine));
            Assert.Equal("미셸", knownLine.CharacterName);
            Assert.Equal("push", knownLine.Message);

            Assert.False(ChatTextAnalyzer.TryParseKnownCharacterChatLine("[Unknown]: push", KnownCharacters, out _));
            Assert.False(ChatTextAnalyzer.TryParseKnownCharacterChatLine("[미셸]:   ", KnownCharacters, out _));
        }

        [Theory]
        [InlineData("12345 !!!", false)]
        [InlineData("[미셸]: 你好", true)]
        [InlineData("[오드리]: hello", true)]
        [InlineData("[미셸]: атакуй", true)]
        public void ContainsReadableLetter_DetectsTranslatableLetters(string text, bool expected)
        {
            Assert.Equal(expected, ChatTextAnalyzer.ContainsReadableLetter(text));
        }

        [Fact]
        public void ScoreOcrCandidate_PrioritizesKnownCharacterChatLines()
        {
            int knownScore = ChatTextAnalyzer.ScoreOcrCandidate(new[] { "[미셸]: attack now" }, KnownCharacters);
            int unknownScore = ChatTextAnalyzer.ScoreOcrCandidate(new[] { "[Unknown]: attack now" }, KnownCharacters);
            int systemScore = ChatTextAnalyzer.ScoreOcrCandidate(new[] { "시스템 메시지 attack now" }, KnownCharacters);

            Assert.True(knownScore > 10000);
            Assert.True(knownScore > unknownScore);
            Assert.True(unknownScore > systemScore);
        }

        [Fact]
        public void ScoreOcrCandidate_PenalizesNoiseHeavyLines()
        {
            int cleanScore = ChatTextAnalyzer.ScoreOcrCandidate(new[] { "[오드리]: hello world" }, KnownCharacters);
            int noisyScore = ChatTextAnalyzer.ScoreOcrCandidate(new[] { "###@@@%%%" }, KnownCharacters);

            Assert.True(cleanScore > 0);
            Assert.True(noisyScore < 0);
            Assert.True(cleanScore > noisyScore);
        }
    }
}
