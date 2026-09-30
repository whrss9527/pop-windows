namespace Pop.Core;

/// 选中的内容可以直接算出结果时，跳过圆盘直接弹出结果卡片
public static class DirectResults
{
    /// 默认直接出结果的内容：算式、带单位的数值、颜色、时间戳
    public const ContentKind DefaultKinds = ContentKind.Math | ContentKind.Measurement | ContentKind.Color | ContentKind.Timestamp;

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
                return new CardContent("计算", lines, Body: Shorten(text) + " =", Replacement: result);

            case ContentKind.Measurement when UnitConverter.Parse(text) is { } measurement:
                return new CardContent("单位换算", UnitConverter.Convert(measurement), Body: UnitConverter.Describe(measurement));

            case ContentKind.Color when ColorValue.Parse(text) is { } color:
                return new CardContent("颜色", color.Rows(), Body: ColorContrast.Summary(color), Swatch: color.HexString);

            case ContentKind.Timestamp when TimestampConverter.Parse(text) is { } stamp:
                return new CardContent("时间", DateParser.Rows(stamp, timeZone));

            case ContentKind.DateTime when DateParser.Parse(text, timeZone) is { } date:
                return new CardContent("时间", DateParser.Rows(date, timeZone));

            case ContentKind.Number when NumberConverter.Parse(text) is { } number:
                return new CardContent("数字", NumberConverter.Rows(number));

            default:
                return null;
        }
    }

    private static string Shorten(string text) => text.Length > 60 ? text[..60] + "…" : text;
}
