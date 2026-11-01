using System.Collections.Concurrent;
using System.Diagnostics;
using Pop.Core;

namespace Pop;

/// 真实 STA 监听检查，只在独立演示回归中执行，不启动输入钩子。
internal static class ClipboardRecordingChecks
{
    public static async Task VerifyAsync()
    {
        using var writer = new ClipboardAccess("Pop 暂停记录检查");
        var original = writer.Save() ?? throw new InvalidOperationException("无法保留检查前的剪贴板");
        try
        {
            using var monitor = new ClipboardMonitor(enabled: false);
            var captures = new ConcurrentQueue<ClipboardCapture>();
            Func<bool>? firstLease = null;
            monitor.Captured += (capture, current) =>
            {
                Interlocked.CompareExchange(ref firstLease, current, null);
                if (current()) captures.Enqueue(capture);
            };
            void Copy(string text)
            {
                if (!writer.SetText(text, temporary: false)) throw new InvalidOperationException("无法准备监听检查内容");
            }
            Copy("recording-disabled"); await Task.Delay(250);
            if (monitor.ReadAttempts != 0 || !captures.IsEmpty) throw new InvalidOperationException("创建时暂停记录仍然读取了内容");

            monitor.Enabled = true;
            Copy("recording-first"); await Until(() => captures.Count == 1);
            if (captures.Single().Text != "recording-first") throw new InvalidOperationException("启用后没有捕获新内容");

            var reads = monitor.ReadAttempts;
            var notifications = monitor.QueuedNotifications;
            Copy("recording-cancelled"); await Until(() => monitor.QueuedNotifications > notifications);
            monitor.Enabled = false; await Task.Delay(250);
            if (monitor.ReadAttempts != reads || captures.Count != 1) throw new InvalidOperationException("暂停后仍处理了旧的排队读取");

            monitor.Enabled = true;
            notifications = monitor.QueuedNotifications;
            Copy("recording-old-generation"); await Until(() => monitor.QueuedNotifications > notifications);
            monitor.Enabled = false; monitor.Enabled = true; await Task.Delay(250);
            if (monitor.ReadAttempts != reads || captures.Count != 1) throw new InvalidOperationException("快速关开后仍处理了旧通知");
            if (firstLease?.Invoke() != false) throw new InvalidOperationException("快速关开后旧结果仍可在界面线程上延迟保存");

            Copy("recording-second"); await Until(() => captures.Count == 2);
            Copy("recording-third"); await Until(() => captures.Count == 3);
            if (!captures.Select(c => c.Text).SequenceEqual(["recording-first", "recording-second", "recording-third"]))
                throw new InvalidOperationException("重新启用后的连续复制内容不完整或混入旧通知");
            monitor.Enabled = false;
        }
        finally { if (writer.OwnsClipboard) writer.Restore(original, onlyIfOwned: true); }
    }

    private static async Task Until(Func<bool> condition)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(3)) await Task.Delay(5);
        if (!condition()) throw new InvalidOperationException("监听检查等待新通知或捕获结果超时");
    }
}
