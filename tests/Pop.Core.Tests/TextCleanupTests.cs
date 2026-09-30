using Pop.Core;

namespace Pop.Core.Tests;

public class TextCleanupTests
{
    [Fact]
    public void JoinLines()
    {
        Assert.Equal("The quick brown fox jumps. This is a sentence.",
            TextCleanup.JoinLines("The quick brown\nfox jumps. This is a sen-\ntence."));
        Assert.Equal("第一行第二行", TextCleanup.JoinLines("第一行\n第二行"));
        Assert.Equal("Para one line two\n\nPara two", TextCleanup.JoinLines("Para one\r\nline two\n\n\nPara two"));
        Assert.Null(TextCleanup.JoinLines("single line"));
    }

    [Fact]
    public void SpacesAndWidth()
    {
        Assert.Equal("用 React 写组件只要 3 分钟", TextCleanup.SpaceBetweenCjkAndLatin("用React写组件只要3分钟"));
        Assert.Equal("已经有 React 的", TextCleanup.SpaceBetweenCjkAndLatin("已经有 React 的"));
        Assert.Equal("ABC123，你好", TextCleanup.HalfWidth("ＡＢＣ１２３，你好"));
        Assert.Equal("a b c\nd", TextCleanup.CollapseSpaces("  a   b\t\tc  \n d  "));
        Assert.Equal("a\nb", TextCleanup.RemoveBlankLines("a\n\n  \nb"));
    }

    [Fact]
    public void RemovesInvisiblesButKeepsEmojiJoiners()
    {
        Assert.Equal("a b\nc", TextCleanup.RemovingInvisibles("a\u00A0b\u2028\u200Bc\uFEFF"));
        const string family = "\U0001F468\u200D\U0001F469";
        Assert.Equal(family, TextCleanup.RemovingInvisibles(family));
    }

    [Fact]
    public void LinesAndRows()
    {
        Assert.Equal("apple\nbanana\ncherry", TextCleanup.SortLines("banana\napple\ncherry"));
        Assert.Equal("line2\nLine10", TextCleanup.SortLines("Line10\nline2"));
        Assert.Equal("a\nb\nc", TextCleanup.UniqueLines("a\nb\na\nc\nb"));
        var rows = TextCleanup.Conversions("第一行\n第一行");
        Assert.Equal("合并换行", rows[0].Label);
        Assert.Contains(rows, r => r.Label == "按行去重" && r.Value == "第一行");
        // 没有可整理的地方时一行都没有
        Assert.Empty(TextCleanup.Conversions("hello"));
    }

    [Theory]
    [InlineData("hello world", false)]
    [InlineData("你好", true)]
    [InlineData("a\nb", true)]
    [InlineData("a  b", true)]
    [InlineData("ＡＢＣ", true)]
    [InlineData("a\u200Bb", true)]
    public void OnlyApplicableWhenUseful(string text, bool expected)
    {
        Assert.Equal(expected, TextCleanup.IsApplicable(text));
    }
}

public class LineToolsTests
{
    private static string? Value(IReadOnlyList<ResultLine> rows, string label) =>
        rows.FirstOrDefault(r => r.Label == label)?.Value;

    [Fact]
    public void Items()
    {
        Assert.Equal(new LineTools.Items(["a", "b", "c"], true), LineTools.Parse("a\n\n b \nc"));
        Assert.Equal(new LineTools.Items(["a", "b", "c"], false), LineTools.Parse("a, b, c"));
        Assert.Equal(new LineTools.Items(["苹果", "香蕉"], false), LineTools.Parse("苹果、香蕉"));
        Assert.Null(LineTools.Parse("只有一项"));
        Assert.False(LineTools.IsApplicable("第一段很长的文字" + new string('。', 300) + "\n第二段"));
        Assert.True(LineTools.IsApplicable("a\nb"));
    }

    [Fact]
    public void Conversions()
    {
        var rows = LineTools.Conversions("1001\n1002\n1003");
        Assert.Equal("1001, 1002, 1003", Value(rows, "逗号隔开"));
        Assert.Equal("'1001', '1002', '1003'", Value(rows, "单引号"));
        Assert.Equal("[1001, 1002, 1003]", Value(rows, "JSON 数组"));
        Assert.Equal("1. 1001\n2. 1002\n3. 1003", Value(rows, "加序号"));
        Assert.Equal("1003\n1002\n1001", Value(rows, "倒序"));
        if (Value(rows, "打乱顺序") is { } shuffled)
        {
            Assert.Equal(["1001", "1002", "1003"], shuffled.Split('\n').Order().ToArray());
        }

        var names = LineTools.Conversions("O'Brien\nSmith");
        Assert.Equal("'O''Brien', 'Smith'", Value(names, "单引号"));
        Assert.Equal("\"O'Brien\", \"Smith\"", Value(names, "双引号"));
        Assert.Equal("[\"O'Brien\", \"Smith\"]", Value(names, "JSON 数组"));

        Assert.Equal("苹果\n香蕉", Value(LineTools.Conversions("1. 苹果\n2. 香蕉"), "去掉序号"));
        Assert.Equal("a\nb\nc", Value(LineTools.Conversions("a、b、c"), "拆成多行"));
        Assert.Equal("a\nb", Value(LineTools.Conversions("'a'\n'b'"), "去掉引号"));
        Assert.Empty(LineTools.Conversions("只有一项"));
    }

    [Fact]
    public void Numbering()
    {
        Assert.Equal("1.5", LineTools.RemovingNumbering("1.5"));
        Assert.Equal("-5", LineTools.RemovingNumbering("-5"));
        Assert.Equal("item", LineTools.RemovingNumbering("- item"));
        Assert.Equal("第三", LineTools.RemovingNumbering("（3）第三"));
        Assert.Equal("第二", LineTools.RemovingNumbering("2、第二"));
    }

    [Fact]
    public void JsonStringEscapes()
    {
        Assert.Equal("\"a\\\"b\\n\\u0001\"", LineTools.JsonString("a\"b\n\u0001"));
    }
}
