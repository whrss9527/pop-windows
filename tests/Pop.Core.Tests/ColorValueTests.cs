namespace Pop.Core.Tests;

public class ColorValueTests
{
    [Fact]
    public void ConvertsBetweenNotations()
    {
        var orange = ColorValue.Parse("#FF8800")!.Value;
        Assert.Equal("#FF8800", orange.HexString);
        Assert.Equal("rgb(255, 136, 0)", orange.RgbString);
        Assert.Equal("hsl(32, 100%, 50%)", orange.HslString);
        Assert.Equal(["HEX", "RGB", "HSL"], orange.Rows().Select(row => row.Label));

        Assert.Equal("#AABBCC", ColorValue.Parse("#abc")?.HexString);
        Assert.Null(ColorValue.Parse("#123"));
        Assert.Null(ColorValue.Parse("#12345"));
        Assert.Equal("#FF0000", ColorValue.Parse("rgb(255, 0, 0)")?.HexString);
        Assert.Equal("#FF000080", ColorValue.Parse("rgb(255 0 0 / 50%)")?.HexString);
        var translucent = ColorValue.Parse("rgba(0, 0, 255, 0.5)")!.Value;
        Assert.Equal("#0000FF80", translucent.HexString);
        Assert.Equal("rgba(0, 0, 255, 0.5)", translucent.RgbString);
        Assert.Equal("hsla(240, 100%, 50%, 0.5)", translucent.HslString);
        Assert.Equal("#00FF00", ColorValue.Parse("hsl(120, 100%, 50%)")?.HexString);
        Assert.Equal("#FFFFFF", ColorValue.Parse("HSL(0deg, 0%, 100%)")?.HexString);
        Assert.Null(ColorValue.Parse("rgb(300, 0, 0)"));
        Assert.Null(ColorValue.Parse("hsl(120, 100, 50)"));
        Assert.Null(ColorValue.Parse("rgba(0, 0, 0, 2)"));
        Assert.Null(ColorValue.Parse("hello"));
    }

    [Fact]
    public void ScaleGoesFromLightToDark()
    {
        var red = ColorValue.Parse("#FF0000")!.Value;
        Assert.Equal(
            ["#FFCCCC", "#FF9999", "#FF6666", "#FF3333", "#FF0000", "#CC0000", "#990000", "#660000", "#330000"],
            red.Scale().Select(color => color.HexString));
        Assert.Equal("#FF8800", ColorValue.Parse("#FF8800")!.Value.Scale()[4].HexString);
    }
}

public class ColorContrastTests
{
    [Fact]
    public void RatiosAndVerdicts()
    {
        var black = ColorValue.Parse("#000000")!.Value;
        var white = ColorValue.Parse("#FFFFFF")!.Value;
        var gray = ColorValue.Parse("#777777")!.Value;
        Assert.Equal(21, ColorContrast.Ratio(black, white), 3);
        Assert.Equal("4.47 : 1", ColorContrast.Format(ColorContrast.Ratio(gray, white)));
        // 普通文字不够 4.5，大号文字够 3
        Assert.Equal(["4.47 : 1", "AA 不通过 · AAA 不通过", "AA 通过 · AAA 不通过"],
            ColorContrast.Rows(gray, white).Select(row => row.Value));
        Assert.Equal("对白色 21.00 : 1，对黑色 1.00 : 1", ColorContrast.Summary(black));
    }

    [Fact]
    public void FindsPairs()
    {
        Assert.Equal("#FFFFFF", ColorContrast.Pair("#333333 on #FFFFFF")?.Background.HexString);
        Assert.True(ColorContrast.IsColorPair("rgb(0, 0, 0), #fff"));
        Assert.True(ColorContrast.IsColorPair("#333333 #FFFFFF"));
        Assert.False(ColorContrast.IsColorPair("#333333"));
        Assert.False(ColorContrast.IsColorPair("#111111 #222222 #333333"));
        Assert.False(ColorContrast.IsColorPair("the button uses #333333 text on a #ffffff background everywhere"));
    }
}
