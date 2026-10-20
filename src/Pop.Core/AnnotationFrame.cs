namespace Pop.Core;

public enum AnnotationBackground { None, Sky, Sunset, Mint, Grape, Graphite, Paper }

/// 美化只改变画布，笔画始终保留原图坐标。没有背景时导出原始尺寸。
public sealed record AnnotationFrame(AnnotationBackground Background = AnnotationBackground.None, int Padding = 32, int Radius = 12, bool Shadow = true)
{
    public int Inset => Background == AnnotationBackground.None ? 0 : Math.Clamp(Padding, 8, 128);
    public int CornerRadius(int width, int height) => Background == AnnotationBackground.None ? 0 : Math.Clamp(Radius, 0, Math.Min(width, height) / 2);
    public PixelBounds ImageBounds(int width, int height) => new(Inset, Inset, width, height);
    public (int Width, int Height) OutputSize(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        return (checked(width + Inset * 2), checked(height + Inset * 2));
    }
    public (string Start, string End) Colors => Background switch
    {
        AnnotationBackground.Sky => ("#599EFF", "#8C5CF5"),
        AnnotationBackground.Sunset => ("#FF9E59", "#F0548F"),
        AnnotationBackground.Mint => ("#5CDBA8", "#299EC7"),
        AnnotationBackground.Grape => ("#C770F2", "#5C45D6"),
        AnnotationBackground.Graphite => ("#545966", "#1F2129"),
        _ => ("#F5F5F7", "#DBDEE6"),
    };
}
