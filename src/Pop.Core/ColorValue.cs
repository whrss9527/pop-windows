using System.Globalization;

namespace Pop.Core;

/// 一个颜色值
/// <param name="Red">0–255</param>
/// <param name="Green">0–255</param>
/// <param name="Blue">0–255</param>
/// <param name="Alpha">0–1</param>
public readonly record struct ColorValue(double Red, double Green, double Blue, double Alpha = 1)
{
    /// 支持 #RGB、#RGBA、#RRGGBB、#RRGGBBAA、rgb()/rgba()、hsl()/hsla()；不是颜色时返回 null。
    /// 三四位的写法里至少要有一个字母（#123 更可能是 issue 编号）。
    public static ColorValue? Parse(string text)
    {
        var trimmed = text.Trim().ToLowerInvariant();
        if (trimmed.Length > 64) return null;
        if (trimmed.StartsWith('#')) return ParseHex(trimmed[1..]);
        if (trimmed.StartsWith("rgb", StringComparison.Ordinal)) return ParseRgb(trimmed);
        if (trimmed.StartsWith("hsl", StringComparison.Ordinal)) return ParseHsl(trimmed);
        return null;
    }

    private static ColorValue? ParseHex(string digits)
    {
        if (digits.Length is not (3 or 4 or 6 or 8) || !digits.All(char.IsAsciiHexDigit)) return null;
        if (digits.Length <= 4 && !digits.Any(char.IsAsciiLetter)) return null;
        var expanded = digits.Length <= 4 ? string.Concat(digits.Select(c => new string(c, 2))) : digits;
        var value = ulong.Parse(expanded, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        if (expanded.Length == 6)
        {
            return new ColorValue((value >> 16) & 0xFF, (value >> 8) & 0xFF, value & 0xFF);
        }
        return new ColorValue((value >> 24) & 0xFF, (value >> 16) & 0xFF, (value >> 8) & 0xFF, (value & 0xFF) / 255.0);
    }

    /// rgb(1, 2, 3)、rgba(1,2,3,0.5)、rgb(1 2 3 / 50%) 括号里的各项
    private static string[]? Arguments(string text, string function)
    {
        var open = text.IndexOf('(');
        if (open < 0 || !text.EndsWith(')')) return null;
        var name = text[..open].Trim();
        if (name != function && name != function + "a") return null;
        var inner = text[(open + 1)..^1];
        var parts = inner.Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length is 3 or 4 ? parts : null;
    }

    private static double? Number(string text, double percentScale)
    {
        if (text.EndsWith('%'))
        {
            return TryDouble(text[..^1]) is { } percent ? percent / 100 * percentScale : null;
        }
        return TryDouble(text);
    }

    private static double? TryDouble(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value
            : null;

    /// 没写透明度时是 1；写了但不合法时返回 null
    private static double? AlphaArgument(string[] parts)
    {
        if (parts.Length != 4) return 1;
        return Number(parts[3], 1) is { } alpha && alpha is >= 0 and <= 1 ? alpha : null;
    }

    private static ColorValue? ParseRgb(string text)
    {
        if (Arguments(text, "rgb") is not { } parts) return null;
        var channels = new double[3];
        for (var i = 0; i < 3; i++)
        {
            if (Number(parts[i], 255) is not { } value || value is < 0 or > 255) return null;
            channels[i] = value;
        }
        if (AlphaArgument(parts) is not { } alpha) return null;
        return new ColorValue(channels[0], channels[1], channels[2], alpha);
    }

    private static ColorValue? ParseHsl(string text)
    {
        if (Arguments(text, "hsl") is not { } parts ||
            TryDouble(parts[0].Replace("deg", "", StringComparison.Ordinal)) is not { } hue ||
            !parts[1].EndsWith('%') || !parts[2].EndsWith('%') ||
            Number(parts[1], 1) is not { } saturation || Number(parts[2], 1) is not { } lightness ||
            saturation is < 0 or > 1 || lightness is < 0 or > 1 ||
            AlphaArgument(parts) is not { } alpha)
        {
            return null;
        }
        var (r, g, b) = HslToRgb(hue, saturation, lightness);
        return new ColorValue(r * 255, g * 255, b * 255, alpha);
    }

    /// 色相（度）、饱和度和亮度（0–1）换成 0–1 的红绿蓝
    public static (double Red, double Green, double Blue) HslToRgb(double hue, double saturation, double lightness)
    {
        var h = (hue % 360 + 360) % 360 / 360;
        if (saturation <= 0) return (lightness, lightness, lightness);
        var q = lightness < 0.5 ? lightness * (1 + saturation) : lightness + saturation - lightness * saturation;
        var p = 2 * lightness - q;
        double Channel(double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
        return (Channel(h + 1.0 / 3), Channel(h), Channel(h - 1.0 / 3));
    }

    /// 色相 0–360，饱和度、亮度 0–1
    public (double Hue, double Saturation, double Lightness) Hsl
    {
        get
        {
            var r = Red / 255;
            var g = Green / 255;
            var b = Blue / 255;
            var maxValue = Math.Max(r, Math.Max(g, b));
            var minValue = Math.Min(r, Math.Min(g, b));
            var lightness = (maxValue + minValue) / 2;
            if (maxValue <= minValue) return (0, 0, lightness);
            var delta = maxValue - minValue;
            var saturation = lightness > 0.5 ? delta / (2 - maxValue - minValue) : delta / (maxValue + minValue);
            double hue;
            if (maxValue == r)
            {
                hue = (g - b) / delta + (g < b ? 6 : 0);
            }
            else if (maxValue == g)
            {
                hue = (b - r) / delta + 2;
            }
            else
            {
                hue = (r - g) / delta + 4;
            }
            return (hue * 60, saturation, lightness);
        }
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int Byte(double value) => Round(Math.Clamp(value, 0, 255));

    /// 最多保留几位小数，去掉末尾的零
    private static string Fraction(double value, int digits = 3)
    {
        var rounded = Math.Round(value, digits, MidpointRounding.ToEven);
        if (rounded == 0) rounded = 0;
        return rounded.ToString("0." + new string('#', digits), CultureInfo.InvariantCulture);
    }

    /// #FF8800，半透明时带上透明度 #FF880080
    public string HexString
    {
        get
        {
            var hex = string.Create(CultureInfo.InvariantCulture, $"#{Byte(Red):X2}{Byte(Green):X2}{Byte(Blue):X2}");
            return Alpha < 1 ? hex + Byte(Alpha * 255).ToString("X2", CultureInfo.InvariantCulture) : hex;
        }
    }

    /// rgb(255, 136, 0) / rgba(0, 0, 255, 0.5)
    public string RgbString
    {
        get
        {
            var channels = string.Create(CultureInfo.InvariantCulture, $"{Byte(Red)}, {Byte(Green)}, {Byte(Blue)}");
            return Alpha < 1 ? $"rgba({channels}, {Fraction(Alpha, 2)})" : $"rgb({channels})";
        }
    }

    /// hsl(32, 100%, 50%) / hsla(…, 0.5)
    public string HslString
    {
        get
        {
            var (hue, saturation, lightness) = Hsl;
            var body = string.Create(CultureInfo.InvariantCulture,
                $"{Round(hue) % 360}, {Round(saturation * 100)}%, {Round(lightness * 100)}%");
            return Alpha < 1 ? $"hsla({body}, {Fraction(Alpha, 2)})" : $"hsl({body})";
        }
    }

    /// 结果卡片上的各行
    public IReadOnlyList<ResultLine> Rows() =>
    [
        new("HEX", HexString),
        new("RGB", RgbString),
        new("HSL", HslString),
    ];

    /// 由浅到深的色阶：和白色混合 80%、60%、40%、20%，原色，再和黑色混合 20%、40%、60%、80%
    public IReadOnlyList<ColorValue> Scale()
    {
        var white = new ColorValue(255, 255, 255);
        var black = new ColorValue(0, 0, 0);
        var self = this;
        return new[] { 0.8, 0.6, 0.4, 0.2 }.Select(amount => self.Mixed(white, amount))
            .Append(self)
            .Concat(new[] { 0.2, 0.4, 0.6, 0.8 }.Select(amount => self.Mixed(black, amount)))
            .ToList();
    }

    /// 按比例往另一个颜色靠（透明度不变）
    public ColorValue Mixed(ColorValue other, double amount)
    {
        static double Channel(double from, double to, double amount) =>
            Math.Round(from + (to - from) * amount, MidpointRounding.AwayFromZero);
        return new ColorValue(Channel(Red, other.Red, amount), Channel(Green, other.Green, amount),
            Channel(Blue, other.Blue, amount), Alpha);
    }
}
