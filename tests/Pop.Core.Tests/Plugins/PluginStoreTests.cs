using Pop.Core;

namespace Pop.Core.Tests;

public sealed class PluginStoreTests : IDisposable
{
    private readonly TempFolder folder = new();
    /// 导入用的文件放在插件文件夹外面
    private readonly TempFolder sources = new();
    private readonly List<PluginStore> stores = [];

    public void Dispose()
    {
        foreach (var store in stores) store.Dispose();
        folder.Dispose();
        sources.Dispose();
    }

    private PluginStore Open(Func<string, bool>? isReserved = null)
    {
        var store = new PluginStore(folder.Path, isReserved);
        stores.Add(store);
        return store;
    }

    private static PluginManifest WebPlugin(string name) =>
        new() { Name = name, Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" } };

    private IEnumerable<string> JsonFiles() => Directory.GetFiles(folder.Path, "*.json").Select(Path.GetFileName)!;

    [Fact]
    public void SaveReloadAndDelete()
    {
        var store = Open();
        Assert.Empty(store.Manifests);
        Assert.Empty(store.LoadErrors);
        var changes = 0;
        store.Changed += (_, _) => changes++;

        var saved = store.Save(WebPlugin("  网页搜索 "));
        Assert.Equal("网页搜索", saved.Name);
        Assert.Equal(PluginOutput.None, saved.Output);
        Assert.True(saved.ModifiedAt > PluginManifest.DistantPast);
        Assert.Equal(0, saved.ModifiedAt.Millisecond);
        Assert.Equal(1, changes);
        Assert.True(File.Exists(folder.File(saved.Id + ".json")));
        Assert.Equal(folder.File(saved.Id + ".json"), store.FilePath(saved.Id));
        Assert.Equal(saved, store.Find(saved.Id));
        // 写出的就是整理过的插件文件
        Assert.Equal(saved.ToJson(), File.ReadAllText(folder.File(saved.Id + ".json")));

        // 重新打开能读到同样的内容；内容没变时重新载入不算修改
        Assert.Equal([saved], Open().Manifests);
        Assert.False(store.Reload());
        Assert.Equal(1, changes);

        Assert.True(store.Delete(saved.Id));
        Assert.Empty(store.Manifests);
        Assert.Equal(2, changes);
        Assert.False(File.Exists(folder.File(saved.Id + ".json")));
        Assert.False(store.Delete(saved.Id));
        Assert.Equal(2, changes);
    }

    [Fact]
    public void HandWrittenFilesAreLoaded()
    {
        var store = Open();
        var changes = 0;
        store.Changed += (_, _) => changes++;

        File.WriteAllText(folder.File("hand-made.json"), "{\"action\": {\"type\": \"javascript\", \"script\": \"input.length\"}}");
        File.WriteAllText(folder.File("broken.json"), "not json");
        File.WriteAllText(folder.File("clash.json"), "{\"id\": \"translate\", \"name\": \"冒名\"}");
        Assert.True(store.Reload());

        Assert.Equal(["hand-made"], store.Manifests.Select(m => m.Id));
        Assert.Equal("hand-made", store.Manifests[0].Name);
        Assert.Equal("不是有效的插件文件", store.LoadErrors["broken.json"]);
        Assert.Contains("translate", store.LoadErrors["clash.json"]);
        Assert.Equal(1, changes);

        // 编辑后写回原来的文件，不会多出一个文件
        var edited = store.Manifests[0] with { Summary = "字数" };
        store.Save(edited);
        Assert.Equal(["broken.json", "clash.json", "hand-made.json"], JsonFiles().Order(StringComparer.Ordinal));
        Assert.Equal("字数", Open().Find("hand-made")?.Summary);
    }

    [Fact]
    public void ImportReplacesInvalidIds()
    {
        var store = Open();
        var source = sources.File("import-me.plugin.json");
        File.WriteAllText(source, "{\"id\": \"translate\", \"name\": \"导入的\", \"action\": {\"type\": \"url\", \"template\": \"https://a.com/{text}\"}}");
        var imported = store.ImportFile(source);
        Assert.NotEqual("translate", imported.Id);
        Assert.StartsWith("user-", imported.Id);
        Assert.Equal(["导入的"], store.Manifests.Select(m => m.Name));
    }

    [Fact]
    public void ImportFixesMissingIdsAndNames()
    {
        var store = Open();
        var json = "{\"action\": {\"type\": \"javascript\", \"script\": \"input.toUpperCase()\"}, \"output\": \"replace\"}";
        var imported = store.Import(json, "大写");
        Assert.StartsWith("user-", imported.Id);
        Assert.Equal("大写", imported.Name);
        Assert.Equal(PluginOutput.Replace, imported.Output);

        // 同一个插件再导入一次是更新
        var again = store.Import(imported.ToJson().Replace("大写", "改名了", StringComparison.Ordinal));
        Assert.Equal(imported.Id, again.Id);
        Assert.Equal(["改名了"], store.Manifests.Select(m => m.Name));

        var fromFile = sources.File("从文件.json");
        File.WriteAllText(fromFile, "{\"id\": \"user-file\", \"action\": {\"type\": \"url\", \"template\": \"https://a.com/{text}\"}}");
        Assert.Equal("从文件", store.ImportFile(fromFile).Name);

        Assert.Equal("不是有效的插件文件", Assert.Throws<PluginStoreException>(() => store.Import("not json")).Message);
        Assert.Contains("「broken.json」", Assert.Throws<PluginStoreException>(() =>
        {
            File.WriteAllText(sources.File("broken.json"), "[1, 2]");
            store.ImportFile(sources.File("broken.json"));
        }).Message);
        Assert.Equal("请填写网址", Assert.Throws<PluginStoreException>(() => store.Import("{\"name\": \"空的\"}")).Message);
    }

    [Fact]
    public void SaveRejectsInvalidPlugins()
    {
        var store = Open(id => id == "mine");
        Assert.Equal("请填写名称", Assert.Throws<PluginStoreException>(() => store.Save(WebPlugin(" "))).Message);
        Assert.Equal("插件 ID 无效", Assert.Throws<PluginStoreException>(() => store.Save(WebPlugin("x") with { Id = "mine" })).Message);
        Assert.Equal("插件 ID 无效", Assert.Throws<PluginStoreException>(() => store.Save(WebPlugin("x") with { Id = "../x" })).Message);
        // 自己指定了保留的 ID 时，Windows 版的内置功能 ID 可以用
        Assert.Equal("copy", store.Save(WebPlugin("x") with { Id = "copy" }).Id);
        Assert.Throws<PluginStoreException>(() => Open().Save(WebPlugin("x") with { Id = "copy" }));
        Assert.Empty(Directory.GetFiles(folder.Path, "*.tmp"));
    }

    [Fact]
    public void SavingNeverOverwritesOtherFiles()
    {
        File.WriteAllText(folder.File("user-a.json"), "not json yet");
        File.WriteAllText(folder.File("other.json"), "{\"id\": \"user-b\", \"name\": \"B\", \"action\": {\"template\": \"https://b.com\"}}");
        var store = Open();
        Assert.Single(store.LoadErrors);

        var saved = store.Save(WebPlugin("A") with { Id = "user-a" });
        Assert.Equal(folder.File("user-a-2.json"), store.FilePath(saved.Id));
        Assert.Equal("not json yet", File.ReadAllText(folder.File("user-a.json")));

        // 手写文件的文件名不是 ID：保存时写回它自己的文件
        var b = store.Find("user-b")!;
        store.Save(b with { Summary = "改过" });
        Assert.Equal(folder.File("other.json"), store.FilePath("user-b"));
        Assert.False(File.Exists(folder.File("user-b.json")));
    }

    [Fact]
    public void DuplicateAndHiddenFiles()
    {
        File.WriteAllText(folder.File("a.json"), "{\"id\": \"User-X\", \"name\": \"Alpha\"}");
        File.WriteAllText(folder.File("b.json"), "{\"id\": \"user-x\", \"name\": \"Second\"}");
        File.WriteAllText(folder.File(".hidden.json"), "{\"name\": \"隐藏\"}");
        File.WriteAllText(folder.File("notes.txt"), "{\"name\": \"不是插件\"}");
        File.WriteAllText(folder.File("c.json.1234.tmp"), "{\"name\": \"没写完的\"}");
        File.WriteAllText(folder.File("UPPER.JSON"), "{\"name\": \"Beta\"}");

        var store = Open();
        Assert.Equal(["Alpha", "Beta"], store.Manifests.Select(m => m.Name));
        Assert.Equal(["User-X", "UPPER"], store.Manifests.Select(m => m.Id));
        Assert.Equal("和 a.json 的插件 ID 重复", Assert.Single(store.LoadErrors).Value);
        Assert.Equal("Alpha", store.Find("user-x")?.Name);
    }

    [Fact]
    public void SortsByNameNaturally()
    {
        var store = Open();
        foreach (var name in new[] { "插件 10", "插件 2", "beta", "Alpha", "插件 2" })
        {
            store.Save(WebPlugin(name));
        }
        Assert.Equal(["Alpha", "beta", "插件 2", "插件 2", "插件 10"], store.Manifests.Select(m => m.Name));
    }

    [Fact]
    public void ExportWritesAMacCompatibleFile()
    {
        var store = Open();
        var saved = store.Save(WebPlugin(" 导出 ") with { Glyph = "Globe" });
        var path = folder.File("exported.txt");
        PluginStore.Export(saved with { Name = "  导出  " }, path);
        var json = File.ReadAllText(path);
        Assert.Equal(saved.ToJson(), json);
        Assert.Equal(saved, PluginManifest.FromJson(json));
        Assert.Throws<PluginStoreException>(() => PluginStore.Export(saved, Path.Combine(folder.Path, "missing", "x.json")));
    }

    [Fact]
    public void ToActionsSkipsBuiltinAndDuplicateIds()
    {
        var actions = PluginRunner.ToActions([
            new PluginManifest { Id = "user-one", Name = "一" },
            new PluginManifest { Id = "translate", Name = "冒名" },
            new PluginManifest { Id = "USER-ONE", Name = "重复" },
            new PluginManifest { Id = "bad id", Name = "无效" },
        ]);
        Assert.Equal(["user-one"], actions.Select(a => a.Id));
        Assert.Equal(["一"], actions.Select(a => a.Title));
    }

    [Fact]
    public async Task WatchingPicksUpExternalChanges()
    {
        var store = Open();
        var changed = new SemaphoreSlim(0);
        store.Changed += (_, _) => changed.Release();
        Assert.True(store.StartWatching());
        Assert.True(store.StartWatching());

        File.WriteAllText(folder.File("outside.json"), "{\"name\": \"外面放进来的\", \"action\": {\"template\": \"https://a.com\"}}");
        Assert.True(await WaitFor(changed, () => store.Find("outside") is not null));
        Assert.Equal("外面放进来的", store.Find("outside")?.Name);

        File.Delete(folder.File("outside.json"));
        Assert.True(await WaitFor(changed, () => store.Manifests.Count == 0));

        store.Dispose();
        File.WriteAllText(folder.File("after.json"), "{}");
        await Task.Delay(800);
        Assert.Null(store.Find("after"));
    }

    private static async Task<bool> WaitFor(SemaphoreSlim changed, Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await changed.WaitAsync(TimeSpan.FromMilliseconds(250));
        }
        return condition();
    }
}
