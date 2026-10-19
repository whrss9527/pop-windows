using Pop.Core;

namespace Pop.Core.Tests;

public class SelectionToolbarTests
{
    [Theory]
    [InlineData("hello ")]
    [InlineData("  Hello\r\n")]
    [InlineData("\t你好 Pop\n\n")]
    public void CopyAndCaseReplacementKeepTheWholeSelection(string original)
    {
        var content = ContentClassifier.Classify(original);
        Assert.Equal(original.Trim(), content.Text);
        Assert.Equal(original, content.OriginalText);
        Assert.Equal(original, Actions.Copy.Run(content)!.Text);
        Assert.Equal(original.ToUpperInvariant(), Actions.Upper.Run(content)!.Text);
        Assert.Equal(original.ToLowerInvariant(), Actions.Lower.Run(content)!.Text);
    }

    [Fact]
    public void ConstructedContentUsesItsTextAsTheOriginalSelection()
    {
        var content = new ClassifiedContent(ContentKind.Text, "manual input ");
        Assert.Equal(content.Text, content.OriginalText);
        Assert.Equal(content.Text, Actions.Copy.Run(content)!.Text);
    }

    [Fact]
    public void SingleClicksDoNotReadButDragDoubleAndTripleClicksDo()
    {
        var tracker = new SelectionGestureTracker();
        Assert.False(tracker.Up(10, 10, 1));
        tracker.Down(10, 10, 1000, 500, 4);
        Assert.False(tracker.Up(12, 12, 1));
        tracker.Down(10, 10, 1200, 500, 4);
        Assert.True(tracker.Up(10, 10, 1));
        tracker.Down(10, 10, 1400, 500, 4);
        Assert.True(tracker.Up(10, 10, 1));
        tracker.Down(10, 10, 1600, 500, 4);
        Assert.False(tracker.Up(10, 10, 1));
        tracker.Down(10, 10, 3000, 500, 4);
        Assert.True(tracker.Up(30, 10, 2));
        Assert.False(tracker.Up(30, 10, 2));
    }

    [Fact]
    public void FarClicksAndResetDoNotCountAsDoubleClicks()
    {
        var tracker = new SelectionGestureTracker();
        tracker.Down(0, 0, 1000, 500, 4); tracker.Up(0, 0, 1);
        tracker.Down(100, 0, 1100, 500, 4);
        Assert.False(tracker.Up(100, 0, 1));
        tracker.Down(100, 0, 1200, 500, 4); tracker.Reset();
        Assert.False(tracker.Up(100, 0, 1));
        tracker.Down(100, 0, 1300, 500, 4);
        Assert.False(tracker.Up(100, 0, 1));
    }

    [Fact]
    public void ToolbarOnlyOffersApplicableContentActionsInRingOrder()
    {
        var content = ContentClassifier.Classify("hello pop");
        var actions = new[] { Actions.ScreenPin, Actions.Upper, Actions.Copy, Actions.Copy, Actions.Color, Actions.All, Actions.Count };
        Assert.Equal(["upper", "copy", "count"], SelectionToolbarLogic.Items(actions, content).Select(a => a.Id));
        Assert.Empty(SelectionToolbarLogic.Items(actions, ClassifiedContent.Empty));
        Assert.Equal(5, SelectionToolbarLogic.Items(Enumerable.Repeat(Actions.List, 2).SelectMany(a => a), content).Count);
    }

    [Fact]
    public void OptionalInputPluginsCanUseSelectedTextButHonorTheirMatcher()
    {
        var optional = new PopAction("optional", "插件", "Apps24", "", ContentKind.None, _ => null, Category: Actions.PluginCategory);
        var rejected = optional with { Id = "rejected", Accepts = _ => false };
        Assert.Equal([optional], SelectionToolbarLogic.Items([optional, rejected, Actions.ScreenPin], ContentClassifier.Classify("hello")));
        Assert.Empty(SelectionToolbarLogic.Items([optional], ClassifiedContent.Empty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PausingDuringAnOwnedMousePairStillConsumesRelease(bool elapsed)
    {
        var tracker = new PressTracker();
        tracker.OnDown(0, 0);
        var token = tracker.Token;
        if (elapsed) Assert.True(tracker.OnTimer(token));
        tracker.Cancel();
        Assert.False(tracker.OnTimer(token));
        Assert.Equal(new PressDecision(HookVerdict.Swallow, PressAction.None), tracker.OnUp());
        Assert.Equal(PressDecision.PassThrough, tracker.OnUp());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void PositionUsesSelectionBoundsAndFlipsBelowTheTopEdge(double scale)
    {
        var screen = new Rect(-1000 * scale, 0, 1000 * scale, 800 * scale);
        var selection = new Rect(100 * scale, 300 * scale, 300 * scale, 320 * scale);
        var frame = SelectionToolbarLogic.Frame(200 * scale, 56 * scale, selection, 300 * scale, 320 * scale, screen, scale);
        Assert.Equal(100 * scale, frame.Left);
        Assert.True(frame.Bottom < selection.Top);
        var high = new Rect(-990 * scale, 2 * scale, -900 * scale, 20 * scale);
        frame = SelectionToolbarLogic.Frame(300 * scale, 56 * scale, high, -900 * scale, 20 * scale, screen, scale);
        Assert.True(frame.Top >= high.Bottom);
        Assert.True(frame.Left >= screen.Left);
        Assert.True(frame.Right <= screen.Right);
        Assert.True(SelectionToolbarLogic.IsNear(selection, 350 * scale, 320 * scale, scale));
        Assert.False(SelectionToolbarLogic.IsNear(selection, 500 * scale, 320 * scale, scale));
    }

    [Fact]
    public void MultilineSelectionUsesPointerAndStaysOnNegativeMonitor()
    {
        var screen = new Rect(-1920, -1080, 0, 0);
        var frame = SelectionToolbarLogic.Frame(372, 56, new(-1900, -1000, -100, -100), -5, -5, screen);
        Assert.InRange(frame.Left, -1920, -372);
        Assert.InRange(frame.Top, -1080, -56);
    }

    [Fact]
    public void SettingsRoundTripAndNormalizeApplicationExclusions()
    {
        var settings = AppSettings.FromJson("""{"selectionToolbar":true,"triggerExcludedApps":[" Code.EXE ","C:\\App\\Code.exe","notepad",42,""]}""");
        Assert.True(settings.SelectionToolbar);
        Assert.Equal(["code", "notepad"], settings.TriggerExcludedApps);
        var restored = AppSettings.FromJson(settings.ToJson());
        Assert.True(restored.SelectionToolbar);
        Assert.Equal(settings.TriggerExcludedApps, restored.TriggerExcludedApps);
        Assert.False(new AppSettings().SelectionToolbar);
    }
}
