namespace Pop;

/// DemoShots：演示模式，把各个界面依次显示出来截图存到这个文件夹（CI 用），DemoScenes 只拍其中几个
internal sealed record StartupOptions(bool UpdateNow, string? UpdatedFrom, bool Silent, bool ShowSettings, string? DemoShots = null, string? DemoScenes = null);

internal static class Program
{
    private const string MutexName = @"Local\io.github.whrss9527.pop";
    public const string ShowSettingsEventName = @"Local\io.github.whrss9527.pop.show-settings";

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Parse(args);
        // 任何线程上没接住的异常都记下来再退出，闪退时能从日志里看到原因
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error($"未处理的异常，Pop 将退出（线程 {Environment.CurrentManagedThreadId}）", e.ExceptionObject as Exception);
            SmokeTest.Report($"crash={(e.ExceptionObject as Exception)?.GetType().Name}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error("后台任务里没人处理的异常", e.Exception);
            e.SetObserved();
        };
        // 演示模式不装钩子、不改设置，可以和正在运行的 Pop 同时运行
        if (options.DemoShots is not null) return RunApp(options);

        using var mutex = new Mutex(false, MutexName);
        // 更新后启动的新版本要等旧进程退出
        var wait = options.UpdatedFrom is not null ? TimeSpan.FromSeconds(20) : TimeSpan.Zero;
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            owned = true;
        }
        if (!owned)
        {
            // 已经在运行：让它打开设置窗口（允许它切到前台），自己退出
            Log.Info("Pop 已经在运行，打开设置窗口后退出");
            if (options.UpdatedFrom is null && !options.UpdateNow && EventWaitHandle.TryOpenExisting(ShowSettingsEventName, out var signal))
            {
                AllowSetForegroundWindow(-1);
                signal.Set();
                signal.Dispose();
            }
            SmokeTest.Report("already-running");
            return 1;
        }

        try
        {
            return RunApp(options);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static int RunApp(StartupOptions options)
    {
        try
        {
            var app = new App(options);
            return app.Run();
        }
        catch (Exception e)
        {
            Log.Error("启动失败", e);
            SmokeTest.Report($"crash={e.GetType().Name}: {e.Message}");
            return 2;
        }
    }

    private static StartupOptions Parse(string[] args)
    {
        var updateNow = false;
        var silent = false;
        var settings = false;
        string? updatedFrom = null;
        string? demoShots = null;
        string? demoScenes = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--update-now":
                    updateNow = true;
                    break;
                case "--after-update":
                    updatedFrom = i + 1 < args.Length ? args[++i] : "?";
                    break;
                case "--silent":
                    silent = true;
                    break;
                case "--settings":
                    settings = true;
                    break;
                case "--demo-shots" when i + 1 < args.Length:
                    demoShots = args[++i];
                    break;
                case "--demo-scenes" when i + 1 < args.Length:
                    demoScenes = args[++i];
                    break;
            }
        }
        return new StartupOptions(updateNow, updatedFrom, silent, settings, demoShots, demoScenes);
    }
}
