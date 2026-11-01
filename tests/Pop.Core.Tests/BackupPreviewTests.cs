using Pop.Core;

namespace Pop.Core.Tests;

public sealed class BackupPreviewTests
{
    [Fact]
    public void AutomaticCleanupChangeIsVisibleWithoutChangingRetentionNumbers()
    {
        var current = new AppSettings();
        var incoming = AppSettings.FromJson(current.ToJson()); incoming.ClipboardAutomaticCleanup = false;
        var change = Assert.Single(BackupPreview.Compare(new(incoming, []), current, []).Settings);
        Assert.Equal("剪贴板偏好", change.Title);
        Assert.Contains("自动清理开", change.Before); Assert.Contains("自动清理关", change.After);
    }

    private static PluginManifest Plugin(string id, string name = "搜索") => new()
    { Id = id, Name = name, Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" } };

    [Fact]
    public void IdenticalSettingsAndReorderedDictionaryKeysHaveNoChanges()
    {
        var current = new AppSettings(); current.ActionShortcuts["upper"] = null; current.ActionShortcuts["count"] = null;
        var next = new AppSettings(); next.ActionShortcuts["count"] = null; next.ActionShortcuts["upper"] = null;
        var preview = BackupPreview.Compare(new(next, [Plugin("same")]), current, [Plugin("same"), Plugin("keep")]);
        Assert.False(preview.HasChanges); Assert.Empty(preview.Settings);
        Assert.Equal(1, preview.UnchangedPlugins); Assert.Equal(1, preview.PreservedPlugins);
    }

    [Fact]
    public void GroupsActualChangesAndIdentifiesSameCountContentEdits()
    {
        var current = new AppSettings();
        var next = AppSettings.FromJson(current.ToJson());
        next.HoldMilliseconds += 50; next.TranslateTarget = "ja";
        next.Snippets[0] = next.Snippets[0] with { Text = "修改后的模板" };
        next.ClipboardRetentionDays = 90;
        var original = current.ToJson(); var incoming = next.ToJson();
        var preview = BackupPreview.Compare(new(next, []), current, []);
        Assert.True(preview.HasChanges);
        Assert.Equal(["鼠标唤起", "常用短语", "翻译偏好", "剪贴板偏好"], preview.Settings.Select(c => c.Title));
        Assert.Contains("内容有更新", preview.Settings.Single(c => c.Title == "常用短语").After);
        Assert.Equal(original, current.ToJson()); Assert.Equal(incoming, next.ToJson());
    }

    [Fact]
    public void RingAndRuleOrderStillCountAsChanges()
    {
        var current = new AppSettings { Rules = [new("foreign", "translate", true), new("math", "calculate", true)] };
        var next = AppSettings.FromJson(current.ToJson());
        next.RingSlots.Reverse(); next.Rules!.Reverse();
        var preview = BackupPreview.Compare(new(next, []), current, []);
        Assert.Contains(preview.Settings, c => c.Title == "圆盘布局");
        Assert.Contains(preview.Settings, c => c.Title == "直达规则");
    }

    [Fact]
    public void CountsNewUpdatedSameAndPreservedPluginsWithoutRunningThem()
    {
        var preview = BackupPreview.Compare(new(new(), [Plugin("same"), Plugin("updated", "新名称"), Plugin("new")]),
            new(), [Plugin("same"), Plugin("updated", "旧名称"), Plugin("keep")]);
        Assert.Equal(1, preview.AddedPlugins); Assert.Equal(1, preview.UpdatedPlugins);
        Assert.Equal(1, preview.UnchangedPlugins); Assert.Equal(1, preview.PreservedPlugins);
        Assert.Equal(BackupPluginChangeKind.Updated, preview.Plugins.Single(p => p.Id == "updated").Kind);
        Assert.True(preview.HasChanges);
    }

    [Fact]
    public void BackupVersionRoundTripsForPreview()
    {
        var backup = SettingsBackup.FromJson(new SettingsBackup(new(), [], "0.13.0").ToJson());
        Assert.Equal("0.13.0", backup.AppVersion);
        Assert.False(BackupPreview.Compare(backup, new(), []).HasChanges);
    }
}
