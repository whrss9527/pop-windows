using Microsoft.Win32;
using Pop.Core;

namespace Pop;

/// 开机时启动：写在当前用户的 Run 键里，不需要管理员权限
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Pop";
    private const string VerificationRoot = @"Software\Pop\Verification\";

    public static StartupPreference Preference { get; } = new(() => IsEnabled, Set);

    public static bool IsEnabled
    {
        get
        {
            return Read(RunKey);
        }
    }

    public static void Set(bool enabled)
    {
        Write(RunKey, enabled);
    }

    private static bool Read(string path)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(Name) is string value && value.Contains(Paths.Executable, StringComparison.OrdinalIgnoreCase);
    }

    private static void Write(string path, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        if (enabled) key.SetValue(Name, $"\"{Paths.Executable}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }

    /// 演示回归只准写独立的非启动项位置，不能改真实的 Run 项。
    internal static StartupPreference ForVerification(string path)
    {
        VerifyTestPath(path);
        return new(() => Read(path), value => Write(path, value));
    }

    internal static void RemoveVerification(string path)
    {
        VerifyTestPath(path);
        Registry.CurrentUser.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    private static void VerifyTestPath(string path)
    {
        if (!path.StartsWith(VerificationRoot, StringComparison.Ordinal) || !Guid.TryParseExact(path[VerificationRoot.Length..], "N", out _))
            throw new InvalidOperationException("启动项检查必须使用独立的验证位置");
    }
}
