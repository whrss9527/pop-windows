using System.Text.Json;
using Pop.Core;

namespace Pop.Core.Tests;

public class ParityToolsTests
{
    [Fact]
    public void NumberListsMatchMacExamples()
    {
        var summary = Assert.IsType<NumberStats.Summary>(NumberStats.Parse("苹果 12\n香蕉 30.5\n橙子 7"));
        Assert.Equal(["49.5", "16.5", "12", "30.5", "7", "3"], summary.Rows.Select(r => r.Value));
        Assert.Equal(2.5, NumberStats.Parse("1\n2\n3\n4")!.Median);
        Assert.Equal("0.3", NumberStats.Parse("0.1\n0.2")!.Rows[0].Value);
        Assert.Equal([1234d, 5678d, 10d], NumberStats.Parse("1,234, 5,678; 10")!.Values);
        Assert.Equal([12d, 3.5d, -4d], NumberStats.Parse("¥12 $3.5 -4")!.Values);
        Assert.NotNull(NumberStats.Parse("表头\n1\n2\n3\n4"));
        Assert.Null(NumberStats.Parse("表头\n1\n2\n3"));
    }

    [Theory]
    [InlineData("1,234")]
    [InlineData("2026-09-29")]
    [InlineData("version 1.2.3")]
    [InlineData("a\nb\nc 3")]
    [InlineData("hello world")]
    public void RejectsNonLists(string text) => Assert.Null(NumberStats.Parse(text));

    [Fact]
    public void TableInputAndOutputMatchMacExamples()
    {
        var tsv = Assert.IsType<TableConverter.Table>(TableConverter.Parse("名字\t城市\n小明\t上海\n小红\t北京"));
        Assert.Equal(TableConverter.Format.Tsv, tsv.Source);
        var csv = Assert.IsType<TableConverter.Table>(TableConverter.Parse("name,note\nPop,\"a, b\"\nApp,\"say \"\"hi\"\"\""));
        Assert.Equal("a, b", csv.Rows[1][1]);
        Assert.Equal("say \"hi\"", csv.Rows[2][1]);
        var markdown = Assert.IsType<TableConverter.Table>(TableConverter.Parse("| a | b |\n| --- | :-: |\n| 1 | x\\|y |"));
        Assert.Equal("x|y", markdown.Rows[1][1]);
        Assert.Equal(["CSV", "制表符分隔", "JSON"], TableConverter.Conversions(markdown).Select(r => r.Label));
        Assert.Equal("| a | b |\n| --- | --- |\n| 1 | x\\|y |", TableConverter.Render(markdown.Rows, TableConverter.Format.Markdown));
        using var json = JsonDocument.Parse(TableConverter.Render(tsv.Rows, TableConverter.Format.Json));
        Assert.Equal("上海", json.RootElement[0].GetProperty("城市").GetString());
    }

    [Theory]
    [InlineData("a,b,c")]
    [InlineData("a,b\nc")]
    [InlineData("今天天气很好\n我们出去走走")]
    [InlineData("a,b\n1,\"unclosed")]
    [InlineData("a,b\n1,\"closed\"garbage")]
    public void RejectsMalformedTables(string text) => Assert.Null(TableConverter.Parse(text));

    [Fact]
    public void TableRoundTripPreservesQuotedCellsAndBackslashes()
    {
        var csv = Assert.IsType<TableConverter.Table>(TableConverter.Parse("name,note\nPop,\"a\nb\"\nApp,\" spaced \""));
        var round = TableConverter.Parse(TableConverter.Render(csv.Rows, TableConverter.Format.Csv))!;
        Assert.Equal("a\nb", round.Rows[1][1]);
        Assert.Equal(" spaced ", round.Rows[2][1]);
        IReadOnlyList<IReadOnlyList<string>> rows = [new[] { "path", "note" }, new[] { @"C:\Users\Pop\", "x|y" }];
        var markdown = TableConverter.Parse(TableConverter.Render(rows, TableConverter.Format.Markdown))!;
        Assert.Equal(rows[1], markdown.Rows[1]);
        Assert.Null(TableConverter.Parse(new string('x', TableConverter.MaxLength + 1)));
        Assert.Null(NumberStats.Parse(new string('1', NumberStats.MaxLength + 1)));
    }

    [Fact]
    public void ToolsAppearInActionsAndRespectContentAndSwitches()
    {
        var content = ContentClassifier.Classify("12\n30\n7");
        Assert.True(Actions.Statistics.IsAvailable(content));
        Assert.False(Actions.Statistics.IsAvailable(ContentClassifier.Classify("42")));
        Assert.Equal("49", Actions.Statistics.Run(content)!.Card!.PrimaryText);
        Assert.True(Actions.TableConvert.IsAvailable(ContentClassifier.Classify("a,b\n1,2")));
        Assert.DoesNotContain(Actions.Filter("", content, id => id != "numberStats"), a => a.Id == "numberStats");
        foreach (var id in new[] { "annotate", "colorPicker", "ruler", "settings" })
        {
            var action = Assert.IsType<PopAction>(Actions.Find(id));
            Assert.True(action.IsAvailable(ClassifiedContent.Empty));
            Assert.Contains(action, RingItems.Choices);
        }
    }
}

public class RingNavigationTests
{
    [Theory]
    [InlineData(2, 2, 1, 1)]
    [InlineData(1918, 2, -1, 1)]
    [InlineData(2, 1038, 1, -1)]
    [InlineData(1918, 1038, -1, -1)]
    public void CornerArcsKeepAnchorAndIgnoreOutwardDirections(double x, double y, int dx, int dy)
    {
        var layout = RingLayout.At(new Rect(0, 0, 1920, 1040), x, y, 150);
        Assert.True(layout.IsArc);
        Assert.Equal(3, layout.VisibleLimit);
        Assert.NotNull(layout.HitTest(dx * 100, dy * 100, 3, 34));
        Assert.Null(layout.HitTest(-dx * 100, -dy * 100, 3, 34));
        Assert.Null(layout.HitTest(0, 0, 3, 34));
        for (var i = 0; i < 3; i++)
        {
            var radians = layout.Angle(i, 3) * Math.PI / 180;
            Assert.Equal(i, layout.HitTest(Math.Sin(radians) * 100, -Math.Cos(radians) * 100, 3, 34));
        }
    }

    [Fact]
    public void MiddleIsFullRingAndScreenEdgesOpenInward()
    {
        var work = new Rect(0, 0, 1920, 1040);
        var middle = RingLayout.At(work, 500, 500, 150);
        Assert.False(middle.IsArc);
        Assert.Equal(0, middle.HitTest(0, -100, 12, 34));
        var top = RingLayout.At(work, 500, 2, 150);
        Assert.Equal(5, top.VisibleLimit);
        Assert.Null(top.HitTest(0, -100, 5, 34));
        Assert.Equal(2, top.HitTest(0, 100, 5, 34));
    }
    [Fact]
    public void TwelveSlotsAndClickModePersist()
    {
        var ids = Actions.BuiltIn.Take(11).Select(a => a.Id).Append("all").ToList();
        var settings = new AppSettings { RingSlots = ids, KeepRingOpen = true };
        var restored = AppSettings.FromJson(settings.ToJson());
        Assert.Equal(ids, restored.RingSlots);
        Assert.True(restored.KeepRingOpen);
        Assert.Equal(12, RingItems.Build(ids).Count);
        Assert.False(AppSettings.FromJson("{}").KeepRingOpen);
        Assert.False(AppSettings.FromJson("""{"keepRingOpen":"bad"}""").KeepRingOpen);
    }

    [Fact]
    public void NumberKeysAndArrowsReachEverySlot()
    {
        Assert.Equal(0, RingNavigation.NumberIndex(0x31, 12));
        Assert.Equal(8, RingNavigation.NumberIndex(0x39, 12));
        Assert.Equal(9, RingNavigation.NumberIndex(0x30, 12));
        Assert.Equal(9, RingNavigation.NumberIndex(0x60, 12));
        Assert.Null(RingNavigation.NumberIndex(0x30, 6));
        Assert.Null(RingNavigation.NumberIndex(0x41, 12));
        Assert.Equal(0, RingNavigation.Move(null, 1, 12));
        Assert.Equal(11, RingNavigation.Move(null, -1, 12));
        Assert.Equal(0, RingNavigation.Move(11, 1, 12));
        Assert.Equal(11, RingNavigation.Move(0, -1, 12));
        Assert.Equal(10, RingNavigation.Move(9, 1, 12));
    }
}

public class AnnotationDocumentTests
{
    [Fact]
    public void DragBoundsUndoRedoAndCounters()
    {
        var document = new AnnotationDocument();
        List<ImagePoint> points = [new(40, 20), new(10, 10)];
        document.Add(new(AnnotationTool.Rectangle, points, "#FF0000", 4));
        points.Clear();
        Assert.Equal(new Rect(10, 10, 40, 20), document.Strokes[0].Bounds);
        document.Add(new(AnnotationTool.Arrow, [new(5, 5)], "#FF0000", 4));
        Assert.Single(document.Strokes);
        document.Add(new(AnnotationTool.Counter, [new(20, 20)], "#FF0000", 4, Counter: document.NextCounter));
        document.Add(new(AnnotationTool.Counter, [new(30, 20)], "#FF0000", 4, Counter: document.NextCounter));
        Assert.Equal(3, document.NextCounter);
        document.Undo(); Assert.Equal(2, document.NextCounter);
        document.Redo(); Assert.Equal(3, document.NextCounter);
        document.Undo(); document.Add(new(AnnotationTool.Text, [new(2, 2)], "#FF0000", 4, Text: "注意"));
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void RejectsEmptyMarksAndKeepsMeaningfulOnes()
    {
        var document = new AnnotationDocument();
        foreach (var tool in Enum.GetValues<AnnotationTool>().Where(t => t != AnnotationTool.Counter))
            document.Add(new(tool, [new(2, 2)], "#FF0000", 4));
        Assert.Empty(document.Strokes);
        document.Undo(); document.Redo();
        document.Add(new(AnnotationTool.Pen, [new(2, 2), new(3, 4)], "#FF0000", 4));
        document.Add(new(AnnotationTool.Mosaic, [new(0, 0), new(20, 20)], "#FF0000", 4));
        Assert.Equal(2, document.Strokes.Count);
    }
}
