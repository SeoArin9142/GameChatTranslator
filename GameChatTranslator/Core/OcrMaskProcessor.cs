using System;

namespace GameTranslator
{
    /// <summary>
    /// OCR 전처리에 필요한 1채널 마스크 생성과 보정만 담당하는 순수 픽셀 처리 클래스입니다.
    /// Bitmap이나 Windows OCR 타입을 참조하지 않아 모든 테스트 환경에서 검증할 수 있습니다.
    /// </summary>
    public sealed class OcrMaskProcessor
    {
        /// <summary>
        /// 흰색 채팅 글자와 노란색 캐릭터명을 보존하는 기본 색상 마스크를 생성합니다.
        /// <paramref name="pixels"/>는 B,G,R,A 순서의 32bpp 픽셀 배열,
        /// <paramref name="stride"/>는 한 행의 byte 길이,
        /// <paramref name="width"/>와 <paramref name="height"/>는 이미지 크기,
        /// <paramref name="threshold"/>는 밝기 판단 기준입니다.
        /// 반환값은 흰 픽셀 255, 배경 0으로 구성된 1채널 마스크입니다.
        /// </summary>
        public byte[] CreateColorMask(byte[] pixels, int stride, int width, int height, int threshold)
        {
            byte[] mask = new byte[width * height];

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * stride;
                int maskOffset = y * width;

                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x * 4;
                    byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    int diff = max - min;

                    // 흰 채팅 글자: 밝고 채도가 낮은 픽셀을 우선 보존합니다.
                    bool isWhite = max > threshold &&
                                   min > Math.Max(0, threshold - 18) &&
                                   (diff < 42 || (max > 0 && diff * 100 / max < 24));

                    // 노란 닉네임: RGB 조건에 채도 조건을 추가해 밝은 배경과 구분합니다.
                    bool isYellow = r > threshold &&
                                    g > Math.Max(0, threshold - 45) &&
                                    b < 125 &&
                                    r + g > b * 3 &&
                                    Math.Abs(r - g) < 95;

                    mask[maskOffset + x] = (isWhite || isYellow) ? (byte)255 : (byte)0;
                }
            }

            return mask;
        }

        /// <summary>
        /// 주변 밝기 평균과 현재 픽셀 밝기를 비교해 배경 변화에 강한 적응형 이진화 마스크를 만듭니다.
        /// <paramref name="pixels"/>는 B,G,R,A 순서의 32bpp 픽셀 배열,
        /// <paramref name="stride"/>는 한 행의 byte 길이,
        /// <paramref name="width"/>와 <paramref name="height"/>는 이미지 크기,
        /// <paramref name="threshold"/>는 최소 밝기 기준입니다.
        /// </summary>
        public byte[] CreateAdaptiveThresholdMask(byte[] pixels, int stride, int width, int height, int threshold)
        {
            byte[] gray = new byte[width * height];
            long[] integral = new long[(width + 1) * (height + 1)];

            for (int y = 0; y < height; y++)
            {
                long rowSum = 0;
                int rowOffset = y * stride;

                for (int x = 0; x < width; x++)
                {
                    int i = rowOffset + x * 4;
                    int value = (pixels[i + 2] * 299 + pixels[i + 1] * 587 + pixels[i] * 114) / 1000;
                    gray[y * width + x] = (byte)value;
                    rowSum += value;
                    integral[(y + 1) * (width + 1) + x + 1] = integral[y * (width + 1) + x + 1] + rowSum;
                }
            }

            byte[] mask = new byte[width * height];
            int radius = Math.Max(10, Math.Min(28, Math.Min(width, height) / 24));
            int offset = 16;
            int minAbsolute = Math.Max(70, threshold - 42);

            for (int y = 0; y < height; y++)
            {
                int y1 = Math.Max(0, y - radius);
                int y2 = Math.Min(height - 1, y + radius);

                for (int x = 0; x < width; x++)
                {
                    int x1 = Math.Max(0, x - radius);
                    int x2 = Math.Min(width - 1, x + radius);

                    int area = (x2 - x1 + 1) * (y2 - y1 + 1);
                    long sum = integral[(y2 + 1) * (width + 1) + x2 + 1]
                               - integral[y1 * (width + 1) + x2 + 1]
                               - integral[(y2 + 1) * (width + 1) + x1]
                               + integral[y1 * (width + 1) + x1];

                    int localAverage = (int)(sum / area);
                    int current = gray[y * width + x];

                    mask[y * width + x] = current >= minAbsolute && current > localAverage + offset ? (byte)255 : (byte)0;
                }
            }

            return mask;
        }

        /// <summary>
        /// 흰색 픽셀 주변 1픽셀 영역을 확장해 얇은 글자를 굵게 보정합니다.
        /// <paramref name="mask"/>는 0/255로 구성된 입력 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 마스크 크기입니다.
        /// 반환값은 글자가 한 픽셀 정도 두꺼워진 새 마스크입니다.
        /// </summary>
        public byte[] DilateMask(byte[] mask, int width, int height)
        {
            byte[] result = new byte[mask.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool hasWhite = false;

                    for (int dy = -1; dy <= 1 && !hasWhite; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx;
                            if (xx < 0 || xx >= width) continue;

                            if (mask[yy * width + xx] == 255)
                            {
                                hasWhite = true;
                                break;
                            }
                        }
                    }

                    result[y * width + x] = hasWhite ? (byte)255 : (byte)0;
                }
            }

            return result;
        }

        /// <summary>
        /// 주변 흰색 픽셀이 거의 없는 고립 노이즈 픽셀을 제거합니다.
        /// <paramref name="mask"/>는 0/255로 구성된 입력 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 마스크 크기입니다.
        /// </summary>
        public byte[] RemoveIsolatedWhitePixels(byte[] mask, int width, int height)
        {
            byte[] result = new byte[mask.Length];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    if (mask[index] == 0) continue;

                    int neighbors = 0;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int yy = y + dy;
                        if (yy < 0 || yy >= height) continue;

                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;

                            int xx = x + dx;
                            if (xx < 0 || xx >= width) continue;
                            if (mask[yy * width + xx] == 255) neighbors++;
                        }
                    }

                    result[index] = neighbors >= 2 ? (byte)255 : (byte)0;
                }
            }

            return result;
        }
    }
}
