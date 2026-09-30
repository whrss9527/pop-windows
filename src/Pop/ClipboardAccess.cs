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

    public ClipboardAccess()
    {
        // 只收消息的隐藏窗口：清空剪贴板后写入需要一个所有者窗口
        window = new HwndSource(0, 0, 0, 0, 0, "Pop 剪贴板", new IntPtr(-3));
    }

    public void Dispose() => window.Dispose();

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
            EmptyClipboard();
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
            EmptyClipboard();
            var bytes = new byte[(text.Length + 1) * 2];
            System.Text.Encoding.Unicode.GetBytes(text, 0, text.Length, bytes, 0);
            var ok = Put(CF_UNICODETEXT, bytes);
            if (temporary) MarkPrivate();
            return ok;
        }
        finally
        {
            CloseClipboard();
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
        for (var i = 0; i < 10; i++)
        {
            if (OpenClipboard(window.Handle)) return true;
            Thread.Sleep(15);
        }
        Log.Error("打不开剪贴板");
        return false;
    }
}
