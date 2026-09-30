namespace Pop;

internal sealed record StartupOptions(bool UpdateNow, string? UpdatedFrom, bool Silent);

internal static class Program
{
    private const string MutexName = @"Local\io.github.whrss9527.pop";

    [STAThread]
    private static int Main(string[] args)
    {
        var options = Parse(args);
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
            Log.Info("Pop 已经在运行，退出");
            SmokeTest.Report("already-running");
            return 1;
        }

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
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static StartupOptions Parse(string[] args)
    {
        var updateNow = false;
        var silent = false;
        string? updatedFrom = null;
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
            }
        }
        return new StartupOptions(updateNow, updatedFrom, silent);
    }
}
