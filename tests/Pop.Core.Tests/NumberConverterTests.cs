namespace Pop.Core.Tests;

public class NumberConverterTests
{
    private static string? Value(IReadOnlyList<ResultLine> rows, string label) =>
        rows.FirstOrDefault(row => row.Label == label)?.Value;

    [Fact]
    public void ParsesRadixesAndGrouping()
    {
        Assert.Equal(31, NumberConverter.Parse("0x1F")?.Integer);
        Assert.Equal(10, NumberConverter.Parse("0b1010")?.Integer);
        Assert.Equal(15, NumberConverter.Parse("0o17")?.Integer);
        Assert.Equal(1_234_567, NumberConverter.Parse("1,234,567")?.Integer);
        Assert.Equal(-42, NumberConverter.Parse("-42")?.Integer);
        Assert.Equal(16, NumberConverter.Parse("0x1F")?.Radix);
        Assert.Null(NumberConverter.Parse("3.14")?.Integer);
        Assert.Equal(3.14m, NumberConverter.Parse("3.14")?.Value);
        Assert.Null(NumberConverter.Parse("1,23"));
        Assert.Null(NumberConverter.Parse("abc"));
        Assert.Null(NumberConverter.Parse("0x"));
        Assert.Null(NumberConverter.Parse("0x1G"));
        // 超出 64 位的十六进制不认
        Assert.Null(NumberConverter.Parse("0x1_0000_0000_0000_0000"));
    }

    [Fact]
    public void RowsForAnInteger()
    {
        var rows = NumberConverter.Rows(NumberConverter.Parse("255")!);
        Assert.Equal("255", Value(rows, "十进制"));
        Assert.Equal("0xFF", Value(rows, "十六进制"));
        Assert.Equal("0o377", Value(rows, "八进制"));
        Assert.Equal("0b11111111", Value(rows, "二进制"));
        Assert.Equal("255", Value(rows, "千分位"));
        Assert.Equal("two hundred fifty-five", Value(rows, "英文读法"));
        Assert.Equal("二百五十五", Value(rows, "中文读法"));
        Assert.Equal("贰佰伍拾伍元整", Value(rows, "人民币大写"));
        Assert.Equal(
            ["十进制", "十六进制", "八进制", "二进制", "千分位", "英文读法", "中文读法", "人民币大写"],
            rows.Select(row => row.Label));
    }

    [Fact]
    public void DecimalsHaveNoRadixRows()
    {
        var rows = NumberConverter.Rows(NumberConverter.Parse("1234.5")!);
        Assert.Null(Value(rows, "十六进制"));
        Assert.Equal("1,234.5", Value(rows, "千分位"));
        Assert.Equal("壹仟贰佰叁拾肆元伍角", Value(rows, "人民币大写"));
    }

    [Fact]
    public void SpellsNumbers()
    {
        Assert.Equal("一万零五十", NumberConverter.SpelledOutChinese(10_050));
        Assert.Equal("三点零五", NumberConverter.SpelledOutChinese(3.05m));
        Assert.Null(NumberConverter.SpelledOutEnglish(1e20m));
        Assert.Null(NumberConverter.SpelledOutChinese(1e20m));

        Assert.Equal("zero", NumberConverter.SpelledOutEnglish(0));
        Assert.Equal("minus forty-two", NumberConverter.SpelledOutEnglish(-42));
        Assert.Equal("three point zero five", NumberConverter.SpelledOutEnglish(3.05m));
        Assert.Equal("one million two hundred thirty-four thousand five hundred sixty-seven",
            NumberConverter.SpelledOutEnglish(1_234_567));
        Assert.Equal("one thousand one", NumberConverter.SpelledOutEnglish(1001));

        Assert.Equal("十", NumberConverter.SpelledOutChinese(10));
        Assert.Equal("十五", NumberConverter.SpelledOutChinese(15));
        Assert.Equal("一百一十", NumberConverter.SpelledOutChinese(110));
        Assert.Equal("一千零五", NumberConverter.SpelledOutChinese(1005));
        Assert.Equal("十万", NumberConverter.SpelledOutChinese(100_000));
        Assert.Equal("十一万", NumberConverter.SpelledOutChinese(110_000));
        Assert.Equal("十万零五百", NumberConverter.SpelledOutChinese(100_500));
        Assert.Equal("一亿零一", NumberConverter.SpelledOutChinese(100_000_001));
        Assert.Equal("负五", NumberConverter.SpelledOutChinese(-5));
        Assert.Equal("零点五", NumberConverter.SpelledOutChinese(0.50m));
    }

    [Fact]
    public void SignedAndGrouped()
    {
        Assert.Equal("-0xFF", NumberConverter.Signed(-255, 16, "0x"));
        Assert.Equal("-0x8000000000000000", NumberConverter.Signed(long.MinValue, 16, "0x"));
        Assert.Equal("1,234,567", NumberConverter.Grouped(1_234_567));
        Assert.Equal("-1,234.5678", NumberConverter.Grouped(-1234.5678m));
    }

    [Theory]
    [InlineData("1234.5", "壹仟贰佰叁拾肆元伍角")]
    [InlineData("100000001", "壹亿零壹元整")]
    [InlineData("105000", "壹拾万伍仟元整")]
    [InlineData("100500", "壹拾万零伍佰元整")]
    [InlineData("10000", "壹万元整")]
    [InlineData("10.05", "壹拾元零伍分")]
    [InlineData("0.5", "伍角")]
    [InlineData("0", "零元整")]
    [InlineData("-3", "负叁元整")]
    [InlineData("2000000000", "贰拾亿元整")]
    [InlineData("12345678901234567", null)]
    public void RmbUppercase(string text, string? expected)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, NumberConverter.RmbUppercase(value));
    }
}
