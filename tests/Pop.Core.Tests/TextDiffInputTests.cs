using Pop.Core;

namespace Pop.Core.Tests;

public sealed class TextDiffInputTests
{
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void CharacterBoundaryMatchesComparison(int extra, bool rejected)
    {
        var text = new string('x', TextDiff.MaximumCharacters + extra);
        Assert.Equal(rejected, TextDiff.InputProblem(text) is not null);
        if (rejected) Assert.Throws<ArgumentException>(() => TextDiff.Compare(text, "short"));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void LineBoundariesRecognizeAllNewlineForms(string newline)
    {
        var atLimit = string.Join(newline, Enumerable.Repeat("x", TextDiff.MaximumLines));
        Assert.Null(TextDiff.InputProblem(atLimit));
        var over = atLimit + newline;
        Assert.Contains("20000", TextDiff.InputProblem(over));
        Assert.Throws<ArgumentException>(() => TextDiff.Compare("short", over));
    }

    [Fact]
    public void ReplacingASelectionCountsTheResultNotJustInsertedLength()
    {
        var current = new string('a', TextDiff.MaximumCharacters);
        Assert.Null(TextDiff.EditProblem(current, 10, 1, "b"));
        Assert.NotNull(TextDiff.EditProblem(current, 10, 0, "b"));
        Assert.Null(TextDiff.EditProblem(current, 0, current.Length, "短内容"));
    }

    [Fact]
    public void ANewLfCanCompleteAnExistingCrWithoutAddingAnExtraLine()
    {
        var current = string.Join('\n', Enumerable.Repeat("x", TextDiff.MaximumLines - 1)) + "\r";
        Assert.Null(TextDiff.EditProblem(current, current.Length, 0, "\n"));
        Assert.NotNull(TextDiff.EditProblem(current, current.Length, 0, "\n\n"));
    }

    [Fact]
    public void ReplacingLineBreaksCanReduceAnOverLimitCandidate()
    {
        var current = string.Join('\n', Enumerable.Repeat("x", TextDiff.MaximumLines + 1));
        Assert.NotNull(TextDiff.InputProblem(current));
        Assert.Null(TextDiff.EditProblem(current, 0, 2, ""));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(4, 0)]
    [InlineData(0, -1)]
    [InlineData(1, 3)]
    public void InvalidSelectionsAreRejected(int start, int length) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => TextDiff.EditProblem("abc", start, length, "x"));
}
