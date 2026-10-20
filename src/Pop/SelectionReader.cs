using System.Diagnostics;
using Pop.Core;
using static Pop.Native;

namespace Pop;

internal sealed record Selection(string Text, string Source, string WindowClass, string Process, Rect? Bounds = null);

/// 读取当前 App 里选中的文字：先问 UI Automation，读不到再模拟 Ctrl+C（会备份并还原剪贴板）。
/// 在界面线程上调用。
internal sealed class SelectionReader(ClipboardAccess clipboard)
{
    /// 马上要模拟 Ctrl+C（剪贴板历史借这个跳过这一次变化）
    public event Action? CopyStarting;

    private static readonly TimeSpan AutomationTimeout = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(1000);
    // Chromium 内核的窗口（Chrome、Edge、Electron）第一次被 UI Automation 访问时要先建立无障碍树，
    // 这期间对 Ctrl+C 的响应能慢到两秒多
    private static readonly TimeSpan ChromiumCopyTimeout = TimeSpan.FromMilliseconds(2500);
    // 放弃等待之后再守这么久：App 晚一步写进剪贴板的内容要清掉，把原来的恢复回去
    private static readonly TimeSpan LateCopyGuard = TimeSpan.FromSeconds(3);

    /// 读取期间剪贴板的变化都不是用户复制的，剪贴板历史要跳过这么久
    public static readonly TimeSpan CopyIgnoreWindow = ChromiumCopyTimeout + LateCopyGuard + TimeSpan.FromMilliseconds(500);

    public async Task<Selection> ReadAsync()
    {
        var foreground = GetForegroundWindow();
        var windowClass = WindowClass(foreground);
        var process = ProcessNameOf(foreground);

        // 读取前先记一笔：如果在读取过程中闪退，日志的最后一行能看出是在哪个窗口
        Log.Info($"读取选中内容：窗口 {windowClass}（{process}）");
        var (supported, text, _) = await ReadWithAutomationAsync();
        if (GetForegroundWindow() != foreground) return new Selection("", "focus-changed", windowClass, process);
        if (!string.IsNullOrEmpty(text)) return new Selection(text, "uia", windowClass, process);

        // 支持文字接口、明确说没有选中内容的，就信它；浏览器和 Electron 的接口不一定准，还是再试一次复制
        var unreliable = windowClass.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) || windowClass.StartsWith("MozillaWindowClass", StringComparison.Ordinal);
        if (supported && !unreliable) return new Selection("", "uia", windowClass, process);

        if (TerminalApps.IsTerminal(windowClass, process))
        {
            // 终端里 Ctrl+C 会结束正在运行的程序
            return new Selection("", "terminal", windowClass, process);
        }

        var timeout = windowClass.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) ? ChromiumCopyTimeout : CopyTimeout;
        return new Selection(await ReadWithCopyAsync(timeout, foreground) ?? "", "copy", windowClass, process);
    }

    /// 在单独的线程上读，目标 App 卡住时不拖累 Pop。
    /// 用 Windows 原生的 UI Automation（UIAutomationCore 的 COM 接口）：.NET 自带的 System.Windows.Automation
    /// 是旧的客户端实现，读 Chrome 时会在原生代码里崩溃，整个进程直接退出，try/catch 也接不住。
    private static int automationBusy;

    /// 自动工具条只询问辅助功能，不模拟 Ctrl+C，也不接触剪贴板。
    public async Task<Selection> ReadPassiveAsync(IntPtr foreground)
    {
        if (GetForegroundWindow() != foreground) return new("", "focus-changed", "", "");
        var process = ProcessNameOf(foreground);
        var windowClass = WindowClass(foreground);
        var (_, text, bounds) = await ReadWithAutomationAsync(withBounds: true);
        return GetForegroundWindow() == foreground
            ? new(text ?? "", "uia-passive", windowClass, process, bounds) : new("", "focus-changed", windowClass, process);
    }

    private static async Task<(bool Supported, string? Text, Rect? Bounds)> ReadWithAutomationAsync(bool withBounds = false)
    {
        // 某个 UIA 提供程序一直不返回时，不为每次拖选继续创建挂起的线程。
        if (Interlocked.CompareExchange(ref automationBusy, 1, 0) != 0) return (false, null, null);
        var source = new TaskCompletionSource<(bool, string?, Rect?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                source.TrySetResult(NativeAutomation.ReadFocusedSelection(withBounds));
            }
            catch (Exception e)
            {
                Log.Info($"UI Automation 读取失败：{e.GetType().Name} {e.Message}");
                source.TrySetResult((false, null, null));
            }
            finally { Volatile.Write(ref automationBusy, 0); }
        })
        {
            IsBackground = true,
            Name = "Pop 读取选中内容",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        var done = await Task.WhenAny(source.Task, Task.Delay(AutomationTimeout));
        if (done == source.Task) return source.Task.Result;
        Log.Info($"UI Automation 读取超时（{AutomationTimeout.TotalMilliseconds} ms）");
        return (false, null, null);
    }

    private async Task<string?> ReadWithCopyAsync(TimeSpan timeout, IntPtr target)
    {
        var snapshot = clipboard.Save();
        if (GetForegroundWindow() != target) return null;
        var before = ClipboardAccess.SequenceNumber;
        CopyStarting?.Invoke();
        InputInjector.CtrlChord(0x43); // C
        var watch = Stopwatch.StartNew();
        while (ClipboardAccess.SequenceNumber == before && watch.Elapsed < timeout)
            await Task.Delay(15);
        if (ClipboardAccess.SequenceNumber == before)
        {
            // 没有选中内容，或者 App 还没来得及响应；它晚一步写进来的话要恢复原来的剪贴板
            _ = GuardLateCopyAsync(before, snapshot);
            return null;
        }

        // App 可能分几次写入不同格式
        await Task.Delay(40);
        var text = clipboard.GetText();
        clipboard.Restore(snapshot);
        return text;
    }

    private async Task GuardLateCopyAsync(uint before, ClipboardAccess.Snapshot? snapshot)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < LateCopyGuard)
        {
            await Task.Delay(50);
            if (ClipboardAccess.SequenceNumber == before) continue;
            await Task.Delay(40);
            clipboard.Restore(snapshot);
            Log.Info($"模拟复制晚到了 {watch.ElapsedMilliseconds} ms，已恢复原来的剪贴板");
            return;
        }
    }
}
