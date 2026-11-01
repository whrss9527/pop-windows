using Pop.Core;

namespace Pop.Core.Tests;

public sealed class CopyInputVersionTests
{
    [Fact]
    public void MenuNavigationAndClicksKeepAnInFlightSelectionValid()
    {
        var input = new CopyInputVersion();
        var proof = new CopyReadProof(1, input.Version, 42);
        input.Key(handledByPop: true); input.Pointer(overPopInterface: true);
        Assert.True(proof.AcceptsOwner(42, 1, input.Version, true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForwardedKeysOrExternalClicksInvalidateTheOldSelection(bool keyboard)
    {
        var input = new CopyInputVersion();
        var proof = new CopyReadProof(1, input.Version, 42);
        if (keyboard) input.Key(handledByPop: false); else input.Pointer(overPopInterface: false);
        Assert.False(proof.AcceptsOwner(42, 1, input.Version, true));
    }

    [Fact]
    public void InputFromDifferentThreadsCannotLoseActivityEvidence()
    {
        var input = new CopyInputVersion();
        Parallel.For(0, 10000, _ => input.Key(handledByPop: false));
        Assert.Equal(10000, input.Version);
    }
}
