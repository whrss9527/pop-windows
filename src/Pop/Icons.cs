using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Wpf.Ui.Controls;
using Image = System.Windows.Controls.Image;

namespace Pop;

/// Fluent System Icons（随 WPF-UI 一起带的图标字体，Windows 10 上也能显示）
internal static class Icons
{
    /// 按名称找图标（不分大小写），找不到用 fallback。
    /// SymbolIcon 只显示得了 0xFFFF 以内的码位，更大的（比如 Calendar24、LinkMultiple24）会变成别的字，也当作找不到
    public static SymbolRegular Parse(string? name, SymbolRegular fallback = SymbolRegular.Apps24) =>
        name is { Length: > 0 } && Enum.TryParse<SymbolRegular>(name, ignoreCase: true, out var symbol) && Displayable(symbol) ? symbol : fallback;

    /// 常规和实心两种样式都在 0xFFFF 以内（选中的图标用实心）
    private static bool Displayable(SymbolRegular symbol) =>
        Enum.IsDefined(symbol) && (int)symbol <= 0xFFFF
        && (!Enum.TryParse<SymbolFilled>(symbol.ToString(), out var filled) || (int)filled <= 0xFFFF);

    public static SymbolIcon Make(string? name, double size, Brush foreground, bool filled = false) => new()
    {
        Symbol = Parse(name),
        FontSize = size,
        Foreground = foreground,
        Filled = filled,
        IsHitTestVisible = false,
    };

    public static SymbolIcon Make(SymbolRegular symbol, double size, Brush foreground, bool filled = false) => new()
    {
        Symbol = symbol,
        FontSize = size,
        Foreground = foreground,
        Filled = filled,
        IsHitTestVisible = false,
    };

    private static IReadOnlyList<BitmapFrame>? appIconFrames;

    /// Pop 的图标：从 Pop.ico 里挑一帧够大的，缩小显示更清楚
    public static Image AppIcon(double size)
    {
        appIconFrames ??= BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/Pop.ico"), BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
            .Frames.OrderBy(f => f.PixelWidth).ToList();
        var frame = appIconFrames.FirstOrDefault(f => f.PixelWidth >= size * 2) ?? appIconFrames[^1];
        var image = new Image { Source = frame, Width = size, Height = size, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }
}
