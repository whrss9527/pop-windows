using Pop.Core;

namespace Pop.Core.Tests;

/// Actions 里的插件列表是全局的：这组测试不和别的测试同时跑
[CollectionDefinition("Actions registry", DisableParallelization = true)]
public sealed class ActionsRegistryCollection;

[Collection("Actions registry")]
public class PluginActionsTests
{
    private static PluginManifest Plugin(string id, string name) => new()
    {
        Id = id,
        Name = name,
        Summary = "测试用",
        Action = new PluginAction { Type = PluginActionType.Url, Template = "https://example.com/?q={text}" },
    };

    [Fact]
    public void PluginsJoinTheListTheRingAndSearch()
    {
        try
        {
            Actions.SetPlugins(PluginRunner.ToActions([Plugin("user-test1", "测试插件")]));
            var action = Actions.Find("user-test1");
            Assert.Equal("测试插件", action?.Title);
            Assert.Equal(Actions.PluginCategory, action?.Category);
            Assert.Equal("测试用", action?.Summary);
            Assert.Equal("user-test1", Actions.List[^1].Id);
            Assert.Equal(Actions.BuiltIn.Count + 1, Actions.List.Count);
            Assert.Single(Actions.Plugins);
            Assert.False(Actions.IsBuiltIn("user-test1"));
            Assert.True(Actions.IsBuiltIn("copy"));
            Assert.True(Actions.IsBuiltIn("all"));

            Assert.Equal(["copy", "user-test1", "search", "all"], RingItems.Build(["copy", "user-test1", "search", "all"]).Select(a => a.Id));
            Assert.Contains(RingItems.Choices, a => a.Id == "user-test1");
            Assert.Contains(Actions.Filter("plugin", ContentClassifier.Classify("hello")), a => a.Id == "user-test1");

            var result = action!.Run(ContentClassifier.Classify("hello"));
            Assert.Equal(ActionEffect.RunPlugin, result?.Effect);
            Assert.Equal("user-test1", result?.PluginId);
        }
        finally
        {
            Actions.SetPlugins([]);
        }
        Assert.Null(Actions.Find("user-test1"));
        Assert.Empty(Actions.Plugins);
        // 插件没了：圆盘上它那一格换成别的功能
        Assert.DoesNotContain(RingItems.Build(["copy", "user-test1", "search", "all"]), a => a.Id == "user-test1");
    }

    [Fact]
    public void PluginsCannotTakeBuiltInIds()
    {
        try
        {
            Actions.SetPlugins(PluginRunner.ToActions([Plugin("copy", "冒名"), Plugin("user-ok", "正常")]));
            Assert.Equal("复制", Actions.Find("copy")?.Title);
            Assert.Equal(["user-ok"], Actions.Plugins.Select(a => a.Id));
        }
        finally
        {
            Actions.SetPlugins([]);
        }
    }
}
