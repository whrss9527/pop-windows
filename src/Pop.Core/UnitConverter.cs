using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Pop.Core;

/// 单位的类别
public enum UnitCategory
{
    Length,
    Mass,
    Temperature,
    Volume,
    Area,
    Speed,
    DataSize,
    DataRate,
}

/// 一个单位。Factor 是 1 个这个单位等于多少个基本单位：长度是米，重量是千克，体积是升，面积是平方米，
/// 速度是米/秒，数据大小是字节，传输速率是比特/秒。温度另外换算。
public sealed record UnitSpec(string Id, UnitCategory Category, string Symbol, string Name, double Factor)
{
    /// 识别用的写法，不区分大小写
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// 识别用的写法，区分大小写（MB 和 Mb 不是一回事）
    public IReadOnlyList<string> ExactAliases { get; init; } = [];

    /// 换算结果里列出这个单位（不常用的单位只在选中它的时候出现）
    public bool Listed { get; init; } = true;
}

/// 识别出来的带单位的数值
public sealed record Measurement(double Value, UnitSpec Unit);

/// 带单位的数值：识别（5 km、100°F、2 斤、6'2"、1 TB、100 Mbps……）和换算。
public static partial class UnitConverter
{
    // 单位表

    /// 同一类里的顺序就是换算结果的顺序：公制在前，英制其次，市制最后。
    public static readonly IReadOnlyList<UnitSpec> Units =
    [
        // 长度（米）
        U("mm", UnitCategory.Length, "mm", "毫米", 0.001, ["mm", "毫米"]),
        U("cm", UnitCategory.Length, "cm", "厘米", 0.01, ["cm", "厘米", "公分"]),
        U("m", UnitCategory.Length, "m", "米", 1, ["米", "公尺", "meter", "meters", "metre", "metres"], exact: ["m"]),
        U("km", UnitCategory.Length, "km", "千米", 1000,
            ["km", "千米", "公里", "kilometer", "kilometers", "kilometre", "kilometres"]),
        U("in", UnitCategory.Length, "in", "英寸", 0.0254, ["in", "inch", "inches", "英寸", "\"", "″"]),
        U("ft", UnitCategory.Length, "ft", "英尺", 0.3048, ["ft", "foot", "feet", "英尺", "'", "′"]),
        U("yd", UnitCategory.Length, "yd", "码", 0.9144, ["yd", "yard", "yards"], listed: false),
        U("mi", UnitCategory.Length, "mi", "英里", 1609.344, ["mi", "mile", "miles", "英里"]),
        U("nmi", UnitCategory.Length, "nmi", "海里", 1852, ["nmi", "海里", "nautical mile", "nautical miles"]),
        U("chi", UnitCategory.Length, "尺", "尺（市尺）", 1.0 / 3.0, ["尺", "市尺"]),
        U("li", UnitCategory.Length, "里", "里（市里）", 500, ["里", "市里", "华里"]),

        // 重量（千克）
        U("mg", UnitCategory.Mass, "mg", "毫克", 0.000_001, ["mg", "毫克"], listed: false),
        U("g", UnitCategory.Mass, "g", "克", 0.001, ["克", "gram", "grams"], exact: ["g"]),
        U("kg", UnitCategory.Mass, "kg", "千克", 1, ["kg", "千克", "公斤", "kilogram", "kilograms", "kilo", "kilos"]),
        U("t", UnitCategory.Mass, "t", "吨", 1000, ["吨", "公吨", "tonne", "tonnes"], exact: ["t"]),
        U("lb", UnitCategory.Mass, "lb", "磅", 0.453_592_37, ["lb", "lbs", "pound", "pounds", "磅"]),
        U("oz", UnitCategory.Mass, "oz", "盎司", 0.028_349_523_125, ["oz", "ounce", "ounces", "盎司"]),
        U("ct", UnitCategory.Mass, "ct", "克拉", 0.0002, ["ct", "carat", "carats", "克拉"], listed: false),
        U("jin", UnitCategory.Mass, "斤", "斤", 0.5, ["斤", "市斤"]),
        U("liang", UnitCategory.Mass, "两", "两", 0.05, ["两", "市两"]),

        // 温度
        U("C", UnitCategory.Temperature, "°C", "摄氏度", 1, ["°c", "℃", "摄氏度", "celsius"]),
        U("F", UnitCategory.Temperature, "°F", "华氏度", 1, ["°f", "℉", "华氏度", "fahrenheit"]),
        U("K", UnitCategory.Temperature, "K", "开尔文", 1, ["开尔文", "kelvin"]),

        // 体积（升）
        U("mL", UnitCategory.Volume, "mL", "毫升", 0.001, ["ml", "毫升", "cc", "cm³", "cm3", "立方厘米"]),
        U("L", UnitCategory.Volume, "L", "升", 1, ["l", "升", "公升", "liter", "liters", "litre", "litres"]),
        U("m3", UnitCategory.Volume, "m³", "立方米", 1000, ["m³", "m3", "立方米"]),
        U("gal", UnitCategory.Volume, "gal", "加仑（美）", 3.785_411_784, ["gal", "gallon", "gallons", "加仑"]),
        U("qt", UnitCategory.Volume, "qt", "夸脱", 0.946_352_946, ["qt", "quart", "quarts", "夸脱"], listed: false),
        U("pint", UnitCategory.Volume, "pt", "品脱", 0.473_176_473, ["pint", "pints", "品脱"], listed: false),
        U("cup", UnitCategory.Volume, "cup", "杯（美）", 0.236_588_236_5, ["cup", "cups"], listed: false),
        U("floz", UnitCategory.Volume, "fl oz", "液量盎司", 0.029_573_529_562_5,
            ["fl oz", "floz", "fl. oz", "fl.oz", "液量盎司"]),
        U("tbsp", UnitCategory.Volume, "tbsp", "汤匙", 0.014_786_764_781_25, ["tbsp", "汤匙"], listed: false),
        U("tsp", UnitCategory.Volume, "tsp", "茶匙", 0.004_928_921_593_75, ["tsp", "茶匙"], listed: false),

        // 面积（平方米）
        U("cm2", UnitCategory.Area, "cm²", "平方厘米", 0.0001, ["cm²", "cm2", "平方厘米"], listed: false),
        U("m2", UnitCategory.Area, "m²", "平方米", 1, ["m²", "m2", "㎡", "平方米", "平米", "sqm"]),
        U("km2", UnitCategory.Area, "km²", "平方千米", 1_000_000, ["km²", "km2", "㎢", "平方千米", "平方公里"]),
        U("ha", UnitCategory.Area, "ha", "公顷", 10_000, ["ha", "公顷", "hectare", "hectares"]),
        U("ft2", UnitCategory.Area, "ft²", "平方英尺", 0.092_903_04, ["ft²", "ft2", "sq ft", "sqft", "平方英尺"]),
        U("in2", UnitCategory.Area, "in²", "平方英寸", 0.000_645_16, ["in²", "in2", "sq in", "平方英寸"], listed: false),
        U("acre", UnitCategory.Area, "acre", "英亩", 4046.856_422_4, ["acre", "acres", "英亩"]),
        U("mi2", UnitCategory.Area, "mi²", "平方英里", 2_589_988.110_336, ["mi²", "mi2", "sq mi", "平方英里"]),
        U("mu", UnitCategory.Area, "亩", "亩", 10_000.0 / 15.0, ["亩", "市亩"]),

        // 速度（米/秒）
        U("mps", UnitCategory.Speed, "m/s", "米/秒", 1, ["m/s", "米/秒", "米每秒"]),
        U("kmh", UnitCategory.Speed, "km/h", "千米/时", 1000.0 / 3600.0,
            ["km/h", "kmh", "kph", "km/hr", "公里/小时", "公里每小时", "千米/小时", "千米每小时", "千米/时", "公里/时"]),
        U("mph", UnitCategory.Speed, "mph", "英里/时", 0.447_04, ["mph", "mi/h", "英里/小时", "英里每小时", "英里/时"]),
        U("kn", UnitCategory.Speed, "kn", "节", 1852.0 / 3600.0, ["kn", "knot", "knots"]),

        // 数据大小（字节）。全小写的 kb、mb、gb 按字节算；Kb、Mb、Gb 是比特
        U("bit", UnitCategory.DataSize, "bit", "比特", 0.125, ["bit", "bits", "比特"], listed: false),
        U("B", UnitCategory.DataSize, "B", "字节", 1, ["byte", "bytes", "字节"], exact: ["B"]),
        U("KB", UnitCategory.DataSize, "KB", "KB", 1e3, exact: ["KB", "kB", "kb"]),
        U("MB", UnitCategory.DataSize, "MB", "MB", 1e6, exact: ["MB", "mb"]),
        U("GB", UnitCategory.DataSize, "GB", "GB", 1e9, exact: ["GB", "gb"]),
        U("TB", UnitCategory.DataSize, "TB", "TB", 1e12, exact: ["TB", "tb"]),
        U("PB", UnitCategory.DataSize, "PB", "PB", 1e15, exact: ["PB", "pb"], listed: false),
        U("KiB", UnitCategory.DataSize, "KiB", "KiB", 1024, ["kib"]),
        U("MiB", UnitCategory.DataSize, "MiB", "MiB", 1_048_576, ["mib"]),
        U("GiB", UnitCategory.DataSize, "GiB", "GiB", 1_073_741_824, ["gib"]),
        U("TiB", UnitCategory.DataSize, "TiB", "TiB", 1_099_511_627_776, ["tib"]),
        U("Kb", UnitCategory.DataSize, "Kb", "Kb（千比特）", 125, ["kbit"], exact: ["Kb"], listed: false),
        U("Mb", UnitCategory.DataSize, "Mb", "Mb（兆比特）", 125_000, ["mbit"], exact: ["Mb"], listed: false),
        U("Gb", UnitCategory.DataSize, "Gb", "Gb（吉比特）", 125_000_000, ["gbit"], exact: ["Gb"], listed: false),

        // 传输速率（比特/秒）。带 ps 的是比特，/s 前面是 B 的是字节
        U("bps", UnitCategory.DataRate, "bps", "bps", 1, ["bps", "bit/s"], listed: false),
        U("Kbps", UnitCategory.DataRate, "Kbps", "Kbps", 1e3, ["kbps", "kbit/s"], exact: ["Kb/s"]),
        U("Mbps", UnitCategory.DataRate, "Mbps", "Mbps", 1e6, ["mbps", "mbit/s"], exact: ["Mb/s"]),
        U("Gbps", UnitCategory.DataRate, "Gbps", "Gbps", 1e9, ["gbps", "gbit/s"], exact: ["Gb/s"]),
        U("KBps", UnitCategory.DataRate, "KB/s", "KB/s", 8e3, exact: ["KB/s", "kB/s", "kb/s"]),
        U("MBps", UnitCategory.DataRate, "MB/s", "MB/s", 8e6, exact: ["MB/s", "mb/s"]),
        U("GBps", UnitCategory.DataRate, "GB/s", "GB/s", 8e9, exact: ["GB/s", "gb/s"]),
        U("MiBps", UnitCategory.DataRate, "MiB/s", "MiB/s", 8_388_608, ["mib/s"], listed: false),
    ];

    private static UnitSpec U(string id, UnitCategory category, string symbol, string name, double factor,
        string[]? aliases = null, string[]? exact = null, bool listed = true) =>
        new(id, category, symbol, name, factor)
        {
            Aliases = aliases ?? [],
            ExactAliases = exact ?? [],
            Listed = listed,
        };

    /// 写法 → 单位。区分大小写的写法优先。
    private static readonly Dictionary<string, UnitSpec> ExactLookup = BuildLookup(u => u.ExactAliases, a => a);

    private static readonly Dictionary<string, UnitSpec> Lookup =
        BuildLookup(u => u.Aliases, a => a.ToLowerInvariant());

    private static Dictionary<string, UnitSpec> BuildLookup(Func<UnitSpec, IReadOnlyList<string>> aliases,
        Func<string, string> key)
    {
        var table = new Dictionary<string, UnitSpec>(StringComparer.Ordinal);
        foreach (var unit in Units)
        {
            foreach (var alias in aliases(unit))
            {
                table.TryAdd(key(alias), unit);
            }
        }
        return table;
    }

    /// 类别的中文名，结果卡片的说明里用
    public static string Title(this UnitCategory category) => category switch
    {
        UnitCategory.Length => "长度",
        UnitCategory.Mass => "重量",
        UnitCategory.Temperature => "温度",
        UnitCategory.Volume => "体积",
        UnitCategory.Area => "面积",
        UnitCategory.Speed => "速度",
        UnitCategory.DataSize => "数据大小",
        UnitCategory.DataRate => "传输速率",
        _ => "",
    };

    public static UnitSpec? Unit(string id) => Units.FirstOrDefault(u => u.Id == id);

    // 识别

    [GeneratedRegex(@"^([+-]?(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d+)?|[+-]?\.\d+)\s*(\S.*)$")]
    private static partial Regex QuantityPattern();

    /// 英尺加英寸，比如 5'11"、6′2″
    [GeneratedRegex(@"^(\d{1,2})\s*['′]\s*(\d{1,2}(?:\.\d+)?)\s*(?:[""″]|'')?$")]
    private static partial Regex FeetInchesPattern();

    /// 识别带单位的数值，不是的话返回 null
    public static Measurement? Parse(string text)
    {
        var trimmed = text.Trim().Replace('−', '-');
        if (trimmed.Length == 0 || new StringInfo(trimmed).LengthInTextElements > 40 || trimmed.Any(IsNewline))
        {
            return null;
        }
        var feetInches = FeetInchesPattern().Match(trimmed);
        if (feetInches.Success
            && TryParseNumber(feetInches.Groups[1].Value, out var feet)
            && TryParseNumber(feetInches.Groups[2].Value, out var inches)
            && inches < 12 && Unit("ft") is { } foot)
        {
            return new Measurement(feet + inches / 12, foot);
        }
        var match = QuantityPattern().Match(trimmed);
        if (!match.Success
            || !TryParseNumber(match.Groups[1].Value.Replace(",", ""), out var value)
            || UnitNamed(match.Groups[2].Value) is not { } spec)
        {
            return null;
        }
        // 只有温度可以是负数
        if (value < 0 && spec.Category != UnitCategory.Temperature) return null;
        return new Measurement(value, spec);
    }

    /// 单位的写法：去掉「/」两边的空格，多个空格算一个
    public static UnitSpec? UnitNamed(string raw)
    {
        var collapsed = string.Join(' ', raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
            .Replace(" / ", "/")
            .Replace(" /", "/")
            .Replace("/ ", "/");
        if (collapsed.Length == 0) return null;
        if (ExactLookup.TryGetValue(collapsed, out var unit)) return unit;
        return Lookup.GetValueOrDefault(collapsed.ToLowerInvariant());
    }

    private static bool IsNewline(char c) => c is '\n' or '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029';

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);

    // 换算

    /// 换算成同一类的另一个单位，类别不同返回 null
    public static double? Convert(Measurement measurement, UnitSpec target)
    {
        if (measurement.Unit.Category != target.Category) return null;
        if (measurement.Unit.Category == UnitCategory.Temperature)
        {
            var celsius = measurement.Unit.Id switch
            {
                "F" => (measurement.Value - 32) * 5 / 9,
                "K" => measurement.Value - 273.15,
                _ => measurement.Value,
            };
            return target.Id switch
            {
                "F" => celsius * 9 / 5 + 32,
                "K" => celsius + 273.15,
                _ => celsius,
            };
        }
        return measurement.Value * measurement.Unit.Factor / target.Factor;
    }

    /// 换算结果：同一类里其他常用单位，数值太大或太小（不好读）的不列。
    public static IReadOnlyList<(UnitSpec Unit, double Value)> Conversions(Measurement measurement, int limit = 8)
    {
        var all = new List<(UnitSpec Unit, double Value)>();
        foreach (var unit in Units)
        {
            if (unit.Category != measurement.Unit.Category || !unit.Listed || unit.Id == measurement.Unit.Id) continue;
            if (Convert(measurement, unit) is { } value) all.Add((unit, value));
        }
        if (measurement.Unit.Category == UnitCategory.Temperature) return all;
        var readable = all.Where(item =>
        {
            var magnitude = Math.Abs(item.Value);
            return magnitude == 0 || (magnitude >= 0.01 && magnitude <= 1_000_000);
        }).ToList();
        return (readable.Count >= 3 ? readable : all).Take(limit).ToList();
    }

    /// 结果卡片上的换算行：左边是单位名，右边是数值加单位
    public static IReadOnlyList<ResultLine> Convert(Measurement measurement)
    {
        var lines = Conversions(measurement).Select(c => new ResultLine(c.Unit.Name, Display(c.Value, c.Unit))).ToList();
        // 身高这类长度再给一个「几英尺几英寸」
        if (measurement.Unit.Category == UnitCategory.Length
            && measurement.Unit.Id is not ("ft" or "in" or "yd" or "mi" or "nmi")
            && Unit("m") is { } meter && Convert(measurement, meter) is { } meters && meters >= 0.3 && meters < 3)
        {
            lines.Add(new ResultLine("英尺英寸", FeetAndInches(meters)));
        }
        return lines;
    }

    /// 结果卡片的说明，比如「长度：5 km」
    public static string Describe(Measurement measurement) =>
        measurement.Unit.Category.Title() + "：" + Display(measurement.Value, measurement.Unit);

    /// 1.8 米 → 5' 10.9"
    public static string FeetAndInches(double meters)
    {
        var totalInches = meters / 0.0254;
        var feet = (int)(totalInches / 12);
        var inches = Math.Round((totalInches - feet * 12.0) * 10, MidpointRounding.AwayFromZero) / 10;
        if (inches >= 12)
        {
            feet += 1;
            inches = 0;
        }
        var inchText = inches.ToString("F1", CultureInfo.InvariantCulture);
        if (inchText.EndsWith(".0", StringComparison.Ordinal)) inchText = inchText[..^2];
        return $"{feet}' {inchText}\"";
    }

    /// 数值加单位：3.10686 mi、37.8°C、2 斤（°和中文单位紧挨着数字）
    public static string Display(double value, UnitSpec unit)
    {
        var number = Format(value);
        var tight = unit.Symbol.StartsWith('°') || Rune.GetRuneAt(unit.Symbol, 0).Value >= 0x2E80;
        return tight ? number + unit.Symbol : number + " " + unit.Symbol;
    }

    /// 最多 6 位有效数字，带千分位
    public static string Format(double value)
    {
        // 避免显示成 -0
        if (Math.Abs(value) < 1e-12) return "0";
        var rounded = double.Parse(value.ToString("G6", CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture);
        return rounded.ToString("#,##0.####################", CultureInfo.InvariantCulture);
    }
}
