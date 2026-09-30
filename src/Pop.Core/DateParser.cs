using System.Globalization;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 识别常见的日期时间写法，列出各种时间表示
public static class DateParser
{
    /// 带时区的 ISO 8601：2024-09-28T08:00:00Z、2024-09-28T16:00:00.123+08:00
    private static readonly Regex IsoPattern = new(
        @"^([0-9]{4})-([0-9]{2})-([0-9]{2})T([0-9]{2}):([0-9]{2}):([0-9]{2})(?:\.([0-9]+))?(Z|[+-][0-9]{2}:?[0-9]{2})\z",
        RegexOptions.CultureInvariant);

    /// 不带时区的写法，按传入的时区理解：
    /// 2026-09-28、2026-09-28 14:30、2026-09-28 14:30:00.123、2026-09-28T14:30:00，
    /// 2026/09/28 14:30:00、2026.09.28 14:30，2026年9月28日 14:30、2026年9月28日14时30分
    private static readonly Regex[] LocalPatterns =
    [
        new(@"^(?<y>[0-9]{4})-(?<M>[0-9]{1,2})-(?<d>[0-9]{1,2})(?:(?: (?<H>[0-9]{1,2}):(?<m>[0-9]{1,2})(?::(?<s>[0-9]{1,2})(?:\.(?<f>[0-9]{1,3}))?)?)|(?:T(?<H>[0-9]{1,2}):(?<m>[0-9]{1,2})(?::(?<s>[0-9]{1,2}))?))?\z",
            RegexOptions.CultureInvariant),
        new(@"^(?<y>[0-9]{4})(?<sep>[/.])(?<M>[0-9]{1,2})\k<sep>(?<d>[0-9]{1,2})(?: (?<H>[0-9]{1,2}):(?<m>[0-9]{1,2})(?::(?<s>[0-9]{1,2}))?)?\z",
            RegexOptions.CultureInvariant),
        new(@"^(?<y>[0-9]{4})年(?<M>[0-9]{1,2})月(?<d>[0-9]{1,2})日(?:(?: (?<H>[0-9]{1,2}):(?<m>[0-9]{1,2})(?::(?<s>[0-9]{1,2}))?)|(?:(?<H>[0-9]{1,2})时(?<m>[0-9]{1,2})分(?:(?<s>[0-9]{1,2})秒)?))?\z",
            RegexOptions.CultureInvariant),
    ];

    private const string AllowedCharacters = "0123456789-/.: 年月日时分秒TZ+";

    /// 1900-01-01 ~ 2200-01-01
    private static readonly DateTimeOffset Earliest = new(1900, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Latest = new(2200, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// 识别常见的日期时间写法；没写时区的按 timeZone 理解（不传时用本机时区）。不是日期时返回 null
    public static DateTimeOffset? Parse(string text, TimeZoneInfo? timeZone = null)
    {
        var trimmed = text.Trim();
        if (trimmed.Length is < 8 or > 40 || !char.IsAsciiDigit(trimmed[0]) ||
            !trimmed.All(c => AllowedCharacters.Contains(c)) ||
            !trimmed.Any(c => "-/.年".Contains(c)))
        {
            return null;
        }
        if (trimmed.Contains('T') &&
            (trimmed.EndsWith('Z') || trimmed.Contains('+') || (trimmed.Length > 10 && trimmed[10..].Contains('-'))) &&
            ParseIso(trimmed) is { } iso && IsPlausible(iso))
        {
            return iso;
        }
        var zone = timeZone ?? TimeZoneInfo.Local;
        foreach (var pattern in LocalPatterns)
        {
            var match = pattern.Match(trimmed);
            if (!match.Success) continue;
            if (LocalDateTime(match) is { } local && IsPlausible(FromLocal(local, zone)))
            {
                return FromLocal(local, zone);
            }
        }
        return null;
    }

    private static int Field(Match match, string name) =>
        match.Groups[name].Success ? int.Parse(match.Groups[name].Value, CultureInfo.InvariantCulture) : 0;

    /// 各项都在合法范围里才组成日期（2026-02-30、25:00 都不行）
    private static DateTime? Compose(int year, int month, int day, int hour, int minute, int second, long ticks)
    {
        if (year < 1 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month) ||
            hour > 23 || minute > 59 || second > 59)
        {
            return null;
        }
        return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified).AddTicks(ticks);
    }

    /// 小数秒换成 ticks：".5" 是 500 毫秒，超过 7 位的部分舍去
    private static long FractionTicks(string digits)
    {
        if (digits.Length == 0) return 0;
        var padded = (digits.Length > 7 ? digits[..7] : digits).PadRight(7, '0');
        return long.Parse(padded, CultureInfo.InvariantCulture);
    }

    private static DateTime? LocalDateTime(Match match) =>
        Compose(Field(match, "y"), Field(match, "M"), Field(match, "d"), Field(match, "H"), Field(match, "m"),
            Field(match, "s"), FractionTicks(match.Groups["f"].Value));

    private static DateTimeOffset? ParseIso(string text)
    {
        var match = IsoPattern.Match(text);
        if (!match.Success) return null;
        int Group(int index) => int.Parse(match.Groups[index].Value, CultureInfo.InvariantCulture);
        if (Compose(Group(1), Group(2), Group(3), Group(4), Group(5), Group(6), FractionTicks(match.Groups[7].Value))
            is not { } local)
        {
            return null;
        }
        var zone = match.Groups[8].Value;
        var offset = TimeSpan.Zero;
        if (zone != "Z")
        {
            var digits = zone[1..].Replace(":", "", StringComparison.Ordinal);
            var hours = int.Parse(digits[..2], CultureInfo.InvariantCulture);
            var minutes = int.Parse(digits[2..], CultureInfo.InvariantCulture);
            if (hours > 14 || minutes > 59) return null;
            offset = new TimeSpan(hours, minutes, 0);
            if (zone[0] == '-') offset = -offset;
        }
        return new DateTimeOffset(local, offset);
    }

    /// 某个时区里的当地时间换成绝对时刻。夏令时跳过的那段时间按跳过前的时差算，也就是往后顺延
    internal static DateTimeOffset FromLocal(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        var offset = zone.IsInvalidTime(unspecified)
            ? zone.GetUtcOffset(unspecified.AddHours(-3))
            : zone.GetUtcOffset(unspecified);
        return new DateTimeOffset(unspecified, offset);
    }

    private static bool IsPlausible(DateTimeOffset date) => date >= Earliest && date <= Latest;

    /// 时间转换卡片上的各行：本地时间、UTC、Unix 秒和毫秒、距今、星期、农历、第几周，再加几个常用城市的当地时间。
    /// timeZone 是「本地」的时区，now 是算「距今」用的当前时间；不传时用本机时区和当前时间
    public static IReadOnlyList<ResultLine> Rows(DateTimeOffset date, TimeZoneInfo? timeZone = null, DateTimeOffset? now = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var ticks = date.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks;
        var seconds = (long)Math.Floor(ticks / (decimal)TimeSpan.TicksPerSecond);
        var milliseconds = (long)Math.Round(ticks / (decimal)TimeSpan.TicksPerMillisecond, MidpointRounding.AwayFromZero);
        var local = TimeZoneInfo.ConvertTime(date, zone);
        var rows = new List<ResultLine>
        {
            new("本地时间", TimestampConverter.LocalString(date, zone)),
            new("UTC", TimestampConverter.IsoString(date)),
            new("Unix 秒", seconds.ToString(CultureInfo.InvariantCulture)),
            new("Unix 毫秒", milliseconds.ToString(CultureInfo.InvariantCulture)),
            new("距今", Relative(date, now ?? DateTimeOffset.Now)),
            new("星期", Weekdays[(int)local.DayOfWeek]),
        };
        if (LunarCalendar.Describe(date, zone) is { } lunar)
        {
            rows.Add(new ResultLine("农历", lunar));
        }
        rows.Add(new ResultLine("第几周", LunarCalendar.WeekAndDay(date, zone)));
        // 几个常用城市的当地时间（和本地时区相同的不重复列）
        foreach (var (city, identifier) in WorldClocks)
        {
            if (IsSameZone(zone, identifier) || FindZone(identifier) is not { } cityZone) continue;
            rows.Add(new ResultLine(city,
                TimeZoneInfo.ConvertTime(date, cityZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
        }
        return rows;
    }

    private static readonly string[] Weekdays = ["星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"];

    /// 时间转换卡片上列出的城市（IANA 时区名）
    public static readonly IReadOnlyList<(string City, string Zone)> WorldClocks =
    [
        ("北京", "Asia/Shanghai"),
        ("东京", "Asia/Tokyo"),
        ("伦敦", "Europe/London"),
        ("纽约", "America/New_York"),
        ("旧金山", "America/Los_Angeles"),
    ];

    /// 按 IANA 时区名找时区；Windows 上找不到时换成 Windows 的时区名再找
    private static TimeZoneInfo? FindZone(string ianaId)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var zone)) return zone;
        return TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId) &&
               TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone)
            ? zone
            : null;
    }

    private static bool IsSameZone(TimeZoneInfo zone, string ianaId)
    {
        if (zone.Id == ianaId) return true;
        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId) && windowsId == zone.Id) return true;
        return TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out var converted) && converted == ianaId;
    }

    /// 「3 天前」「2 小时后」：取最大的一个单位
    public static string Relative(DateTimeOffset date, DateTimeOffset now)
    {
        var difference = date - now;
        var past = difference < TimeSpan.Zero;
        var span = past ? -difference : difference;
        var days = span.TotalDays;
        var (count, unit) = span switch
        {
            _ when days >= 365 => ((long)(days / 365.2425), "年"),
            _ when days >= 30 => ((long)(days / 30.436875), "个月"),
            _ when days >= 7 => ((long)(days / 7), "周"),
            _ when days >= 1 => ((long)days, "天"),
            _ when span.TotalHours >= 1 => ((long)span.TotalHours, "小时"),
            _ when span.TotalMinutes >= 1 => ((long)span.TotalMinutes, "分钟"),
            _ => ((long)span.TotalSeconds, "秒钟"),
        };
        return count.ToString(CultureInfo.InvariantCulture) + unit + (past ? "前" : "后");
    }
}
