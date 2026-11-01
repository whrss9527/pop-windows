namespace Pop.Core;

public sealed record StartupPreferenceState(bool? Enabled, string? Error);

/// 开关结果以系统回读为准；写入失败也要展示实际状态，而不是用户刚点的目标值。
public sealed class StartupPreference(Func<bool> read, Action<bool> write)
{
    public StartupPreferenceState Read()
    {
        try { return new(read(), null); }
        catch (Exception e) when (IsAccessFailure(e)) { return new(null, "无法读取开机启动设置：" + e.Message); }
    }

    public StartupPreferenceState Set(bool enabled)
    {
        string? writeError = null;
        try { write(enabled); }
        catch (Exception e) when (IsAccessFailure(e)) { writeError = "更改开机启动失败：" + e.Message; }
        var actual = Read();
        if (writeError is not null) return new(actual.Enabled, writeError + (actual.Error is null ? "" : "\n" + actual.Error));
        if (actual.Error is not null) return new(null, "已提交更改，但尚未确认 Windows 的实际状态。\n" + actual.Error);
        return actual.Enabled == enabled ? actual : new(actual.Enabled, "Windows 中的启动项没有变为请求的状态，请重新读取后重试");
    }

    private static bool IsAccessFailure(Exception e) => e is IOException or UnauthorizedAccessException or System.Security.SecurityException;
}
