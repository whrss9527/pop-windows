using System.Globalization;

namespace Pop.Core;

/// Unix 时间戳识别与格式化。
public static class TimestampConverter
{
    /// 10 位（秒）或 13 位（毫秒）纯数字，且落在 2001-09-09 ~ 2100-01-01 之间才认为是时间戳；否则返回 null
    public static DateTimeOffset? Parse(string text)
    {
        if (text.Length is not (10 or 13) || !text.All(char.IsAsciiDigit)) return null;
        var value = long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
        var milliseconds = text.Length == 13 ? value : value * 1000;
        if (milliseconds < 1_000_000_000_000 || milliseconds >= 4_102_444_800_000) return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

    /// 某个时区的当地时间：2024-09-28 08:00:00（不传时区时用本机时区）
    public static string LocalString(DateTimeOffset date, TimeZoneInfo? timeZone = null) =>
        TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local)
            .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// UTC 的 ISO 8601 写法：2024-09-28T08:00:00Z
    public static string IsoString(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
