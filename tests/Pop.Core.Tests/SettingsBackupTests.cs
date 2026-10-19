using Pop.Core;

namespace Pop.Core.Tests;

public sealed class SettingsBackupTests : IDisposable
{
    private readonly TempFolder folder = new();
    public void Dispose() => folder.Dispose();
    private static PluginManifest Plugin(string id, string name = "搜索") => new()
    { Id = id, Name = name, Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" } };

    [Fact]
    public void BackupRoundTripsSettingsAndExactPluginFormat()
    {
        var settings = new AppSettings { SelectionToolbar = true, TriggerExcludedApps = ["code"], Rules = [new("foreign", "custom.web", false)] };
        settings.AppRings["code"] = ["custom.web", "hash", "copy", "all"];
        settings.ActionShortcuts["upper"] = null;
        var plugin = Plugin("custom.web");
        var json = new SettingsBackup(settings, [plugin], "0.9.0").ToJson();
        var restored = SettingsBackup.FromJson(json);
        Assert.Equal(AppSettings.FromJson(settings.ToJson()).ToJson(), restored.Settings.ToJson());
        Assert.Equal(plugin.ToJson(), Assert.Single(restored.Plugins).ToJson());
        Assert.Contains("\"platform\": \"windows\"", json);
        Assert.DoesNotContain("apiKey", json, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"format\":\"other\"}")]
    [InlineData("{\"format\":\"pop-settings\",\"version\":2}")]
    [InlineData("{\"format\":\"pop-settings\",\"version\":1,\"settings\":{},\"plugins\":[]}")]
    [InlineData("{\"format\":\"pop-settings\",\"version\":1,\"platform\":\"windows\",\"settings\":null,\"plugins\":[]}")]
    public void DamagedFutureAndOtherPlatformBackupsAreRejected(string json) => Assert.Throws<InvalidDataException>(() => SettingsBackup.FromJson(json));

    [Theory]
    [InlineData("copy", "ok")]
    [InlineData("../escape", "ok")]
    [InlineData("valid", "")]
    public void InvalidPluginsRejectTheWholeBackup(string id, string name)
    {
        var json = new SettingsBackup(new(), [Plugin(id, name)]).ToJson();
        Assert.Throws<InvalidDataException>(() => SettingsBackup.FromJson(json));
    }

    [Fact]
    public void DuplicatePluginIdsAreRejectedCaseInsensitively()
    {
        var json = new SettingsBackup(new(), [Plugin("one"), Plugin("ONE")]).ToJson();
        Assert.Throws<InvalidDataException>(() => SettingsBackup.FromJson(json));
    }

    [Fact]
    public void RestoreUpdatesMatchingPluginsPreservesOthersAndSavesSettings()
    {
        using var store = new PluginStore(folder.File("plugins"));
        store.Save(Plugin("old", "原名")); store.Save(Plugin("keep"));
        var backup = new SettingsBackup(new() { SelectionToolbar = true }, [Plugin("old", "新名"), Plugin("new")]);
        backup.Restore(folder.File("settings.json"), store);
        Assert.True(AppSettings.Load(folder.File("settings.json")).SelectionToolbar);
        Assert.Equal("新名", store.Find("old")!.Name);
        Assert.NotNull(store.Find("keep")); Assert.NotNull(store.Find("new"));
    }

    [Fact]
    public void SettingsWriteFailureRestoresOriginalFilesAndDoesNotPublishPluginChanges()
    {
        using var store = new PluginStore(folder.File("plugins"));
        store.Save(Plugin("old", "原名"));
        var original = File.ReadAllBytes(store.FilePath("old")!);
        var changed = 0; store.Changed += (_, _) => changed++;
        Assert.Throws<IOException>(() => store.ImportBatch([Plugin("old", "新名"), Plugin("new")], () => throw new IOException("磁盘写入失败")));
        Assert.Equal(original, File.ReadAllBytes(store.FilePath("old")!));
        Assert.Equal("原名", store.Find("old")!.Name); Assert.Null(store.Find("new"));
        Assert.False(File.Exists(folder.File("plugins/new.json"))); Assert.Equal(0, changed);
        Assert.False(store.Reload());
    }
}
