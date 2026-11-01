using System.Globalization;
using Pop.Core;

namespace Pop.Core.Tests;

public class PluginOutputPerformanceTests
{
    private static readonly char[] Newlines = ['\n', '\r', '\u000B', '\u000C', '\u0085', '\u2028', '\u2029'];
    private static readonly PluginManifest Toast = new() { Output = PluginOutput.Toast };

    [Theory]
    [InlineData("a")]
    [InlineData("中")]
    [InlineData("e\u0301")]
    [InlineData("👩‍💻")]
    [InlineData("🇨🇳")]
    [InlineData("👍🏽")]
    public void OutputPrefixesPreserveCompleteTextElements(string element)
    {
        var line = string.Concat(Enumerable.Repeat(element, 700));
        var starts = StringInfo.ParseCombiningCharacters(line);
        foreach (var separator in Newlines)
        {
            var output = new string(separator, 3) + line + separator + "later output";
            Assert.Equal(line[..starts[60]], PluginRunner.Present(output, Toast, false).Text);
        }
        Assert.Equal(line[..starts[500]], PluginRunner.ScriptOutput(new(1, "", "  " + line + "\n", false)).Error);
        for (var offset = 1; offset <= element.Length + 1; offset++)
        {
            var text = new string('a', PluginRunner.MaxEnvironmentLength - offset) + line;
            var end = StringInfo.ParseCombiningCharacters(text).Last(i => i <= PluginRunner.MaxEnvironmentLength);
            Assert.Equal(text[..end], PluginRunner.Environment(PluginInput.Of(text))["POP_TEXT"]);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\r\n\u0085\u2028")]
    [InlineData("\n \r\nnext")]
    [InlineData("\n\tfirst\nsecond")]
    [InlineData("short")]
    public void ToastKeepsFirstNonemptyLineRules(string output)
    {
        var first = output.Split(Newlines, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        Assert.Equal(first.Length == 0 ? "已完成" : first, PluginRunner.Present(output, Toast, false).Text);
    }

    [Fact]
    public void LargeOutputAndSelectionAllocateOnlyDisplayedPrefixes()
    {
        var text = new string('a', 2_000_000);
        var lines = string.Concat(Enumerable.Repeat("first line\n", 100_000));
        Check(() => PluginRunner.Present(lines, Toast, false), 4096);
        Check(() => PluginRunner.ScriptOutput(new(1, "", text, false)), 4096);
        Check(() => PluginRunner.Environment(PluginInput.Of(text)), 80_000);
        static void Check(Func<object> run, long maximum)
        {
            run();
            var before = GC.GetAllocatedBytesForCurrentThread();
            var value = run();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            GC.KeepAlive(value);
            Assert.InRange(allocated, 0, maximum);
        }
    }
}
