using Pop.Core;

namespace Pop.Core.Tests;

public sealed class SettingsCatalogTests
{
    [Theory]
    [InlineData("剪贴板", "clipboard")]
    [InlineData("jtb", "clipboard")]
    [InlineData("ＣＬＩＰＢＯＡＲＤ", "clipboard")]
    [InlineData("FY", "translate")]
    [InlineData("hotkey", "shortcuts")]
    [InlineData("备份", "backup")]
    public void FindsRelatedPages(string query, string page) => Assert.Contains(SettingsCatalog.Search(query), entry => entry.Page == page);

    [Fact]
    public void CombinesWordsWithoutIncludingUnrelatedEntries()
    {
        var matches = SettingsCatalog.Search("剪贴板 密码");
        Assert.Single(matches);
        Assert.Equal("privacy", matches[0].Id);
    }

    [Fact]
    public void EmptyAndUnknownQueriesDoNotPretendToHaveMatches()
    {
        Assert.Empty(SettingsCatalog.Search(null));
        Assert.Empty(SettingsCatalog.Search(" \r\n\t "));
        Assert.Empty(SettingsCatalog.Search("nothing-matches-this-value"));
    }

    [Fact]
    public void EveryEntryHasAUniqueIdAndKnownPage()
    {
        string[] pages = ["general", "triggers", "rules", "shortcuts", "ring", "actions", "translate", "clipboard", "snippets", "backup", "about"];
        Assert.Equal(SettingsCatalog.Entries.Count, SettingsCatalog.Entries.Select(e => e.Id).Distinct().Count());
        Assert.All(SettingsCatalog.Entries, e => Assert.Contains(e.Page, pages));
    }
}
