namespace Pop.Core.Tests;

public class DateParserTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    /// 东八区，不依赖系统时区数据库；Id 用 IANA 名，这样「北京」一行会被当成本地时区
    private static readonly TimeZoneInfo Shanghai =
        TimeZoneInfo.CreateCustomTimeZone("Asia/Shanghai", TimeSpan.FromHours(8), "Asia/Shanghai", "Asia/Shanghai");

    private static readonly TimeZoneInfo PlusEight =
        TimeZoneInfo.CreateCustomTimeZone("Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");

    private static string? Value(IReadOnlyList<ResultLine> rows, string label) =>
        rows.FirstOrDefault(row => row.Label == label)?.Value;

    private static long? Seconds(string text, TimeZoneInfo? zone = null) =>
        DateParser.Parse(text, zone)?.ToUnixTimeSeconds();

    [Fact]
    public void ParsesCommonFormats()
    {
        Assert.Equal(1_790_605_800, Seconds("2026-09-28 14:30", Utc));
        Assert.Equal(1_790_553_600, Seconds("2026-09-28", Utc));
        Assert.Equal(1_790_605_800, Seconds("2026/09/28 14:30:00", Utc));
        Assert.Equal(1_790_605_800, Seconds("2026.09.28 14:30", Utc));
        Assert.Equal(1_790_605_800, Seconds("2026-09-28T14:30:00", Utc));
        Assert.Equal(1_790_553_600, Seconds("2026年9月28日", Utc));
        Assert.Equal(1_790_605_800, Seconds("2026年9月28日14时30分", Utc));
        Assert.Equal(1_790_605_800 - 8 * 3600, Seconds("2026-09-28 14:30", PlusEight));
        Assert.Equal(1_790_605_800_123, DateParser.Parse("2026-09-28 14:30:00.123", Utc)?.ToUnixTimeMilliseconds());
        // 带时区的 ISO 8601 不看传入的时区
        Assert.Equal(1_727_510_400, Seconds("2024-09-28T08:00:00Z", PlusEight));
        Assert.Equal(1_727_510_400, Seconds("2024-09-28T16:00:00+08:00", Utc));
        Assert.Equal(1_727_510_400, Seconds("2024-09-28T04:00:00.000-04:00", Utc));

        Assert.Null(DateParser.Parse("12345678"));
        Assert.Null(DateParser.Parse("hello world"));
        Assert.Null(DateParser.Parse("2026-13-45"));
        Assert.Null(DateParser.Parse("2026-02-30"));
        Assert.Null(DateParser.Parse("1800-01-01", Utc));
        Assert.Null(DateParser.Parse("2026-9-1T"));
    }

    [Fact]
    public void RowsForADate()
    {
        var date = DateTimeOffset.FromUnixTimeSeconds(1_727_510_400);
        var rows = DateParser.Rows(date, Utc, date.AddDays(3));
        Assert.Equal("2024-09-28 08:00:00", Value(rows, "本地时间"));
        Assert.Equal("2024-09-28T08:00:00Z", Value(rows, "UTC"));
        Assert.Equal("1727510400", Value(rows, "Unix 秒"));
        Assert.Equal("1727510400000", Value(rows, "Unix 毫秒"));
        Assert.Equal("星期六", Value(rows, "星期"));
        Assert.Equal("3天前", Value(rows, "距今"));
        Assert.Equal("第 39 周 · 全年第 272 天", Value(rows, "第几周"));
        // 常用城市的当地时间（9 月纽约是夏令时 UTC-4）
        Assert.Equal("2024-09-28 16:00", Value(rows, "北京"));
        Assert.Equal("2024-09-28 04:00", Value(rows, "纽约"));
        Assert.Null(Value(DateParser.Rows(date, Shanghai, date), "北京"));
    }

    [Fact]
    public void RelativeTime()
    {
        var now = DateTimeOffset.FromUnixTimeSeconds(1_727_510_400);
        Assert.Equal("2小时后", DateParser.Relative(now.AddHours(2.5), now));
        Assert.Equal("5分钟前", DateParser.Relative(now.AddMinutes(-5), now));
        Assert.Equal("2周后", DateParser.Relative(now.AddDays(15), now));
        Assert.Equal("3个月前", DateParser.Relative(now.AddDays(-95), now));
        Assert.Equal("1年后", DateParser.Relative(now.AddDays(400), now));
    }
}

public class TimestampConverterTests
{
    [Fact]
    public void ParsesSecondsAndMilliseconds()
    {
        var date = TimestampConverter.Parse("1727510400");
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_727_510_400), date);
        Assert.Equal("2024-09-28 08:00:00", TimestampConverter.LocalString(date!.Value, TimeZoneInfo.Utc));
        Assert.Equal(1_727_510_400_123, TimestampConverter.Parse("1727510400123")?.ToUnixTimeMilliseconds());
        Assert.Null(TimestampConverter.Parse("17275104a0"));
        // 太早、太晚、位数不对
        Assert.Null(TimestampConverter.Parse("0999999999"));
        Assert.Null(TimestampConverter.Parse("4102444800"));
        Assert.Null(TimestampConverter.Parse("17275104001"));
        Assert.Equal("2024-09-28T08:00:00Z", TimestampConverter.IsoString(date.Value));
    }
}

public class LunarCalendarTests
{
    private static readonly TimeZoneInfo Shanghai =
        TimeZoneInfo.CreateCustomTimeZone("Test+8", TimeSpan.FromHours(8), "Test+8", "Test+8");

    private static DateTimeOffset Date(string text) => DateParser.Parse(text, Shanghai)!.Value;

    [Fact]
    public void LunarDates()
    {
        Assert.Equal("丙午年（马年）八月十五 · 中秋节", LunarCalendar.Describe(Date("2026-09-25 12:00"), Shanghai));
        Assert.Equal("丙午年（马年）八月十九", LunarCalendar.Describe(Date("2026-09-29 12:00"), Shanghai));
        Assert.Equal("丙午年（马年）正月初一 · 春节", LunarCalendar.Describe(Date("2026-02-17 08:00"), Shanghai));
        Assert.Equal("除夕", LunarCalendar.Festival(Date("2026-02-16 20:00"), Shanghai));
        Assert.Equal("乙巳年（蛇年）正月初一 · 春节", LunarCalendar.Describe(Date("2025-01-29 08:00"), Shanghai));
        // 2025 年闰六月
        Assert.Equal("乙巳年（蛇年）闰六月初一", LunarCalendar.Describe(Date("2025-07-25 12:00"), Shanghai));
        Assert.Equal("乙巳年（蛇年）七月初一", LunarCalendar.Describe(Date("2025-08-23 12:00"), Shanghai));
        Assert.Equal("第 40 周 · 全年第 272 天", LunarCalendar.WeekAndDay(Date("2026-09-29 12:00"), Shanghai));
        // 同一时刻在不同时区可能是不同的日子
        Assert.Equal("丙午年（马年）八月十四", LunarCalendar.Describe(Date("2026-09-25 02:00"), TimeZoneInfo.Utc));
    }

    [Fact]
    public void DayNames()
    {
        Assert.Equal(["初一", "初十", "十五", "二十", "廿一", "三十"],
            new[] { 1, 10, 15, 20, 21, 30 }.Select(LunarCalendar.DayName));
    }

    [Fact]
    public void TimeCardShowsLunarDate()
    {
        var rows = DateParser.Rows(Date("2024-09-28 16:00"), Shanghai);
        Assert.Equal("甲辰年（龙年）八月廿六", rows.FirstOrDefault(row => row.Label == "农历")?.Value);
        Assert.Contains(rows, row => row.Label == "第几周");
    }
}
