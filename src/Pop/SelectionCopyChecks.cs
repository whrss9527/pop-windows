using System.Collections.Concurrent;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Pop.Core;

namespace Pop;

/// 在独立 Windows 演示中使用真实剪贴板、前台窗口和锁；不在用户电脑执行。
internal static class SelectionCopyChecks
{
    public static async Task VerifyAsync(App app)
    {
        if (!app.IsDemo) throw new InvalidOperationException("选区复制检查仅用于演示");
        using var writer = new ClipboardAccess("Pop 选区复制检查写入");
        using var access = new ClipboardAccess("Pop 选区复制检查读取");
        var original = writer.Save() ?? throw new InvalidOperationException("无法保留检查前的剪贴板");
        var text = new TextBox { Text = "guard-real-selection", Margin = new Thickness(16) };
        var target = new Window { Title = "Pop 选区复制检查", Width = 420, Height = 160, Content = text, ShowInTaskbar = false };
        long activity = 0;
        Func<bool> response = () => InputInjector.CtrlChord(0x43);
        using var reader = new SelectionReader(access, () => Interlocked.Read(ref activity), () => response());
        using var monitor = new ClipboardMonitor();
        var captured = new ConcurrentQueue<ClipboardCapture>();
        monitor.Captured += (capture, current) => { if (current()) captured.Enqueue(capture); };
        reader.CopyStarting += current => monitor.IgnoreChangesFor(SelectionReader.CopyIgnoreWindow, current);
        target.Show(); await Task.Delay(200);
        var hwnd = new WindowInteropHelper(target).Handle;
        async Task Focus()
        {
            target.Activate(); text.Focus(); Keyboard.Focus(text); text.SelectAll(); await Task.Delay(100);
            if (Native.GetForegroundWindow() != hwnd) throw new InvalidOperationException("选区检查未能取得目标前台");
        }
        void Reset() { if (!writer.SetText("guard-original", temporary: true)) throw new InvalidOperationException("无法准备原始剪贴板"); }
        void Copy(string value) { if (!writer.SetText(value, temporary: false)) throw new InvalidOperationException("无法准备复制响应"); }
        async Task<string?> Read(int timeout = 200) => await reader.ReadCopyForChecksAsync(TimeSpan.FromMilliseconds(timeout), hwnd);
        try
        {
            await Focus(); Reset();
            if (await Read(1200) != "guard-real-selection" || writer.GetText() != "guard-original")
                throw new InvalidOperationException("真实 Ctrl+C 未读取选区并还原原内容");
            if (access.Save() is not { } saved || saved.Sequence != ClipboardAccess.SequenceNumber)
                throw new InvalidOperationException("快照没有记录锁内最终序列");

            // 正常读取后不再整段忽略用户的下一次复制。
            Copy("guard-after-normal"); await Until(() => captured.Any(c => c.Text == "guard-after-normal"));
            if (captured.Any(c => c.Text == "guard-real-selection")) throw new InvalidOperationException("本次模拟复制混入历史");

            Reset();
            response = () => { Copy("guard-selected"); return true; };
            if (await Read() != "guard-selected" || writer.GetText() != "guard-original") throw new InvalidOperationException("受控正常响应未还原");

            Reset(); Task? newer = null;
            response = () =>
            {
                Copy("guard-selected");
                newer = Later(10, () => { Interlocked.Increment(ref activity); Copy("guard-new-user"); });
                return true;
            };
            if (await Read() is not null) throw new InvalidOperationException("用户新复制仍被当作本次选区");
            await newer!;
            if (writer.GetText() != "guard-new-user") throw new InvalidOperationException("正常读取恢复覆盖了同应用新复制");
            await Until(() => captured.Any(c => c.Text == "guard-new-user"));

            Reset(); Task? delayed = null;
            response = () => { delayed = Later(180, () => Copy("guard-late-target")); return true; };
            if (await Read(40) is not null) throw new InvalidOperationException("超时响应应先返回空选区");
            await delayed!; await reader.PendingLateCopy;
            if (writer.GetText() != "guard-original") throw new InvalidOperationException("可信的迟到响应没有还原");

            Reset(); response = () => true;
            await Read(30);
            Interlocked.Increment(ref activity); Copy("guard-after-timeout");
            await reader.PendingLateCopy;
            if (writer.GetText() != "guard-after-timeout") throw new InvalidOperationException("超时守卫覆盖了用户新复制");
            await Until(() => captured.Any(c => c.Text == "guard-after-timeout"));

            Reset(); await Read(30); var previous = reader.PendingLateCopy;
            response = () => { Copy("guard-second-read"); return true; };
            if (await Read() != "guard-second-read") throw new InvalidOperationException("新读取被旧守卫干扰");
            if (await Task.WhenAny(previous, Task.Delay(500)) != previous) throw new InvalidOperationException("新读取未取消旧守卫");
            await previous;

            Reset();
            response = () =>
            {
                using var unowned = new ClipboardAccess("Pop 无所有者检查");
                return unowned.SetText("guard-unowned", temporary: true);
            };
            if (await Read() is not null || writer.GetText() != "guard-unowned") throw new InvalidOperationException("未知所有者内容未保留");

            await VerifyForeignOwner(reader, writer, hwnd, action => response = action);
            await VerifyFocusChange(reader, writer, target, hwnd, action => response = action);
            await Focus();
            await SelectionClipboardLockChecks.VerifyAsync(hwnd);

            Reset(); response = () => false;
            if (await Read() is not null || writer.GetText() != "guard-original") throw new InvalidOperationException("发送失败后仍读取或还原");
            Reset(); response = () => true; await Read(30); var pending = reader.PendingLateCopy;
            reader.Dispose(); Copy("guard-after-dispose");
            if (await Task.WhenAny(pending, Task.Delay(500)) != pending) throw new InvalidOperationException("释放读取器没有取消迟到守卫");
            await pending;
            if (writer.GetText() != "guard-after-dispose") throw new InvalidOperationException("释放后仍覆盖剪贴板");
        }
        finally
        {
            reader.Dispose(); monitor.Enabled = false; target.Close();
            // 检查中的无所有者/外部进程会改变所有权，只接管本组已知的哨兵文本再还原。
            if (writer.GetText()?.StartsWith("guard-", StringComparison.Ordinal) == true) writer.SetText("", temporary: true);
            if (writer.OwnsClipboard) writer.Restore(original, onlyIfOwned: true);
        }
    }

    private static async Task VerifyForeignOwner(SelectionReader reader, ClipboardAccess writer, IntPtr target, Action<Func<bool>> setResponse)
    {
        using var foreign = await SelectionForeignClipboard.StartAsync();
        // 外部进程在保存快照后才写入；不能把它读成当前选区或用旧副本覆盖。
        setResponse(() => { foreign.Write(); return true; });
        if (await reader.ReadCopyForChecksAsync(TimeSpan.FromMilliseconds(300), target) is not null || writer.GetText() != "guard-foreign")
            throw new InvalidOperationException("其他进程的新复制没有保留");
        Native.GetWindowThreadProcessId(Native.GetClipboardOwner(), out var owner);
        if (owner == 0 || owner == Environment.ProcessId) throw new InvalidOperationException("独立所有者检查没有覆盖实际其他进程");
    }

    private static async Task VerifyFocusChange(SelectionReader reader, ClipboardAccess writer, Window target, IntPtr hwnd, Action<Func<bool>> setResponse)
    {
        var other = new Window { Title = "Pop 另一个前台", Width = 240, Height = 100, ShowInTaskbar = false };
        Task? change = null;
        try
        {
            writer.SetText("guard-original", temporary: true);
            setResponse(() => { change = Later(15, () => { other.Show(); other.Activate(); writer.SetText("guard-other-window", temporary: true); }); return true; });
            if (await reader.ReadCopyForChecksAsync(TimeSpan.FromMilliseconds(300), hwnd) is not null) throw new InvalidOperationException("切换窗口后仍读取旧选区");
            await change!;
            if (writer.GetText() != "guard-other-window") throw new InvalidOperationException("换窗口后的内容被旧读取覆盖");
        }
        finally { other.Close(); target.Activate(); }
    }

    private static async Task Later(int milliseconds, Action action) { await Task.Delay(milliseconds); action(); }
    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(10);
        if (!condition()) throw new InvalidOperationException("选区复制检查等待历史记录超时");
    }
}
