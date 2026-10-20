namespace Pop.Core;

public readonly record struct PixelBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// 定格屏幕的 BGRA 像素。传入的缓冲区归调用者所有，使用期间不要修改。
public sealed class ScreenPixels
{
    private readonly ReadOnlyMemory<byte> pixels;
    private readonly int stride;
    public int Width { get; }
    public int Height { get; }

    public ScreenPixels(int width, int height, ReadOnlyMemory<byte> pixels, int stride = 0)
    {
        if (width <= 0 || height <= 0 || width > int.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(width));
        this.stride = stride == 0 ? width * 4 : stride;
        if (this.stride < width * 4 || (long)this.stride * height > pixels.Length) throw new ArgumentException("像素缓冲区尺寸不正确", nameof(pixels));
        Width = width; Height = height; this.pixels = pixels;
    }

    public ColorValue Sample(int x, int y)
    {
        if ((uint)x >= Width || (uint)y >= Height) throw new ArgumentOutOfRangeException(nameof(x));
        var offset = y * stride + x * 4;
        var data = pixels.Span;
        return new(data[offset + 2], data[offset + 1], data[offset], data[offset + 3] / 255d);
    }

    /// 从鼠标位置向四周寻找与起点颜色不同的像素；边界右侧、下侧使用开区间。
    public PixelBounds? FindEdges(int x, int y, int tolerance = 24, PixelBounds? clip = null)
    {
        if ((uint)x >= Width || (uint)y >= Height || tolerance is < 0 or > 765) return null;
        var limit = clip ?? new PixelBounds(0, 0, Width, Height);
        var minX = Math.Clamp(limit.Left, 0, Width - 1); var minY = Math.Clamp(limit.Top, 0, Height - 1);
        var maxX = (int)Math.Clamp((long)limit.Left + limit.Width, minX + 1, Width);
        var maxY = (int)Math.Clamp((long)limit.Top + limit.Height, minY + 1, Height);
        if (limit.Width <= 0 || limit.Height <= 0 || x < minX || x >= maxX || y < minY || y >= maxY) return null;
        var data = pixels.Span;
        var origin = y * stride + x * 4;
        var blue = data[origin]; var green = data[origin + 1]; var red = data[origin + 2];
        bool Same(int px, int py)
        {
            var span = pixels.Span;
            var i = py * stride + px * 4;
            return Math.Abs(span[i] - blue) + Math.Abs(span[i + 1] - green) + Math.Abs(span[i + 2] - red) <= tolerance;
        }
        var left = x; var right = x; var top = y; var bottom = y;
        while (left > minX && Same(left - 1, y)) left--;
        while (right < maxX - 1 && Same(right + 1, y)) right++;
        while (top > minY && Same(x, top - 1)) top--;
        while (bottom < maxY - 1 && Same(x, bottom + 1)) bottom++;
        return new(left, top, right - left + 1, bottom - top + 1);
    }
}
