using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 剪贴板历史：监听剪贴板，存进本机的 SQLite，按保存天数和条数上限每小时清理一次（固定的不清理）
internal sealed class ClipboardHistory : IDisposable
{
    private readonly Func<AppSettings> settings;
    private readonly DispatcherTimer cleanupTimer = new() { Interval = TimeSpan.FromHours(1) };

    public ClipboardStore Store { get; }
    public ClipboardMonitor Monitor { get; } = new();

    public ClipboardHistory(Func<AppSettings> settings)
    {
        this.settings = settings;
        Store = new ClipboardStore(ClipboardStore.DefaultDirectory);
        if (Store.OpenError is { } error) Log.Error($"打不开剪贴板历史数据库：{error}");
        Monitor.Captured += OnCaptured;
        cleanupTimer.Tick += (_, _) => Cleanup();
        cleanupTimer.Start();
        Cleanup();
    }

    public void Dispose()
    {
        cleanupTimer.Stop();
        Monitor.Dispose();
        Store.Dispose();
    }

    public void Cleanup()
    {
        var s = settings();
        var removed = Store.Cleanup(s.ClipboardRetentionDays, s.ClipboardMaxItems, DateTimeOffset.Now);
        if (removed > 0) Log.Info($"剪贴板历史：清理了 {removed} 条");
    }

    private void OnCaptured(ClipboardCapture capture)
    {
        var s = settings();
        if (!s.ClipboardHistory) return;
        if (capture.SourceApp is { Length: > 0 } app && s.ClipboardExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase)) return;
        if (Store.Add(capture, DateTimeOffset.Now) is not null)
            Log.Info($"剪贴板历史：记录一条{Name(capture.Kind)}{(string.IsNullOrEmpty(capture.SourceApp) ? "" : $"（来自 {capture.SourceApp}）")}");
    }

    public static string Name(ClipboardKind kind) => kind switch
    {
        ClipboardKind.Image => "图片",
        ClipboardKind.Files => "文件",
        _ => "文字",
    };
}
