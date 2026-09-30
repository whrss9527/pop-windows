using System.Runtime.InteropServices;
using System.Windows.Interop;
using static Pop.Native;

namespace Pop;

/// 直接用 Win32 接口读写剪贴板，在界面线程上调用。
/// 读取选中内容、替换原文时要先备份剪贴板，用完再原样放回去。
internal sealed class ClipboardAccess : IDisposable
{
    // 写进剪贴板的临时内容不进「剪贴板历史」、不同步到其他设备，也让剪贴板工具忽略
    private static readonly uint ExcludeFromMonitor = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint CanIncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint CanUploadToCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

    // 这几种不是内存块，没法原样复制；系统会从 CF_DIB 等格式自动合成
    private static readonly HashSet<uint> Unsupported = [2 /* CF_BITMAP */, 3 /* CF_METAFILEPICT */, 9 /* CF_PALETTE */, 14 /* CF_ENHMETAFILE */, 0x0082 /* CF_DSPBITMAP */, 0x008E /* CF_DSPENHMETAFILE */];

    private readonly HwndSource window;
    private readonly int openAttempts;

    /// 在哪个线程上创建，就在哪个线程上用。openAttempts：剪贴板被别的程序占着时试几次（每次隔 15 毫秒）
    public ClipboardAccess(string name = "Pop 剪贴板", int openAttempts = 10)
    {
        // 只收消息的隐藏窗口：清空剪贴板后写入需要一个所有者窗口
        window = new HwndSource(0, 0, 0, 0, 0, name, new IntPtr(-3));
        this.openAttempts = openAttempts;
    }

    public void Dispose() => window.Dispose();

    /// 剪贴板的主人窗口会收到 WM_RENDERFORMAT、WM_DESTROYCLIPBOARD 这些消息
    public void AddHook(HwndSourceHook hook) => window.AddHook(hook);

    /// 正在清空剪贴板：原来的主人（可能就是自己）这时收到的 WM_DESTROYCLIPBOARD 不是别人复制了东西
    public bool Emptying { get; private set; }

    public static uint SequenceNumber => GetClipboardSequenceNumber();

    public sealed record Snapshot(IReadOnlyList<(uint Format, byte[] Data)> Items);

    public Snapshot? Save()
    {
        if (!Open()) return null;
        try
        {
            var items = new List<(uint, byte[])>();
            uint format = 0;
            while ((format = EnumClipboardFormats(format)) != 0)
            {
                if (Unsupported.Contains(format)) continue;
                var handle = GetClipboardData(format);
                if (handle == IntPtr.Zero) continue;
                var size = (int)GlobalSize(handle).ToUInt64();
                if (size <= 0) continue;
                var ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) continue;
                try
                {
                    var data = new byte[size];
                    Marshal.Copy(ptr, data, 0, size);
                    items.Add((format, data));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            return new Snapshot(items);
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void Restore(Snapshot? snapshot)
    {
        if (snapshot is null || !Open()) return;
        try
        {
            Empty();
            foreach (var (format, data) in snapshot.Items) Put(format, data);
            MarkPrivate();
        }
        finally
        {
            CloseClipboard();
        }
    }

    public string? GetText()
    {
        if (!Open()) return null;
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == IntPtr.Zero) return null;
            var ptr = GlobalLock(handle);
            if (ptr == IntPtr.Zero) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// 写入文字。temporary 为 true 时不进剪贴板历史（替换原文时临时用一下）
    public bool SetText(string text, bool temporary)
    {
        if (!Open()) return false;
        try
        {
            Empty();
            var ok = Put(CF_UNICODETEXT, TextBytes(text));
            if (temporary) MarkPrivate();
            return ok;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// 先说剪贴板里有文字，等有程序来读的时候再给（延迟提供）：来读时主人窗口收到 WM_RENDERFORMAT，这时调用 RenderText。
    /// 不进剪贴板历史
    public bool PromiseText()
    {
        if (!Open()) return false;
        try
        {
            Empty();
            SetClipboardData(CF_UNICODETEXT, IntPtr.Zero);
            MarkPrivate();
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// 回应 WM_RENDERFORMAT：来读的程序已经打开了剪贴板，直接放进去
    public static bool RenderText(string text) => Put(CF_UNICODETEXT, TextBytes(text));

    private static byte[] TextBytes(string text)
    {
        var bytes = new byte[(text.Length + 1) * 2];
        System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
        return bytes;
    }

    private void Empty()
    {
        Emptying = true;
        try
        {
            EmptyClipboard();
        }
        finally
        {
            Emptying = false;
        }
    }

    private void MarkPrivate()
    {
        Put(ExcludeFromMonitor, [0, 0, 0, 0]);
        Put(CanIncludeInHistory, [0, 0, 0, 0]);
        Put(CanUploadToCloud, [0, 0, 0, 0]);
    }

    private static bool Put(uint format, byte[] data)
    {
        var mem = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)data.Length);
        if (mem == IntPtr.Zero) return false;
        var ptr = GlobalLock(mem);
        if (ptr == IntPtr.Zero)
        {
            GlobalFree(mem);
            return false;
        }
        Marshal.Copy(data, 0, ptr, data.Length);
        GlobalUnlock(mem);
        if (SetClipboardData(format, mem) != IntPtr.Zero) return true;
        GlobalFree(mem);
        return false;
    }

    /// 别的程序可能正占着剪贴板，重试几次
    private bool Open()
    {
        for (var i = 0; i < openAttempts; i++)
        {
            if (OpenClipboard(window.Handle)) return true;
            Thread.Sleep(15);
        }
        Log.Error("打不开剪贴板");
        return false;
    }
}
