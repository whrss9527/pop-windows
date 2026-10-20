using System.Globalization;
using Pop.Core;

namespace Pop.Core.Tests;

public class PluginPerformanceTests
{
    [Fact]
    public void MismatchReasonsUseTheSameRulesAsAvailability()
    {
        var exact = new PluginMatch { Kinds = [], MinLength = 2, MaxLength = 2 };
        Assert.Equal(PluginMatchFailure.None, PluginMatcher.Check(exact, ContentClassifier.Classify("👩‍💻🇨🇳")));
        Assert.Equal(PluginMatchFailure.TooShort, PluginMatcher.Check(exact, ContentClassifier.Classify("👩‍💻")));
        Assert.Equal(PluginMatchFailure.TooLong, PluginMatcher.Check(exact, ContentClassifier.Classify("👩‍💻🇨🇳中")));
        Assert.Equal(PluginMatchFailure.Empty, PluginMatcher.Check(exact, ClassifiedContent.Empty));
        Assert.Equal(PluginMatchFailure.Kind, PluginMatcher.Check(new() { Kinds = ["url"] }, ContentClassifier.Classify("hello")));
        Assert.Equal(PluginMatchFailure.Pattern, PluginMatcher.Check(new() { Pattern = "^yes$" }, ContentClassifier.Classify("no")));
        Assert.Equal(PluginMatchFailure.InvalidPattern, PluginMatcher.Check(new() { Pattern = "(" }, ContentClassifier.Classify("hello")));
    }

    [Fact]
    public void InvalidLengthRangesAreEditableButCannotBeSaved()
    {
        var manifest = new PluginManifest { Name = "长度条件", Action = new() { Type = PluginActionType.JavaScript, Script = "input" },
            Match = new() { MinLength = 5, MaxLength = 2 } };
        Assert.Equal("最多字符不能少于最少字符", manifest.ValidationError());
        Assert.Equal(manifest.Match, PluginManifest.FromJson(manifest.ToJson())!.Match);
        Assert.Null((manifest with { Match = manifest.Match with { MaxLength = 5 } }).ValidationError());
        Assert.Null((manifest with { Match = manifest.Match with { MaxLength = 0 } }).Normalized().ValidationError());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("a\u0301中")]
    [InlineData("👩‍💻🇨🇳👍🏽")]
    [InlineData("a\r\nb")]
    public void LengthRulesMatchWholeGraphemeCounts(string text)
    {
        var content = ContentClassifier.Classify(text);
        var subject = content.Text.Length > 0 ? content.Text : content.Path ?? "";
        var length = new StringInfo(subject).LengthInTextElements;
        int?[] bounds = [null, -1, 0, 1, 2, 3, 8];
        foreach (var minimum in bounds) foreach (var maximum in bounds)
        {
            var match = new PluginMatch { Kinds = [], MinLength = minimum, MaxLength = maximum };
            var expected = minimum is null && maximum is null || subject.Length > 0 && !(length < minimum || length > maximum);
            Assert.Equal(expected, PluginMatcher.Matches(match, content));
        }
    }

    [Fact]
    public void LengthChecksDoNotAllocateAnArrayForWholeSelection()
    {
        var content = ContentClassifier.Classify(new string('a', 300_000));
        foreach (var match in new[] { new PluginMatch { MinLength = 10 }, new PluginMatch { MaxLength = 12 } })
        {
            PluginMatcher.Matches(match, content);
            var before = GC.GetAllocatedBytesForCurrentThread();
            var result = PluginMatcher.Matches(match, content);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(match.MaxLength is null, result);
            Assert.InRange(allocated, 0, 1024);
        }
    }

    [Theory]
    [InlineData(PluginActionType.Ai)]
    [InlineData(PluginActionType.Shortcut)]
    public void UnsupportedImportsKeepDataButAreNotOfferedAsExecutableActions(PluginActionType type)
    {
        var manifest = new PluginManifest { Id = "user-unsupported", Name = "导入的插件", Action = new PluginAction { Type = type }, Match = new PluginMatch { Kinds = [] } };
        var action = PluginRunner.ToAction(manifest);
        Assert.False(action.IsAvailable(ContentClassifier.Classify("hello")));
        Assert.False(action.IsAvailable(ClassifiedContent.Empty));
        Assert.Null(action.Run(ContentClassifier.Classify("hello")));
        Assert.Contains("Windows", action.Summary);
        Assert.Equal(type, PluginManifest.FromJson(manifest.ToJson())!.Action.Type);
    }
}
