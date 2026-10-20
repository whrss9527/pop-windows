using System.Globalization;
using Pop.Core;

namespace Pop.Core.Tests;

public class ListContentPerformanceTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData("C:\\文档\\一.txt")]
    [InlineData("\nC:\\一.txt\n\n\\\\server\\share\\二.txt\nC:\\三\\\nC:\\四.txt\n")]
    public void FileSummariesKeepCountAndOriginalPathOrder(string text)
    {
        var expected = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var summary = FileListSummary.Parse(text);
        Assert.Equal(expected.Length, summary.Count);
        Assert.Equal(expected.Take(3), summary.FirstPaths);
    }

    [Fact]
    public void FilePreviewAllocationDoesNotGrowWithFileCount()
    {
        var paths = Enumerable.Range(0, 10_000).Select(i => $@"C:\文档\文件-{i}.txt").ToArray();
        var shortText = string.Join('\n', paths.Take(3));
        var text = string.Join('\n', paths);
        var small = Allocation(() => FileListSummary.Parse(shortText));
        Assert.InRange(Allocation(() => FileListSummary.Parse(text)), 0, small + 1024);
        var summary = FileListSummary.Parse(text);
        Assert.Equal(10_000, summary.Count);
        Assert.Equal(paths.Take(3), summary.FirstPaths);
        var item = new ClipboardItem { Kind = ClipboardKind.Files, Text = text };
        Assert.Equal(paths, item.FilePaths);
        Assert.Same(text, item.Text);
    }

    [Theory]
    [InlineData("", "\r\n\nfirst\r\nsecond", "first")]
    [InlineData("", "\r\n \nsecond", " ")]
    [InlineData("", "\r\n", "（空）")]
    [InlineData(" 标题 ", "正文", "标题")]
    public void AutomaticTitleKeepsFirstNonEmptyLineRules(string title, string text, string expected) =>
        Assert.Equal(expected, new Snippet("id", title, text).DisplayTitle);

    [Fact]
    public void SnippetSearchKeepsTitleBodyAndCrossBoundaryMatches()
    {
        var snippets = new[] { new Snippet("1", " Alpha ", "Beta\nGamma"), new Snippet("2", "", "首行\r\n正文内容"),
            new Snippet("3", "a\nb", "c\nd"), new Snippet("4", "中文😀", "İıſ和其他内容") };
        string[] queries = ["", "  ", "alpha", "BETA", "ha\nBe", "a\nb\nc", "b\nc\nd", " 首行 ", "首行\n首行", "正文", "😀\nİ", "missing"];
        foreach (var snippet in snippets)
            foreach (var query in queries)
                Assert.Equal(string.IsNullOrWhiteSpace(query) || (snippet.DisplayTitle + "\n" + snippet.Text).Contains(query.Trim(), StringComparison.OrdinalIgnoreCase),
                    snippet.Matches(query));
    }

    [Fact]
    public void SnippetPreviewAndSearchDoNotCopyTheFullBody()
    {
        var body = string.Concat(Enumerable.Repeat("第一行\r\n下一行\n", 10_000));
        var snippet = new Snippet("id", "", body);
        Assert.Equal("第一行", snippet.DisplayTitle);
        Assert.Equal(body, snippet.Text);
        Assert.Equal("第一行 ↵ 下一行 ↵ ", new Snippet("id", "", "第一行\r\n下一行\n").DisplayPreview);
        Assert.True(snippet.Matches("下一行"));
        Assert.InRange(Allocation(() => snippet.DisplayPreview), 0, 8192);
        Assert.InRange(Allocation(() => snippet.Matches("not present")), 0, 4096);
        Assert.InRange(Allocation(() => snippet.DisplayTitle), 0, 4096);
        var title = new Snippet("id", "", new string('a', 39) + "👩‍💻rest").DisplayTitle;
        Assert.Equal(new string('a', 39) + "…", title);
        Assert.DoesNotContain("DisplayPreview", System.Text.Json.JsonSerializer.Serialize(snippet));
    }

    [Theory]
    [InlineData("ab👩‍💻cd")]
    [InlineData(" a\r\n\n b\t😀e\u0301 ")]
    [InlineData("🇨🇳👍🏽👨‍👩‍👧‍👦")]
    [InlineData("\r\r\r\r\r\rtext\r\r")]
    public void BoundedPreviewsMatchWholeStringTextElements(string text)
    {
        var marked = text.Replace("\r", "").Replace("\n", " ↵ ");
        string Expected(string value, int limit)
        {
            if (value.Length <= limit) return value;
            var boundary = StringInfo.ParseCombiningCharacters(value).Last(i => i <= limit);
            return value[..boundary] + "…";
        }
        for (var limit = 0; limit <= marked.Length + 1; limit++)
        {
            Assert.Equal(Expected(text, limit), TextPreview.Truncate(text, limit));
            Assert.Equal(Expected(marked, limit), TextPreview.WithLineBreaks(text, limit));
        }
    }

    private static long Allocation<T>(Func<T> action)
    {
        action();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var value = action();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(value);
        return allocated;
    }
}
