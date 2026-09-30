using System.Globalization;
using System.Text;

namespace Pop.Core;

public readonly record struct TextStats(int Characters, int CharactersNoSpaces, int Chinese, int Words, int Lines);

/// 原型里圆盘上几个功能的文字处理
public static class TextActions
{
    public static string SearchUrl(string text) =>
        "https://www.bing.com/search?q=" + Uri.EscapeDataString(text.Trim());

    /// 中文翻成英文，其他翻成简体中文
    public static string TranslateUrl(string text)
    {
        var to = ChineseRatio(text) > 0.3 ? "en" : "zh-Hans";
        return $"https://www.bing.com/translator?from=auto&to={to}&text={Uri.EscapeDataString(text.Trim())}";
    }

    public static string ToUpper(string text) => text.ToUpper(CultureInfo.InvariantCulture);
    public static string ToLower(string text) => text.ToLower(CultureInfo.InvariantCulture);

    public static bool IsChinese(Rune r) =>
        r.Value is >= 0x4E00 and <= 0x9FFF or >= 0x3400 and <= 0x4DBF or >= 0x20000 and <= 0x2EBEF or >= 0xF900 and <= 0xFAFF;

    public static double ChineseRatio(string text)
    {
        int chinese = 0, letters = 0;
        foreach (var r in text.EnumerateRunes())
        {
            if (IsChinese(r)) { chinese++; letters++; }
            else if (Rune.IsLetter(r)) letters++;
        }
        return letters == 0 ? 0 : (double)chinese / letters;
    }

    /// 字数：汉字一个算一个字，连续的字母数字算一个词
    public static TextStats Count(string text)
    {
        int characters = 0, noSpaces = 0, chinese = 0, words = 0;
        var inWord = false;
        foreach (var r in text.EnumerateRunes())
        {
            if (r.Value is '\r' or '\n') { inWord = false; continue; }
            characters++;
            if (Rune.IsWhiteSpace(r)) { inWord = false; continue; }
            noSpaces++;
            if (IsChinese(r)) { chinese++; inWord = false; continue; }
            if (Rune.IsLetterOrDigit(r) || (inWord && r.Value is '\'' or '’'))
            {
                if (!inWord) words++;
                inWord = true;
            }
            else inWord = false;
        }
        var lines = text.Length == 0 ? 0 : text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Length;
        return new TextStats(characters, noSpaces, chinese, words, lines);
    }

    public static string Describe(TextStats s) =>
        $"字符 {s.Characters}（不含空格 {s.CharactersNoSpaces}）\n汉字 {s.Chinese}\n单词 {s.Words}\n行数 {s.Lines}";
}
