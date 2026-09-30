using Pop.Core;

namespace Pop.Core.Tests;

public class RingGeometryTests
{
    [Theory]
    [InlineData(0, -100, 0)]   // 正上方
    [InlineData(100, 0, 2)]    // 右（6 格时每格 60 度，90 度落在第 2 格的边上往后）
    [InlineData(0, 100, 3)]    // 正下方
    [InlineData(-100, -10, 5)] // 左偏上
    [InlineData(20, -100, 0)]
    public void SixSectors(double dx, double dy, int expected)
    {
        Assert.Equal(expected, RingGeometry.HitTest(dx, dy, 6, 30));
    }

    [Theory]
    [InlineData(0, -100, 0)]
    [InlineData(100, 0, 1)]
    [InlineData(0, 100, 2)]
    [InlineData(-100, 0, 3)]
    public void FourSectors(double dx, double dy, int expected)
    {
        Assert.Equal(expected, RingGeometry.HitTest(dx, dy, 4, 30));
    }

    [Fact]
    public void DeadZoneReturnsNull()
    {
        Assert.Null(RingGeometry.HitTest(10, 10, 6, 30));
        Assert.Null(RingGeometry.HitTest(100, 0, 0, 30));
    }

    [Fact]
    public void SectorBoundaryBelongsToNext()
    {
        // 6 格时第 0 格覆盖 -30°～30°，正好 30° 算第 1 格
        var rad = 30 * Math.PI / 180;
        Assert.Equal(1, RingGeometry.HitTest(Math.Sin(rad) * 100 + 0.001, -Math.Cos(rad) * 100, 6, 30));
    }

    [Fact]
    public void HighlightTakesShortestPath()
    {
        Assert.Equal(-60, RingGeometry.NextHighlightAngle(0, 5, 6));
        Assert.Equal(60, RingGeometry.NextHighlightAngle(0, 1, 6));
        Assert.Equal(360, RingGeometry.NextHighlightAngle(300, 0, 6));
    }

    [Fact]
    public void ClampMovesRingInsideWorkArea()
    {
        var work = new Rect(0, 0, 1920, 1040);
        Assert.Equal((130.0, 130.0), RingGeometry.ClampCenter(work, 5, 5, 130));
        Assert.Equal((1790.0, 910.0), RingGeometry.ClampCenter(work, 1919, 1039, 130));
        Assert.Equal((500.0, 500.0), RingGeometry.ClampCenter(work, 500, 500, 130));
        // 工作区比圆盘还小时放在中间
        Assert.Equal((50.0, 50.0), RingGeometry.ClampCenter(new Rect(0, 0, 100, 100), 0, 0, 130));
    }
}
