using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 编码转换：Base64、URL、Unicode 转义、HTML 实体。纯逻辑，方便测试。
public static class TextCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// 能解码的放在前面（选中一段编码过的文字时，多半是想看原文）。
    public static IReadOnlyList<ResultLine> Conversions(string text)
    {
        var rows = new List<ResultLine>();
        if (Base64Decode(text) is { } base64 && base64 != text) rows.Add(new("Base64 解码", base64));
        if (UrlDecode(text) is { } url && url != text) rows.Add(new("URL 解码", url));
        if (UnicodeUnescape(text) is { } unicodeText && unicodeText != text) rows.Add(new("Unicode 还原", unicodeText));
        if (HtmlUnescape(text) is { } htmlText && htmlText != text) rows.Add(new("HTML 还原", htmlText));
        rows.Add(new("Base64 编码", Base64Encode(text)));
        rows.Add(new("URL 编码", UrlEncode(text)));
        var unicode = UnicodeEscape(text);
        if (unicode != text) rows.Add(new("Unicode 转义", unicode));
        var html = HtmlEscape(text);
        if (html != text) rows.Add(new("HTML 转义", html));
        return rows;
    }

    public static string Base64Encode(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    /// 支持标准和 URL 安全两种字母表，缺的补齐等号；解出来不是正常文字时返回 null。
    public static string? Base64Decode(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            builder.Append(c switch { '-' => '+', '_' => '/', _ => c });
        }
        var normalized = builder.ToString();
        if (normalized.Length < 4 || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/' or '=')) return null;
        var remainder = normalized.Length % 4;
        if (remainder == 1) return null;
        if (remainder > 0) normalized += new string('=', 4 - remainder);

        var buffer = new byte[normalized.Length / 4 * 3];
        if (!Convert.TryFromBase64String(normalized, buffer, out var written)) return null;
        string decoded;
        try
        {
            decoded = StrictUtf8.GetString(buffer, 0, written);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
        if (decoded.Length == 0) return null;
        foreach (var rune in decoded.EnumerateRunes())
        {
            if (rune.Value is '\n' or '\r' or '\t') continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format) return null;
        }
        return decoded;
    }

    /// 只保留字母、数字和 -._~，其余按 UTF-8 转成 %XX
    public static string UrlEncode(string text) => Uri.EscapeDataString(text);

    /// 没有 % 时返回 null；% 后面不是两位十六进制、或者解出来不是正常的 UTF-8 时也返回 null。「+」保持原样。
    public static string? UrlDecode(string text)
    {
        if (!text.Contains('%')) return null;
        byte[] source;
        try
        {
            source = StrictUtf8.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            return null;
        }
        var bytes = new List<byte>(source.Length);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] != (byte)'%')
            {
                bytes.Add(source[i]);
                continue;
            }
            if (i + 2 >= source.Length) return null;
            var high = HexDigit(source[i + 1]);
            var low = HexDigit(source[i + 2]);
            if (high < 0 || low < 0) return null;
            bytes.Add((byte)(high * 16 + low));
            i += 2;
        }
        try
        {
            return StrictUtf8.GetString(bytes.ToArray());
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }

    private static int HexDigit(byte b) => b switch
    {
        >= (byte)'0' and <= (byte)'9' => b - '0',
        >= (byte)'a' and <= (byte)'f' => b - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => b - 'A' + 10,
        _ => -1,
    };

    /// 非 ASCII 字符转成 \uXXXX（按 UTF-16 编码，表情等字符是一对代理项）。
    public static string UnicodeEscape(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var unit in text)
        {
            if (unit < 0x80) result.Append(unit);
            else result.Append("\\u").Append(((int)unit).ToString("x4", CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }

    /// 还原 \uXXXX（含代理项对）和 \u{XXXXX}；没有 \u 时返回 null。
    public static string? UnicodeUnescape(string text)
    {
        if (!text.Contains("\\u", StringComparison.Ordinal)) return null;
        var scalars = text.EnumerateRunes().ToArray();
        var result = new StringBuilder(text.Length);
        var pendingUnits = new StringBuilder();

        void FlushUnits()
        {
            if (pendingUnits.Length == 0) return;
            // 落单的代理项换成 U+FFFD
            AppendSanitized(result, pendingUnits.ToString());
            pendingUnits.Clear();
        }

        uint? HexValue(int start, int end)
        {
            if (start < 0 || end > scalars.Length || start >= end) return null;
            uint value = 0;
            for (var i = start; i < end; i++)
            {
                var digit = scalars[i].IsAscii ? HexDigit((byte)scalars[i].Value) : -1;
                if (digit < 0) return null;
                if (value > 0x0FFF_FFFF) return null;
                value = value * 16 + (uint)digit;
            }
            return value;
        }

        var index = 0;
        while (index < scalars.Length)
        {
            if (scalars[index].Value == '\\' && index + 1 < scalars.Length && scalars[index + 1].Value == 'u')
            {
                if (index + 2 < scalars.Length && scalars[index + 2].Value == '{')
                {
                    var close = Array.FindIndex(scalars, index + 3, r => r.Value == '}');
                    if (close >= 0 && close - (index + 3) <= 6
                        && HexValue(index + 3, close) is { } scalarValue && Rune.IsValid(scalarValue))
                    {
                        FlushUnits();
                        result.Append(new Rune(scalarValue).ToString());
                        index = close + 1;
                        continue;
                    }
                }
                if (HexValue(index + 2, index + 6) is { } unit && unit <= 0xFFFF)
                {
                    pendingUnits.Append((char)unit);
                    index += 6;
                    continue;
                }
            }
            FlushUnits();
            result.Append(scalars[index].ToString());
            index++;
        }
        FlushUnits();
        return result.ToString();
    }

    private static void AppendSanitized(StringBuilder result, string units)
    {
        for (var i = 0; i < units.Length; i++)
        {
            var c = units[i];
            if (char.IsHighSurrogate(c) && i + 1 < units.Length && char.IsLowSurrogate(units[i + 1]))
            {
                result.Append(c).Append(units[i + 1]);
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                result.Append('�');
            }
            else
            {
                result.Append(c);
            }
        }
    }

    public static string HtmlEscape(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '&': result.Append("&amp;"); break;
                case '<': result.Append("&lt;"); break;
                case '>': result.Append("&gt;"); break;
                case '"': result.Append("&quot;"); break;
                case '\'': result.Append("&#39;"); break;
                default: result.Append(c); break;
            }
        }
        return result.ToString();
    }

    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.Ordinal)
    {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'", ["nbsp"] = " ",
        ["copy"] = "©", ["reg"] = "®", ["trade"] = "™", ["hellip"] = "…", ["mdash"] = "—",
        ["ndash"] = "–", ["lsquo"] = "‘", ["rsquo"] = "’", ["ldquo"] = "“", ["rdquo"] = "”",
        ["middot"] = "·", ["yen"] = "¥", ["euro"] = "€",
    };

    /// 还原命名实体、&#十进制; 和 &#x十六进制;；认不出的原样保留。没有 & 或 ; 时返回 null。
    public static string? HtmlUnescape(string text)
    {
        if (!text.Contains('&') || !text.Contains(';')) return null;
        var result = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            if (text[index] == '&')
            {
                // 分号要在 & 起的 12 个字符以内
                var semicolon = text.IndexOf(';', index, Math.Min(12, text.Length - index));
                if (semicolon >= 0)
                {
                    var entity = text[(index + 1)..semicolon];
                    string? replacement = null;
                    if (entity.StartsWith("#x", StringComparison.Ordinal) || entity.StartsWith("#X", StringComparison.Ordinal))
                    {
                        if (uint.TryParse(entity.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)
                            && Rune.IsValid(value))
                            replacement = new Rune(value).ToString();
                    }
                    else if (entity.StartsWith('#'))
                    {
                        if (uint.TryParse(entity.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
                            && Rune.IsValid(value))
                            replacement = new Rune(value).ToString();
                    }
                    else
                    {
                        replacement = NamedEntities.GetValueOrDefault(entity);
                    }
                    if (replacement is not null)
                    {
                        result.Append(replacement);
                        index = semicolon + 1;
                        continue;
                    }
                }
            }
            result.Append(text[index]);
            index++;
        }
        return result.ToString();
    }
}
