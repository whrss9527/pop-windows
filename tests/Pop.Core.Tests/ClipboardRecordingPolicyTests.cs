using Pop.Core;

namespace Pop.Core.Tests;

public class ClipboardRecordingPolicyTests
{
    [Theory]
    [InlineData("keepassxc")]
    [InlineData("KEEPASSXC.EXE")]
    [InlineData(@"C:\Apps\KeePassXC.exe")]
    public void MatchesCanonicalProcessNamesWithoutChangingSavedNames(string source)
    {
        var settings = new AppSettings { ClipboardExcludedApps = [@"C:\Apps\KeePassXC.EXE", "Legacy.exe", "Legacy", "  "] };
        var before = settings.ToJson();
        var policy = new ClipboardRecordingPolicy(true, settings.ClipboardExcludedApps);
        Assert.False(policy.Allows(source));
        Assert.False(policy.Allows("legacy"));
        Assert.True(policy.Allows("chrome"));
        Assert.Equal(before, settings.ToJson());
    }

    [Fact]
    public void SnapshotDoesNotFollowChangesToTheUiList()
    {
        var names = new List<string> { "KeePass" };
        var policy = new ClipboardRecordingPolicy(true, names);
        names.Clear(); names.Add("Chrome.exe");
        Assert.False(policy.Allows("keepass")); Assert.True(policy.Allows("chrome"));
        var changed = new ClipboardRecordingPolicy(true, names);
        Assert.True(changed.Allows("keepass")); Assert.False(changed.Allows("chrome"));
        Assert.False(policy.EquivalentTo(changed));
    }

    [Fact]
    public void ReorderingAliasesAndDuplicatesDoesNotChangeEffectivePolicy()
    {
        var a = new ClipboardRecordingPolicy(true, ["KeePass", "Chrome", "CHROME.EXE"]);
        var b = new ClipboardRecordingPolicy(true, [@"C:\App\chrome.exe", " keepass.exe "]);
        Assert.True(a.EquivalentTo(b)); Assert.True(b.EquivalentTo(a));
        Assert.False(a.EquivalentTo(b.WithEnabled(false))); Assert.False(a.EquivalentTo(null));
    }

    [Fact]
    public void DisablingAndReenablingPreservesExclusionsAndOriginalSnapshot()
    {
        var enabled = new ClipboardRecordingPolicy(true, ["KeePass"]);
        var disabled = enabled.WithEnabled(false);
        Assert.Same(enabled, enabled.WithEnabled(true));
        Assert.False(disabled.Allows("chrome")); Assert.False(disabled.Allows(null));
        Assert.True(enabled.Allows("chrome"));
        var reenabled = disabled.WithEnabled(true);
        Assert.True(reenabled.EquivalentTo(enabled)); Assert.False(reenabled.Allows("KeePass.exe"));
    }

    [Fact]
    public void UnknownSourceKeepsExistingBehaviorWhenRecordingIsEnabled()
    {
        var policy = new ClipboardRecordingPolicy(true, ["", "  ", "KeePass"]);
        Assert.True(policy.Allows(null)); Assert.True(policy.Allows("")); Assert.True(policy.Allows("  "));
        Assert.False(new ClipboardRecordingPolicy(false).Allows("not-excluded"));
    }
}
