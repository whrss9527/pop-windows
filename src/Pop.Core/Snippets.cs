using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Pop.Core;

public sealed record Snippet(string Id, string Title, string Text)
{
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title)) return Title.Trim();
            var start = Text.AsSpan().IndexOfAnyExcept('\r', '\n');
            if (start < 0) return "（空）";
            // 40 字符标题只多读一个码点，不扫描首行余下的大段正文。
            var first = Text.AsSpan(start, Math.Min(Text.Length - start, 42));
            var end = first.IndexOfAny('\r', '\n');
            if (end >= 0) first = first[..end];
            return TextPreview.Truncate(first, 40);
        }
    }
    [JsonIgnore]
    public string DisplayPreview => TextPreview.WithLineBreaks(Text, 160);

    public bool Matches(string query)
    {
        var keyword = query.AsSpan().Trim();
        if (keyword.IsEmpty || Text.AsSpan().Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        var title = DisplayTitle.AsSpan();
        if (title.Contains(keyword, StringComparison.OrdinalIgnoreCase)) return true;
        // 保留原来「标题 + 换行 + 正文」的跨边界匹配，但不创建整段拼接副本。
        for (var i = 0; i < keyword.Length; i++)
        {
            var suffix = keyword.Length - i - 1;
            if (keyword[i] == '\n' && i <= title.Length && suffix <= Text.Length
                && title[^i..].Equals(keyword[..i], StringComparison.OrdinalIgnoreCase)
                && Text.AsSpan(0, suffix).Equals(keyword[(i + 1)..], StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
    public static IReadOnlyList<Snippet> Examples =>
    [new("example-date", "今天的日期", "{date}"), new("example-signature", "邮件结尾", "祝好！\n\n{date}")];
    public static List<Snippet> Normalize(IEnumerable<Snippet> values)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Snippet>(); var characters = 0;
        foreach (var item in values)
        {
            if (item is not { Text: not null } || item.Title?.Length > 200 || item.Text.Length is 0 or > 100_000 || string.IsNullOrWhiteSpace(item.Text)) continue;
            var snippet = item with { Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id, Title = item.Title ?? "" };
            if (snippet.Id.Length > 128 || !seen.Add(snippet.Id)) continue;
            if (characters + snippet.Text.Length > 2_000_000) break;
            result.Add(snippet); characters += snippet.Text.Length;
            if (result.Count >= 1000) break;
        }
        return result;
    }
}

public static partial class SnippetExpander
{
    public const int MaximumCharacters = 2_000_000;
    /// 只扫描模板一次，剪贴板和选区里的花括号原样保留，不递归展开。
    public static string Expand(string text, DateTimeOffset now, TimeZoneInfo? zone = null, string? clipboard = null, string? selection = null)
    {
        var date = TimeZoneInfo.ConvertTime(now, zone ?? TimeZoneInfo.Local);
        long length = text.Length;
        if (length > MaximumCharacters) throw new ArgumentException("短语展开后超过 200 万个字符，请缩短模板、选区或剪贴板内容");
        return Placeholder().Replace(text, match =>
        {
            var value = match.Value switch
            {
                "{date}" => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                "{time}" => date.ToString("HH:mm", CultureInfo.InvariantCulture),
                "{datetime}" => date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                "{weekday}" => date.ToString("dddd", CultureInfo.GetCultureInfo("zh-CN")),
                "{clipboard}" => clipboard ?? "",
                "{selection}" => selection ?? "",
                _ => match.Value,
            };
            length += value.Length - match.Length;
            if (length > MaximumCharacters) throw new ArgumentException("短语展开后超过 200 万个字符，请缩短模板、选区或剪贴板内容");
            return value;
        });
    }
    [GeneratedRegex(@"\{(?:date|time|datetime|weekday|clipboard|selection)\}")]
    private static partial Regex Placeholder();
}
