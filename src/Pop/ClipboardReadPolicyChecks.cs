using System.Collections.Concurrent;
using Pop.Core;

namespace Pop;

/// 控制隐私格式检查与正文读取之间的写入，不依赖时序碰撞，也不使用用户数据。
internal static class ClipboardReadPolicyChecks
{
    public static async Task VerifyAsync(App app)
    {
        if (!app.IsDemo) throw new InvalidOperationException("剪贴板读取范围检查仅用于独立演示");
        using var writer = new ClipboardAccess("Pop 捕获范围检查");
        var original = writer.Save() ?? throw new InvalidOperationException("无法保留检查前剪贴板");
        using var proceed = new ManualResetEventSlim();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = 0;
        using var monitor = new ClipboardMonitor(beforeContentForDemo: () =>
        {
            if (Interlocked.Exchange(ref paused, 1) != 0) return;
            entered.TrySetResult(true);
            proceed.Wait(TimeSpan.FromSeconds(10));
        });
        var captures = new ConcurrentQueue<ClipboardCapture>();
        monitor.Captured += (capture, current) => { if (current()) captures.Enqueue(capture); };
        try
        {
            var completed = monitor.CompletedCaptures;
            var contentReads = monitor.ContentReadAttempts;
            if (!writer.SetText("public-before-privacy-check", temporary: false)) throw new InvalidOperationException("无法准备普通复制样例");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // 原捕获已确认普通格式，但还没有读取正文；此时完整替换成带私密标记的内容。
            if (!writer.SetText("private-after-privacy-check", temporary: true)) throw new InvalidOperationException("无法准备私密复制样例");
            proceed.Set();
            var deadline = Environment.TickCount64 + 5_000;
            while (monitor.CompletedCaptures == completed && Environment.TickCount64 < deadline) await Task.Delay(5);
            if (monitor.CompletedCaptures == completed) throw new TimeoutException("受控剪贴板捕获未完成");
            if (monitor.ContentReadAttempts != contentReads) throw new InvalidOperationException("隐私格式检查后剪贴板已变化，但仍尝试读取新私密正文");
            if (!captures.IsEmpty) throw new InvalidOperationException("隐私格式检查后剪贴板已变化，但仍发布了新私密内容");
            monitor.Enabled = false;
        }
        finally
        {
            proceed.Set();
            monitor.Enabled = false;
            if (writer.OwnsClipboard) writer.Restore(original, onlyIfOwned: true);
        }
    }
}
