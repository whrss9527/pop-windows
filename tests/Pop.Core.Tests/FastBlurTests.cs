using Pop.Core;

namespace Pop.Core.Tests;

public class FastBlurTests
{
    private static byte[] Image(int width, int height, Func<int, int, byte> value)
    {
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * width + x) * 4;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = value(x, y);
            pixels[i + 3] = 255;
        }
        return pixels;
    }

    private static byte Blue(byte[] pixels, int width, int x, int y) => pixels[(y * width + x) * 4];

    [Fact]
    public void FlatColorStaysTheSame()
    {
        var pixels = Image(20, 12, (_, _) => 173);
        var expected = pixels.ToArray();
        FastBlur.Gaussian(pixels, 20, 12, 20 * 4, 3.5);
        Assert.Equal(expected, pixels);
    }

    [Fact]
    public void BrightDotSpreadsEvenlyAndKeepsItsBrightness()
    {
        const int size = 41;
        var pixels = Image(size, size, (x, y) => x == 20 && y == 20 ? (byte)255 : (byte)0);
        FastBlur.Gaussian(pixels, size, size, size * 4, 2);

        var center = Blue(pixels, size, 20, 20);
        Assert.InRange(center, 1, 254);
        for (var d = 1; d <= 6; d++)
        {
            Assert.Equal(Blue(pixels, size, 20 - d, 20), Blue(pixels, size, 20 + d, 20));
            Assert.Equal(Blue(pixels, size, 20, 20 - d), Blue(pixels, size, 20, 20 + d));
            Assert.True(Blue(pixels, size, 20 + d, 20) <= Blue(pixels, size, 20 + d - 1, 20));
        }
        // 总亮度差不多不变（每一步都四舍五入）
        var total = 0;
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++) total += Blue(pixels, size, x, y);
        Assert.InRange(total, 200, 310);
        // 透明度不变
        Assert.Equal(255, pixels[(20 * size + 20) * 4 + 3]);
    }

    [Fact]
    public void EdgesUseTheColorAtTheEdge()
    {
        // 左半白、右半黑：最左边一列还是白的，从左到右越来越暗
        const int width = 30;
        var pixels = Image(width, 4, (x, _) => x < width / 2 ? (byte)255 : (byte)0);
        FastBlur.Gaussian(pixels, width, 4, width * 4, 3.5);
        Assert.Equal(255, Blue(pixels, width, 0, 2));
        Assert.Equal(0, Blue(pixels, width, width - 1, 2));
        for (var x = 1; x < width; x++) Assert.True(Blue(pixels, width, x, 2) <= Blue(pixels, width, x - 1, 2));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3.5)]
    [InlineData(8)]
    public void BoxesAddUpToTheGaussianSpread(double sigma)
    {
        var sizes = FastBlur.BoxSizes(sigma, 3);
        Assert.All(sizes, s => Assert.Equal(1, s % 2));
        // 宽 w 的方框模糊方差是 (w² − 1) / 12，几次加起来接近 sigma²
        var variance = sizes.Sum(w => (w * w - 1) / 12.0);
        Assert.InRange(variance, sigma * sigma * 0.8, sigma * sigma * 1.2);
    }

    [Fact]
    public void TinyAndEmptyImagesAreFine()
    {
        var one = Image(1, 1, (_, _) => 90);
        FastBlur.Gaussian(one, 1, 1, 4, 3.5);
        Assert.Equal(90, one[0]);
        FastBlur.Gaussian([], 0, 0, 0, 3.5);
    }
}
