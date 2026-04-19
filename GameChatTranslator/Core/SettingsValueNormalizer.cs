namespace GameTranslator
{
    /// <summary>
    /// config.ini와 프리셋에서 읽은 숫자 설정값을 런타임 허용 범위로 보정하는 순수 로직입니다.
    /// UI 입력, 프리셋, 런타임 읽기 경로가 같은 기준을 쓰도록 모읍니다.
    /// </summary>
    public static class SettingsValueNormalizer
    {
        public const int DefaultThreshold = 120;
        public const int MinThreshold = 1;
        public const int MaxThreshold = 255;

        public const int DefaultAutoTranslateInterval = 5;
        public const int MinAutoTranslateInterval = 1;
        public const int MaxAutoTranslateInterval = 60;

        public const int DefaultScaleFactor = 3;
        public const int MinScaleFactor = 1;
        public const int MaxScaleFactor = 4;

        public const int DefaultResultHistoryLimit = 5;
        public const int MinResultHistoryLimit = 1;
        public const int MaxResultHistoryLimit = 10;

        /// <summary>
        /// OCR 이진화 기준값을 1~255 범위로 보정합니다.
        /// 숫자가 아니거나 비어 있으면 기본값 120을 사용합니다.
        /// </summary>
        public static int NormalizeThreshold(string rawValue)
        {
            return NormalizeInteger(rawValue, DefaultThreshold, MinThreshold, MaxThreshold);
        }

        /// <summary>
        /// OCR 이진화 기준값을 1~255 범위로 보정합니다.
        /// </summary>
        public static int NormalizeThreshold(int value)
        {
            return Clamp(value, MinThreshold, MaxThreshold);
        }

        /// <summary>
        /// 자동 번역 주기를 1~60초 범위로 보정합니다.
        /// 숫자가 아니거나 비어 있으면 기본값 5초를 사용합니다.
        /// </summary>
        public static int NormalizeAutoTranslateInterval(string rawValue)
        {
            return NormalizeInteger(rawValue, DefaultAutoTranslateInterval, MinAutoTranslateInterval, MaxAutoTranslateInterval);
        }

        /// <summary>
        /// 자동 번역 주기를 1~60초 범위로 보정합니다.
        /// </summary>
        public static int NormalizeAutoTranslateInterval(int value)
        {
            return Clamp(value, MinAutoTranslateInterval, MaxAutoTranslateInterval);
        }

        /// <summary>
        /// OCR 입력 이미지 확대 배율을 1~4 범위로 보정합니다.
        /// 숫자가 아니거나 비어 있으면 기본값 3배를 사용합니다.
        /// </summary>
        public static int NormalizeScaleFactor(string rawValue)
        {
            return NormalizeInteger(rawValue, DefaultScaleFactor, MinScaleFactor, MaxScaleFactor);
        }

        /// <summary>
        /// OCR 입력 이미지 확대 배율을 1~4 범위로 보정합니다.
        /// </summary>
        public static int NormalizeScaleFactor(int value)
        {
            return Clamp(value, MinScaleFactor, MaxScaleFactor);
        }

        /// <summary>
        /// 번역 결과 누적 표시 줄 수를 1~10 범위로 보정합니다.
        /// 숫자가 아니거나 비어 있으면 기본값 5를 사용합니다.
        /// </summary>
        public static int NormalizeResultHistoryLimit(string rawValue)
        {
            return NormalizeInteger(rawValue, DefaultResultHistoryLimit, MinResultHistoryLimit, MaxResultHistoryLimit);
        }

        /// <summary>
        /// 번역 결과 누적 표시 줄 수를 1~10 범위로 보정합니다.
        /// </summary>
        public static int NormalizeResultHistoryLimit(int value)
        {
            return Clamp(value, MinResultHistoryLimit, MaxResultHistoryLimit);
        }

        /// <summary>
        /// 문자열 숫자 설정값을 파싱하고 실패 시 기본값을 사용한 뒤 허용 범위로 보정합니다.
        /// </summary>
        private static int NormalizeInteger(string rawValue, int defaultValue, int minValue, int maxValue)
        {
            int value = int.TryParse(rawValue, out int parsed) ? parsed : defaultValue;
            return Clamp(value, minValue, maxValue);
        }

        /// <summary>
        /// 정수 값을 지정된 최소/최대 범위 안으로 제한합니다.
        /// </summary>
        private static int Clamp(int value, int minValue, int maxValue)
        {
            if (value < minValue) return minValue;
            if (value > maxValue) return maxValue;
            return value;
        }
    }
}
