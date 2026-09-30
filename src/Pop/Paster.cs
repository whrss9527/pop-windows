using System.Diagnostics;
using System.Windows.Threading;
using static Pop.Native;

namespace Pop;

/// 替换原文：把结果临时放进剪贴板、模拟 Ctrl+V，等目标 App 真的读走以后再把原来的剪贴板放回去。
/// 文字是「延迟提供」的：有程序来读时系统先通知剪贴板的主人，这样就知道粘贴已经发生了，反应慢的 App 也不会贴成原来的内容。
/// 剪贴板的主人是单独线程上的隐藏窗口：Pop 的界面线程忙的时候，目标 App 读剪贴板也不用等它
internal sealed class Paster : IDisposable
{
    public enum Outcome
    {
        /// 目标 App 读走了，原来的内容已经放回去
        Pasted,
        /// 等了 Deadline 还没有来读，原来的内容已经放回去
        NotRead,
        /// 这期间剪贴板里放进了别的内容（多半是用户又复制了东西），不再恢复
        Replaced,
        /// 打不开剪贴板
        Failed,
    }

    /// Milliseconds：目标 App 过了多久来读（没来读时是一共等了多久）；OtherReaders：来读过的其他程序
    public sealed record Result(Outcome Outcome, long Milliseconds, IReadOnlyList<string> OtherReaders);

    /// 最多等目标 App 这么久
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);
    /// 目标 App 读走以后再等一会儿才恢复：它可能还开着剪贴板
    private static readonly TimeSpan AfterRead = TimeSpan.FromMilliseconds(150);

    private const int WM_RENDERFORMAT = 0x0305;
    private const int WM_DESTROYCLIPBOARD = 0x0307;

    private readonly Dispatcher dispatcher;
    private ClipboardAccess clipboard = null!;
    private Pending? pending;
    /// 剪贴板里还欠着的文字（「延迟提供」了但还没人来读）
    private string? promised;

    private sealed class Pending(string text, ClipboardAccess.Snapshot? snapshot, uint target)
    {
        public string Text { get; } = text;
        public ClipboardAccess.Snapshot? Snapshot { get; } = snapshot;
        /// 目标 App 的进程
        public uint Target { get; } = target;
        public Stopwatch Watch { get; } = Stopwatch.StartNew();
        public TaskCompletionSource<Result> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public DispatcherTimer? Timer { get; set; }
        public long? ReadAt { get; set; }
        public List<string> OtherReaders { get; } = [];
    }

    public Paster()
    {
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            // 目标 App 读完可能还开着剪贴板一会儿，多试几次（在这个线程上等不影响界面）
            clipboard = new ClipboardAccess("Pop 替换原文", openAttempts: 40);
            clipboard.AddHook(WndProc);
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Pop 替换原文",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        dispatcher = created!;
    }

    public void Dispose()
    {
        dispatcher.Invoke(() =>
        {
            if (pending is { } p) Finish(p, Outcome.NotRead, restore: true);
            clipboard.Dispose();
        });
        dispatcher.InvokeShutdown();
    }

    /// 粘贴到当前的前台 App（替换它选中的文字）
    public Task<Result> PasteAsync(string text) => dispatcher.InvokeAsync(() => Begin(text)).Task.Unwrap();

    private Task<Result> Begin(string text)
    {
        // 上一次的还没恢复：原来的内容还是那一份，别把上一次的临时内容当成原来的
        var previous = pending;
        var snapshot = previous is null ? clipboard.Save() : previous.Snapshot;
        if (previous is not null) Finish(previous, previous.ReadAt is null ? Outcome.NotRead : Outcome.Pasted, restore: false);

        GetWindowThreadProcessId(GetForegroundWindow(), out var target);
        if (!clipboard.PromiseText()) return Task.FromResult(new Result(Outcome.Failed, 0, []));
        promised = text;
        var p = new Pending(text, snapshot, target);
        pending = p;
        p.Timer = new DispatcherTimer(DispatcherPriority.Normal, dispatcher) { Interval = Deadline };
        p.Timer.Tick += (_, _) => Finish(p, p.ReadAt is null ? Outcome.NotRead : Outcome.Pasted, restore: true);
        p.Timer.Start();
        InputInjector.CtrlChord(0x56); // V
        return p.Done.Task;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_RENDERFORMAT when (uint)wParam == CF_UNICODETEXT && promised is { } text:
                handled = true;
                ClipboardAccess.RenderText(text);
                if (pending is { } p) OnRead(p);
                break;
            case WM_DESTROYCLIPBOARD:
                promised = null;
                // 不是自己清空的：别的程序往剪贴板里放了新内容，不再恢复原来的，免得把它冲掉
                if (!clipboard.Emptying && pending is { } replaced) Finish(replaced, Outcome.Replaced, restore: false);
                break;
        }
        return IntPtr.Zero;
    }

    private static void OnRead(Pending p)
    {
        var reader = GetOpenClipboardWindow();
        GetWindowThreadProcessId(reader, out var process);
        // 剪贴板工具来读不算，要等目标 App；看不出是谁（没用窗口打开剪贴板）的算。
        // 粘贴时没有前台窗口的话，Pop 以外的程序来读就算
        var fromTarget = process == 0 || process == p.Target || (p.Target == 0 && process != Environment.ProcessId);
        if (!fromTarget)
        {
            p.OtherReaders.Add(ProcessNameOf(reader));
            return;
        }
        if (p.ReadAt is not null) return;
        p.ReadAt = p.Watch.ElapsedMilliseconds;
        p.Timer!.Stop();
        p.Timer.Interval = AfterRead;
        p.Timer.Start();
    }

    private void Finish(Pending p, Outcome outcome, bool restore)
    {
        if (!ReferenceEquals(pending, p)) return;
        pending = null;
        p.Timer?.Stop();
        if (restore)
        {
            if (p.Snapshot is { } snapshot) clipboard.Restore(snapshot);
            // 原来的内容没能备份下来：至少把文字真的放进去，不留一个没人兑现的「延迟提供」
            else clipboard.SetText(p.Text, temporary: true);
        }
        p.Done.TrySetResult(new Result(outcome, p.ReadAt ?? p.Watch.ElapsedMilliseconds, p.OtherReaders));
    }
}
