using Pop.Core;

namespace Pop;

/// 使用非 Run 的独立验证项和受控失败，检查开关回读、未知状态和恢复入口。
internal static class StartupExperienceChecks
{
    public static async Task VerifyAsync(App app, Action<string, SettingsWindow> shot)
    {
        if (!app.IsDemo) throw new InvalidOperationException("启动项检查仅用于演示回归");
        var before = app.Settings.ToJson();
        var path = @"Software\Pop\Verification\" + Guid.NewGuid().ToString("N");
        SettingsWindow? window = null;
        try
        {
            var actual = AutoStart.ForVerification(path);
            window = new SettingsWindow(app, "general", actual); window.Show(); await Task.Delay(450);
            window.PreviewStartupChange(true);
            if (window.StartupStatus != new StartupPreferenceState(true, null) || !actual.Read().Enabled.GetValueOrDefault())
                throw new InvalidOperationException("启动开关未确认独立验证项的实际值");
            shot("settings-startup-confirmed", window);
            window.PreviewStartupChange(false);
            if (actual.Read().Enabled != false || window.StartupStatus.Error is not null)
                throw new InvalidOperationException("关闭启动项后回读状态错误");
            window.Close(); window = null;

            window = new SettingsWindow(app, "general", new StartupPreference(() => false, _ => throw new UnauthorizedAccessException("演示：没有写入权限")));
            window.Show(); await Task.Delay(450); window.PreviewStartupChange(true); await Task.Delay(100);
            if (window.StartupStatus.Enabled != false || window.StartupStatus.Error is null || !window.StartupErrorIsVisible || !window.StartupCanToggle)
                throw new InvalidOperationException("启动项写入失败后未恢复实际开关或未显示重试说明");
            shot("settings-startup-denied", window); window.Close(); window = null;

            var readable = false; var writes = 0;
            var unavailable = new StartupPreference(() => readable ? false : throw new IOException("演示：暂时无法读取"), _ => writes++);
            window = new SettingsWindow(app, "general", unavailable); window.Show(); await Task.Delay(450);
            if (window.StartupStatus.Enabled is not null || window.StartupCanToggle || !window.StartupErrorIsVisible || writes != 0)
                throw new InvalidOperationException("启动项未知状态被显示成关闭或仍允许盲目更改");
            shot("settings-startup-unknown", window);
            readable = true; window.PreviewStartupReload();
            if (window.StartupStatus != new StartupPreferenceState(false, null) || !window.StartupCanToggle || writes != 0)
                throw new InvalidOperationException("重新读取启动项没有恢复可操作的真实状态");
            window.Close(); window = null;

            window = new SettingsWindow(app, "general", new StartupPreference(() => false, _ => { }));
            window.Show(); await Task.Delay(200); window.PreviewStartupChange(true);
            if (window.StartupStatus.Enabled != false || window.StartupStatus.Error is null)
                throw new InvalidOperationException("系统未采用启动设置时仍报告成功");
            if (app.Settings.ToJson() != before) throw new InvalidOperationException("启动项检查改变了其他偏好");
        }
        finally { window?.Close(); AutoStart.RemoveVerification(path); }
    }
}
