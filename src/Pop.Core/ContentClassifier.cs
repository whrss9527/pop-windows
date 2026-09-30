using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 识别出的内容特征。一段内容可以同时具备多个特征（比如链接同时也是文本）
[Flags]
public enum ContentKind
{
    None = 0,
    /// 任意文本
    Text = 1 << 0,
    /// 以中文为主的自然语言文本
    ChineseText = 1 << 1,
    /// 非中文的自然语言文本（外文）
    ForeignText = 1 << 2,
    Url = 1 << 3,
    Email = 1 << 4,
    /// 可计算的算式
    Math = 1 << 5,
    /// Unix 时间戳（秒或毫秒）
    Timestamp = 1 << 6,
    Json = 1 << 7,
    /// 本机上存在的路径
    Files = 1 << 8,
    /// 单个词（英文单词或很短的中文词）
    Word = 1 << 9,
    /// 颜色值：#RRGGBB、rgb()、hsl()
    Color = 1 << 10,
    /// 数字（十进制、0x 十六进制、0b 二进制、0o 八进制）
    Number = 1 << 11,
    /// 日期时间，比如 2026-09-28 14:30、2026年9月28日
    DateTime = 1 << 12,
    /// 带单位的数值，比如 5 km、100°F、2 斤、1 TB
    Measurement = 1 << 13,
}

/// 分类后的内容
public sealed record ClassifiedContent(ContentKind Kinds, string Text, string? Url = null, string? Path = null)
{
    public static readonly ClassifiedContent Empty = new(ContentKind.None, "");

    public bool IsEmpty => Kinds == ContentKind.None;

    public bool Has(ContentKind kind) => (Kinds & kind) == kind;

    /// 圆盘中心显示的简短描述
    public string Summary
    {
        get
        {
            if (IsEmpty) return "未选中内容";
            if (Has(ContentKind.Measurement) && Text.Length <= 12) return Text;
            ReadOnlySpan<(ContentKind Kind, string Title)> specific =
            [
                (ContentKind.Url, "链接"), (ContentKind.Email, "邮箱"), (ContentKind.Math, "算式"), (ContentKind.Timestamp, "时间戳"),
                (ContentKind.Json, "JSON"), (ContentKind.Color, "颜色"), (ContentKind.DateTime, "日期时间"),
                (ContentKind.Measurement, "带单位的数值"), (ContentKind.Number, "数字"), (ContentKind.Files, "路径"),
            ];
            foreach (var (kind, title) in specific)
                if (Has(kind)) return title;
            if (Has(ContentKind.Word)) return Text;
            return $"{new StringInfo(Text).LengthInTextElements} 字";
        }
    }
}

/// 把选中的文字归类，决定直接出哪种结果、圆盘上哪些功能可用
public static partial class ContentClassifier
{
    public static ClassifiedContent Classify(string? raw)
    {
        var text = raw?.Trim() ?? "";
        if (text.Length == 0) return ClassifiedContent.Empty;
        var kinds = ContentKind.Text;
        string? url = null, path = null;

        // 结构化文本（链接、JSON、颜色、时间、带单位的数值、数字、算式、路径）不再算作自然语言
        if (DetectLink(text) is { } link)
        {
            url = link;
            kinds |= link.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? ContentKind.Email : ContentKind.Url;
        }
        else if (IsJson(text)) kinds |= ContentKind.Json;
        else if (ColorValue.Parse(text) is not null) kinds |= ContentKind.Color;
        else if (TimestampConverter.Parse(text) is not null) kinds |= ContentKind.Timestamp;
        else if (DetectFilePath(text) is { } file)
        {
            path = file;
            kinds |= ContentKind.Files;
        }
        else if (DateParser.Parse(text) is not null) kinds |= ContentKind.DateTime;
        else if (UnitConverter.Parse(text) is not null) kinds |= ContentKind.Measurement;
        else if (NumberConverter.Parse(text) is not null) kinds |= ContentKind.Number;
        else if (LooksLikeMath(text)) kinds |= ContentKind.Math;
        else if (ColorContrast.IsColorPair(text) || LooksLikeToken(text))
        {
            // 两个颜色、令牌、哈希、密钥这类串只当普通文字，不算外文
        }
        else
        {
            var profile = new ScriptProfile(text);
            if (profile.IsChinese) kinds |= ContentKind.ChineseText;
            else if (profile.HasLetters) kinds |= ContentKind.ForeignText;
            if (profile.HasLetters && IsSingleWord(text)) kinds |= ContentKind.Word;
        }
        return new ClassifiedContent(kinds, text, url, path);
    }

    public static bool IsJson(string text)
    {
        if (text.Length < 2 || !(text[0] == '{' && text[^1] == '}' || text[0] == '[' && text[^1] == ']')) return false;
        try
        {
            using var _ = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// 没有空白、又长又混着字母和数字的串：JWT、哈希、API Key、提交号、订单号……
    public static bool LooksLikeToken(string text)
    {
        if (text.Length < 16 || text.Any(char.IsWhiteSpace)) return false;
        return text.Any(char.IsAsciiDigit) && text.Any(char.IsAsciiLetter) && new ScriptProfile(text).Han == 0;
    }

    /// 单个英文单词（可以带连字符、撇号），或者不超过 8 个字的纯中文词
    public static bool IsSingleWord(string text)
    {
        var elements = new StringInfo(text).LengthInTextElements;
        if (elements > 40 || !char.IsLetter(text[0]) || !char.IsLetter(text[^1])) return false;
        foreach (var r in text.EnumerateRunes())
            if (!Rune.IsLetter(r) && r.Value is not ('-' or '\'' or '’')) return false;
        var profile = new ScriptProfile(text);
        if (profile.Han > 0) return profile.Han == elements && elements <= 8;
        return elements >= 2 && profile.Kana == 0 && profile.Hangul == 0;
    }

    /// 选中的是本机上存在的路径（C:\…、\\服务器\…，%USERPROFILE% 这样的环境变量会展开）
    public static string? DetectFilePath(string text)
    {
        if (text.Length >= 1024 || text.Contains('\n') || text.Contains('\r')) return null;
        var candidate = text.Trim('"');
        if (candidate.Contains('%')) candidate = Environment.ExpandEnvironmentVariables(candidate);
        if (candidate.StartsWith("~/", StringComparison.Ordinal))
            candidate = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), candidate[2..]);
        try
        {
            if (!System.IO.Path.IsPathFullyQualified(candidate)) return null;
            return File.Exists(candidate) || Directory.Exists(candidate) ? candidate : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// 整段文字是一个链接或邮箱时返回链接（邮箱返回 mailto:）。
    /// 只认带协议头、www. 开头或邮箱，避免把 README.md、main.py 这类文件名当成域名
    public static string? DetectLink(string text)
    {
        if (text.Length >= 2048 || text.Any(char.IsWhiteSpace)) return null;
        if (EmailPattern().IsMatch(text)) return "mailto:" + text;
        if (text.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) && EmailPattern().IsMatch(text[7..])) return text;
        var candidate = text.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? "https://" + text : text;
        if (!candidate.Contains("://", StringComparison.Ordinal)) return null;
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme is not ("http" or "https" or "ftp")) return null;
        if (uri.Host.Length == 0 || (!uri.Host.Contains('.') && uri.Host != "localhost")) return null;
        return uri.AbsoluteUri;
    }

    [GeneratedRegex(@"^[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    /// 形如 1+2*3、(3.5 - 1) / 2、200*15% 的算式。
    /// 只有减号或斜杠、且没有空格的串（2026-09-28、138-0000-0000、9/28）当作日期或编号
    public static bool LooksLikeMath(string text)
    {
        if (text.Length > 200) return false;
        var chars = Calculator.Normalize(text);
        if (chars.Length < 3) return false;
        const string binary = "+-*/^";
        if (!chars.All(c => char.IsAsciiDigit(c) || binary.Contains(c) || "%.()".Contains(c)) || !chars.Any(char.IsAsciiDigit)) return false;
        var operators = chars.Skip(1).Where(c => binary.Contains(c)).ToList();
        if (operators.Count == 0) return false;
        var identifier = !text.Contains(' ') && !chars.Contains('(') && (operators.All(c => c == '-') || operators.All(c => c == '/'));
        return !identifier && Calculator.Evaluate(text) is not null;
    }
}

/// 一段文字里各种文字的数量
public readonly struct ScriptProfile
{
    public int Han { get; }
    public int Kana { get; }
    public int Hangul { get; }
    /// 其他字母文字（拉丁、西里尔等）组成的单词数
    public int OtherWords { get; }

    public ScriptProfile(string text)
    {
        var inWord = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (TextActions.IsChinese(r)) { Han++; inWord = false; }
            else if (r.Value is >= 0x3040 and <= 0x30FF) { Kana++; inWord = false; }
            else if (r.Value is >= 0xAC00 and <= 0xD7AF or >= 0x1100 and <= 0x11FF) { Hangul++; inWord = false; }
            else if (Rune.IsLetter(r))
            {
                if (!inWord) OtherWords++;
                inWord = true;
            }
            else inWord = false;
        }
    }

    public bool HasLetters => Han + Kana + Hangul + OtherWords > 0;

    /// 以中文为主：有汉字、没有假名和谚文，并且汉字数不少于其他文字的单词数（「用 React 写一个组件」算中文）
    public bool IsChinese => Han > 0 && Kana == 0 && Hangul == 0 && Han >= OtherWords;
}
