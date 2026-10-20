using System.Globalization;
using Pop.Core;

namespace Pop.Core.Tests;

public class SnippetExperienceTests
{
    [Fact]
    public void TemplatesUseLocalDateAndExpandUserInputOnlyOnce()
    {
        var now = new DateTimeOffset(2026, 10, 19, 20, 30, 0, TimeSpan.Zero);
        var zone = TimeZoneInfo.CreateCustomTimeZone("test-utc8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var text = SnippetExpander.Expand("{date}|{time}|{datetime}|{weekday}|{clipboard}|{selection}|{unknown}", now, zone, "{selection}", " {date}\n");
        Assert.Equal("2026-10-20|04:30|2026-10-20 04:30|星期二|{selection}| {date}\n|{unknown}", text);
        Assert.Equal("before  after", SnippetExpander.Expand("before {clipboard} after", now));
    }
    [Fact]
    public void SnippetsPreserveWhitespaceOrderAndEmptyConfigurationAcrossBackup()
    {
        var settings = AppSettings.FromJson("""{"snippets":[{"id":"one","title":"邮件","text":"  祝好！\n{date}\n"},{"text":"没有标题"},42]}""");
        Assert.Equal(2, settings.Snippets.Count); Assert.Equal("邮件", settings.Snippets[0].DisplayTitle);
        Assert.Equal("  祝好！\n{date}\n", settings.Snippets[0].Text);
        Assert.Equal("没有标题", settings.Snippets[1].DisplayTitle); Assert.NotEmpty(settings.Snippets[1].Id);
        var backup = SettingsBackup.FromJson(new SettingsBackup(settings, []).ToJson());
        Assert.Equal(settings.Snippets, backup.Settings.Snippets);
        Assert.Empty(AppSettings.FromJson("""{"snippets":[]}""").Snippets);
        Assert.Equal(Snippet.Examples, new AppSettings().Snippets);
    }
    [Fact]
    public void BadAndDuplicateSnippetsDoNotPoisonGoodEntries()
    {
        var snippets = Snippet.Normalize([new("same", "first", "OK"), new("SAME", "duplicate", "Other"), new("empty", "", "  "), new("long", "", new string('x', 100001))]);
        Assert.Equal("first", Assert.Single(snippets).Title);
        Assert.True(snippets[0].Matches("ok")); Assert.False(snippets[0].Matches("missing"));
    }
    [Theory]
    [InlineData("中文😀", "中文")]
    [InlineData("a\u0301", "")]
    [InlineData("家👨‍👩‍👧‍👦", "家")]
    [InlineData("", "")]
    public void SearchBackspaceRemovesAWholeVisibleCharacter(string text, string expected) => Assert.Equal(expected, SearchQuery.Backspace(text));
    [Fact]
    public void PastedSearchIsBoundedWithoutSplittingUnicode()
    {
        Assert.Equal("字数 统计", SearchQuery.Append("字数", "\n统计"));
        var value = SearchQuery.Append("", string.Concat(Enumerable.Repeat("😀", 1000)));
        Assert.Equal(SearchQuery.MaximumElements, new StringInfo(value).LengthInTextElements);
        Assert.Equal(400, value.Length);
    }
}
