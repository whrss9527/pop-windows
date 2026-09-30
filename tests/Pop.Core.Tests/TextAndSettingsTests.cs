using Pop.Core;

namespace Pop.Core.Tests;

public class TextActionsTests
{
    [Fact]
    public void CountsMixedText()
    {
        var s = TextActions.Count("Hello world，你好世界\nit's 2026");
        Assert.Equal(4, s.Chinese);
        Assert.Equal(4, s.Words);
        Assert.Equal(2, s.Lines);
        Assert.Equal(25, s.Characters);
        Assert.Equal(23, s.CharactersNoSpaces);
    }

    [Fact]
    public void EmptyText()
    {
        Assert.Equal(new TextStats(0, 0, 0, 0, 0), TextActions.Count(""));
    }

    [Fact]
    public void TranslateDirection()
    {
        Assert.Contains("to=zh-Hans", TextActions.TranslateUrl("hello world"));
        Assert.Contains("to=en", TextActions.TranslateUrl("你好世界"));
        Assert.EndsWith("text=a%20%26%20b", TextActions.TranslateUrl(" a & b "));
        Assert.Equal("https://www.bing.com/search?q=C%23%20%E8%AF%AD%E8%A8%80", TextActions.SearchUrl("C# 语言"));
    }

    [Fact]
    public void CaseConversion()
    {
        Assert.Equal("HELLO 你好", TextActions.ToUpper("hello 你好"));
        Assert.Equal("istanbul", TextActions.ToLower("ISTANBUL"));
    }

    [Fact]
    public void RingItemsNeedText()
    {
        var copy = RingItems.Default.First(i => i.Id == "copy");
        Assert.False(copy.IsAvailable(ContentClassifier.Classify("")));
        Assert.True(copy.IsAvailable(ContentClassifier.Classify("x")));
        Assert.Equal(RingItems.Default.Count, RingItems.Default.Select(i => i.Id).Distinct().Count());
    }
}

public class TerminalAppsTests
{
    [Theory]
    [InlineData("ConsoleWindowClass", "cmd", true)]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", "WindowsTerminal", true)]
    [InlineData("Chrome_WidgetWin_1", "wezterm-gui.exe", true)]
    [InlineData("Notepad", "notepad", false)]
    [InlineData("Chrome_WidgetWin_1", "Code", false)]
    [InlineData(null, null, false)]
    public void Detects(string? cls, string? process, bool expected)
    {
        Assert.Equal(expected, TerminalApps.IsTerminal(cls, process));
    }
}

public class AppSettingsTests
{
    [Fact]
    public void DefaultsWhenBroken()
    {
        var s = AppSettings.FromJson("not json");
        Assert.True(s.Enabled);
        Assert.Equal(250, s.HoldMilliseconds);
        Assert.True(s.CheckForUpdates);
        Assert.False(s.IncludePrerelease);
        Assert.True(s.DirectResults);
        Assert.True(s.ClipboardHistory);
        Assert.Equal(30, s.ClipboardRetentionDays);
        Assert.Equal(500, s.ClipboardMaxItems);
        Assert.Contains("KeePass", s.ClipboardExcludedApps);
    }

    [Fact]
    public void ClipboardSettings()
    {
        var s = AppSettings.FromJson("""{"clipboardHistory":false,"clipboardRetentionDays":0,"clipboardMaxItems":5,"clipboardExcludedApps":["Foo"," ",3]}""");
        Assert.False(s.ClipboardHistory);
        Assert.Equal(1, s.ClipboardRetentionDays);
        Assert.Equal(10, s.ClipboardMaxItems);
        Assert.Equal(["Foo"], s.ClipboardExcludedApps);
        var round = AppSettings.FromJson(s.ToJson());
        Assert.Equal(["Foo"], round.ClipboardExcludedApps);
        Assert.False(round.ClipboardHistory);
    }

    [Fact]
    public void ReadsLenientlyAndClamps()
    {
        var s = AppSettings.FromJson("""{"enabled":false,"holdMilliseconds":20,"checkForUpdates":"yes","IncludePrerelease":true,"directResults":false,"unknown":1}""");
        Assert.False(s.Enabled);
        Assert.Equal(AppSettings.MinHold, s.HoldMilliseconds);
        Assert.True(s.CheckForUpdates);
        Assert.True(s.IncludePrerelease);
        Assert.False(s.DirectResults);
    }

    [Fact]
    public void RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pop-settings-{Guid.NewGuid():N}", "settings.json");
        var s = new AppSettings { Enabled = false, HoldMilliseconds = 400, IncludePrerelease = true };
        s.Save(path);
        var loaded = AppSettings.Load(path);
        Assert.False(loaded.Enabled);
        Assert.Equal(400, loaded.HoldMilliseconds);
        Assert.True(loaded.IncludePrerelease);
        Directory.Delete(Path.GetDirectoryName(path)!, true);
        Assert.True(AppSettings.Load(path).Enabled);
    }
}

public class RingAndDirectSettingsTests
{
    [Fact]
    public void DefaultsMatchTheBuiltInRing()
    {
        var s = new AppSettings();
        Assert.Equal(RingItems.DefaultIds, s.RingSlots);
        Assert.Equal(DirectResults.DefaultKinds, s.DirectKindFlags);
        s.DirectResults = false;
        Assert.Equal(ContentKind.None, s.DirectKindFlags);
    }

    [Fact]
    public void ReadsRingSlotsAndDirectKinds()
    {
        var s = AppSettings.FromJson("""{"ringSlots":["calc","copy","hash","all","json"],"directKinds":["number","MATH","nope"]}""");
        Assert.Equal(["calc", "copy", "hash", "all", "json"], s.RingSlots);
        Assert.Equal(ContentKind.Number | ContentKind.Math, s.DirectKindFlags);
        // 格子数不在 4–8 之间时不用
        Assert.Equal(RingItems.DefaultIds, AppSettings.FromJson("""{"ringSlots":["copy","all"]}""").RingSlots);
        var round = AppSettings.FromJson(s.ToJson());
        Assert.Equal(s.RingSlots, round.RingSlots);
        Assert.Equal(s.DirectKinds, round.DirectKinds);
    }

    [Fact]
    public void BuildsRingsFromIds()
    {
        var ring = RingItems.Build(["copy", "unknown", "calc", "json", "all"]);
        Assert.Equal(["copy", "calc", "json", "all"], ring.Select(a => a.Id));
        Assert.Equal(RingItems.DefaultIds, RingItems.Build(["copy"]).Select(a => a.Id));
        Assert.Contains(RingItems.Choices, a => a.Id == "all");

        var link = ContentClassifier.Classify("https://example.com");
        Assert.Contains(RingItems.For(ring, link), a => a.Id == "copy");
        var withSearch = RingItems.Build(["copy", "search", "open", "all"]);
        // 圆盘上已经有「打开」时，「搜索」不换
        Assert.Equal(withSearch, RingItems.For(withSearch, link));
    }

    [Fact]
    public void DirectResultsFollowTheChosenKinds()
    {
        var number = ContentClassifier.Classify("0xFF");
        Assert.Null(DirectResults.For(number));
        Assert.Equal("数字", DirectResults.For(number, AppSettings.FromJson("""{"directKinds":["number"]}""").DirectKindFlags)?.Title);
    }
}
