using Pop.Core;

namespace Pop.Core.Tests;

public class ContentClassifierTests
{
    private static ContentKind Kinds(string text) => ContentClassifier.Classify(text).Kinds;

    private const ContentKind T = ContentKind.Text;

    [Fact]
    public void EmptySelection()
    {
        Assert.True(ContentClassifier.Classify(null).IsEmpty);
        Assert.True(ContentClassifier.Classify("  \n ").IsEmpty);
        Assert.Equal("未选中内容", ContentClassifier.Classify("").Summary);
    }

    [Fact]
    public void ForeignText()
    {
        var content = ContentClassifier.Classify("  Hello, how are you doing today?  ");
        Assert.Equal(T | ContentKind.ForeignText, content.Kinds);
        Assert.Equal("Hello, how are you doing today?", content.Text);
        Assert.Equal(T | ContentKind.ForeignText, Kinds("こんにちは、元気ですか"));
        Assert.Equal(T | ContentKind.ForeignText, Kinds("The word 你好 means hello"));
    }

    [Fact]
    public void ChineseText()
    {
        Assert.Equal(T | ContentKind.ChineseText, Kinds("今天天气很好，我们出去走走吧"));
        // 中英混排仍然算中文
        Assert.Equal(T | ContentKind.ChineseText, Kinds("用 React 写一个组件"));
    }

    [Fact]
    public void LinksAndEmail()
    {
        var url = ContentClassifier.Classify("https://github.com/whrss9527/pop");
        Assert.Equal(T | ContentKind.Url, url.Kinds);
        Assert.Equal("github.com", new Uri(url.Url!).Host);
        Assert.True(ContentClassifier.Classify("www.apple.com").Has(ContentKind.Url));
        Assert.Equal("https://www.apple.com/", ContentClassifier.Classify("www.apple.com").Url);
        var email = ContentClassifier.Classify("someone@example.com");
        Assert.Equal(T | ContentKind.Email, email.Kinds);
        Assert.Equal("mailto:someone@example.com", email.Url);
        // 文件名不当成域名
        Assert.False(ContentClassifier.Classify("README.md").Has(ContentKind.Url));
        Assert.False(ContentClassifier.Classify("main.py").Has(ContentKind.Url));
        Assert.Equal("链接", url.Summary);
    }

    [Fact]
    public void StructuredText()
    {
        Assert.Equal(T | ContentKind.Json, Kinds("""{"name": "pop", "tags": [1, 2]}"""));
        Assert.Equal(T | ContentKind.Json, Kinds("[1, 2, 3]"));
        Assert.False(ContentClassifier.Classify("{not json}").Has(ContentKind.Json));
        Assert.Equal(T | ContentKind.Timestamp, Kinds("1727510400"));
        Assert.Equal(T | ContentKind.Timestamp, Kinds("1727510400000"));
        Assert.False(ContentClassifier.Classify("9999999999").Has(ContentKind.Timestamp));
    }

    [Fact]
    public void Math()
    {
        Assert.Equal(T | ContentKind.Math, Kinds("1 + 2"));
        Assert.Equal(T | ContentKind.Math, Kinds("(3.5 - 1) / 2"));
        Assert.Equal(T | ContentKind.Math, Kinds("200*15%"));
        Assert.Equal(T | ContentKind.Math, Kinds("10 - 3"));
        Assert.Equal(T | ContentKind.Math, Kinds("128×3="));
        // 日期、编号、单独的数字不算算式
        Assert.False(ContentClassifier.Classify("2026-09-28").Has(ContentKind.Math));
        Assert.False(ContentClassifier.Classify("9/28").Has(ContentKind.Math));
        Assert.False(ContentClassifier.Classify("50%").Has(ContentKind.Math));
        Assert.Equal(T | ContentKind.Number, Kinds("12345"));
        Assert.Equal(T, Kinds("138-0000-0000"));
    }

    [Fact]
    public void ColorsNumbersAndDates()
    {
        Assert.Equal(T | ContentKind.Color, Kinds("#FF8800"));
        Assert.Equal(T | ContentKind.Color, Kinds("rgb(255, 136, 0)"));
        Assert.Equal(T | ContentKind.Color, Kinds("hsl(120, 100%, 50%)"));
        // #123 更像 issue 编号
        Assert.False(ContentClassifier.Classify("#123").Has(ContentKind.Color));
        Assert.Equal(T | ContentKind.Number, Kinds("0x1F"));
        Assert.Equal(T | ContentKind.Number, Kinds("3.14"));
        Assert.Equal(T | ContentKind.Number, Kinds("1,234,567"));
        Assert.Equal(T | ContentKind.DateTime, Kinds("2026-09-28"));
        Assert.Equal(T | ContentKind.DateTime, Kinds("2026-09-28 14:30"));
        Assert.Equal(T | ContentKind.DateTime, Kinds("2026年9月28日"));
        Assert.Equal("颜色", ContentClassifier.Classify("#FF8800").Summary);
    }

    [Fact]
    public void WordsAndPaths()
    {
        Assert.Equal(T | ContentKind.ForeignText | ContentKind.Word, Kinds("serendipity"));
        Assert.Equal(T | ContentKind.ForeignText | ContentKind.Word, Kinds("state-of-the-art"));
        Assert.Equal(T | ContentKind.ChineseText | ContentKind.Word, Kinds("你好"));
        Assert.False(ContentClassifier.Classify("hello world").Has(ContentKind.Word));
        Assert.False(ContentClassifier.Classify("a").Has(ContentKind.Word));

        var temp = Path.GetTempPath();
        var path = ContentClassifier.Classify(temp);
        Assert.Equal(T | ContentKind.Files, path.Kinds);
        Assert.Equal(temp, path.Path);
        Assert.Equal("路径", path.Summary);
        Assert.False(ContentClassifier.Classify(Path.Combine(temp, $"not-here-{Guid.NewGuid():N}")).Has(ContentKind.Files));
    }

    [Fact]
    public void Measurements()
    {
        Assert.Equal(T | ContentKind.Measurement, Kinds("5 km"));
        Assert.Equal(T | ContentKind.Measurement, Kinds("100°F"));
        Assert.Equal(T | ContentKind.Measurement, Kinds("2斤"));
        Assert.Equal(T | ContentKind.Measurement, Kinds("16 GB"));
        Assert.Equal("16 GB", ContentClassifier.Classify("16 GB").Summary);
    }

    [Fact]
    public void TokensAreNotForeignText()
    {
        Assert.Equal(T, Kinds("sk-abc123def456ghi789"));
        Assert.Equal(T, Kinds("#333333 on #FFFFFF"));
    }

    [Fact]
    public void ScriptProfiles()
    {
        Assert.True(new ScriptProfile("中文").IsChinese);
        Assert.False(new ScriptProfile("日本語のテキスト").IsChinese);
        Assert.False(new ScriptProfile("한국어 텍스트").IsChinese);
        Assert.False(new ScriptProfile("12345 !!").HasLetters);
    }
}

public class DirectResultsTests
{
    private static CardContent? Direct(string text) => DirectResults.For(ContentClassifier.Classify(text), timeZone: TimeZoneInfo.Utc);

    [Fact]
    public void MathGivesResultAndReplacement()
    {
        var card = Direct("128*3");
        Assert.NotNull(card);
        Assert.Equal("计算", card.Title);
        Assert.Equal("384", card.Replacement);
        Assert.Equal("384", card.PrimaryText);
        Assert.Equal("结果", card.Lines[0].Label);
        Assert.Contains(Direct("12345*10")!.Lines, l => l.Label == "千分位" && l.Value == "123,450");
    }

    [Fact]
    public void MeasurementColorAndTimestamp()
    {
        var unit = Direct("5 km");
        Assert.Equal("单位换算", unit?.Title);
        Assert.NotEmpty(unit!.Lines);
        Assert.Null(unit.Replacement);

        var color = Direct("#FF8800");
        Assert.Equal("颜色", color?.Title);
        Assert.Equal("#FF8800", color!.Swatch);
        Assert.Contains(color.Lines, l => l.Value.StartsWith("rgb(", StringComparison.Ordinal));

        var time = Direct("1727510400");
        Assert.Equal("时间", time?.Title);
        Assert.Contains(time!.Lines, l => l.Value.Contains("2024-09-28", StringComparison.Ordinal));
    }

    [Fact]
    public void PlainTextNumbersAndDatesAreNotDirectByDefault()
    {
        Assert.Null(Direct("hello world"));
        Assert.Null(Direct("12345"));
        Assert.Null(Direct("2026-09-28"));
        Assert.NotNull(DirectResults.For(ContentClassifier.Classify("12345"), ContentKind.Number));
        Assert.Null(DirectResults.For(ContentClassifier.Classify("128*3"), ContentKind.None));
    }

    [Fact]
    public void LinksReplaceSearchWithOpen()
    {
        var items = RingItems.For(ContentClassifier.Classify("https://example.com"));
        Assert.Contains(items, i => i.Id == "open");
        Assert.DoesNotContain(items, i => i.Id == "search");
        Assert.Same(RingItems.Default, RingItems.For(ContentClassifier.Classify("hello")));
    }
}
