using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pop;

/// 界面的配色、字体和尺寸，数值参照 Windows 11 的 Fluent 设计。
/// 跟随系统的深浅色和主题色；POP_APPEARANCE=dark / light 可以强制指定（截图时用）
internal sealed record Theme(
    bool Dark,
    Color Tint,
    double TintOpacity,
    Color Stroke,
    Color InnerStroke,
    Color Text,
    Color SecondaryText,
    Color TertiaryText,
    Color DisabledText,
    Color Accent,
    Color AccentText,
    Color AccentSoft,
    Color Hover,
    Color Selected,
    Color Divider,
    Color Field,
    Color FieldStroke,
    Color CenterFill,
    double ShadowOpacity)
{
    // ── 字体 ───────────────────────────────────────────
    // 西文和数字用 Windows 11 的 Segoe UI Variable（Windows 10 上是 Segoe UI），
    // 中文用随包带的 Noto Sans CJK SC 子集，子集里没有的生僻字再退到微软雅黑
    private static readonly Uri FontBase = new("pack://application:,,,/");

    public static readonly FontFamily TextFont = new(FontBase, "Segoe UI Variable Text, Segoe UI, ./Assets/Fonts/#Noto Sans CJK SC, Microsoft YaHei UI");
    public static readonly FontFamily DisplayFont = new(FontBase, "Segoe UI Variable Display, Segoe UI, ./Assets/Fonts/#Noto Sans CJK SC, Microsoft YaHei UI");
    public static readonly FontFamily MonoFont = new(FontBase, "Cascadia Mono, Consolas, ./Assets/Fonts/#Noto Sans CJK SC, Microsoft YaHei UI");

    // ── 字号（DIP），对应 Fluent 的 Caption / Body / Body Large / Subtitle / Title ──
    public const double Caption = 12;
    public const double Body = 14;
    public const double BodyLarge = 16;
    public const double Subtitle = 20;
    public const double TitleSize = 28;

    // ── 圆角 ───────────────────────────────────────────
    public const double OverlayRadius = 12;
    public const double ControlRadius = 6;

    /// 浮窗四周留给阴影的空白（DIP）
    public const double ShadowPad = 24;

    public static bool SystemIsDark()
    {
        var forced = Environment.GetEnvironmentVariable("POP_APPEARANCE");
        if (forced == "dark") return true;
        if (forced == "light") return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
    }

    public static Theme Current()
    {
        var dark = SystemIsDark();
        var (accentLight, accentDark) = SystemAccents();
        if (dark)
        {
            var accent = accentLight;
            return new Theme(true,
                Tint: Color.FromRgb(0x24, 0x24, 0x26), TintOpacity: 0.80,
                Stroke: Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF), InnerStroke: Color.FromArgb(0x0F, 0xFF, 0xFF, 0xFF),
                Text: Colors.White, SecondaryText: Color.FromArgb(0xC8, 0xFF, 0xFF, 0xFF),
                TertiaryText: Color.FromArgb(0x8B, 0xFF, 0xFF, 0xFF), DisabledText: Color.FromArgb(0x5D, 0xFF, 0xFF, 0xFF),
                Accent: accent, AccentText: Readable(accent), AccentSoft: Color.FromArgb(0x33, accent.R, accent.G, accent.B),
                Hover: Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), Selected: Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
                Divider: Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
                Field: Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), FieldStroke: Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF),
                CenterFill: Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF),
                ShadowOpacity: 0.45);
        }
        else
        {
            var accent = accentDark;
            return new Theme(false,
                Tint: Color.FromRgb(0xF6, 0xF6, 0xF7), TintOpacity: 0.78,
                Stroke: Color.FromArgb(0x1C, 0x00, 0x00, 0x00), InnerStroke: Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF),
                Text: Color.FromArgb(0xE4, 0x00, 0x00, 0x00), SecondaryText: Color.FromArgb(0x9E, 0x00, 0x00, 0x00),
                TertiaryText: Color.FromArgb(0x72, 0x00, 0x00, 0x00), DisabledText: Color.FromArgb(0x5C, 0x00, 0x00, 0x00),
                Accent: accent, AccentText: Readable(accent), AccentSoft: Color.FromArgb(0x24, accent.R, accent.G, accent.B),
                Hover: Color.FromArgb(0x0A, 0x00, 0x00, 0x00), Selected: Color.FromArgb(0x10, 0x00, 0x00, 0x00),
                Divider: Color.FromArgb(0x12, 0x00, 0x00, 0x00),
                Field: Color.FromArgb(0xB8, 0xFF, 0xFF, 0xFF), FieldStroke: Color.FromArgb(0x12, 0x00, 0x00, 0x00),
                CenterFill: Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
                ShadowOpacity: 0.20);
        }
    }

    /// 主题色上用黑字还是白字
    private static Color Readable(Color c) =>
        0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B > 150 ? Color.FromRgb(0, 0, 0) : Colors.White;

    /// 系统主题色的调色板：浅色模式用深一档（Dark1），深色模式用浅两档（Light2），和系统控件一致
    private static (Color Light, Color Dark) SystemAccents()
    {
        var fallback = (Color.FromRgb(0x4C, 0xC2, 0xFF), Color.FromRgb(0x00, 0x5F, 0xB8));
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
            {
                Color At(int i) => Color.FromRgb(palette[i * 4], palette[i * 4 + 1], palette[i * 4 + 2]);
                return (At(1), At(4));
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        return fallback;
    }

    public SolidColorBrush Brush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// 给窗口统一的文字渲染设置：平滑的 Ideal 模式，小字号也不发虚
    public static void ApplyTextOptions(DependencyObject element)
    {
        TextOptions.SetTextFormattingMode(element, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(element, TextRenderingMode.Auto);
        TextOptions.SetTextHintingMode(element, TextHintingMode.Auto);
    }
}
