using Pop.Core;

namespace Pop.Core.Tests;

public class TextPreviewTests
{
    [Theory]
    [InlineData("", 8, "")]
    [InlineData(" \t\r\n\u2003 ", 8, "")]
    [InlineData("  hello\r\n\tworld \r\n ", 30, "hello world")]
    [InlineData("a\r \nb", 30, "a   b")]
    [InlineData("a\u2003\u2003b", 30, "a\u2003\u2003b")]
    [InlineData("  abc   ", 3, "abc")]
    [InlineData("abc\t\r\n ", 3, "abc")]
    [InlineData("abc      d", 3, "abc…")]
    [InlineData("abcd", 3, "abc…")]
    [InlineData("abc", 0, "…")]
    [InlineData(" \t ", 0, "")]
    [InlineData("ab😀cd", 3, "ab…")]
    [InlineData("ab😀cd", 4, "ab😀…")]
    [InlineData("abe\u0301cd", 3, "ab…")]
    [InlineData("ab👩‍💻cd", 5, "ab…")]
    public void PreservesWhitespaceRulesAndWholeTextElements(string text, int limit, string expected) =>
        Assert.Equal(expected, TextPreview.SingleLine(text, limit));

    [Fact]
    public void OrdinaryPreviewsMatchExistingFormatting()
    {
        var random = new Random(41);
        const string alphabet = "ab中  \r\n\t\u2003";
        for (var i = 0; i < 1000; i++)
        {
            var text = new string(Enumerable.Range(0, random.Next(400)).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray());
            var limit = random.Next(120);
            var flat = string.Join(" ", text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            var expected = flat.Length > limit ? flat[..limit] + "…" : flat;
            Assert.Equal(expected, TextPreview.SingleLine(text, limit));
        }
    }

    [Theory]
    [InlineData("ab👩‍💻cd")]
    [InlineData("a🇨🇳🇯🇵b")]
    [InlineData("abc👍🏽def")]
    [InlineData("x👨‍👩‍👧‍👦y")]
    [InlineData("12e\u0301\u0308xy")]
    [InlineData("a1️⃣b")]
    public void EveryUnicodeBoundaryMatchesFullString(string text)
    {
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text);
        for (var limit = 0; limit <= text.Length; limit++)
        {
            var expected = limit == text.Length ? text : text[..boundaries.Last(b => b <= limit)] + "…";
            Assert.Equal(expected, TextPreview.SingleLine(text, limit));
        }
    }

    [Fact]
    public void LargeContentAndTrailingWhitespaceDoNotAllocateWholeText()
    {
        var small = string.Concat(Enumerable.Repeat("line\r\n", 100));
        var large = string.Concat(Enumerable.Repeat("line\r\n", 200_000));
        var trailing = "hello" + new string(' ', 1_000_000);
        TextPreview.SingleLine(small, 120); TextPreview.SingleLine(large, 120);
        long Allocation(string text)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            TextPreview.SingleLine(text, 120);
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Assert.Equal(TextPreview.SingleLine(small, 120), TextPreview.SingleLine(large, 120));
        Assert.InRange(Allocation(large), 0, Allocation(small) + 4096);
        Assert.Equal("hello", TextPreview.SingleLine(trailing, 120));
        Assert.InRange(Allocation(trailing), 0, 4096);
        Assert.Throws<ArgumentOutOfRangeException>(() => TextPreview.SingleLine("text", -1));
    }
}
