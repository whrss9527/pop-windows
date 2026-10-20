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
            var first = Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "（空）";
            return first.Length > 40 ? first[..40] + "…" : first;
        }
    }
    public bool Matches(string query) => string.IsNullOrWhiteSpace(query)
        || (DisplayTitle + "\n" + Text).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase);
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
