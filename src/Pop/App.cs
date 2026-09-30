using System.Windows;
using Pop.Core;
using Forms = System.Windows.Forms;

namespace Pop;

internal sealed class App : Application
{
    private readonly StartupOptions options;
    private InputHook? hook;
    private Coordinator? coordinator;
    private TrayIcon? tray;
    private ClipboardHistory? history;
    private HotKeys? hotKeys;
    private bool notifiedVersion;

    public App(StartupOptions options)
    {
        this.options = options;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Settings = AppSettings.Load(Paths.Settings);
        Updater = new Updater(() => Settings);
    }

    public AppSettings Settings { get; private set; }
    public Updater Updater { get; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("界面线程未处理的异常", args.Exception);
            args.Handled = true;
        };
        Log.Info($"Pop {Updater.CurrentVersion} 启动，{Paths.Executable}{(options.UpdatedFrom is { } from ? $"（从 {from} 更新而来）" : "")}");
        CleanUpOldExecutable();

        if (options.UpdateNow)
        {
            await RunHeadlessUpdateAsync();
            return;
        }

        hook = new InputHook { Enabled = Settings.Enabled, HoldMilliseconds = Settings.HoldMilliseconds };
        hook.Start();
        history = new ClipboardHistory(() => Settings);
        coordinator = new Coordinator(Dispatcher, hook, () => Settings, history);
        hotKeys = new HotKeys();
        // Win+Shift+V 被系统占用了；默认用 Win+Alt+V，也被占用时依次换下一个
        foreach (var (modifiers, vk, name) in new (uint, uint, string)[]
        {
            (HotKeys.MOD_WIN | HotKeys.MOD_ALT, 0x56, "Win+Alt+V"),
            (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x56, "Ctrl+Alt+Shift+V"),
            (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x48, "Ctrl+Alt+Shift+H"),
        })
        {
            if (!hotKeys.Register(modifiers, vk, $"{name}（剪贴板历史）", () => coordinator.ShowHistory())) continue;
            HistoryHotKey = name;
            break;
        }
        tray = new TrayIcon(this);
        Updater.Changed += OnUpdaterChanged;
        Updater.StartSchedule();

        if (options.UpdatedFrom is not null)
            tray.Notify("Pop 已更新", $"现在是 {Updater.CurrentVersion}");
        else if (!options.Silent && !System.IO.File.Exists(Paths.Settings))
            tray.Notify("Pop 已在运行", "在任意 App 里选中文字，长按鼠标右键试试。Pop 的菜单在任务栏右下角的图标上。");
        if (!System.IO.File.Exists(Paths.Settings)) SaveSettings();

        SmokeTest.Report($"started version={Updater.CurrentVersion} hooks={(hook.IsInstalled ? "ok" : "failed")} updated-from={options.UpdatedFrom ?? "-"}");
        if (SmokeTest.ExitAfterStart) Quit();
    }

    public void UpdateSettings(Action<AppSettings> change)
    {
        change(Settings);
        SaveSettings();
        if (hook is not null)
        {
            hook.Enabled = Settings.Enabled;
            hook.HoldMilliseconds = Settings.HoldMilliseconds;
        }
        history?.Cleanup();
    }

    public async Task CheckForUpdatesAsync(bool userInitiated)
    {
        try
        {
            var release = await Updater.CheckAsync(userInitiated);
            if (userInitiated && release is null) tray?.Notify("Pop", $"{Updater.CurrentVersion} 已是最新版本");
        }
        catch (UpdateException e)
        {
            tray?.Notify("Pop", e.Message, Forms.ToolTipIcon.Warning);
        }
    }

    public async Task InstallUpdateAsync(ReleaseInfo release)
    {
        tray?.Notify("Pop", $"正在下载 {release.Version}，完成后会自动重新启动");
        try
        {
            if (await Updater.InstallAsync(release, null)) Quit();
        }
        catch (UpdateException e)
        {
            tray?.Notify("更新失败", e.Message, Forms.ToolTipIcon.Warning);
        }
    }

    /// 剪贴板历史实际生效的快捷键；都注册不上时为 null
    public string? HistoryHotKey { get; private set; }

    public void ShowClipboardHistory() => coordinator?.ShowHistory();

    public void ClearClipboardHistory()
    {
        history?.Store.Clear(keepPinned: true);
        Log.Info("清空了剪贴板历史（保留固定的）");
    }

    public void Quit()
    {
        hotKeys?.Dispose();
        coordinator?.Dispose();
        history?.Dispose();
        hook?.Dispose();
        tray?.Dispose();
        Updater.Dispose();
        Log.Info("退出");
        Shutdown();
    }

    private void OnUpdaterChanged()
    {
        tray?.Refresh();
        if (Updater.Available is { } release && !notifiedVersion)
        {
            notifiedVersion = true;
            tray?.Notify($"Pop {release.Version} 可以更新了", "点任务栏右下角的 Pop 图标，选「更新到 " + release.Version + "」");
        }
    }

    private void SaveSettings()
    {
        try
        {
            Settings.Save(Paths.Settings);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Error("保存设置失败", e);
        }
    }

    /// 上一次更新留下的 Pop.exe.old：旧进程可能还没退出，隔一会儿再删
    private static void CleanUpOldExecutable()
    {
        if (ExecutableSwap.CleanUp(Paths.Executable)) return;
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                if (ExecutableSwap.CleanUp(Paths.Executable)) return;
                await Task.Delay(1000);
            }
        });
    }

    /// --update-now：不显示界面，检查并安装更新（自动化测试用）
    private async Task RunHeadlessUpdateAsync()
    {
        try
        {
            var release = await Updater.CheckAsync(userInitiated: true);
            if (release is null)
            {
                SmokeTest.Report($"no-update version={Updater.CurrentVersion}");
            }
            else if (!await Updater.InstallAsync(release, null))
            {
                SmokeTest.Report("update-error=busy");
            }
        }
        catch (UpdateException e)
        {
            SmokeTest.Report($"update-error={e.Message}");
        }
        Quit();
    }
}

/// 自动化测试：设置了 POP_SMOKE_MARKER 时把关键状态写进这个文件
internal static class SmokeTest
{
    private static readonly string? Marker = Environment.GetEnvironmentVariable("POP_SMOKE_MARKER");

    public static bool ExitAfterStart => Environment.GetEnvironmentVariable("POP_SMOKE_EXIT") == "1";

    public static void Report(string line)
    {
        Log.Info($"自检：{line}");
        if (string.IsNullOrEmpty(Marker)) return;
        try
        {
            File.AppendAllText(Marker, line + Environment.NewLine);
        }
        catch (IOException e)
        {
            Log.Error("写自检结果失败", e);
        }
    }
}
