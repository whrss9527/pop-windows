using System.Windows.Media;
using Microsoft.Win32;

namespace Pop;

/// 浮窗的配色：跟随系统的深浅色和主题色。POP_APPEARANCE=dark / light 可以强制指定（截图时用）
internal sealed record Theme(
    bool Dark,
    Color Surface,
    Color SurfaceBorder,
    Color Segment,
    Color Text,
    Color SecondaryText,
    Color DisabledText,
    Color Accent,
    Color AccentText)
{
    public static Theme Current()
    {
        var dark = Environment.GetEnvironmentVariable("POP_APPEARANCE") switch
        {
            "dark" => true,
            "light" => false,
            _ => SystemIsDark(),
        };
        var accent = SystemAccent() ?? Color.FromRgb(0x3B, 0x7B, 0xF6);
        return dark
            ? new Theme(true,
                Color.FromArgb(0xE6, 0x26, 0x28, 0x2E), Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF),
                Color.FromRgb(0xF2, 0xF2, 0xF4), Color.FromRgb(0xA8, 0xAB, 0xB3), Color.FromArgb(0x60, 0xF2, 0xF2, 0xF4),
                accent, Colors.White)
            : new Theme(false,
                Color.FromArgb(0xEB, 0xF7, 0xF7, 0xF9), Color.FromArgb(0x30, 0x00, 0x00, 0x00), Color.FromArgb(0x0C, 0x00, 0x00, 0x00),
                Color.FromRgb(0x1C, 0x1C, 0x1E), Color.FromRgb(0x6B, 0x6E, 0x76), Color.FromArgb(0x55, 0x1C, 0x1C, 0x1E),
                accent, Colors.White);
    }

    private static bool SystemIsDark()
    {
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

    /// 系统主题色（注册表里是 ABGR）
    private static Color? SystemAccent()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr)
            {
                var c = Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
                // 太浅的主题色上白字看不清，换成默认的蓝色
                var luminance = 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
                return luminance > 190 ? null : c;
            }
        }
        catch (System.Security.SecurityException)
        {
        }
        return null;
    }

    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily TextFont = new("Microsoft YaHei UI, Segoe UI");
}
