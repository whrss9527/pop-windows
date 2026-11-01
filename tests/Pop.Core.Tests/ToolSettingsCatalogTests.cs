using Pop.Core;

namespace Pop.Core.Tests;

public class ToolSettingsCatalogTests
{
    [Theory]
    [InlineData("JSON 格式化")]
    [InlineData("json格式化")]
    [InlineData("ＪＳＯＮ 格式化")]
    [InlineData("gsh")]
    public void FindsRealToolsByNameCaseWidthAndPinyin(string query) =>
        Assert.Contains(ToolSettingsCatalog.Select(new AppSettings(), query).Items, a => a.Id == Actions.Json.Id);

    [Fact]
    public void StateViewsPartitionMatchingToolsWithoutChangingSettings()
    {
        var settings = new AppSettings { DisabledActions = [Actions.Json.Id, Actions.Hash.Id, "own.plugin"] };
        var before = settings.ToJson();
        var all = ToolSettingsCatalog.Select(settings, "");
        var enabled = ToolSettingsCatalog.Select(settings, "", ToolStateFilter.Enabled);
        var disabled = ToolSettingsCatalog.Select(settings, "", ToolStateFilter.Disabled);
        Assert.Equal(all.Total, enabled.Items.Count + disabled.Items.Count);
        Assert.Equal(2, disabled.Items.Count);
        Assert.Equal(all.Total - 2, all.Enabled);
        Assert.All(disabled.Items, a => Assert.False(settings.IsEnabled(a.Id)));
        Assert.Empty(enabled.EnableIds);
        Assert.Equal(before, settings.ToJson());
    }

    [Fact]
    public void CountsReflectNewStateWhenToolIsToggled()
    {
        var settings = new AppSettings();
        var before = ToolSettingsCatalog.Select(settings, "");
        settings.DisabledActions.Add(Actions.Json.Id);
        var after = ToolSettingsCatalog.Select(settings, "");
        Assert.Equal(before.Enabled - 1, after.Enabled);
        Assert.Contains(Actions.Json.Id, after.EnableIds);
        Assert.Equal(before.Total, after.Total);
    }

    [Fact]
    public void BulkEnablesOnlyCurrentMatchingToolsAndPreservesOtherFlags()
    {
        var settings = new AppSettings { DisabledActions = ["JSON", "json", Actions.Hash.Id, "own.plugin", Actions.All.Id] };
        Assert.Equal(1, ToolSettingsCatalog.EnableVisible(settings, "JSON 格式化", ToolStateFilter.Disabled));
        Assert.True(settings.IsEnabled(Actions.Json.Id));
        Assert.Equal([Actions.Hash.Id, "own.plugin", Actions.All.Id], settings.DisabledActions);
        Assert.Equal(0, ToolSettingsCatalog.EnableVisible(settings, "JSON 格式化", ToolStateFilter.Disabled));
    }

    [Fact]
    public void LockedEntryRemainsAvailableAndNeverBecomesBulkTarget()
    {
        var settings = new AppSettings { DisabledActions = [Actions.All.Id] };
        var all = ToolSettingsCatalog.Select(settings, Actions.All.Title);
        Assert.Contains(all.Items, a => a.Id == Actions.All.Id);
        Assert.True(ToolSettingsCatalog.IsEnabled(settings, Actions.All));
        Assert.DoesNotContain(Actions.All.Id, all.EnableIds);
        Assert.DoesNotContain(ToolSettingsCatalog.Select(settings, "", ToolStateFilter.Disabled).Items, a => a.Id == Actions.All.Id);
    }

    [Fact]
    public void NoResultsDoNotAffectCountsOrMutateFlags()
    {
        var settings = new AppSettings { DisabledActions = [Actions.Json.Id] };
        var before = settings.ToJson();
        var view = ToolSettingsCatalog.Select(settings, "no-such-built-in-tool-xyz");
        Assert.Empty(view.Items); Assert.Empty(view.EnableIds); Assert.Equal(0, view.MatchingTotal);
        Assert.Equal(ToolSettingsCatalog.Entries.Count - 1, view.Enabled);
        Assert.Equal(0, ToolSettingsCatalog.EnableVisible(settings, "no-such-built-in-tool-xyz"));
        Assert.Equal(before, settings.ToJson());
    }

    [Fact]
    public void SelectKeepsCatalogOrderAndExcludesCustomPlugins()
    {
        var view = ToolSettingsCatalog.Select(new AppSettings(), "");
        Assert.Equal(ToolSettingsCatalog.Entries.Select(a => a.Id), view.Items.Select(a => a.Id));
        Assert.All(view.Items, a => Assert.True(Actions.IsBuiltIn(a.Id)));
        Assert.Empty(ToolSettingsCatalog.Select(new AppSettings(), "bad\ud800paste").Items);
        Assert.Throws<ArgumentOutOfRangeException>(() => ToolSettingsCatalog.Select(new AppSettings(), "", (ToolStateFilter)42));
    }
}
