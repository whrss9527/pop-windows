using Pop.Core;

namespace Pop.Core.Tests;

public class WindowPlacementTests
{
    [Fact]
    public void PreservesUserSizeAndEdgeAlignedPositionWhenAlreadyVisible()
    {
        var current = new Rect(0, 0, 960, 1040);
        var fit = WindowPlacement.Fit(current, new(0, 0, 1920, 1040), 1, 720, 520);
        Assert.Equal(current, fit.Bounds);
        Assert.Equal(720, fit.MinWidth); Assert.Equal(520, fit.MinHeight);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void ShrinksOversizedWindowIntoSmallerWorkAreaAtEachDpi(double scale)
    {
        var work = new Rect(40, 80, 680, 540);
        var fit = WindowPlacement.Fit(new(-200, -300, 1400, 1100), work, scale, 720, 520);
        Assert.Equal(work, fit.Bounds);
        Assert.Equal(Math.Min(720, work.Width / scale), fit.MinWidth);
        Assert.Equal(Math.Min(520, work.Height / scale), fit.MinHeight);
    }

    [Fact]
    public void HandlesMonitorToLeftAndAbovePrimaryWithTaskbarInset()
    {
        var work = new Rect(-1900, -1080, -40, -48);
        var fit = WindowPlacement.Fit(new(-2100, -1200, -1100, -500), work, 1.25, 720, 520);
        Assert.Equal(new(-1900, -1080, -900, -380), fit.Bounds);
    }

    [Fact]
    public void RepositionsDisconnectedMonitorWindowWithoutUnnecessaryResize()
    {
        var fit = WindowPlacement.Fit(new(2800, 500, 3700, 1100), new(0, 40, 1920, 1080), 1, 720, 520);
        Assert.Equal(new(1020, 480, 1920, 1080), fit.Bounds);
    }

    [Fact]
    public void RestoresOriginalMinimumOnLargerMonitorWithoutRestoringWholePreferredSize()
    {
        var small = WindowPlacement.Fit(new(0, 0, 1000, 800), new(0, 0, 640, 460), 1, 720, 520);
        var large = WindowPlacement.Fit(small.Bounds, new(0, 0, 1920, 1040), 1, 720, 520);
        Assert.Equal(new(0, 0, 720, 520), large.Bounds);
        Assert.Equal(720, large.MinWidth); Assert.Equal(520, large.MinHeight);
    }

    [Fact]
    public void PreservesLargerUserResizeAfterReturningToLargeMonitor()
    {
        var resized = new Rect(100, 60, 1500, 960);
        Assert.Equal(resized, WindowPlacement.Fit(resized, new(0, 0, 1920, 1040), 1, 720, 520).Bounds);
    }

    [Fact]
    public void AllowsInvisibleResizeBordersToRemainOutsideSnapEdges()
    {
        var outer = new Rect(-8, 0, 968, 1048);
        var visible = new Rect(0, 0, 960, 1040);
        var expanded = WindowPlacement.OuterWorkArea(new(0, 0, 1920, 1040), outer, visible);
        Assert.Equal(new(-8, 0, 1928, 1048), expanded);
        Assert.Equal(outer, WindowPlacement.Fit(outer, expanded, 1, 720, 520).Bounds);
    }

    [Fact]
    public void RejectsStaleOrUnavailableVisibleFrameBounds()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var outer = new Rect(100, 100, 900, 700);
        Assert.Equal(work, WindowPlacement.OuterWorkArea(work, outer, default));
        Assert.Equal(work, WindowPlacement.OuterWorkArea(work, outer, new(-8, 0, 968, 1048)));
    }

    [Fact]
    public void TinyWorkAreaStillProducesPositiveVisibleBounds()
    {
        var fit = WindowPlacement.Fit(new(100, 200, 900, 900), new(-1, -1, 0, 0), 2, 720, 520);
        Assert.Equal(new(-1, -1, 0, 0), fit.Bounds);
        Assert.Equal(.5, fit.MinWidth); Assert.Equal(.5, fit.MinHeight);
    }

    [Fact]
    public void FittingTwiceIsStableAcrossWorkAreasAndScales()
    {
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
        foreach (var work in new[] { new Rect(0, 0, 1920, 1040), new Rect(-1920, -1080, -40, -40), new Rect(40, 24, 700, 530) })
        foreach (var current in new[] { new Rect(-2000, -1300, -900, -600), new Rect(0, 0, 3000, 2000), new Rect(50, 50, 790, 590) })
        {
            var first = WindowPlacement.Fit(current, work, scale, 720, 520);
            var again = WindowPlacement.Fit(first.Bounds, work, scale, 720, 520);
            Assert.Equal(first, again);
            Assert.InRange(first.Bounds.Left, work.Left, work.Right - first.Bounds.Width);
            Assert.InRange(first.Bounds.Top, work.Top, work.Bottom - first.Bounds.Height);
        }
    }
}
