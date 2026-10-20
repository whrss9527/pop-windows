using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Pop.Core;
using Wpf.Ui.Appearance;
using WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType;

namespace Pop;

internal sealed class App : Application
{
    private readonly StartupOptions options;
    private InputHook? hook;
    private Coordinator? coordinator;
    private UiWatchdog? watchdog;
    private TrayIcon? tray;
    private ClipboardHistory? history;
    private HotKeys? hotKeys;
    private string? shortcutSignature;
    private bool shortcutsSuspended;
    private readonly Dictionary<string, string?> activeShortcuts = new(StringComparer.OrdinalIgnoreCase);
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
            AnnotateHotKey = "Win+Alt+A";
            ColorHotKey = "Win+Alt+C";
            RingHotKey = "Win+Alt+Space";
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
        hook = new InputHook { Enabled = Settings.Enabled, HoldMilliseconds = Settings.HoldMilliseconds,
            TriggerMode = Settings.TriggerMode, TriggerModifier = Settings.TriggerModifier, SelectionToolbar = Settings.SelectionToolbar };
        hook.ExcludeApplications(Settings.TriggerExcludedApps);
        hook.Start();
        NativeAutomation.WarmUp();
        history = new ClipboardHistory(() => Settings);
        coordinator = new Coordinator(Dispatcher, hook, () => Settings, history, () => Plugins, PluginRunner);
        watchdog = new UiWatchdog(Dispatcher);
        ConfigureHotKeys();
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
        if (!SmokeTest.ExitAfterStart) _ = Dispatcher.BeginInvoke(() => coordinator.WarmUp(), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        SmokeTest.Report($"started version={Updater.CurrentVersion} hooks={(hook.IsInstalled ? "ok" : "failed")} updated-from={options.UpdatedFrom ?? "-"}");
        if (SmokeTest.ExitAfterStart) Quit();
    }

    /// 设置改了（设置窗口和托盘面板都会改，互相刷新）
    public event Action? SettingsChanged;

    public void UpdateSettings(Action<AppSettings> change)
    {
        change(Settings);
        SaveSettings();
        ApplySettings();
    }

    private void ApplySettings()
    {
        if (hook is not null)
        {
            hook.Enabled = Settings.Enabled;
            hook.HoldMilliseconds = Settings.HoldMilliseconds;
            hook.TriggerMode = Settings.TriggerMode;
            hook.TriggerModifier = Settings.TriggerModifier;
            hook.SelectionToolbar = Settings.SelectionToolbar;
            hook.ExcludeApplications(Settings.TriggerExcludedApps);
        }
        coordinator?.SettingsUpdated();
        history?.Cleanup();
        ConfigureHotKeys();
        SettingsChanged?.Invoke();
    }

    public bool IsDemo => options.DemoShots is not null;

    public string ImportBackup(SettingsBackup backup)
    {
        var directory = Path.Combine(Paths.RoamingData, "Backups");
        Directory.CreateDirectory(directory);
        var recovery = Path.Combine(directory, $"导入前-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.json");
        File.WriteAllText(recovery, new SettingsBackup(Settings, Plugins.Manifests, Updater.CurrentVersion.ToString()).ToJson());
        backup.Restore(Paths.Settings, Plugins);
        Settings = AppSettings.FromJson(backup.Settings.ToJson());
        ApplySettings();
        return recovery;
    }

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
        ConfigureHotKeys();
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
            tray?.Notify("Pop", e.Message, warning: true);
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
            tray?.Notify("更新失败", e.Message, warning: true);
        }
    }

    /// 实际生效的快捷键；都注册不上时为 null
    public string? HistoryHotKey { get; private set; }
    public string? OcrHotKey { get; private set; }
    public string? PinHotKey { get; private set; }
    public string? AnnotateHotKey { get; private set; }
    public string? ColorHotKey { get; private set; }
    public string? RingHotKey { get; private set; }

    public string? ShortcutText(string id) => IsDemo
        ? KeyboardShortcut.Effective(Settings.ActionShortcuts).GetValueOrDefault(id)?.Display
        : activeShortcuts.GetValueOrDefault(id);

    public string? ShortcutProblem(string id) => !Settings.IsEnabled(id) ? "功能已关闭，启用后快捷键生效"
        : KeyboardShortcut.Effective(Settings.ActionShortcuts).ContainsKey(id) && ShortcutText(id) is null
            ? "快捷键被系统或其他程序占用，请换一个组合" : null;

    public bool SetShortcut(string id, KeyboardShortcut? shortcut, out string? error)
    {
        error = null;
        if (shortcut is { Modifiers: 2, Key: 0x43 or 0x56 }) { error = "Ctrl+C / Ctrl+V 用于复制粘贴，请选择其他组合"; return false; }
        if (shortcut is not null && !shortcut.IsValid) { error = "请使用 Ctrl、Alt 或 Win 加字母、数字、空格或功能键"; return false; }
        var candidate = new Dictionary<string, KeyboardShortcut?>(Settings.ActionShortcuts, StringComparer.OrdinalIgnoreCase) { [id] = shortcut };
        if (shortcut is not null && KeyboardShortcut.Effective(candidate).Any(p => p.Key != id && p.Value == shortcut))
        { error = "这个组合已经分配给 Pop 的其他功能"; return false; }
        UpdateSettings(s => s.ActionShortcuts[id] = shortcut);
        return true;
    }

    public void SuspendShortcuts(bool suspended)
    {
        shortcutsSuspended = suspended;
        shortcutSignature = null;
        if (suspended) { hotKeys?.Dispose(); hotKeys = null; }
        else ConfigureHotKeys();
    }

    private void ConfigureHotKeys()
    {
        if (IsDemo || coordinator is null || shortcutsSuspended) return;
        var signature = System.Text.Json.JsonSerializer.Serialize(Settings.ActionShortcuts)
            + string.Join(',', Settings.DisabledActions.Order()) + string.Join(',', Actions.List.Select(a => a.Id));
        if (signature == shortcutSignature) return;
        shortcutSignature = signature;
        hotKeys?.Dispose(); hotKeys = new HotKeys(); activeShortcuts.Clear();
        var bindings = KeyboardShortcut.Effective(Settings.ActionShortcuts);
        foreach (var (id, shortcut) in bindings)
        {
            if (id != KeyboardShortcut.RingTarget && (!Settings.IsEnabled(id) || Actions.Find(id) is null)) continue;
            var target = id;
            var title = id == KeyboardShortcut.RingTarget ? "圆盘" : Actions.Find(id)!.Title;
            Action handler = id == KeyboardShortcut.RingTarget ? coordinator.ShowRingFromShortcut : () => coordinator.RunActionFromShortcut(target);
            var candidates = new List<KeyboardShortcut> { shortcut };
            if (!Settings.ActionShortcuts.ContainsKey(id) && id != KeyboardShortcut.RingTarget)
            {
                var fallback = new KeyboardShortcut(HotKeys.MOD_CONTROL | HotKeys.MOD_ALT | HotKeys.MOD_SHIFT, shortcut.Key);
                if (!bindings.Any(p => p.Key != id && p.Value == fallback)) candidates.Add(fallback);
                if (id == "clipboard" && !bindings.Any(p => p.Key != id && p.Value == new KeyboardShortcut(7, 0x48))) candidates.Add(new(7, 0x48));
            }
            activeShortcuts[id] = null;
            foreach (var candidate in candidates)
            {
                if (!hotKeys.Register(candidate.Modifiers, candidate.Key, $"{candidate.Display}（{title}）", handler)) continue;
                activeShortcuts[id] = candidate.Display; break;
            }
        }
        HistoryHotKey = ShortcutText("clipboard"); OcrHotKey = ShortcutText("ocr"); PinHotKey = ShortcutText("pin");
        AnnotateHotKey = ShortcutText("annotate"); ColorHotKey = ShortcutText("colorPicker"); RingHotKey = ShortcutText(KeyboardShortcut.RingTarget);
    }

    public void CaptureText() => _ = coordinator?.CaptureTextAsync();

    public void CapturePin() => _ = coordinator?.CapturePinAsync();
    public void CaptureAnnotate() => _ = coordinator?.CaptureAnnotateAsync();
    public void PickColor() => _ = coordinator?.PickColorAsync();
    public void MeasureScreen() => _ = coordinator?.MeasureScreenAsync();
    public void ShowSnippets() => coordinator?.RunActionFromShortcut(Actions.Snippets.Id);
    public bool IsKeepingAwake => coordinator?.IsKeepingAwake == true;
    public string AwakeStatus => coordinator?.AwakeStatus ?? "当前未保持唤醒";
    public void ShowKeepAwake() => coordinator?.RunActionFromShortcut(Actions.Awake.Id);

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
        watchdog?.Dispose();
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
