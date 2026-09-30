using System.Diagnostics;
using Pop.Core;
using static Pop.Native;

namespace Pop;

internal sealed record Selection(string Text, string Source, string WindowClass, string Process);

/// 读取当前 App 里选中的文字：先问 UI Automation，读不到再模拟 Ctrl+C（会备份并还原剪贴板）。
/// 在界面线程上调用。
internal sealed class SelectionReader(ClipboardAccess clipboard)
{
    /// 马上要模拟 Ctrl+C（剪贴板历史借这个跳过这一次变化）
    public event Action? CopyStarting;

    private static readonly TimeSpan AutomationTimeout = TimeSpan.FromMilliseconds(600);
    // 浏览器第一次被 UI Automation 访问时要先建立无障碍树，这期间对 Ctrl+C 的响应也会慢一些
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(1000);

    public async Task<Selection> ReadAsync()
    {
        var foreground = GetForegroundWindow();
        var windowClass = WindowClass(foreground);
        var process = ProcessNameOf(foreground);

        // 读取前先记一笔：如果在读取过程中闪退，日志的最后一行能看出是在哪个窗口
        Log.Info($"读取选中内容：窗口 {windowClass}（{process}）");
        var (supported, text) = await ReadWithAutomationAsync();
        if (!string.IsNullOrEmpty(text)) return new Selection(text, "uia", windowClass, process);

        // 支持文字接口、明确说没有选中内容的，就信它；浏览器和 Electron 的接口不一定准，还是再试一次复制
        var unreliable = windowClass.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) || windowClass.StartsWith("MozillaWindowClass", StringComparison.Ordinal);
        if (supported && !unreliable) return new Selection("", "uia", windowClass, process);

        if (TerminalApps.IsTerminal(windowClass, process))
        {
            // 终端里 Ctrl+C 会结束正在运行的程序
            return new Selection("", "terminal", windowClass, process);
        }

        return new Selection(await ReadWithCopyAsync() ?? "", "copy", windowClass, process);
    }

    /// 在单独的线程上读，目标 App 卡住时不拖累 Pop。
    /// 用 Windows 原生的 UI Automation（UIAutomationCore 的 COM 接口）：.NET 自带的 System.Windows.Automation
    /// 是旧的客户端实现，读 Chrome 时会在原生代码里崩溃，整个进程直接退出，try/catch 也接不住。
    private static async Task<(bool Supported, string? Text)> ReadWithAutomationAsync()
    {
        var source = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                source.TrySetResult(NativeAutomation.ReadFocusedSelection());
            }
            catch (Exception e)
            {
                Log.Info($"UI Automation 读取失败：{e.GetType().Name} {e.Message}");
                source.TrySetResult((false, null));
            }
        })
        {
            IsBackground = true,
            Name = "Pop 读取选中内容",
        };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
        var done = await Task.WhenAny(source.Task, Task.Delay(AutomationTimeout));
        if (done == source.Task) return source.Task.Result;
        Log.Info($"UI Automation 读取超时（{AutomationTimeout.TotalMilliseconds} ms），改用复制");
        return (false, null);
    }

    private async Task<string?> ReadWithCopyAsync()
    {
        var snapshot = clipboard.Save();
        var before = ClipboardAccess.SequenceNumber;
        CopyStarting?.Invoke();
        InputInjector.CtrlChord(0x43); // C
        var watch = Stopwatch.StartNew();
        while (ClipboardAccess.SequenceNumber == before && watch.Elapsed < CopyTimeout)
            await Task.Delay(15);
        if (ClipboardAccess.SequenceNumber == before) return null; // 没有选中内容，App 没往剪贴板里写东西

        // App 可能分几次写入不同格式
        await Task.Delay(40);
        var text = clipboard.GetText();
        clipboard.Restore(snapshot);
        return text;
    }
}

/// 把结果粘贴回原来的 App（替换选中的文字），粘贴完把剪贴板恢复原样
internal sealed class Paster(ClipboardAccess clipboard)
{
    public async Task PasteAsync(string text)
    {
        var snapshot = clipboard.Save();
        if (!clipboard.SetText(text, temporary: true)) return;
        InputInjector.CtrlChord(0x56); // V
        // 目标 App 读剪贴板需要一点时间，太早恢复会粘贴出原来的内容
        await Task.Delay(350);
        clipboard.Restore(snapshot);
    }
}
