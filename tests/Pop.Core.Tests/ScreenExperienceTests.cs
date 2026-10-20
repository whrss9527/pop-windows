using Pop.Core;

namespace Pop.Core.Tests;

public class ScreenExperienceTests
{
    [Fact]
    public void EdgeFinderMeasuresExactPhysicalPixelsAndIgnoresAlpha()
    {
        var bytes = Enumerable.Repeat((byte)255, 20 * 16 * 4).ToArray();
        for (var y = 3; y < 11; y++) for (var x = 4; x < 17; x++)
        { var i = (y * 20 + x) * 4; bytes[i] = 30; bytes[i + 1] = 60; bytes[i + 2] = 90; bytes[i + 3] = (byte)(x % 2 * 255); }
        var pixels = new ScreenPixels(20, 16, bytes);
        Assert.Equal(new PixelBounds(4, 3, 13, 8), pixels.FindEdges(10, 7));
        Assert.Equal(new ColorValue(90, 60, 30, 0), pixels.Sample(10, 7));
        Assert.Equal(new PixelBounds(6, 4, 7, 6), pixels.FindEdges(10, 7, clip: new(6, 4, 7, 6)));
        Assert.Null(pixels.FindEdges(-1, 7)); Assert.Null(pixels.FindEdges(20, 7));
    }
    [Fact]
    public void EdgeToleranceComparesAgainstOriginAndSupportsStride()
    {
        var bytes = new byte[3 * 20];
        for (var y = 0; y < 3; y++) for (var x = 0; x < 4; x++) bytes[y * 20 + x * 4] = (byte)(x * 10);
        var pixels = new ScreenPixels(4, 3, bytes, 20);
        Assert.Equal(new PixelBounds(0, 0, 3, 3), pixels.FindEdges(0, 1, 24));
        Assert.Equal(new PixelBounds(0, 0, 1, 3), pixels.FindEdges(0, 1, 0));
        Assert.Null(pixels.FindEdges(3, 1, clip: new(0, 0, 2, 3)));
        Assert.Throws<ArgumentException>(() => new ScreenPixels(4, 4, new byte[10]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScreenPixels(0, 4, Array.Empty<byte>()));
    }
    [Fact]
    public void BackgroundChangesCanvasButKeepsImageCoordinates()
    {
        var plain = new AnnotationFrame();
        Assert.Equal((100, 60), plain.OutputSize(100, 60)); Assert.Equal(0, plain.CornerRadius(100, 60));
        var framed = new AnnotationFrame(AnnotationBackground.Sky, 32, 200);
        Assert.Equal((164, 124), framed.OutputSize(100, 60)); Assert.Equal(new PixelBounds(32, 32, 100, 60), framed.ImageBounds(100, 60));
        Assert.Equal(30, framed.CornerRadius(100, 60));
        Assert.Equal(8, (framed with { Padding = -10 }).Inset);
        Assert.Equal(128, (framed with { Padding = 1000 }).Inset);
        foreach (var background in Enum.GetValues<AnnotationBackground>())
        { var colors = (framed with { Background = background }).Colors; Assert.NotNull(ColorValue.Parse(colors.Start)); Assert.NotNull(ColorValue.Parse(colors.End)); }
    }
}
