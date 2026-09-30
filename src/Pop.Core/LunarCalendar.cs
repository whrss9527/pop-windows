using System.Globalization;

namespace Pop.Core;

/// 农历日期（干支纪年、生肖、月、日）和传统节日。纯逻辑，方便测试。
/// 用系统自带的农历历法，能算 1901 年到 2100 年之间的日子，超出范围时返回 null。
public static class LunarCalendar
{
    private static readonly string[] Stems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
    private static readonly string[] Branches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];
    private static readonly string[] Zodiacs = ["鼠", "牛", "虎", "兔", "龙", "蛇", "马", "羊", "猴", "鸡", "狗", "猪"];
    private static readonly string[] Months =
        ["正月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "冬月", "腊月"];

    private static readonly Dictionary<(int Month, int Day), string> Festivals = new()
    {
        [(1, 1)] = "春节", [(1, 15)] = "元宵节", [(5, 5)] = "端午节", [(7, 7)] = "七夕", [(7, 15)] = "中元节",
        [(8, 15)] = "中秋节", [(9, 9)] = "重阳节", [(12, 8)] = "腊八节",
    };

    private static readonly ChineseLunisolarCalendar Chinese = new();

    /// 农历日期
    /// <param name="CycleYear">六十甲子里的第几年（1 是甲子）</param>
    /// <param name="Month">1–12，闰月和它前面的月份同号</param>
    /// <param name="Day">1–30</param>
    /// <param name="IsLeapMonth">是不是闰月</param>
    public sealed record LunarDate(int CycleYear, int Month, int Day, bool IsLeapMonth);

    /// 某个时刻在某个时区里是农历哪一天
    public static LunarDate? Date(DateTimeOffset date, TimeZoneInfo? timeZone = null) =>
        Date(TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local).Date);

    private static LunarDate? Date(DateTime day)
    {
        if (day < Chinese.MinSupportedDateTime || day > Chinese.MaxSupportedDateTime) return null;
        var year = Chinese.GetYear(day);
        var month = Chinese.GetMonth(day);
        var leapMonth = Chinese.GetLeapMonth(year);
        var isLeap = leapMonth > 0 && month == leapMonth;
        // 有闰月的年份一共 13 个月，闰月及以后的月份序号要减一
        if (leapMonth > 0 && month >= leapMonth) month -= 1;
        return new LunarDate(Chinese.GetSexagenaryYear(day), month, Chinese.GetDayOfMonth(day), isLeap);
    }

    /// 初一、十五、廿一、三十
    public static string DayName(int day)
    {
        string[] digits = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];
        return day switch
        {
            >= 1 and <= 10 => "初" + digits[day],
            >= 11 and <= 19 => "十" + digits[day - 10],
            20 => "二十",
            >= 21 and <= 29 => "廿" + digits[day - 20],
            30 => "三十",
            _ => "",
        };
    }

    /// 「丙午年（马年）八月十五 · 中秋节」
    public static string? Describe(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var day = TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local).Date;
        if (Date(day) is not { } lunar) return null;
        var index = lunar.CycleYear - 1;
        var text = Stems[index % 10] + Branches[index % 12] + "年（" + Zodiacs[index % 12] + "年）"
            + (lunar.IsLeapMonth ? "闰" : "") + Months[lunar.Month - 1] + DayName(lunar.Day);
        if (Festival(day) is { } name)
        {
            text += " · " + name;
        }
        return text;
    }

    /// 传统节日，不是节日时返回 null
    public static string? Festival(DateTimeOffset date, TimeZoneInfo? timeZone = null) =>
        Festival(TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local).Date);

    private static string? Festival(DateTime day)
    {
        if (Date(day) is not { } lunar) return null;
        if (!lunar.IsLeapMonth && Festivals.TryGetValue((lunar.Month, lunar.Day), out var name))
        {
            return name;
        }
        // 除夕：第二天是正月初一
        if (lunar.Month == 12 && day < DateTime.MaxValue.Date &&
            Date(day.AddDays(1)) is { Month: 1, Day: 1, IsLeapMonth: false })
        {
            return "除夕";
        }
        return null;
    }

    /// 「第 40 周 · 全年第 272 天」（按 ISO 8601，周一是一周的第一天）
    public static string WeekAndDay(DateTimeOffset date, TimeZoneInfo? timeZone = null)
    {
        var local = TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local).DateTime;
        return $"第 {ISOWeek.GetWeekOfYear(local).ToString(CultureInfo.InvariantCulture)} 周 · 全年第 {local.DayOfYear.ToString(CultureInfo.InvariantCulture)} 天";
    }
}
