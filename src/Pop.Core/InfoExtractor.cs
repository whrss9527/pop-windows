using System.Globalization;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 提取出的信息类别
public enum InfoKind { Link, Email, Phone, Ip }

/// 提取出的一项
public sealed record InfoItem(InfoKind Kind, string Value);

/// 从一段文字里找出链接、邮箱、电话号码和 IP 地址，按出现的顺序、去掉重复。纯逻辑，方便测试。
public static partial class InfoExtractor
{
    public const int MaxLength = 100_000;

    public static string Title(InfoKind kind) => kind switch
    {
        InfoKind.Link => "链接",
        InfoKind.Email => "邮箱",
        InfoKind.Phone => "电话",
        _ => "IP 地址",
    };

    private static readonly InfoKind[] AllKinds = [InfoKind.Link, InfoKind.Email, InfoKind.Phone, InfoKind.Ip];

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![\d.])(?:(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(?::\d{1,5})?(?!\d)(?!\.\d)")]
    private static partial Regex Ipv4Regex();

    // 手机号（可以带 +86、中间用空格或短横线隔开）、带区号的固定电话、400/800 电话、+ 开头的国际号码、(415) 555-2671 这样的写法
    [GeneratedRegex(
        @"(?<![\d+])(?:\+?86[- ]?)?1[3-9]\d(?:[- ]?\d{4}){2}(?!\d)"
        + @"|(?<![\d(])(?:\(0\d{2,3}\)\s?|0\d{2,3}-)\d{7,8}(?:-\d{1,6})?(?!\d)"
        + @"|(?<!\d)[48]00-?\d{3}-?\d{4}(?!\d)"
        + @"|(?<![\w+])\+(?:[1-9]\d{0,2})[- ]?(?:\(\d{1,4}\)[- ]?)?\d{1,4}(?:[- ]?\d{2,4}){2,4}(?!\d)"
        + @"|(?<![\d(])\(\d{3}\)\s?\d{3}-\d{4}(?!\d)")]
    private static partial Regex PhoneRegex();

    // 网址候选：带协议的（https://…），或者没写协议的域名（可以带端口和路径）。
    // 先宽松地找出来，再按结束符截断、去掉结尾的标点，最后用 IsLikelyLink 判断
    [GeneratedRegex(
        @"(?<![A-Za-z0-9+.-])[A-Za-z][A-Za-z0-9+.-]*://[^\s<>""]+"
        + @"|(?<![A-Za-z0-9_@.-])(?:[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?\.)+[A-Za-z]{2,}(?::\d{1,5})?(?![A-Za-z0-9_@-])(?:/[^\s<>""]*)?")]
    private static partial Regex LinkCandidateRegex();

    [GeneratedRegex(@"@|://|[A-Za-z0-9-]\.[A-Za-z]{2,}|\d\.\d|\d{7,}|\d{3,4}[- ]\d{3,4}")]
    private static partial Regex HintRegex();

    /// 没写 http:// 的网址只认这些常见后缀（免得把 main.py、README.md 这样的文件名当成网址）
    /// （.app、.sh、.cc 这些也是文件扩展名，不算）
    private static readonly HashSet<string> BareDomainSuffixes =
    [
        "com", "net", "org", "cn", "io", "dev", "ai", "co", "me", "info", "edu", "gov", "xyz", "top", "tech",
        "site", "tv", "uk", "jp", "hk", "tw", "us", "de", "fr", "gg", "link",
    ];

    /// 网址里遇到这些字符就算结束（中文里夹着网址时常见）
    private const string UrlTerminators = "，。！？；：、“”‘’（）【】《》「」『』…　";

    /// 网址结尾的这些标点一般是句子的，不属于网址
    private const string TrailingPunctuation = ".,;:!?'\"*";

    public static IReadOnlyList<InfoItem> Extract(string text)
    {
        if (text.Length == 0 || text.Length > MaxLength) return [];
        var found = new List<(int Location, InfoItem Item)>();
        var taken = new bool[text.Length];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(InfoKind kind, string value, int location, int length)
        {
            if (value.Length == 0) return;
            for (var i = location; i < location + length; i++)
                if (taken[i]) return;
            for (var i = location; i < location + length; i++) taken[i] = true;
            var key = kind switch
            {
                InfoKind.Phone => "phone:" + new string(value.Where(char.IsDigit).ToArray()),
                InfoKind.Ip => "ip:" + value,
                _ => kind + ":" + value.ToLowerInvariant(),
            };
            if (!seen.Add(key)) return;
            found.Add((location, new InfoItem(kind, value)));
        }

        // 邮箱最先找，免得里面的域名被当成网址
        foreach (Match match in EmailRegex().Matches(text))
            Add(InfoKind.Email, match.Value, match.Index, match.Length);
        foreach (Match match in LinkCandidateRegex().Matches(text))
        {
            var value = TrimLink(match.Value);
            if (!IsLikelyLink(value)) continue;
            Add(InfoKind.Link, value, match.Index, value.Length);
        }
        foreach (Match match in Ipv4Regex().Matches(text))
            Add(InfoKind.Ip, match.Value, match.Index, match.Length);
        foreach (Match match in PhoneRegex().Matches(text))
            Add(InfoKind.Phone, match.Value, match.Index, match.Length);
        return found.OrderBy(f => f.Location).Select(f => f.Item).ToList();
    }

    /// 截到第一个结束符，再去掉结尾的标点和多出来的右括号
    private static string TrimLink(string value)
    {
        var cut = value.IndexOfAny(UrlTerminators.ToCharArray());
        if (cut >= 0) value = value[..cut];
        while (value.Length > 0)
        {
            var last = value[^1];
            if (TrailingPunctuation.Contains(last))
            {
                value = value[..^1];
                continue;
            }
            if ((last == ')' && Count(value, '(') < Count(value, ')'))
                || (last == ']' && Count(value, '[') < Count(value, ']')))
            {
                value = value[..^1];
                continue;
            }
            break;
        }
        return value;
    }

    private static int Count(string value, char c) => value.Count(x => x == c);

    /// 带协议的都算；没带协议的要以 www. 开头，或者是常见的后缀；纯 IP 留给 IP 地址
    private static bool IsLikelyLink(string value)
    {
        var lowered = value.ToLowerInvariant();
        if (lowered.Contains("://", StringComparison.Ordinal)) return true;
        var host = lowered.Split('/', 2)[0];
        var hostWithoutPort = host.Split(':', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? host;
        var labels = hostWithoutPort.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.All(l => long.TryParse(l, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _))) return false;
        if (lowered.StartsWith("www.", StringComparison.Ordinal)) return true;
        return labels.Length > 0 && BareDomainSuffixes.Contains(labels[^1]);
    }

    /// 圆盘里要不要显示「提取信息」：找到两项以上，或者找到的那一项不是选中的全部内容
    public static bool IsWorthExtracting(string text)
    {
        var trimmed = text.Trim();
        // 先粗略看一眼，大部分文字不用跑完整的识别；很长的文字只看这一眼
        if (trimmed.Length > MaxLength || !HintRegex().IsMatch(trimmed)) return false;
        if (trimmed.Length > 20_000) return true;
        var items = Extract(trimmed);
        return items.Count >= 2 || (items.Count == 1 && items[0].Value != trimmed);
    }

    /// 结果卡片：每一项一行，按类别编号；Body 是「2 个链接，1 个邮箱」这样的汇总
    public static CardContent Card(IReadOnlyList<InfoItem> items, int limit = 60)
    {
        var lines = new List<ResultLine>();
        var numbers = new Dictionary<InfoKind, int>();
        foreach (var item in items.Take(limit))
        {
            numbers[item.Kind] = numbers.GetValueOrDefault(item.Kind) + 1;
            lines.Add(new ResultLine($"{Title(item.Kind)} {numbers[item.Kind]}", item.Value));
        }
        return new CardContent("提取信息", lines, Summary(items, limit));
    }

    /// 汇总：「2 个链接，1 个邮箱，1 个 IP 地址」；超过 limit 项时说明只列出了前面的
    public static string Summary(IReadOnlyList<InfoItem> items, int limit = 60)
    {
        // 「1 个 IP 地址」：英文前面空一格
        var summary = string.Join("，", Groups(items).Select(group =>
        {
            var title = Title(group.Kind);
            return $"{group.Values.Count} 个" + (char.IsAscii(title[0]) ? " " : "") + title;
        }));
        return items.Count > limit ? $"{summary}（只列出前 {limit} 项，复制全部时包括所有的）" : summary;
    }

    /// 「复制全部」按钮：某一类有两项以上时才有，Label 是按钮文字，Value 是一行一项的全部内容（包括超出 limit 的）
    public static IReadOnlyList<ResultLine> CopyAllActions(IReadOnlyList<InfoItem> items) =>
        Groups(items)
            .Where(group => group.Values.Count > 1)
            .Select(group => new ResultLine($"复制全部{Title(group.Kind)}", string.Join("\n", group.Values)))
            .ToList();

    /// 「打开全部链接」要打开的网址：链接有 2 到 10 个时才给；没写协议的按 https 打开
    public static IReadOnlyList<string> LinksToOpen(IReadOnlyList<InfoItem> items)
    {
        var links = items
            .Where(item => item.Kind == InfoKind.Link)
            .Select(item => item.Value.Contains("://", StringComparison.Ordinal) ? item.Value : "https://" + item.Value)
            .Where(url => Uri.TryCreate(url, UriKind.Absolute, out _))
            .ToList();
        return links.Count is >= 2 and <= 10 ? links : [];
    }

    private static IEnumerable<(InfoKind Kind, List<string> Values)> Groups(IReadOnlyList<InfoItem> items) =>
        AllKinds
            .Select(kind => (Kind: kind, Values: items.Where(i => i.Kind == kind).Select(i => i.Value).ToList()))
            .Where(group => group.Values.Count > 0);
}
