using Pop.Core;

namespace Pop.Core.Tests;

public sealed class StartupPreferenceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SuccessfulChangeIsConfirmedByAnIndependentRead(bool enabled)
    {
        var stored = !enabled;
        var reads = 0;
        var preference = new StartupPreference(() => { reads++; return stored; }, value => stored = value);
        Assert.Equal(new StartupPreferenceState(enabled, null), preference.Set(enabled));
        Assert.Equal(1, reads);
    }

    [Fact]
    public void DeniedWriteReportsTheActualOldStateAndAllowsRetry()
    {
        var denied = true;
        var stored = false;
        var preference = new StartupPreference(() => stored, value => { if (denied) throw new UnauthorizedAccessException("无写入权限"); stored = value; });
        var result = preference.Set(true);
        Assert.False(result.Enabled); Assert.Contains("无写入权限", result.Error);
        denied = false;
        Assert.Equal(new StartupPreferenceState(true, null), preference.Set(true));
    }

    [Fact]
    public void AnErrorAfterTheSystemChangeStillDisplaysTheConfirmedState()
    {
        var stored = false;
        var preference = new StartupPreference(() => stored, value => { stored = value; throw new IOException("保存后连接中断"); });
        var result = preference.Set(true);
        Assert.True(result.Enabled); Assert.NotNull(result.Error);
    }

    [Fact]
    public void ReadFailureIsUnknownAndNeverInventsAnOffStateOrWrites()
    {
        var writes = 0;
        var preference = new StartupPreference(() => throw new System.Security.SecurityException("无法访问"), _ => writes++);
        Assert.Null(preference.Read().Enabled);
        Assert.NotNull(preference.Read().Error); Assert.Equal(0, writes);
    }

    [Fact]
    public void SuccessfulWriteWithUnreadableResultIsNotReportedAsSaved()
    {
        var writes = 0;
        var preference = new StartupPreference(() => throw new IOException("回读失败"), _ => writes++);
        var result = preference.Set(true);
        Assert.Equal(1, writes); Assert.Null(result.Enabled); Assert.Contains("尚未确认", result.Error);
    }

    [Fact]
    public void SilentSystemRejectionReturnsTheActualStateWithAnError()
    {
        var preference = new StartupPreference(() => false, _ => { });
        var result = preference.Set(true);
        Assert.False(result.Enabled); Assert.NotNull(result.Error);
    }

    [Fact]
    public void IndependentWriteAndReadErrorsAreBothRetained()
    {
        var preference = new StartupPreference(() => throw new IOException("读取失败"), _ => throw new IOException("写入失败"));
        var result = preference.Set(false);
        Assert.Null(result.Enabled); Assert.Contains("读取失败", result.Error); Assert.Contains("写入失败", result.Error);
    }
}
