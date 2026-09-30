using Pop.Core;

namespace Pop.Core.Tests;

public class PressTrackerTests
{
    [Fact]
    public void ShortPressReplaysClick()
    {
        var t = new PressTracker();
        Assert.Equal(new PressDecision(HookVerdict.Swallow, PressAction.StartTimer), t.OnDown(100, 100));
        Assert.Equal(PressPhase.Pending, t.Phase);
        Assert.Equal(new PressDecision(HookVerdict.Swallow, PressAction.ReplayClick), t.OnUp());
        Assert.Equal(PressPhase.Idle, t.Phase);
        // 松开之后计时才到，不算长按
        Assert.False(t.OnTimer(t.Token));
    }

    [Fact]
    public void SmallJitterStaysPending()
    {
        var t = new PressTracker();
        t.OnDown(100, 100);
        Assert.Equal(PressDecision.PassThrough, t.OnMove(103, 104));
        Assert.Equal(PressPhase.Pending, t.Phase);
    }

    [Fact]
    public void DragReplaysDownAndPasses()
    {
        var t = new PressTracker();
        t.OnDown(100, 100);
        Assert.Equal(new PressDecision(HookVerdict.Pass, PressAction.ReplayDown), t.OnMove(110, 100));
        Assert.Equal(PressPhase.Idle, t.Phase);
        Assert.False(t.OnTimer(t.Token));
        // 拖动结束的松开交给 App
        Assert.Equal(PressDecision.PassThrough, t.OnUp());
    }

    [Fact]
    public void LongPressIsOwned()
    {
        var t = new PressTracker();
        t.OnDown(10, 20);
        Assert.True(t.OnTimer(t.Token));
        Assert.Equal(PressPhase.Owned, t.Phase);
        Assert.Equal((10.0, 20.0), t.Origin);
        Assert.Equal(new PressDecision(HookVerdict.Pass, PressAction.Moved), t.OnMove(80, 20));
        Assert.Equal(new PressDecision(HookVerdict.Swallow, PressAction.Release), t.OnUp());
        Assert.Equal(PressPhase.Idle, t.Phase);
    }

    [Fact]
    public void StaleTimerIsIgnored()
    {
        var t = new PressTracker();
        t.OnDown(0, 0);
        var first = t.Token;
        t.OnUp();
        t.OnDown(0, 0);
        Assert.False(t.OnTimer(first));
        Assert.True(t.OnTimer(t.Token));
    }

    [Fact]
    public void DiscardSwallowsTheNextRelease()
    {
        var t = new PressTracker();
        t.OnDown(0, 0);
        t.OnTimer(t.Token);
        t.Discard();
        Assert.Equal(PressPhase.Discarding, t.Phase);
        Assert.Equal(new PressDecision(HookVerdict.Swallow, PressAction.None), t.OnUp());
        Assert.Equal(PressPhase.Idle, t.Phase);
    }

    [Fact]
    public void SecondDownWhileOwnedPasses()
    {
        var t = new PressTracker();
        t.OnDown(0, 0);
        t.OnTimer(t.Token);
        Assert.Equal(PressDecision.PassThrough, t.OnDown(0, 0));
    }

    [Fact]
    public void ResetInvalidatesPendingTimer()
    {
        var t = new PressTracker();
        t.OnDown(0, 0);
        var token = t.Token;
        t.Reset();
        Assert.False(t.OnTimer(token));
        Assert.Equal(PressDecision.PassThrough, t.OnUp());
    }
}
