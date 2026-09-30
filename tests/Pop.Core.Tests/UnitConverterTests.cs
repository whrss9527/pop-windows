using Pop.Core;

namespace Pop.Core.Tests;

public class UnitConverterTests
{
    private static double? Convert(string text, string id) =>
        UnitConverter.Parse(text) is { } measurement && UnitConverter.Unit(id) is { } unit
            ? UnitConverter.Convert(measurement, unit)
            : null;

    private static double Unwrap(double? value)
    {
        Assert.NotNull(value);
        return value.Value;
    }

    private static Measurement Parsed(string text)
    {
        var measurement = UnitConverter.Parse(text);
        Assert.NotNull(measurement);
        return measurement;
    }

    [Fact]
    public void Parsing()
    {
        Assert.Equal("km", UnitConverter.Parse("5 km")?.Unit.Id);
        Assert.Equal(5, UnitConverter.Parse("5km")?.Value);
        Assert.Equal(1500, UnitConverter.Parse("1,500 m")?.Value);
        Assert.Equal("F", UnitConverter.Parse("100°F")?.Unit.Id);
        Assert.Equal("C", UnitConverter.Parse("37.5 ℃")?.Unit.Id);
        Assert.Equal(-5, UnitConverter.Parse("-5 °C")?.Value);
        Assert.Equal("jin", UnitConverter.Parse("2 斤")?.Unit.Id);
        Assert.Equal("mu", UnitConverter.Parse("3亩")?.Unit.Id);
        Assert.Equal("in", UnitConverter.Parse("27\"")?.Unit.Id);
        Assert.Equal("kmh", UnitConverter.Parse("100 km / h")?.Unit.Id);
        Assert.Equal("GB", UnitConverter.Parse("16GB")?.Unit.Id);
        Assert.Equal("MB", UnitConverter.Parse("500 mb")?.Unit.Id);
        Assert.Equal("Mb", UnitConverter.Parse("100Mb")?.Unit.Id);
        Assert.Equal("Mbps", UnitConverter.Parse("100 Mbps")?.Unit.Id);
        Assert.Equal("MBps", UnitConverter.Parse("20 MB/s")?.Unit.Id);
        Assert.Equal("floz", UnitConverter.Parse("2 fl oz")?.Unit.Id);
        Assert.Equal("m", UnitConverter.Parse("10 m")?.Unit.Id);
        var height = UnitConverter.Parse("5'11\"");
        Assert.Equal("ft", height?.Unit.Id);
        Assert.Equal(5 + 11.0 / 12, height?.Value ?? 0, 1e-9);
    }

    // 不是带单位的数值：单独的数字、有歧义的单个大写字母、负的长度、不认识的单位
    [Theory]
    [InlineData("12345")]
    [InlineData("3.14")]
    [InlineData("5G")]
    [InlineData("16G")]
    [InlineData("100M")]
    [InlineData("10T")]
    [InlineData("-5 km")]
    [InlineData("5 min")]
    [InlineData("50%")]
    [InlineData("3 in 1")]
    [InlineData("hello")]
    [InlineData("12 pt")]
    [InlineData("")]
    public void NotAMeasurement(string text)
    {
        Assert.Null(UnitConverter.Parse(text));
    }

    [Fact]
    public void Conversions()
    {
        Assert.Equal(3.106_855_96, Unwrap(Convert("5 km", "mi")), 1e-6);
        Assert.Equal(37.777_777_8, Unwrap(Convert("100°F", "C")), 1e-6);
        Assert.Equal(273.15, Unwrap(Convert("0 °C", "K")), 1e-9);
        Assert.Equal(80.33, Unwrap(Convert("300 kelvin", "F")), 1e-9);
        Assert.Equal(1, Unwrap(Convert("2 斤", "kg")), 1e-12);
        Assert.Equal(666.666_666_7, Unwrap(Convert("1 亩", "m2")), 1e-6);
        Assert.Equal(12.5, Unwrap(Convert("100 Mbps", "MBps")), 1e-9);
        Assert.Equal(931.322_574_6, Unwrap(Convert("1 TB", "GiB")), 1e-6);
        Assert.Equal(96.560_64, Unwrap(Convert("60 mph", "kmh")), 1e-6);
        var km = Parsed("5 km");
        var kg = UnitConverter.Unit("kg");
        Assert.NotNull(kg);
        Assert.Null(UnitConverter.Convert(km, kg));
    }

    [Fact]
    public void RowsAndFormatting()
    {
        Assert.Equal("3.10686", UnitConverter.Format(3.106_855_961));
        Assert.Equal("16,404.2", UnitConverter.Format(16_404.199_475));
        Assert.Equal("5,000", UnitConverter.Format(5000));
        Assert.Equal("0", UnitConverter.Format(-0.000_000_000_000_1));

        var rows = UnitConverter.Convert(Parsed("5 km"));
        Assert.Equal("3.10686 mi", rows.FirstOrDefault(r => r.Label == "英里")?.Value);
        Assert.Equal("5,000 m", rows.FirstOrDefault(r => r.Label == "米")?.Value);
        // 不列出原来的单位，也不列太大、不好读的数
        Assert.Null(rows.FirstOrDefault(r => r.Label == "千米"));
        Assert.Null(rows.FirstOrDefault(r => r.Label == "毫米"));
        Assert.True(rows.Count <= 9);

        var temperature = UnitConverter.Convert(Parsed("100°F"));
        Assert.Equal(["摄氏度", "开尔文"], temperature.Select(r => r.Label));
        Assert.Equal("37.7778°C", temperature.FirstOrDefault()?.Value);

        var weight = UnitConverter.Convert(Parsed("1 kg"));
        Assert.Equal("2斤", weight.FirstOrDefault(r => r.Label == "斤")?.Value);
        Assert.Equal("2.20462 lb", weight.FirstOrDefault(r => r.Label == "磅")?.Value);

        var height = UnitConverter.Convert(Parsed("180 cm"));
        Assert.Equal("5' 10.9\"", height.FirstOrDefault(r => r.Label == "英尺英寸")?.Value);
        Assert.Equal("6' 0\"", UnitConverter.FeetAndInches(1.8288));
    }

    [Fact]
    public void Describe()
    {
        Assert.Equal("长度：5 km", UnitConverter.Describe(Parsed("5 km")));
        Assert.Equal("重量：2斤", UnitConverter.Describe(Parsed("2 斤")));
    }
}
