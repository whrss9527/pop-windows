namespace Pop.Core;

/// 近似的高斯模糊：连做三次方框模糊。毛玻璃的底图缩小以后用它模糊，只要几毫秒
public static class FastBlur
{
    /// 就地模糊一张 BGRA（每个像素 4 个字节）的图。sigma 是高斯模糊的标准差（像素）；图外面当作和边上一样的颜色
    public static void Gaussian(byte[] pixels, int width, int height, int stride, double sigma)
    {
        if (width < 1 || height < 1 || sigma <= 0) return;
        var buffer = new byte[pixels.Length];
        foreach (var size in BoxSizes(sigma, 3))
        {
            var radius = (size - 1) / 2;
            if (radius < 1) continue;
            Horizontal(pixels, buffer, width, height, stride, radius);
            Vertical(buffer, pixels, width, height, stride, radius);
        }
    }

    /// 几次方框模糊合起来接近某个 sigma 的高斯模糊时，每次方框的宽度（奇数）
    public static int[] BoxSizes(double sigma, int passes)
    {
        var ideal = Math.Sqrt(12 * sigma * sigma / passes + 1);
        var lower = (int)Math.Floor(ideal);
        if (lower % 2 == 0) lower--;
        var upper = lower + 2;
        var lowerCount = (int)Math.Round((12 * sigma * sigma - passes * lower * lower - 4.0 * passes * lower - 3 * passes) / (-4.0 * lower - 4));
        var sizes = new int[passes];
        for (var i = 0; i < passes; i++) sizes[i] = i < lowerCount ? lower : upper;
        return sizes;
    }

    private static void Horizontal(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        var span = 2 * radius + 1;
        for (var y = 0; y < height; y++)
        {
            var row = y * stride;
            for (var c = 0; c < 4; c++)
            {
                // 以 x 为中心、宽 span 的一段的和，超出图的位置用边上的像素
                var sum = 0;
                for (var i = -radius; i <= radius; i++) sum += source[row + Math.Clamp(i, 0, width - 1) * 4 + c];
                for (var x = 0; x < width; x++)
                {
                    target[row + x * 4 + c] = (byte)((sum + span / 2) / span);
                    sum += source[row + Math.Min(x + radius + 1, width - 1) * 4 + c] - source[row + Math.Max(x - radius, 0) * 4 + c];
                }
            }
        }
    }

    private static void Vertical(byte[] source, byte[] target, int width, int height, int stride, int radius)
    {
        var span = 2 * radius + 1;
        for (var x = 0; x < width; x++)
        {
            var column = x * 4;
            for (var c = 0; c < 4; c++)
            {
                var sum = 0;
                for (var i = -radius; i <= radius; i++) sum += source[Math.Clamp(i, 0, height - 1) * stride + column + c];
                for (var y = 0; y < height; y++)
                {
                    target[y * stride + column + c] = (byte)((sum + span / 2) / span);
                    sum += source[Math.Min(y + radius + 1, height - 1) * stride + column + c] - source[Math.Max(y - radius, 0) * stride + column + c];
                }
            }
        }
    }
}
