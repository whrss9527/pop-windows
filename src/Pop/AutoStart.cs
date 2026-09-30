using Microsoft.Win32;

namespace Pop;

/// 开机时启动：写在当前用户的 Run 键里，不需要管理员权限
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "Pop";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string value && value.Contains(Paths.Executable, StringComparison.OrdinalIgnoreCase);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled) key.SetValue(Name, $"\"{Paths.Executable}\"");
        else key.DeleteValue(Name, throwOnMissingValue: false);
    }
}
