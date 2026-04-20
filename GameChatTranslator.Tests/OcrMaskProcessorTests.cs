using System.Linq;
using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class OcrMaskProcessorTests
    {
        private readonly OcrMaskProcessor _processor = new OcrMaskProcessor();

        [Fact]
        public void CreateColorMask_KeepsWhiteAndYellowPixels()
        {
            byte[] pixels =
            {
                255, 255, 255, 255,
                40, 230, 245, 255,
                30, 30, 30, 255
            };

            byte[] mask = _processor.CreateColorMask(pixels, stride: 12, width: 3, height: 1, threshold: 120);

            Assert.Equal(255, mask[0]);
            Assert.Equal(255, mask[1]);
            Assert.Equal(0, mask[2]);
        }

        [Fact]
        public void CreateAdaptiveThresholdMask_KeepsBrightPixelOnDarkBackground()
        {
            byte[] pixels = CreateSolidPixels(width: 25, height: 25, b: 20, g: 20, r: 20);
            SetPixel(pixels, stride: 100, x: 12, y: 12, b: 255, g: 255, r: 255);

            byte[] mask = _processor.CreateAdaptiveThresholdMask(pixels, stride: 100, width: 25, height: 25, threshold: 120);

            Assert.Equal(255, mask[12 * 25 + 12]);
        }

        [Fact]
        public void DilateMask_ExpandsSingleWhitePixelToNeighbors()
        {
            byte[] mask =
            {
                0, 0, 0,
                0, 255, 0,
                0, 0, 0
            };

            byte[] result = _processor.DilateMask(mask, 3, 3);

            Assert.All(result, value => Assert.Equal(255, value));
        }

        [Fact]
        public void RemoveIsolatedWhitePixels_RemovesSingleNoisePixel()
        {
            byte[] mask =
            {
                0, 0, 0,
                0, 255, 0,
                0, 0, 0
            };

            byte[] result = _processor.RemoveIsolatedWhitePixels(mask, 3, 3);

            Assert.All(result, value => Assert.Equal(0, value));
        }

        [Fact]
        public void RemoveIsolatedWhitePixels_KeepsConnectedPixels()
        {
            byte[] mask =
            {
                255, 255, 0,
                255, 0, 0,
                0, 0, 0
            };

            byte[] result = _processor.RemoveIsolatedWhitePixels(mask, 3, 3);

            Assert.Equal(255, result[0]);
            Assert.Equal(255, result[1]);
            Assert.Equal(255, result[3]);
        }

        [Fact]
        public void OcrMaskProcessor_DoesNotReferenceBitmapOrWinRtTypes()
        {
            string[] referencedNames = typeof(OcrMaskProcessor)
                .GetMethods()
                .Select(method => method.ReturnType.FullName)
                .Concat(typeof(OcrMaskProcessor).GetProperties().Select(property => property.PropertyType.FullName))
                .Concat(typeof(OcrMaskProcessor).GetFields().Select(field => field.FieldType.FullName))
                .Where(name => name != null)
                .ToArray();

            Assert.DoesNotContain(referencedNames, name => name.Contains("System.Drawing"));
            Assert.DoesNotContain(referencedNames, name => name.Contains("Windows.Media.Ocr"));
            Assert.DoesNotContain(referencedNames, name => name.Contains("Windows.Graphics.Imaging"));
        }

        private static byte[] CreateSolidPixels(int width, int height, byte b, byte g, byte r)
        {
            int stride = width * 4;
            byte[] pixels = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    SetPixel(pixels, stride, x, y, b, g, r);
                }
            }

            return pixels;
        }

        private static void SetPixel(byte[] pixels, int stride, int x, int y, byte b, byte g, byte r)
        {
            int index = y * stride + x * 4;
            pixels[index] = b;
            pixels[index + 1] = g;
            pixels[index + 2] = r;
            pixels[index + 3] = 255;
        }
    }
}
