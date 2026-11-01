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
        try
        {
            await VerifyDelayedAsync(writer);
            await VerifyReplacementAsync(writer);
            await VerifyExclusionsAsync(writer);
        }
        finally { if (writer.OwnsClipboard) writer.Restore(original, onlyIfOwned: true); }
    }

    private static async Task VerifyReplacementAsync(ClipboardAccess writer)
    {
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
        }
    }

    private static async Task VerifyDelayedAsync(ClipboardAccess writer)
    {
        const string text = "public-delayed-history";
        var renders = 0;
        System.Windows.Interop.HwndSourceHook render = (IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message == 0x0305 && wParam.ToInt64() == 13)
            {
                Interlocked.Increment(ref renders);
                if (!ClipboardAccess.RenderText(text)) throw new InvalidOperationException("无法提供延迟文字样例");
                handled = true;
            }
            return IntPtr.Zero;
        };
        writer.AddHook(render);
        try
        {
            using var monitor = new ClipboardMonitor();
            var captures = new ConcurrentQueue<ClipboardCapture>();
            monitor.Captured += (capture, current) => { if (current()) captures.Enqueue(capture); };
            if (!writer.PromisePublicTextForDemo()) throw new InvalidOperationException("无法准备公开延迟文字");
            var before = ClipboardAccess.SequenceNumber;
            var deadline = Environment.TickCount64 + 5_000;
            while (!captures.Any(c => c.Text == text) && Environment.TickCount64 < deadline) await Task.Delay(5);
            if (renders == 0 || !captures.Any(c => c.Text == text)) throw new InvalidOperationException("正常延迟提供的文字没有进入捕获队列");
            monitor.Enabled = false;
            SmokeTest.Report($"clipboard-policy-stage=delayed renders={renders} captured={captures.Count} sequence-before={before} sequence-after={ClipboardAccess.SequenceNumber}");
        }
        finally { writer.RemoveHook(render); }
    }

    private static async Task VerifyExclusionsAsync(ClipboardAccess writer)
    {
        var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        using var monitor = new ClipboardMonitor(excludedApplications: [name.ToUpperInvariant() + ".EXE"]);
        var captures = new ConcurrentQueue<ClipboardCapture>();
        Func<bool>? firstLease = null;
        monitor.Captured += (capture, current) =>
        {
            Interlocked.CompareExchange(ref firstLease, current, null);
            if (current()) captures.Enqueue(capture);
        };
        void Copy(string text)
        {
            if (!writer.SetText(text, temporary: false)) throw new InvalidOperationException("无法准备来源排除样例");
        }
        var completed = monitor.CompletedCaptures;
        Copy("excluded-before-content-read"); await Until(() => monitor.CompletedCaptures > completed);
        if (monitor.ContentReadAttempts != 0 || !captures.IsEmpty) throw new InvalidOperationException("排除应用的正文仍被读取");

        monitor.Configure(true, ["example-not-running-app"]);
        Copy("allowed-before-policy-change"); await Until(() => captures.Count == 1);
        monitor.Configure(true, ["EXAMPLE-NOT-RUNNING-APP.exe", "example-not-running-app"]);
        if (firstLease?.Invoke() != true) throw new InvalidOperationException("等效来源规则使正常捕获失效");

        var reads = monitor.ContentReadAttempts;
        var notifications = monitor.QueuedNotifications;
        completed = monitor.CompletedCaptures;
        Copy("queued-before-exclusion"); await Until(() => monitor.QueuedNotifications > notifications);
        monitor.Configure(true, [name + ".exe"]);
        await Until(() => monitor.CompletedCaptures > completed);
        if (monitor.ContentReadAttempts != reads || captures.Count != 1 || firstLease?.Invoke() != false)
            throw new InvalidOperationException("排除规则变化没有取消排队读取和旧的延迟消费");
        monitor.Configure(true, []);
        Copy("allowed-after-exclusion"); await Until(() => captures.Count == 2);
        if (captures.Last().Text != "allowed-after-exclusion") throw new InvalidOperationException("放开来源后没有恢复新的完整捕获");
        monitor.Enabled = false;
    }

    private static async Task Until(Func<bool> ready)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (!ready() && Environment.TickCount64 < deadline) await Task.Delay(5);
        if (!ready()) throw new TimeoutException("剪贴板来源或记录策略检查未完成");
    }
}
