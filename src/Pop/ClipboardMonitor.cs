using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 监听剪贴板变化，读出文字、图片或文件，交给剪贴板历史。
/// 标了「不要记录」的内容（密码管理器、Pop 自己的临时内容）和排除的 App 都跳过。
/// 在单独的线程上监听和读取：界面线程忙的时候，接连复制的几段也不会只剩最后一段
internal sealed class ClipboardMonitor : IDisposable
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    /// 超过这个长度的文字不记
    private const int MaxTextLength = 2_000_000;
    /// 超过这个大小的图片不记（PNG 字节数）
    private const int MaxImageBytes = 30_000_000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardOwner();

    private static readonly uint ExcludeFromMonitor = Native.RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint ViewerIgnore = Native.RegisterClipboardFormat("Clipboard Viewer Ignore");
    private static readonly uint CanIncludeInHistory = Native.RegisterClipboardFormat("CanIncludeInClipboardHistory");

    private readonly Dispatcher dispatcher;
    private HwndSource window = null!;
    private DispatcherTimer debounce = null!;

    /// 读到一条新内容。在监听线程上调用；延迟消费前须再次核对本次记录状态。
    public event Action<ClipboardCapture, Func<bool>>? Captured;

    private sealed record RecordingState(ClipboardRecordingPolicy Policy)
    {
        public bool Enabled => Policy.Enabled;
    }
    private RecordingState recording;
    private RecordingState? queuedRecording;
    private long queuedNotifications, readAttempts, completedCaptures, contentReadAttempts;
    private readonly Action? beforeContentForDemo;

    internal long QueuedNotifications => Interlocked.Read(ref queuedNotifications);
    internal long ReadAttempts => Interlocked.Read(ref readAttempts);
    internal long CompletedCaptures => Interlocked.Read(ref completedCaptures);
    internal long ContentReadAttempts => Interlocked.Read(ref contentReadAttempts);

    public bool Enabled
    {
        get => Volatile.Read(ref recording).Enabled;
        set
        {
            // 每次开关产生新的身份；快速关开也不能继续旧的排队任务。
            while (true)
            {
                var previous = Volatile.Read(ref recording);
                if (previous.Enabled == value) return;
                if (ReferenceEquals(Interlocked.CompareExchange(ref recording, new RecordingState(previous.Policy.WithEnabled(value)), previous), previous)) return;
            }
        }
    }

    public void Configure(bool enabled, IEnumerable<string> excludedApplications)
    {
        var policy = new ClipboardRecordingPolicy(enabled, excludedApplications);
        while (true)
        {
            var previous = Volatile.Read(ref recording);
            if (previous.Policy.EquivalentTo(policy)) return;
            if (ReferenceEquals(Interlocked.CompareExchange(ref recording, new RecordingState(policy), previous), previous)) return;
        }
    }

    private sealed record IgnoreScope(long Until, Func<bool>? Current);
    private IgnoreScope? ignore;

    private bool IsIgnored()
    {
        var scope = Volatile.Read(ref ignore);
        return scope is not null && DateTime.UtcNow.Ticks < scope.Until && (scope.Current?.Invoke() ?? true);
    }

    /// 接下来一小段时间的变化不记：Pop 读取选中内容时模拟 Ctrl+C，目标 App 写进剪贴板的那一次不是用户复制的
    public void IgnoreChangesFor(TimeSpan duration, Func<bool>? current = null) =>
        Volatile.Write(ref ignore, new IgnoreScope((DateTime.UtcNow + duration).Ticks, current));

    public ClipboardMonitor(bool enabled = true, IEnumerable<string>? excludedApplications = null, Action? beforeContentForDemo = null)
    {
        if (beforeContentForDemo is not null && Application.Current is not App { IsDemo: true })
            throw new InvalidOperationException("捕获边界检查仅用于独立演示回归");
        this.beforeContentForDemo = beforeContentForDemo;
        recording = new RecordingState(new ClipboardRecordingPolicy(enabled, excludedApplications));
        Dispatcher? created = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            created = Dispatcher.CurrentDispatcher;
            window = new HwndSource(0, 0, 0, 0, 0, "Pop 剪贴板监听", new IntPtr(-3));
            window.AddHook(WndProc);
            // 有的 App 分几次写入不同格式，等一下再读
            debounce = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(150) };
            debounce.Tick += (_, _) =>
            {
                debounce.Stop();
                var pending = queuedRecording; queuedRecording = null;
                if (pending is not null) Capture(pending);
            };
            if (!AddClipboardFormatListener(window.Handle))
                Log.Error($"监听剪贴板失败，错误码 {Marshal.GetLastWin32Error()}");
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Pop 剪贴板监听",
        };
        // 读剪贴板（WPF 的 Clipboard 走 OLE）要在 STA 线程上
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait();
        dispatcher = created!;
    }

    public void Dispose()
    {
        Enabled = false;
        dispatcher.Invoke(() =>
        {
            debounce.Stop();
            RemoveClipboardFormatListener(window.Handle);
            window.Dispose();
        });
        dispatcher.InvokeShutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        var state = Volatile.Read(ref recording);
        if (msg == WM_CLIPBOARDUPDATE && state.Enabled && !IsIgnored())
        {
            queuedRecording = state;
            debounce.Stop();
            debounce.Start();
            Interlocked.Increment(ref queuedNotifications);
        }
        return IntPtr.Zero;
    }

    private void Capture(RecordingState state)
    {
        var sequence = ClipboardAccess.SequenceNumber;
        bool Recording() => state.Enabled && ReferenceEquals(state, Volatile.Read(ref recording));
        bool Active() => Recording() && !IsIgnored() && ClipboardAccess.SequenceNumber == sequence;
        try
        {
            if (sequence == 0 || !Active()) return;
            Interlocked.Increment(ref readAttempts);
            if (IsPrivate(Active) || !Active()) return;
            beforeContentForDemo?.Invoke();
            var source = Native.ProcessNameOf(GetClipboardOwner());
            if (!Active() || !state.Policy.Allows(source)) return;
            // 这里仍检查捕获的同一序列；提交到界面线程后只检查记录策略，保留已经完成的正常快照。
            if (Read(source, Active) is { } capture && Active()) Captured?.Invoke(capture, Recording);
        }
        catch (Exception e) when (e is COMException or ExternalException or IOException or NotSupportedException or InvalidOperationException)
        {
            Log.Info($"读取剪贴板失败，跳过这一次：{e.GetType().Name} {e.Message}");
        }
        finally
        {
            Interlocked.Increment(ref completedCaptures);
            // 延迟提供也会改变序列。放弃旧快照后重新走全部隐私/来源检查，不依赖额外通知一定到达。
            if (Recording() && !IsIgnored() && ClipboardAccess.SequenceNumber != sequence)
            {
                queuedRecording = state;
                debounce.Stop(); debounce.Start();
            }
        }
    }

    /// 标了不进剪贴板历史的内容
    private static bool IsPrivate(Func<bool> active)
    {
        if (IsClipboardFormatAvailable(ExcludeFromMonitor) || IsClipboardFormatAvailable(ViewerIgnore)) return true;
        if (!IsClipboardFormatAvailable(CanIncludeInHistory)) return false;
        return Retry(() => Clipboard.GetData("CanIncludeInClipboardHistory"), active) is MemoryStream stream
            && stream.Length >= 4 && new BinaryReader(stream).ReadInt32() == 0;
    }

    private ClipboardCapture? Read(string? source, Func<bool> active)
    {
        if (!active()) return null;
        Interlocked.Increment(ref contentReadAttempts);
        if (Retry(Clipboard.ContainsFileDropList, active) && Retry(Clipboard.GetFileDropList, active) is { Count: > 0 } files)
            return ClipboardCapture.Files(files.Cast<string>().ToList(), source);

        if (Retry(Clipboard.ContainsText, active) && Retry(() => Clipboard.GetText(TextDataFormat.UnicodeText), active) is { Length: > 0 } text)
        {
            if (text.Length > MaxTextLength || string.IsNullOrWhiteSpace(text)) return null;
            return new ClipboardCapture(ClipboardKind.Text, text, SourceApp: source);
        }

        if (Retry(Clipboard.ContainsImage, active) && Retry(Clipboard.GetImage, active) is { } image)
        {
            var encoder = new PngBitmapEncoder();
            if (!active()) return null;
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            if (stream.Length > MaxImageBytes) return null;
            return ClipboardCapture.Image(stream.ToArray(), source);
        }
        return null;
    }

    /// 别的程序正占着剪贴板时 WPF 会抛异常，重试几次
    private static T Retry<T>(Func<T> read, Func<bool> active)
    {
        for (var i = 0; ; i++)
        {
            if (!active()) return default!;
            try
            {
                return read();
            }
            catch (COMException) when (i < 4)
            {
                Thread.Sleep(30);
            }
        }
    }
}
