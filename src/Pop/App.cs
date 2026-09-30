using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pop.Core;
using Wpf.Ui.Appearance;
using Forms = System.Windows.Forms;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

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
        // 演示模式用默认设置，也不保存，不影响真正的设置
        Settings = options.DemoShots is null ? AppSettings.Load(Paths.Settings) : new AppSettings();
        Updater = new Updater(() => Settings);
    }

    public AppSettings Settings { get; private set; }
    public Updater Updater { get; }

    /// 用户的插件（插件文件夹）；OnStartup 里创建
    public PluginStore Plugins { get; private set; } = null!;

    /// 运行插件：脚本的临时文件放在 %LOCALAPPDATA%\Pop\plugin-scripts
    public PluginRunner PluginRunner { get; } = new(scriptDirectory: Path.Combine(Paths.LocalData, "plugin-scripts"));

    /// 插件增加、删除或者改了（界面线程上触发）
    public event Action? PluginsChanged;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("界面线程未处理的异常", args.Exception);
            args.Handled = true;
        };
        SetUpAppearance();
        if (options.DemoShots is { } shots)
        {
            // 演示用的插件放在临时文件夹里
            SetUpPlugins(Demo.PluginFolder());
            // 演示模式不注册快捷键，界面上显示默认的那几个
            HistoryHotKey = "Win+Alt+V";
            OcrHotKey = "Win+Alt+O";
            PinHotKey = "Win+Alt+P";
            await Demo.RunAsync(this, shots, options.DemoScenes);
            Shutdown();
            return;
        }

        Log.Info($"Pop {Updater.CurrentVersion} 启动，{Paths.Executable}{(options.UpdatedFrom is { } from ? $"（从 {from} 更新而来）" : "")}");
        CleanUpOldExecutable();

        if (options.UpdateNow)
        {
            await RunHeadlessUpdateAsync();
            return;
        }

        SetUpPlugins(Paths.Plugins);
        hook = new InputHook { Enabled = Settings.Enabled, HoldMilliseconds = Settings.HoldMilliseconds };
        hook.Start();
        history = new ClipboardHistory(() => Settings);
        coordinator = new Coordinator(Dispatcher, hook, () => Settings, history, () => Plugins, PluginRunner);
        hotKeys = new HotKeys();
        // Win+Shift+V 被系统占用了；默认用 Win+Alt+V，也被占用时依次换下一个
        HistoryHotKey = RegisterFirst("剪贴板历史", () => coordinator.ShowHistory(), (HotKeys.MOD_WIN | HotKeys.MOD_ALT, 0x56, "Win+Alt+V"),
            (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x56, "Ctrl+Alt+Shift+V"), (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x48, "Ctrl+Alt+Shift+H"));
        OcrHotKey = RegisterFirst("截图识字", () => _ = coordinator.CaptureTextAsync(), (HotKeys.MOD_WIN | HotKeys.MOD_ALT, 0x4F, "Win+Alt+O"),
            (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x4F, "Ctrl+Alt+Shift+O"));
        PinHotKey = RegisterFirst("截图贴图", () => _ = coordinator.CapturePinAsync(), (HotKeys.MOD_WIN | HotKeys.MOD_ALT, 0x50, "Win+Alt+P"),
            (HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, 0x50, "Ctrl+Alt+Shift+P"));
        tray = new TrayIcon(this);
        ListenForSecondInstance();
        Updater.Changed += OnUpdaterChanged;
        Updater.StartSchedule();

        if (options.UpdatedFrom is not null)
            tray.Notify("Pop 已更新", $"现在是 {Updater.CurrentVersion}");
        else if (!options.Silent && !System.IO.File.Exists(Paths.Settings))
            tray.Notify("Pop 已在运行", "在任意 App 里选中文字，长按鼠标右键试试。点任务栏右下角的 Pop 图标可以打开面板和设置。");
        if (!System.IO.File.Exists(Paths.Settings)) SaveSettings();

        if (options.ShowSettings) ShowSettings();
        SmokeTest.Report($"started version={Updater.CurrentVersion} hooks={(hook.IsInstalled ? "ok" : "failed")} updated-from={options.UpdatedFrom ?? "-"}");
        if (SmokeTest.ExitAfterStart) Quit();
    }

    /// 设置改了（设置窗口和托盘面板都会改，互相刷新）
    public event Action? SettingsChanged;

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
        SettingsChanged?.Invoke();
    }

    public bool IsDemo => options.DemoShots is not null;

    // ── 插件 ──────────────────────────────────────────

    private void SetUpPlugins(string directory)
    {
        Plugins = new PluginStore(directory);
        Actions.SetPlugins(PluginRunner.ToActions(Plugins.Manifests));
        Plugins.Changed += (_, _) => Dispatcher.BeginInvoke(OnPluginsChanged);
        if (!IsDemo) Plugins.StartWatching();
        Log.Info($"插件：{Plugins.Manifests.Count} 个{(Plugins.LoadErrors.Count > 0 ? $"，{Plugins.LoadErrors.Count} 个文件读不了" : "")}");
    }

    private void OnPluginsChanged()
    {
        Actions.SetPlugins(PluginRunner.ToActions(Plugins.Manifests));
        Log.Info($"插件有变化：现在 {Plugins.Manifests.Count} 个{(Plugins.LoadErrors.Count > 0 ? $"，{Plugins.LoadErrors.Count} 个文件读不了" : "")}");
        PluginsChanged?.Invoke();
    }

    // ── 外观 ──────────────────────────────────────────

    /// WPF-UI 的控件样式和配色，跟随系统的深浅色和主题色
    private void SetUpAppearance()
    {
        var dark = Theme.SystemIsDark();
        Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ThemesDictionary { Theme = dark ? ApplicationTheme.Dark : ApplicationTheme.Light });
        Resources.MergedDictionaries.Add(new Wpf.Ui.Markup.ControlsDictionary());
        // 右键菜单和提示框不在窗口里面，字体要单独指定
        foreach (var type in new[] { typeof(ContextMenu), typeof(ToolTip) })
        {
            var style = new Style(type, TryFindResource(type) as Style);
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Theme.TextFont));
            Resources[type] = style;
        }
        ApplyAppearance();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    private void ApplyAppearance()
    {
        var theme = Theme.SystemIsDark() ? ApplicationTheme.Dark : ApplicationTheme.Light;
        // WPF-UI 换配色时会顺带改「主窗口」的窗口样式；主窗口默认是第一个创建的浮窗，这里临时换成设置窗口
        var main = MainWindow;
        MainWindow = settingsWindow;
        try
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.Mica, updateAccent: false);
        }
        finally
        {
            MainWindow = main;
        }
        // 用系统调色板里的主题色（和系统控件一样），不是按亮度推算的
        ApplicationAccentColorManager.Apply(ApplicationAccentColorManager.GetColorizationColor(), theme, systemGlassColor: false, systemAccentColor: true);
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke(ApplyAppearance);
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

    /// 实际生效的快捷键；都注册不上时为 null
    public string? HistoryHotKey { get; private set; }
    public string? OcrHotKey { get; private set; }
    public string? PinHotKey { get; private set; }

    /// 依次尝试注册，返回注册上的那一个
    private string? RegisterFirst(string title, Action handler, params (uint Modifiers, uint Key, string Name)[] candidates)
    {
        foreach (var (modifiers, key, name) in candidates)
            if (hotKeys!.Register(modifiers, key, $"{name}（{title}）", handler)) return name;
        return null;
    }

    public void CaptureText() => _ = coordinator?.CaptureTextAsync();

    public void CapturePin() => _ = coordinator?.CapturePinAsync();

    public void ShowClipboardHistory() => coordinator?.ShowHistory();

    public (int Count, long Bytes) ClipboardStatistics() => history?.Store.Statistics() ?? (0, 0);

    private SettingsWindow? settingsWindow;

    public void ShowSettings() => ShowSettings(null);

    /// 打开设置窗口，page 是要显示的那一页（SettingsWindow.Pages 里的 ID）
    public SettingsWindow ShowSettings(string? page)
    {
        if (settingsWindow is { IsLoaded: true })
        {
            if (settingsWindow.WindowState == WindowState.Minimized) settingsWindow.WindowState = WindowState.Normal;
            if (page is not null) settingsWindow.Navigate(page);
            settingsWindow.Activate();
            return settingsWindow;
        }
        settingsWindow = new SettingsWindow(this, page);
        settingsWindow.Closed += (_, _) => settingsWindow = null;
        settingsWindow.Show();
        settingsWindow.Activate();
        Log.Info("设置窗口已打开");
        return settingsWindow;
    }

    /// 再次运行 Pop.exe 时，正在运行的 Pop 打开设置窗口
    private void ListenForSecondInstance()
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, Program.ShowSettingsEventName);
        ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => Dispatcher.BeginInvoke(() => ShowSettings()), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void ClearClipboardHistory()
    {
        history?.Store.Clear(keepPinned: true);
        Log.Info("清空了剪贴板历史（保留固定的）");
    }

    public void Quit()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        hotKeys?.Dispose();
        coordinator?.Dispose();
        Plugins?.Dispose();
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
            tray?.Notify($"Pop {release.Version} 可以更新了", "点任务栏右下角的 Pop 图标，在面板上点「更新」");
        }
    }

    private void SaveSettings()
    {
        if (IsDemo) return;
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
