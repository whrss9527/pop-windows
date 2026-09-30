using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 识别出来的数字
/// <param name="Integer">整数值；有小数部分（或超出 64 位整数范围）时为 null</param>
/// <param name="Value">数值</param>
/// <param name="Radix">原文的进制：10、16、8、2</param>
public sealed record ParsedNumber(long? Integer, decimal Value, int Radix);

/// 数字转换：进制、千分位、英文和中文读法、人民币大写
public static class NumberConverter
{
    private static readonly Regex DecimalPattern =
        new(@"^([0-9]{1,3}(,[0-9]{3})+|[0-9]+)(\.[0-9]+)?\z", RegexOptions.CultureInvariant);

    /// 十进制（可以带千分位逗号和小数）、0x 十六进制、0b 二进制、0o 八进制；不是数字时返回 null
    public static ParsedNumber? Parse(string text)
    {
        var body = text.Trim(' ', '\t', ' ', '　');
        if (body.Length == 0 || body.Length > 40) return null;
        var negative = false;
        if (body.StartsWith('-'))
        {
            negative = true;
            body = body[1..];
        }
        else if (body.StartsWith('+'))
        {
            body = body[1..];
        }

        var lower = body.ToLowerInvariant();
        foreach (var (prefix, radix) in new[] { ("0x", 16), ("0b", 2), ("0o", 8) })
        {
            if (!lower.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var digits = lower[2..].Replace("_", "", StringComparison.Ordinal);
            if (digits.Length == 0 || ParseRadix(digits, radix) is not { } magnitude) return null;
            var value = negative ? -magnitude : magnitude;
            return new ParsedNumber(value, value, radix);
        }

        if (!DecimalPattern.IsMatch(body)) return null;
        var plain = body.Replace(",", "", StringComparison.Ordinal);
        if (!decimal.TryParse(plain, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var decimalMagnitude))
        {
            return null;
        }
        long? integer = null;
        if (!plain.Contains('.') && long.TryParse(plain, NumberStyles.None, CultureInfo.InvariantCulture, out var whole))
        {
            integer = negative ? -whole : whole;
        }
        return new ParsedNumber(integer, negative ? -decimalMagnitude : decimalMagnitude, 10);
    }

    /// 按进制解析不带符号的数字，超出 64 位整数范围时返回 null
    private static long? ParseRadix(string digits, int radix)
    {
        long value = 0;
        foreach (var c in digits)
        {
            int digit = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'z' => c - 'a' + 10,
                _ => int.MaxValue,
            };
            if (digit >= radix) return null;
            try
            {
                value = checked(value * radix + digit);
            }
            catch (OverflowException)
            {
                return null;
            }
        }
        return value;
    }

    /// 结果卡片上的各行
    public static IReadOnlyList<ResultLine> Rows(ParsedNumber number)
    {
        var rows = new List<ResultLine>();
        if (number.Integer is { } value)
        {
            rows.Add(new ResultLine("十进制", value.ToString(CultureInfo.InvariantCulture)));
            rows.Add(new ResultLine("十六进制", Signed(value, 16, "0x")));
            rows.Add(new ResultLine("八进制", Signed(value, 8, "0o")));
            rows.Add(new ResultLine("二进制", Signed(value, 2, "0b")));
        }
        rows.Add(new ResultLine("千分位", Grouped(number.Value)));
        if (SpelledOutEnglish(number.Value) is { } english)
        {
            rows.Add(new ResultLine("英文读法", english));
        }
        if (SpelledOutChinese(number.Value) is { } chinese)
        {
            rows.Add(new ResultLine("中文读法", chinese));
        }
        if (RmbUppercase(number.Value) is { } uppercase)
        {
            rows.Add(new ResultLine("人民币大写", uppercase));
        }
        return rows;
    }

    /// 带符号和前缀的进制写法：-255 → -0xFF
    public static string Signed(long value, int radix, string prefix)
    {
        var magnitude = value < 0 ? (ulong)(-(value + 1)) + 1 : (ulong)value;
        const string symbols = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var digits = new StringBuilder();
        do
        {
            digits.Insert(0, symbols[(int)(magnitude % (ulong)radix)]);
            magnitude /= (ulong)radix;
        } while (magnitude > 0);
        return (value < 0 ? "-" : "") + prefix + digits;
    }

    /// 千分位写法，最多保留 10 位小数：1234567 → 1,234,567
    public static string Grouped(decimal value)
    {
        var rounded = Math.Round(value, 10, MidpointRounding.ToEven);
        return rounded.ToString("#,0.##########", CultureInfo.InvariantCulture);
    }

    // MARK: 读法

    /// 太大的数不读
    private static readonly decimal SpellLimit = 1_000_000_000_000_000m;

    /// 拆成整数部分和小数部分的各位数字（去掉末尾的零）
    private static (bool Negative, ulong Integer, string Fraction)? SplitForSpelling(decimal value)
    {
        if (Math.Abs(value) >= SpellLimit) return null;
        var text = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var dot = text.IndexOf('.');
        var integerText = dot < 0 ? text : text[..dot];
        var fraction = dot < 0 ? "" : text[(dot + 1)..].TrimEnd('0');
        var integer = ulong.Parse(integerText, NumberStyles.None, CultureInfo.InvariantCulture);
        var negative = value < 0 && (integer > 0 || fraction.Length > 0);
        return (negative, integer, fraction);
    }

    private static readonly string[] EnglishOnes =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen",
    ];

    private static readonly string[] EnglishTens =
        ["", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety"];

    /// 英文读法：255 → two hundred fifty-five，3.05 → three point zero five；太大的数返回 null
    public static string? SpelledOutEnglish(decimal value)
    {
        if (SplitForSpelling(value) is not var (negative, integer, fraction)) return null;
        var text = EnglishInteger(integer);
        if (fraction.Length > 0)
        {
            text += " point " + string.Join(" ", fraction.Select(c => EnglishOnes[c - '0']));
        }
        return negative ? "minus " + text : text;
    }

    private static string EnglishInteger(ulong value)
    {
        if (value < 20) return EnglishOnes[value];
        if (value < 100)
        {
            var tens = EnglishTens[value / 10];
            return value % 10 == 0 ? tens : tens + "-" + EnglishOnes[value % 10];
        }
        foreach (var (unit, name) in new[]
                 {
                     (1_000_000_000_000UL, "trillion"), (1_000_000_000UL, "billion"), (1_000_000UL, "million"),
                     (1_000UL, "thousand"), (100UL, "hundred"),
                 })
        {
            if (value < unit) continue;
            var head = EnglishInteger(value / unit) + " " + name;
            var rest = value % unit;
            return rest == 0 ? head : head + " " + EnglishInteger(rest);
        }
        return EnglishOnes[0];
    }

    private static readonly string[] ChineseDigits = ["零", "一", "二", "三", "四", "五", "六", "七", "八", "九"];

    /// 中文读法：255 → 二百五十五，10050 → 一万零五十，3.05 → 三点零五；太大的数返回 null
    public static string? SpelledOutChinese(decimal value)
    {
        if (SplitForSpelling(value) is not var (negative, integer, fraction)) return null;
        var text = ChineseInteger(integer, leading: true);
        if (fraction.Length > 0)
        {
            text += "点" + string.Concat(fraction.Select(c => ChineseDigits[c - '0']));
        }
        return negative ? "负" + text : text;
    }

    /// 最前面的十几读「十几」，跟在高位后面的读「一十几」（一百一十）；
    /// 中间隔了空位时补一个「零」（一千零五、十万零五百）
    private static string ChineseInteger(ulong value, bool leading)
    {
        if (value < 10) return ChineseDigits[value];
        if (value < 20 && leading) return "十" + (value == 10 ? "" : ChineseDigits[value - 10]);
        foreach (var (unit, name) in new[]
                 {
                     (1_000_000_000_000UL, "兆"), (100_000_000UL, "亿"), (10_000UL, "万"),
                     (1_000UL, "千"), (100UL, "百"), (10UL, "十"),
                 })
        {
            if (value < unit) continue;
            var head = ChineseInteger(value / unit, leading) + name;
            var rest = value % unit;
            if (rest == 0) return head;
            var gap = rest < unit / 10 ? "零" : "";
            return head + gap + ChineseInteger(rest, leading: false);
        }
        return ChineseDigits[0];
    }

    // MARK: 人民币大写

    /// 人民币金额大写，比如 1234.5 → 壹仟贰佰叁拾肆元伍角。整数部分最多 16 位，超出时返回 null
    public static string? RmbUppercase(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        var text = rounded.ToString("0.##", CultureInfo.InvariantCulture);
        var negative = false;
        if (text.StartsWith('-'))
        {
            negative = true;
            text = text[1..];
        }
        var parts = text.Split('.');
        var integerPart = parts[0];
        var fractionPart = parts.Length > 1 ? parts[1] : "";
        var fraction = (fractionPart + "00")[..2];
        if (integerPart.Length == 0 || integerPart.Length > 16 ||
            !integerPart.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit))
        {
            return null;
        }

        string[] names = ["零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖"];
        string[] positionUnits = ["", "拾", "佰", "仟"];
        string[] groupUnits = ["", "万", "亿", "万亿"];
        var digits = integerPart.Select(c => c - '0').ToArray();

        var integerText = new StringBuilder();
        if (digits.Any(d => d != 0))
        {
            var pendingZero = false;
            for (var offset = 0; offset < digits.Length; offset++)
            {
                var digit = digits[offset];
                var position = digits.Length - 1 - offset;
                var unitIndex = position % 4;
                var groupIndex = position / 4;
                if (digit == 0)
                {
                    if (integerText.Length > 0)
                    {
                        pendingZero = true;
                    }
                }
                else
                {
                    if (pendingZero)
                    {
                        integerText.Append('零');
                        pendingZero = false;
                    }
                    integerText.Append(names[digit]).Append(positionUnits[unitIndex]);
                }
                // 每四位一组，组内有非零数字才加「万」「亿」；组尾的零被单位吸收，不读「零」
                if (unitIndex == 0 && groupIndex > 0)
                {
                    var groupStart = Math.Max(0, offset - 3);
                    if (digits[groupStart..(offset + 1)].Any(d => d != 0))
                    {
                        integerText.Append(groupUnits[groupIndex]);
                        pendingZero = false;
                    }
                }
            }
            integerText.Append('元');
        }

        var jiao = fraction[0] - '0';
        var fen = fraction[1] - '0';
        var fractionText = new StringBuilder();
        if (jiao == 0 && fen == 0)
        {
            fractionText.Append(integerText.Length == 0 ? "" : "整");
        }
        else
        {
            if (jiao > 0)
            {
                fractionText.Append(names[jiao]).Append('角');
            }
            else if (integerText.Length > 0)
            {
                fractionText.Append('零');
            }
            if (fen > 0)
            {
                fractionText.Append(names[fen]).Append('分');
            }
        }
        if (integerText.Length == 0 && fractionText.Length == 0)
        {
            return "零元整";
        }
        return (negative ? "负" : "") + integerText + fractionText;
    }
}
