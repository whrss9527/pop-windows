using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pop;

/// 全局快捷键（RegisterHotKey）。快捷键被别的程序占用时注册失败，记进日志，不影响其他功能
internal sealed class HotKeys : IDisposable
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource window;
    private readonly Dictionary<int, Action> handlers = [];
    private int nextId = 1;

    public HotKeys()
    {
        window = new HwndSource(0, 0, 0, 0, 0, "Pop 快捷键", new IntPtr(-3));
        window.AddHook(WndProc);
    }

    /// 注册成功返回 true
    public bool Register(uint modifiers, uint vk, string name, Action handler)
    {
        var id = nextId++;
        if (!RegisterHotKey(window.Handle, id, modifiers | MOD_NOREPEAT, vk))
        {
            Log.Info($"快捷键 {name} 注册失败（可能被别的程序占用了），错误码 {Marshal.GetLastWin32Error()}");
            return false;
        }
        handlers[id] = handler;
        Log.Info($"快捷键 {name} 已注册");
        return true;
    }

    public void Dispose()
    {
        foreach (var id in handlers.Keys) UnregisterHotKey(window.Handle, id);
        window.Dispose();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && handlers.TryGetValue((int)wParam, out var handler))
        {
            handled = true;
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Log.Error("处理快捷键出错", e);
            }
        }
        return IntPtr.Zero;
    }
}
