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
        Assert.False(RingItems.IsAvailable(copy, ""));
        Assert.True(RingItems.IsAvailable(copy, "x"));
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
    }

    [Fact]
    public void ReadsLenientlyAndClamps()
    {
        var s = AppSettings.FromJson("""{"enabled":false,"holdMilliseconds":20,"checkForUpdates":"yes","IncludePrerelease":true,"unknown":1}""");
        Assert.False(s.Enabled);
        Assert.Equal(AppSettings.MinHold, s.HoldMilliseconds);
        Assert.True(s.CheckForUpdates);
        Assert.True(s.IncludePrerelease);
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
