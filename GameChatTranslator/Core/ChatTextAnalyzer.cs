using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GameTranslator
{
    /// <summary>
    /// OCR 결과 문자열에서 게임 채팅 라인을 판별하고 후보 신뢰도를 계산하는 순수 로직입니다.
    /// WPF/Windows OCR 타입에 의존하지 않으므로 단위 테스트에서 직접 검증할 수 있습니다.
    /// </summary>
    public static class ChatTextAnalyzer
    {
        private const string ReadableLetterPattern = @"[a-zA-Z가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ]";
        private const string AllowedNoisePattern = @"[^a-zA-Z0-9가-힣ぁ-んァ-ヶ一-龥а-яА-ЯёЁ\s\[\]\(\):;：!\.,\?\-]";
        private const string ChatLinePattern = @"^(.*[\[\(]([^\]\)]+)[\]\)]\s*[:;：!])\s*(.*)$";

        /// <summary>
        /// OCR 문자열에서 추출한 채팅 한 줄입니다.
        /// CharacterName은 대괄호/괄호를 제거한 캐릭터명이고, Message는 구분자 뒤 채팅 본문입니다.
        /// </summary>
        public sealed class ChatLine
        {
            public ChatLine(string characterName, string message)
            {
                CharacterName = characterName ?? "";
                Message = message ?? "";
            }

            public string CharacterName { get; }
            public string Message { get; }
            public string CharacterLabel => $"[{CharacterName}]: ";
        }

        /// <summary>
        /// 텍스트에 OCR 번역 대상으로 볼 만한 문자가 하나 이상 포함되어 있는지 확인합니다.
        /// 숫자/기호만 있는 라인은 번역 후보에서 제외하기 위한 1차 필터입니다.
        /// </summary>
        public static bool ContainsReadableLetter(string text)
        {
            return Regex.IsMatch(text ?? "", ReadableLetterPattern);
        }

        /// <summary>
        /// "[캐릭터명]: 내용" 또는 "(캐릭터명): 내용" 형태의 OCR 문자열을 캐릭터명과 본문으로 분리합니다.
        /// characters.txt 포함 여부는 검사하지 않고, 문자열 형식만 검사합니다.
        /// </summary>
        public static bool TryParseChatLine(string rawText, out ChatLine chatLine)
        {
            chatLine = null;

            string text = rawText?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) return false;

            Match match = Regex.Match(text, ChatLinePattern);
            if (!match.Success) return false;

            string characterName = match.Groups[2].Value.Trim();
            string message = match.Groups[3].Value.Trim();
            if (string.IsNullOrWhiteSpace(characterName)) return false;

            chatLine = new ChatLine(characterName, message);
            return true;
        }

        /// <summary>
        /// OCR 문자열이 채팅 형식이고 characters.txt에 등록된 캐릭터명인지 확인합니다.
        /// 본문이 비어 있으면 실제 번역 대상이 아니므로 false를 반환합니다.
        /// </summary>
        public static bool TryParseKnownCharacterChatLine(string rawText, ISet<string> characterNames, out ChatLine chatLine)
        {
            chatLine = null;
            if (!TryParseChatLine(rawText, out ChatLine parsedLine)) return false;
            if (characterNames == null || !characterNames.Contains(parsedLine.CharacterName)) return false;
            if (string.IsNullOrWhiteSpace(parsedLine.Message)) return false;

            chatLine = parsedLine;
            return true;
        }

        /// <summary>
        /// OCR 후보 라인 목록의 신뢰도를 점수화합니다.
        /// 채팅 포맷, characters.txt 캐릭터명 일치, 본문 길이, 외국어 문자 포함은 가산하고 노이즈는 감산합니다.
        /// </summary>
        public static int ScoreOcrCandidate(IEnumerable<string> lineTexts, ISet<string> characterNames)
        {
            int score = 0;

            foreach (string rawText in lineTexts ?? Enumerable.Empty<string>())
            {
                string text = rawText?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(text)) continue;

                int letterCount = Regex.Matches(text, ReadableLetterPattern).Count;
                int noiseCount = Regex.Matches(text, AllowedNoisePattern).Count;
                score += letterCount * 2;
                score -= noiseCount * 18;

                if (!TryParseChatLine(text, out ChatLine chatLine))
                {
                    score -= 80;
                    continue;
                }

                if (characterNames != null && characterNames.Contains(chatLine.CharacterName))
                {
                    score += 10000;
                    score += Math.Min(chatLine.Message.Length, 80) * 40;
                }
                else
                {
                    score -= 200;
                }

                if (Regex.IsMatch(chatLine.Message, @"[\u4e00-\u9fa5]")) score += 400;
                if (Regex.IsMatch(chatLine.Message, @"[a-zA-Z]{2,}")) score += 300;
                if (Regex.IsMatch(chatLine.Message, @"[ぁ-んァ-ヶ]")) score += 200;
                if (Regex.IsMatch(chatLine.Message, @"[а-яА-ЯёЁ]")) score += 100;
            }

            return score;
        }
    }
}
