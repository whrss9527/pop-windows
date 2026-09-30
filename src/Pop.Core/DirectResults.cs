namespace Pop.Core;

/// 选中的内容可以直接算出结果时，跳过圆盘直接弹出结果卡片
public static class DirectResults
{
    /// 默认直接出结果的内容：外文（翻译）、算式、带单位的数值、颜色、时间戳
    public const ContentKind DefaultKinds = ContentKind.ForeignText | ContentKind.Math | ContentKind.Measurement | ContentKind.Color | ContentKind.Timestamp;

    /// 设置里用的名字
    public static readonly IReadOnlyList<(string Name, ContentKind Kind, string Title)> Kinds =
    [
        ("foreign", ContentKind.ForeignText, "外文（直接翻译）"),
        ("math", ContentKind.Math, "算式"),
        ("measurement", ContentKind.Measurement, "带单位的数值"),
        ("color", ContentKind.Color, "颜色值"),
        ("timestamp", ContentKind.Timestamp, "Unix 时间戳"),
        ("datetime", ContentKind.DateTime, "日期时间"),
        ("number", ContentKind.Number, "数字（进制、人民币大写）"),
    ];

    public static readonly IReadOnlyList<string> DefaultKindNames = ["foreign", "math", "measurement", "color", "timestamp"];

    /// 每类内容直接出结果时用的功能；这个功能停用了就不直接出结果
    public static string ActionFor(ContentKind kind) => kind switch
    {
        ContentKind.ForeignText => "translate",
        ContentKind.Math => "calc",
        ContentKind.Measurement => "unit",
        ContentKind.Color => "color",
        ContentKind.Timestamp or ContentKind.DateTime => "time",
        ContentKind.Number => "number",
        _ => "",
    };

    /// 选中的是外文、并且设置了外文直接翻译
    public static bool TranslatesDirectly(ClassifiedContent content, ContentKind directKinds) =>
        (directKinds & ContentKind.ForeignText) != 0 && content.Has(ContentKind.ForeignText);

    public static ContentKind? KindNamed(string name) =>
        Kinds.FirstOrDefault(k => string.Equals(k.Name, name, StringComparison.OrdinalIgnoreCase)) is { Name: not null } found ? found.Kind : null;

    /// 这段内容要不要直接出结果；要的话返回卡片内容
    public static CardContent? For(ClassifiedContent content, ContentKind directKinds = DefaultKinds, TimeZoneInfo? timeZone = null)
    {
        foreach (var kind in new[] { ContentKind.Math, ContentKind.Measurement, ContentKind.Color, ContentKind.Timestamp, ContentKind.DateTime, ContentKind.Number })
        {
            if (!content.Has(kind) || (directKinds & kind) == 0) continue;
            if (Card(content, kind, timeZone) is { } card) return card;
        }
        return null;
    }

    /// 某一类内容的结果卡片；算不出来返回 null
    public static CardContent? Card(ClassifiedContent content, ContentKind kind, TimeZoneInfo? timeZone = null)
    {
        var text = content.Text;
        switch (kind)
        {
            case ContentKind.Math when Calculator.Evaluate(text) is { } value:
                var result = Calculator.Format(value);
                var lines = new List<ResultLine> { new("结果", result) };
                if (Math.Abs(value) >= 10000 && NumberConverter.Parse(result) is { } parsed)
                    lines.Add(new("千分位", NumberConverter.Grouped(parsed.Value)));
                return new CardContent("计算", lines, Body: Shorten(text) + " =", Replacement: result, Icon: "Calculator24");

            case ContentKind.Measurement when UnitConverter.Parse(text) is { } measurement:
                return new CardContent("单位换算", UnitConverter.Convert(measurement), Body: UnitConverter.Describe(measurement), Icon: "Ruler24");

            case ContentKind.Color when ColorValue.Parse(text) is { } color:
                return new CardContent("颜色", color.Rows(), Body: ColorContrast.Summary(color), Swatch: color.HexString, Monospace: true, Icon: "Color24");

            case ContentKind.Timestamp when TimestampConverter.Parse(text) is { } stamp:
                return new CardContent("时间", DateParser.Rows(stamp, timeZone), Icon: "Clock24");

            case ContentKind.DateTime when DateParser.Parse(text, timeZone) is { } date:
                return new CardContent("时间", DateParser.Rows(date, timeZone), Icon: "Clock24");

            case ContentKind.Number when NumberConverter.Parse(text) is { } number:
                return new CardContent("数字", NumberConverter.Rows(number), Icon: "NumberSymbol24");

            default:
                return null;
        }
    }

    private static string Shorten(string text) => text.Length > 60 ? text[..60] + "…" : text;
}
