using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 按行处理：一列文字（每行一项）加引号和逗号、转 JSON 数组、加减序号、倒序、打乱；一行用逗号隔开的拆成多行。
/// 纯逻辑，方便测试。
public static class LineTools
{
    /// 拆出来的各项；FromLines 表示原来就是一行一项（否则是一行里用分隔符隔开的）
    public sealed record Items(IReadOnlyList<string> Values, bool FromLines)
    {
        public bool Equals(Items? other) =>
            other != null && FromLines == other.FromLines && Values.SequenceEqual(other.Values);

        public override int GetHashCode() => HashCode.Combine(FromLines, Values.Count);
    }

    public const int MaxItems = 10_000;

    /// 一行里能拆开的分隔符，按优先级排
    private static readonly string[] Separators = ["\t", "，", "、", "；", ";", "|", ","];

    public static Items? Parse(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n')
            .Select(TextCleanup.TrimHorizontal)
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count >= 2)
        {
            return lines.Count <= MaxItems ? new Items(lines, true) : null;
        }
        if (lines.Count == 0) return null;
        var line = lines[0];
        foreach (var separator in Separators)
        {
            if (!line.Contains(separator, StringComparison.Ordinal)) continue;
            var values = line.Split(separator)
                .Select(TextCleanup.TrimHorizontal)
                .Where(v => v.Length > 0)
                .ToList();
            if (values.Count >= 2 && values.Count <= MaxItems)
            {
                return new Items(values, false);
            }
        }
        return null;
    }

    /// 圆盘里要不要显示：至少两项，每项都不太长（像是一列值，不是几段文章）
    public static bool IsApplicable(string text) =>
        Parse(text) is { } parsed && parsed.Values.All(v => new StringInfo(v).LengthInTextElements <= 200);

    /// 「1. 」「2、」「(3)」「- 」「• 」这样的序号和项目符号；1.5、-5 这样的数不算
    private static readonly Regex Numbering =
        new(@"^(?:\d+(?:[)、．]|\.(?!\d))|[(（]\d+[)）]|[-*]\s|[•·])\s*", RegexOptions.CultureInvariant);

    private static readonly Regex PlainNumber =
        new(@"^-?(?:0|[1-9]\d*)(?:\.\d+)?$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<ResultLine> Conversions(string text)
    {
        if (Parse(text) is not { } parsed) return [];
        var values = parsed.Values;
        var trimmed = text.Trim();
        var rows = new List<ResultLine>();
        void Add(string label, string? value)
        {
            if (value == null || value == trimmed || rows.Exists(r => r.Value == value)) return;
            rows.Add(new ResultLine(label, value));
        }
        if (!parsed.FromLines)
        {
            Add("拆成多行", string.Join("\n", values));
        }
        Add("逗号隔开", string.Join(", ", values));
        Add("单引号", string.Join(", ", values.Select(v => "'" + v.Replace("'", "''") + "'")));
        Add("双引号", string.Join(", ", values.Select(JsonString)));
        var numeric = values.All(v => PlainNumber.IsMatch(v));
        Add("JSON 数组", "[" + string.Join(", ", numeric ? values : values.Select(JsonString)) + "]");
        var stripped = values.Select(RemovingNumbering).ToList();
        if (!stripped.SequenceEqual(values))
        {
            Add("去掉序号", string.Join("\n", stripped));
        }
        else
        {
            Add("加序号", string.Join("\n", values.Select((v, i) => (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + v)));
        }
        if (RemovingQuotes(values) is { } unquoted)
        {
            Add("去掉引号", string.Join("\n", unquoted));
        }
        var separator = parsed.FromLines ? "\n" : ", ";
        Add("倒序", string.Join(separator, values.Reverse()));
        if (values.Count > 2)
        {
            var shuffled = values.ToArray();
            Random.Shared.Shuffle(shuffled);
            Add("打乱顺序", string.Join(separator, shuffled));
        }
        return rows;
    }

    public static string RemovingNumbering(string value) => Numbering.Replace(value, "", 1);

    private static readonly (string Open, string Close)[] QuotePairs = [("'", "'"), ("\"", "\""), ("“", "”"), ("`", "`")];

    /// 每一项都用同一种引号包着时去掉引号
    public static IReadOnlyList<string>? RemovingQuotes(IReadOnlyList<string> values)
    {
        foreach (var (open, close) in QuotePairs)
        {
            if (values.All(v => v.Length >= 2 && v.StartsWith(open, StringComparison.Ordinal) && v.EndsWith(close, StringComparison.Ordinal)))
            {
                return values.Select(v => v[1..^1]).ToList();
            }
        }
        return null;
    }

    /// JSON 字符串写法：控制字符写成大写的 \uXXXX
    public static string JsonString(string value)
    {
        var result = new StringBuilder(value.Length + 2);
        result.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': result.Append("\\\""); break;
                case '\\': result.Append("\\\\"); break;
                case '\n': result.Append("\\n"); break;
                case '\r': result.Append("\\r"); break;
                case '\t': result.Append("\\t"); break;
                default:
                    if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    else result.Append(c);
                    break;
            }
        }
        return result.Append('"').ToString();
    }
}
