using System.Globalization;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 两种颜色（文字和背景）的对比度，按 WCAG 2.1 的算法。
public static class ColorContrast
{
    /// 找出文字里的颜色值：#RGB、#RRGGBB、#RRGGBBAA、rgb()/rgba()、hsl()/hsla()
    private static readonly Regex Token = new(
        @"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3,4})\b|(?:rgba?|hsla?)\([^)]*\)",
        RegexOptions.CultureInvariant);

    /// 正好两种颜色时返回它们（前一个当文字，后一个当背景），否则返回 null
    public static (ColorValue Foreground, ColorValue Background)? Pair(string text)
    {
        if (new StringInfo(text).LengthInTextElements > 200) return null;
        var colors = Token.Matches(text)
            .Select(match => ColorValue.Parse(match.Value))
            .OfType<ColorValue>()
            .ToList();
        return colors.Count == 2 ? (colors[0], colors[1]) : null;
    }

    /// 整段文字就是两个颜色（中间可以有「on」「和」、逗号、斜杠这样的几个字）
    public static bool IsColorPair(string text)
    {
        if (Pair(text) is null) return false;
        var rest = Token.Replace(text, "").Trim();
        return new StringInfo(rest).LengthInTextElements <= 12;
    }

    /// 对比度 1–21
    public static double Ratio(ColorValue first, ColorValue second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    /// 相对亮度
    public static double Luminance(ColorValue color)
    {
        static double Linear(double value)
        {
            var c = Math.Clamp(value / 255, 0, 1);
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(color.Red) + 0.7152 * Linear(color.Green) + 0.0722 * Linear(color.Blue);
    }

    /// 12.63 : 1（两位小数直接截断，不四舍五入，免得把不达标的说成达标）
    public static string Format(double ratio) =>
        (Math.Floor(ratio * 100) / 100).ToString("0.00", CultureInfo.InvariantCulture) + " : 1";

    /// 普通文字要 4.5（AA）/ 7（AAA），大号文字（18pt 以上或 14pt 粗体）要 3 / 4.5
    public static string Verdict(double ratio, bool large)
    {
        var aa = ratio >= (large ? 3 : 4.5);
        var aaa = ratio >= (large ? 4.5 : 7);
        return $"AA {(aa ? "通过" : "不通过")} · AAA {(aaa ? "通过" : "不通过")}";
    }

    /// 对比度卡片上的各行
    public static IReadOnlyList<ResultLine> Rows(ColorValue foreground, ColorValue background)
    {
        var value = Ratio(foreground, background);
        return
        [
            new("对比度", Format(value)),
            new("普通文字", Verdict(value, large: false)),
            new("大号文字", Verdict(value, large: true)),
        ];
    }

    /// 单独一种颜色：在白底、黑底上的对比度，放在颜色转换卡片下面
    public static string Summary(ColorValue color)
    {
        var white = new ColorValue(255, 255, 255);
        var black = new ColorValue(0, 0, 0);
        return $"对白色 {Format(Ratio(color, white))}，对黑色 {Format(Ratio(color, black))}";
    }
}
