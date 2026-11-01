using System.Globalization;
using System.Text;
using Pop.Core;

namespace Pop.Core.Tests;

public class ResultPreviewTests
{
    [Theory]
    [InlineData("")]
    [InlineData("普通结果\r\n完整保留")]
    [InlineData("emoji 👩‍💻 与 e\u0301")]
    public void ShortResultKeepsOriginalReferenceAndContent(string text)
    {
        Assert.Same(text, ResultPreview.Text(text, 80));
        Assert.Equal(text, new TextPages(text, 80).Current.Text);
    }

    [Theory]
    [InlineData("a\r\nb\r\nc\n末行")]
    [InlineData("ab😀cd👩‍💻🇨🇳🇯🇵e\u0301末尾")]
    [InlineData("a👨‍👩‍👧‍👦b👍🏽c1️⃣d")]
    public void EveryPageSizePreservesExactContentAndBackNavigation(string text)
    {
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToHashSet();
        for (var limit = 2; limit <= text.Length + 1; limit++)
        {
            var pages = new TextPages(text, limit);
            var captured = new List<TextPage>();
            var restored = new StringBuilder();
            do
            {
                var page = pages.Current;
                Assert.InRange(page.Text.Length, 1, limit);
                Assert.Equal(text[page.Start..page.End], page.Text);
                if (page.End < text.Length) Assert.False(char.IsHighSurrogate(text[page.End - 1]) && char.IsLowSurrogate(text[page.End]));
                if (!page.SplitOversizedElement && boundaries.Contains(page.Start)) Assert.Contains(page.End, boundaries);
                restored.Append(page.Text); captured.Add(page);
            } while (pages.Next());
            Assert.Equal(text, restored.ToString());
            Assert.False(pages.Next());
            for (var i = captured.Count - 1; i >= 0; i--)
            {
                Assert.Equal(captured[i], pages.Current);
                Assert.Equal(i > 0, pages.Previous());
            }
            Assert.False(pages.Previous());
            Assert.Equal(captured.Count > 1, pages.Next());
            if (captured.Count > 1) Assert.Equal(captured[1], pages.Current);
        }
    }

    [Fact]
    public void OversizedSingleTextElementAlwaysMakesProgressWithinLimit()
    {
        var text = "a" + new string('\u0301', 10_000) + "👩‍💻\r\nend";
        var pages = new TextPages(text, 16);
        Assert.True(pages.Current.SplitOversizedElement);
        var restored = new StringBuilder(); var count = 0;
        do
        {
            Assert.InRange(pages.Current.Text.Length, 1, 16);
            restored.Append(pages.Current.Text);
            Assert.True(++count <= text.Length);
        } while (pages.Next());
        Assert.Equal(text, restored.ToString());
    }

    [Fact]
    public void EmptyAndBoundaryPagesDoNotCreateExtraPages()
    {
        var empty = new TextPages("");
        Assert.Equal(new TextPage("", 0, 0, false), empty.Current);
        Assert.False(empty.HasNext); Assert.False(empty.Next()); Assert.False(empty.Previous());
        var exact = new TextPages("12345678", 4);
        Assert.True(exact.Next()); Assert.Equal("5678", exact.Current.Text); Assert.False(exact.Next());
        Assert.Throws<ArgumentOutOfRangeException>(() => new TextPages("text", 1));
    }

    [Fact]
    public void FirstPagesOfTwoMillionCharactersHaveBoundedAllocation()
    {
        var text = string.Concat(Enumerable.Repeat("正文😀\r\n", 334_000));
        static long Measure(string value)
        {
            var start = GC.GetAllocatedBytesForCurrentThread();
            var pages = new TextPages(value); pages.Next(); pages.Previous();
            Assert.True(pages.HasNext);
            return GC.GetAllocatedBytesForCurrentThread() - start;
        }
        Measure(text);
        Assert.InRange(Measure(text), 0, 160_000);
        var preview = ResultPreview.Text(text, 2000);
        Assert.InRange(preview.Length, 1, 2001);
        Assert.StartsWith(preview[..^1], text);
    }

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(12, 0, 0, 12)]
    [InlineData(13, 1, 12, 1)]
    [InlineData(500, 41, 492, 8)]
    [InlineData(500, int.MaxValue, 500, 0)]
    public void ResultRowsAreBoundedAndLastWindowRetainsAllItems(int count, int page, int start, int shown) =>
        Assert.Equal((start, shown), ResultPreview.Lines(count, page));

    [Fact]
    public void SourceReaderAccountsForClippedLinesAndLongSingleLine()
    {
        Assert.False(ResultPreview.SourceNeedsReader("短原文"));
        Assert.True(ResultPreview.SourceNeedsReader("一\n二\n三"));
        Assert.True(ResultPreview.SourceNeedsReader(new string('x', 100_000)));
        Assert.False(ResultPreview.LineNeedsReader("一\n二\n三"));
        Assert.True(ResultPreview.LineNeedsReader("一\n二\n三\n四\n五\n六\n七"));
        Assert.True(ResultPreview.LineNeedsReader(new string('x', 401)));
    }
}
