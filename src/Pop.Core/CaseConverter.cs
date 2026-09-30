using System.Globalization;
using System.Text;

namespace Pop.Core;

/// 大小写和命名风格转换。纯逻辑，方便测试。
public static class CaseConverter
{
    /// 拆成单词：按空格、标点拆分，也拆开驼峰写法（getHTTPResponse → get、HTTP、Response）。
    public static IReadOnlyList<string> Words(string text)
    {
        var words = new List<string>();
        var current = new StringBuilder();
        var characters = TextElements(text);
        string? last = null;
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            if (!IsLetter(character) && !IsNumber(character))
            {
                if (current.Length > 0)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
                last = null;
                continue;
            }
            if (last is not null)
            {
                var next = index + 1 < characters.Count ? characters[index + 1] : null;
                var lowerToUpper = (IsLower(last) || IsNumber(last)) && IsUpper(character);
                var acronymEnds = IsUpper(last) && IsUpper(character) && next is not null && IsLower(next);
                if (lowerToUpper || acronymEnds)
                {
                    words.Add(current.ToString());
                    current.Clear();
                }
            }
            current.Append(character);
            last = character;
        }
        if (current.Length > 0) words.Add(current.ToString());
        return words;
    }

    public static IReadOnlyList<ResultLine> Conversions(string text)
    {
        var lower = Words(text).Select(w => w.ToLowerInvariant()).ToList();
        if (lower.Count == 0) return [];
        var capitalized = lower.Select(CapitalizeFirst).ToList();
        return
        [
            new("大写", text.ToUpperInvariant()),
            new("小写", text.ToLowerInvariant()),
            new("首字母大写", Capitalized(text)),
            new("camelCase", lower[0] + string.Concat(capitalized.Skip(1))),
            new("PascalCase", string.Concat(capitalized)),
            new("snake_case", string.Join("_", lower)),
            new("kebab-case", string.Join("-", lower)),
            new("CONSTANT", string.Join("_", lower).ToUpperInvariant()),
        ];
    }

    /// 每个单词首字母大写、其余小写；紧跟在非字母后面的字母算新单词的开头
    public static string Capitalized(string text)
    {
        var result = new StringBuilder(text.Length);
        var previousIsLetter = false;
        foreach (var element in TextElements(text))
        {
            var isLetter = IsLetter(element);
            result.Append(isLetter && !previousIsLetter ? element.ToUpperInvariant() : element.ToLowerInvariant());
            previousIsLetter = isLetter;
        }
        return result.ToString();
    }

    private static string CapitalizeFirst(string word)
    {
        var elements = TextElements(word);
        return elements.Count == 0 ? word : elements[0].ToUpperInvariant() + string.Concat(elements.Skip(1));
    }

    /// 按用户看到的字符拆（带组合符号的字母、表情算一个）
    private static List<string> TextElements(string text)
    {
        var list = new List<string>();
        var enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext()) list.Add(enumerator.GetTextElement());
        return list;
    }

    private static Rune FirstRune(string element) =>
        Rune.DecodeFromUtf16(element, out var rune, out _) == System.Buffers.OperationStatus.Done ? rune : Rune.ReplacementChar;

    private static bool IsLetter(string element) => Rune.IsLetter(FirstRune(element));
    private static bool IsNumber(string element) => Rune.IsNumber(FirstRune(element));
    private static bool IsUpper(string element) => Rune.IsUpper(FirstRune(element));
    private static bool IsLower(string element) => Rune.IsLower(FirstRune(element));
}
