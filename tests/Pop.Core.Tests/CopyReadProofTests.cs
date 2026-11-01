using Pop.Core;

namespace Pop.Core.Tests;

public sealed class CopyReadProofTests
{
    private static readonly CopyReadProof Original = new(5, 8, 42);

    [Fact]
    public void AnUnchangedTargetAndInteractionCanRestoreItsOwnCopy() =>
        Assert.True(Original.AcceptsOwner(42, 5, 8, true));

    [Theory]
    [InlineData(0, 5, 8, true)]
    [InlineData(99, 5, 8, true)]
    [InlineData(42, 6, 8, true)]
    [InlineData(42, 5, 9, true)]
    [InlineData(42, 5, 8, false)]
    public void UnknownOwnerOtherProcessNewReadNewUserInputOrFocusChangePreservesCurrentContent(uint owner, long operation, long activity, bool focused) =>
        Assert.False(Original.AcceptsOwner(owner, operation, activity, focused));

    [Fact]
    public void AnUnknownTargetCannotIssueATrustedCopy() =>
        Assert.False(new CopyReadProof(5, 8, 0).IsCurrent(5, 8, true));

    [Fact]
    public void ReturningToTheSameApplicationDoesNotReviveAnOldInteraction() =>
        Assert.False(Original.AcceptsOwner(42, 5, 10, true));
}
