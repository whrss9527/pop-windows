using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Pop.Core;

namespace Pop;

/// 原生通知区域图标：支持键盘激活、Explorer 重启恢复，并在退出时撤销注册。
internal sealed class NativeTrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x8051;
    private readonly HwndSource source;
    private readonly DispatcherTimer retry = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly uint taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private NotifyData data;
    private bool version4, disposed;
    public bool Registered { get; private set; }
    internal IntPtr Handle => source.Handle;
    internal uint TaskbarCreatedMessage => taskbarCreated;
    internal int Registrations { get; private set; }
    internal bool UsesVersion4 => version4;
    public event Action<int, int>? Activated;

    public NativeTrayIcon(Drawing.Icon icon, string tooltip)
    {
        source = new HwndSource(new HwndSourceParameters("Pop 通知区域") { WindowStyle = 0, ExtendedWindowStyle = 0x80, Width = 0, Height = 0 });
        source.AddHook(WindowMessage);
        data = new NotifyData
        {
            Size = (uint)Marshal.SizeOf<NotifyData>(), Window = source.Handle, Id = 1,
            Flags = 0x1 | 0x2 | 0x4 | 0x80, Callback = CallbackMessage, Icon = icon.Handle,
            Tip = Clip(tooltip, 127), Info = "", InfoTitle = "", Version = 4,
        };
        retry.Tick += (_, _) => Add();
        Add();
    }

    public void Update(Drawing.Icon icon, string tooltip)
    {
        if (disposed) return;
        data.Icon = icon.Handle; data.Tip = Clip(tooltip, 127);
        if (!Registered || !ShellNotify(1, ref data)) { Registered = false; Add(); }
    }

    public void Notify(string title, string text, bool warning)
    {
        if (disposed) return;
        if (!Registered) Add();
        var balloon = data;
        balloon.Flags = 0x10; balloon.InfoTitle = Clip(title, 63); balloon.Info = Clip(text, 255);
        balloon.InfoFlags = warning ? 2u : 0u;
        ShellNotify(1, ref balloon);
    }

    private void Add()
    {
        if (disposed) return;
        Registered = ShellNotify(0, ref data);
        if (Registered) { Registrations++; version4 = ShellNotify(4, ref data); retry.Stop(); }
        else retry.Start();
    }

    private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (taskbarCreated != 0 && (uint)message == taskbarCreated)
        {
            // 删除残留注册也让重复的恢复通知保持幂等。
            ShellNotify(2, ref data); Registered = false; Add();
        }
        if (message != CallbackMessage || disposed) return IntPtr.Zero;
        var notification = version4 ? (int)(lParam.ToInt64() & 0xffff) : lParam.ToInt32();
        if (version4 ? notification is 0x400 or 0x401 or 0x7b : notification is 0x202 or 0x205)
        {
            Native.GetCursorPos(out var cursor);
            var x = version4 ? (short)(wParam.ToInt64() & 0xffff) : cursor.X;
            var y = version4 ? (short)((wParam.ToInt64() >> 16) & 0xffff) : cursor.Y;
            if (x == -1 && y == -1) { x = cursor.X; y = cursor.Y; }
            source.Dispatcher.BeginInvoke(() => { if (!disposed) Activated?.Invoke(x, y); });
        }
        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; retry.Stop(); ShellNotify(2, ref data); Registered = false;
        source.RemoveHook(WindowMessage); source.Dispose();
    }

    private static string Clip(string value, int limit) => value.Length <= limit ? value : TextPreview.Truncate(value, limit - 1);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotify(uint operation, ref NotifyData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }
}
