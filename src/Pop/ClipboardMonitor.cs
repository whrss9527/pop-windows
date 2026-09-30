using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pop.Core;

namespace Pop;

/// 监听剪贴板变化，读出文字、图片或文件，交给剪贴板历史。
/// 标了「不要记录」的内容（密码管理器、Pop 自己的临时内容）和排除的 App 都跳过
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

    private readonly HwndSource window;
    private readonly DispatcherTimer debounce;

    /// 读到一条新内容
    public event Action<ClipboardCapture>? Captured;

    public bool Enabled { get; set; } = true;

    private DateTime ignoreUntil;

    /// 接下来一小段时间的变化不记：Pop 读取选中内容时模拟 Ctrl+C，目标 App 写进剪贴板的那一次不是用户复制的
    public void IgnoreChangesFor(TimeSpan duration) => ignoreUntil = DateTime.UtcNow + duration;

    public ClipboardMonitor()
    {
        window = new HwndSource(0, 0, 0, 0, 0, "Pop 剪贴板监听", new IntPtr(-3));
        window.AddHook(WndProc);
        // 有的 App 分几次写入不同格式，等一下再读
        debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            Capture();
        };
        if (!AddClipboardFormatListener(window.Handle))
            Log.Error($"监听剪贴板失败，错误码 {Marshal.GetLastWin32Error()}");
    }

    public void Dispose()
    {
        debounce.Stop();
        RemoveClipboardFormatListener(window.Handle);
        window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE && Enabled && DateTime.UtcNow >= ignoreUntil)
        {
            debounce.Stop();
            debounce.Start();
        }
        return IntPtr.Zero;
    }

    private void Capture()
    {
        try
        {
            if (IsPrivate()) return;
            var source = Native.ProcessNameOf(GetClipboardOwner());
            if (Read(source) is { } capture) Captured?.Invoke(capture);
        }
        catch (Exception e) when (e is COMException or ExternalException or IOException or NotSupportedException or InvalidOperationException)
        {
            Log.Info($"读取剪贴板失败，跳过这一次：{e.GetType().Name} {e.Message}");
        }
    }

    /// 标了不进剪贴板历史的内容
    private static bool IsPrivate()
    {
        if (IsClipboardFormatAvailable(ExcludeFromMonitor) || IsClipboardFormatAvailable(ViewerIgnore)) return true;
        if (!IsClipboardFormatAvailable(CanIncludeInHistory)) return false;
        return Retry(() => Clipboard.GetData("CanIncludeInClipboardHistory")) is MemoryStream stream
            && stream.Length >= 4 && new BinaryReader(stream).ReadInt32() == 0;
    }

    private static ClipboardCapture? Read(string? source)
    {
        if (Retry(Clipboard.ContainsFileDropList) && Retry(Clipboard.GetFileDropList) is { Count: > 0 } files)
            return ClipboardCapture.Files(files.Cast<string>().ToList(), source);

        if (Retry(Clipboard.ContainsText) && Retry(() => Clipboard.GetText(TextDataFormat.UnicodeText)) is { Length: > 0 } text)
        {
            if (text.Length > MaxTextLength || string.IsNullOrWhiteSpace(text)) return null;
            return new ClipboardCapture(ClipboardKind.Text, text, SourceApp: source);
        }

        if (Retry(Clipboard.ContainsImage) && Retry(Clipboard.GetImage) is { } image)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            if (stream.Length > MaxImageBytes) return null;
            return ClipboardCapture.Image(stream.ToArray(), source);
        }
        return null;
    }

    /// 别的程序正占着剪贴板时 WPF 会抛异常，重试几次
    private static T Retry<T>(Func<T> read)
    {
        for (var i = 0; ; i++)
        {
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
