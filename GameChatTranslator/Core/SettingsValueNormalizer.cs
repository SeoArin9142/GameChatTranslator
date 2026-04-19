namespace GameTranslator
{
    /// <summary>
    /// config.ini와 프리셋에서 읽은 숫자 설정값을 런타임 허용 범위로 보정하는 순수 로직입니다.
    /// UI 입력, 프리셋, 런타임 읽기 경로가 같은 기준을 쓰도록 모읍니다.
    /// </summary>
    public static class SettingsValueNormalizer
    {
        public const int DefaultResultHistoryLimit = 5;
        public const int MinResultHistoryLimit = 1;
        public const int MaxResultHistoryLimit = 10;

        /// <summary>
        /// 번역 결과 누적 표시 줄 수를 1~10 범위로 보정합니다.
        /// 숫자가 아니거나 비어 있으면 기본값 5를 사용합니다.
        /// </summary>
        public static int NormalizeResultHistoryLimit(string rawValue)
        {
            int value = int.TryParse(rawValue, out int parsed) ? parsed : DefaultResultHistoryLimit;
            return NormalizeResultHistoryLimit(value);
        }

        /// <summary>
        /// 번역 결과 누적 표시 줄 수를 1~10 범위로 보정합니다.
        /// </summary>
        public static int NormalizeResultHistoryLimit(int value)
        {
            if (value < MinResultHistoryLimit) return MinResultHistoryLimit;
            if (value > MaxResultHistoryLimit) return MaxResultHistoryLimit;
            return value;
        }
    }
}
