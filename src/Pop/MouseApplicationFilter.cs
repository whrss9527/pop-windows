using Pop.Core;

namespace Pop;

/// 在后台解析前台程序，输入钩子只读取快照，不打开进程或访问文件。
/// 刚切换窗口、快照尚未就绪时保留原 App 的点击。
internal sealed class MouseApplicationFilter : IDisposable
{
    private sealed record Snapshot(IntPtr Window, uint Process, string[] Excluded, bool Allowed);
    private volatile string[] excluded = [];
    private volatile Snapshot? snapshot;
    private readonly Timer timer;

    public MouseApplicationFilter() => timer = new Timer(_ => Refresh(), null, 0, 40);

    public void Update(IEnumerable<string> names)
    {
        var next = names.Select(AppSettings.ApplicationName).Where(n => n.Length > 0).Distinct().Order().ToArray();
        if (!excluded.SequenceEqual(next)) excluded = next;
    }

    public bool Allows(IntPtr window, uint process)
    {
        if (process == 0 || process == Environment.ProcessId) return false;
        var names = excluded;
        if (names.Length == 0) return true;
        return snapshot is { } value && value.Window == window && value.Process == process
            && ReferenceEquals(value.Excluded, names) && value.Allowed;
    }

    private void Refresh()
    {
        var names = excluded;
        if (names.Length == 0) return;
        var window = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(window, out var process);
        if (snapshot is { } old && old.Window == window && old.Process == process && ReferenceEquals(old.Excluded, names)) return;
        var name = AppSettings.ApplicationName(Native.ProcessNameOf(window));
        snapshot = new(window, process, names, name.Length > 0 && !names.Contains(name));
    }

    public void Dispose() => timer.Dispose();
}
