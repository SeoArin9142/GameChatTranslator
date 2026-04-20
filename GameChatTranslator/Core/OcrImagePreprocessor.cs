using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;

namespace GameTranslator
{
    /// <summary>
    /// OCR 입력 Bitmap을 Color/ColorThick/Adaptive 후보 이미지로 전처리합니다.
    /// Windows OCR 엔진이나 UI에 의존하지 않고 System.Drawing 픽셀 처리만 담당합니다.
    /// </summary>
    public sealed class OcrImagePreprocessor
    {
        private readonly OcrMaskProcessor maskProcessor;

        public OcrImagePreprocessor()
            : this(new OcrMaskProcessor())
        {
        }

        public OcrImagePreprocessor(OcrMaskProcessor maskProcessor)
        {
            this.maskProcessor = maskProcessor ?? throw new ArgumentNullException(nameof(maskProcessor));
        }

        /// <summary>
        /// 요청된 전처리 후보 종류만 생성해 OCR 입력 Bitmap 목록을 만듭니다.
        /// <paramref name="source"/>는 확대된 원본 캡처 이미지,
        /// <paramref name="threshold"/>는 색상/밝기 판단 기준값,
        /// <paramref name="preprocessKinds"/>는 만들 전처리 후보 종류 목록입니다.
        /// 반환값의 Bitmap은 호출자가 Dispose해야 합니다.
        /// </summary>
        public List<PreprocessedOcrImage> CreatePreprocessedOcrImages(Bitmap source, int threshold, params OcrPreprocessKind[] preprocessKinds)
        {
            int width = source.Width;
            int height = source.Height;
            byte[] pixels = ReadBitmapPixels(source, out int stride);

            if (preprocessKinds == null || preprocessKinds.Length == 0)
            {
                preprocessKinds = new[] { OcrPreprocessKind.Color, OcrPreprocessKind.ColorThick, OcrPreprocessKind.Adaptive };
            }

            byte[] colorMask = null;
            var images = new List<PreprocessedOcrImage>();

            foreach (OcrPreprocessKind kind in preprocessKinds.Distinct())
            {
                if (kind == OcrPreprocessKind.Color)
                {
                    colorMask ??= maskProcessor.CreateColorMask(pixels, stride, width, height, threshold);
                    images.Add(new PreprocessedOcrImage { Name = "Color", Bitmap = CreateBitmapFromMask(colorMask, width, height) });
                }
                else if (kind == OcrPreprocessKind.ColorThick)
                {
                    colorMask ??= maskProcessor.CreateColorMask(pixels, stride, width, height, threshold);
                    byte[] colorThickMask = maskProcessor.DilateMask(colorMask, width, height);
                    images.Add(new PreprocessedOcrImage { Name = "ColorThick", Bitmap = CreateBitmapFromMask(colorThickMask, width, height) });
                }
                else if (kind == OcrPreprocessKind.Adaptive)
                {
                    byte[] adaptiveMask = maskProcessor.CreateAdaptiveThresholdMask(pixels, stride, width, height, threshold);
                    byte[] adaptiveCleanMask = maskProcessor.DilateMask(maskProcessor.RemoveIsolatedWhitePixels(adaptiveMask, width, height), width, height);
                    images.Add(new PreprocessedOcrImage { Name = "Adaptive", Bitmap = CreateBitmapFromMask(adaptiveCleanMask, width, height) });
                }
            }

            return images;
        }

        /// <summary>
        /// Bitmap의 픽셀 데이터를 32bpp ARGB byte 배열로 복사합니다.
        /// <paramref name="source"/>는 읽을 Bitmap,
        /// <paramref name="stride"/>는 반환되는 한 행의 byte 길이입니다.
        /// 반환 배열은 B,G,R,A 순서의 픽셀 데이터를 담습니다.
        /// </summary>
        public byte[] ReadBitmapPixels(Bitmap source, out int stride)
        {
            BitmapData data = source.LockBits(new Rectangle(0, 0, source.Width, source.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * source.Height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return pixels;
            }
            finally
            {
                source.UnlockBits(data);
            }
        }

        /// <summary>
        /// 0/255 마스크를 Windows OCR이 읽을 수 있는 32bpp ARGB Bitmap으로 변환합니다.
        /// <paramref name="mask"/>는 width*height 길이의 1채널 마스크,
        /// <paramref name="width"/>와 <paramref name="height"/>는 생성할 Bitmap 크기입니다.
        /// </summary>
        public Bitmap CreateBitmapFromMask(byte[] mask, int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);

            try
            {
                int stride = Math.Abs(data.Stride);
                byte[] pixels = new byte[stride * height];

                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    int maskOffset = y * width;

                    for (int x = 0; x < width; x++)
                    {
                        byte value = mask[maskOffset + x];
                        int i = rowOffset + x * 4;
                        pixels[i] = value;
                        pixels[i + 1] = value;
                        pixels[i + 2] = value;
                        pixels[i + 3] = 255;
                    }
                }

                Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return bitmap;
        }
    }

    /// <summary>
    /// 전처리된 OCR 입력 이미지를 이름과 함께 관리하는 disposable 모델입니다.
    /// Bitmap은 unmanaged 리소스를 포함하므로 사용 후 Dispose로 해제합니다.
    /// </summary>
    public sealed class PreprocessedOcrImage : IDisposable
    {
        public string Name;
        public Bitmap Bitmap;

        /// <summary>
        /// 전처리 이미지 Bitmap 리소스를 해제합니다.
        /// </summary>
        public void Dispose()
        {
            Bitmap?.Dispose();
        }
    }
}
