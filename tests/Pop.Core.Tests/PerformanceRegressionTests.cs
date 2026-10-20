using Pop.Core;

namespace Pop.Core.Tests;

public class PerformanceRegressionTests
{
    [Fact]
    public void AvailabilityChecksOncePerActionAndSelection()
    {
        var calls = 0;
        var action = new PopAction("expensive", "测试", "", "", ContentKind.Text, _ => null,
            c => { calls++; return c.Text == "first"; });
        var first = new ActionAvailability(ContentClassifier.Classify("first"));
        for (var i = 0; i < 100; i++) Assert.True(first.IsAvailable(action));
        Assert.Equal(1, calls);
        var next = new ActionAvailability(ContentClassifier.Classify("second"));
        Assert.False(next.IsAvailable(action)); Assert.False(next.IsAvailable(action)); Assert.Equal(2, calls);
        // 插件重新载入后，即使 ID 相同，新定义也必须重新判断。
        var reloaded = action with { Accepts = _ => { calls++; return false; } };
        Assert.False(first.IsAvailable(reloaded)); Assert.Equal(3, calls);
    }
    [Theory]
    [InlineData("hello pop", "")]
    [InlineData("1,2,3", "统计")]
    [InlineData("{\"a\":1}", "json")]
    [InlineData("https://example.com", "")]
    public void CachedQueriesKeepOrderAndRespectEnabledState(string text, string query)
    {
        var content = ContentClassifier.Classify(text); var cache = new ActionAvailability(content);
        foreach (var disabled in new[] { "", "copy", "hash" })
        {
            bool Enabled(string id) => id != disabled;
            Assert.Equal(Actions.Filter(query, content, Enabled), Actions.Filter(query, content, Enabled, cache.IsAvailable));
        }
    }
}
