using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 文字整理：合并换行、去空行、去多余空格、中英文之间加空格、全角转半角、去掉看不见的字符、按行排序去重。
/// 纯逻辑，方便测试。
public static class TextCleanup
{
    /// 至少有一种整理方式会让文字变样时才出现在圆盘上：有换行、连续空白、汉字、全角字母数字或者看不见的字符。
    /// 汉字按码点范围列出（基本区、扩展 A、兼容区、部首，以及扩展 B 之后用代理对表示的部分）
    public const string ApplicablePattern =
        @"\n|[ \t 　]{2}"
        + @"|[⺀-⺙⺛-⻳⼀-⿕々〇〡-〩〸-〻㐀-䶿一-鿿豈-舘並-龎]"
        + @"|[\uD840-\uD888][\uDC00-\uDFFF]"
        + @"|[０-９Ａ-Ｚａ-ｚ]"
        + @"|[­​-‏ -‮⁠-⁤⁦-⁩﻿]";

    private static readonly Regex Applicable = new(ApplicablePattern, RegexOptions.CultureInvariant);

    public static bool IsApplicable(string text) => Applicable.IsMatch(text);

    /// 只列出会让文字变样的整理方式
    public static IReadOnlyList<ResultLine> Conversions(string text)
    {
        var rows = new List<ResultLine>();
        void Add(string label, string? value)
        {
            if (string.IsNullOrEmpty(value) || value == text || rows.Exists(r => r.Value == value)) return;
            rows.Add(new ResultLine(label, value));
        }
        Add("合并换行", JoinLines(text));
        Add("去掉空行", RemoveBlankLines(text));
        Add("去多余空格", CollapseSpaces(text));
        Add("中英文空格", SpaceBetweenCjkAndLatin(text));
        Add("全角转半角", HalfWidth(text));
        Add("去掉看不见的字符", RemovingInvisibles(text));
        Add("按行排序", SortLines(text));
        Add("按行去重", UniqueLines(text));
        return rows;
    }

    // 换行和空白

    /// 把段落里被硬换行切开的句子接起来（从 PDF 里复制的文字常见）。空行分开的段落仍然分开。
    /// 英文行尾的连字符（sen-\ntence）去掉后接上；中文、日文接的时候不加空格。
    public static string? JoinLines(string text)
    {
        var lines = NormalizedLines(text);
        if (lines.Length <= 1) return null;
        var paragraphs = new List<string>();
        var current = "";
        foreach (var rawLine in lines)
        {
            var line = TrimHorizontal(rawLine);
            if (line.Length == 0)
            {
                if (current.Length > 0)
                {
                    paragraphs.Add(current);
                    current = "";
                }
                continue;
            }
            if (current.Length == 0)
            {
                current = line;
                continue;
            }
            var last = LastRune(current, current.Length);
            var first = FirstRune(line);
            if (last.Value == '-' && Rune.IsLower(first) && current.Length > 1 && Rune.IsLetter(LastRune(current, current.Length - 1)))
            {
                current = current[..^1] + line;
            }
            else if (IsCjk(last.Value) || IsCjk(first.Value))
            {
                current += line;
            }
            else
            {
                current += " " + line;
            }
        }
        if (current.Length > 0) paragraphs.Add(current);
        return string.Join("\n\n", paragraphs);
    }

    public static string? RemoveBlankLines(string text)
    {
        var lines = NormalizedLines(text);
        if (lines.Length <= 1) return null;
        return string.Join("\n", lines.Where(l => TrimHorizontal(l).Length > 0));
    }

    private static readonly char[] HorizontalSpaces = [' ', '\t', ' ', '　'];

    /// 连续的空格、制表符（包括不换行空格和全角空格）并成一个，去掉每行首尾的空白
    public static string CollapseSpaces(string text) =>
        string.Join("\n", NormalizedLines(text).Select(line =>
            string.Join(" ", line.Split(HorizontalSpaces, StringSplitOptions.RemoveEmptyEntries))));

    // 中英文

    /// 汉字、假名和英文字母、数字挨着时中间加一个空格：用React写 → 用 React 写
    public static string SpaceBetweenCjkAndLatin(string text)
    {
        var result = new StringBuilder(text.Length + 8);
        string? previous = null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            var character = elements.GetTextElement();
            if (previous != null
                && ((IsHanOrKana(previous) && IsLatinOrDigit(character)) || (IsLatinOrDigit(previous) && IsHanOrKana(character))))
            {
                result.Append(' ');
            }
            result.Append(character);
            previous = character;
        }
        return result.ToString();
    }

    /// 全角的字母、数字和空格换成半角：ＡＢＣ１２３ → ABC123。全角标点（，。！？）保持不变。
    public static string HalfWidth(string text)
    {
        var result = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is (>= '０' and <= '９') or (>= 'Ａ' and <= 'Ｚ') or (>= 'ａ' and <= 'ｚ'))
                result.Append((char)(c - 0xFEE0));
            else if (c == '　')
                result.Append(' ');
            else
                result.Append(c);
        }
        return result.ToString();
    }

    // 看不见的字符

    /// 看不见、但会让文字出问题的字符（从网页、聊天软件复制的文字里常见）
    private static readonly HashSet<int> Invisibles =
    [
        0x00A0, 0x00AD, 0x034F, 0x061C, 0x115F, 0x1160, 0x17B4, 0x17B5, 0x180E,
        0x200B, 0x200C, 0x200D, 0x200E, 0x200F, 0x2028, 0x2029,
        0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
        0x2060, 0x2061, 0x2062, 0x2063, 0x2064, 0x2066, 0x2067, 0x2068, 0x2069,
        0x3164, 0xFEFF, 0xFFA0,
    ];

    /// 去掉看不见的字符：不换行空格换成普通空格，行、段分隔符换成换行，其余直接删掉。
    /// 表情符号里用来连接的零宽连字保留。
    public static string RemovingInvisibles(string text)
    {
        var runes = text.EnumerateRunes().ToArray();
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < runes.Length; i++)
        {
            var value = runes[i].Value;
            if (!IsInvisible(runes, i))
            {
                result.Append(runes[i].ToString());
                continue;
            }
            if (value == 0x00A0) result.Append(' ');
            else if (value is 0x2028 or 0x2029) result.Append('\n');
        }
        return result.ToString();
    }

    private static bool IsInvisible(Rune[] runes, int index)
    {
        var value = runes[index].Value;
        if (!Invisibles.Contains(value)) return false;
        if (value == 0x200D && index > 0 && index + 1 < runes.Length
            && IsEmojiLike(runes[index - 1].Value) && IsEmojiLike(runes[index + 1].Value))
        {
            return false;
        }
        return true;
    }

    /// 表情符号：变体选择符、肤色，以及常见的表情和符号区段
    private static bool IsEmojiLike(int value) =>
        value == 0xFE0F
        || value is >= 0x1F000 and <= 0x1FAFF
        || value is >= 0x2600 and <= 0x27BF
        || value is >= 0x2300 and <= 0x23FF
        || value is >= 0x2B00 and <= 0x2BFF
        || value is >= 0x2190 and <= 0x21FF
        || value is >= 0x25A0 and <= 0x25FF
        || value is 0x203C or 0x2049 or 0x2122 or 0x2139 or 0x2934 or 0x2935 or 0x3030 or 0x303D or 0x3297 or 0x3299
        || value is >= 0xE0020 and <= 0xE007F;

    // 按行

    public static string? SortLines(string text)
    {
        var lines = NormalizedLines(text);
        if (lines.Count(l => l.Length > 0) <= 1) return null;
        return string.Join("\n", lines.OrderBy(l => l, NaturalComparer.Instance));
    }

    public static string? UniqueLines(string text)
    {
        var lines = NormalizedLines(text);
        if (lines.Length <= 1) return null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        return string.Join("\n", lines.Where(seen.Add));
    }

    /// 按行排序用的比较：忽略大小写，连续的数字按数值比（第2行排在第10行前面）
    private sealed class NaturalComparer : IComparer<string>
    {
        public static readonly NaturalComparer Instance = new();
        private static readonly CompareInfo Compare = CultureInfo.InvariantCulture.CompareInfo;

        int IComparer<string>.Compare(string? x, string? y)
        {
            if (x == null || y == null) return string.CompareOrdinal(x, y);
            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
                {
                    int si = i, sj = j;
                    while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                    var a = x[si..i].TrimStart('0');
                    var b = y[sj..j].TrimStart('0');
                    if (a.Length != b.Length) return a.Length.CompareTo(b.Length);
                    var digits = string.CompareOrdinal(a, b);
                    if (digits != 0) return digits;
                }
                else
                {
                    int si = i, sj = j;
                    while (i < x.Length && !char.IsAsciiDigit(x[i])) i++;
                    while (j < y.Length && !char.IsAsciiDigit(y[j])) j++;
                    var part = Compare.Compare(x[si..i], y[sj..j], CompareOptions.IgnoreCase);
                    if (part != 0) return part;
                }
            }
            var rest = (x.Length - i).CompareTo(y.Length - j);
            return rest != 0 ? rest : Compare.Compare(x, y, CompareOptions.None);
        }
    }

    // 字符分类

    private static string[] NormalizedLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    /// 去掉首尾的空格和制表符（包括不换行空格、全角空格这类空白，不含换行）
    internal static string TrimHorizontal(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && IsHorizontalWhitespace(text[start])) start++;
        while (end > start && IsHorizontalWhitespace(text[end - 1])) end--;
        return text[start..end];
    }

    private static bool IsHorizontalWhitespace(char c) =>
        c == '\t' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator;

    /// end 之前的最后一个码点
    private static Rune LastRune(string text, int end) =>
        Rune.DecodeLastFromUtf16(text.AsSpan(0, end), out var rune, out _) == OperationStatus.Done ? rune : Rune.ReplacementChar;

    private static Rune FirstRune(string text) =>
        Rune.DecodeFromUtf16(text, out var rune, out _) == OperationStatus.Done ? rune : Rune.ReplacementChar;

    /// 汉字、假名、中日文标点和全角字符：这些字符前后接行时不加空格。谚文按词分行，不算在内。
    public static bool IsCjk(int value) =>
        value is (>= 0x2E80 and <= 0x303F) or (>= 0x3040 and <= 0x30FF) or (>= 0x31F0 and <= 0x31FF)
            or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF) or (>= 0xF900 and <= 0xFAFF)
            or (>= 0xFE30 and <= 0xFE4F) or (>= 0xFF00 and <= 0xFFEF) or (>= 0x20000 and <= 0x2FFFF);

    private static bool IsHanOrKana(string character)
    {
        var value = FirstRune(character).Value;
        return value is (>= 0x3040 and <= 0x30FF) or (>= 0x3400 and <= 0x4DBF) or (>= 0x4E00 and <= 0x9FFF)
            or (>= 0xF900 and <= 0xFAFF) or (>= 0x20000 and <= 0x2FFFF);
    }

    private static bool IsLatinOrDigit(string character) =>
        character.Length == 1 && char.IsAsciiLetterOrDigit(character[0]);
}
